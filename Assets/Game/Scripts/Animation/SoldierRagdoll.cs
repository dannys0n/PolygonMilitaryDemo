using UnityEngine;
using UnityEngine.Animations.Rigging;

// Presentation only: health/lifecycle still own death and respawn.
[DefaultExecutionOrder(20)]
public class SoldierRagdoll : MonoBehaviour
{
    public SoldierLife life;
    public Animator animator;
    public RigBuilder rig;
    public Rigidbody[] bodies;
    public Collider[] bodyColliders;
    public Behaviour[] animationDrivers;
    public float fallImpulse = 1.5f;
    public bool IsRagdoll { get; private set; }
    Transform[] bones;
    Vector3[] restPositions;
    Quaternion[] restRotations;
    bool[] driverEnabled;
    bool animatorEnabled;
    RigBuilder[] allRigBuilders;
    Behaviour[] constraints;
    bool[] builderEnabled, constraintEnabled;
    float[] constraintWeights;
    Transform heldWeapon, weaponParent;
    Vector3 weaponPosition;
    Vector3 weaponScale;
    Quaternion weaponRotation;
    Vector3 lastVelocity;

    void Awake()
    {
        bones = animator.GetBoneTransform(HumanBodyBones.Hips).GetComponentsInChildren<Transform>(true);
        restPositions = new Vector3[bones.Length]; restRotations = new Quaternion[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        { restPositions[i] = bones[i].localPosition; restRotations[i] = bones[i].localRotation; }
        // Both the body and the FPS arms have rig graphs and grip drivers.
        var drivers = new System.Collections.Generic.HashSet<Behaviour>(animationDrivers);
        foreach (var grip in GetComponentsInChildren<SoldierMotionGrip>(true)) drivers.Add(grip);
        animationDrivers = new Behaviour[drivers.Count]; drivers.CopyTo(animationDrivers);
        driverEnabled = new bool[animationDrivers.Length];
        allRigBuilders = GetComponentsInChildren<RigBuilder>(true);
        builderEnabled = new bool[allRigBuilders.Length];
        var found = new System.Collections.Generic.List<Behaviour>();
        foreach (var behaviour in GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour is IRigConstraint) found.Add(behaviour);
        constraints = found.ToArray();
        constraintEnabled = new bool[constraints.Length]; constraintWeights = new float[constraints.Length];
        SetPhysics(false);
        // Reapply for each instance, including prefab variants.
        for (int i = 0; i < bodyColliders.Length; i++)
            for (int j = i + 1; j < bodyColliders.Length; j++)
                Physics.IgnoreCollision(bodyColliders[i], bodyColliders[j]);
    }

    void OnEnable()
    {
        life.health.Died += OnDeath;
        life.Respawning += RestoreAnimation;
    }
    void OnDisable()
    {
        life.health.Died -= OnDeath;
        life.Respawning -= RestoreAnimation;
    }
    void LateUpdate() { if (!IsRagdoll) lastVelocity = life.controller.velocity; }

    void OnDeath(GameObject source)
    {
        if (IsRagdoll) return;
        IsRagdoll = true;
        var positions = new Vector3[bones.Length]; var rotations = new Quaternion[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        { positions[i] = bones[i].localPosition; rotations[i] = bones[i].localRotation; }
        animatorEnabled = animator.enabled;
        for (int i = 0; i < animationDrivers.Length; i++)
        { driverEnabled[i] = animationDrivers[i].enabled; animationDrivers[i].enabled = false; }
        for (int i = 0; i < constraints.Length; i++)
        {
            constraintEnabled[i] = constraints[i].enabled;
            var constraint = (IRigConstraint)constraints[i];
            constraintWeights[i] = constraint.weight; constraint.weight = 0f;
            constraints[i].enabled = false;
        }
        for (int i = 0; i < allRigBuilders.Length; i++)
        { builderEnabled[i] = allRigBuilders[i].enabled; allRigBuilders[i].enabled = false; }
        rig.enabled = false; animator.enabled = false; life.controller.enabled = false;
        for (int i = 0; i < bones.Length; i++)
        { bones[i].localPosition = positions[i]; bones[i].localRotation = rotations[i]; }
        heldWeapon = life.loadout.slots[life.loadout.selectedSlot].thirdPersonWeapon;
        weaponParent = heldWeapon.parent; weaponPosition = heldWeapon.localPosition; weaponRotation = heldWeapon.localRotation;
        weaponScale = heldWeapon.localScale;
        heldWeapon.SetParent(animator.GetBoneTransform(HumanBodyBones.RightHand), true);
        Physics.SyncTransforms();
        SetPhysics(true);
        Vector3 direction = source ? Vector3.ProjectOnPlane(transform.position - source.transform.position, Vector3.up).normalized : -transform.forward;
        if (direction.sqrMagnitude < .01f) direction = -transform.forward;
        foreach (var body in bodies) body.linearVelocity = lastVelocity;
        bodies[0].AddForce(direction * fallImpulse, ForceMode.Impulse);
    }

    void SetPhysics(bool active)
    {
        foreach (var collider in bodyColliders) collider.enabled = active;
        foreach (var body in bodies)
        {
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            // Hit-region triggers can share these rigidbodies while the soldier is alive.
            body.isKinematic = !active; body.detectCollisions = true;
        }
    }

    void RestoreAnimation()
    {
        if (!IsRagdoll) return;
        SetPhysics(false);
        heldWeapon.SetParent(weaponParent, false);
        heldWeapon.localPosition = weaponPosition; heldWeapon.localRotation = weaponRotation;
        heldWeapon.localScale = weaponScale;
        for (int i = 0; i < bones.Length; i++)
        { bones[i].localPosition = restPositions[i]; bones[i].localRotation = restRotations[i]; }
        for (int i = 0; i < constraints.Length; i++)
        {
            ((IRigConstraint)constraints[i]).weight = constraintWeights[i];
            constraints[i].enabled = constraintEnabled[i];
        }
        animator.enabled = animatorEnabled;
        for (int i = 0; i < allRigBuilders.Length; i++) allRigBuilders[i].enabled = builderEnabled[i];
        for (int i = 0; i < animationDrivers.Length; i++) animationDrivers[i].enabled = driverEnabled[i];
        IsRagdoll = false;
    }
}
