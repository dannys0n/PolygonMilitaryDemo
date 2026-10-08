using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(menuName = "Soldier/Weapon definition")]
public class SoldierWeaponDefinition : ScriptableObject
{
    public enum SprintStyle { Regular, Tactical }
    [Header("Animation controllers")]
    public RuntimeAnimatorController firstPerson;
    public RuntimeAnimatorController firstPersonParts;
    public RuntimeAnimatorController thirdPerson;

    [Header("Gameplay action timing")]
    [Tooltip("Gameplay reload duration in seconds. Keep presentation clips aligned with this timing.")]
    [Min(.01f)] public float reloadDuration = 3f;

    [Header("Gameplay weapon settings")]
    [Min(1)] public int magazineCapacity = 30;
    [Min(0)] public int startingReserveAmmo = 90;
    [Min(0f)] public float damage = 25f;
    [Min(.01f)] public float range = 100f;
    [Min(1f)] public float headshotMultiplier = 2f;

    [Header("Weapon switching")]
    [Min(.01f)] public float equipDuration = .5f;
    [Min(.01f)] public float unequipDuration = .5f;

    [Header("Third-person placement")]
    public Vector3 thirdPersonHipAimPosition;
    public Vector3 thirdPersonAimPosition;
    [Tooltip("Authored reload placement, kept separate from hip/ADS pose tuning.")]
    public Vector3 thirdPersonReloadPosition;

    [Header("First-person placement")]
    public Vector3 viewOffset;
    public Vector3 hipFireOffset;
    public Vector3 aimOffset;
    [Range(20f, 90f)] public float aimFieldOfView = 50f;
    [Min(.01f)] public float aimBlendSpeed = 8f;

    [Header("Animation motion")]
    public SprintStyle firstPersonSprintStyle = SprintStyle.Regular;
    [Range(0f, 1f)] public float reloadSwayWeight = .65f;
    [Range(0f, 1f)] public float reloadWalkWeight = .35f;
    [Range(0f, 1f)] public float locomotionWeight = 1f;
    [Range(0f, 1f)] public float aimTransitionWeight = 1f;
    [FormerlySerializedAs("motionBlendTime")]
    [Tooltip("Damping for movement parameters and sway weights. State blend times live in Animator transitions.")]
    [Min(.01f)] public float motionSmoothingTime = .12f;
    [Min(.01f)] public float walkingSpeed = 4f;
    [Range(0f, 1f)] public float cameraPositionWeight = 1f;
    [Range(0f, 1f)] public float cameraRotationWeight = 1f;

    [Header("Firing")]
    [Tooltip("Blend into the recoil pose. Use zero for an immediate shot with synchronized moving weapon parts.")]
    [Min(0f)] public float fireBlendTime = .03f;
    public SoldierActions.FireMode fireMode;
    [Min(1f)] public float roundsPerMinute = 600f;
}
