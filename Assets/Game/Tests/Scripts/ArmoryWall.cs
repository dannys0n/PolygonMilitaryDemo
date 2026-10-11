using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Builds the equipment sandbox from the same catalog used by class selection.
// Display geometry is static: no weapon scripts, scope cameras, or animators survive.
public class ArmoryWall : MonoBehaviour
{
    public ArenaWeaponCatalog catalog;
    public ArmoryTarget displayPrefab;
    public Transform weaponWall, attachmentWall;
    [Min(1)] public int columns = 13;
    public Vector2 spacing = new Vector2(2.35f, 1.1f);
    public float bottomHeight = 1.05f;
    public Vector2 modelSize = new Vector2(1.85f, .55f);
    readonly List<Mesh> generatedMeshes = new();

    public ArmoryTarget[] Build()
    {
        var targets = new List<ArmoryTarget>();
        var staging = new GameObject("Display preparation");
        staging.SetActive(false);
        try
        {
            int weaponIndex = 0, attachmentIndex = 0;
            foreach (var entry in catalog.entries.OrderBy(e => e.weapon.displayName))
            {
                var target = Add(entry.weapon.displayName, weaponWall, weaponIndex++);
                target.weapon = entry.weapon;
                AddModel(entry.basePrefab, target, staging.transform);
                targets.Add(target);
            }
            var reset = Add("Default sights", attachmentWall, attachmentIndex++);
            reset.resetSight = true;
            targets.Add(reset);
            foreach (var sight in catalog.entries.SelectMany(e => e.sights).Distinct().OrderBy(s => s.displayName))
            {
                // The default-sights display covers the bare iron configuration.
                if (!sight.prefab) continue;
                var target = Add(sight.displayName, attachmentWall, attachmentIndex++);
                target.sight = sight;
                AddModel(sight.prefab.gameObject, target, staging.transform);
                targets.Add(target);
            }
            foreach (var attachment in catalog.entries.SelectMany(e => e.attachments).Distinct()
                         .OrderBy(a => a.mount).ThenBy(a => a.displayName))
            {
                var target = Add(attachment.displayName, attachmentWall, attachmentIndex++);
                target.attachment = attachment;
                AddModel(attachment.prefab, target, staging.transform);
                targets.Add(target);
            }
        }
        finally { Destroy(staging); }
        return targets.ToArray();
    }

    ArmoryTarget Add(string title, Transform wall, int index)
    {
        var target = Instantiate(displayPrefab, wall, false);
        target.name = title;
        target.title = title;
        target.transform.localPosition = new Vector3(
            (index % columns - (columns - 1) * .5f) * spacing.x,
            bottomHeight + index / columns * spacing.y, -.18f);
        target.label.text = title;
        return target;
    }

    void AddModel(GameObject prefab, ArmoryTarget target, Transform staging)
    {
        var source = Instantiate(prefab, staging, false);
        var snapshots = new List<Mesh>();
        try
        {
            var visual = new GameObject("Equipment display").transform;
            visual.SetParent(target.transform, false);
            var groups = new Dictionary<Material, List<CombineInstance>>();
            var nodes = new Dictionary<Transform, Transform> { [source.transform] = visual };
            var bounds = new Bounds();
            bool hasBounds = false;
            foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeSelf) continue;
                Mesh mesh;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    mesh = new Mesh();
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
                var mesh = new Mesh { name = target.title + " display", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
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
        finally
        {
            foreach (var mesh in snapshots) Destroy(mesh);
            Destroy(source);
        }
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
        foreach (var mesh in generatedMeshes) Destroy(mesh);
    }
}
