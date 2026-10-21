using UnityEngine;

// Cosmetic only: one link advances toward the receiver for each accepted shot.
// The authored reload owns these same bones whenever it is active.
[DefaultExecutionOrder(300)]
public sealed class WeaponBeltFeed : MonoBehaviour
{
    public Transform beltFrame;
    [Tooltip("Linked round bones in hierarchy order, with parents before their children.")]
    public Transform[] rounds;
    [Tooltip("Enable when the bone chain starts at the magazine and ends at the receiver.")]
    public bool feedTowardLastRound;
    [Range(.2f, 1f)] public float feedFractionOfShot = .8f;

    SoldierActions actions;
    Vector3[] positions, localPositions;
    Quaternion[] rotations, localRotations;
    float shotTime = float.NegativeInfinity, duration;
    bool restore;

    void Awake()
    {
        positions = new Vector3[rounds.Length];
        rotations = new Quaternion[rounds.Length];
        localPositions = new Vector3[rounds.Length];
        localRotations = new Quaternion[rounds.Length];
        for (int i = 0; i < rounds.Length; i++)
        {
            positions[i] = beltFrame.InverseTransformPoint(rounds[i].position);
            rotations[i] = Quaternion.Inverse(beltFrame.rotation) * rounds[i].rotation;
            localPositions[i] = rounds[i].localPosition;
            localRotations[i] = rounds[i].localRotation;
        }
    }

    void OnEnable()
    {
        actions = GetComponentInParent<SoldierActions>();
        if (actions) actions.FireStarted += OnShot;
    }

    void OnDisable()
    {
        if (actions) actions.FireStarted -= OnShot;
        shotTime = float.NegativeInfinity;
        if (restore) Restore();
    }

    void OnShot()
    {
        if (rounds.Length < 2) return;
        duration = 60f / Mathf.Max(1f, actions.roundsPerMinute) * feedFractionOfShot;
        shotTime = Time.time;
        restore = true;
    }

    void LateUpdate()
    {
        if (!restore) return;
        if (actions && (actions.IsReloading || actions.IsThrowing))
        {
            // Do not restore here: the Animator has already evaluated the reload.
            restore = false;
            shotTime = float.NegativeInfinity;
            return;
        }
        float phase = (Time.time - shotTime) / duration;
        if (phase >= 1f) { Restore(); return; }
        ApplyFeed(Mathf.SmoothStep(0f, 1f, phase));
    }

    void ApplyFeed(float phase)
    {
        // Parent-first world poses avoid accumulating offsets down the bone chain.
        // Each rig can run from the receiver to the bag, or in the other direction.
        for (int i = 0; i < rounds.Length; i++)
        {
            int nextIndex = i + (feedTowardLastRound ? 1 : -1);
            bool end = nextIndex < 0 || nextIndex >= rounds.Length;
            Vector3 next = end ? positions[i] * 2f - positions[i + (feedTowardLastRound ? -1 : 1)] : positions[nextIndex];
            Quaternion rotation = end ? rotations[i] : rotations[nextIndex];
            rounds[i].SetPositionAndRotation(beltFrame.TransformPoint(Vector3.Lerp(positions[i], next, phase)),
                beltFrame.rotation * Quaternion.Slerp(rotations[i], rotation, phase));
        }
    }

    void Restore()
    {
        for (int i = 0; i < rounds.Length; i++)
            rounds[i].SetLocalPositionAndRotation(localPositions[i], localRotations[i]);
        restore = false;
    }
}
