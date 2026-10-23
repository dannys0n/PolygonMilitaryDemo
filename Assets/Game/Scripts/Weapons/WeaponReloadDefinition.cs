using System;
using UnityEngine;

// A reload uses the same gameplay clock and animation state machine in every view.
[Serializable]
public struct WeaponReloadDefinition
{
    [Tooltip("Clip duration at its original playback rate.")]
    [Min(.01f)] public float duration;
    [Tooltip("Playback rate for this weapon. Zero in older assets means the original speed.")]
    [Min(.01f)] public float playbackSpeed;
    public float PlaybackSpeed => playbackSpeed > 0f ? playbackSpeed : 1f;
    public float Duration => Mathf.Max(.01f, duration) / PlaybackSpeed;
    public RuntimeAnimatorController firstPerson;
    public RuntimeAnimatorController thirdPerson;
    public RuntimeAnimatorController thirdPersonParts;
    public RoundReloadTiming roundReload;
}
