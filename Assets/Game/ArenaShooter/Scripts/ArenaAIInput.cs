using UnityEngine;
using UnityEngine.AI;

// Arena-specific decisions; the shared controller owns movement and weapon actions.
public class ArenaAIInput : SoldierInputSource
{
    public SoldierController soldier;
    public SoldierLife life;
    public ArenaParticipant participant;
    public NavMeshAgent agent;
    public float sightRange = 45f;
    [Range(0f, 180f)] public float fieldOfView = 140f;
    public float attackRange = 18f;
    public float turnSpeed = 180f;
    public float reactionTime = .35f;
    public float memoryDuration = 4f;
    public float repathInterval = .3f;

    ArenaParticipant target;
    Vector3 lastSeen;
    float seenAt = float.NegativeInfinity, readyAt, nextPath;
    int patrolIndex;

    void Awake()
    {
        agent.updatePosition = false;
        agent.updateRotation = false;
        agent.updateUpAxis = false;
    }

    void OnEnable()
    {
        life.Respawned += ResetIntent;
        life.health.Died += OnDied;
    }

    void OnDisable()
    {
        life.Respawned -= ResetIntent;
        life.health.Died -= OnDied;
        ResetIntent();
    }

    void OnDied(GameObject source) => ResetIntent();

    void ResetIntent()
    {
        target = null;
        seenAt = float.NegativeInfinity;
        nextPath = 0f;
        if (agent.isOnNavMesh) agent.ResetPath();
        if (NavMesh.SamplePosition(transform.position, out var hit, 2f, agent.areaMask))
            agent.Warp(hit.position);
    }

    public override SoldierInputFrame ReadInput()
    {
        if (!soldier.actions.enabled || !participant.match.IsRunning) return default;
        if (!agent.isOnNavMesh) return default;
        agent.nextPosition = transform.position;
        agent.speed = soldier.movement.moveSpeed;

        ArenaParticipant visible = null;
        float nearest = sightRange * sightRange;
        foreach (var enemy in participant.match.participants)
        {
            if (enemy.team == participant.team || enemy.health.IsDead) continue;
            float distance = (AimPoint(enemy) - life.look.aimCamera.transform.position).sqrMagnitude;
            if (distance < nearest && CanSee(enemy)) { nearest = distance; visible = enemy; }
        }
        if (visible)
        {
            if (target != visible) readyAt = Time.time + reactionTime;
            target = visible;
            lastSeen = visible.transform.position;
            seenAt = Time.time;
        }
        else if (target && (target.health.IsDead || Time.time - seenAt > memoryDuration)) target = null;

        // Patrol opposing spawn areas until sight provides an actual target.
        var patrol = participant.team == ArenaParticipant.Team.Red
            ? participant.match.blueSpawns : participant.match.redSpawns;
        Vector3 destination = target ? lastSeen : patrol[patrolIndex % patrol.Length].position;
        if (!target && Vector3.Distance(transform.position, destination) < 2f) patrolIndex++;
        if (Time.time >= nextPath)
        {
            agent.SetDestination(destination);
            nextPath = Time.time + repathInterval;
        }

        bool inRange = visible && nearest < attackRange * attackRange;
        Vector3 direction = visible ? AimPoint(visible) - life.look.aimCamera.transform.position
            : agent.steeringTarget - transform.position;
        float yaw = direction.x * direction.x + direction.z * direction.z > .001f
            ? Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg : transform.eulerAngles.y;
        float up = visible ? Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg : 0f;
        float yawError = Mathf.DeltaAngle(transform.eulerAngles.y, yaw);
        float pitchError = up + life.look.Pitch;
        float step = turnSpeed * Time.deltaTime;
        Vector2 look = new Vector2(Mathf.Clamp(yawError, -step, step), Mathf.Clamp(pitchError, -step, step));
        Vector3 velocity = inRange ? Vector3.zero : agent.desiredVelocity;
        // The controller applies this frame's turn before its local movement.
        Quaternion facing = Quaternion.Euler(0f, transform.eulerAngles.y + look.x, 0f);
        Vector3 local = Quaternion.Inverse(facing) * velocity / Mathf.Max(agent.speed, .01f);
        bool fire = inRange && Time.time >= readyAt && Mathf.Abs(yawError) < 4f && Mathf.Abs(pitchError) < 4f;
        return new SoldierInputFrame {
            move = Vector2.ClampMagnitude(new Vector2(local.x, local.z), 1f),
            lookDelta = look, aim = inRange,
            fireHeld = fire, firePressed = fire,
            reload = soldier.loadout.EquippedState.MagazineAmmo == 0 && soldier.loadout.EquippedState.ReserveAmmo > 0
        };
    }

    Vector3 AimPoint(ArenaParticipant enemy) => enemy.transform.position + Vector3.up * 1.25f;

    bool CanSee(ArenaParticipant enemy)
    {
        Vector3 origin = life.look.aimCamera.transform.position;
        Vector3 delta = AimPoint(enemy) - origin;
        if (Vector3.Angle(transform.forward, delta) > fieldOfView * .5f) return false;
        float closest = delta.magnitude;
        Collider blocker = null;
        foreach (var hit in Physics.RaycastAll(origin, delta.normalized, closest, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.collider is CharacterController) continue;
            if (hit.collider.isTrigger && !hit.collider.GetComponent<SoldierHitRegion>()) continue;
            if (hit.distance <= closest) { closest = hit.distance; blocker = hit.collider; }
        }
        return !blocker || blocker.GetComponentInParent<ArenaParticipant>() == enemy;
    }
}
