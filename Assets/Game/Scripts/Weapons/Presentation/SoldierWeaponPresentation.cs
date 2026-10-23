using UnityEngine;
using UnityEngine.Animations.Rigging;

[DefaultExecutionOrder(100)]
public class SoldierWeaponPresentation : MonoBehaviour
{
    public SoldierActions actions;
    public Animator characterAnimator;
    public SoldierPhysicsBody controller;
    public SoldierFirstPersonView view;
    public SoldierWeapon weapon;
    public Transform aimPivot;
    public Transform weaponRoot;
    public Animator partsAnimator;
    public Transform aimTarget;
    public MultiAimConstraint chestAim;
    public MultiAimConstraint weaponAim;
    public TwoBoneIKConstraint supportHand;
    public TwoBoneIKConstraint firingHand;
    public MultiParentConstraint runWeaponFollow;
    [Header("Remote presentation detail")]
    [Min(0)] public float fullDetailDistance = 50f;
    [Min(1)] public float simpleDetailDistance = 70f;
    [Min(0)] public float firingPoseHold = .35f;

    int characterSwayLayer, weaponPoseLayer, weaponSwayLayer, weaponFireLayer, sprintBodyLayer, sprintArmsLayer, bodyFireLayer, bodyReloadLayer, bodyHitLayer, weaponSwitchLayer;
    static readonly int FireActionWeight = Animator.StringToHash("WeaponFireWeight");
    static readonly int FireState = Animator.StringToHash("Fire");
    static readonly int ReloadState = Animator.StringToHash("Reload");
    static readonly int HitState = Animator.StringToHash("HitReaction");
    static readonly int EquipState = Animator.StringToHash("Equip");
    static readonly int UnequipState = Animator.StringToHash("Unequip");
    static readonly int EmptyState = Animator.StringToHash("Empty");

    SoldierGrenades grenades;
    SoldierController soldier;
    float fullPoseUntil, cosmeticDetail = 1f, nextDetailRefresh;
    float supportElbowHintWeight, firingElbowHintWeight;
    Rig aimRig;
    RigBuilder rigBuilder;
    RigLayer aimLayer;
    void Awake()
    {
        grenades=GetComponent<SoldierGrenades>();
        soldier=GetComponent<SoldierController>();
        aimRig=chestAim.GetComponentInParent<Rig>(true);
        rigBuilder=characterAnimator.GetComponent<RigBuilder>();
        aimLayer=rigBuilder.layers.Find(layer=>layer.rig==aimRig);
        supportElbowHintWeight = supportHand.data.hintWeight;
        firingElbowHintWeight = firingHand.data.hintWeight;
        ApplyWeapon(weapon);
        actions.FireStarted += PlayFire;
        actions.ReloadStarted += PlayReload;
        actions.ReloadCanceled += CancelReload;
        actions.ReloadSectionChanged += SeekReload;
        actions.ReloadCompleted += FinishReload;
    }

    public void ApplyWeapon(SoldierWeapon selectedWeapon)
    {
        weapon = selectedWeapon;
        SoldierWeaponDefinition set = weapon.definition;
        // Converted clips supply the elbow bend. Keep hand IK for grip contact,
        // but do not pull that bend toward hints calibrated for the older poses.
        SetElbowHintWeight(supportHand, set.thirdPersonAnimatedGrips ? 0f : supportElbowHintWeight);
        SetElbowHintWeight(firingHand, set.thirdPersonAnimatedGrips ? 0f : firingElbowHintWeight);
        view.armsAnimator.runtimeAnimatorController = set.firstPerson;
        if (GetComponent<SoldierWeaponLoadout>().HasFirstPersonPresentation)
        {
            var fp=GetComponent<SoldierWeaponLoadout>().FirstPersonView;
            view.weaponAnimator.runtimeAnimatorController = fp.animationOverride ? fp.animationOverride : set.firstPersonParts;
            view.weaponAnimator.Rebind();
        }
        characterAnimator.runtimeAnimatorController = set.thirdPerson;
        partsAnimator.runtimeAnimatorController = set.thirdPersonParts ? set.thirdPersonParts : set.firstPersonParts;
        partsAnimator.Rebind();
        view.ApplyWeaponDefinition(set);
        characterSwayLayer = characterAnimator.GetLayerIndex("SourceSway");
        weaponPoseLayer = characterAnimator.GetLayerIndex("WeaponPose");
        weaponSwayLayer = characterAnimator.GetLayerIndex("WeaponSway");
        weaponFireLayer = characterAnimator.GetLayerIndex("WeaponFire");
        bodyFireLayer = characterAnimator.GetLayerIndex("Fire");
        bodyReloadLayer = characterAnimator.GetLayerIndex("Reload");
        bodyHitLayer = characterAnimator.GetLayerIndex("HitReaction");
        weaponSwitchLayer = characterAnimator.GetLayerIndex("WeaponSwitch");
        if (set.thirdPersonAnimatedGrips)
        {
            if (bodyReloadLayer >= 0) characterAnimator.SetLayerWeight(bodyReloadLayer, 0f);
            if (bodyHitLayer >= 0) characterAnimator.SetLayerWeight(bodyHitLayer, 0f);
            if (weaponSwitchLayer >= 0) characterAnimator.SetLayerWeight(weaponSwitchLayer, 0f);
            if (set.thirdPersonAuthoredFire && bodyFireLayer >= 0) characterAnimator.SetLayerWeight(bodyFireLayer, 0f);
        }
        if (set.thirdPersonAuthoredFire && bodyFireLayer >= 0)
        {
            if (!characterAnimator.IsParameterControlledByCurve(FireActionWeight))
                characterAnimator.SetFloat(FireActionWeight, 0f);
            characterAnimator.Play(EmptyState, bodyFireLayer, 0f);
        }
        sprintBodyLayer = characterAnimator.GetLayerIndex("SprintBody");
        sprintArmsLayer = characterAnimator.GetLayerIndex("SprintArms");
        // A new controller/weapon needs its initial authored pose before it can
        // be culled. Mounted seating keeps its separate calibration ownership.
        fullPoseUntil = Time.time + firingPoseHold;
        nextDetailRefresh = 0f;
        UpdatePresentationQuality();
    }

    static void SetElbowHintWeight(TwoBoneIKConstraint constraint, float weight)
    {
        var data = constraint.data;
        data.hintWeight = weight;
        constraint.data = data;
    }

    void OnDestroy()
    {
        actions.FireStarted -= PlayFire;
        actions.ReloadStarted -= PlayReload;
        actions.ReloadCanceled -= CancelReload;
        actions.ReloadSectionChanged -= SeekReload;
        actions.ReloadCompleted -= FinishReload;
    }

    void Update()
    {
        UpdatePresentationQuality();
        float reloadWeight = characterAnimator.GetFloat("WeaponReloadWeight");
        float fireWeight = AuthoredFireWeight();
        if (fireWeight > 0f) fullPoseUntil = Time.time + firingPoseHold;
        UpdateRigWeights(reloadWeight, fireWeight);
        UpdateMovementMotion();
        UpdateWeaponLayers(reloadWeight, fireWeight);
        UpdateWeaponPlacement(reloadWeight);
    }

    void UpdatePresentationQuality()
    {
        bool viewer = soldier && soldier.IsLocallyControlled || view.NeedsCameraAnimation;
        // Muzzles, hand release points and hit regions use this skeleton too.
        // Keep combat poses current even when the character is outside the view.
        bool fullPose = viewer || view.WantsToAim || actions.IsReloading
            || grenades && grenades.IsThrowing || Time.time < fullPoseUntil;
        var culling = fullPose ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullUpdateTransforms;
        if(characterAnimator.cullingMode != culling)characterAnimator.cullingMode = culling;
        if(partsAnimator.cullingMode != culling)partsAnimator.cullingMode = culling;
        if(viewer)cosmeticDetail = 1f;
        else if(Time.unscaledTime >= nextDetailRefresh)
        {
            nextDetailRefresh = Time.unscaledTime + .2f;
            cosmeticDetail = FootIkViewDistance.Weight(transform.position, fullDetailDistance,
                Mathf.Max(fullDetailDistance + 1f, simpleDetailDistance));
        }
    }

    float AuthoredFireWeight()
    {
        if (!weapon.definition.thirdPersonAuthoredFire || bodyFireLayer < 0) return 0f;
        // Empty states and weapon switches must never retain an action-blend value
        // left by a clip that was interrupted before its final key.
        return ActionActive(bodyFireLayer, FireState) ? Mathf.Clamp01(characterAnimator.GetFloat(FireActionWeight)) : 0f;
    }

    bool ActionActive(int layer, int state)
    {
        if (layer < 0) return false;
        return characterAnimator.GetCurrentAnimatorStateInfo(layer).shortNameHash == state ||
            characterAnimator.IsInTransition(layer) && characterAnimator.GetNextAnimatorStateInfo(layer).shortNameHash == state;
    }

    void UpdateRigWeights(float weight, float fireWeight)
    {
        float throwing=grenades && grenades.IsThrowing?1f:0f;
        aimRig.weight=1f-throwing;
        bool rigActive=throwing==0f;
        bool rigStateChanged=aimLayer.active!=rigActive;
        aimLayer.active=rigActive;
        float sprint = Mathf.Clamp01(characterAnimator.GetFloat("RunBlend"));
        // Animator damping approaches the target asymptotically. Finish the blend
        // exactly so a tiny remaining run value cannot keep IK disabled.
        if (sprint < .001f) sprint = 0f;
        else if (sprint > .999f) sprint = 1f;
        sprint *= (1f - weight) * (1f - throwing);
        characterAnimator.SetLayerWeight(sprintBodyLayer, sprint);
        characterAnimator.SetLayerWeight(sprintArmsLayer, sprint);
        // Reloads may own the torso. Bolt/pump cycling owns only the arms and hands,
        // so aiming must keep its torso pose instead of snapping back to the action baseline.
        chestAim.weight = (1f-weight) * (1f-sprint) * (1f-throwing);
        // Authored weapon actions animate the existing weapon-relative grip targets.
        // Keeping IK active makes the hand path follow the player's aiming frame.
        firingHand.weight = weapon.definition.thirdPersonAnimatedGrips ? (1f-sprint)*(1f-throwing) : chestAim.weight;
        supportHand.weight = (weapon.definition.thirdPersonAnimatedGrips ? 1f : 1f-weight) * (1f-throwing);
        weaponAim.weight = (weapon.definition.thirdPersonAnimatedGrips ? 1f : 1f-weight) * (1f - throwing);
        runWeaponFollow.weight = sprint;
        if(rigStateChanged)rigBuilder.SyncLayers();
    }

    void UpdateMovementMotion()
    {
        Vector3 velocity = controller.velocity;
        velocity.y = 0f;
        float speed = controller.isGrounded ? Mathf.Clamp01(velocity.magnitude / view.walkingSpeed) : 0f;
        characterAnimator.SetFloat("MotionSpeed", speed, .12f, Time.deltaTime);

    }

    void UpdateWeaponLayers(float weight, float fireWeight)
    {
        // The same curve returns the weapon to its hold pose and the hands
        // to their grips. Animator blends the clips before the rig solves IK.
        bool authoredGrips = weapon.definition.thirdPersonAnimatedGrips;
        if (authoredGrips)
        {
            // A Humanoid layer's empty state can still output default muscles.
            // Give it ownership only while its action is running or blending out.
            if (bodyReloadLayer >= 0) characterAnimator.SetLayerWeight(bodyReloadLayer, ActionActive(bodyReloadLayer, ReloadState) ? 1f : 0f);
            if (weaponSwitchLayer >= 0) characterAnimator.SetLayerWeight(weaponSwitchLayer,
                ActionActive(weaponSwitchLayer, EquipState) || ActionActive(weaponSwitchLayer, UnequipState) ? 1f : 0f);
            if (bodyHitLayer >= 0) characterAnimator.SetLayerWeight(bodyHitLayer, ActionActive(bodyHitLayer, HitState) ? 1f : 0f);
            if (weapon.definition.thirdPersonAuthoredFire && bodyFireLayer >= 0)
                characterAnimator.SetLayerWeight(bodyFireLayer, ActionActive(bodyFireLayer, FireState) ? 1f : 0f);
        }
        characterAnimator.SetLayerWeight(characterSwayLayer, authoredGrips ? 1f : (1f - weight) * cosmeticDetail);
        characterAnimator.SetLayerWeight(weaponPoseLayer, weight);
        characterAnimator.SetLayerWeight(weaponSwayLayer, (1f - weight) * (1f - fireWeight) * cosmeticDetail);
        characterAnimator.SetLayerWeight(weaponFireLayer, (1f - weight) *
            (weapon.definition.thirdPersonAuthoredFire ? fireWeight : 1f));
    }

    void UpdateWeaponPlacement(float weight)
    {
        SoldierWeaponDefinition set = weapon.definition;
        // Share the first-person placement blend and its exact duration.
        float aimBlend = view.PlacementAimBlend;
        Vector3 holdPosition = Vector3.Lerp(set.thirdPersonHipAimPosition,
            set.thirdPersonAimPosition, aimBlend);
        // Converted clips already contain the complete reload motion. Applying the
        // legacy reload offset again pushes their calibrated hand targets out of reach.
        aimPivot.localPosition = set.thirdPersonAnimatedGrips ? holdPosition
            : Vector3.Lerp(holdPosition, set.thirdPersonReloadPosition, weight);
        // The rig aims this pivot after the current spine pose is evaluated,
        // before weapon following and hand IK. Reload uses its authored frame.
        aimPivot.localRotation = Quaternion.identity;
    }
    void PlayFire()
    {
        fullPoseUntil = Time.time + firingPoseHold;
        UpdatePresentationQuality();
        if (weapon.definition.thirdPersonAuthoredFire && bodyFireLayer >= 0)
        {
            characterAnimator.ResetTrigger("Fire");
            characterAnimator.CrossFadeInFixedTime(FireState, weapon.definition.fireBlendTime, bodyFireLayer, 0f);
        }
        characterAnimator.ResetTrigger("WeaponFire");
        characterAnimator.CrossFadeInFixedTime("Fire", weapon.definition.fireBlendTime, weaponFireLayer, 0f);
        partsAnimator.ResetTrigger("Fire");
        partsAnimator.Play("Fire", 0, 0f);
    }

    public void ConfigureReload(WeaponReloadDefinition reload)
    {
        // Reload variants share their base state machines, so switching overrides
        // preserves locomotion, aiming and the current action state.
        var definition = weapon.definition;
        SetController(view.armsAnimator, reload.firstPerson ? reload.firstPerson : definition.firstPerson);
        var loadout = GetComponent<SoldierWeaponLoadout>();
        if (loadout.HasFirstPersonPresentation)
        {
            var controller = loadout.FirstPersonView.ReloadAnimation(actions.IsFullReload);
            SetController(view.weaponAnimator, controller ? controller : definition.firstPersonParts);
        }
        SetController(characterAnimator, reload.thirdPerson ? reload.thirdPerson : definition.thirdPerson);
        SetController(partsAnimator, reload.thirdPersonParts ? reload.thirdPersonParts
            : definition.thirdPersonParts ? definition.thirdPersonParts : definition.firstPersonParts);
    }

    static void SetController(Animator target, RuntimeAnimatorController controller)
    {
        if (target && controller && target.runtimeAnimatorController != controller)
            target.runtimeAnimatorController = controller;
    }

    void FinishReload()
    {
        if (actions.CurrentReload.roundReload.IsValid) CancelReload();
    }

    void SeekReload(float normalizedTime)
    {
        Seek(view.armsAnimator, "Base Layer", normalizedTime, "Reload");
        if (GetComponent<SoldierWeaponLoadout>().HasFirstPersonPresentation)
            Seek(view.weaponAnimator, "Base Layer", normalizedTime, "Reload");
        Seek(characterAnimator, "Reload", normalizedTime, "Reload");
        Seek(characterAnimator, "WeaponPose", normalizedTime, "WeaponReload");
        Seek(partsAnimator, "Base Layer", normalizedTime, "Reload");
    }

    static void Seek(Animator target, string layerName, float normalizedTime, string trigger)
    {
        if (!target || !target.runtimeAnimatorController || !target.isInitialized) return;
        int layer = target.GetLayerIndex(layerName);
        if (layer < 0) return;
        int state = Animator.StringToHash(layerName + ".Reload");
        if (!target.HasState(layer, state)) return;
        target.ResetTrigger(trigger);
        if (normalizedTime <= 0f) target.Play(state, layer, 0f);
        else
        {
            // Converted full montages can have slightly different boundary poses.
            // Blend section jumps using the same offset for hands and mechanisms.
            float length = target.GetCurrentAnimatorStateInfo(layer).length;
            target.CrossFadeInFixedTime(state, .08f, layer, normalizedTime * length);
        }
    }

    void CancelReload()
    {
        characterAnimator.ResetTrigger("WeaponReload");
        if (!characterAnimator.IsParameterControlledByCurve("WeaponReloadWeight"))
            characterAnimator.SetFloat("WeaponReloadWeight", 0f);
        characterAnimator.Play("Idle", weaponPoseLayer, 0f);
        partsAnimator.ResetTrigger("Reload");
        partsAnimator.Play("Idle", 0, 0f);
    }

    void PlayReload()
    {
        UpdatePresentationQuality();
        if(actions.IsSwapReload)return;
        if (weapon.definition.thirdPersonAuthoredFire && bodyFireLayer >= 0)
            characterAnimator.Play(EmptyState, bodyFireLayer, 0f);
        characterAnimator.ResetTrigger("WeaponFire");
        characterAnimator.SetTrigger("CancelWeaponFire");
        characterAnimator.SetTrigger("WeaponReload");
        partsAnimator.ResetTrigger("Fire");
        partsAnimator.SetTrigger("Reload");
    }
}
