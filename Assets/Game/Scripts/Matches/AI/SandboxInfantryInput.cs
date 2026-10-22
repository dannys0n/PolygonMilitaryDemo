using UnityEngine;
using UnityEngine.AI;

// Intent only: the same soldier controller, movement, aiming and weapon components
// execute this input as they do for a local player. No vehicle or cover policy lives here.
public class SandboxInfantryInput : SoldierInputSource
{
    public SoldierController soldier;
    public SoldierLife life;
    public ArenaParticipant participant;
    public NavMeshAgent agent;
    public ConquestCapturePoint objective;
    [Header("Simple infantry combat")]
    [Min(1)] public float sightRange = 45f;
    [Range(1, 360)] public float fieldOfView = 160f;
    [Min(.05f)] public float perceptionInterval = .15f;
    [Min(0)] public float reactionTime = 1f;
    [Min(0)] public float aimHoldTime = .6f;
    [Min(1)] public float turnSpeed = 240f;
    [Min(0)] public float initialAimError = 3f;
    [Min(.01f)] public float aimSettleTime = 4f;
    [Min(1)] public int burstRounds = 2;
    [Min(.01f)] public float burstPause = .4f;
    [Min(.01f)] public float semiAutoInterval = .22f;
    [Header("Objective movement")]
    [Min(.1f)] public float repathInterval = .5f;
    [Min(1)] public float sprintDistance = 15f;
    public ArenaParticipant Target => target;
    public Vector3 Destination { get; private set; }
    public bool IsFighting => target && Time.time - seenAt < aimHoldTime;

    ArenaParticipant target;
    VehicleOccupant occupant;
    readonly RaycastHit[] hits = new RaycastHit[32];
    float nextScan, nextPath, seenAt = float.NegativeInfinity, acquiredAt, readyAt;
    float nextSemi, pauseUntil, nextNoise;
    int shots;
    Vector2 noise;
    Vector3 goalOffset;
    bool visible;
    ArenaParticipant firingSightTarget;
    SoldierWeaponView firingSightWeapon;
    Vector3 firingSightOrigin, firingSightMuzzle, firingSightPoint;
    float nextFiringSight;
    bool firingSightClear;

    void Awake()
    {
        if (!soldier) soldier = GetComponent<SoldierController>();
        if (!life) life = GetComponent<SoldierLife>();
        if (!participant) participant = GetComponent<ArenaParticipant>();
        if (!agent) agent = GetComponent<NavMeshAgent>();
        occupant = GetComponent<VehicleOccupant>();
        if (agent)
        {
            agent.updatePosition = agent.updateRotation = agent.updateUpAxis = false;
            agent.avoidancePriority = 20 + (GetEntityId().GetHashCode() & 63);
        }
    }
    void OnEnable()
    {
        if (life) life.Respawned += ResetIntent;
        if (soldier && soldier.actions) soldier.actions.ShotFired += OnShot;
        ResetIntent();
    }
    void OnDisable()
    {
        if (life) life.Respawned -= ResetIntent;
        if (soldier && soldier.actions) soldier.actions.ShotFired -= OnShot;
        if (agent && agent.enabled && agent.isOnNavMesh) agent.ResetPath();
    }
    public void Configure(ConquestCapturePoint point)
    {
        objective = point;
        float angle = (GetEntityId().GetHashCode() & 255) * 2.399963f;
        goalOffset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (point ? Mathf.Min(3f, point.radius * .4f) : 0);
        ResetIntent();
    }
    void ResetIntent()
    {
        target = null; visible = false; shots = 0; noise = Vector2.zero;
        seenAt = float.NegativeInfinity;
        nextPath = pauseUntil = nextSemi = nextNoise = 0;
        nextScan = Time.time + Random.value * perceptionInterval;
        firingSightTarget = null;
        firingSightWeapon = null;
        nextFiringSight = 0;
        if (agent && agent.enabled && agent.isOnNavMesh)
        {
            agent.ResetPath(); agent.isStopped = false;
            if (NavMesh.SamplePosition(transform.position, out var spawn, 1f, Filter)) agent.Warp(spawn.position);
        }
    }
    void OnShot()
    {
        if (++shots < burstRounds) return;
        shots = 0; pauseUntil = Time.time + burstPause;
    }

    public override SoldierInputFrame ReadInput()
    {
        if (!objective || !soldier || !life || !participant || !participant.match || !agent
            || !soldier.actions.enabled || life.health.IsDead || participant.IsAwaitingSpawn
            || !participant.match.IsRunning || (occupant && occupant.IsMounted)) return default;
        if (!agent.enabled)
        {
            if (!NavMesh.SamplePosition(transform.position, out _, .3f, Filter)) return default;
            agent.enabled = true;
        }
        if (!agent.isOnNavMesh) return default;
        agent.nextPosition = transform.position;
        Scan();
        bool alert = target && Time.time - seenAt < aimHoldTime;
        if (Time.time >= nextPath && !agent.pathPending)
        {
            nextPath = Time.time + repathInterval;
            Vector3 requested = objective.transform.position + goalOffset;
            if (NavMesh.SamplePosition(requested, out var nav, 5f, Filter))
            {
                Destination = nav.position;
                if (!agent.hasPath || (agent.destination - Destination).sqrMagnitude > .25f)
                    agent.SetDestination(Destination);
            }
        }
        int selection = ChooseWeapon();
        var ammo = soldier.loadout.EquippedState;
        bool reload = ammo.MagazineAmmo == 0 && ammo.ReserveAmmo > 0;
        bool busy = soldier.actions.IsReloading || soldier.loadout.IsSwitching || selection != 0;
        agent.isStopped = visible;
        agent.speed = soldier.movement.IsRunning ? soldier.movement.runSpeed : soldier.movement.moveSpeed;
        Vector3 direction = alert ? AimPoint(target) - life.look.aimCamera.transform.position
            : agent.hasPath ? agent.steeringTarget - transform.position : transform.forward;
        float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        float pitch = alert ? Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg : 0;
        if (visible)
        {
            float error = initialAimError * (1 - Mathf.Clamp01((Time.time - acquiredAt) / aimSettleTime));
            if (error <= 0) noise = Vector2.zero;
            else if (Time.time >= nextNoise) { noise = Random.insideUnitCircle; nextNoise = Time.time + .3f; }
            yaw += noise.x * error; pitch += noise.y * error;
        }
        // Use the commanded camera basis, including its authored offset, rather
        // than feeding the interpolated body's heading back into the aim loop.
        var camera = life.look.aimCamera.transform;
        Quaternion offset = Quaternion.Inverse(life.look.playerCamera.rotation) * camera.rotation;
        Vector3 forward = Quaternion.Euler(life.look.Pitch, life.look.Yaw, 0) * offset * Vector3.forward;
        float yawError = Mathf.DeltaAngle(alert ? Heading(forward) : life.look.Yaw, yaw);
        float pitchError = pitch - Elevation(forward);
        float step = turnSpeed * Time.deltaTime;
        Vector2 look = new Vector2(Mathf.Clamp(yawError, -step, step), Mathf.Clamp(pitchError, -step, step));
        bool arrived = Vector3.ProjectOnPlane(Destination - transform.position, Vector3.up).sqrMagnitude < 1.5f;
        Vector3 velocity = visible || arrived || !agent.hasPath || agent.pathStatus != NavMeshPathStatus.PathComplete
            ? Vector3.zero : agent.desiredVelocity;
        Vector3 local = Quaternion.Inverse(Quaternion.Euler(0, life.look.Yaw + look.x, 0)) * velocity / Mathf.Max(.01f, agent.speed);
        bool fire = visible && !busy && !reload && Time.time >= readyAt && Time.time >= pauseUntil
            && Mathf.Abs(Mathf.DeltaAngle(Heading(camera.forward), yaw)) < 4f
            && Mathf.Abs(Elevation(camera.forward) - pitch) < 4f && FiringLineClear(target);
        bool pressed = fire && Time.time >= nextSemi;
        if (pressed) nextSemi = Time.time + Mathf.Max(semiAutoInterval, 60f / Mathf.Max(1, soldier.actions.roundsPerMinute));
        return new SoldierInputFrame {
            move = Vector2.ClampMagnitude(new Vector2(local.x, local.z), 1), lookDelta = look,
            aim = alert, sprint = !alert && !busy && !reload && !arrived && agent.remainingDistance > sprintDistance && Mathf.Abs(yawError) < 20,
            fireHeld = fire, firePressed = pressed, reload = reload && selection == 0, weaponSelection = selection
        };
    }
    NavMeshQueryFilter Filter => new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
    int ChooseWeapon()
    {
        var loadout = soldier.loadout;
        if (loadout.IsSwitching || soldier.actions.IsReloading || loadout.AvailableSlots.Length == 0) return 0;
        var primary = loadout.slots[loadout.AvailableSlots[0]].state;
        int desired = primary.MagazineAmmo == 0 && primary.ReserveAmmo == 0 && loadout.AvailableSlots.Length > 1 ? 1 : 0;
        return loadout.selectedSlot == loadout.AvailableSlots[desired] ? 0 : desired + 1;
    }
    bool Eligible(ArenaParticipant enemy)
    {
        if (!enemy || !enemy.isActiveAndEnabled || enemy == participant || enemy.team == participant.team
            || enemy.match != participant.match || enemy.health.IsDead || enemy.IsAwaitingSpawn || enemy.IsSpectator) return false;
        var input = enemy.GetComponent<SandboxInfantryInput>();
        if (!input || input.objective != objective) return false;
        var rider = enemy.GetComponent<VehicleOccupant>();
        return !rider || !rider.IsMounted;
    }
    void Scan()
    {
        if (!Eligible(target)) { target = null; visible = false; }
        if (Time.time < nextScan) return;
        nextScan = Time.time + perceptionInterval;
        ArenaParticipant found = target && CanSee(target) ? target : null;
        float nearest = sightRange * sightRange;
        if (!found) foreach (var enemy in participant.match.participants)
        {
            if (!Eligible(enemy)) continue;
            float distance = (AimPoint(enemy) - life.look.aimCamera.transform.position).sqrMagnitude;
            if (distance < nearest && CanSee(enemy)) { nearest = distance; found = enemy; }
        }
        visible = found;
        if (found)
        {
            if (target != found || Time.time - seenAt > aimHoldTime)
            { acquiredAt = Time.time; readyAt = Time.time + reactionTime; }
            target = found; seenAt = Time.time;
        }
        else if (Time.time - seenAt >= aimHoldTime) target = null;
    }
    bool CanSee(ArenaParticipant enemy)
    {
        Vector3 delta = AimPoint(enemy) - life.look.aimCamera.transform.position;
        return delta.sqrMagnitude <= sightRange * sightRange
            && Vector3.Angle(Quaternion.Euler(0, life.look.Yaw, 0) * Vector3.forward, delta) <= fieldOfView * .5f
            && ClearShot(enemy);
    }
    bool ClearShot(ArenaParticipant enemy)
    {
        Vector3 from = life.look.aimCamera.transform.position, to = AimPoint(enemy), delta = to - from;
        if (GrenadeSmoke.BlocksLine(from, to)) return false;
        int count = Physics.RaycastNonAlloc(from, delta.normalized, hits, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        // A saturated buffer cannot establish a clear shot safely.
        if (count == hits.Length) return false;
        float closest = delta.magnitude; Collider blocker = null;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.transform.IsChildOf(transform) || SoldierPhysicsBody.IsMotorCollider(hit.collider)) continue;
            if (hit.collider.isTrigger && !hit.collider.GetComponent<SoldierHitRegion>()) continue;
            if (hit.distance <= closest) { closest = hit.distance; blocker = hit.collider; }
        }
        return !blocker || blocker.GetComponentInParent<ArenaParticipant>() == enemy;
    }
    bool FiringLineClear(ArenaParticipant enemy)
    {
        var weapon = soldier.loadout.ThirdPersonView;
        Vector3 origin = life.look.aimCamera.transform.position;
        var muzzle = soldier.loadout.EquippedMuzzle;
        Vector3 muzzlePosition = muzzle ? muzzle.position : origin;
        Vector3 point = AimPoint(enemy);
        // Perception retains its normal cadence. Only the extra firing permission
        // ray is cached; every actual shot still performs its own collision query.
        if (enemy != firingSightTarget || weapon != firingSightWeapon || Time.time >= nextFiringSight
            || (origin - firingSightOrigin).sqrMagnitude > .0625f
            || (muzzlePosition - firingSightMuzzle).sqrMagnitude > .0625f
            || (point - firingSightPoint).sqrMagnitude > .16f)
        {
            firingSightTarget = enemy; firingSightWeapon = weapon;
            firingSightOrigin = origin; firingSightMuzzle = muzzlePosition; firingSightPoint = point;
            nextFiringSight = Time.time + .06f;
            firingSightClear = ClearShot(enemy);
        }
        return firingSightClear;
    }
    static Vector3 AimPoint(ArenaParticipant enemy) => enemy.transform.position + Vector3.up * 1.25f;
    static float Heading(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
    static float Elevation(Vector3 direction) => Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
}
