using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Mission policy only: production infantry/vehicle AI still owns perception, steering and firing.
[DefaultExecutionOrder(-25)]
public sealed class CombinedArmsOrchestrator : MonoBehaviour
{
    public enum Role { Infantry, Armor, Transport, Helicopter, Fighter }
    public ArenaMatch match;
    public ConquestRules rules;
    [Min(.2f)] public float decisionInterval=1;
    [Min(1)] public float assignmentDuration=18;
    [Min(1)] public float loadingTime=8;
    [Min(1)] public float boardingTimeout=30;
    [Min(1)] public float dismountRadius=28;
    [Min(0)] public int fighterPilotsPerTeam=2,helicopterPilotsPerTeam=2,armorDriversPerTeam=2,transportDriversPerTeam=2;
    public sealed class Order
    {
        public ArenaParticipant actor;
        public Role role;
        public ConquestCapturePoint objective;
        public Vehicle vehicle;
        public float assignedAt,loadingUntil,reconsiderAt,retryAt;
        public Vector3 boardingProgress, destination;
        public float progressAt;
        public bool boarded;
        public string status="Moving to objective";
    }
    readonly Dictionary<ArenaParticipant,Order> orders=new Dictionary<ArenaParticipant,Order>();
    readonly List<ArenaParticipant> removed=new List<ArenaParticipant>();
    NavMeshPath objectivePath;
    readonly HashSet<Vehicle> claimed=new HashSet<Vehicle>();
    public IEnumerable<Order> Orders=>orders.Values;
    float nextDecision;
    void Awake(){objectivePath=new NavMeshPath();}
    void Update(){if(match && match.IsRunning && Time.time>=nextDecision){nextDecision=Time.time+decisionInterval;Replan();}}
    void OnDisable(){foreach(var order in orders.Values)if(order.actor){var ai=order.actor.GetComponent<ArenaVehicleAI>();if(ai){ai.ReleaseMission();ai.MissionManaged=false;}}orders.Clear();}
    public bool IsCrew(ArenaParticipant actor)=>orders.TryGetValue(actor,out var order) && order.role!=Role.Infantry;
    public Role RoleForIndex(int index)
    {
        if(index<fighterPilotsPerTeam)return Role.Fighter;index-=fighterPilotsPerTeam;
        if(index<helicopterPilotsPerTeam)return Role.Helicopter;index-=helicopterPilotsPerTeam;
        if(index<armorDriversPerTeam)return Role.Armor;index-=armorDriversPerTeam;
        return index<transportDriversPerTeam?Role.Transport:Role.Infantry;
    }
    public bool TryGetDestination(ArenaParticipant actor,out Vector3 destination)
    {
        if(orders.TryGetValue(actor,out var order) && order.objective)
        {
            destination=order.destination;
            return true;
        }
        destination=default;return false;
    }
    public void Replan()
    {
        if(!isActiveAndEnabled || !match || !rules || !match.IsRunning)return;
        removed.Clear();
        foreach(var actor in orders.Keys)if(!actor || System.Array.IndexOf(match.participants,actor)<0 || !IsBot(actor))removed.Add(actor);
        foreach(var actor in removed)
        {
            Release(orders[actor]);
            if(actor && actor.TryGetComponent<ArenaVehicleAI>(out var ai))ai.MissionManaged=false;
            orders.Remove(actor);
        }
        int red=0,blue=0;
        foreach(var actor in match.participants)
        {
            if(!IsBot(actor))continue;
            int index=actor.team==ArenaParticipant.Team.Red?red++:blue++;
            if(!orders.TryGetValue(actor,out var order))orders[actor]=order=new Order{actor=actor,role=RoleForIndex(index)};
            var motorAI=actor.GetComponent<ArenaVehicleAI>();if(motorAI)motorAI.MissionManaged=true;
            if(actor.health.IsDead || actor.IsAwaitingSpawn){Release(order);order.status="Respawning";continue;}
            if(!order.objective || Time.time>=order.reconsiderAt || order.objective.Owner==actor.team && Time.time-actor.GetComponent<ArenaAIInput>().LastCombatTime>rules.postCaptureDefenseTime)
                ChooseObjective(order);
        }
        claimed.Clear();
        foreach(var order in orders.Values)
            if(order.vehicle && !order.vehicle.IsDestroyed && Active(order.actor)
                && DriverOrder(order.vehicle)==order)claimed.Add(order.vehicle);
        // Driver orders are processed before passengers, so each vehicle has a single owner.
        foreach(var order in orders.Values)if(order.role!=Role.Infantry && Active(order.actor))CommandCrew(order);
        foreach(var order in orders.Values)if(order.role==Role.Infantry && Active(order.actor))CommandInfantry(order);
    }
    bool Active(ArenaParticipant actor)=>actor && !actor.health.IsDead && !actor.IsAwaitingSpawn;
    static bool IsBot(ArenaParticipant actor)=>actor && actor.TryGetComponent<SoldierController>(out var controller) && controller.inputSource is ArenaAIInput;
    static bool HasHumanCrew(Vehicle vehicle)
    {
        foreach(var seat in vehicle.seats)
            if(seat.occupant && seat.occupant.IsHuman)return true;
        return false;
    }
    Order DriverOrder(Vehicle vehicle)
    {
        foreach(var order in orders.Values)
            if(order.vehicle==vehicle && Active(order.actor))
            {
                var rider=order.actor.GetComponent<VehicleOccupant>();
                if(vehicle.Driver==rider || !rider.IsMounted && !order.boarded && order.role!=Role.Infantry)return order;
            }
        return null;
    }
    void Release(Order order)
    {
        if(order.vehicle && order.actor)order.vehicle.ReleaseReservation(order.actor.GetComponent<VehicleOccupant>());
        if(order.actor && order.actor.TryGetComponent<ArenaVehicleAI>(out var ai))ai.ReleaseMission();
        order.vehicle=null;order.boarded=false;
    }
    void ChooseObjective(Order order)
    {
        ConquestCapturePoint best=null;Vector3 bestPosition=default;float bestScore=float.NegativeInfinity;
        foreach(var point in rules.points)
        {
            if(!point)continue;
            int index=System.Array.IndexOf(match.participants,order.actor);
            Vector3 proposed=point.transform.position+Quaternion.Euler(0,index*137.5f,0)*Vector3.forward*Mathf.Min(6,point.radius*.4f);
            var ai=order.actor.GetComponent<ArenaAIInput>();
            var filter=new NavMeshQueryFilter{agentTypeID=ai.agent.agentTypeID,areaMask=ai.agent.areaMask};
            if(!NavMesh.SamplePosition(proposed,out var hit,3,filter))
                if(!NavMesh.SamplePosition(point.transform.position,out hit,3,filter))continue;
            if(!point.Contains(hit.position))continue;
            if(!order.actor.GetComponent<VehicleOccupant>().IsMounted
                && (!NavMesh.CalculatePath(order.actor.transform.position,hit.position,filter,objectivePath) || objectivePath.status!=NavMeshPathStatus.PathComplete))continue;
            int assigned=0;
            foreach(var other in orders.Values)if(other!=order && other.actor && other.actor.team==order.actor.team && other.objective==point && Active(other.actor))assigned++;
            // Human intentions are unknown; account for their current presence, without issuing orders.
            foreach(var other in match.participants)
                if(Active(other) && !IsBot(other) && other.team==order.actor.team
                    && Vector3.Distance(other.transform.position,point.transform.position)<point.radius+35)assigned++;
            float score=(point.Owner==order.actor.team?0:100)-assigned*8-Mathf.Min(Vector3.Distance(order.actor.transform.position,point.transform.position)*.025f,20);
            if(score>bestScore){bestScore=score;best=point;bestPosition=hit.position;}
        }
        order.objective=best;order.destination=bestPosition;order.reconsiderAt=Time.time+assignmentDuration;
    }
    bool Friendly(Vehicle vehicle,ArenaParticipant actor)
    {
        foreach(var seat in vehicle.seats)if(seat.occupant && seat.occupant.faction!=(int)actor.team)return false;
        return true;
    }
    bool Matches(Vehicle vehicle,Role role)
    {
        bool armed=vehicle.turret || vehicle.GetComponent<VehicleLauncher>();
        switch(role){case Role.Fighter:return vehicle.motor is JetMotor && armed;
            case Role.Helicopter:return vehicle.motor is HelicopterMotor && armed;
            case Role.Armor:return !vehicle.IsAircraft && armed;
            case Role.Transport:return !vehicle.IsAircraft && !armed && vehicle.seats.Length>=3;
            default:return false;}
    }
    void CommandCrew(Order order)
    {
        var actor=order.actor;var ai=actor.GetComponent<ArenaVehicleAI>();if(!ai)return;
        var rider=ai.occupant;
        if(rider.IsMounted)
        {
            if(rider.MountedVehicle.seats[rider.SeatIndex].role!=Vehicle.SeatRole.Driver)
            {CommandMountedSupport(order,ai);return;}
            if(!order.boarded)order.loadingUntil=Time.time+loadingTime;
            order.vehicle=rider.MountedVehicle;order.boarded=true;
            if(!TryGetDestination(actor,out var goal))return;
            ai.SetMissionGoal(goal,true);
            ai.flightHeight=55+System.Array.IndexOf(match.participants,actor)%3*20;
            ai.HoldDeparture=Time.time<order.loadingUntil && (!order.vehicle.IsAircraft || System.Array.Exists(order.vehicle.seats,s=>s.role==Vehicle.SeatRole.Gunner));
            order.status=ai.HoldDeparture?"Loading crew":"Engage / support objective";
            return;
        }
        var infantry=actor.GetComponent<ArenaAIInput>();
        if(infantry.IsCombatAlert && infantry.Target)
        {
            Release(order);order.retryAt=Time.time+5;order.status="Engage after dismount";return;
        }
        if(order.boarded || order.vehicle && (order.vehicle.IsDestroyed || !Friendly(order.vehicle,actor) || order.vehicle.Driver || Time.time-order.assignedAt>boardingTimeout))
        {Release(order);order.retryAt=Time.time+5;}
        if(!order.vehicle && Time.time>=order.retryAt)
        {
            float nearest=float.PositiveInfinity;
            foreach(var vehicle in Vehicle.Active)
            {
                if(vehicle.gameObject.scene!=gameObject.scene || vehicle.IsDestroyed || vehicle.Driver || claimed.Contains(vehicle) || !Matches(vehicle,order.role) || !Friendly(vehicle,actor) || !vehicle.CanExit || vehicle.Body.linearVelocity.sqrMagnitude>9)continue;
                float distance=(vehicle.transform.position-actor.transform.position).sqrMagnitude;
                if(distance>280*280 || !vehicle.CanBoard(rider,0))continue;
                // A nearby human waiting in a passenger/gunner seat needs a driver before another empty vehicle does.
                float score=distance*(HasHumanCrew(vehicle)? .25f:1f);
                if(score>=nearest)continue;
                nearest=score;order.vehicle=vehicle;
            }
            if(order.vehicle && !order.vehicle.Reserve(rider,0))order.vehicle=null;
            if(order.vehicle){claimed.Add(order.vehicle);order.assignedAt=order.progressAt=Time.time;order.boardingProgress=actor.transform.position;order.loadingUntil=Time.time+loadingTime;}
        }
        if(order.vehicle && BoardingStalled(order))
        {Release(order);order.retryAt=Time.time+20;}
        if(order.vehicle){ai.SetTransportOrder(order.vehicle,true);order.status="Board assigned "+order.role;}
        else {ai.ReleaseMission();order.status="No reachable vehicle / fight on foot";}
    }
    void CommandInfantry(Order order)
    {
        var actor=order.actor;var ai=actor.GetComponent<ArenaVehicleAI>();if(!ai)return;
        if(ai.occupant.IsMounted)
        {
            if(ai.occupant.MountedVehicle.seats[ai.occupant.SeatIndex].role==Vehicle.SeatRole.Driver)CommandCrew(order);
            else CommandMountedSupport(order,ai);
            return;
        }
        if(order.boarded){Release(order);order.retryAt=Time.time+5;}
        if(order.vehicle && (order.vehicle.IsDestroyed || Time.time-order.assignedAt>boardingTimeout || Vector3.Distance(actor.transform.position,order.vehicle.transform.position)>120 || !Friendly(order.vehicle,actor) || !order.vehicle.CanExit || order.vehicle.Body.linearVelocity.sqrMagnitude>9
            || order.vehicle.FirstAvailableSeat(ai.occupant)<0 || order.vehicle.FirstAvailableSeat(ai.occupant)==0 && !HasHumanCrew(order.vehicle) || BoardingStalled(order)))
        {Release(order);order.retryAt=Time.time+20;}
        if(order.vehicle && Time.time<order.retryAt){order.vehicle=null;ai.ReleaseMission();}
        if(!order.vehicle && Time.time>=order.retryAt && !actor.GetComponent<ArenaAIInput>().IsCombatAlert)
        {
            float best=120*120;
            foreach(var vehicle in Vehicle.Active)
            {
                if(!vehicle || vehicle.gameObject.scene!=gameObject.scene || vehicle.IsDestroyed
                    || !Friendly(vehicle,actor) || !vehicle.CanExit || vehicle.Body.linearVelocity.sqrMagnitude>9)continue;
                var driverOrder=DriverOrder(vehicle);
                bool humanCrew=HasHumanCrew(vehicle);
                if(driverOrder==null && !humanCrew)continue;
                if(driverOrder!=null && driverOrder.actor.team!=actor.team)continue;
                int seat=vehicle.FirstAvailableSeat(ai.occupant);
                if(seat<0 || seat==0 && (!humanCrew || driverOrder!=null))continue;
                if(vehicle.IsAircraft && !humanCrew && vehicle.seats[seat].role!=Vehicle.SeatRole.Gunner)continue;
                if(!humanCrew && Time.time>=driverOrder.loadingUntil && vehicle.seats[seat].role!=Vehicle.SeatRole.Gunner)continue;
                float distance=(vehicle.transform.position-actor.transform.position).sqrMagnitude;
                if(distance>=best)continue;
                best=distance;order.vehicle=vehicle;
                if(driverOrder!=null){order.objective=driverOrder.objective;order.destination=driverOrder.destination;}
            }
            if(order.vehicle)
            {
                int seat=order.vehicle.FirstAvailableSeat(ai.occupant);
                if(seat<0 || !order.vehicle.Reserve(ai.occupant,seat))order.vehicle=null;
                else {order.assignedAt=order.progressAt=Time.time;order.boardingProgress=actor.transform.position;}
            }
        }
        if(order.vehicle){ai.SetTransportOrder(order.vehicle,true);order.status="Board support vehicle";}
        else {ai.ReleaseMission();order.status="Capture / defend objective";}
    }
    void CommandMountedSupport(Order order,ArenaVehicleAI ai)
    {
        order.vehicle=ai.occupant.MountedVehicle;order.boarded=true;
        // If a human leaves the controls, an existing AI rider can take the vacant driver seat.
        if(!order.vehicle.Driver && ai.occupant.SwitchSeat(0))
        {order.boarded=false;CommandCrew(order);return;}
        var driver=DriverOrder(order.vehicle);
        if(driver!=null && driver!=order){order.objective=driver.objective;order.destination=driver.destination;}
        if(!TryGetDestination(order.actor,out var goal))return;
        var seat=order.vehicle.seats[ai.occupant.SeatIndex];
        bool humanDriver=order.vehicle.Driver && order.vehicle.Driver.IsHuman;
        bool dismount=seat.role==Vehicle.SeatRole.Passenger && order.vehicle.CanExit
            && (!order.vehicle.IsAircraft || humanDriver)
            && (Vector3.Distance(order.vehicle.transform.position,goal)<dismountRadius || order.actor.GetComponent<ArenaAIInput>().IsCombatAlert);
        ai.HoldDeparture=false;
        if(dismount){ai.SetTransportOrder(order.vehicle,false);order.retryAt=Time.time+20;order.status="Dismount and capture";}
        else {ai.SetMissionGoal(goal,false);order.status=seat.role==Vehicle.SeatRole.Gunner?"Operate weapon":"Ride to objective";}
    }
    bool BoardingStalled(Order order)
    {
        if((order.actor.transform.position-order.boardingProgress).sqrMagnitude>1)
        {order.boardingProgress=order.actor.transform.position;order.progressAt=Time.time;}
        return Time.time-order.progressAt>6;
    }
}
