using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Names and shootable plates are ready immediately; weapon displays follow across frames.
// Display geometry is static: no weapon scripts, scope cameras, or animators survive.
public class ArmoryWall : MonoBehaviour
{
    public string sourceCatalogPath;
    public bool polishedOnly;
    public ArmoryAssetIndex Index { get; private set; }
    public void ReadAssets() => Index = ArmoryAssetIndex.Read(sourceCatalogPath, polishedOnly);
    public ArmoryTarget displayPrefab;
    public Transform weaponWall, attachmentWall;
    [Min(1)] public int columns = 13;
    public Vector2 spacing = new Vector2(2.35f, 1.1f);
    public float bottomHeight = 1.05f;
    public Vector2 modelSize = new Vector2(1.85f, .55f);
    readonly List<Mesh> generatedMeshes = new();
    readonly Dictionary<Transform, (int columns, float scale)> layouts = new();
    Coroutine weaponDisplayRoutine;
    GameObject weaponDisplayStaging;

    public ArmoryTarget[] Build()
    {
        if (Index == null) ReadAssets();
        var sights = new List<WeaponSightDefinition>();
        var attachments = new List<WeaponAttachmentDefinition>();
#if UNITY_EDITOR
        sights.AddRange(Index.sightPaths.Select(UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponSightDefinition>)
            .Where(s => s && s.prefab).OrderBy(s => s.displayName));
        attachments.AddRange(Index.attachmentPaths.Select(UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>)
            .Where(a => a && a.prefab).OrderBy(a => a.mount).ThenBy(a => a.displayName));
#endif
        SetLayout(weaponWall, Index.weapons.Length);
        SetLayout(attachmentWall, 1 + sights.Count + attachments.Count);
        var targets = new List<ArmoryTarget>();
        int w = 0, a = 0;
        foreach (var weapon in Index.weapons)
        {
            var target = Add(weapon.title, weaponWall, w++);
            target.weaponId = weapon.id;
            // Keep the plate usable while its optional display model is being prepared.
            target.label.rectTransform.anchoredPosition = Vector2.zero;
            targets.Add(target);
        }
        var reset = Add("Default sights", attachmentWall, a++);
        reset.resetSight = true; targets.Add(reset);
        var staging = new GameObject("Attachment display preparation"); staging.SetActive(false);
        try
        {
            foreach (var sight in sights)
            {
                var target = Add(sight.displayName, attachmentWall, a++); target.sight = sight;
                AddDisplayModel(sight.prefab.gameObject, target.transform, staging.transform); targets.Add(target);
            }
            foreach (var attachment in attachments)
            {
                var target = Add(attachment.displayName, attachmentWall, a++); target.attachment = attachment;
                AddDisplayModel(attachment.prefab, target.transform, staging.transform); targets.Add(target);
            }
        }
        finally { Release(staging); }
        var result = targets.ToArray();
        if (Application.isPlaying) weaponDisplayRoutine = StartCoroutine(PopulateWeaponDisplays(result));
        return result;
    }

    IEnumerator PopulateWeaponDisplays(ArmoryTarget[] targets)
    {
        // Return control to gameplay before loading any weapon prefab dependencies.
        yield return null;
        weaponDisplayStaging = new GameObject("Weapon display preparation");
        weaponDisplayStaging.SetActive(false);
        weaponDisplayStaging.transform.SetParent(transform, false);
        try
        {
            for (int i = 0; i < Index.weapons.Length; i++)
            {
#if UNITY_EDITOR
                try
                {
                    // Resolve the TPS prefab directly without loading a complete equipment slot.
                    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Index.weapons[i].thirdPersonPath);
                    if (prefab && targets[i])
                    {
                        AddDisplayModel(prefab, targets[i].transform, weaponDisplayStaging.transform);
                        targets[i].label.rectTransform.anchoredPosition = displayPrefab.label.rectTransform.anchoredPosition;
                    }
                }
                catch (System.Exception error)
                {
                    Debug.LogException(error, this);
                }
#endif
                // A single coroutine, one model per frame. AssetDatabase loading itself is synchronous.
                yield return null;
            }
        }
        finally
        {
            Release(weaponDisplayStaging);
            weaponDisplayStaging = null;
            weaponDisplayRoutine = null;
        }
    }

    void SetLayout(Transform wall, int count)
    {
        int cols = Mathf.Max(columns, Mathf.CeilToInt(Mathf.Sqrt(count * columns / 3f)));
        int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)cols));
        layouts[wall] = (cols, Mathf.Min(1f, columns / (float)cols, 3f / rows));
    }

    ArmoryTarget Add(string title, Transform wall, int index)
    {
        var layout = layouts[wall];
        var target = Instantiate(displayPrefab, wall, false);
        target.transform.localScale = Vector3.one * layout.scale;
        target.name = title;
        target.title = title;
        target.transform.localPosition = new Vector3(
            (index % layout.columns - (layout.columns - 1) * .5f) * spacing.x * layout.scale,
            bottomHeight + index / layout.columns * spacing.y * layout.scale, -.18f);
        target.label.text = title;
        return target;
    }

    GameObject AddDisplayModel(GameObject prefab, Transform parent, Transform staging)
    {
        var source = Instantiate(prefab, staging, false);
        var snapshots = new List<Mesh>();
        var visual = new GameObject("Equipment display").transform;
        visual.SetParent(parent,false);
        try
        {
            var groups = new Dictionary<Material, List<CombineInstance>>();
            var nodes = new Dictionary<Transform, Transform> { [source.transform] = visual };
            var bounds = new Bounds();
            bool hasBounds = false;
            foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !VisibleWithin(renderer.transform, source.transform)) continue;
                Mesh mesh;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    mesh = new Mesh();
                    // Preserve the renderer-local scale convention before applying its matrix.
                    skin.BakeMesh(mesh, true);
                    snapshots.Add(mesh);
                }
                else if (renderer.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
                else continue;
                if (!mesh) continue;
                var matrix = source.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                if (!mesh.isReadable)
                {
                    // Render imported GPU-only meshes directly; don't enable Read/Write
                    // on the entire asset pack just to build a sandbox display.
                    var node = CopyTransform(renderer.transform, nodes);
                    node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var display = node.gameObject.AddComponent<MeshRenderer>();
                    display.sharedMaterials = renderer.sharedMaterials;
                    display.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    display.receiveShadows = false;
                    Measure(mesh.bounds, Matrix4x4.Rotate(visual.localRotation) * matrix, ref bounds, ref hasBounds);
                    continue;
                }
                for (int i = 0; i < mesh.subMeshCount && i < renderer.sharedMaterials.Length; i++)
                {
                    var material = renderer.sharedMaterials[i];
                    if (!material) continue;
                    if (!groups.TryGetValue(material, out var instances)) groups[material] = instances = new();
                    instances.Add(new CombineInstance { mesh = mesh, subMeshIndex = i, transform = matrix });
                }
            }
            foreach (var group in groups)
            {
                var mesh = new Mesh { name = prefab.name + " display", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(group.Value.ToArray(), true, true);
                generatedMeshes.Add(mesh);
                var node = new GameObject(group.Key.name);
                node.transform.SetParent(visual, false);
                node.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = node.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = group.Key;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                // Measure the rotated geometry, rather than assuming source-model axes.
                Measure(mesh.bounds, Matrix4x4.Rotate(visual.localRotation), ref bounds, ref hasBounds);
            }
            if (hasBounds)
            {
                // Imported bases have different authoring axes. Use their geometry
                // to lay out a side profile without changing the weapon asset.
                var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
                int longest = bounds.size.x >= bounds.size.y && bounds.size.x >= bounds.size.z ? 0
                    : bounds.size.y >= bounds.size.z ? 1 : 2;
                int upright = Enumerable.Range(0, 3).Where(i => i != longest)
                    .OrderByDescending(i => bounds.size[i]).First();
                visual.localRotation = Quaternion.Inverse(Quaternion.LookRotation(
                    Vector3.Cross(axes[longest], axes[upright]), axes[upright]));
                var sourceBounds = bounds;
                hasBounds = false;
                Measure(sourceBounds, Matrix4x4.Rotate(visual.localRotation), ref bounds, ref hasBounds);
                float scale = Mathf.Min(modelSize.x / Mathf.Max(bounds.size.x, .001f),
                    modelSize.y / Mathf.Max(bounds.size.y, .001f));
                visual.localScale = Vector3.one * scale;
                visual.localPosition = new Vector3(0, .06f, -.08f - bounds.max.z * scale) -
                    new Vector3(bounds.center.x, bounds.center.y, 0) * scale;
            }
        }
        catch
        {
            Release(visual.gameObject);
            throw;
        }
        finally
        {
            foreach (var mesh in snapshots) Release(mesh);
            Release(source);
        }
        return visual.gameObject;
    }
    static void Release(Object value){if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}

    static bool VisibleWithin(Transform node, Transform root)
    {
        for (; node != root; node = node.parent)
            if (!node.gameObject.activeSelf) return false;
        return true;
    }

    static Transform CopyTransform(Transform source, Dictionary<Transform, Transform> nodes)
    {
        if (nodes.TryGetValue(source, out var existing)) return existing;
        var parent = CopyTransform(source.parent, nodes);
        var node = new GameObject(source.name).transform;
        node.SetParent(parent, false);
        node.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
        node.localScale = source.localScale;
        nodes[source] = node;
        return node;
    }

    static void Measure(Bounds meshBounds, Matrix4x4 matrix, ref Bounds bounds, ref bool hasBounds)
    {
        for (int corner = 0; corner < 8; corner++)
        {
            var p = meshBounds.center + Vector3.Scale(meshBounds.extents, new Vector3(
                (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
            p = matrix.MultiplyPoint3x4(p);
            if (!hasBounds) { bounds = new Bounds(p, Vector3.zero); hasBounds = true; }
            else bounds.Encapsulate(p);
        }
    }

    void OnDestroy()
    {
        if (weaponDisplayRoutine != null) StopCoroutine(weaponDisplayRoutine);
        if (weaponDisplayStaging) Release(weaponDisplayStaging);
        foreach (var mesh in generatedMeshes)if(mesh)Release(mesh);
    }
}

