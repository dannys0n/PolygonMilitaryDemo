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

    int characterSwayLayer, weaponPoseLayer, weaponSwayLayer, weaponFireLayer, sprintBodyLayer, sprintArmsLayer;

    SoldierGrenades grenades;
    SoldierController soldier;
    float fullPoseUntil, cosmeticDetail = 1f, nextDetailRefresh;
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
        ApplyWeapon(weapon);
        actions.FireStarted += PlayFire;
        actions.ReloadStarted += PlayReload;
        actions.ReloadCanceled += CancelReload;
    }

    public void ApplyWeapon(SoldierWeapon selectedWeapon)
    {
        weapon = selectedWeapon;
        SoldierWeaponDefinition set = weapon.definition;
        view.armsAnimator.runtimeAnimatorController = set.firstPerson;
        if (GetComponent<SoldierWeaponLoadout>().HasFirstPersonPresentation)
        {
            view.weaponAnimator.runtimeAnimatorController = set.firstPersonParts;
            view.weaponAnimator.Rebind();
        }
        characterAnimator.runtimeAnimatorController = set.thirdPerson;
        partsAnimator.runtimeAnimatorController = set.firstPersonParts;
        partsAnimator.Rebind();
        view.ApplyWeaponDefinition(set);
        characterSwayLayer = characterAnimator.GetLayerIndex("SourceSway");
        weaponPoseLayer = characterAnimator.GetLayerIndex("WeaponPose");
        weaponSwayLayer = characterAnimator.GetLayerIndex("WeaponSway");
        weaponFireLayer = characterAnimator.GetLayerIndex("WeaponFire");
        sprintBodyLayer = characterAnimator.GetLayerIndex("SprintBody");
        sprintArmsLayer = characterAnimator.GetLayerIndex("SprintArms");
        // A new controller/weapon needs its initial authored pose before it can
        // be culled. Mounted seating keeps its separate calibration ownership.
        fullPoseUntil = Time.time + firingPoseHold;
        nextDetailRefresh = 0f;
        UpdatePresentationQuality();
    }

    void OnDestroy()
    {
        actions.FireStarted -= PlayFire;
        actions.ReloadStarted -= PlayReload;
        actions.ReloadCanceled -= CancelReload;
    }

    void Update()
    {
        UpdatePresentationQuality();
        float reloadWeight = characterAnimator.GetFloat("WeaponReloadWeight");
        UpdateRigWeights(reloadWeight);
        UpdateMovementMotion();
        UpdateWeaponLayers(reloadWeight);
        UpdateWeaponPlacement(reloadWeight);
    }

    void UpdatePresentationQuality()
    {
        bool viewer = soldier && soldier.inputSource is SoldierLocalInput || view.NeedsCameraAnimation;
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

    void UpdateRigWeights(float weight)
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
        chestAim.weight = firingHand.weight = (1f - weight) * (1f - sprint) * (1f - throwing);
        supportHand.weight = (1f - weight) * (1f - throwing);
        weaponAim.weight = (1f - weight) * (1f - throwing);
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

    void UpdateWeaponLayers(float weight)
    {
        // The same curve returns the weapon to its hold pose and the hands
        // to their grips. Animator blends the clips before the rig solves IK.
        characterAnimator.SetLayerWeight(characterSwayLayer, (1f - weight) * cosmeticDetail);
        characterAnimator.SetLayerWeight(weaponPoseLayer, weight);
        characterAnimator.SetLayerWeight(weaponSwayLayer, (1f - weight) * cosmeticDetail);
        characterAnimator.SetLayerWeight(weaponFireLayer, 1f - weight);
    }

    void UpdateWeaponPlacement(float weight)
    {
        SoldierWeaponDefinition set = weapon.definition;
        // Share the first-person placement blend and its exact duration.
        float aimBlend = view.PlacementAimBlend;
        Vector3 holdPosition = Vector3.Lerp(set.thirdPersonHipAimPosition,
            set.thirdPersonAimPosition, aimBlend);
        // Fade to the authored reload pivot independently of the hold pose.
        aimPivot.localPosition = Vector3.Lerp(holdPosition, set.thirdPersonReloadPosition, weight);
        // The rig aims this pivot after the current spine pose is evaluated,
        // before weapon following and hand IK. Reload uses its authored frame.
        aimPivot.localRotation = Quaternion.identity;
    }
    void PlayFire()
    {
        fullPoseUntil = Time.time + firingPoseHold;
        UpdatePresentationQuality();
        characterAnimator.ResetTrigger("WeaponFire");
        characterAnimator.CrossFadeInFixedTime("Fire", weapon.definition.fireBlendTime, weaponFireLayer, 0f);
        partsAnimator.ResetTrigger("Fire");
        partsAnimator.Play("Fire", 0, 0f);
    }

    void CancelReload()
    {
        characterAnimator.ResetTrigger("WeaponReload");
        characterAnimator.Play("Idle", weaponPoseLayer, 0f);
        partsAnimator.ResetTrigger("Reload");
        partsAnimator.Play("Idle", 0, 0f);
    }

    void PlayReload()
    {
        UpdatePresentationQuality();
        if(actions.IsSwapReload)return;
        characterAnimator.ResetTrigger("WeaponFire");
        characterAnimator.SetTrigger("CancelWeaponFire");
        characterAnimator.SetTrigger("WeaponReload");
        partsAnimator.ResetTrigger("Fire");
        partsAnimator.SetTrigger("Reload");
    }
}
