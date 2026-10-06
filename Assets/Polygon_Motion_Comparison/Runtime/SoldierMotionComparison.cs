using UnityEngine;
using UnityEngine.Animations.Rigging;

[DefaultExecutionOrder(100)]
public class SoldierMotionComparison : MonoBehaviour
{
    public bool weaponDriven;
    public Animator characterAnimator;
    public CharacterController controller;
    public SoldierFirstPersonView view;
    public Transform weaponRoot;
    public Transform motionSample;
    public Transform aimTarget;
    public Quaternion neutralWeaponRotation;
    public AnimationClip weaponIdle;
    public AnimationClip weaponReload;
    public AnimationClip idleSway;
    public AnimationClip walkSway;
    public AnimationClip fireKick;
    public MultiAimConstraint chestAim;
    public TwoBoneIKConstraint supportHand;
    public TwoBoneIKConstraint firingHand;
    int reloadLayer, fireLayer, swayLayer;

    void Awake()
    {
        reloadLayer = characterAnimator.GetLayerIndex("Reload");
        fireLayer = characterAnimator.GetLayerIndex("Fire");
        swayLayer = characterAnimator.GetLayerIndex("SourceSway");
    }

    void Update()
    {
        float weight = characterAnimator.GetFloat("WeaponReloadWeight");
        chestAim.weight = 1f - weight;
        supportHand.weight = 1f - weight;
        if (weaponDriven) firingHand.weight = 1f;
        Vector3 velocity = controller.velocity; velocity.y = 0;
        float speed = controller.isGrounded ? Mathf.Clamp01(velocity.magnitude / view.walkingSpeed) : 0;
        characterAnimator.SetFloat("MotionSpeed", speed, .12f, Time.deltaTime);
        characterAnimator.SetLayerWeight(swayLayer, 1f - weight);
        var reload = characterAnimator.GetCurrentAnimatorStateInfo(reloadLayer);
        if (characterAnimator.IsInTransition(reloadLayer))
        {
            var next = characterAnimator.GetNextAnimatorStateInfo(reloadLayer);
            if (next.IsName("Reload")) reload = next;
        }
        weaponIdle.SampleAnimation(weaponRoot.gameObject, 0);
        Vector3 heldPosition = weaponRoot.localPosition;
        Quaternion heldRotation = weaponRoot.localRotation;
        if (weaponDriven)
        {
            Transform chest = weaponRoot.parent;
            Quaternion aimFrame = Quaternion.Inverse(chest.rotation) *
                Quaternion.LookRotation(aimTarget.position - weaponRoot.position, transform.up);
            idleSway.SampleAnimation(motionSample.gameObject, Mathf.Repeat(Time.time, idleSway.length));
            Vector3 position = motionSample.localPosition;
            Quaternion rotation = motionSample.localRotation;
            walkSway.SampleAnimation(motionSample.gameObject, Mathf.Repeat(Time.time, walkSway.length));
            position = Vector3.Lerp(position, motionSample.localPosition, speed);
            rotation = Quaternion.Slerp(rotation, motionSample.localRotation, speed);
            var fire = characterAnimator.GetCurrentAnimatorStateInfo(fireLayer);
            if (characterAnimator.IsInTransition(fireLayer))
            {
                var next = characterAnimator.GetNextAnimatorStateInfo(fireLayer);
                if (next.IsName("Fire")) fire = next;
            }
            if (fire.IsName("Fire"))
            {
                fireKick.SampleAnimation(motionSample.gameObject, Mathf.Clamp01(fire.normalizedTime) * fireKick.length);
                float fireWeight = characterAnimator.IsInTransition(fireLayer) &&
                    !characterAnimator.GetNextAnimatorStateInfo(fireLayer).IsName("Fire") ?
                    1f - characterAnimator.GetAnimatorTransitionInfo(fireLayer).normalizedTime : 1f;
                position += motionSample.localPosition * fireWeight;
                rotation *= Quaternion.Slerp(Quaternion.identity, motionSample.localRotation, fireWeight);
            }
            heldPosition += aimFrame * position;
            heldRotation = aimFrame * rotation * neutralWeaponRotation;
        }
        if (reload.IsName("Reload"))
            weaponReload.SampleAnimation(weaponRoot.gameObject, Mathf.Clamp01(reload.normalizedTime) * weaponReload.length);
        weaponRoot.SetLocalPositionAndRotation(
            Vector3.Lerp(heldPosition, weaponRoot.localPosition, weight),
            Quaternion.Slerp(heldRotation, weaponRoot.localRotation, weight));
    }
}
