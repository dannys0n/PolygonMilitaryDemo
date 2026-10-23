using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Build the empty Kar98k stripper-clip reload through the production scaled rig.
public static class Kar98ReloadBuilder
{
    const string Folder = "Assets/Game/Animations/Weapons/Reloads/Kar98k";
    const string Source = "Assets/FPSAnimationPack/Actions/UE4/Kar98K/";
    static T Save<T>(T value, string path) where T : Object
    {
        var old = AssetDatabase.LoadAssetAtPath<T>(path);
        value.name = Path.GetFileNameWithoutExtension(path);
        if (old) { EditorUtility.CopySerialized(value, old); Object.DestroyImmediate(value); EditorUtility.SetDirty(old); return old; }
        AssetDatabase.CreateAsset(value, path); return value;
    }
    static Transform Find(GameObject root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
    static RuntimeAnimatorController Override(RuntimeAnimatorController original, Dictionary<AnimationClip, AnimationClip> replacements, string name)
    {
        var result = new AnimatorOverrideController(WeaponAnimationSource.Base(original));
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(); result.GetOverrides(pairs);
        for (int i = 0; i < pairs.Count; i++)
        {
            var key = pairs[i].Key; var effective = WeaponAnimationSource.Resolve(original, key);
            pairs[i] = new(key, replacements.TryGetValue(effective, out var clip) ? clip : effective);
        }
        result.ApplyOverrides(pairs); return Save(result, Folder + "/" + name + ".overrideController");
    }

    public static object Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode only.");
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>("Assets/Game/Definitions/Weapons/Catalogs/WeaponCatalog.asset");
        var entry = catalog.entries.First(e => e.weapon.catalogId == "ue4-original-proportions-kar98k_ue4");
        var definition = entry.weapon; var view = entry.firstPersonPrefab; var tactical = definition.tacticalReload;
        var idle = WeaponAnimationSource.State(view.animationOverride, "Base Layer", "Idle");
        var originalReload = WeaponAnimationSource.State(view.animationOverride, "Base Layer", "Reload");
        var character = AssetDatabase.LoadAssetAtPath<AnimationClip>(Source + "Character/AM_FP_Kar98K_Empty_Reload.anim");
        var weapon = AssetDatabase.LoadAssetAtPath<AnimationClip>(Source + "Weapon/AM_W_Kar98K_Empty_Reload.anim");
        var root = Object.Instantiate(view.sharedFirstPersonRig);
        AnimationClip full;
        try
        {
            var rig = root.GetComponent<SoldierSharedFirstPersonRig>();
            var gun = Object.Instantiate(view.gameObject, root.transform.Find(view.sharedWeaponMountPath), false);
            gun.name = "SourceWeapon"; gun.transform.SetLocalPositionAndRotation(view.mountPosition, view.mountRotation); gun.transform.localScale = view.mountScale;
            var arms = Find(root, "SourceArms");
            foreach (var animator in root.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            idle.SampleAnimation(root, 0);
            var bones = arms.GetComponentsInChildren<Transform>(true);
            var rest = bones.Select(t => (t.localPosition, t.localRotation, t.localScale)).ToArray();
            var tracks = bones.Select(_ => Enumerable.Range(0, 10).Select(_ => new List<Keyframe>()).ToArray()).ToArray();
            int frames = Mathf.CeilToInt(character.length * 60);
            var clip = Find(gun.gameObject, "Clip"); var cartridge = Find(gun.gameObject, "Cartridge");
            for (int f = 0; f <= frames; f++)
            {
                float time = character.length * f / frames;
                for (int b = 0; b < bones.Length; b++) { bones[b].SetLocalPositionAndRotation(rest[b].localPosition, rest[b].localRotation); bones[b].localScale = rest[b].localScale; }
                character.SampleAnimation(arms.gameObject, time); weapon.SampleAnimation(gun.gameObject, time);
                // Source props are parked below the rifle while not in use.
                clip.localScale = clip.localPosition.y > -.30f ? Vector3.one : Vector3.zero;
                cartridge.localScale = Vector3.zero;
                for (int b = 0; b < bones.Length; b++)
                {
                    var t = bones[b];
                    for (int a = 0; a < 10; a++) tracks[b][a].Add(new Keyframe(time, a < 3 ? t.localPosition[a] : a < 7 ? t.localRotation[a - 3] : t.localScale[a - 7]));
                }
            }
            full = new AnimationClip { frameRate = 60 };
            var bindings = new List<EditorCurveBinding>(); var curves = new List<AnimationCurve>();
            for (int b = 0; b < bones.Length; b++) for (int a = 0; a < 10; a++)
            {
                string path = AnimationUtility.CalculateTransformPath(bones[b], root.transform);
                var keys = tracks[b][a];
                var curve = new AnimationCurve(keys.All(k => Mathf.Abs(k.value - keys[0].value) < 1e-7f) ? new[] { keys.First(), keys.Last() } : keys.ToArray());
                for (int k = 0; k < curve.length; k++) { AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear); }
                bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), a < 3 ? "m_LocalPosition." + "xyz"[a] : a < 7 ? "m_LocalRotation." + "xyzw"[a - 3] : "m_LocalScale." + "xyz"[a - 7])); curves.Add(curve);
            }
            AnimationUtility.SetEditorCurves(full, bindings.ToArray(), curves.ToArray()); full.EnsureQuaternionContinuity();
            AnimationClipCompactor.Optimize(full); full = Save(full, Folder + "/FullReloadFPS.anim");
        }
        finally { Object.DestroyImmediate(root); }
        var proof = ScriptableObject.CreateInstance<WeaponAnimationConversionProof>();
        try
        {
            proof.originalProportions = true; proof.source = proof.converted = entry; proof.idle = idle; proof.reload = full;
            proof.thirdPersonSourceController = tactical.thirdPerson; proof.thirdPersonSourceParts = tactical.thirdPersonParts;
            TpsAnimationAdapter.BuildReload(proof, Folder);
        }
        finally { Object.DestroyImmediate(proof); }
        var fullReload = tactical; fullReload.duration = full.length; fullReload.roundReload = default;
        var fullFPS = Override(view.animationOverride, new() { [originalReload] = full }, "FullFPS");
        var prefabPath = AssetDatabase.GetAssetPath(view);
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            prefab.GetComponent<SoldierWeaponView>().fullReloadAnimationOverride = fullFPS;
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var replacements = new Dictionary<AnimationClip, AnimationClip>
        {
            [WeaponAnimationSource.State(tactical.thirdPerson, "Reload", "Reload")] = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/Reload.anim"),
            [WeaponAnimationSource.State(tactical.thirdPerson, "WeaponPose", "Reload")] = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/ReloadWeapon.anim")
        };
        fullReload.thirdPerson = Override(tactical.thirdPerson, replacements, "FullTPS");
        fullReload.thirdPersonParts = Override(tactical.thirdPersonParts, new() { [WeaponAnimationSource.State(tactical.thirdPersonParts, "Base Layer", "Reload")] = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/ReloadParts.anim") }, "FullParts");
        definition.fullReload = fullReload; EditorUtility.SetDirty(definition); AssetDatabase.SaveAssets();
        return new { definition.displayName, fullReload.duration, tactical = tactical.duration, rounds = definition.magazineCapacity };
    }
}
