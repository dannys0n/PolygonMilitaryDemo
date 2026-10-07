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
    public TwoBoneIKConstraint supportHand;
    public TwoBoneIKConstraint firingHand;
    public MultiParentConstraint runWeaponFollow;

    int characterSwayLayer, weaponPoseLayer, weaponSwayLayer, weaponFireLayer, sprintBodyLayer, sprintArmsLayer;

    void Awake()
    {
        ApplyWeapon(weapon);
        actions.FireStarted += PlayFire;
        actions.ReloadStarted += PlayReload;
    }

    // Call after the matching first- and third-person visual rigs have been equipped.
    public bool TryApplyWeapon(SoldierWeapon selectedWeapon)
    {
        if (actions.IsReloading || view.IsEquipping) return false;
        ApplyWeapon(selectedWeapon);
        return true;
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
        view.armsAnimator.Rebind();
        view.weaponAnimator.Rebind();
        partsAnimator.Rebind();
        characterAnimator.Rebind();
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
    }

    void Update()
    {
        float weight = characterAnimator.GetFloat("WeaponReloadWeight");
        float sprint = Mathf.Clamp01(characterAnimator.GetFloat("RunBlend"));
        // Animator damping approaches the target asymptotically. Finish the blend
        // exactly so a tiny remaining run value cannot keep IK disabled.
        if (sprint < .001f) sprint = 0f;
        else if (sprint > .999f) sprint = 1f;
        sprint *= 1f - weight;
        characterAnimator.SetLayerWeight(sprintBodyLayer, sprint);
        characterAnimator.SetLayerWeight(sprintArmsLayer, sprint);
        chestAim.weight = firingHand.weight = (1f - weight) * (1f - sprint);
        supportHand.weight = 1f - weight;
        runWeaponFollow.weight = sprint;
        Vector3 velocity = controller.velocity;
        velocity.y = 0f;
        float speed = controller.isGrounded ? Mathf.Clamp01(velocity.magnitude / view.walkingSpeed) : 0f;
        characterAnimator.SetFloat("MotionSpeed", speed, .12f, Time.deltaTime);

        // The same curve returns the weapon to its hold pose and the hands
        // to their grips. Animator blends the clips before the rig solves IK.
        characterAnimator.SetLayerWeight(characterSwayLayer, 1f - weight);
        characterAnimator.SetLayerWeight(weaponPoseLayer, weight);
        characterAnimator.SetLayerWeight(weaponSwayLayer, 1f - weight);
        characterAnimator.SetLayerWeight(weaponFireLayer, 1f - weight);
        SoldierWeaponDefinition set = weapon.definition;
        // Share the first-person placement blend and its exact duration.
        float aimBlend = view.PlacementAimBlend;
        Vector3 holdPosition = Vector3.Lerp(set.thirdPersonHipAimPosition,
            set.thirdPersonAimPosition, aimBlend);
        // Reload clips are authored relative to the ADS pivot. Fade back to
        // that reference position as the reload takes control of the weapon.
        aimPivot.localPosition = Vector3.Lerp(holdPosition, set.thirdPersonAimPosition, weight);
        Quaternion aiming = Quaternion.Inverse(aimPivot.parent.rotation) *
            Quaternion.LookRotation(aimTarget.position - aimPivot.position, transform.up);
        aimPivot.localRotation = Quaternion.Slerp(aiming, Quaternion.identity, weight);
    }
    void PlayFire()
    {
        characterAnimator.ResetTrigger("WeaponFire");
        characterAnimator.SetTrigger("WeaponFire");
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
