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
    [Header("Combat awareness (independent of weapon reach)")]
    [Min(1)] public float groundDetectionRange=90, helicopterDetectionRange=135, jetDetectionRange=260;
    [Min(1)] public float infantryDetectionRange=65;
    [Min(1)] public float aircraftGroundDetectionRange=220;
    [Header("Aircraft tactics")]
    [Min(1)] public float jetPursuitTime=7, jetExtensionTime=4, jetResetDistance=200;
    [Min(1)] public float helicopterSeparation=32;
    public enum AirManeuver { Patrol, Pursuit, Extend, GroundAttack, GroundExtend }
    public AirManeuver Maneuver { get; private set; }
    public int ExtensionCount { get; private set; }
    public ArenaParticipant CombatTarget => vehicleTarget;
    Vehicle trackedAircraft;
    float pursuitStarted, extensionUntil;
    float lastAircraftContact=float.NegativeInfinity;
    Vector3 groundAttackPoint;
    float lastGroundContact=float.NegativeInfinity;
    Vector3 extensionPoint;
    Vector3 lastAircraftPoint, lastVehiclePoint;
    public TravelState State { get; private set; }
    public Vector3 Goal { get; private set; }
    ArenaVehicleScenario scenario;
    Vehicle orderedVehicle;
    public string BoardingStatus { get; private set; }="Idle";
    public bool MissionCrew { get; private set; }
    public bool MissionManaged { get; set; }
    public bool HoldDeparture { get; set; }
    public void SetMissionGoal(Vector3 destination,bool crew)
    {
        if(hasTransportOrder)SetTransportOrder(null,false);
        MissionCrew=crew;
        if((Goal-destination).sqrMagnitude>16){Goal=destination;nextPath=0;landing=false;}
    }
    public void ReleaseMission()
    {
        MissionCrew=false;HoldDeparture=false;
        if(hasTransportOrder)SetTransportOrder(null,false);
    }
    bool exitClearancePending, hasExitGoal;
    Vector3 exitGoal, reservedApproach, failedApproach;
    bool hasFailedApproach;
    float failedEntryUntil;
    void ClearExitArea()
    {
        if(!exitClearancePending)return;
        if(!hasExitGoal)
        {
            var bounds=orderedVehicle.EntryBounds;
            var local=orderedVehicle.transform.InverseTransformPoint(transform.position);
            if(!bounds.Contains(local)){exitClearancePending=false;BoardingStatus="Outside";return;}
            float best=float.PositiveInfinity;
            for(int edge=0;edge<4;edge++)
            {
                bool side=edge<2;
                Vector3 candidate=side
                    ? new Vector3(edge==0?bounds.min.x-2:bounds.max.x+2,0,Mathf.Clamp(local.z,bounds.min.z,bounds.max.z))
                    : new Vector3(Mathf.Clamp(local.x,bounds.min.x,bounds.max.x),0,edge==2?bounds.min.z-2:bounds.max.z+2);
                var proposed=orderedVehicle.transform.TransformPoint(candidate);proposed.y=GroundHeight(proposed)+.05f;
                if(!ArenaAITactics.Reachable(infantry,proposed,path,out var point,out float walk)||walk>=best)continue;
                best=walk;exitGoal=point;hasExitGoal=true;
            }
            if(!hasExitGoal){BoardingStatus="Waiting for clear exit route";return;}
        }
        if(Vector3.ProjectOnPlane(transform.position-exitGoal,Vector3.up).sqrMagnitude<.49f)
        {
            exitClearancePending=hasExitGoal=false;
            infantry.BoardingTarget=null;infantry.BoardingApproachPoint=null;BoardingStatus="Outside";
        }
        else
        {
            infantry.BoardingTarget=orderedVehicle.transform;infantry.BoardingApproachPoint=exitGoal;
            BoardingStatus="Clearing exit using navigation";
        }
    }
    bool hasTransportOrder, enterOrderedVehicle;
    public void SetTransportOrder(Vehicle vehicle,bool enter)
    {
        if(hasTransportOrder&&orderedVehicle==vehicle&&enterOrderedVehicle==enter)
        {infantry.HoldMovement=true;nextDecision=0;return;}
        CancelBoarding();exitClearancePending=!enter;hasExitGoal=false;
        orderedVehicle=vehicle;hasTransportOrder=vehicle;enterOrderedVehicle=enter;
        infantry.HoldMovement=hasTransportOrder;nextDecision=0;
    }
    bool HandleTransportOrder()
    {
        if(!hasTransportOrder)return false;
        if(!orderedVehicle||orderedVehicle.IsDestroyed){CancelBoarding();BoardingStatus="Vehicle unavailable";return true;}
        if(occupant.IsMounted)
        {
            if(occupant.SeatIndex==0)occupant.MountedVehicle.DriverInput=new VehicleInput{brake=true};
            BoardingStatus=enterOrderedVehicle?"Seated":"Exiting seat";
            if(!enterOrderedVehicle&&occupant.Leave(false)){exitClearancePending=true;hasExitGoal=false;}
            return true;
        }
        if(!enterOrderedVehicle){ClearExitArea();return true;}
        if(reserved)return false; // Use the same approach, reservation renewal and Board path as autonomous AI.
        if(Time.time<nextDecision)return true;nextDecision=Time.time+.25f;
        int seat=orderedVehicle.FirstAvailableSeat(occupant);
        if(seat<0){BoardingStatus="Full: waiting for a free seat";return true;}
        if(!orderedVehicle.CanExit){BoardingStatus="Waiting for vehicle to stop";return true;}
        if(orderedVehicle.IsInsideEntryBox(transform.position)&&orderedVehicle.Board(occupant,seat))
        {BoardingStatus="Seated";return true;}
        var entry=ChooseEntry(orderedVehicle,out reservedApproach);
        if(!entry||!orderedVehicle.Reserve(occupant,seat)){BoardingStatus="No reachable entry; retrying";return true;}
        reserved=orderedVehicle;reservedSeat=seat;reservedEntry=entry;reservedForCombat=true;
        State=TravelState.Boarding;BoardingStatus="Approaching reserved seat";infantry.BoardingTarget=entry;infantry.BoardingApproachPoint=reservedApproach;progressAt=Time.time;progressPosition=transform.position;
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
            if(MissionCrew)return true;
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
        nextDecision = hasTransportOrder ? Time.time : Time.time + Random.Range(0f, 2f);
    }
    void OnDisable()
    {
        CombatEvents.DamageConfirmed-=OnCombatDamage;
        trackedAircraft=null;lastAircraftContact=float.NegativeInfinity;Maneuver=AirManeuver.Patrol;vehicleTarget=null;
        CancelBoarding();
        if (occupant.IsMounted)
            occupant.Leave(true);
    }
    void OnEnable()=>CombatEvents.DamageConfirmed+=OnCombatDamage;
    void OnCombatDamage(CombatEvents.ConfirmedDamage damage)
    {
        if(!occupant.IsMounted || damage.target!=occupant.MountedVehicle.gameObject)return;
        infantry.NotifyThreat(damage.attacker);
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
        // After clearing the exit, combat takes over from the completed transport order.
        if(hasTransportOrder && !enterOrderedVehicle && !occupant.IsMounted
            && !exitClearancePending && infantry.IsCombatAlert)SetTransportOrder(null,false);
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
            infantry.BoardingTarget = reservedEntry;infantry.BoardingApproachPoint=reservedApproach;
            if (reserved.IsInsideEntryBox(transform.position) && reserved.Board(occupant, reservedSeat))
            {
                infantry.BoardingTarget = null;infantry.BoardingApproachPoint=null;BoardingStatus="Seated";
                reserved = null;
                State = TravelState.Driving;
                boardedAt = progressAt = Time.time;
                progressPosition = transform.position;
                recoveries = 0;
                nextPath = 0;
                landing = false;
            }
            else if(Vector3.ProjectOnPlane(transform.position-progressPosition,Vector3.up).sqrMagnitude>.09f)
            {progressAt=Time.time;progressPosition=transform.position;}
            else if(Time.time-progressAt>3)
            {
                failedApproach=reservedApproach;hasFailedApproach=true;failedEntryUntil=Time.time+5;
                var alternative=ChooseEntry(reserved,out var point);
                if(alternative){reservedEntry=alternative;reservedApproach=point;progressAt=Time.time;BoardingStatus="Retrying another entry";}
                else {CancelBoarding();BoardingStatus="Entry blocked; retrying";}
            }
            return;
        }
        if(MissionManaged)return;
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
                if (!ChooseEntry(v,out var boardingPoint) || !ArenaAITactics.Reachable(infantry, boardingPoint, path, out _, out float walk))
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
        reservedEntry = ChooseEntry(choice,out reservedApproach);
        if(!reservedEntry){CancelBoarding();return;}
        reservedSeat = seatIndex;
        Goal = goal;
        State = TravelState.Boarding;
        infantry.BoardingTarget = reservedEntry;
        infantry.BoardingApproachPoint = reservedApproach;
        progressPosition = transform.position;
        progressAt = Time.time;
    }
    Transform ChooseEntry(Vehicle vehicle,out Vector3 approach)
    {
        approach=default;
        float bestCost=float.PositiveInfinity;
        var bounds=vehicle.EntryBounds;
        var local=vehicle.transform.InverseTransformPoint(transform.position);
        float inset=Mathf.Min(Mathf.Max(.3f,infantry.agent.stoppingDistance+.15f),Mathf.Min(bounds.extents.x,bounds.extents.z)*.5f);
        float left=bounds.min.x+inset,right=bounds.max.x-inset;
        float back=bounds.min.z+inset,front=bounds.max.z-inset;
        // Closest point on each edge, followed by samples along every edge for blocked approaches.
        for(int edge=0;edge<4;edge++)
        for(int sample=-1;sample<=8;sample++)
        {
            bool side=edge<2;
            float along=sample<0 ? Mathf.Clamp(side?local.z:local.x,side?back:left,side?front:right)
                : Mathf.Lerp(side?back:left,side?front:right,sample/8f);
            var candidate=side?new Vector3(edge==0?left:right,0,along):new Vector3(along,0,edge==2?back:front);
            var proposed=vehicle.transform.TransformPoint(candidate);
            if(Vector3.ProjectOnPlane(proposed-transform.position,Vector3.up).magnitude>=bestCost)continue;
            proposed.y=GroundHeight(proposed)+.05f;
            if(!ArenaAITactics.Reachable(infantry,proposed,path,out var point,out float walk)||!vehicle.IsInsideEntryBox(point))continue;
            if(hasFailedApproach&&Time.time<failedEntryUntil&&(point-failedApproach).sqrMagnitude<2.25f)continue;
            float cost=walk;
            foreach(var seat in vehicle.seats)
            {
                var rider=seat.reservation;if(!rider||rider==occupant||Time.time>=seat.reservedUntil)continue;
                var ai=rider.GetComponent<ArenaVehicleAI>();
                if(ai&&ai.reserved&&(ai.reservedApproach-point).sqrMagnitude<2.25f)cost+=4;
            }
            if(cost<bestCost){bestCost=cost;approach=point;}
        }
        return bestCost<float.PositiveInfinity?vehicle.transform:null;
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
        infantry.BoardingTarget = null;infantry.BoardingApproachPoint=null;
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
        if(HoldDeparture){v.DriverInput=new VehicleInput{brake=true};return;}
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
        float startRadius=Mathf.Max(5,v.NavigationBounds.extents.x+2);
        if(!NavMesh.SamplePosition(v.transform.position,out var from,startRadius,filter))return false;
        // Flags can be upstairs or inside infantry-only spaces. A vehicle supports the
        // nearest accessible perimeter; passengers finish the approach on foot.
        for(int ring=0;ring<4;ring++)for(int i=0;i<(ring==0?1:8);i++)
        {
            var candidate=goal+Quaternion.Euler(0,i*45,0)*Vector3.forward*(ring==0?0:ring==1?8:ring==2?16:24);
            if(NavMesh.SamplePosition(candidate,out var to,6,filter)
                && NavMesh.CalculatePath(from.position,to.position,filter,path) && path.status==NavMeshPathStatus.PathComplete)return true;
        }
        return false;
    }
    void FlyJet(Vehicle v, JetMotor jet)
    {
        State = TravelState.Driving;
        Vector3 target = Goal;
        float ground = GroundHeight(v.transform.position);
        int lane=(int)((uint)infantry.participant.GetEntityId().GetHashCode()%4);
        target.y = GroundHeight(target) + 65+lane*25;
        var aerialEnemy=FindVehicleTarget(v,v.turret?v.turret.muzzle.position:v.cameraTarget.position,jetDetectionRange,false,true);
        Vehicle opposingAircraft=null;
        if(aerialEnemy&&aerialEnemy.TryGetComponent<VehicleOccupant>(out var opponent)&&opponent.IsMounted&&opponent.MountedVehicle.IsAircraft)
            opposingAircraft=opponent.MountedVehicle;
        if(opposingAircraft)
        {lastAircraftContact=Time.time;lastAircraftPoint=opposingAircraft.Body.worldCenterOfMass;target=JetCombatPoint(v,opposingAircraft,jet);}
        else if(trackedAircraft && !trackedAircraft.IsDestroyed && Time.time-lastAircraftContact<combatHoldTime)
        {
            if(Maneuver==AirManeuver.Extend && Time.time>=extensionUntil+2)
            {Maneuver=AirManeuver.Pursuit;pursuitStarted=Time.time;}
            target=Maneuver==AirManeuver.Extend?extensionPoint:lastAircraftPoint;
        }
        else
        {
            trackedAircraft=null;
            if(Maneuver==AirManeuver.GroundExtend && Time.time<extensionUntil)target=extensionPoint;
            else
            {
                bool armed=v.turret && v.turret.controlRole==Vehicle.SeatRole.Driver
                    || v.TryGetComponent<VehicleWeaponSystems>(out var weapons) && System.Array.Exists(weapons.weapons,w=>w.role==Vehicle.SeatRole.Driver);
                var groundEnemy=armed?FindVehicleTarget(v,v.turret?v.turret.muzzle.position:v.cameraTarget.position,aircraftGroundDetectionRange,false):null;
                if(groundEnemy)
                {groundAttackPoint=EnemyPoint(groundEnemy);lastGroundContact=Time.time;target=JetGroundAttackPoint(v,jet,groundEnemy);}
                else if(armed && Time.time-lastGroundContact<15)
                {target=groundAttackPoint+Vector3.up*65;Maneuver=AirManeuver.GroundAttack;}
                else Maneuver=AirManeuver.Patrol;
            }
        }
        Vector3 direction = target - v.transform.position;
        float horizontal = Vector3.ProjectOnPlane(direction, Vector3.up).magnitude;
        float heading = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        // Bank around the objective rather than trying to hover over its exact point.
        if (Maneuver==AirManeuver.Patrol && horizontal < 180) heading += 65;
        float pitch = -Mathf.Atan2(direction.y, Mathf.Max(1, horizontal)) * Mathf.Rad2Deg;
        if (jet.Grounded || v.transform.position.y < ground + 30) pitch = -18;
        Vector3 flight=v.Body.linearVelocity.sqrMagnitude>1?v.Body.linearVelocity.normalized:v.transform.forward;
        // A deliberate strafing descent needs a shorter avoidance horizon; the normal
        // two-second probe otherwise cancels every pass before the cannon can align.
        float avoidanceDistance=Maneuver==AirManeuver.GroundAttack?Mathf.Max(20,jet.Airspeed*.7f):Mathf.Max(45,jet.Airspeed*2);
        foreach(var obstacle in Physics.SphereCastAll(v.Body.worldCenterOfMass,3,flight,avoidanceDistance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if(!obstacle.transform.IsChildOf(v.transform)){pitch=-25;break;}
        v.DriverInput = new VehicleInput { directionalFlight = true, heading = heading,
            viewPitch = Mathf.Clamp(pitch, -25, Maneuver==AirManeuver.GroundAttack?40:15), move = new Vector2(0, jet.Airspeed < jet.maxSpeed * .85f ? 1 : -.3f),
            boost = jet.Grounded };
        if (v.turret) UseTurret(v);
        if (v.GetComponent<VehicleLauncher>()) UseRockets(v);
    }
    Vector3 JetGroundAttackPoint(Vehicle own,JetMotor jet,ArenaParticipant enemy)
    {
        if(Maneuver==AirManeuver.GroundExtend && Time.time<extensionUntil)return extensionPoint;
        Vector3 point=EnemyPoint(enemy);
        float horizontal=Vector3.ProjectOnPlane(point-own.transform.position,Vector3.up).magnitude;
        if(Maneuver!=AirManeuver.GroundAttack)pursuitStarted=Time.time;
        bool missedPass=Vector3.Angle(own.transform.forward,point-own.transform.position)>65 && Time.time-pursuitStarted>4;
        if(missedPass || horizontal<Mathf.Max(55,jet.Airspeed*.9f) || own.transform.position.y<GroundHeight(own.transform.position)+35)
        {
            Maneuver=AirManeuver.GroundExtend;extensionUntil=Time.time+5;
            Vector3 escape=Vector3.ProjectOnPlane(own.transform.forward,Vector3.up).normalized;
            extensionPoint=own.transform.position+escape*Mathf.Max(260,jet.Airspeed*5);
            extensionPoint.y=Mathf.Max(own.transform.position.y+45,GroundHeight(extensionPoint)+85);
            return extensionPoint;
        }
        Maneuver=AirManeuver.GroundAttack;
        Vector3 aim=own.turret?LeadProjectile(own,point,EnemyVelocity(enemy),own.turret.muzzle.position,own.turret.projectile):point;
        // Anticipate the changing sight angle during a fast pass. Weapon aim still uses
        // the actual ballistic intercept; this only gives the pilot time to align the nose.
        return aim-own.Body.linearVelocity*.35f;
    }
    Vector3 JetCombatPoint(Vehicle own,Vehicle enemy,JetMotor jet)
    {
        if(trackedAircraft!=enemy)
        {trackedAircraft=enemy;pursuitStarted=Time.time;Maneuver=AirManeuver.Pursuit;}
        float distance=Vector3.Distance(own.Body.worldCenterOfMass,enemy.Body.worldCenterOfMass);
        if(Maneuver==AirManeuver.Extend)
        {
            // Commit to a fixed escape point; chasing the moving target here recreates the circle.
            if(Time.time<extensionUntil || distance<jetResetDistance && Time.time<extensionUntil+2)return extensionPoint;
            Maneuver=AirManeuver.Pursuit;pursuitStarted=Time.time;
        }
        Vector3 toEnemy=enemy.Body.worldCenterOfMass-own.Body.worldCenterOfMass;
        float offNose=Vector3.Angle(own.transform.forward,toEnemy);
        float stagger=(uint)infantry.participant.GetEntityId().GetHashCode()%4;
        bool stalemate=offNose>45 && Time.time-pursuitStarted>jetPursuitTime+stagger;
        bool tooClose=distance<Mathf.Max(50,jet.Airspeed*1.3f);
        if(stalemate || tooClose)
        {
            Maneuver=AirManeuver.Extend;ExtensionCount++;
            extensionUntil=Time.time+jetExtensionTime+stagger*.3f;
            float side=own.GetEntityId().GetHashCode()<enemy.GetEntityId().GetHashCode()?1:-1;
            Vector3 forward=Vector3.ProjectOnPlane(own.transform.forward,Vector3.up).normalized;
            Vector3 escape=(forward+Vector3.Cross(Vector3.up,forward)*side*.3f).normalized;
            extensionPoint=own.Body.worldCenterOfMass+escape*Mathf.Max(jetResetDistance*1.5f,jet.Airspeed*(jetExtensionTime+2));
            extensionPoint.y+=side>0?25:-10;
            extensionPoint.y=Mathf.Max(extensionPoint.y,GroundHeight(extensionPoint)+45);
            return extensionPoint;
        }
        float lead=Mathf.Clamp(distance/Mathf.Max(30,jet.Airspeed)*.2f,.2f,1.2f);
        if(offNose<35 && own.turret)
            return LeadProjectile(own,enemy.Body.worldCenterOfMass,enemy.Body.linearVelocity,own.turret.muzzle.position,own.turret.projectile);
        return enemy.Body.worldCenterOfMass+enemy.Body.linearVelocity*lead;
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
        var enemy = !landing && (launcher || v.turret) ? FindVehicleTarget(v, launcher?launcher.NextShotRay.origin:v.turret.muzzle.position, 150, false) : null;
        if(v.turret && v.turret.controlRole==Vehicle.SeatRole.Driver)UseTurret(v);
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
            if(IsVehicleCrew && (launcher || v.turret))
            {
                if(infantry.participant.match.rules && infantry.participant.match.rules.TryGetObjective(infantry,out var nextObjective))
                    Goal=nextObjective;
                // No objective in a combat test: stay airborne and available rather than land.
            }
            else landing=true;
        }
        State = landing ? TravelState.Landing : TravelState.Driving;
        float altitude = GroundHeight(v.transform.position);
        float desiredHeight = landing ? Goal.y + .05f : altitude + flightHeight;
        Vector3 velocity = Vector3.ProjectOnPlane(difference, Vector3.up).normalized * Mathf.Min(landing ? 3 : 12, distance * .8f);
        if (v.transform.position.y - altitude < flightHeight * .65f && !landing)
            velocity = Vector3.zero; // Climb before translating.
        if(enemy)
        {
            Vector3 outward=Vector3.ProjectOnPlane(v.Body.worldCenterOfMass-EnemyPoint(enemy),Vector3.up);
            float range=outward.magnitude;
            if(range<.1f)outward=v.transform.right;else outward/=range;
            var rider=enemy.GetComponent<VehicleOccupant>();
            Vector3 follow=rider && rider.IsMounted ? Vector3.ProjectOnPlane(rider.MountedVehicle.Body.linearVelocity,Vector3.up)*.5f : Vector3.zero;
            // Arrive at a stand-off band and brake closing velocity, rather than chasing the hull.
            float spacing=Mathf.Max(airEngagementDistance,helicopterSeparation*2);
            velocity=Vector3.ClampMagnitude(outward*Mathf.Clamp((spacing-range)*.6f,-10,12)+follow,12);
            // Use fixed altitude lanes; matching the opponent's height plus an offset
            // makes two pilots continually chase each other upward.
            if(rider && rider.IsMounted && rider.MountedVehicle.IsAircraft)
                desiredHeight+=(uint)infantry.participant.GetEntityId().GetHashCode()%2*6;
        }
        else if(!landing && Time.time-lastVehicleContact<combatHoldTime)
        {
            Vector3 offset=Vector3.ProjectOnPlane(lastVehiclePoint-v.Body.worldCenterOfMass,Vector3.up);
            velocity=offset.normalized*Mathf.Clamp((offset.magnitude-airEngagementDistance)*.6f,-8,12);
        }
        if(!landing)velocity=SeparateAircraft(v,velocity);
        if(v.transform.position.y-altitude<flightHeight*.65f && !landing)velocity=Vector3.zero;
        foreach(var hit in Physics.SphereCastAll(v.Body.worldCenterOfMass,2,velocity.normalized,Mathf.Max(12,v.Body.linearVelocity.magnitude*2),Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
        {
            if(hit.transform.IsChildOf(v.transform))continue;
            var aircraft=hit.collider.GetComponentInParent<Vehicle>();
            if(aircraft && aircraft.IsAircraft)continue; // Predictive separation already handles aircraft.
            velocity=Vector3.zero;desiredHeight=Mathf.Max(desiredHeight,v.transform.position.y+4);break;
        }
        float heading=Mathf.Atan2(difference.x,difference.z)*Mathf.Rad2Deg;
        if(enemy)
        {var aim=EnemyPoint(enemy)-v.Body.worldCenterOfMass;heading=Mathf.Atan2(aim.x,aim.z)*Mathf.Rad2Deg;}
        // Submit a desired velocity once. The shared assist already performs velocity feedback.
        Vector3 local=Quaternion.Inverse(Quaternion.Euler(0,heading,0))*velocity/Mathf.Max(1,heli.maxSpeed);
        v.DriverInput = new VehicleInput
        {
            directionalFlight=true,heading=heading,movementHeading=heading,
            move=Vector2.ClampMagnitude(new Vector2(local.x,local.z),1),
            lift=Mathf.Clamp((desiredHeight-v.transform.position.y)*.5f,-.6f,1)
        };
        if (landing && heli.Grounded && v.CanExit && Time.time - boardedAt > 4)
        {
            occupant.Leave(false);
            retryAt = Time.time + reuseDelay;
        }
        if (enemy && !landing && v.transform.position.y - altitude > 4)
        {
            Vector3 aim = EnemyPoint(enemy) - (launcher?launcher.NextShotRay.origin:v.turret.muzzle.position);
            var input = v.DriverInput;
            input.directionalFlight = true;
            // Braking/separation takes precedence over pitching fixed guns onto a target.
            input.aim = velocity.magnitude<3 && Vector3.ProjectOnPlane(v.Body.linearVelocity,Vector3.up).magnitude<4;
            input.heading = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg;
            input.viewPitch = -Mathf.Atan2(aim.y, new Vector2(aim.x, aim.z).magnitude) * Mathf.Rad2Deg;
            input.movementHeading = heading;
            v.DriverInput = input;
            if(launcher)UseRockets(v);
        }
        if (!IsVehicleCrew && Time.time - boardedAt > 70 && Time.time - lastVehicleContact > combatHoldTime)
            landing = true;
    }
    Vector3 SeparateAircraft(Vehicle own,Vector3 desiredVelocity)
    {
        Vector3 correction=Vector3.zero;
        foreach(var other in Vehicle.Active)
        {
            if(other==own || other.IsDestroyed || !other.IsAircraft || other.gameObject.scene!=gameObject.scene)continue;
            Vector3 offset=own.Body.worldCenterOfMass-other.Body.worldCenterOfMass;
            Vector3 relative=own.Body.linearVelocity-other.Body.linearVelocity;
            float time=relative.sqrMagnitude>.1f?Mathf.Clamp(-Vector3.Dot(offset,relative)/relative.sqrMagnitude,0,2.5f):0;
            Vector3 closest=offset+relative*time;
            float safe=Mathf.Max(helicopterSeparation,own.NavigationBounds.extents.magnitude+other.NavigationBounds.extents.magnitude+4);
            if(closest.magnitude>=safe || offset.magnitude>safe+relative.magnitude*2.5f)continue;
            Vector3 away=Vector3.ProjectOnPlane(offset,Vector3.up).normalized;
            if(away.sqrMagnitude<.01f)away=own.GetEntityId().GetHashCode()<other.GetEntityId().GetHashCode()?Vector3.right:Vector3.left;
            // Deterministic lateral bias prevents two head-on pilots mirroring their sidestep.
            Vector3 side=Vector3.Cross(Vector3.up,away);
            correction+=(away+side*.5f).normalized*Mathf.Lerp(6,18,1-Mathf.Clamp01(closest.magnitude/safe));
        }
        return Vector3.ClampMagnitude(desiredVelocity+correction,14);
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
    float DetectionRange(Vehicle v,ArenaParticipant enemy,float weaponRange)
    {
        float range=v.motor is JetMotor?jetDetectionRange:v.IsAircraft?helicopterDetectionRange:groundDetectionRange;
        var rider=enemy.GetComponent<VehicleOccupant>();
        if(v.IsAircraft && (!rider || !rider.IsMounted || !rider.MountedVehicle.IsAircraft))range=Mathf.Min(range,aircraftGroundDetectionRange);
        else if(!rider || !rider.IsMounted)range=Mathf.Min(range,infantryDetectionRange);
        return Mathf.Min(weaponRange,range);
    }
    ArenaParticipant FindVehicleTarget(Vehicle v,Vector3 origin,float range,bool gunner,bool aircraftOnly=false)
    {
        if (Time.time < nextTargetDecision && vehicleTarget)
        {
            var rider=vehicleTarget.GetComponent<VehicleOccupant>();
            bool compatible=!aircraftOnly || rider && rider.IsMounted && rider.MountedVehicle.IsAircraft;
            float limit=DetectionRange(v,vehicleTarget,range);
            if(compatible && (EnemyPoint(vehicleTarget)-origin).sqrMagnitude<=limit*limit)
            return vehicleTarget && vehicleTarget.isActiveAndEnabled && !vehicleTarget.health.IsDead && !vehicleTarget.IsAwaitingSpawn
                && HasLineOfFire(v, vehicleTarget, origin, EnemyPoint(vehicleTarget)) ? vehicleTarget : null;
        }
        nextTargetDecision=Time.time+.2f;vehicleTarget=null;
        float nearest=float.PositiveInfinity;
        foreach(var enemy in infantry.participant.match.participants)
        {
            if(!enemy.isActiveAndEnabled||enemy.team==infantry.participant.team||enemy.health.IsDead||enemy.IsAwaitingSpawn)continue;
            Vector3 point=EnemyPoint(enemy),direction=point-origin;
            float distance=direction.magnitude;if(distance>DetectionRange(v,enemy,range))continue;
            var mounted=enemy.GetComponent<VehicleOccupant>();
            if(aircraftOnly && (!mounted || !mounted.IsMounted || !mounted.MountedVehicle.IsAircraft))continue;
            float score=distance-(mounted&&mounted.IsMounted?range:0);
            if(v.IsAircraft && mounted && mounted.IsMounted && mounted.MountedVehicle.IsAircraft)score-=range*2;
            if(score>nearest)continue;
            if(gunner&&v.turret&&!v.turret.fixedMount)
            {
                var local=v.turret.yawPivot.parent.InverseTransformDirection(direction);
                float elevation=Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg;
                if(elevation<v.turret.pitchLimits.x||elevation>v.turret.pitchLimits.y)continue;
            }
            if(!HasLineOfFire(v,enemy,origin,point))continue;
            vehicleTarget=enemy;nearest=score;lastVehicleContact=Time.time;lastVehiclePoint=point;
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
            bool groundTarget=!armored || !enemyOccupant.MountedVehicle.IsAircraft;
            if(v.motor is JetMotor && groundTarget && trackedAircraft && !trackedAircraft.IsDestroyed
                && Time.time-lastAircraftContact<combatHoldTime)return;
            int choice=0;
            if(v.IsAircraft)
            {
                choice=ChooseAircraftWeapon(v,systems,profiles,target,searchOrigin);
                if(choice<0)return;
            }
            else if(!armored)
            {
                for(int i=0;i<profiles.Length;i++)if(profiles[i].name.Contains("COAX")){choice=i;break;}
            }
            else
            {
                for(int i=0;i<profiles.Length;i++)
                    if(profiles[i].system==VehicleWeaponSystems.FireSystem.Launcher && systems.IsAvailable(profiles[i])
                        && profiles[i].ReadyIn<=0 && (!(v.motor is JetMotor) || Vector3.Distance(searchOrigin,EnemyPoint(target))>140))
                    {choice=i;break;}
            }
            if(systems.Current(occupant)!=profiles[choice])systems.Select(occupant,choice);
            Vector3 physicalPoint=EnemyPoint(target),origin=profiles[choice].launcher?profiles[choice].launcher.NextMuzzle.position:v.turret.muzzle.position;
            Vector3 weaponPoint=profiles[choice].system==VehicleWeaponSystems.FireSystem.Gun
                ? LeadProjectile(v,physicalPoint,EnemyVelocity(target),origin,profiles[choice].projectile):physicalPoint;
            Vector3 weaponDirection=weaponPoint-origin;systems.AimAt(occupant,new Ray(origin,weaponDirection.normalized));
            float allowance=profiles[choice].system==VehicleWeaponSystems.FireSystem.Launcher?20:4;
            Vector3 forward=profiles[choice].launcher?profiles[choice].launcher.NextMuzzle.forward:v.turret.muzzle.forward;
            if(Vector3.Angle(forward,weaponDirection)<allowance&&HasLineOfFire(v,target,origin,physicalPoint))systems.TryFire(occupant);
            return;
        }
        var enemy=FindVehicleTarget(v,v.turret.muzzle.position,v.turret.range,true);
        if(!enemy)return;
        Vector3 point=LeadProjectile(v,EnemyPoint(enemy),EnemyVelocity(enemy),v.turret.muzzle.position,v.turret.projectile),direction=point-v.turret.muzzle.position;
        v.turret.Aim(direction);
        if(Vector3.Angle(v.turret.muzzle.forward,direction)<4 && HasLineOfFire(v,enemy,v.turret.muzzle.position,EnemyPoint(enemy)))
            v.turret.TryFire(occupant);
    }
    int ChooseAircraftWeapon(Vehicle v,VehicleWeaponSystems systems,VehicleWeaponSystems.Weapon[] profiles,ArenaParticipant target,Vector3 origin)
    {
        var rider=target.GetComponent<VehicleOccupant>();
        bool ground=!rider || !rider.IsMounted || !rider.MountedVehicle.IsAircraft;
        float distance=Vector3.Distance(origin,EnemyPoint(target));
        int choice=-1;float best=float.NegativeInfinity;
        for(int i=0;i<profiles.Length;i++)
        {
            var weapon=profiles[i];
            if(!systems.IsAvailable(weapon) || distance>weapon.range)continue;
            bool launcher=weapon.system==VehicleWeaponSystems.FireSystem.Launcher;
            if(launcher)
            {
                if(!weapon.launcher || weapon.launcher.Missiles==0)continue;
                var guidance=weapon.launcher.projectile.GetComponent<VehicleMissileGuidance>();
                if(ground && guidance && (guidance.guidance==VehicleMissileGuidance.Guidance.AirToAir
                    || guidance.guidance==VehicleMissileGuidance.Guidance.AirDefence))continue;
                if(weapon.launcher.requiresLock && (!rider || !rider.IsMounted))continue;
            }
            else if(!weapon.gun)continue;
            // Ground attack prefers loaded explosive systems; air combat keeps the close-range cannon policy.
            float score=ground?(launcher?40:weapon.explosive?30:10)
                :launcher && weapon.ReadyIn<=0 && (!(v.motor is JetMotor) || distance>140)?30:launcher?5:20;
            if(score>best){best=score;choice=i;}
        }
        return choice;
    }
    static Vector3 EnemyVelocity(ArenaParticipant enemy)
    {
        var rider=enemy.GetComponent<VehicleOccupant>();
        return rider && rider.IsMounted?rider.MountedVehicle.Body.linearVelocity:enemy.player?enemy.player.movement.Velocity:Vector3.zero;
    }
    static Vector3 LeadProjectile(Vehicle own,Vector3 point,Vector3 velocity,Vector3 origin,VehicleRocket projectile)
    {
        if(!projectile || !projectile.ballistic)return point; // Hitscan has no travel time; missiles guide themselves.
        Vector3 relative=velocity-own.Body.GetPointVelocity(origin);
        Vector3 distance=point-origin;
        float time=distance.magnitude/Mathf.Max(1,projectile.speed);
        for(int i=0;i<3;i++)time=Mathf.Min(2,(distance+relative*time).magnitude/Mathf.Max(1,projectile.speed));
        return point+relative*time-(projectile.useGravity?Physics.gravity*(.5f*time*time):Vector3.zero);
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


