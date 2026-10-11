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
    public float aimFieldOfView => loadout.EquippedSights && loadout.EquippedSights.SelectedSight
        ? loadout.EquippedSights.SelectedSight.fieldOfView : definition.aimFieldOfView;
    public float AimSensitivity => firstPerson && loadout.EquippedSights && loadout.EquippedSights.SelectedSight
        ? Mathf.Lerp(1f, loadout.EquippedSights.SelectedSight.sensitivityMultiplier, aimBlend) : 1f;
    public float aimBlendSpeed => definition.aimBlendSpeed;
    public Vector3 aimOffset => definition.aimOffset;

    private Vector3 thirdPersonCameraPosition;
    private Quaternion thirdPersonCameraRotation;
    private Quaternion firstPersonRestRotation;
    private int originalCullingMask;
    private float originalNearClip;
    private float originalFieldOfView;
    private float aimBlend;
    private SoldierWeaponSights lastSights;
    private int sightRevision;
    private Vector3 adsPosition, previousAdsPosition;
    private Quaternion adsRotation=Quaternion.identity, previousAdsRotation=Quaternion.identity;
    private float sightChangeTime;
    public float AimBlend => aimBlend;
    public float ThirdPersonFieldOfView => originalFieldOfView;
#if UNITY_EDITOR
    // The placement tool can hold a pose without adding runtime equipment objects.
    public bool? EditorPlacementAim { get; set; }
    public Quaternion EditorPlacementRestRotation => firstPersonRestRotation;
#endif
    public float PlacementAimBlend => 1f - (1f - aimBlend) * (1f - aimBlend);
    public bool IsEquipping => loadout.IsSwitching;
    public bool WantsToAim { get; private set; }
    private SoldierMovement movement;

    private int locomotionLayer;
    private int aimTransitionLayer;
    private Renderer[] firstPersonRenderers;
    private SkinnedMeshRenderer[] armsRenderers;

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
        firstPersonRestRotation = firstPersonRoot.localRotation;
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
        // Editor previews can refresh before the runtime Awake sequence.
        if (!loadout) loadout = GetComponent<SoldierWeaponLoadout>();
        if (!definition) definition = GetComponent<SoldierWeaponPresentation>().weapon.definition;
        // These are visibility groups, not gameplay or networking ownership.
        foreach (Transform child in thirdPersonRoot.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = thirdPersonLayer;
        foreach (Transform child in firstPersonRoot.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = firstPersonLayer;
        firstPersonRenderers = firstPersonRoot.GetComponentsInChildren<Renderer>(true);
        var leftHand = loadout.firstPersonGripRig.leftHand.data.tip;
        var rightHand = loadout.firstPersonGripRig.rightHand.data.tip;
        armsRenderers = System.Array.FindAll(firstPersonRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true),
            mesh => System.Array.IndexOf(mesh.bones, leftHand) >= 0 || System.Array.IndexOf(mesh.bones, rightHand) >= 0);
        armsAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (loadout.HasFirstPersonPresentation) weaponAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (Renderer mesh in firstPersonRenderers)
            mesh.shadowCastingMode = ShadowCastingMode.Off;
        ApplyHandVisibility();
    }

    void ApplyHandVisibility()
    {
        foreach (var mesh in armsRenderers) mesh.forceRenderingOff = !definition.showFirstPersonHands;
        loadout.firstPersonGripRig.gripEnabled = definition.showFirstPersonHands && loadout.HasFirstPersonPresentation;
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
        WantsToAim = aimRequested && !IsReloading && !IsEquipping && !actions.IsThrowing;
        bool aiming = WantsToAim && !movement.IsRunning;
#if UNITY_EDITOR
        if (EditorPlacementAim.HasValue) aiming = EditorPlacementAim.Value;
#endif
        aimBlend = Mathf.MoveTowards(aimBlend, aiming ? 1f : 0f, aimBlendSpeed * Time.deltaTime);
        armsAnimator.SetBool("Aim", aiming);
        armsAnimator.SetFloat("AimBlend", aimBlend);
        if (firstPerson) playerCamera.fieldOfView = Mathf.Lerp(originalFieldOfView, aimFieldOfView, aimBlend);
        ApplyPlacement(PlacementAimBlend);
    }

    private void ApplyPlacement(float blend)
    {
        if (!loadout.HasFirstPersonPresentation) return;
        Quaternion rotation = firstPersonRestRotation * Quaternion.Euler(definition.viewRotationOffset)
            * Quaternion.Slerp(Quaternion.Euler(definition.hipFireRotationOffset),
                Quaternion.Euler(definition.aimRotationOffset), blend);
        // Keep the authored pivot in place while rotating the complete arms/weapon group.
        Vector3 pivot = Vector3.Scale(definition.viewRotationPivot, firstPersonRoot.localScale);
        firstPersonRoot.localPosition = Vector3.Lerp(viewOffset + hipFireOffset, viewOffset + aimOffset, blend)
            + firstPersonRestRotation * pivot - rotation * pivot;
        firstPersonRoot.localRotation = rotation;
        var sights=loadout.EquippedSights;
        if (!sights || !sights.SelectedSight) { lastSights=null; return; }
        sights.GetAdsPlacement(firstPersonRoot.localScale, out var targetPosition, out var targetRotation);
        if (lastSights != sights)
        {
            lastSights=sights; sightRevision=sights.Revision;
            previousAdsPosition=targetPosition; previousAdsRotation=targetRotation;
            sightChangeTime=Time.time-.15f;
        }
        else if (sightRevision != sights.Revision)
        {
            sightRevision=sights.Revision;
            previousAdsPosition=adsPosition; previousAdsRotation=adsRotation;
            sightChangeTime=Time.time;
        }
        float sightBlend=Mathf.SmoothStep(0f,1f,Mathf.Clamp01((Time.time-sightChangeTime)/.15f));
        adsPosition=Vector3.Lerp(previousAdsPosition,targetPosition,sightBlend);
        adsRotation=Quaternion.Slerp(previousAdsRotation,targetRotation,sightBlend);
        // Keep the existing hip placement and authored ADS transition motion.
        Quaternion hipRotation=firstPersonRestRotation*Quaternion.Euler(definition.viewRotationOffset)
            *Quaternion.Euler(definition.hipFireRotationOffset);
        Vector3 hipPosition=viewOffset+hipFireOffset+firstPersonRestRotation*pivot-hipRotation*pivot;
        firstPersonRoot.SetLocalPositionAndRotation(Vector3.Lerp(hipPosition,adsPosition,blend),
            Quaternion.Slerp(hipRotation,adsRotation,blend));
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
        if(actions.IsThrowing)weight=0;
        armsAnimator.SetLayerWeight(locomotionLayer, Mathf.MoveTowards(
            armsAnimator.GetLayerWeight(locomotionLayer), weight, Time.deltaTime / motionSmoothingTime));
        armsAnimator.SetLayerWeight(aimTransitionLayer,
            IsReloading || IsEquipping || actions.IsThrowing ? 0f : aimTransitionWeight);
    }

    public void ApplyWeaponDefinition(SoldierWeaponDefinition selectedDefinition)
    {
        definition = selectedDefinition;
        ApplyHandVisibility();
        armsAnimator.SetFloat("TacticalSprint", definition.firstPersonSprintStyle == SoldierWeaponDefinition.SprintStyle.Tactical ? 1f : 0f);

        aimBlend = 0f;
        locomotionLayer = armsAnimator.GetLayerIndex("Locomotion");
        aimTransitionLayer = armsAnimator.GetLayerIndex("Aim Transitions");
        armsAnimator.SetBool("Aim", false);
        armsAnimator.SetFloat("AimBlend", 0f);
        ApplyPlacement(0f);
        playerCamera.fieldOfView = originalFieldOfView;
    }

    public void GetThirdPersonCameraPose(out Vector3 position, out Quaternion rotation)
    {
        // Reuse the authored TPS offset while following the live look/recoil direction.
        var cameraTransform = playerCamera.transform;
        rotation = cameraTransform.rotation;
        position = cameraTransform.parent.TransformPoint(thirdPersonCameraPosition);
    }

    public void SetFirstPerson(bool enabled)
    {
        firstPerson = enabled;
        loadout.RefreshFirstPersonPresentation();
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
        if(loadout.HasFirstPersonPresentation)weaponAnimator.ResetTrigger("Fire");
        armsAnimator.CrossFadeInFixedTime("Fire", definition.fireBlendTime, 0, 0f);
        if(loadout.HasFirstPersonPresentation)weaponAnimator.Play("Fire", 0, 0f);
    }

    public void CancelReload()
    {
        armsAnimator.ResetTrigger("Reload");
        if(loadout.HasFirstPersonPresentation)weaponAnimator.ResetTrigger("Reload");
        armsAnimator.Play("Idle", 0, 0f);
        if(loadout.HasFirstPersonPresentation)weaponAnimator.Play("Idle", 0, 0f);
    }

    public void PlayReload()
    {

        armsAnimator.ResetTrigger("Fire");
        if(loadout.HasFirstPersonPresentation)weaponAnimator.ResetTrigger("Fire");
        armsAnimator.SetBool("Aim", false);
        armsAnimator.SetLayerWeight(aimTransitionLayer, 0f);
        armsAnimator.SetTrigger("Reload");
        if(loadout.HasFirstPersonPresentation)weaponAnimator.SetTrigger("Reload");
    }
}
