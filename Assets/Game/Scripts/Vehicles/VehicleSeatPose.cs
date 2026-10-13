using UnityEngine;

// Mounted presentation only; infantry controllers and rigs are restored by VehicleOccupant.
[RequireComponent(typeof(Animator))]
public class VehicleSeatPose : MonoBehaviour
{
    Animator animator;
    RuntimeAnimatorController infantryController;
    AnimatorCullingMode infantryCulling;
    bool infantryRootMotion, mounted;
    Transform leftGrip, rightGrip, pelvisAnchor;

    public void Begin(Animator bodyAnimator, Vehicle.Seat seat, Transform leftTarget, Transform rightTarget)
    {
        if (!mounted)
        {
            animator = bodyAnimator;
            infantryController = animator.runtimeAnimatorController;
            infantryRootMotion = animator.applyRootMotion;
            infantryCulling = animator.cullingMode;
        }
        mounted = true;
        pelvisAnchor = seat.anchor;
        leftGrip = leftTarget;
        rightGrip = rightTarget;
        enabled = true;
        animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("VehicleSeats");
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.enabled = true;
        animator.Play(seat.role == Vehicle.SeatRole.Gunner ? "Turret" : seat.role == Vehicle.SeatRole.Driver ? "Driving" : "Sitting", 0, 0);
        animator.Update(0f);
    }

    public void End()
    {
        if (!mounted) return;
        mounted = false;
        leftGrip = rightGrip = pelvisAnchor = null;
        animator.runtimeAnimatorController = infantryController;
        animator.applyRootMotion = infantryRootMotion;
        animator.cullingMode = infantryCulling;
        // Rebind restores infantry layer defaults and clears mounted pose IK.
        animator.Rebind();
        animator.Update(0f);
        enabled = false;
    }

    void OnAnimatorIK(int layer)
    {
        if (!mounted || layer != 0) return;
        // Anchors represent the seat cushion/pelvis, independent of avatar height.
        animator.bodyPosition = pelvisAnchor.position;
        animator.bodyRotation = pelvisAnchor.rotation;
        PlaceHand(AvatarIKGoal.LeftHand, leftGrip);
        PlaceHand(AvatarIKGoal.RightHand, rightGrip);
    }

    void PlaceHand(AvatarIKGoal hand, Transform target)
    {
        float weight = target ? 1f : 0f;
        animator.SetIKPositionWeight(hand, weight);
        animator.SetIKRotationWeight(hand, weight);
        if (!target) return;
        animator.SetIKPosition(hand, target.position);
        animator.SetIKRotation(hand, target.rotation);
    }
}

