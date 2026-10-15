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
    Vector3 bodyOffset, lastBodyPosition;

    public void Begin(Animator bodyAnimator, Vehicle.Seat seat, Transform leftTarget, Transform rightTarget)
    {
        if (!mounted)
        {
            animator = bodyAnimator;
            infantryController = animator.runtimeAnimatorController;
            infantryRootMotion = animator.applyRootMotion;
            infantryCulling = animator.cullingMode;
        }
        mounted = true; bodyOffset=Vector3.zero;
        pelvisAnchor = seat.anchor; lastBodyPosition=pelvisAnchor.position;
        leftGrip = leftTarget;
        rightGrip = rightTarget;
        enabled = true;
        animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("VehicleSeats");
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.enabled = true;
        string state = seat.pose != Vehicle.Seat.Pose.Automatic ? seat.pose.ToString()
            : seat.role == Vehicle.SeatRole.Gunner ? "Turret" : seat.role == Vehicle.SeatRole.Driver ? "Driving" : "Sitting";
        animator.Play(state, 0, 0);
        animator.Update(0f);
        bodyOffset=Quaternion.Inverse(pelvisAnchor.rotation)*(lastBodyPosition-animator.GetBoneTransform(HumanBodyBones.Hips).position);
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
        if (animator.isActiveAndEnabled) animator.Update(0f);
        enabled = false;
    }

    void OnAnimatorIK(int layer)
    {
        if (!mounted || layer != 0) return;
        // Anchors represent the seat cushion/pelvis, independent of avatar height.
        lastBodyPosition = pelvisAnchor.position + pelvisAnchor.rotation * bodyOffset;
        animator.bodyPosition = lastBodyPosition;
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

