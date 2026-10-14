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
    Vehicle orderedVehicle;
    bool hasTransportOrder, enterOrderedVehicle;
    public void SetTransportOrder(Vehicle vehicle,bool enter)
    {
        CancelBoarding();orderedVehicle=vehicle;hasTransportOrder=vehicle;enterOrderedVehicle=enter;
        infantry.HoldMovement=hasTransportOrder;nextDecision=0;
    }
    bool HandleTransportOrder()
    {
        if(!hasTransportOrder)return false;
        if(!orderedVehicle||orderedVehicle.IsDestroyed){CancelBoarding();return true;}
        if(occupant.IsMounted)
        {
            if(occupant.SeatIndex==0)occupant.MountedVehicle.DriverInput=new VehicleInput{brake=true};
            if(!enterOrderedVehicle)occupant.Leave(false);
            return true;
        }
        if(!enterOrderedVehicle)return true;
        if(reserved)return false; // Use the same approach, reservation renewal and Board path as autonomous AI.
        if(Time.time<nextDecision)return true;nextDecision=Time.time+.25f;
        int seat=orderedVehicle.FirstAvailableSeat(occupant);
        if(seat<0||!orderedVehicle.CanExit)return true;
        var entry=orderedVehicle.NearestEntryPoint(transform.position);
        if(!ArenaAITactics.Reachable(infantry,entry.position,path,out _,out _)||!orderedVehicle.Reserve(occupant,seat))return true;
        reserved=orderedVehicle;reservedSeat=seat;reservedEntry=entry;reservedForCombat=true;
        State=TravelState.Boarding;infantry.BoardingTarget=entry;progressAt=Time.time;
        return false;
    }
    Vehicle reserved;
    Transform reservedEntry;
    int reservedSeat, corner, recoveries;
    NavMeshPath path;
    float nextDecision, nextPath, retryAt, boardedAt, progressAt, reverseUntil;
    Vector3 progressPosition;
    bool landing, reservedForCombat;
    ArenaParticipant vehicleTarget;
    float nextTargetDecision, lastVehicleContact = float.NegativeInfinity;

    bool IsVehicleCrew
    {
        get
        {
            var actor=infantry.participant;
            if(actor.match.rules is ConquestRules conquest && conquest.squads)
                return conquest.squads.IsVehicleSquad(actor);
            int index=0;
            foreach(var member in actor.match.participants)
                if(member.team==actor.team && member.GetComponent<ArenaVehicleAI>())
                {if(member==actor)return index<4;index++;}
            return false;
        }
    }

    public void SetTravelGoal(Vector3 destination)
    {
        SetTransportOrder(null,false);
        Goal=destination;nextPath=0;corner=0;recoveries=0;landing=false;
        boardedAt=progressAt=Time.time;progressPosition=transform.position;
    }
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
        if(HandleTransportOrder())return;
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
        bool crew=IsVehicleCrew;
        if (!crew && !combat && Vector3.Distance(transform.position, infantry.Destination) < minimumTrip)
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
            if (distance > (crew ? Mathf.Max(vehicleSearchRadius, 100f) : vehicleSearchRadius))
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
                if (v.motor is JetMotor && !scenario.allowAIHelicopters) continue;
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

                if (driver && !crew && !combat && trip < minimumTrip)
                    continue;
                float footTime = Vector3.Distance(transform.position, infantry.Destination) / infantry.soldier.movement.moveSpeed;
                float rideTime = walk / infantry.soldier.movement.moveSpeed + trip / cruiseSpeed + 3f
                    + Vector3.Distance(proposed, infantry.Destination) / infantry.soldier.movement.moveSpeed
                    + (v.motor is HelicopterMotor ? 8f : 0f);
                float score = footTime - rideTime;
                if(combat && !crew)
                {
                    if (walk > combatBoardDistance || !infantry.Target) continue;
                    Vector3 origin = v.turret ? v.turret.muzzle.position : v.cameraTarget.position;
                    if (!driver && !HasLineOfFire(v, infantry.Target, origin, EnemyPoint(infantry.Target))) continue;
                    score = 22 - walk * .65f;
                }
                if(crew)
                {
                    // Crew role chooses a usable vehicle even when walking would be faster.
                    bool armed=v.turret || v.GetComponent<VehicleLauncher>();
                    score=35-walk*.2f+(armed?20:0)+(v.seats[seat].role==Vehicle.SeatRole.Gunner?8:0);
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
        reservedForCombat=combat || crew;
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
        if (v.motor is JetMotor jet)
            FlyJet(v, jet);
        else if (v.motor is HelicopterMotor heli)
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
    void FlyJet(Vehicle v, JetMotor jet)
    {
        State = TravelState.Driving;
        Vector3 target = Goal;
        float ground = GroundHeight(v.transform.position);
        int lane=(int)((uint)infantry.participant.GetEntityId().GetHashCode()%4);
        target.y = GroundHeight(target) + 65+lane*25;
        var aerialEnemy=FindVehicleTarget(v,v.turret?v.turret.muzzle.position:v.cameraTarget.position,350,false);
        if(aerialEnemy&&aerialEnemy.TryGetComponent<VehicleOccupant>(out var opponent)&&opponent.IsMounted&&opponent.MountedVehicle.IsAircraft)
            target=opponent.MountedVehicle.Body.worldCenterOfMass+opponent.MountedVehicle.Body.linearVelocity*1.5f;
        Vector3 direction = target - v.transform.position;
        float horizontal = Vector3.ProjectOnPlane(direction, Vector3.up).magnitude;
        float heading = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        // Bank around the objective rather than trying to hover over its exact point.
        if (horizontal < 180) heading += 65;
        float pitch = -Mathf.Atan2(direction.y, Mathf.Max(1, horizontal)) * Mathf.Rad2Deg;
        if (jet.Grounded || v.transform.position.y < ground + 30) pitch = -18;
        Vector3 flight=v.Body.linearVelocity.sqrMagnitude>1?v.Body.linearVelocity.normalized:v.transform.forward;
        foreach(var obstacle in Physics.SphereCastAll(v.Body.worldCenterOfMass,3,flight,Mathf.Max(45,jet.Airspeed*2),Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if(!obstacle.transform.IsChildOf(v.transform)){pitch=-25;break;}
        v.DriverInput = new VehicleInput { directionalFlight = true, heading = heading,
            viewPitch = Mathf.Clamp(pitch, -25, 15), move = new Vector2(0, jet.Airspeed < jet.maxSpeed * .85f ? 1 : -.3f),
            boost = jet.Grounded };
        if (v.turret) UseTurret(v);
        if (v.GetComponent<VehicleLauncher>()) UseRockets(v);
    }
    void Drive(Vehicle v, bool threat)
    {
        State = TravelState.Driving;
        float distance = Vector3.ProjectOnPlane(Goal - v.transform.position, Vector3.up).magnitude;
        var enemy = v.turret ? FindVehicleTarget(v, v.turret.muzzle.position, v.turret.range, true) : null;
        bool driverWeapon = v.turret && v.turret.controlRole == Vehicle.SeatRole.Driver;
        if(driverWeapon) UseTurret(v);
        if(v.TryGetComponent<VehicleLauncherMount>(out var launcherMount))
        {
            var rocketEnemy = FindVehicleTarget(v, launcherMount.muzzle.position, 150, false);
            if(rocketEnemy) { launcherMount.Aim(EnemyPoint(rocketEnemy)); UseRockets(v); enemy=rocketEnemy; }
        }
        bool supportingGunner = enemy && (driverWeapon || HasGunner(v));
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
        if(!enemy && distance < 6 && IsVehicleCrew && infantry.participant.match.rules
            && infantry.participant.match.rules.TryGetObjective(infantry,out var nextObjective))
        {Goal=nextObjective;nextPath=0;distance=Vector3.ProjectOnPlane(Goal-v.transform.position,Vector3.up).magnitude;}
        bool bail = (!enemy && distance < 6 && !IsVehicleCrew) || (threat && !v.turret && Time.time - boardedAt > 4)
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
        { v.DriverInput = new VehicleInput { brake = true }; return; }
        corner = Mathf.Clamp(corner, 1, corners.Length - 1);
        // Reach the corner before advancing: a five-metre shortcut cuts through narrow bends.
        while (corner < corners.Length - 1 && Vector3.Distance(v.transform.position, corners[corner]) < 2)
            corner++;
        Vector3 local = v.transform.InverseTransformPoint(corners[corner]);
        float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        float desired = cruiseSpeed * Mathf.Lerp(1, .25f, Mathf.Clamp01(Mathf.Abs(angle) / 65));
        float speed = Vector3.Dot(v.Body.linearVelocity, v.transform.forward);
        bool blocked = false;
        var hull = v.NavigationBounds;
        Vector3 nose = v.transform.TransformPoint(hull.center + Vector3.forward * hull.extents.z);
        Vector3 clearance = new Vector3(hull.extents.x + .2f, hull.extents.y * .65f, .2f);
        foreach (var hit in Physics.BoxCastAll(nose, clearance, v.transform.forward, v.transform.rotation,
            Mathf.Max(2, speed * .7f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
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
        var enemy = launcher && !landing ? FindVehicleTarget(v, launcher.NextShotRay.origin, 150, false) : null;
        Vector3 travelGoal = Goal;
        if (enemy)
        {
            Vector3 offset = Vector3.ProjectOnPlane(v.transform.position - EnemyPoint(enemy), Vector3.up).normalized;
            travelGoal = EnemyPoint(enemy) + offset * airEngagementDistance;
        }
        Vector3 difference = travelGoal - v.transform.position;
        float distance = Vector3.ProjectOnPlane(difference, Vector3.up).magnitude;
        if (!enemy && Time.time - lastVehicleContact > combatHoldTime && distance < 7)
        {
            // Armed crew aircraft remain available over the battlefield rather than
            // treating every completed short trip as a reason to land and abandon it.
            if(IsVehicleCrew && launcher && infantry.participant.match.rules
                && infantry.participant.match.rules.TryGetObjective(infantry,out var nextObjective))
                Goal=nextObjective;
            else landing=true;
        }
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
        if (!IsVehicleCrew && Time.time - boardedAt > 70 && Time.time - lastVehicleContact > combatHoldTime)
            landing = true;
    }
    static float GroundHeight(Vector3 position)
    {
        float height = 0;
        float nearest = float.PositiveInfinity;
        foreach (var hit in Physics.RaycastAll(new Vector3(position.x,5000,position.z), Vector3.down, 10000, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
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
        if(rider && rider.IsMounted)
        {
            var vehicle=rider.MountedVehicle;
            // Camera framing may sit above the hull; aim at physical geometry instead.
            return vehicle.targetPoint ? vehicle.targetPoint.position : vehicle.transform.TransformPoint(vehicle.NavigationBounds.center);
        }
        return enemy.transform.position+Vector3.up*1.25f;
    }
    bool HasLineOfFire(Vehicle v,ArenaParticipant enemy,Vector3 from,Vector3 to)
    {
        if(GrenadeSmoke.BlocksLine(from,to))return false;
        Vector3 barrel=v.turret?v.turret.muzzle.forward:(to-from).normalized;
        Vector3 checkFrom=from+barrel*.15f;
        foreach(var obstruction in Physics.RaycastAll(checkFrom,(to-checkFrom).normalized,Vector3.Distance(checkFrom,to),Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if(obstruction.transform.IsChildOf(v.transform)&&obstruction.distance>.25f)return false;
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
        float nearest=float.PositiveInfinity;
        foreach(var enemy in infantry.participant.match.participants)
        {
            if(enemy.team==infantry.participant.team||enemy.health.IsDead||enemy.IsAwaitingSpawn)continue;
            Vector3 point=EnemyPoint(enemy),direction=point-origin;
            float distance=direction.magnitude;if(distance>range)continue;
            var mounted=enemy.GetComponent<VehicleOccupant>();
            float score=distance-(mounted&&mounted.IsMounted?range:0);if(score>nearest)continue;
            if(gunner&&v.turret&&!v.turret.fixedMount)
            {
                var local=v.turret.yawPivot.parent.InverseTransformDirection(direction);
                float elevation=Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg;
                if(elevation<v.turret.pitchLimits.x||elevation>v.turret.pitchLimits.y)continue;
            }
            else if(distance<18)continue;
            if(!HasLineOfFire(v,enemy,origin,point))continue;
            vehicleTarget=enemy;nearest=score;lastVehicleContact=Time.time;
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
        if(v.TryGetComponent<VehicleWeaponSystems>(out var systems))
        {
            var role=v.seats[occupant.SeatIndex].role;
            var profiles=System.Array.FindAll(systems.weapons,w=>w.role==role);
            if(profiles.Length==0)return;
            float range=0;foreach(var profile in profiles)range=Mathf.Max(range,profile.range);
            var searchOrigin=profiles[0].gun ? profiles[0].gun.muzzle.position : profiles[0].launcher ? profiles[0].launcher.NextShotRay.origin : v.cameraTarget.position;
            var target=FindVehicleTarget(v,searchOrigin,range,true);if(!target)return;
            var enemyOccupant=target.GetComponent<VehicleOccupant>();
            bool armored=enemyOccupant&&enemyOccupant.IsMounted;
            int choice=0;
            if(!armored)
            {
                for(int i=0;i<profiles.Length;i++)if(profiles[i].name.Contains("COAX")){choice=i;break;}
            }
            else
            {
                for(int i=0;i<profiles.Length;i++)if(profiles[i].system==VehicleWeaponSystems.FireSystem.Launcher){choice=i;break;}
            }
            if(systems.Current(occupant)!=profiles[choice])systems.Select(occupant,choice);
            Vector3 weaponPoint=EnemyPoint(target),origin=profiles[choice].launcher?profiles[choice].launcher.NextMuzzle.position:v.turret.muzzle.position;
            Vector3 weaponDirection=weaponPoint-origin;systems.AimAt(occupant,new Ray(origin,weaponDirection.normalized));
            float allowance=profiles[choice].system==VehicleWeaponSystems.FireSystem.Launcher?20:4;
            Vector3 forward=profiles[choice].launcher?profiles[choice].launcher.NextMuzzle.forward:v.turret.muzzle.forward;
            if(Vector3.Angle(forward,weaponDirection)<allowance&&HasLineOfFire(v,target,origin,weaponPoint))systems.TryFire(occupant);
            return;
        }
        var enemy=FindVehicleTarget(v,v.turret.muzzle.position,v.turret.range,true);
        if(!enemy)return;
        Vector3 point=EnemyPoint(enemy),direction=point-v.turret.muzzle.position;
        v.turret.Aim(direction);
        if(Vector3.Angle(v.turret.muzzle.forward,direction)<4 && HasLineOfFire(v,enemy,v.turret.muzzle.position,point))
            v.turret.TryFire(occupant);
    }
    void UseRockets(Vehicle v)
    {
        if(v.GetComponent<VehicleWeaponSystems>()){UseTurret(v);return;}
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


