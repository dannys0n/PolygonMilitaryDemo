using UnityEngine;
using UnityEngine.Animations.Rigging;

[DefaultExecutionOrder(100)]
public class SoldierWeaponPresentation : MonoBehaviour
{
    public SoldierActions actions;
    public Animator characterAnimator;
    public CharacterController controller;
    public SoldierFirstPersonView view;
    public SoldierWeaponAnimationSet animationSet;
    public Transform aimPivot;
    public Transform weaponRoot;
    public Transform aimTarget;
    public MultiAimConstraint chestAim;
    public TwoBoneIKConstraint supportHand;
    public TwoBoneIKConstraint firingHand;

    int characterSwayLayer, weaponPoseLayer, weaponSwayLayer, weaponFireLayer;

    void Awake()
    {
        ApplyAnimationSet(animationSet);
        actions.FireStarted += PlayFire;
        actions.ReloadStarted += PlayReload;
    }

    public void ApplyAnimationSet(SoldierWeaponAnimationSet set)
    {
        animationSet = set;
        view.armsAnimator.runtimeAnimatorController = set.firstPerson;
        view.weaponAnimator.runtimeAnimatorController = set.firstPersonParts;
        characterAnimator.runtimeAnimatorController = set.thirdPerson;
        characterSwayLayer = characterAnimator.GetLayerIndex("SourceSway");
        weaponPoseLayer = characterAnimator.GetLayerIndex("WeaponPose");
        weaponSwayLayer = characterAnimator.GetLayerIndex("WeaponSway");
        weaponFireLayer = characterAnimator.GetLayerIndex("WeaponFire");
    }

    void OnDestroy()
    {
        actions.FireStarted -= PlayFire;
        actions.ReloadStarted -= PlayReload;
    }

    void Update()
    {
        float weight = characterAnimator.GetFloat("WeaponReloadWeight");
        chestAim.weight = supportHand.weight = firingHand.weight = 1f - weight;
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
        Quaternion aiming = Quaternion.Inverse(aimPivot.parent.rotation) *
            Quaternion.LookRotation(aimTarget.position - aimPivot.position, transform.up);
        aimPivot.localRotation = Quaternion.Slerp(aiming, Quaternion.identity, weight);
    }
    void PlayFire() => characterAnimator.CrossFadeInFixedTime("Fire", .05f, weaponFireLayer, 0f);

    void PlayReload()
    {
        characterAnimator.Play("Empty", weaponFireLayer, 0f);
        characterAnimator.CrossFadeInFixedTime("Reload", view.motionBlendTime, weaponPoseLayer, 0f);
    }
}


