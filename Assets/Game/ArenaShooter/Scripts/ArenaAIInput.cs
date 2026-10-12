using UnityEngine;
using UnityEngine.AI;

// Action selection -> steering -> locomotion: https://www.red3d.com/cwr/steer/gdc99/
// Explicit combat/search states: Sergio Ocio, Game AI Pro 3, chapter 7.
public class ArenaAIInput : SoldierInputSource
{
    public enum Behavior { AttackVehicle, Inactive, Idle, Patrol, Search, Approach, Strafe, MoveToCover, Hide, Peek, BoardVehicle }
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
    public float perceptionInterval = .15f;
    public float sprintDistance = 14f;
    [Tooltip("Keep sprinting until this close to the destination. Must be below Sprint Distance.")]
    public float sprintStopDistance = 6f;
    [Header("Combat readiness")]
    [Min(0f)] public float combatReadyDuration = 6f;
    [Tooltip("Hold ADS through brief occlusion and changes of target; this does not permit firing without sight.")]
    [Min(0f)] public float aimHoldDuration = .8f;
    [Min(0f)] public float hearingRange = 20f;
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
    // Optional transport intent supplied by the arena vehicle policy.
    public Transform BoardingTarget { get; set; }
    public Behavior State { get; private set; }
    public Vector3 Destination { get; private set; }
    public ArenaParticipant Target => target;
    public bool HasCover { get; private set; }
    public Vector3 CoverPosition => cover.position;
    public Vector3 PeekPosition => cover.peek;
    public bool WantsToSprint { get; private set; }
    public bool IsCombatAlert => Time.time < combatAlertUntil;
    public float LastCombatTime { get; private set; } = float.NegativeInfinity;
    float ArrivalDistance => Mathf.Max(.35f, agent.stoppingDistance + .1f);

    ArenaParticipant target;
    Vector3 lastSeen, trackedAim, progressPosition;
    Vector2 aimNoise, desiredNoise;
    ArenaAITactics.Cover cover;
    NavMeshPath queryPath;
    float seenAt = float.NegativeInfinity, acquiredAt, readyAt, nextPath;
    float nextCover, nextReposition, phaseUntil, nextNoise, burstUntil, pauseUntil, nextSemi, nextWeapon;
    float progressAt, recoveryUntil;
    Vector3 patrolDestination;
    float patrolWaitUntil, nextPerception;
    int lastPatrolIndex = -1;
    bool hasPatrolDestination, targetVisible;
    float aimHoldUntil;
    int burstShots, burstShotLimit;
    float combatAlertUntil, heardAt = float.NegativeInfinity, investigationUntil, nextLook;
    Vector3 investigationPoint, openLook;
    bool investigating;
    Vehicle launcherTarget;
    float nextVehicleScan;
    [Header("Anti-vehicle utility")]
    public float launcherRange=80f;
    public float launcherSafeDistance=12f;
    public float launcherScanInterval=.4f;

    bool TryVehicleAttack(out SoldierInputFrame input)
    {
        input=default;
        var utility=soldier.launchers;
        if(!utility)return false;
        if(Time.time>=nextVehicleScan)
        {
            nextVehicleScan=Time.time+launcherScanInterval;launcherTarget=null;
            float closest=launcherRange;
            foreach(var vehicle in Vehicle.Active)
            {
                if(vehicle.IsDestroyed||!vehicle.IsOccupied)continue;
                bool enemy=false,ally=false;
                foreach(var seat in vehicle.seats)if(seat.occupant){var actor=seat.occupant.GetComponent<ArenaParticipant>();if(actor){if(actor.team==participant.team)ally=true;else enemy=true;}}
                if(!enemy||ally)continue;
                float distance=Vector3.Distance(transform.position,vehicle.transform.position);
                if(distance<launcherSafeDistance||distance>closest)continue;
                if(!VehicleShotClear(vehicle,out _))continue;
                launcherTarget=vehicle;closest=distance;
            }
        }
        int available=-1;
        for(int i=0;i<utility.types.Length;i++)if(utility.Count(i)>0){available=i;break;}
        if(available<0||!launcherTarget||launcherTarget.IsDestroyed||!launcherTarget.IsOccupied||!VehicleShotClear(launcherTarget,out var point))
        {
            if(utility.IsEquipped){input.weaponSelection=1;return true;}
            return false;
        }
        // Hold position for the shot; rifle combat and navigation resume afterward.
        State=Behavior.AttackVehicle;WantsToSprint=false;combatAlertUntil=Time.time+combatReadyDuration;
        Vector3 direction=point-life.look.aimCamera.transform.position;
        float yaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
        float pitch=Mathf.Atan2(direction.y,new Vector2(direction.x,direction.z).magnitude)*Mathf.Rad2Deg;
        float yawError=Mathf.DeltaAngle(transform.eulerAngles.y,yaw),pitchError=pitch+life.look.Pitch,step=turnSpeed*Time.deltaTime;
        input.lookDelta=new Vector2(Mathf.Clamp(yawError,-step,step),Mathf.Clamp(pitchError,-step,step));
        input.utilitySelection=!utility.IsEquipped||utility.Selected!=available?available+1:0;
        input.firePressed=utility.IsEquipped&&utility.Selected==available&&utility.Ready&&Mathf.Abs(yawError)<3&&Mathf.Abs(pitchError)<3;
        return true;
    }
    bool VehicleShotClear(Vehicle vehicle,out Vector3 point)
    {
        point=vehicle.Body.worldCenterOfMass;
        Vector3 from=life.look.aimCamera.transform.position,delta=point-from;
        foreach(var hit in Physics.RaycastAll(from,delta.normalized,delta.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if(!hit.transform.IsChildOf(transform)&&!hit.transform.IsChildOf(vehicle.transform))return false;
        // Avoid deliberately firing into allies clustered around the impact.
        foreach(var actor in participant.match.participants)
            if(actor!=participant&&actor.team==participant.team&&!actor.health.IsDead&&Vector3.Distance(actor.transform.position,point)<5)return false;
        return true;
    }

    void Awake()
    {
        queryPath = new NavMeshPath();
        agent.updatePosition = agent.updateRotation = agent.updateUpAxis = false;
    }
    void OnEnable() { life.Respawned += ResetIntent; life.health.Died += OnDied; life.health.Damaged += OnDamaged; soldier.actions.FireStarted += OnShot; }
    void OnDisable() { life.Respawned -= ResetIntent; life.health.Died -= OnDied; life.health.Damaged -= OnDamaged; soldier.actions.FireStarted -= OnShot; ResetIntent(); }
    void OnDied(GameObject source) => ResetIntent();
    void OnShot() { burstShots++; LastCombatTime = Time.time; }
    void OnDamaged(float amount, GameObject source)
    {
        combatAlertUntil = Time.time + combatReadyDuration;
        LastCombatTime = Time.time;
        if (source && !target) Investigate(source.transform.position);
    }
    void Investigate(Vector3 point)
    {
        // A sound/damage location is evidence, not a visible target to shoot through walls.
        if (!ArenaAITactics.Reachable(this, point, queryPath, out investigationPoint, out _)) return;
        investigating = true; investigationUntil = Time.time + memoryDuration;
        hasPatrolDestination = false; patrolWaitUntil = 0f;
    }

    void ResetIntent()
    {
        target = null; HasCover = false; State = Behavior.Inactive;
        aimHoldUntil = 0f; burstShots = burstShotLimit = 0; LastCombatTime = float.NegativeInfinity;
        WantsToSprint = false;
        combatAlertUntil = investigationUntil = nextLook = 0f;
        heardAt = float.NegativeInfinity; investigating = false; openLook = transform.forward;
        seenAt = float.NegativeInfinity;
        nextPath = nextCover = nextReposition = nextWeapon = nextNoise = 0f;
        nextPerception = Time.time + Random.value * perceptionInterval;
        nextCover = Time.time + Random.value;
        patrolWaitUntil = 0f;
        hasPatrolDestination = targetVisible = false;
        lastPatrolIndex = -1;
        burstUntil = pauseUntil = nextSemi = phaseUntil = recoveryUntil = 0f;
        aimNoise = desiredNoise = Vector2.zero;
        progressPosition = Destination = transform.position; progressAt = Time.time;
        if (!agent.isActiveAndEnabled) return;
        if (agent.isOnNavMesh) agent.ResetPath();
        if (NavMesh.SamplePosition(transform.position, out var hit, 2f, new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask })) agent.Warp(hit.position);
    }

    public override SoldierInputFrame ReadInput()
    {
        if (!soldier.actions.enabled || participant.IsAwaitingSpawn || !participant.match.IsRunning || !agent.isOnNavMesh)
        { State = Behavior.Inactive; WantsToSprint = false; return default; }
        if(soldier.grenades.FlashAlpha>.1f){WantsToSprint=false;return default;}
        agent.nextPosition = transform.position;
        bool visible = UpdatePerception();
        // A visible infantry threat takes priority over scanning for launcher targets.
        if(!BoardingTarget && !visible && Time.time >= aimHoldUntil && TryVehicleAttack(out var vehicleInput))return vehicleInput;
        float distance = target ? Vector3.Distance(transform.position, lastSeen) : float.PositiveInfinity;
        int selection = ChooseWeapon(distance);
        if (visible && soldier.launchers && soldier.launchers.IsEquipped)
            selection = System.Array.IndexOf(soldier.loadout.AvailableSlots, soldier.loadout.selectedSlot) + 1;
        bool reload = soldier.loadout.EquippedState.MagazineAmmo == 0 && soldier.loadout.EquippedState.ReserveAmmo > 0;
        if (BoardingTarget)
        { State = Behavior.BoardVehicle; SetDestination(BoardingTarget.position); }
        else if (Time.time >= recoveryUntil) ChooseMovement(visible, distance, reload);
        RecoverStuckPath();
        if (visible) aimHoldUntil = Time.time + aimHoldDuration;
        bool faceThreat = !BoardingTarget && (target || Time.time < aimHoldUntil);
        bool wantsAim = faceThreat && (visible || Time.time < aimHoldUntil)
            && (visible || State != Behavior.MoveToCover && State != Behavior.Hide);
        bool busy = soldier.actions.IsReloading || soldier.loadout.IsSwitching || selection != 0;
        Vector3 direction = faceThreat ? (visible ? GetImperfectAim() : lastSeen + Vector3.up * 1.25f) - life.look.aimCamera.transform.position
            : agent.steeringTarget - transform.position;
        float yaw = direction.x * direction.x + direction.z * direction.z > .001f
            ? Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg : transform.eulerAngles.y;
        float up = faceThreat ? Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg : 0f;
        if (visible && faceThreat) { yaw += aimNoise.x; up += aimNoise.y; }
        if (!faceThreat)
        {
            if (State == Behavior.Hide) direction = cover.peek - transform.position;
            direction = GetOpenLook(direction);
            yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }
        float yawError = Mathf.DeltaAngle(transform.eulerAngles.y, yaw), pitchError = up + life.look.Pitch;
        float step = turnSpeed * Time.deltaTime;
        var look = new Vector2(Mathf.Clamp(yawError, -step, step), Mathf.Clamp(pitchError, -step, step));
        bool arrived = HorizontalDistance(transform.position, Destination) <= ArrivalDistance;
        float travelRemaining = agent.hasPath && !agent.pathPending && !float.IsInfinity(agent.remainingDistance)
            ? agent.remainingDistance : Vector3.Distance(transform.position, Destination);
        bool travelState = State == Behavior.Patrol && !target && !investigating && !IsCombatAlert;
        float sprintThreshold = WantsToSprint ? Mathf.Min(sprintStopDistance, sprintDistance) : sprintDistance;
        WantsToSprint = travelState && !wantsAim && !busy && !reload && !arrived
            && travelRemaining > sprintThreshold && HorizontalDistance(transform.position, agent.steeringTarget) > 4f
            && Mathf.Abs(yawError) < (WantsToSprint ? 30f : 15f);
        // Plan at the speed the shared movement actually admits (grounding and ADS can delay sprint).
        agent.speed = soldier.movement.IsRunning ? soldier.movement.runSpeed : soldier.movement.moveSpeed;
        Vector3 velocity = arrived ? Vector3.zero : agent.desiredVelocity;
        // The controller applies yaw before movement.
        Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, transform.eulerAngles.y + look.x, 0f))
            * velocity / Mathf.Max(agent.speed, .01f);
        bool fire = visible && wantsAim && distance <= Mathf.Max(attackRange, sightRange) && !busy && !reload && Time.time >= readyAt
            && Mathf.Abs(yawError) < 4f && Mathf.Abs(pitchError) < 4f
            && ArenaAITactics.ClearShot(this, life.look.aimCamera.transform.position, AimPoint(target), target) && InBurst();
        bool pressed = fire && Time.time >= nextSemi;
        if (pressed) nextSemi = Time.time + Mathf.Max(semiAutoInterval, 60f / soldier.actions.roundsPerMinute);
        return new SoldierInputFrame {
            move = Vector2.ClampMagnitude(new Vector2(local.x, local.z), 1f), lookDelta = look,
            aim = wantsAim, sprint = WantsToSprint, fireHeld = fire, firePressed = pressed,
            reload = reload && selection == 0, weaponSelection = selection
        };
    }

    bool UpdatePerception()
    {
        if (target && (!target.isActiveAndEnabled || target.IsAwaitingSpawn))
        { target = null; targetVisible = false; HasCover = false; nextPerception = 0f; }
        if (Time.time < nextPerception) return target && !target.health.IsDead && targetVisible;
        nextPerception = Time.time + Mathf.Max(.02f, perceptionInterval);
        float newestSound = heardAt;
        Vector3 soundPoint = default;
        bool heardEnemy = false;
        foreach (var actor in participant.match.participants)
        {
            if (actor == participant || actor.LastShotTime < heardAt || Time.time - actor.LastShotTime > 1f
                || (actor.LastShotPosition - transform.position).sqrMagnitude > hearingRange * hearingRange) continue;
            combatAlertUntil = Mathf.Max(combatAlertUntil, actor.LastShotTime + combatReadyDuration);
            if (actor.LastShotTime >= newestSound && actor.team != participant.team)
            { newestSound = actor.LastShotTime; soundPoint = actor.LastShotPosition; heardEnemy = true; }
        }
        heardAt = Time.time;
        if (heardEnemy && !target && !investigating) Investigate(soundPoint);
        // Finish the current engagement while it remains visible. Only acquire another
        // enemy when this one is lost, instead of chasing the closest candidate each scan.
        ArenaParticipant visible = target && !target.health.IsDead
            && (AimPoint(target) - life.look.aimCamera.transform.position).sqrMagnitude <= sightRange * sightRange
            && CanSee(target) ? target : null;
        float nearest = sightRange * sightRange;
        if (!visible) foreach (var enemy in participant.match.participants)
        {
            if (enemy.team == participant.team || enemy.health.IsDead || enemy.IsAwaitingSpawn) continue;
            float distance = (AimPoint(enemy) - life.look.aimCamera.transform.position).sqrMagnitude;
            if (distance < nearest && CanSee(enemy))
            { nearest = distance; visible = enemy; }
        }
        if (visible)
        {
            if (target != visible || Time.time - seenAt > aimHoldDuration)
            {
                readyAt = Time.time + (IsCombatAlert ? 0f : Mathf.Min(reactionTime, .2f));
                acquiredAt = Time.time; trackedAim = AimPoint(visible);
                // Preserve the cadence across target changes rather than repeatedly delaying a shot.
                if (burstUntil == 0f && Time.time >= pauseUntil) burstShots = 0;
            }
            if (target != visible)
            { HasCover = false; nextCover = Time.time + Random.value; nextReposition = 0f; hasPatrolDestination = false; }
            target = visible; lastSeen = visible.transform.position; seenAt = Time.time;
            LastCombatTime = Time.time;
            combatAlertUntil = Time.time + combatReadyDuration;
            investigating = false;
        }
        else if (target && (target.health.IsDead || Time.time - seenAt > memoryDuration))
        { target = null; HasCover = false; nextReposition = 0f; }
        targetVisible = visible;
        return targetVisible;
    }

    int ChooseWeapon(float distance)
    {
        if (Time.time < nextWeapon || soldier.actions.IsReloading || soldier.loadout.IsSwitching) return 0;
        var loadout = soldier.loadout;
        var current = loadout.EquippedState;
        int desired = Mathf.Clamp(primaryWeapon, 1, loadout.AvailableSlots.Length);
        if (current.MagazineAmmo + current.ReserveAmmo == 0)
        {
            int usable = 0;
            for (int i = 0; i < loadout.AvailableSlots.Length; i++)
            {
                var alternate = loadout.slots[loadout.AvailableSlots[i]].state;
                if (alternate.MagazineAmmo > 0) { usable = i + 1; break; }
                if (usable == 0 && alternate.ReserveAmmo > 0) usable = i + 1;
            }
            if (usable == 0) return 0;
            desired = usable;
        }
        else if (current.MagazineAmmo == 0 && targetVisible && distance < sidearmRange)
        {
            desired = Mathf.Clamp(sidearmWeapon, 1, loadout.AvailableSlots.Length);
            if (loadout.slots[loadout.AvailableSlots[desired - 1]].state.MagazineAmmo == 0) return 0;
        }
        else if (target || IsCombatAlert || Time.time < aimHoldUntil) return 0;
        else if (loadout.slots[loadout.AvailableSlots[desired - 1]].state.MagazineAmmo
            + loadout.slots[loadout.AvailableSlots[desired - 1]].state.ReserveAmmo == 0) return 0;
        if (loadout.AvailableSlots[desired - 1] == loadout.selectedSlot) return 0;
        nextWeapon = Time.time + weaponDecisionInterval;
        return desired;
    }

    void ChooseMovement(bool visible, float distance, bool needsReload)
    {
        if (!target)
        {
            HasCover = false;
            if (investigating && Time.time < investigationUntil && HorizontalDistance(transform.position, investigationPoint) > 1.2f)
            { State = Behavior.Search; SetDestination(investigationPoint); return; }
            investigating = false;
            Patrol(); return;
        }
        Vector3 threat = lastSeen + Vector3.up * 1.25f;
        bool mustHide = needsReload || soldier.actions.IsReloading;
        bool defensive = mustHide || life.health.CurrentHealth / life.health.maxHealth < retreatHealth;
        if (Time.time >= nextCover && (defensive || !visible && distance < attackRange))
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
        if (!visible)
        {
            if (HorizontalDistance(transform.position, lastSeen) <= 1.2f)
            { target = null; targetVisible = false; HasCover = false; hasPatrolDestination = false; patrolWaitUntil = 0f; Patrol(); }
            else { State = Behavior.Search; SetDestination(lastSeen); }
            return;
        }
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

    void Patrol()
    {
        var rules = participant.match.rules;
        if (rules && rules.TryGetObjective(this, out var objective))
        {
            hasPatrolDestination = false; patrolWaitUntil = 0;
            State = Behavior.Patrol; SetDestination(objective); return;
        }
        if (Time.time < patrolWaitUntil)
        { State = Behavior.Idle; SetDestination(transform.position); return; }
        if (hasPatrolDestination && HorizontalDistance(transform.position, patrolDestination) < 1.2f)
        {
            hasPatrolDestination = false;
            patrolWaitUntil = IsCombatAlert ? Time.time : Time.time + Random.Range(.3f, .7f);
            State = Behavior.Idle; SetDestination(transform.position); return;
        }
        if (!hasPatrolDestination)
        {
            var points = participant.match.patrolPoints;
            int start = points.Length > 0 ? Random.Range(0, points.Length) : 0;
            for (int i = 0; i < points.Length; i++)
            {
                int index = (start + i) % points.Length;
                if (index == lastPatrolIndex || HorizontalDistance(transform.position, points[index].position) < 3f) continue;
                if (!ArenaAITactics.Reachable(this, points[index].position, queryPath, out patrolDestination, out _)) continue;
                lastPatrolIndex = index; hasPatrolDestination = true; break;
            }
            // Fall back when authored points are absent OR temporarily unreachable.
            if (!hasPatrolDestination)
                for (int i = 0; i < 4 && !hasPatrolDestination; i++)
                {
                    Vector2 offset = Random.insideUnitCircle.normalized * 10f;
                    hasPatrolDestination = ArenaAITactics.Reachable(this, transform.position + new Vector3(offset.x, 0, offset.y),
                        queryPath, out patrolDestination, out _);
                }
            if (!hasPatrolDestination)
            { State = Behavior.Idle; patrolWaitUntil = Time.time + 1f; SetDestination(transform.position); return; }
        }
        State = Behavior.Patrol; SetDestination(patrolDestination);
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

    Vector3 GetOpenLook(Vector3 preferred)
    {
        preferred = Vector3.ProjectOnPlane(preferred, Vector3.up);
        if (preferred.sqrMagnitude < .01f) preferred = transform.forward;
        preferred.Normalize();
        Vector3 origin = life.look.aimCamera.transform.position;
        if (State != Behavior.Idle && !ArenaAITactics.WorldBlocked(this, origin, origin + preferred * 2f)) return preferred;
        if (Time.time < nextLook) return openLook;
        nextLook = Time.time + 1f;
        float best = float.NegativeInfinity;
        foreach (float angle in new[] { 0f, -35f, 35f, -70f, 70f, -110f, 110f })
        {
            Vector3 candidate = Quaternion.Euler(0, angle, 0) * preferred;
            float clearance = ArenaAITactics.WorldBlocked(this, origin, origin + candidate * 2f) ? 0f
                : ArenaAITactics.WorldBlocked(this, origin, origin + candidate * 6f) ? 2f : 6f;
            float score = clearance - Mathf.Abs(angle) * .015f;
            if (score > best) { best = score; openLook = candidate; }
        }
        return openLook;
    }

    bool InBurst()
    {
        if (Time.time < pauseUntil) return false;
        if (burstUntil == 0f)
        {
            burstUntil = Time.time + Random.Range(burstDuration.x, burstDuration.y);
            burstShots = 0;
            // A full-health infantry target normally requires at least two accurate bursts.
            // Count actual emitted rounds, not frames holding the trigger. High-damage
            // single-shot weapons remain lethal; this policy does not alter weapon damage.
            float damage = Mathf.Max(1f, soldier.loadout.EquippedWeapon.definition.damage);
            int lethalRounds = Mathf.CeilToInt(target.health.maxHealth / damage);
            burstShotLimit = Mathf.Min(Mathf.Clamp((lethalRounds + 1) / 2, 1, 4),
                Mathf.Max(1, Mathf.RoundToInt((burstUntil-Time.time)*soldier.actions.roundsPerMinute/60f)));
        }
        if (burstShots < burstShotLimit) return true;
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
