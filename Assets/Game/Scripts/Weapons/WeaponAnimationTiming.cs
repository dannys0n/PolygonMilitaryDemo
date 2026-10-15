using System.Collections.Generic;
using UnityEngine;

// Resolve the effective clip behind a shared Animator state, including weapon overrides.
public static class WeaponAnimationTiming
{
    public static AnimationClip Clip(RuntimeAnimatorController controller, string originalName)
    {
        if(controller is AnimatorOverrideController overrides)
        {
            var pairs=new List<KeyValuePair<AnimationClip,AnimationClip>>();
            overrides.GetOverrides(pairs);
            foreach(var pair in pairs)
                if(pair.Key.name==originalName)return pair.Value?pair.Value:pair.Key;
        }
        foreach(var clip in controller.animationClips)if(clip.name==originalName)return clip;
        return null;
    }
    public static float Speed(Animator animator, string originalName, float duration)
    {
        var clip=Clip(animator.runtimeAnimatorController,originalName);
        return clip?clip.length/Mathf.Max(.01f,duration):1f;
    }
}
