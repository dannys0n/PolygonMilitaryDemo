using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Rendering baseline alongside EntityPerformanceControl. No Update loop or per-actor helper scripts.
public sealed class InfantryRenderingPerformanceControl : MonoBehaviour
{
    public enum Representation { StaticMeshes, FrozenSkinnedMeshes, AnimatedSkinnedMeshes, ScriptedInfantry }
    public EntityPerformanceControl scripted;
    public Representation representation;
    [Min(0)] public int initialPerTeam = 32;
    public bool showControls = true;
    readonly List<GameObject> red = new List<GameObject>(), blue = new List<GameObject>();
    readonly List<Mesh> bakedMeshes = new List<Mesh>();
    GameObject[] staticTemplates, riggedTemplates;
    int redCount, blueCount;
    string redText = "32", blueText = "32", status = "Preparing identical visual templates...";
    bool ready;
    static readonly string[] Labels = { "Static", "Skinned / frozen", "Skinned / idle", "Full actor" };
    static readonly string[] Activities = { "Idle", "AI + FX", "Combat" };
    public bool IsReady => ready;
    public int RedCount => redCount;
    public int BlueCount => blueCount;
    public int VisualCount => red.Count + blue.Count;

    IEnumerator Start()
    {
        scripted.showControls = false;
        scripted.enabled = false; // Its match-rule methods remain active; only the duplicate debug panel is suppressed.
        scripted.SetActivity(EntityPerformanceControl.Activity.Idle);
        while (!scripted.match.IsRunning) yield return null;
        scripted.SetCount(MatchParticipant.Team.Red, 1);
        scripted.SetCount(MatchParticipant.Team.Blue, 1);
        // Capture the production loadout after its first pose evaluation, not a prefab T-pose.
        yield return new WaitForSeconds(1);
        staticTemplates = new GameObject[2]; riggedTemplates = new GameObject[2];
        // Actors are root objects in the production test harness.
        foreach (var actor in FindObjectsByType<MatchParticipant>())
            if (actor.match == scripted.match && !riggedTemplates[(int)actor.team]) Capture(actor);
        if (!riggedTemplates[0] || !riggedTemplates[1])
        {
            status = "Template capture failed. Check the Console.";
            Debug.LogError(status, this); yield break;
        }
        scripted.SetCount(MatchParticipant.Team.Red, 0); scripted.SetCount(MatchParticipant.Team.Blue, 0);
        yield return null;
        ready = true;
        status = "Same skins, equipment, spacing, shadows and observer camera.";
        SetCount(MatchParticipant.Team.Red, initialPerTeam); SetCount(MatchParticipant.Team.Blue, initialPerTeam);
    }

    void Capture(MatchParticipant actor)
    {
        int team = (int)actor.team;
        if (riggedTemplates[team]) return;
        var source = actor.transform;
        var transforms = new Dictionary<Transform, Transform>();
        var rigged = new GameObject(actor.team + " visual template"); rigged.SetActive(false);
        rigged.transform.SetParent(transform, false);
        transforms.Add(source, rigged.transform);
        var required = new HashSet<Transform> { source };
        foreach (var renderer in source.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue;
            Require(renderer.transform, source, required);
            if (renderer is SkinnedMeshRenderer skinned)
            {
                if (skinned.rootBone) Require(skinned.rootBone, source, required);
                foreach (var bone in skinned.bones) if (bone) Require(bone, source, required);
            }
        }
        foreach (var animator in source.GetComponentsInChildren<Animator>())
        {
            if (!animator.isActiveAndEnabled || !animator.runtimeAnimatorController || !required.Contains(animator.transform)) continue;
            if (animator.isHuman)
                for (int bone = 0; bone < (int)HumanBodyBones.LastBone; bone++)
                { var item = animator.GetBoneTransform((HumanBodyBones)bone); if (item) Require(item, source, required); }
        }
        CopyChildren(source, rigged.transform, transforms, required);
        var baked = new GameObject(actor.team + " baked template"); baked.SetActive(false);
        baked.transform.SetParent(transform, false);
        foreach (var renderer in source.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            Mesh mesh;
            if (renderer is SkinnedMeshRenderer skin)
            {
                var target = transforms[skin.transform].gameObject.AddComponent<SkinnedMeshRenderer>();
                target.sharedMesh = skin.sharedMesh; target.rootBone = skin.rootBone ? transforms[skin.rootBone] : null;
                var bones = skin.bones;
                for (int i = 0; i < bones.Length; i++) bones[i] = bones[i] ? transforms[bones[i]] : null;
                target.bones = bones; target.localBounds = skin.localBounds; target.quality = skin.quality;
                for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++) target.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
                CopyRendering(renderer, target);
                mesh = new Mesh { name = actor.team + " " + skin.name + " baked pose" };
                skin.BakeMesh(mesh, true); bakedMeshes.Add(mesh);
            }
            else if (renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh)
            {
                mesh = filter.sharedMesh;
                transforms[renderer.transform].gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                CopyRendering(renderer, transforms[renderer.transform].gameObject.AddComponent<MeshRenderer>());
            }
            else continue;
            // Keep the imported renderer scale; BakeMesh(true) compensates its FBX unit scale.
            var part = new GameObject(renderer.name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(baked.transform, false);
            var matrix = source.worldToLocalMatrix * renderer.localToWorldMatrix;
            part.transform.localPosition = matrix.GetColumn(3);
            part.transform.localRotation = matrix.rotation; part.transform.localScale = matrix.lossyScale;
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            CopyRendering(renderer, part.GetComponent<MeshRenderer>());
        }
        foreach (var animator in source.GetComponentsInChildren<Animator>())
        {
            if (!animator.isActiveAndEnabled || !animator.runtimeAnimatorController || !transforms.ContainsKey(animator.transform)) continue;
            var target = transforms[animator.transform].gameObject.AddComponent<Animator>();
            target.avatar = animator.avatar; target.runtimeAnimatorController = animator.runtimeAnimatorController;
            target.applyRootMotion = false; target.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            target.enabled = false;
        }
        riggedTemplates[team] = rigged; staticTemplates[team] = baked;
    }
    static void Require(Transform item, Transform root, HashSet<Transform> required)
    {
        while (item) { required.Add(item); if (item == root) return; item = item.parent; }
    }
    static void CopyChildren(Transform source, Transform parent, Dictionary<Transform, Transform> map, HashSet<Transform> required)
    {
        foreach (Transform child in source)
        {
            if (!required.Contains(child)) continue;
            var copy = new GameObject(child.name).transform; copy.SetParent(parent, false);
            copy.localPosition = child.localPosition; copy.localRotation = child.localRotation; copy.localScale = child.localScale;
            copy.gameObject.SetActive(child.gameObject.activeSelf); map.Add(child, copy);
            CopyChildren(child, copy, map, required);
        }
    }
    static void CopyRendering(Renderer source, Renderer target)
    {
        target.sharedMaterials = source.sharedMaterials;
        var properties = new MaterialPropertyBlock(); source.GetPropertyBlock(properties); target.SetPropertyBlock(properties);
        target.shadowCastingMode = source.shadowCastingMode; target.receiveShadows = source.receiveShadows;
        target.lightProbeUsage = source.lightProbeUsage; target.reflectionProbeUsage = source.reflectionProbeUsage;
        target.renderingLayerMask = source.renderingLayerMask; target.gameObject.layer = source.gameObject.layer;
    }
    public void SetRepresentation(Representation value)
    {
        if (!ready || representation == value) return;
        ClearVisuals(red); ClearVisuals(blue);
        scripted.SetCount(MatchParticipant.Team.Red, 0); scripted.SetCount(MatchParticipant.Team.Blue, 0);
        representation = value;
        Populate(MatchParticipant.Team.Red, redCount); Populate(MatchParticipant.Team.Blue, blueCount);
    }
    public void SetCount(MatchParticipant.Team team, int count)
    {
        if (!ready) return;
        count = Mathf.Clamp(count, 0, scripted.maximumPerTeam);
        if (team == MatchParticipant.Team.Red) { redCount = count; redText = count.ToString(); }
        else { blueCount = count; blueText = count.ToString(); }
        Populate(team, count);
    }
    void Populate(MatchParticipant.Team team, int count)
    {
        if (representation == Representation.ScriptedInfantry) { scripted.SetCount(team, count); return; }
        var list = team == MatchParticipant.Team.Red ? red : blue;
        while (list.Count > count) { var item = list[list.Count - 1]; list.RemoveAt(list.Count - 1); item.SetActive(false); Destroy(item); }
        while (list.Count < count)
        {
            int index = list.Count; float side = team == MatchParticipant.Team.Red ? -1 : 1;
            var template = representation == Representation.StaticMeshes ? staticTemplates[(int)team] : riggedTemplates[(int)team];
            var item = Instantiate(template, transform);
            item.name = team + " " + representation + " " + index;
            item.transform.SetPositionAndRotation(new Vector3(((index % 8 + 1) / 2) * (index % 2 == 0 ? -scripted.spacing : scripted.spacing), .15f,
                side * (scripted.teamDistance * .5f + index / 8 * scripted.spacing)), Quaternion.Euler(0, side < 0 ? 0 : 180, 0));
            foreach (var animator in item.GetComponentsInChildren<Animator>(true)) animator.enabled = representation == Representation.AnimatedSkinnedMeshes;
            item.SetActive(true);
            if (representation == Representation.AnimatedSkinnedMeshes)
                foreach (var animator in item.GetComponentsInChildren<Animator>())
                    foreach (var parameter in animator.parameters)
                        if (parameter.type == AnimatorControllerParameterType.Bool && parameter.name == "Grounded") animator.SetBool(parameter.nameHash, true);
            list.Add(item);
        }
    }
    static void ClearVisuals(List<GameObject> list)
    {
        foreach (var item in list) { item.SetActive(false); Destroy(item); }
        list.Clear();
    }
    void OnDestroy() { foreach (var mesh in bakedMeshes) if (mesh) Destroy(mesh); }
    void OnGUI()
    {
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F1) { showControls = !showControls; Event.current.Use(); }
        if (!showControls) return;
        GUILayout.BeginArea(new Rect(12, 48, 440, 310), GUI.skin.box);
        GUILayout.Label("INFANTRY RENDERING BASELINE"); GUILayout.Label(status);
        GUI.enabled = ready;
        int selected = GUILayout.SelectionGrid((int)representation, Labels, 2);
        if (selected != (int)representation) SetRepresentation((Representation)selected);
        Row("RED", MatchParticipant.Team.Red, ref redText); Row("BLUE", MatchParticipant.Team.Blue, ref blueText);
        if (representation == Representation.ScriptedInfantry)
        {
            int activity = GUILayout.SelectionGrid((int)scripted.activity, Activities, 3);
            if (activity != (int)scripted.activity) scripted.SetActivity((EntityPerformanceControl.Activity)activity);
        }
        if (GUILayout.Button("Clear both teams")) { SetCount(MatchParticipant.Team.Red, 0); SetCount(MatchParticipant.Team.Blue, 0); }
        GUI.enabled = true;
        GUILayout.Label("Frozen/static: identical captured pose. Idle: animation only, no IK.\nFull actor: production scripts, physics and animation.\nWait after changes before profiling. F1 hides this panel.\nRMB + WASD / Q E: fly | Shift: faster.");
        GUILayout.EndArea();
    }
    void Row(string label, MatchParticipant.Team team, ref string value)
    {
        GUILayout.BeginHorizontal(); GUILayout.Label(label, GUILayout.Width(40));
        value = GUILayout.TextField(value, 4, GUILayout.Width(55));
        if (GUILayout.Button("Apply") && int.TryParse(value, out int count)) SetCount(team, count);
        int current = team == MatchParticipant.Team.Red ? redCount : blueCount;
        if (GUILayout.Button("-32")) SetCount(team, current - 32);
        if (GUILayout.Button("+32")) SetCount(team, current + 32);
        GUILayout.EndHorizontal();
    }
}







