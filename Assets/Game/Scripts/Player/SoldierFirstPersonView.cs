using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-10)]
public class SoldierFirstPersonView : MonoBehaviour
{
    public Camera playerCamera;
    public Transform thirdPersonRoot;
    public Transform firstPersonRoot;
    public Animator armsAnimator;
    public Animator weaponAnimator;
    public CharacterController controller;
    public bool firstPerson = true;
    public int firstPersonLayer = 8;
    public int thirdPersonLayer = 9;

    private SoldierWeaponDefinition definition;
    public float reloadSwayWeight => definition.reloadSwayWeight;
    public float reloadWalkWeight => definition.reloadWalkWeight;
    public float locomotionWeight => definition.locomotionWeight;
    public float aimTransitionWeight => definition.aimTransitionWeight;
    public float motionSmoothingTime => definition.motionSmoothingTime;
    public float walkingSpeed => definition.walkingSpeed;
    public Vector3 viewOffset => definition.viewOffset;
    public Vector3 hipFireOffset => definition.hipFireOffset;
    public float aimFieldOfView => definition.aimFieldOfView;
    public float aimBlendSpeed => definition.aimBlendSpeed;
    public Vector3 aimOffset => definition.aimOffset;

    private Vector3 thirdPersonCameraPosition;
    private Quaternion thirdPersonCameraRotation;
    private int originalCullingMask;
    private float originalNearClip;
    private float originalFieldOfView;
    private float aimBlend;
    public float AimBlend => aimBlend;
    public float PlacementAimBlend => 1f - (1f - aimBlend) * (1f - aimBlend);
    public bool IsEquipping => loadout.IsSwitching;
    public bool WantsToAim { get; private set; }
    private SoldierMovement movement;

    private int locomotionLayer;
    private int aimTransitionLayer;
    private Renderer[] firstPersonRenderers;

    public Vector3 CameraRestPosition => firstPerson ? Vector3.zero : thirdPersonCameraPosition;
    public Quaternion CameraRestRotation => firstPerson ? Quaternion.identity : thirdPersonCameraRotation;

    private SoldierActions actions;
    private SoldierWeaponLoadout loadout;
    private bool aimRequested;
    public bool IsReloading => actions.IsReloading;

    private void Awake()
    {
        definition = GetComponent<SoldierWeaponPresentation>().weapon.definition;
        movement = GetComponent<SoldierMovement>();
        actions = GetComponent<SoldierActions>();
        loadout = GetComponent<SoldierWeaponLoadout>();
        thirdPersonCameraPosition = playerCamera.transform.localPosition;
        thirdPersonCameraRotation = playerCamera.transform.localRotation;
        originalCullingMask = playerCamera.cullingMask;
        originalNearClip = playerCamera.nearClipPlane;
        originalFieldOfView = playerCamera.fieldOfView;

        RefreshVisuals();

        locomotionLayer = armsAnimator.GetLayerIndex("Locomotion");
        aimTransitionLayer = armsAnimator.GetLayerIndex("Aim Transitions");
        SetFirstPerson(firstPerson);
    }

    public void RefreshVisuals()
    {
        // These are visibility groups, not gameplay or networking ownership.
        foreach (Transform child in thirdPersonRoot.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = thirdPersonLayer;
        foreach (Transform child in firstPersonRoot.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = firstPersonLayer;
        firstPersonRenderers = firstPersonRoot.GetComponentsInChildren<Renderer>(true);
        armsAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        weaponAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (Renderer mesh in firstPersonRenderers)
            mesh.shadowCastingMode = ShadowCastingMode.Off;

    }

    public void CancelAim()
    {
        aimBlend = 0f;
        armsAnimator.SetFloat("AimBlend", 0f);
        armsAnimator.SetBool("Aim", false);
        playerCamera.fieldOfView = originalFieldOfView;
    }

    private void Update() => UpdateAim();

    public void SetAimRequested(bool requested)
    {
        aimRequested = requested;
        WantsToAim = requested && !IsReloading && !IsEquipping;
    }

    private void UpdateAim()
    {
        WantsToAim = aimRequested && !IsReloading && !IsEquipping;
        bool aiming = WantsToAim && !movement.IsRunning;
        aimBlend = Mathf.MoveTowards(aimBlend, aiming ? 1f : 0f, aimBlendSpeed * Time.deltaTime);
        armsAnimator.SetBool("Aim", aiming);
        armsAnimator.SetFloat("AimBlend", aimBlend);
        if (firstPerson) playerCamera.fieldOfView = Mathf.Lerp(originalFieldOfView, aimFieldOfView, aimBlend);
        firstPersonRoot.localPosition = Vector3.Lerp(viewOffset + hipFireOffset, viewOffset + aimOffset, PlacementAimBlend);
    }

    private void LateUpdate() => UpdateMotion();

    private void UpdateMotion()
    {
        armsAnimator.SetFloat("TacticalSprint", definition.firstPersonSprintStyle == SoldierWeaponDefinition.SprintStyle.Tactical ? 1f : 0f);
        // Use actual movement so walking bob stops against walls and in the air.
        Vector3 velocity = controller.velocity;
        velocity.y = 0f;
        float movementAmount = controller.isGrounded ? Mathf.Clamp01(velocity.magnitude / walkingSpeed) : 0f;
        if (IsReloading) movementAmount *= reloadWalkWeight;
        armsAnimator.SetFloat("Movement", movementAmount, motionSmoothingTime, Time.deltaTime);
        float weight = IsReloading ? locomotionWeight * reloadSwayWeight : locomotionWeight;
        weight *= 1f - armsAnimator.GetFloat("RunBlend");
        armsAnimator.SetLayerWeight(locomotionLayer, Mathf.MoveTowards(
            armsAnimator.GetLayerWeight(locomotionLayer), weight, Time.deltaTime / motionSmoothingTime));
        armsAnimator.SetLayerWeight(aimTransitionLayer,
            IsReloading || IsEquipping ? 0f : aimTransitionWeight);
    }

    public void ApplyWeaponDefinition(SoldierWeaponDefinition selectedDefinition)
    {
        definition = selectedDefinition;
        armsAnimator.SetFloat("TacticalSprint", definition.firstPersonSprintStyle == SoldierWeaponDefinition.SprintStyle.Tactical ? 1f : 0f);

        aimBlend = 0f;
        locomotionLayer = armsAnimator.GetLayerIndex("Locomotion");
        aimTransitionLayer = armsAnimator.GetLayerIndex("Aim Transitions");
        armsAnimator.SetBool("Aim", false);
        armsAnimator.SetFloat("AimBlend", 0f);
        firstPersonRoot.localPosition = viewOffset + hipFireOffset;
        playerCamera.fieldOfView = originalFieldOfView;
    }

    public void SetFirstPerson(bool enabled)
    {
        firstPerson = enabled;
        playerCamera.fieldOfView = originalFieldOfView;
        firstPersonRoot.gameObject.SetActive(true);
        foreach (Renderer mesh in firstPersonRenderers) mesh.enabled = enabled;
        playerCamera.transform.localPosition = enabled ? Vector3.zero : thirdPersonCameraPosition;
        playerCamera.transform.localRotation = enabled ? Quaternion.identity : thirdPersonCameraRotation;
        playerCamera.nearClipPlane = enabled ? 0.03f : originalNearClip;
        playerCamera.cullingMask = enabled
            ? (originalCullingMask | (1 << firstPersonLayer)) & ~(1 << thirdPersonLayer)
            : originalCullingMask & ~(1 << firstPersonLayer);
    }

    public void PlayFire()
    {
        armsAnimator.ResetTrigger("Fire");
        weaponAnimator.ResetTrigger("Fire");
        armsAnimator.CrossFadeInFixedTime("Fire", definition.fireBlendTime, 0, 0f);
        weaponAnimator.Play("Fire", 0, 0f);
    }

    public void CancelReload()
    {
        armsAnimator.ResetTrigger("Reload");
        weaponAnimator.ResetTrigger("Reload");
        armsAnimator.Play("Idle", 0, 0f);
        weaponAnimator.Play("Idle", 0, 0f);
    }

    public void PlayReload()
    {

        armsAnimator.ResetTrigger("Fire");
        weaponAnimator.ResetTrigger("Fire");
        armsAnimator.SetBool("Aim", false);
        armsAnimator.SetLayerWeight(aimTransitionLayer, 0f);
        armsAnimator.SetTrigger("Reload");
        weaponAnimator.SetTrigger("Reload");
    }
}
