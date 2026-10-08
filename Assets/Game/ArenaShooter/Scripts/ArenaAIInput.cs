using UnityEngine;
using UnityEngine.AI;

// Action selection -> steering -> locomotion: https://www.red3d.com/cwr/steer/gdc99/
// Explicit combat/search states: Sergio Ocio, Game AI Pro 3, chapter 7.
public class ArenaAIInput : SoldierInputSource
{
    public enum Behavior { Inactive, Patrol, Search, Approach, Strafe, MoveToCover, Hide, Peek }
    public SoldierController soldier;
    public SoldierLife life;
    public ArenaParticipant participant;
    public NavMeshAgent agent;
    [Header("Perception and movement")]
    public float sightRange = 45f;
    [Range(0f, 180f)] public float fieldOfView = 140f;
    public float attackRange = 18f;
    public float turnSpeed = 180f;
    public float reactionTime = .35f;
    public float memoryDuration = 4f;
    public float repathInterval = .3f;
    public float sprintDistance = 8f;
    public float strafeDistance = 2f;
    public Vector2 repositionInterval = new Vector2(1.5f, 3f);
    [Header("Cover")]
    public float coverSearchRadius = 12f;
    public float coverDecisionInterval = 5f;
    [Range(0f, 1f)] public float coverChance = .65f;
    [Range(0f, 1f)] public float retreatHealth = .4f;
    public Vector2 hideDuration = new Vector2(.6f, 1.2f);
    public Vector2 peekDuration = new Vector2(1f, 2f);
    [Header("Aim and bursts")]
    public float initialAimError = 3f;
    public float settledAimError = 1f;
    public float aimSettleTime = 1.5f;
    public float aimTrackingTime = .18f;
    public float aimNoiseInterval = .4f;
    public Vector2 burstDuration = new Vector2(.5f, 1f);
    public Vector2 burstPause = new Vector2(.35f, .8f);
    public float semiAutoInterval = .25f;
    [Header("Weapon choice")]
    [Tooltip("One-based positions in the arena loadout, like keyboard weapon selection.")]
    [Min(1)] public int primaryWeapon = 1;
    [Min(1)] public int sidearmWeapon = 2;
    public float sidearmRange = 7f;
    public float weaponDecisionInterval = 2f;
    public Behavior State { get; private set; }
    public Vector3 Destination { get; private set; }
    public ArenaParticipant Target => target;
    public bool HasCover { get; private set; }
    public Vector3 CoverPosition => cover.position;
    public Vector3 PeekPosition => cover.peek;
    float ArrivalDistance => Mathf.Max(.35f, agent.stoppingDistance + .1f);

    ArenaParticipant target;
    Vector3 lastSeen, trackedAim, progressPosition;
    Vector2 aimNoise, desiredNoise;
    ArenaAITactics.Cover cover;
    NavMeshPath queryPath;
    float seenAt = float.NegativeInfinity, acquiredAt, readyAt, nextPath;
    float nextCover, nextReposition, phaseUntil, nextNoise, burstUntil, pauseUntil, nextSemi, nextWeapon;
    float progressAt, recoveryUntil;
    int patrolIndex;
    bool wasVisible;

    void Awake()
    {
        queryPath = new NavMeshPath();
        agent.updatePosition = agent.updateRotation = agent.updateUpAxis = false;
    }
    void OnEnable() { life.Respawned += ResetIntent; life.health.Died += OnDied; }
    void OnDisable() { life.Respawned -= ResetIntent; life.health.Died -= OnDied; ResetIntent(); }
    void OnDied(GameObject source) => ResetIntent();

    void ResetIntent()
    {
        target = null; HasCover = wasVisible = false; State = Behavior.Inactive;
        seenAt = float.NegativeInfinity;
        nextPath = nextCover = nextReposition = nextWeapon = nextNoise = 0f;
        burstUntil = pauseUntil = nextSemi = phaseUntil = recoveryUntil = 0f;
        aimNoise = desiredNoise = Vector2.zero;
        progressPosition = Destination = transform.position; progressAt = Time.time;
        if (agent.isOnNavMesh) agent.ResetPath();
        if (NavMesh.SamplePosition(transform.position, out var hit, 2f, agent.areaMask)) agent.Warp(hit.position);
    }

    public override SoldierInputFrame ReadInput()
    {
        if (!soldier.actions.enabled || !participant.match.IsRunning || !agent.isOnNavMesh)
        { State = Behavior.Inactive; return default; }
        agent.nextPosition = transform.position;
        bool visible = UpdatePerception();
        float distance = target ? Vector3.Distance(transform.position, lastSeen) : float.PositiveInfinity;
        int selection = ChooseWeapon(distance);
        bool reload = soldier.loadout.EquippedState.MagazineAmmo == 0 && soldier.loadout.EquippedState.ReserveAmmo > 0;
        if (Time.time >= recoveryUntil) ChooseMovement(visible, distance, reload);
        RecoverStuckPath();
        bool wantsAim = visible && distance <= attackRange && State != Behavior.MoveToCover && State != Behavior.Hide;
        bool busy = soldier.actions.IsReloading || soldier.loadout.IsSwitching || selection != 0;
        bool faceMemory = target && (State == Behavior.Peek || State == Behavior.Hide
            || State == Behavior.Search && HorizontalDistance(transform.position, Destination) < 1f);
        Vector3 direction = wantsAim ? GetImperfectAim() - life.look.aimCamera.transform.position
            : faceMemory ? lastSeen + Vector3.up * 1.25f - life.look.aimCamera.transform.position
            : agent.steeringTarget - transform.position;
        float yaw = direction.x * direction.x + direction.z * direction.z > .001f
            ? Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg : transform.eulerAngles.y;
        float up = wantsAim ? Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg : 0f;
        if (wantsAim) { yaw += aimNoise.x; up += aimNoise.y; }
        float yawError = Mathf.DeltaAngle(transform.eulerAngles.y, yaw), pitchError = up + life.look.Pitch;
        float step = turnSpeed * Time.deltaTime;
        var look = new Vector2(Mathf.Clamp(yawError, -step, step), Mathf.Clamp(pitchError, -step, step));
        bool arrived = HorizontalDistance(transform.position, Destination) <= ArrivalDistance;
        bool sprint = !wantsAim && !busy && !reload && !arrived
            && Vector3.Distance(transform.position, Destination) > sprintDistance && Mathf.Abs(yawError) < 25f;
        agent.speed = sprint ? soldier.movement.runSpeed : soldier.movement.moveSpeed;
        Vector3 velocity = arrived ? Vector3.zero : agent.desiredVelocity;
        // The controller applies yaw before movement.
        Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, transform.eulerAngles.y + look.x, 0f))
            * velocity / Mathf.Max(agent.speed, .01f);
        bool fire = wantsAim && !busy && !reload && Time.time >= readyAt
            && Mathf.Abs(yawError) < 4f && Mathf.Abs(pitchError) < 4f && InBurst();
        bool pressed = fire && Time.time >= nextSemi;
        if (pressed) nextSemi = Time.time + Mathf.Max(semiAutoInterval, 60f / soldier.actions.roundsPerMinute);
        wasVisible = visible;
        return new SoldierInputFrame {
            move = Vector2.ClampMagnitude(new Vector2(local.x, local.z), 1f), lookDelta = look,
            aim = wantsAim && !busy, sprint = sprint, fireHeld = fire, firePressed = pressed,
            reload = reload && selection == 0, weaponSelection = selection
        };
    }

    bool UpdatePerception()
    {
        ArenaParticipant visible = null; float nearest = sightRange * sightRange;
        foreach (var enemy in participant.match.participants)
        {
            if (enemy.team == participant.team || enemy.health.IsDead) continue;
            float distance = (AimPoint(enemy) - life.look.aimCamera.transform.position).sqrMagnitude;
            float score = enemy == target ? distance * .8f : distance;
            if (distance <= sightRange * sightRange && score < nearest && CanSee(enemy))
            { nearest = score; visible = enemy; }
        }
        if (visible)
        {
            if (target != visible || !wasVisible)
            {
                readyAt = Time.time + reactionTime; acquiredAt = Time.time; trackedAim = AimPoint(visible);
                burstUntil = pauseUntil = 0f;
            }
            if (target != visible) { HasCover = false; nextCover = nextReposition = 0f; }
            target = visible; lastSeen = visible.transform.position; seenAt = Time.time;
        }
        else if (target && (target.health.IsDead || Time.time - seenAt > memoryDuration))
        { target = null; HasCover = false; nextReposition = 0f; }
        return visible;
    }

    int ChooseWeapon(float distance)
    {
        if (Time.time < nextWeapon || soldier.actions.IsReloading || soldier.loadout.IsSwitching) return 0;
        var loadout = soldier.loadout;
        bool holdingSidearm = System.Array.IndexOf(loadout.AvailableSlots, loadout.selectedSlot) == sidearmWeapon - 1;
        int desired = target && distance < sidearmRange + (holdingSidearm ? 2f : 0f) ? sidearmWeapon : primaryWeapon;
        desired = Mathf.Clamp(desired, 1, loadout.AvailableSlots.Length);
        var state = loadout.slots[loadout.AvailableSlots[desired - 1]].state;
        if (state.MagazineAmmo + state.ReserveAmmo == 0 ||
            (loadout.EquippedState.MagazineAmmo == 0 && target && distance < sidearmRange))
        {
            int usable = 0;
            for (int i = 0; i < loadout.AvailableSlots.Length; i++)
            {
                var alternate = loadout.slots[loadout.AvailableSlots[i]].state;
                if (alternate.MagazineAmmo > 0) { usable = i + 1; break; }
                if (usable == 0 && alternate.ReserveAmmo > 0) usable = i + 1;
            }
            if (usable > 0) desired = usable;
        }
        if (loadout.AvailableSlots[desired - 1] == loadout.selectedSlot) return 0;
        nextWeapon = Time.time + weaponDecisionInterval;
        return desired;
    }

    void ChooseMovement(bool visible, float distance, bool needsReload)
    {
        if (!target)
        {
            HasCover = false; State = Behavior.Patrol;
            var patrol = participant.team == ArenaParticipant.Team.Red ? participant.match.blueSpawns : participant.match.redSpawns;
            Vector3 point = patrol[patrolIndex % patrol.Length].position;
            if (HorizontalDistance(transform.position, point) < 2f)
            { patrolIndex++; point = patrol[patrolIndex % patrol.Length].position; }
            SetDestination(point); return;
        }
        Vector3 threat = lastSeen + Vector3.up * 1.25f;
        bool mustHide = needsReload || soldier.actions.IsReloading;
        bool defensive = mustHide || life.health.CurrentHealth / life.health.maxHealth < retreatHealth;
        if (Time.time >= nextCover && (defensive || visible && distance < attackRange))
        {
            nextCover = Time.time + coverDecisionInterval;
            if ((defensive || Random.value < coverChance) && ArenaAITactics.FindCover(this, threat, queryPath, out var found))
            { cover = found; HasCover = true; State = Behavior.MoveToCover; phaseUntil = 0f; }
        }
        if (HasCover)
        {
            if (!ArenaAITactics.WorldBlocked(this, threat, cover.position + Vector3.up * 1.25f)) HasCover = false;
            else
            {
                if (State == Behavior.Peek && (mustHide || Time.time >= phaseUntil))
                { State = Behavior.MoveToCover; phaseUntil = 0f; }
                if (State != Behavior.Peek)
                {
                    SetDestination(cover.position);
                    if (HorizontalDistance(transform.position, cover.position) <= ArrivalDistance)
                    {
                        State = Behavior.Hide;
                        if (phaseUntil == 0f) phaseUntil = Time.time + Random.Range(hideDuration.x, hideDuration.y);
                        if (!mustHide && Time.time >= phaseUntil)
                        { State = Behavior.Peek; phaseUntil = Time.time + Random.Range(peekDuration.x, peekDuration.y); }
                    }
                    else State = Behavior.MoveToCover;
                }
                if (State == Behavior.Peek) SetDestination(cover.peek);
                return;
            }
        }
        if (!visible) { State = Behavior.Search; SetDestination(lastSeen); return; }
        if (distance > attackRange) { State = Behavior.Approach; SetDestination(lastSeen); return; }
        State = Behavior.Strafe;
        if (Time.time >= nextReposition)
        {
            nextReposition = Time.time + Random.Range(repositionInterval.x, repositionInterval.y);
            if (ArenaAITactics.FindCombatPosition(this, threat, queryPath, out var point)) SetDestination(point, true);
            else SetDestination(transform.position, true);
        }
        else SetDestination(Destination);
    }

    void SetDestination(Vector3 point, bool force = false)
    {
        Destination = point;
        if (!force && Time.time < nextPath) return;
        agent.SetDestination(point); nextPath = Time.time + Mathf.Max(.05f, repathInterval);
    }

    void RecoverStuckPath()
    {
        if (Time.time - progressAt < 1.5f) return;
        bool blocked = agent.desiredVelocity.sqrMagnitude > .25f && HorizontalDistance(progressPosition, transform.position) < .2f;
        progressPosition = transform.position; progressAt = Time.time;
        if (!blocked) return;
        HasCover = false; agent.ResetPath(); nextPath = nextReposition = 0f;
        if (ArenaAITactics.FindCombatPosition(this, lastSeen + Vector3.up * 1.25f, queryPath, out var point))
        { SetDestination(point, true); recoveryUntil = Time.time + 2f; }
    }

    Vector3 GetImperfectAim()
    {
        // Accuracy as difficulty control: Sergio Ocio, Game AI Pro 3, chapter 33.
        trackedAim = Vector3.Lerp(trackedAim, AimPoint(target), 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(.01f, aimTrackingTime)));
        if (Time.time >= nextNoise)
        {
            float settled = Mathf.Clamp01((Time.time - acquiredAt) / Mathf.Max(.01f, aimSettleTime));
            float error = Mathf.Lerp(initialAimError, settledAimError, settled);
            if (agent.desiredVelocity.sqrMagnitude > .25f) error *= 1.5f;
            desiredNoise = Random.insideUnitCircle * error; nextNoise = Time.time + Mathf.Max(.05f, aimNoiseInterval);
        }
        aimNoise = Vector2.Lerp(aimNoise, desiredNoise, 1f - Mathf.Exp(-Time.deltaTime * 8f));
        return trackedAim;
    }

    bool InBurst()
    {
        if (Time.time < pauseUntil) return false;
        if (burstUntil == 0f) burstUntil = Time.time + Random.Range(burstDuration.x, burstDuration.y);
        if (Time.time < burstUntil) return true;
        burstUntil = 0f; pauseUntil = Time.time + Random.Range(burstPause.x, burstPause.y); return false;
    }

    Vector3 AimPoint(ArenaParticipant enemy) => enemy.transform.position + Vector3.up * 1.25f;
    static float HorizontalDistance(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;
    bool CanSee(ArenaParticipant enemy)
    {
        Vector3 origin = life.look.aimCamera.transform.position, point = AimPoint(enemy);
        if (Vector3.Angle(transform.forward, point - origin) > fieldOfView * .5f) return false;
        return ArenaAITactics.ClearShot(this, origin, point, enemy);
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = Color.cyan; Gizmos.DrawLine(transform.position, Destination); Gizmos.DrawWireSphere(Destination, .3f);
        if (!HasCover) return;
        Gizmos.color = Color.green; Gizmos.DrawWireSphere(cover.position, .4f);
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(cover.peek, .4f);
    }
}
