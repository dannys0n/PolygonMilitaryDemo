using UnityEngine;
using UnityEngine.Animations.Rigging;

[DefaultExecutionOrder(100)]
public class SoldierWeaponPresentation : MonoBehaviour
{
    public SoldierActions actions;
    public Animator characterAnimator;
    public CharacterController controller;
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

    int characterSwayLayer, weaponPoseLayer, weaponSwayLayer, weaponFireLayer, sprintBodyLayer, sprintArmsLayer;

    SoldierGrenades grenades;
    Rig aimRig;
    RigBuilder rigBuilder;
    RigLayer aimLayer;
    void Awake()
    {
        grenades=GetComponent<SoldierGrenades>();
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
        view.weaponAnimator.runtimeAnimatorController = set.firstPersonParts;
        characterAnimator.runtimeAnimatorController = set.thirdPerson;
        characterAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        partsAnimator.runtimeAnimatorController = set.firstPersonParts;
        partsAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        view.weaponAnimator.Rebind();
        partsAnimator.Rebind();
        view.ApplyWeaponDefinition(set);
        characterSwayLayer = characterAnimator.GetLayerIndex("SourceSway");
        weaponPoseLayer = characterAnimator.GetLayerIndex("WeaponPose");
        weaponSwayLayer = characterAnimator.GetLayerIndex("WeaponSway");
        weaponFireLayer = characterAnimator.GetLayerIndex("WeaponFire");
        sprintBodyLayer = characterAnimator.GetLayerIndex("SprintBody");
        sprintArmsLayer = characterAnimator.GetLayerIndex("SprintArms");
    }

    void OnDestroy()
    {
        actions.FireStarted -= PlayFire;
        actions.ReloadStarted -= PlayReload;
        actions.ReloadCanceled -= CancelReload;
    }

    void Update()
    {
        float reloadWeight = characterAnimator.GetFloat("WeaponReloadWeight");
        UpdateRigWeights(reloadWeight);
        UpdateMovementMotion();
        UpdateWeaponLayers(reloadWeight);
        UpdateWeaponPlacement(reloadWeight);
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
        characterAnimator.SetLayerWeight(characterSwayLayer, 1f - weight);
        characterAnimator.SetLayerWeight(weaponPoseLayer, weight);
        characterAnimator.SetLayerWeight(weaponSwayLayer, 1f - weight);
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
        characterAnimator.ResetTrigger("WeaponFire");
        characterAnimator.SetTrigger("CancelWeaponFire");
        characterAnimator.SetTrigger("WeaponReload");
        partsAnimator.ResetTrigger("Fire");
        partsAnimator.SetTrigger("Reload");
    }
}
