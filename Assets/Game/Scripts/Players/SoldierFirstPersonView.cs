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
    public SoldierPhysicsBody controller;
    public bool firstPerson = true;
    public int firstPersonLayer = 8;
    public int thirdPersonLayer = 9;

    [Header("First-person jump feedback")]
    [Min(0f)] public float jumpArmsPitch = 2f;
    [Min(0f)] public float jumpArmsLift = .008f;
    [Min(.01f)] public float jumpRiseTime = .20f;
    [Range(0f,1f)] public float jumpAimWeight = .25f;
    float jumpStarted = float.NegativeInfinity;
    float jumpLanded = float.PositiveInfinity, jumpLandingWeight;

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
        ? loadout.EquippedSights.FieldOfView : definition.aimFieldOfView;
    public float AimSensitivity => firstPerson && loadout.EquippedSights && loadout.EquippedSights.SelectedSight
        ? Mathf.Lerp(1f, loadout.EquippedSights.SensitivityMultiplier, aimBlend) : 1f;
    public float aimBlendSpeed => definition.aimBlendSpeed;
    public Vector3 aimOffset => definition.aimOffset;

    [Header("Third person camera")]
    public Vector3 thirdPersonCameraPosition = new Vector3(.63f, 0f, -1.091f);
    public Vector3 thirdPersonCameraAngles = new Vector3(5.702f, -6.186f, 0f);
    private Quaternion thirdPersonCameraRotation => Quaternion.Euler(thirdPersonCameraAngles);
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
    private SoldierLife life;
    private UnityEngine.Animations.Rigging.RigBuilder armsRig;
    private SoldierWeaponPresentation presentation;
    private bool cameraAnimationSuspended;
    private bool savedArmsEnabled, savedRigEnabled, savedGripEnabled;
    // TPS still consumes AimBlend, but an unobserved soldier needs no FPS graph.
    public bool NeedsCameraAnimation => playerCamera && playerCamera.gameObject.activeInHierarchy
        && (playerCamera.enabled || playerCamera.targetTexture)
        && (!life || !life.health.IsDead);

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
        life = GetComponent<SoldierLife>();
        armsRig = armsAnimator.GetComponent<UnityEngine.Animations.Rigging.RigBuilder>();
        presentation = GetComponent<SoldierWeaponPresentation>();
        actions = GetComponent<SoldierActions>();
        loadout = GetComponent<SoldierWeaponLoadout>();
        firstPersonRestRotation = firstPersonRoot.localRotation;
        originalCullingMask = playerCamera.cullingMask;
        originalNearClip = playerCamera.nearClipPlane;
        originalFieldOfView = playerCamera.fieldOfView;

        RefreshVisuals();

        locomotionLayer = armsAnimator.GetLayerIndex("Locomotion");
        aimTransitionLayer = armsAnimator.GetLayerIndex("Aim Transitions");
        SetFirstPerson(firstPerson);
    }

    void OnEnable() => GetComponent<SoldierMovement>().Jumped += OnJump;
    void OnDisable()
    {
        GetComponent<SoldierMovement>().Jumped -= OnJump;
        jumpStarted = float.NegativeInfinity;
    }
    void OnJump()
    {
        if(firstPerson && NeedsCameraAnimation)
        {
            jumpStarted=Time.time; jumpLanded=float.PositiveInfinity;
        }
    }
    void ApplyJumpFeedback()
    {
        if(!firstPerson || !NeedsCameraAnimation || !loadout.HasFirstPersonPresentation)return;
        float age=Time.time-jumpStarted;
        if(float.IsPositiveInfinity(age) || age<0f)return;
        float envelope=1f-Mathf.Pow(1f-Mathf.Clamp01(age/jumpRiseTime),3f);
        if(float.IsPositiveInfinity(jumpLanded) && controller.isGrounded)
        {
            jumpLanded=Time.time;
            jumpLandingWeight=envelope;
        }
        // Finish the rise and hold it in the air; only ground contact starts recovery.
        if(!float.IsPositiveInfinity(jumpLanded))
        {
            float landedFor=Time.time-jumpLanded;
            if(landedFor>=jumpRiseTime){jumpStarted=float.NegativeInfinity;return;}
            envelope=jumpLandingWeight*Mathf.Pow(1f-Mathf.Clamp01(landedFor/jumpRiseTime),3f);
        }
        float weight=envelope*Mathf.Lerp(1f,jumpAimWeight,PlacementAimBlend);
        if(actions.IsThrowing)weight=0f;
        var root=firstPersonRoot;
        var muzzle=loadout.EquippedMuzzle;
        // Pivot the coherent arms/weapon group near the barrel: the rear rises
        // more than the muzzle, without separating the hands from their grips.
        Vector3 pivot=muzzle?root.parent.InverseTransformPoint(muzzle.position)
            :root.localPosition+root.localRotation*Vector3.forward*.65f;
        Quaternion impulse=Quaternion.AngleAxis(jumpArmsPitch*weight,Vector3.right);
        root.localPosition=pivot+impulse*(root.localPosition-pivot)+Vector3.up*(jumpArmsLift*weight);
        root.localRotation=impulse*root.localRotation;
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

    private void Update()
    {
        RefreshCameraAnimation();
        UpdateAim();
    }

    void RefreshCameraAnimation()
    {
        // Death and seated poses own their enable/restore state.
        if (life && life.health.IsDead) return;
        bool wanted = NeedsCameraAnimation;
        var grip = loadout.firstPersonGripRig;
        if (!wanted && !cameraAnimationSuspended)
        {
            savedArmsEnabled = armsAnimator.enabled;
            savedRigEnabled = armsRig && armsRig.enabled;
            savedGripEnabled = grip && grip.enabled;
            armsAnimator.enabled = false;
            if (armsRig) armsRig.enabled = false;
            if (grip) grip.enabled = false;
            cameraAnimationSuspended = true;
        }
        else if (wanted && cameraAnimationSuspended)
        {
            armsAnimator.enabled = savedArmsEnabled;
            if (armsRig) armsRig.enabled = savedRigEnabled;
            if (grip) grip.enabled = savedGripEnabled;
            cameraAnimationSuspended = false;
            if (armsAnimator.enabled) SynchronizeCameraAnimation();
        }
    }

    void SynchronizeCameraAnimation()
    {
        // Paused FPS graphs must not replay old triggers when observation changes.
        armsAnimator.ResetTrigger("Fire");
        armsAnimator.ResetTrigger("Reload");
        var body = presentation.characterAnimator;
        armsAnimator.SetFloat("RunBlend", body.GetFloat("RunBlend"));
        armsAnimator.SetFloat("WeaponSwitchSpeed", body.GetFloat("WeaponSwitchSpeed"));
        var parts = presentation.partsAnimator;
        var state = CurrentState(parts, 0);
        string baseState = state.shortNameHash == Animator.StringToHash("Reload") ? "Reload"
            : state.shortNameHash == Animator.StringToHash("Fire") ? "Fire" : "Idle";
        armsAnimator.Play("Base Layer." + baseState, 0, state.normalizedTime);
        SynchronizeLayer(body, "WeaponSwitch", "Empty", "Equip", "Unequip");
        SynchronizeLayer(body, "Grenade", "Empty", "Throw");
    }

    static AnimatorStateInfo CurrentState(Animator source, int layer)
        => source.IsInTransition(layer) ? source.GetNextAnimatorStateInfo(layer) : source.GetCurrentAnimatorStateInfo(layer);

    void SynchronizeLayer(Animator source, string layerName, params string[] states)
    {
        int sourceLayer = source.GetLayerIndex(layerName), targetLayer = armsAnimator.GetLayerIndex(layerName);
        if (sourceLayer < 0 || targetLayer < 0) return;
        var state = CurrentState(source, sourceLayer);
        string selected = states[0];
        foreach (var candidate in states)
            if (state.shortNameHash == Animator.StringToHash(candidate)) { selected = candidate; break; }
        armsAnimator.SetLayerWeight(targetLayer, source.GetLayerWeight(sourceLayer));
        armsAnimator.Play(layerName + "." + selected, targetLayer, state.normalizedTime);
    }

    public void SetAimRequested(bool requested)
    {
        aimRequested = requested;
        if (requested) actions.RequestReloadStop();
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
        if (NeedsCameraAnimation)
        {
            armsAnimator.SetBool("Aim", aiming);
            armsAnimator.SetFloat("AimBlend", aimBlend);
        }
        if (firstPerson) playerCamera.fieldOfView = Mathf.Lerp(originalFieldOfView, aimFieldOfView, aimBlend);
        ApplyPlacement(PlacementAimBlend);
        ApplyJumpFeedback();
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
        if (!NeedsCameraAnimation) return;
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
        jumpStarted = float.NegativeInfinity;
        // ADS placement speed does not retime the authored additive arm gesture.
        // Compressing both clips to the placement duration made aim-out especially abrupt.
        armsAnimator.SetFloat("AimInSpeed",1f);
        armsAnimator.SetFloat("AimOutSpeed",1f);
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
        rotation = firstPerson
            ? cameraTransform.parent.rotation * thirdPersonCameraRotation * cameraTransform.localRotation
            : cameraTransform.rotation;
        position = cameraTransform.parent.TransformPoint(thirdPersonCameraPosition);
    }

    public void SetFirstPerson(bool enabled) => ApplyPerspective(enabled, true);

    // Observer shutdown restores camera/visibility state without rebuilding an
    // equipment presentation while its scene hierarchy is being destroyed.
    public void RestoreCameraPerspective(bool enabled) => ApplyPerspective(enabled, false);

    void ApplyPerspective(bool enabled, bool refreshEquipment)
    {
        firstPerson = enabled;
        if(refreshEquipment)loadout.RefreshFirstPersonPresentation();
        playerCamera.fieldOfView = originalFieldOfView;
        firstPersonRoot.gameObject.SetActive(true);
        foreach (Renderer mesh in firstPersonRenderers) if(mesh)mesh.enabled = enabled;
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
