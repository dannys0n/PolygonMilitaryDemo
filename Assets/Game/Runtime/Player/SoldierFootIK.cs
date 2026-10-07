using UnityEngine;

public class SoldierFootIK : MonoBehaviour
{
    public Animator animator;
    public CharacterController controller;
    public LayerMask groundLayers = 9;

    [Header("Ground placement")]
    [Min(0f)] public float soleClearance = 0.015f;
    [Min(0.01f)] public float castHeight = 0.5f;
    [Min(0.01f)] public float maxFootOffset = 0.35f;
    [Min(0f)] public float maxPelvisDrop = 0.25f;
    [Min(0.01f)] public float smoothing = 12f;

    [Header("Leg stability")]
    [Range(0f, 1f)] public float rotationWeight = 0.5f;
    [Range(0f, 45f)] public float maxFootTilt = 25f;
    [Range(0f, 1f)] public float kneeHintWeight = 0.5f;
    [Min(0.01f)] public float kneeForwardOffset = 0.4f;
    [Min(0f)] public float liftFadeStart = 0.04f;
    [Min(0.01f)] public float liftFadeEnd = 0.18f;
    public bool drawDebug;

    private readonly RaycastHit[] groundHits = new RaycastHit[16];
    private FootState left;
    private FootState right;
    private float groundedWeight;
    private float pelvisOffset;

    private struct FootState
    {
        public float offset;
        public float weight;
        public Vector3 normal;
    }

    private struct FootSample
    {
        public Vector3 position;
        public Quaternion rotation;
        public float offset;
        public float contact;
        public bool foundGround;
    }

    void OnEnable() => ResetFeet();

    void ResetFeet()
    {
        left = new FootState { normal = Vector3.up };
        right = new FootState { normal = Vector3.up };
        groundedWeight = 0f;
        pelvisOffset = 0f;
    }

    void OnAnimatorIK(int layerIndex)
    {
        // Only the Base Layer has IK Pass enabled.
        if (layerIndex != 0)
            return;

        // Running owns the entire body pose, including pelvis and feet.
        float correctionWeight = 1f - animator.GetLayerWeight(animator.GetLayerIndex("SprintBody"));
        if (correctionWeight <= 0f)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
            animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
            animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
            animator.SetIKHintPositionWeight(AvatarIKHint.LeftKnee, 0f);
            animator.SetIKHintPositionWeight(AvatarIKHint.RightKnee, 0f);
            ResetFeet();
            return;
        }

        float blend = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        bool grounded = controller.isGrounded && animator.GetFloat("VerticalSpeed") <= 0f;
        groundedWeight = Mathf.Lerp(groundedWeight, grounded ? 1f : 0f, blend);

        Vector3 root = controller.transform.position;
        float referenceFloor = root.y;
        if (TraceGround(root + Vector3.up * castHeight,
            castHeight + maxFootOffset, out RaycastHit centerHit))
            referenceFloor = centerHit.point.y;

        FootSample leftSample = SampleFoot(AvatarIKGoal.LeftFoot,
            animator.leftFeetBottomHeight, referenceFloor, grounded, blend, ref left);
        FootSample rightSample = SampleFoot(AvatarIKGoal.RightFoot,
            animator.rightFeetBottomHeight, referenceFloor, grounded, blend, ref right);

        // Lower the body toward the lower supported foot instead of stretching that leg.
        float desiredPelvis = 0f;
        if (leftSample.foundGround && leftSample.contact > 0.5f)
            desiredPelvis = Mathf.Min(desiredPelvis, leftSample.offset);
        if (rightSample.foundGround && rightSample.contact > 0.5f)
            desiredPelvis = Mathf.Min(desiredPelvis, rightSample.offset);
        desiredPelvis = Mathf.Clamp(desiredPelvis, -maxPelvisDrop, 0f);
        pelvisOffset = Mathf.Lerp(pelvisOffset, desiredPelvis, blend);

        Vector3 body = animator.bodyPosition;
        body.y += pelvisOffset * groundedWeight * correctionWeight;
        animator.bodyPosition = body;

        ApplyFoot(AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, leftSample, left, body, correctionWeight);
        ApplyFoot(AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, rightSample, right, body, correctionWeight);
    }

    FootSample SampleFoot(AvatarIKGoal goal, float soleHeight, float referenceFloor,
        bool grounded, float blend, ref FootState state)
    {
        Vector3 animatedPosition = animator.GetIKPosition(goal);
        Quaternion animatedRotation = animator.GetIKRotation(goal);
        Vector3 animatedSole = animatedPosition + animatedRotation * Vector3.down * soleHeight;

        FootSample sample = new FootSample
        {
            position = animatedPosition,
            rotation = animatedRotation
        };

        Vector3 origin = animatedSole;
        origin.y = Mathf.Max(animatedSole.y, controller.transform.position.y) + castHeight;
        float distance = origin.y - animatedSole.y + maxFootOffset;
        RaycastHit hit = default;
        bool hitGround = grounded && TraceGround(origin, distance, out hit);

        Vector3 normal = Vector3.up;
        if (hitGround)
        {
            normal = Vector3.RotateTowards(Vector3.up, hit.normal,
                maxFootTilt * Mathf.Deg2Rad, 0f);
            Quaternion targetRotation = Quaternion.FromToRotation(Vector3.up, normal) * animatedRotation;
            Vector3 target = hit.point - targetRotation * Vector3.down * soleHeight;
            target.y += soleClearance;
            float offset = target.y - animatedPosition.y;

            if (Mathf.Abs(offset) <= maxFootOffset)
            {
                sample.foundGround = true;
                sample.offset = offset;
                // Let the walking animation lift a foot instead of pinning it down.
                float lift = Mathf.Max(0f, animatedSole.y - referenceFloor - soleClearance);
                float contact = 1f - Mathf.InverseLerp(liftFadeStart,
                    Mathf.Max(liftFadeStart + 0.01f, liftFadeEnd), lift);
                sample.contact = Mathf.SmoothStep(0f, 1f, contact);
            }
        }

        state.offset = Mathf.Lerp(state.offset, sample.offset, blend);
        state.weight = Mathf.Lerp(state.weight, sample.contact, blend);
        state.normal = Vector3.Slerp(state.normal, normal, blend);

        // Smooth only the correction: the animation retains its horizontal motion and yaw.
        sample.position.y += state.offset;
        sample.rotation = Quaternion.FromToRotation(Vector3.up, state.normal) * animatedRotation;
        if (drawDebug)
            Debug.DrawLine(origin, origin + Vector3.down * distance,
                sample.foundGround ? Color.green : Color.red);
        return sample;
    }

    void ApplyFoot(AvatarIKGoal goal, AvatarIKHint hint, FootSample sample,
        FootState state, Vector3 body, float correctionWeight)
    {
        float weight = state.weight * groundedWeight * correctionWeight;
        animator.SetIKPositionWeight(goal, weight);
        animator.SetIKRotationWeight(goal, weight * rotationWeight);
        animator.SetIKPosition(goal, sample.position);
        animator.SetIKRotation(goal, sample.rotation);

        Vector3 knee = Vector3.Lerp(sample.position, body, 0.5f)
            + controller.transform.forward * kneeForwardOffset;
        animator.SetIKHintPositionWeight(hint, weight * kneeHintWeight);
        animator.SetIKHintPosition(hint, knee);
    }

    bool TraceGround(Vector3 origin, float distance, out RaycastHit closest)
    {
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits,
            distance, groundLayers, QueryTriggerInteraction.Ignore);
        closest = default;
        float nearest = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            // Default-layer scenery is supported without hitting our own capsule or weapon.
            if (hit.collider.transform.IsChildOf(controller.transform)
                || hit.collider is CharacterController
                || Vector3.Angle(Vector3.up, hit.normal) > controller.slopeLimit
                || hit.distance >= nearest)
                continue;
            closest = hit;
            nearest = hit.distance;
            found = true;
        }
        return found;
    }
}
