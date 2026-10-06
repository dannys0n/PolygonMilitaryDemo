using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(menuName = "Soldier/Weapon definition")]
public class SoldierWeaponDefinition : ScriptableObject
{
    [Header("Animation controllers")]
    public RuntimeAnimatorController firstPerson;
    public RuntimeAnimatorController firstPersonParts;
    public RuntimeAnimatorController thirdPerson;

    [Header("Weapon switching")]
    [Min(.01f)] public float equipDuration = .5f;
    [Min(.01f)] public float unequipDuration = .5f;

    [Header("Third-person placement")]
    public Vector3 thirdPersonHipAimPosition;
    public Vector3 thirdPersonAimPosition;

    [Header("First-person placement")]
    public Vector3 viewOffset;
    public Vector3 hipFireOffset;
    public Vector3 aimOffset;
    [Range(20f, 90f)] public float aimFieldOfView = 50f;
    [Min(.01f)] public float aimBlendSpeed = 8f;

    [Header("Animation motion")]
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
    public SoldierActions.FireMode fireMode;
    [Min(1f)] public float roundsPerMinute = 600f;
}
