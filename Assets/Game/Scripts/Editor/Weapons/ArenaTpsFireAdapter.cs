using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

// The ordinary TPS Fire layer contains additive recoil. A complete bolt-cycle
// pose needs an override layer instead, without changing other weapons' recoil.
public static class ArenaTpsFireAdapter
{
    public static void Configure(SoldierWeaponDefinition definition, string folder,
        AnimationClip fireBody, AnimationClip fireWeapon)
    {
        if (!fireBody || !fireWeapon) throw new ArgumentException("Authored TPS fire clips are required.");
        var input = definition.thirdPerson;
        var original = ArenaWeaponAnimationSource.Base(input);
        string controllerPath = folder + "/FireBody.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (!controller)
        {
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original), controllerPath))
                throw new InvalidOperationException("Unable to create the authored TPS fire controller.");
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        }
        if (!controller.parameters.Any(p => p.name == "WeaponFireWeight"))
            controller.AddParameter("WeaponFireWeight", AnimatorControllerParameterType.Float);

        var layers = controller.layers;
        int fireLayer = Array.FindIndex(layers, layer => layer.name == "Fire");
        if (fireLayer < 0) throw new InvalidOperationException("The TPS controller has no body Fire layer.");
        var layer = layers[fireLayer];
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layer.defaultWeight = 1;
        layer.avatarMask = FireMask(layers.First(l => l.name == "Reload").avatarMask, fireBody,
            folder + "/FireArms.mask");
        var states = layer.stateMachine.states.Select(child => child.state).ToArray();
        var fire = states.First(s => s.name == "Fire");
        var empty = states.First(s => s.name == "Empty");
        fire.speed = 1;
        fire.speedParameterActive = false;
        fire.writeDefaultValues = false;
        empty.writeDefaultValues = false;

        // Keep the original clip as the override key: all generated idle/reload
        // mappings stay valid when this controller is rebuilt.
        var oldFire = fire.motion as AnimationClip;
        var oldWeaponFire = layers.First(l => l.name == "WeaponFire").stateMachine.states
            .First(s => s.state.name == "Fire").state.motion as AnimationClip;
        if (!oldFire || !oldWeaponFire) throw new InvalidOperationException("TPS fire states must use clips.");

        string emptyPath = folder + "/FireEmpty.anim";
        var reset = AssetDatabase.LoadAssetAtPath<AnimationClip>(emptyPath);
        if (!reset)
        {
            reset = new AnimationClip { name = "FireEmpty", frameRate = 30 };
            AssetDatabase.CreateAsset(reset, emptyPath);
        }
        // Returning to an explicit hold pose avoids a blank state retaining action
        // finger channels. Reuse the same baked idle data, with only the action flag reset.
        var idleBody=ArenaWeaponAnimationSource.Body(input,"Idle");
        EditorUtility.CopySerialized(idleBody,reset);reset.name="FireEmpty";
        AnimationUtility.SetEditorCurve(reset,
            EditorCurveBinding.FloatCurve("", typeof(Animator), "WeaponFireWeight"),
            AnimationCurve.Constant(0, 1f / 30f, 0));
        empty.motion = reset;
        // The old recoil exits after only part of its clip. A bolt cycle must
        // finish its complete authored hand motion before returning to idle.
        foreach (var transition in fire.transitions) fire.RemoveTransition(transition);
        var exit = fire.AddTransition(empty);
        exit.hasExitTime = true;
        exit.exitTime = 1;
        exit.hasFixedDuration = true;
        exit.duration = .06f;
        layer.stateMachine.defaultState = empty;
        layers[fireLayer] = layer;
        int weaponFireLayer = Array.FindIndex(layers, l => l.name == "WeaponFire");
        layers[weaponFireLayer].blendingMode = AnimatorLayerBlendingMode.Override;
        var weaponStates = layers[weaponFireLayer].stateMachine.states.Select(child => child.state).ToArray();
        var weaponAction = weaponStates.First(s => s.name == "Fire");
        var weaponEmpty = weaponStates.First(s => s.name == "Empty");
        weaponAction.speed = 1;
        weaponAction.speedParameterActive = false;
        foreach (var transition in weaponAction.transitions) weaponAction.RemoveTransition(transition);
        var weaponExit = weaponAction.AddTransition(weaponEmpty);
        weaponExit.hasExitTime = true;
        weaponExit.exitTime = 1;
        weaponExit.hasFixedDuration = true;
        weaponExit.duration = .06f;
        // Reload/Empty still writes its humanoid defaults even without an
        // active reload. Put the authored bolt action after that layer and the
        // holding pose so those earlier defaults cannot erase its hand motion.
        // All gameplay layer lookups use names, so no cached index is changed.
        var ordered = layers.ToList();
        ordered.Remove(layer);
        int holdLayer = ordered.FindIndex(l => l.name == "SourceSway");
        ordered.Insert(holdLayer + 1, layer);
        controller.layers = ordered.ToArray();
        ArenaTpsControllerAdapter.ConfigureLayers(controller);
        EditorUtility.SetDirty(controller);

        string overridePath = folder + "/FireBody.overrideController";
        var inherited = controller.animationClips.Where(c => c != reset).Distinct()
            .ToDictionary(c => c, c => ArenaWeaponAnimationSource.Resolve(input, c));
        var result = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(overridePath);
        if (!result)
        {
            result = new AnimatorOverrideController(controller) { name = "FireBody" };
            AssetDatabase.CreateAsset(result, overridePath);
        }
        else result.runtimeAnimatorController = controller;
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        result.GetOverrides(overrides);
        for (int i = 0; i < overrides.Count; i++)
        {
            var key = overrides[i].Key;
            var effective = key == oldFire ? fireBody : key == oldWeaponFire ? fireWeapon :
                key == reset ? reset : inherited.TryGetValue(key, out var value) ? value : key;
            overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(key, effective);
        }
        result.ApplyOverrides(overrides);
        definition.thirdPerson = result;
        definition.thirdPersonAuthoredFire = true;
        EditorUtility.SetDirty(result);
        EditorUtility.SetDirty(definition);
    }

    static AvatarMask FireMask(AvatarMask source, AnimationClip clip, string path)
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
        if (!mask)
        {
            mask = source ? Object.Instantiate(source) : new AvatarMask();
            mask.name = "FireArms";
            AssetDatabase.CreateAsset(mask, path);
        }
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, i == (int)AvatarMaskBodyPart.LeftArm
                || i == (int)AvatarMaskBodyPart.RightArm || i == (int)AvatarMaskBodyPart.LeftFingers
                || i == (int)AvatarMaskBodyPart.RightFingers);
        var tracks = AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(Transform))
            .Select(b => b.path).Distinct().OrderBy(p => p).ToArray();
        var paths = source ? Enumerable.Range(0, source.transformCount).Select(i => source.GetTransformPath(i)).Concat(tracks).Distinct().ToArray() : tracks;
        mask.transformCount = paths.Length;
        for (int i = 0; i < paths.Length; i++)
        {
            mask.SetTransformPath(i, paths[i]);
            mask.SetTransformActive(i, true);
        }
        EditorUtility.SetDirty(mask);
        return mask;
    }
}
