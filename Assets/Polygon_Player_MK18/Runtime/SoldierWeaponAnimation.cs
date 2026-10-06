using UnityEngine;
using UnityEngine.Animations.Rigging;

// The animated right hand owns the held weapon. Reload offsets are authored
// relative to that hand; the support hand releases into the reload animation.
[DefaultExecutionOrder(100)]
public class SoldierWeaponAnimation : MonoBehaviour
{
    public Animator characterAnimator;
    public Transform weaponRoot;
    public AnimationClip weaponIdle;
    public AnimationClip weaponReload;
    public MultiAimConstraint chestAim;
    public TwoBoneIKConstraint supportHand;
    int reloadLayer;

    void Awake() => reloadLayer = characterAnimator.GetLayerIndex("Reload");

    void Update()
    {
        float weight = characterAnimator.GetFloat("WeaponReloadWeight");
        chestAim.weight = 1f - weight;
        supportHand.weight = 1f - weight;
        var state = characterAnimator.GetCurrentAnimatorStateInfo(reloadLayer);
        if (characterAnimator.IsInTransition(reloadLayer))
        {
            var next = characterAnimator.GetNextAnimatorStateInfo(reloadLayer);
            if (next.IsName("Reload")) state = next;
        }
        weaponIdle.SampleAnimation(weaponRoot.gameObject, 0f);
        Vector3 heldPosition = weaponRoot.localPosition;
        Quaternion heldRotation = weaponRoot.localRotation;
        if (state.IsName("Reload"))
            weaponReload.SampleAnimation(weaponRoot.gameObject,
                Mathf.Clamp01(state.normalizedTime) * weaponReload.length);
        weaponRoot.SetLocalPositionAndRotation(
            Vector3.Lerp(heldPosition, weaponRoot.localPosition, weight),
            Quaternion.Slerp(heldRotation, weaponRoot.localRotation, weight));
    }
}
