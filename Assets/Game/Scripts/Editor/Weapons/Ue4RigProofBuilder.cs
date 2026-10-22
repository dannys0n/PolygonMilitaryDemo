using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Fits Polygon geometry to the native UE4 bind skeleton once, in the editor.
// Source animation tracks, weapon rig and runtime controllers remain unchanged.
[InitializeOnLoad]
public static class Ue4RigProofBuilder
{
    const string Root = "Assets/Game/AnimationTests/UE4RigProof";
    const string Tests = "Assets/Game/AnimationTests";
    const string SourceArms = "Assets/FPSAnimationPack/Meshes/Characters/FPS_Arms_UE4/SK_Arms_Mono.fbx";
    const string Id = "ue4-rig-proof-mg5";
    const string IdPrefix = "ue4-rig-proof-";
    public const string PreviewRigPath = Root + "/PolygonArms_UE4.prefab";
    public const string OriginalPreviewRigPath = "Assets/Game/Models/Characters/ScaledSoldier/ArmsRig.prefab";
    static Ue4RigProofBuilder() => FpsPackPreviewCatalog.AdditionalModels += PreviewModels;

    static T Save<T>(T asset, string path) where T : Object
    {
        asset.name = Path.GetFileNameWithoutExtension(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var old = AssetDatabase.LoadAssetAtPath<T>(path);
        if (!old) { AssetDatabase.CreateAsset(asset, path); return asset; }
        // Replacing skin weights / vertex order also needs fresh native mesh buffers.
        // CopySerialized alone can leave an existing asset's GPU skin data stale.
        if (old is Mesh previousMesh) previousMesh.Clear(false);
        EditorUtility.CopySerialized(asset, old);
        if (old is Mesh updatedMesh) updatedMesh.UploadMeshData(false);
        Object.DestroyImmediate(asset);
        EditorUtility.SetDirty(old);
        return old;
    }

    static GameObject Clone(GameObject prefab)
    {
        var go = Object.Instantiate(prefab);
        if (PrefabUtility.IsPartOfPrefabInstance(go))
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        return go;
    }

    static string Map(string name)
    {
        var body = new Dictionary<string, string>
        {
            {"Root", "root"}, {"Hips", "pelvis"}, {"Spine_01", "spine_01"},
            {"Spine_02", "spine_02"}, {"Spine_03", "spine_03"}, {"Neck", "neck_01"}, {"Head", "head"}
        };
        if (body.TryGetValue(name, out var mapped)) return mapped;
        foreach (var side in new[] { "L", "R" })
        {
            var suffix = "_" + side.ToLowerInvariant();
            if (name == "Clavicle_" + side) return "clavicle" + suffix;
            if (name == "Shoulder_" + side) return "upperarm" + suffix;
            if (name == "Elbow_" + side) return "lowerarm" + suffix;
            if (name == "Hand_" + side) return "hand" + suffix;
            var ending = side == "R" ? "1" : "";
            foreach (var finger in new[] { "Thumb", "IndexFinger", "Finger" })
                for (int joint = 1; joint <= (finger == "Thumb" ? 3 : 4); joint++)
                    if (name == finger + "_0" + joint + ending)
                        return (finger == "Thumb" ? "thumb" : finger == "IndexFinger" ? "index" : "ring")
                            + "_0" + Mathf.Min(joint, 3) + suffix;
        }
        return "spine_03"; // Unused lower-body bindings on the arms-only mesh.
    }

    static string Next(string name)
    {
        if (name.StartsWith("Clavicle_")) return name.Replace("Clavicle_", "Shoulder_");
        if (name.StartsWith("Shoulder_")) return name.Replace("Shoulder_", "Elbow_");
        if (name.StartsWith("Elbow_")) return name.Replace("Elbow_", "Hand_");
        if (name.StartsWith("Hand_")) return "IndexFinger_01" + (name.EndsWith("R") ? "1" : "");
        foreach (var finger in new[] { "Thumb", "IndexFinger", "Finger" })
            for (int joint = 1; joint < (finger == "Thumb" ? 3 : 4); joint++)
                foreach (var ending in new[] { "", "1" })
                    if (name == finger + "_0" + joint + ending) return finger + "_0" + (joint + 1) + ending;
        return null;
    }

    static Vector3 Position(Matrix4x4 m) => m.GetColumn(3);
    static float fittedScale;

    static string MirrorBone(string name)
    {
        foreach (var pair in new[] { "Clavicle", "Shoulder", "Elbow", "Hand" })
        {
            if (name == pair + "_L") return pair + "_R";
            if (name == pair + "_R") return pair + "_L";
        }
        foreach (var finger in new[] { "Thumb", "IndexFinger", "Finger" })
            for (int joint = 1; joint <= (finger == "Thumb" ? 3 : 4); joint++)
            {
                var left = finger + "_0" + joint;
                if (name == left) return left + "1";
                if (name == left + "1") return left;
            }
        return name;
    }

    static Mesh FitMesh(SkinnedMeshRenderer polygon, GameObject source, bool originalProportions = false)
    {
        var mesh = polygon.sharedMesh;
        if (mesh.blendShapeCount != 0) throw new InvalidOperationException("The arm proof expects an arms-only mesh without blend shapes");
        var sourceBones = source.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
        var oldBind = mesh.bindposes.Select(b => polygon.transform.localToWorldMatrix * b.inverse).ToArray();
        var indices = polygon.bones.Select((bone, i) => (bone.name, i)).ToDictionary(p => p.name, p => p.i);
        Vector3 Original(string name) => Position(oldBind[indices[name]]);
        Vector3 U(string name)
        {
            var bone = sourceBones[Map(name)];
            if (name.StartsWith("IndexFinger_04") || name.StartsWith("Finger_04") || name.StartsWith("Thumb_03"))
                return bone.position + (bone.position - bone.parent.position) * .8f;
            return bone.position;
        }
        float PolygonLength(string side) => Vector3.Distance(Original("Shoulder_" + side), Original("Elbow_" + side))
            + Vector3.Distance(Original("Elbow_" + side), Original("Hand_" + side));
        float UeLength(string side) => Vector3.Distance(U("Shoulder_" + side), U("Elbow_" + side))
            + Vector3.Distance(U("Elbow_" + side), U("Hand_" + side));
        // Bone lengths are independent of the Polygon T-pose / UE4 A-pose difference.
        // Scale the whole input first; residual corrections are derived only on the left.
        fittedScale = (UeLength("L") + UeLength("R")) / (PolygonLength("L") + PolygonLength("R"));
        Vector3 P(string name) => Original(name) * fittedScale;
        var uniform = Matrix4x4.Scale(Vector3.one * fittedScale) * polygon.transform.localToWorldMatrix;
        var mirrorWorld = Matrix4x4.Scale(new Vector3(-1, 1, 1));
        var mirrorPalm = Matrix4x4.Scale(new Vector3(1, -1, 1));
        var leftConversion = new Matrix4x4[polygon.bones.Length];
        var rightConversion = new Matrix4x4[polygon.bones.Length];
        Vector3 OldNormal() => Vector3.Cross(P("IndexFinger_01") - P("Hand_L"), P("Finger_01") - P("Hand_L")).normalized;
        Vector3 NewNormal(string hand, string ending) => Vector3.Cross(U("IndexFinger_01" + ending) - U(hand), U("Finger_01" + ending) - U(hand)).normalized;
        Quaternion Frame(string name, Vector3 normal)
        {
            string next = Next(name);
            var axis = next != null ? U(next) - U(name) : U(name) - U(polygon.bones[indices[name]].parent.name);
            return Quaternion.LookRotation(axis, normal);
        }
        for (int i = 0; i < polygon.bones.Length; i++)
        {
            string name = polygon.bones[i].name;
            string other = MirrorBone(name);
            if (other == name)
            {
                leftConversion[i] = Matrix4x4.TRS(U(name), Quaternion.Euler(0, 180, 0), Vector3.one)
                    * Matrix4x4.Translate(-P(name)) * uniform;
                rightConversion[i] = mirrorWorld * leftConversion[i];
                continue;
            }
            // Right-side input geometry/weights are intentionally not used.
            if (Map(name).EndsWith("_r")) continue;
            string next = Next(name);
            var oldAxis = next != null ? P(next) - P(name) : P(name) - P(polygon.bones[i].parent.name);
            var oldFrame = Quaternion.LookRotation(oldAxis, OldNormal());
            var leftFrame = Frame(name, NewNormal("Hand_L", ""));
            var rightFrame = Frame(other, NewNormal("Hand_R", "1"));
            float lengthCorrection = next != null ? Vector3.Distance(U(next), U(name)) / oldAxis.magnitude : 1;
            bool hand = name.StartsWith("Hand_") || name.StartsWith("Thumb_") || name.StartsWith("IndexFinger_") || name.StartsWith("Finger_");
            float widthCorrection = hand ? Mathf.Clamp(Vector3.Distance(U("IndexFinger_01"), U("Finger_01"))
                / Vector3.Distance(P("IndexFinger_01"), P("Finger_01")), .85f, 1.15f) : 1;
            var correction = originalProportions ? Matrix4x4.identity : Matrix4x4.Scale(new Vector3(widthCorrection, widthCorrection, lengthCorrection));
            var oldToLocal = Matrix4x4.TRS(P(name), oldFrame, Vector3.one).inverse * uniform;
            leftConversion[i] = Matrix4x4.TRS(U(name), leftFrame, Vector3.one) * correction * oldToLocal;
            // A palm frame changes handedness under reflection. Mirror its local normal
            // as well as the geometry; use exactly the left-side correction parameters.
            rightConversion[i] = Matrix4x4.TRS(U(other), rightFrame, Vector3.one) * mirrorPalm * correction * oldToLocal;
        }
        var originalVertices = mesh.vertices;
        var worldVertices = originalVertices.Select(v => polygon.transform.TransformPoint(v)).ToArray();
        var leftTriangles = new int[mesh.subMeshCount][];
        var used = new SortedSet<int>();
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            var input = mesh.GetTriangles(sub);
            var triangles = new List<int>();
            for (int i = 0; i < input.Length; i += 3)
            {
                float min = Mathf.Min(worldVertices[input[i]].x, worldVertices[input[i + 1]].x, worldVertices[input[i + 2]].x);
                float max = Mathf.Max(worldVertices[input[i]].x, worldVertices[input[i + 1]].x, worldVertices[input[i + 2]].x);
                if (min < -.0001f && max > .0001f) throw new InvalidOperationException("An arm triangle crosses the mirror plane");
                if (min >= -.0001f) continue;
                for (int j = 0; j < 3; j++) { triangles.Add(input[i + j]); used.Add(input[i + j]); }
            }
            leftTriangles[sub] = triangles.ToArray();
        }
        var originals = used.ToArray();
        int count = originals.Length;
        var remap = originals.Select((v, i) => (v, i)).ToDictionary(p => p.v, p => p.i);
        var weights = mesh.boneWeights;
        var vertices = new Vector3[count * 2];
        var fittedWeights = new BoneWeight[vertices.Length];
        var bonePairs = polygon.bones.Select(b => indices[MirrorBone(b.name)]).ToArray();
        Vector3 Apply(Matrix4x4[] matrices, BoneWeight w, Vector3 v) => matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0
            + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
            + matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2
            + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
        for (int i = 0; i < count; i++)
        {
            int index = originals[i];
            var w = weights[index];
            foreach (var influence in new[] { (w.boneIndex0, w.weight0), (w.boneIndex1, w.weight1), (w.boneIndex2, w.weight2), (w.boneIndex3, w.weight3) })
                if (influence.Item2 > 0 && Map(polygon.bones[influence.Item1].name).EndsWith("_r"))
                    throw new InvalidOperationException("Left mesh has a right-arm skin influence");
            vertices[i] = Apply(leftConversion, w, originalVertices[index]);
            vertices[count + i] = Apply(rightConversion, w, originalVertices[index]);
            if (!float.IsFinite(vertices[i].sqrMagnitude) || !float.IsFinite(vertices[count + i].sqrMagnitude))
                throw new InvalidOperationException("Invalid fitted vertex");
            fittedWeights[i] = w;
            fittedWeights[count + i] = new BoneWeight { boneIndex0 = bonePairs[w.boneIndex0], weight0 = w.weight0,
                boneIndex1 = bonePairs[w.boneIndex1], weight1 = w.weight1, boneIndex2 = bonePairs[w.boneIndex2], weight2 = w.weight2,
                boneIndex3 = bonePairs[w.boneIndex3], weight3 = w.weight3 };
        }
        string meshName = originalProportions ? "PolygonArms_UE4OriginalProportions" : "PolygonArms_UE4Bind";
        var result = new Mesh { name = meshName, indexFormat = mesh.indexFormat, vertices = vertices,
            boneWeights = fittedWeights, bindposes = polygon.bones.Select(b => sourceBones[Map(b.name)].worldToLocalMatrix).ToArray(), subMeshCount = mesh.subMeshCount };
        int contactVertices = originalProportions ? 0 : Ue4HandFit.Refine(result, polygon.bones.Select(b => sourceBones[Map(b.name)]).ToArray(), source.GetComponentInChildren<SkinnedMeshRenderer>());
        for (int channel = 0; channel < 8; channel++)
        {
            var uv = new List<Vector4>(); mesh.GetUVs(channel, uv);
            if (uv.Count == 0) continue;
            result.SetUVs(channel, originals.Concat(originals).Select(i => uv[i]).ToList());
        }
        var colors = mesh.colors32;
        if (colors.Length != 0) result.colors32 = originals.Concat(originals).Select(i => colors[i]).ToArray();
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            var left = leftTriangles[sub].Select(i => remap[i]).ToArray();
            var right = new int[left.Length];
            for (int i = 0; i < left.Length; i += 3)
            { right[i] = left[i] + count; right[i + 1] = left[i + 2] + count; right[i + 2] = left[i + 1] + count; }
            result.SetTriangles(left.Concat(right).ToArray(), sub);
        }
        result.RecalculateNormals(); result.RecalculateTangents(); result.RecalculateBounds();
        Directory.CreateDirectory("Library/AnimationTests");
        File.WriteAllText("Library/AnimationTests/" + (originalProportions ? "Ue4OriginalProportions.txt" : "Ue4RigFit.txt"), $"Uniform scale: {fittedScale:F6}\nTemplate: left Polygon arm\nMirrored vertex pairs: {count}\nMirrored skin weights: exact\nOriginal proportions: {originalProportions}\nFinger surface-fit pairs: {contactVertices}\n");
        return Save(result, originalProportions ? "Assets/Game/Models/Characters/ScaledSoldier/Arms.asset" : Root + "/" + meshName + ".asset");
    }

    static void ReplaceArms(GameObject root, Mesh mesh, SkinnedMeshRenderer polygon)
    {
        var arms = root.transform.Find("SourceArms") ?? root.transform;
        if (!arms) throw new InvalidOperationException("Missing source arms frame");
        var weapon = arms.Find("root/ik_hand_root/ik_hand_gun/SourceWeapon");
        foreach (var renderer in arms.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(r => !weapon || !r.transform.IsChildOf(weapon)))
            Object.DestroyImmediate(renderer);
        var bones = arms.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
        var node = new GameObject("PolygonArms_UE4Skin"); node.transform.SetParent(arms, false);
        var skin = node.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = mesh; skin.sharedMaterials = polygon.sharedMaterials;
        skin.bones = polygon.bones.Select(b => bones[Map(b.name)]).ToArray();
        skin.rootBone = bones["root"]; skin.updateWhenOffscreen = true;
        skin.shadowCastingMode = ShadowCastingMode.Off;
        skin.localBounds = new Bounds(new Vector3(0, 1.35f, -.25f), Vector3.one * 3);
    }

    [MenuItem("Tools/Game/Animation Tests/Review Viper UE4 reload")]
    public static void PreviewViper() => FpsPackPreview.OpenWeapon("Viper-357", OriginalPreviewRigPath);
    [MenuItem("Tools/Game/Animation Tests/Review original-proportions Viper reload")]
    public static void PreviewOriginalViper() => FpsPackPreview.OpenWeapon("Viper-357", OriginalPreviewRigPath);
    static IEnumerable<FpsPackPreviewCatalog.Entry> PreviewModels()
    {
        foreach (string path in new[] { OriginalPreviewRigPath })
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path))
            yield return new FpsPackPreviewCatalog.Entry { file = path, source = "Game/" + Path.GetFileNameWithoutExtension(path),
                type = "SkeletalMesh", success = true, rigLabel = path == PreviewRigPath ? "UE4 / fitted Polygon" : "UE4 / scaled Polygon",
                skeleton = "/FPSAnimationPack/Meshes/Characters/Mannequin_UE4/Skeleton/SK_Mannequin_Skeleton.SK_Mannequin_Skeleton" };
    }

    [MenuItem("Tools/Game/Animation Tests/Preview Polygon UE4 animations")]
    public static void Preview() => FpsPackPreview.OpenCharacter(OriginalPreviewRigPath);

    static AuthoredGrenadeView BuildGrenade(WeaponCatalog.Entry entry, Mesh mesh, SkinnedMeshRenderer polygon, bool originalProportions = false)
    {
        var grenade = Clone(entry.firstPersonPrefab.grenadeView.gameObject);
        try
        {
            ReplaceArms(grenade, mesh, polygon);
            grenade.name = originalProportions ? "Grenade_UE4OriginalProportions_FPS" : "Grenade_UE4RigProof_FPS";
            var view = grenade.GetComponent<AuthoredGrenadeView>();
            var clip = grenade.GetComponent<Animator>().runtimeAnimatorController.animationClips.First();
            clip.SampleAnimation(grenade, .55f);
            var bones = grenade.transform.Find("SourceArms").GetComponentsInChildren<Transform>(true).ToDictionary(b => b.name);
            var pocket = (bones["index_02_r"].position + bones["ring_02_r"].position + bones["thumb_02_r"].position) / 3;
            view.handPosition = view.hand.InverseTransformPoint(pocket);
            var up = (bones["index_01_r"].position - bones["ring_01_r"].position).normalized;
            view.handRotation = (Quaternion.Inverse(view.hand.rotation)
                * Quaternion.LookRotation(Vector3.Cross(up, pocket - view.hand.position).normalized, up)).eulerAngles;
            clip.SampleAnimation(grenade, view.pinPullTime);
            view.pinHand = bones["index_03_l"];
            pocket = (bones["index_02_r"].position + bones["ring_02_r"].position + bones["thumb_02_r"].position) / 3;
            var pinTarget = (bones["index_02_l"].position + bones["index_03_l"].position) * .5f;
            var types = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Players/Player_Polygon_Local.prefab").GetComponent<SoldierGrenades>().types;
            view.heldPoses = types.Select(type =>
            {
                var art = new GameObject("Grip calibration");
                try
                {
                    foreach (Transform child in type.projectile.transform) Object.Instantiate(child.gameObject, art.transform, false);
                    var renderers = art.GetComponentsInChildren<Renderer>();
                    var body = renderers.First(r => !r.name.Contains("_Pin_") && !r.name.Contains("_Clip_"));
                    var pin = renderers.First(r => r.name.Contains("_Pin_"));
                    var rotation = Quaternion.FromToRotation(pin.bounds.center - body.bounds.center, pinTarget - pocket);
                    var position = pinTarget - rotation * pin.bounds.center;
                    return new AuthoredGrenadeView.HeldPose { grenade = type, position = view.hand.InverseTransformPoint(position),
                        rotation = (Quaternion.Inverse(view.hand.rotation) * rotation).eulerAngles };
                }
                finally { Object.DestroyImmediate(art); }
            }).ToArray();
            clip.SampleAnimation(grenade, 0);
            return PrefabUtility.SaveAsPrefabAsset(grenade, originalProportions ? "Assets/Game/Prefabs/Players/Visuals/ScaledGrenadeView.prefab" : Root + "/" + grenade.name + ".prefab").GetComponent<AuthoredGrenadeView>();
        }
        finally { Object.DestroyImmediate(grenade); }
    }

    static WeaponCatalog.Entry BuildWeapon(WeaponCatalog.Entry source, Mesh mesh, SkinnedMeshRenderer polygon, AuthoredGrenadeView grenade, bool originalProportions = false)
    {
        string key = source.weapon.catalogId.Substring("animation-test-".Length);
        bool mg5 = key == "MGX5_UE4";
        string stem = (mg5 ? "MG5" : key.Replace("_UE4", "")) + (originalProportions ? "_UE4OriginalProportions" : "_UE4RigProof");
        var fps = Clone(source.firstPersonPrefab.gameObject);
        var tps = Clone(source.thirdPersonPrefab.gameObject);
        try
        {
            ReplaceArms(fps, mesh, polygon);
            fps.name = stem + "_FPS"; tps.name = stem + "_TPS";
            var definition = Object.Instantiate(source.weapon);
            definition.catalogId = originalProportions ? "ue4-original-proportions-" + key.ToLowerInvariant() : mg5 ? Id : IdPrefix + key.ToLowerInvariant();
            definition.displayName = source.weapon.displayName.Replace(" (UE4)", originalProportions ? " (UE4 Original Proportions)" : " (UE4 Rig Proof)");
            definition = Save(definition, Root + "/" + stem + ".asset");
            tps.GetComponent<SoldierWeapon>().definition = definition;
            fps.GetComponent<SoldierWeaponView>().grenadeView = grenade;
            var f = PrefabUtility.SaveAsPrefabAsset(fps, Root + "/" + stem + "_FPS.prefab").GetComponent<SoldierWeaponView>();
            var t = PrefabUtility.SaveAsPrefabAsset(tps, Root + "/" + stem + "_TPS.prefab").GetComponent<SoldierWeaponView>();
            return new WeaponCatalog.Entry { weapon = definition, basePrefab = source.basePrefab,
                firstPersonPrefab = f, thirdPersonPrefab = t, polished = false,
                sights = source.sights, attachments = source.attachments, defaultSight = source.defaultSight,
                animationStyle = originalProportions ? "Native UE4 skeleton / uniformly scaled original Polygon proportions" : "Native UE4 skeleton / fitted Polygon FPS skin proof" };
        }
        finally { Object.DestroyImmediate(fps); Object.DestroyImmediate(tps); }
    }

    [MenuItem("Tools/Game/Animation Tests/Build all UE4 skeleton proofs")]
    public static void Rebuild() => Main();

    [MenuItem("Tools/Game/Animation Tests/Build original-proportions comparison")]
    public static void BuildOriginalComparison() => WeaponProportionComparisonBuilder.Main();
    public static object OriginalComparison()
    {
        if (WeaponProportionComparisonBuilder.Fitted().Any())
            return WeaponProportionComparisonBuilder.Main();
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building");
        var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Tests + "/AnimationTestCatalog.asset");
        var sources = catalog.entries.Where(e => e.weapon.catalogId.StartsWith("animation-test-") && e.weapon.catalogId.EndsWith("_UE4")).ToArray();
        if (sources.Length == 0) throw new InvalidOperationException("Missing source UE4 weapons");
        var polygon = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Players/Player_Polygon_Local.prefab")
            .GetComponent<SoldierFirstPersonView>().armsAnimator.GetComponentInChildren<SkinnedMeshRenderer>(true);
        var neutral = Clone(AssetDatabase.LoadAssetAtPath<GameObject>(SourceArms));
        try
        {
            var mesh = FitMesh(polygon, neutral, true);
            ReplaceArms(neutral, mesh, polygon);
            neutral.name = "PolygonArms_UE4OriginalProportions";
            PrefabUtility.SaveAsPrefabAsset(neutral, OriginalPreviewRigPath);
            var grenade = BuildGrenade(sources[0], mesh, polygon, true);
            var comparisons = sources.Select(e => BuildWeapon(e, mesh, polygon, grenade, true)).ToArray();
            var accepted = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Root + "/UE4RigProofCatalog.asset");
            var review = ScriptableObject.CreateInstance<WeaponCatalog>();
            review.entries = accepted.entries.Concat(comparisons).ToArray();
            review = Save(review, Root + "/OriginalProportionsCatalog.asset");
            AssetDatabase.SaveAssets();
            return new { weapons = review.entries.Length, uniformScale = fittedScale, originalMesh = OriginalPreviewRigPath };
        }
        finally { Object.DestroyImmediate(neutral); }
    }

    public static object Main()
    {
        if (WeaponProportionComparisonBuilder.Fitted().Any()) return WeaponProportionComparisonBuilder.RefreshPairs();
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building");
        var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Tests + "/AnimationTestCatalog.asset");
        var sources = catalog.entries.Where(e => e.weapon.catalogId.StartsWith("animation-test-")
            && e.weapon.catalogId.EndsWith("_UE4")).ToArray();
        if (sources.Length == 0) throw new InvalidOperationException("Build the source UE4 test weapons first");
        var polygon = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Players/Player_Polygon_Local.prefab")
            .GetComponent<SoldierFirstPersonView>().armsAnimator.GetComponentInChildren<SkinnedMeshRenderer>(true);
        var neutral = Clone(AssetDatabase.LoadAssetAtPath<GameObject>(SourceArms));
        try
        {
            var mesh = FitMesh(polygon, neutral);
            ReplaceArms(neutral, mesh, polygon);
            neutral.name = "PolygonArms_UE4";
            PrefabUtility.SaveAsPrefabAsset(neutral, PreviewRigPath);
            var grenade = BuildGrenade(sources[0], mesh, polygon);
            var proofs = sources.Select(e => BuildWeapon(e, mesh, polygon, grenade)).ToArray();
            catalog.entries = catalog.entries.Where(e => !e.weapon.catalogId.StartsWith(IdPrefix)).Concat(proofs).ToArray();
            EditorUtility.SetDirty(catalog);
            const string reviewPath = Root + "/UE4RigProofCatalog.asset";
            const string oldPath = Root + "/MG5_ReviewCatalog.asset";
            if (!AssetDatabase.LoadAssetAtPath<WeaponCatalog>(reviewPath) && AssetDatabase.LoadAssetAtPath<WeaponCatalog>(oldPath))
            {
                var error = AssetDatabase.MoveAsset(oldPath, reviewPath);
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            }
            var review = ScriptableObject.CreateInstance<WeaponCatalog>();
            review.entries = proofs;
            review = Save(review, reviewPath);
            AssetDatabase.SaveAssets();
            return new { weapons = proofs.Length, vertices = mesh.vertexCount, uniformScale = fittedScale,
                catalogCount = catalog.entries.Length, previewRig = PreviewRigPath };
        }
        finally { Object.DestroyImmediate(neutral); }
    }
}
