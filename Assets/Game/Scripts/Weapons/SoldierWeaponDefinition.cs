using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(menuName = "Soldier/Weapon definition")]
public class SoldierWeaponDefinition : ScriptableObject
{
    public enum WeaponCategory { Rifle, SMG, LMG, Shotgun, Sniper, Pistol, Launcher, Melee, Explosive }
    [Header("Catalog")]
    public string catalogId,displayName;
    public WeaponCategory category;
    [Tooltip("May occupy a utility equipment slot while retaining its normal weapon animations, sights and ammunition.")]
    public bool isUtility;
    public bool infiniteAmmunition;
    [Tooltip("Initial attachments for a newly equipped weapon. An explicit player selection can remove or replace them.")]
    public WeaponAttachmentDefinition[] defaultAttachments = System.Array.Empty<WeaponAttachmentDefinition>();
    public VehicleRocket rocketProjectile;
    [Tooltip("Use a supplied launcher reload clip instead of the single-shot swap fallback.")]
    public bool useAuthoredReload;
    public bool UsesSwapReload => rocketProjectile && magazineCapacity == 1 && !useAuthoredReload;
    public GrenadeDefinition thrownExplosive;
    [Tooltip("Half-angle of the shotgun spread cone. Damage is a single radius check per target, not individual pellet traces.")]
    [Range(0,20)] public float spreadDegrees;
    public bool UsesSpread => category == WeaponCategory.Shotgun && spreadDegrees > 0f;
    [Header("UI")]
    public Sprite icon;
    public enum SprintStyle { Regular, Tactical }
    [Header("Animation controllers")]
    public RuntimeAnimatorController firstPerson;
    public RuntimeAnimatorController firstPersonParts;
    public RuntimeAnimatorController thirdPerson;
    [Tooltip("Optional TPS weapon-parts controller; falls back to the shared parts controller.")]
    public RuntimeAnimatorController thirdPersonParts;
    [Tooltip("Play an authored TPS hand action during firing, such as cycling a bolt. Ordinary recoil keeps its existing additive animation and grip IK.")]
    public bool thirdPersonAuthoredFire;
    [Tooltip("Authored weapon actions animate the existing hand IK targets in weapon space.")]
    public bool thirdPersonAnimatedGrips;
    [Tooltip("Render FPS arms and apply their grip IK. Weapon animation stays active when disabled.")]
    public bool showFirstPersonHands = true;

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
    [Tooltip("Camera distance behind this weapon's built-in rear sight. Optics and rear-iron attachments use their own eye distance.")]
    [Min(.03f)] public float builtInIronEyeDistance = .25f;
    public Vector3 viewOffset;
    public Vector3 hipFireOffset;
    public Vector3 aimOffset;
    [Tooltip("Euler angles in degrees, applied to the complete first-person arms and weapon view.")]
    public Vector3 viewRotationOffset;
    [Tooltip("Fixed rotation pivot in FirstPersonView local coordinates. Place it near the weapon grip to avoid swinging the gun around the view origin.")]
    public Vector3 viewRotationPivot;
    public Vector3 hipFireRotationOffset;
    public Vector3 aimRotationOffset;
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

    [Header("Firing effects")]
    public ParticleSystem muzzleFlash;
    [Min(1)] public int flashParticlesCount = 5;
    public Light muzzleFlashLight;
    [Min(0f)] public float flashLightDuration = .035f;
    public SoldierShellCasing shellCasing;
    public Vector3 flashLightOffset = new Vector3(0f, 0f, -.05f);

    [Header("Firing")]
    [Tooltip("Blend into the recoil pose. Use zero for an immediate shot with synchronized moving weapon parts.")]
    [Min(0f)] public float fireBlendTime = .03f;
    public SoldierActions.FireMode fireMode;
    [Min(1f)] public float roundsPerMinute = 600f;
}
