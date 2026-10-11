using UnityEngine;
using UnityEngine.AI;

// Choose a ride -> reserve a seat -> walk to it -> steer -> safely dismount.
// Travel, arrival and obstacle avoidance follow Reynolds' separation of action/steering/motor.
[DefaultExecutionOrder(-15)]
public class ArenaVehicleAI : MonoBehaviour
{
    public enum TravelState
    {
        OnFoot, Boarding, Driving, Landing, Passenger
    }
    public ArenaAIInput infantry;
    public VehicleOccupant occupant;
    public float minimumTrip = 26, vehicleSearchRadius = 35, decisionInterval = .6f, reuseDelay = 12;
    public float cruiseSpeed = 12, flightHeight = 13;
    public float combatBoardDistance = 18, combatHoldTime = 8, groundEngagementDistance = 35, airEngagementDistance = 55;
    public TravelState State { get; private set; }
    public Vector3 Goal { get; private set; }
    ArenaVehicleScenario scenario;
    Vehicle reserved;
    Transform reservedEntry;
    int reservedSeat, corner, recoveries;
    NavMeshPath path;
    float nextDecision, nextPath, retryAt, boardedAt, progressAt, reverseUntil;
    Vector3 progressPosition;
    bool landing, reservedForCombat;
    ArenaParticipant vehicleTarget;
    float nextTargetDecision, lastVehicleContact = float.NegativeInfinity;

    void Awake()
    {
        path = new NavMeshPath();
        scenario = FindAnyObjectByType<ArenaVehicleScenario>();
    }
    void Start()
    {
        occupant.faction = (int)infantry.participant.team;
        nextDecision = Time.time + Random.Range(0f, 2f);
    }
    void OnDisable()
    {
        CancelBoarding();
        if (occupant.IsMounted)
            occupant.Leave(true);
    }
    void Update()
    {
        if (!infantry.participant.match.IsRunning || infantry.life.health.IsDead || infantry.participant.IsAwaitingSpawn)
        {
            CancelBoarding();
            if (occupant.IsMounted)
                occupant.Leave(true);
            return;
        }
        if (occupant.IsMounted)
        {
            DriveOrRide();
            return;
        }
        if (State != TravelState.OnFoot && State != TravelState.Boarding)
        {
            State = TravelState.OnFoot;
            retryAt = Time.time + reuseDelay;
        }
        if (reserved)
        {
            if (reserved.IsDestroyed || reserved.Health < reserved.maxHealth*.4f || (!reservedForCombat && (infantry.IsCombatAlert || infantry.Target)) || !reserved.Reserve(occupant, reservedSeat))
            {
                CancelBoarding();
                return;
            }
            infantry.BoardingTarget = reservedEntry;
            if (Vector3.Distance(transform.position, infantry.BoardingTarget.position) < 2.8f && reserved.Board(occupant, reservedSeat))
            {
                infantry.BoardingTarget = null;
                reserved = null;
                State = TravelState.Driving;
                boardedAt = progressAt = Time.time;
                progressPosition = transform.position;
                recoveries = 0;
                nextPath = 0;
                landing = false;
            }
            else if (Time.time - progressAt > 15)
                CancelBoarding();
            return;
        }
        if (Time.time < nextDecision || Time.time < retryAt)
            return;
        nextDecision = Time.time + decisionInterval;
        bool combat=infantry.IsCombatAlert || infantry.Target;
        if (!combat && Vector3.Distance(transform.position, infantry.Destination) < minimumTrip)
            return;
        float best = 0;
        Vehicle choice = null;
        int seatIndex = 0;
        Vector3 goal = infantry.Destination;
        foreach (var v in Vehicle.Active)
        {
            if (v.gameObject.scene != gameObject.scene || v.IsDestroyed || v.Health < v.maxHealth*.4f || !v.motor.CanDisembark || v.Body.linearVelocity.magnitude > 2)
                continue;
            float distance = Vector3.Distance(transform.position, v.NearestEntryPoint(transform.position).position);
            if (distance > vehicleSearchRadius)
                continue;
            int seat = v.FirstAvailableSeat(occupant);
            if (seat < 0) continue;
            {
                if (!v.CanBoard(occupant, seat))
                    continue;
                bool driver = v.seats[seat].role == Vehicle.SeatRole.Driver;
                bool armedDriver = driver && (v.turret || v.GetComponent<VehicleLauncher>());
                if (combat && (v.seats[seat].role == Vehicle.SeatRole.Passenger || (!armedDriver && !v.turret))) continue;
                if (!driver && !v.Driver)
                    continue; // Do not occupy an unpiloted passenger seat indefinitely.
                Vector3 proposed = infantry.Destination;
                if (v.motor is HelicopterMotor && (!scenario.allowAIHelicopters || (driver && !ChooseLanding(v, out proposed))))
                    continue;
                float trip=Vector3.Distance(v.transform.position,proposed);
                if(driver && v.motor is HummerMotor)
                {
                    if(!PlanRoad(v,proposed))continue;
                    trip=0;var road=path.corners;
                    for(int i=1;i<road.Length;i++)trip+=Vector3.Distance(road[i-1],road[i]);
                }
                if (!ArenaAITactics.Reachable(infantry, v.NearestEntryPoint(transform.position).position, path, out _, out float walk))
                    continue;

                if (driver && !combat && trip < minimumTrip)
                    continue;
                float footTime = Vector3.Distance(transform.position, infantry.Destination) / infantry.soldier.movement.moveSpeed;
                float rideTime = walk / infantry.soldier.movement.moveSpeed + trip / cruiseSpeed + 3f
                    + Vector3.Distance(proposed, infantry.Destination) / infantry.soldier.movement.moveSpeed
                    + (v.motor is HelicopterMotor ? 8f : 0f);
                float score = footTime - rideTime;
                if(combat)
                {
                    if (walk > combatBoardDistance || !infantry.Target) continue;
                    Vector3 origin = v.turret ? v.turret.muzzle.position : v.cameraTarget.position;
                    if (!driver && !HasLineOfFire(v, infantry.Target, origin, EnemyPoint(infantry.Target))) continue;
                    score = 22 - walk * .65f;
                }
                if (score < 3f)
                    continue;
                if (score <= best)
                    continue;
                best = score;
                choice = v;
                seatIndex = seat;
                goal = proposed;
            }
        }
        if (!choice || !choice.Reserve(occupant, seatIndex))
            return;
        reservedForCombat=combat;
        reserved = choice;
        reservedEntry = choice.NearestEntryPoint(transform.position);
        reservedSeat = seatIndex;
        Goal = goal;
        State = TravelState.Boarding;
        infantry.BoardingTarget = reservedEntry;
        progressAt = Time.time;
    }
    bool ChooseLanding(Vehicle v, out Vector3 goal)
    {
        goal = default;
        float best = float.PositiveInfinity;
        foreach (var p in scenario.landingZones)
        {
            bool occupied = false;
            foreach (var other in Vehicle.Active)
                if (other != v && Vector3.Distance(other.transform.position, p.position) < 14)
                {
                    occupied = true;
                    break;
                }
            foreach (var actor in infantry.participant.match.participants)
                if (actor.TryGetComponent<ArenaVehicleAI>(out var pilot) && pilot != this && pilot.occupant.IsMounted
                    && pilot.occupant.MountedVehicle.motor is HelicopterMotor && Vector3.Distance(pilot.Goal, p.position) < 14)
                    occupied = true;
            if (occupied)
                continue;
            float distance = Vector3.Distance(v.transform.position, p.position);
            if (distance < minimumTrip)
                continue;
            float cost = distance / cruiseSpeed + Vector3.Distance(p.position, infantry.Destination) / infantry.soldier.movement.moveSpeed;
            if (cost < best)
            {
                best = cost;
                goal = p.position;
            }
        }
        return best < float.PositiveInfinity;
    }
    void CancelBoarding()
    {
        if (reserved)
            reserved.ReleaseReservation(occupant);
        reserved = null;
        reservedForCombat=false;
        infantry.BoardingTarget = null;
        State = TravelState.OnFoot;
        retryAt = Time.time + 3;
    }
    void DriveOrRide()
    {
        var v = occupant.MountedVehicle;
        var role = v.seats[occupant.SeatIndex].role;
        if (role == Vehicle.SeatRole.Gunner)
        {
            State = TravelState.Passenger;
            UseTurret(v);
        }
        else if (role == Vehicle.SeatRole.Passenger)
            State = TravelState.Passenger;
        if (role != Vehicle.SeatRole.Driver)
        {
            if (!v.Driver && role == Vehicle.SeatRole.Gunner && !vehicleTarget && Time.time - lastVehicleContact > combatHoldTime && v.CanExit)
            {
                // A lone gunner can drive to the next encounter instead of abandoning a healthy armed vehicle.
                if (!v.seats[0].reservation || Time.time >= v.seats[0].reservedUntil) occupant.SwitchSeat(0);
            }
            else if (!v.Driver && role == Vehicle.SeatRole.Passenger && Time.time - boardedAt > 5 && v.CanExit)
                occupant.Leave(false);
            if (v.Health < v.maxHealth * .25f && v.CanExit)
                occupant.Leave(false);
            return;
        }
        if (Time.time - boardedAt < 2f)
        {
            v.DriverInput = new VehicleInput { brake = true, lift = -.3f };
            return;
        }
        bool threat = NearbyEnemy(15) != null;
        if (v.motor is HelicopterMotor heli)
            Fly(v, heli, threat);
        else
            Drive(v, threat);
    }
    bool PlanRoad(Vehicle v, Vector3 goal)
    {
        var filter = new NavMeshQueryFilter { agentTypeID = scenario.vehicleAgentType, areaMask = NavMesh.AllAreas };
        return NavMesh.SamplePosition(v.transform.position, out var from, 5, filter)
            && NavMesh.SamplePosition(goal, out var to, 6, filter)
            && NavMesh.CalculatePath(from.position, to.position, filter, path) && path.status == NavMeshPathStatus.PathComplete;
    }
    void Drive(Vehicle v, bool threat)
    {
        State = TravelState.Driving;
        float distance = Vector3.ProjectOnPlane(Goal - v.transform.position, Vector3.up).magnitude;
        var enemy = v.turret ? FindVehicleTarget(v, v.turret.muzzle.position, v.turret.range, true) : null;
        bool supportingGunner = enemy && HasGunner(v);
        if (enemy)
        {
            Goal = EnemyPoint(enemy) - Vector3.ProjectOnPlane(EnemyPoint(enemy) - v.transform.position, Vector3.up).normalized * groundEngagementDistance;
            distance = Vector3.ProjectOnPlane(Goal - v.transform.position, Vector3.up).magnitude;
            if (supportingGunner || distance < 5)
            {
                v.DriverInput = new VehicleInput { brake = true };
                if (!supportingGunner && v.CanExit)
                    for (int i = 1; i < v.seats.Length; i++)
                        if (v.seats[i].role == Vehicle.SeatRole.Gunner && !v.seats[i].occupant
                            && (!v.seats[i].reservation || Time.time >= v.seats[i].reservedUntil))
                        { occupant.SwitchSeat(i); break; }
                return;
            }
        }
        bool bail = (!enemy && distance < 6) || (threat && !v.turret && Time.time - boardedAt > 4)
            || v.Health < v.maxHealth * .3f || recoveries >= 3;
        if (!enemy && Time.time - lastVehicleContact < combatHoldTime && v.turret)
        { v.DriverInput = new VehicleInput { brake = true }; return; }
        if (bail)
        {
            v.DriverInput = new VehicleInput { brake = true };
            if (v.CanExit)
            {
                occupant.Leave(false);
                retryAt = Time.time + reuseDelay;
            }
            return;
        }
        if (Time.time >= nextPath)
        {
            nextPath = Time.time + 1;
            if (!PlanRoad(v, Goal))
            {
                recoveries++;
                v.DriverInput = new VehicleInput { brake = true };
                return;
            }
            corner = 1;
        }
        var corners = path.corners;
        if (corners.Length < 2)
            return;
        corner = Mathf.Clamp(corner, 1, corners.Length - 1);
        while (corner < corners.Length - 1 && Vector3.Distance(v.transform.position, corners[corner]) < 5)
            corner++;
        Vector3 local = v.transform.InverseTransformPoint(corners[corner]);
        float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        float desired = cruiseSpeed * Mathf.Lerp(1, .25f, Mathf.Clamp01(Mathf.Abs(angle) / 65));
        float speed = Vector3.Dot(v.Body.linearVelocity, v.transform.forward);
        bool blocked = false;
        foreach (var hit in Physics.SphereCastAll(v.transform.position + Vector3.up, .8f, v.transform.forward, Mathf.Max(4, speed * .7f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(v.transform) && !(hit.collider is WheelCollider) && hit.normal.y < .65f)
            {
                blocked = true;
                break;
            }
        if (Vector3.Distance(progressPosition, v.transform.position) > 2)
        {
            progressPosition = v.transform.position;
            progressAt = Time.time;
        }
        if (Time.time - progressAt > 5)
        {
            reverseUntil = Time.time + 1.4f;
            progressAt = Time.time;
            recoveries++;
        }
        if (Time.time < reverseUntil)
            v.DriverInput = new VehicleInput { move = new Vector2(-Mathf.Sign(angle) * .8f, -.55f) };
        else
            v.DriverInput = new VehicleInput { move = new Vector2(Mathf.Clamp(angle / 32, -1, 1), blocked ? 0 : Mathf.Clamp((desired - speed) * .3f, 0, 1)), brake = blocked || speed > desired + 1 };
    }
    void Fly(Vehicle v, HelicopterMotor heli, bool threat)
    {
        if (v.Health < v.maxHealth * .35f)
            landing = true;
        var launcher = v.GetComponent<VehicleLauncher>();
        var enemy = launcher && !landing ? FindVehicleTarget(v, v.cameraTarget.position, 150, false) : null;
        Vector3 travelGoal = Goal;
        if (enemy)
        {
            Vector3 offset = Vector3.ProjectOnPlane(v.transform.position - EnemyPoint(enemy), Vector3.up).normalized;
            travelGoal = EnemyPoint(enemy) + offset * airEngagementDistance;
        }
        Vector3 difference = travelGoal - v.transform.position;
        float distance = Vector3.ProjectOnPlane(difference, Vector3.up).magnitude;
        if (!enemy && Time.time - lastVehicleContact > combatHoldTime && distance < 7)
            landing = true;
        State = landing ? TravelState.Landing : TravelState.Driving;
        float altitude = GroundHeight(v.transform.position);
        float desiredHeight = landing ? Goal.y + .05f : altitude + flightHeight;
        Vector3 velocity = Vector3.ProjectOnPlane(difference, Vector3.up).normalized * Mathf.Min(landing ? 3 : 12, distance * .8f);
        if (v.transform.position.y - altitude < flightHeight * .65f && !landing)
            velocity = Vector3.zero; // Climb before translating.
        foreach (var hit in Physics.SphereCastAll(v.transform.position + Vector3.up * 2, 2, velocity.normalized, 12, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(v.transform))
            {
                velocity = Vector3.zero;
                desiredHeight = Mathf.Max(desiredHeight, v.transform.position.y + 4);
                break;
            }
        Vector3 local = v.transform.InverseTransformDirection(velocity - v.Body.linearVelocity);
        float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(v.transform.forward, Vector3.up), Vector3.ProjectOnPlane(difference, Vector3.up), Vector3.up);
        v.DriverInput = new VehicleInput
        {
            move = Vector2.ClampMagnitude(new Vector2(local.x, local.z) * .16f, 1),
            yaw = landing ? 0 : Mathf.Clamp(yaw / 45, -1, 1),
            lift = Mathf.Clamp((desiredHeight - v.transform.position.y) * .5f, -.6f, 1)
        };
        if (landing && heli.Grounded && v.CanExit && Time.time - boardedAt > 4)
        {
            occupant.Leave(false);
            retryAt = Time.time + reuseDelay;
        }
        if (enemy && !landing && v.transform.position.y - altitude > 4)
        {
            Vector3 aim = EnemyPoint(enemy) - launcher.NextShotRay.origin;
            var input = v.DriverInput;
            input.directionalFlight = true;
            input.aim = true;
            input.heading = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg;
            input.viewPitch = -Mathf.Atan2(aim.y, new Vector2(aim.x, aim.z).magnitude) * Mathf.Rad2Deg;
            input.movementHeading = v.transform.eulerAngles.y;
            input.move = Vector2.ClampMagnitude(new Vector2(local.x, local.z) * .08f, .55f);
            v.DriverInput = input;
            UseRockets(v);
        }
        if (Time.time - boardedAt > 70 && Time.time - lastVehicleContact > combatHoldTime)
            landing = true;
    }
    static float GroundHeight(Vector3 position)
    {
        float height = 0;
        float nearest = float.PositiveInfinity;
        foreach (var hit in Physics.RaycastAll(position + Vector3.up * 30, Vector3.down, 100, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.collider.GetComponentInParent<Vehicle>() && !hit.collider.GetComponentInParent<SoldierLife>() && hit.distance < nearest)
            {
                nearest = hit.distance;
                height = hit.point.y;
            }
        return height;
    }
    ArenaParticipant NearbyEnemy(float radius)
    {
        ArenaParticipant best = null;
        float distance = radius;
        foreach (var actor in infantry.participant.match.participants)
        {
            if (actor.team == infantry.participant.team || actor.health.IsDead || actor.IsAwaitingSpawn)
                continue;
            float d = Vector3.Distance(actor.transform.position, transform.position);
            if (d < distance)
            {
                distance = d;
                best = actor;
            }
        }
        return best;
    }
    Vector3 EnemyPoint(ArenaParticipant enemy)
    {
        var rider=enemy.GetComponent<VehicleOccupant>();
        return rider && rider.IsMounted ? rider.MountedVehicle.cameraTarget.position : enemy.transform.position+Vector3.up*1.25f;
    }
    bool HasLineOfFire(Vehicle v,ArenaParticipant enemy,Vector3 from,Vector3 to)
    {
        if(GrenadeSmoke.BlocksLine(from,to))return false;
        Vector3 delta=to-from;
        var rider=enemy.GetComponent<VehicleOccupant>();
        foreach(var hit in Physics.RaycastAll(from,delta.normalized,delta.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
        {
            if(hit.transform.IsChildOf(v.transform)||hit.transform.IsChildOf(enemy.transform)||hit.collider.GetComponentInParent<VehicleRocket>())continue;
            if(rider && rider.IsMounted && hit.transform.IsChildOf(rider.MountedVehicle.transform))continue;
            return false;
        }
        return true;
    }
    ArenaParticipant FindVehicleTarget(Vehicle v,Vector3 origin,float range,bool gunner)
    {
        if (Time.time < nextTargetDecision)
            return vehicleTarget && !vehicleTarget.health.IsDead && !vehicleTarget.IsAwaitingSpawn
                && HasLineOfFire(v, vehicleTarget, origin, EnemyPoint(vehicleTarget)) ? vehicleTarget : null;
        nextTargetDecision=Time.time+.2f;vehicleTarget=null;
        float nearest=range;
        foreach(var enemy in infantry.participant.match.participants)
        {
            if(enemy.team==infantry.participant.team||enemy.health.IsDead||enemy.IsAwaitingSpawn)continue;
            Vector3 point=EnemyPoint(enemy),direction=point-origin;
            float distance=direction.magnitude;if(distance>nearest)continue;
            if(gunner)
            {
                var local=v.turret.yawPivot.parent.InverseTransformDirection(direction);
                float elevation=Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg;
                if(elevation<v.turret.pitchLimits.x||elevation>v.turret.pitchLimits.y)continue;
            }
            else if(distance<18)continue;
            if(!HasLineOfFire(v,enemy,origin,point))continue;
            vehicleTarget=enemy;nearest=distance;lastVehicleContact=Time.time;
        }
        return vehicleTarget;
    }
    static bool HasGunner(Vehicle v)
    {
        foreach (var seat in v.seats)
            if (seat.role == Vehicle.SeatRole.Gunner && seat.occupant) return true;
        return false;
    }
    void UseTurret(Vehicle v)
    {
        var enemy=FindVehicleTarget(v,v.turret.muzzle.position,v.turret.range,true);
        if(!enemy)return;
        Vector3 point=EnemyPoint(enemy),direction=point-v.turret.muzzle.position;
        v.turret.Aim(direction);
        if(Vector3.Angle(v.turret.muzzle.forward,direction)<4 && HasLineOfFire(v,enemy,v.turret.muzzle.position,point))
            v.turret.TryFire(occupant);
    }
    void UseRockets(Vehicle v)
    {
        var launcher=v.GetComponent<VehicleLauncher>();
        if (!launcher || launcher.ReadyIn > 0) return;
        var enemy=FindVehicleTarget(v,launcher.NextShotRay.origin,150,false);
        if(!enemy)return;
        Vector3 impact=launcher.PredictedImpactPoint;
        if(Vector3.Distance(impact,EnemyPoint(enemy))>launcher.projectile.radius*.75f)return;
        foreach(var ally in infantry.participant.match.participants)
            if(ally.team==infantry.participant.team&&!ally.health.IsDead&&!ally.IsAwaitingSpawn
                && Vector3.Distance(impact,EnemyPoint(ally))<launcher.projectile.radius+2)return;
        if(!HasLineOfFire(v,enemy,launcher.NextShotRay.origin,EnemyPoint(enemy)))return;
        Vector3 shotOrigin=launcher.NextShotRay.origin;
        if(launcher.TryFire(occupant))infantry.participant.RecordShotAt(shotOrigin);
    }
}
