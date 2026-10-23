using UnityEngine;

// Local references inside one spawnable FPS or TPS weapon presentation.
public class SoldierWeaponView : MonoBehaviour
{
    [System.Serializable] public class AttachmentSocket
    {
        public WeaponAttachmentDefinition.MountKind kind;
        public Transform socket;
        public Renderer[] builtIn;
    }
    [Tooltip("Loaded rocket model, hidden after firing and restored when reloading.")]
    public GameObject loadedProjectile;
    public Animator animator;
    [Tooltip("Optional authored FPS rig controller. The ordinary weapon and gameplay controllers remain shared.")]
    public RuntimeAnimatorController animationOverride;
    [Tooltip("Optional empty-reload override. Kept on the FPS view so TPS-only displays do not load its arm animations.")]
    public RuntimeAnimatorController fullReloadAnimationOverride;
    [Tooltip("Optional partial-reload override; otherwise uses the ordinary animation controller.")]
    public RuntimeAnimatorController tacticalReloadAnimationOverride;
    public RuntimeAnimatorController ReloadAnimation(bool empty)
    {
        var selected = empty ? fullReloadAnimationOverride : tacticalReloadAnimationOverride;
        return selected ? selected : animationOverride;
    }

    [Tooltip("Optional shared player-owned FPS rig. This prefab then contains only the weapon.")]
    public GameObject sharedFirstPersonRig;
    [Tooltip("Weapon parent relative to the shared rig root; blank uses its default mount.")]
    public string sharedWeaponMountPath;
    [Tooltip("An authored arms/weapon rig uses its own frame instead of following the legacy arms weapon bone.")]
    public bool independentFirstPersonFrame;
    public AuthoredGrenadeView grenadeView;
    public string mountName;
    public Vector3 mountPosition, mountScale=Vector3.one;
    public Quaternion mountRotation=Quaternion.identity;
    public SoldierWeaponSockets effects;
    public Transform opticMount, rearSight, frontSight;
    [Tooltip("Animated weapon model frame for sight calibration when arms and gun share one authored rig.")]
    public Transform sightPlacementRoot;
    [Tooltip("Authored receiver/grip reference for animation conversion. Independent of sights and wrapper origins.")]
    public Transform gripFrame;
    public AttachmentSocket[] attachments;
}
