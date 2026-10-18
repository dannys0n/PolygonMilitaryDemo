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
    [Tooltip("Extra forward impulse on the chest for stationary deaths, as a multiple of fallImpulse.")]
    [Min(0)] public float stationaryToppleMultiplier = 5f;
    public bool IsRagdoll { get; private set; }
    Transform[] bones;
    Vector3[] restPositions;
    Quaternion[] restRotations;
    Vector3[] deathPositions, bodyPositions;
    Quaternion[] deathRotations, bodyRotations;
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
    Rigidbody chestBody;
    SkinnedMeshRenderer[] bodyMeshes;
    bool[] previousOffscreenUpdates;
    bool[] damageBodies;

    void Awake()
    {
        var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        if (!chest) chest = animator.GetBoneTransform(HumanBodyBones.Spine);
        if (chest) chestBody = chest.GetComponent<Rigidbody>();
        bones = System.Array.FindAll(animator.GetBoneTransform(HumanBodyBones.Hips).GetComponentsInChildren<Transform>(true),
            bone => !bone.IsChildOf(life.loadout.thirdPersonMount));
        restPositions = new Vector3[bones.Length]; restRotations = new Quaternion[bones.Length];
        deathPositions = new Vector3[bones.Length]; deathRotations = new Quaternion[bones.Length];
        bodyPositions = new Vector3[bodies.Length]; bodyRotations = new Quaternion[bodies.Length];
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
        damageBodies = new bool[bodies.Length];
        foreach (var region in GetComponentsInChildren<SoldierHitRegion>(true))
            foreach (var collider in region.GetComponents<Collider>())
            {
                int index = System.Array.IndexOf(bodies, collider.attachedRigidbody);
                if (index >= 0 && collider.enabled) damageBodies[index] = true;
            }
        SetPhysics(false);
        // Joint limits constrain the pose; avoid extra self-collision contacts.
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
    void LateUpdate() { if (!IsRagdoll) lastVelocity = life.controller.Velocity; }

    void OnDeath(GameObject source)
    {
        if (IsRagdoll) return;
        IsRagdoll = true;
        // Equipment can be replaced after Awake; snapshot only the current skins.
        bodyMeshes = life.view.thirdPersonRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        previousOffscreenUpdates = new bool[bodyMeshes.Length];
        // PhysX moves bones beyond the imported animation bounds. Recompute bounds
        // while this short-lived corpse is active; living characters retain culling.
        for (int i = 0; i < bodyMeshes.Length; i++)
        {
            previousOffscreenUpdates[i] = bodyMeshes[i].updateWhenOffscreen;
            bodyMeshes[i].updateWhenOffscreen = true;
        }
        for (int i = 0; i < bones.Length; i++)
        { deathPositions[i] = bones[i].localPosition; deathRotations[i] = bones[i].localRotation; }
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
        { bones[i].localPosition = deathPositions[i]; bones[i].localRotation = deathRotations[i]; }
        // Animated bones can have stale kinematic physics poses. Capture the visible
        // world pose before handing ownership to PhysX, including every child body.
        for (int i = 0; i < bodies.Length; i++)
        { bodyPositions[i] = bodies[i].transform.position; bodyRotations[i] = bodies[i].transform.rotation; }
        heldWeapon = life.loadout.ThirdPersonView.transform;
        weaponParent = heldWeapon.parent; weaponPosition = heldWeapon.localPosition; weaponRotation = heldWeapon.localRotation;
        weaponScale = heldWeapon.localScale;
        heldWeapon.SetParent(animator.GetBoneTransform(HumanBodyBones.RightHand), true);
        Physics.SyncTransforms();
        SetPhysics(true);
        for (int i = 0; i < bodies.Length; i++)
        { bodies[i].position = bodyPositions[i]; bodies[i].rotation = bodyRotations[i]; }
        Vector3 direction = source ? Vector3.ProjectOnPlane(transform.position - source.transform.position, Vector3.up).normalized : -transform.forward;
        if (direction.sqrMagnitude < .01f) direction = -transform.forward;
        foreach (var body in bodies) body.linearVelocity = lastVelocity;
        bodies[0].AddForce(direction * fallImpulse, ForceMode.Impulse);
        if (chestBody && lastVelocity.sqrMagnitude < .25f && fallImpulse > 0)
        {
            // A pelvis-only nudge lets an idle body crumple vertically. Push above
            // its centre of mass so gravity carries it into a forward fall.
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            forward = (forward + transform.right * Random.Range(-.12f, .12f)).normalized;
            chestBody.AddForce(forward * (fallImpulse * stationaryToppleMultiplier), ForceMode.Impulse);
        }
    }

    void SetPhysics(bool active)
    {
        foreach (var collider in bodyColliders) collider.enabled = active;
        for (int i = 0; i < bodies.Length; i++)
        {
            var body = bodies[i];
            // Interpolation belongs to the simulated corpse. Leaving it on while
            // kinematic lets old physics poses overwrite the animated skeleton.
            body.interpolation = active ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            // Hit-region triggers can share these rigidbodies while the soldier is alive.
            body.isKinematic = !active; body.detectCollisions = active || damageBodies[i];
        }
    }

    void RestoreAnimation()
    {
        if (!IsRagdoll) return;
        for (int i = 0; i < bodyMeshes.Length; i++)
            if (bodyMeshes[i]) bodyMeshes[i].updateWhenOffscreen = previousOffscreenUpdates[i];
        SetPhysics(false);
        if(heldWeapon && heldWeapon==life.loadout.ThirdPersonView.transform)
        {
            heldWeapon.SetParent(weaponParent, false);
            heldWeapon.localPosition = weaponPosition; heldWeapon.localRotation = weaponRotation;
            heldWeapon.localScale = weaponScale;
        }
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
