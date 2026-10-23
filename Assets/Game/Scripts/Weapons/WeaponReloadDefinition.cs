using System;
using UnityEngine;

// A reload uses the same gameplay clock and animation state machine in every view.
[Serializable]
public struct WeaponReloadDefinition
{
    [Min(.01f)] public float duration;
    public RuntimeAnimatorController firstPerson;
    public RuntimeAnimatorController thirdPerson;
    public RuntimeAnimatorController thirdPersonParts;
    public RoundReloadTiming roundReload;
}
