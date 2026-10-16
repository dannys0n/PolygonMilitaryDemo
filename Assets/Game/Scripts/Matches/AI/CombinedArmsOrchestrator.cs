using System.Collections.Generic;
using UnityEngine;

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
        public bool boarded;
        public string status="Moving to objective";
    }
    readonly Dictionary<ArenaParticipant,Order> orders=new Dictionary<ArenaParticipant,Order>();
    readonly List<ArenaParticipant> removed=new List<ArenaParticipant>();
    readonly HashSet<Vehicle> claimed=new HashSet<Vehicle>();
    public IEnumerable<Order> Orders=>orders.Values;
    float nextDecision;
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
            int index=System.Array.IndexOf(match.participants,actor);
            destination=order.objective.transform.position+Quaternion.Euler(0,index*137.5f,0)*Vector3.forward*Mathf.Min(6,order.objective.radius*.4f);
            return true;
        }
        destination=default;return false;
    }
    public void Replan()
    {
        if(!isActiveAndEnabled || !match || !rules || !match.IsRunning)return;
        removed.Clear();
        foreach(var actor in orders.Keys)if(!actor || System.Array.IndexOf(match.participants,actor)<0)removed.Add(actor);
        foreach(var actor in removed)orders.Remove(actor);
        int red=0,blue=0;
        foreach(var actor in match.participants)
        {
            if(!actor || !actor.GetComponent<ArenaAIInput>() || actor.GetComponent<SoldierLocalInput>())continue;
            int index=actor.team==ArenaParticipant.Team.Red?red++:blue++;
            if(!orders.TryGetValue(actor,out var order))orders[actor]=order=new Order{actor=actor,role=RoleForIndex(index)};
            var motorAI=actor.GetComponent<ArenaVehicleAI>();if(motorAI)motorAI.MissionManaged=true;
            if(actor.health.IsDead || actor.IsAwaitingSpawn){Release(order);order.status="Respawning";continue;}
            if(!order.objective || Time.time>=order.reconsiderAt || order.objective.Owner==actor.team && Time.time-actor.GetComponent<ArenaAIInput>().LastCombatTime>rules.postCaptureDefenseTime)
                ChooseObjective(order);
        }
        claimed.Clear();
        foreach(var order in orders.Values)if(order.role!=Role.Infantry && order.vehicle && !order.vehicle.IsDestroyed && !order.actor.health.IsDead && !order.actor.IsAwaitingSpawn)claimed.Add(order.vehicle);
        // Driver orders are processed before passengers, so each vehicle has a single owner.
        foreach(var order in orders.Values)if(order.role!=Role.Infantry && Active(order.actor))CommandCrew(order);
        foreach(var order in orders.Values)if(order.role==Role.Infantry && Active(order.actor))CommandInfantry(order);
    }
    bool Active(ArenaParticipant actor)=>actor && !actor.health.IsDead && !actor.IsAwaitingSpawn;
    void Release(Order order)
    {
        if(order.actor && order.actor.TryGetComponent<ArenaVehicleAI>(out var ai))ai.ReleaseMission();
        order.vehicle=null;order.boarded=false;
    }
    void ChooseObjective(Order order)
    {
        ConquestCapturePoint best=null;float bestScore=float.NegativeInfinity;
        foreach(var point in rules.points)
        {
            if(!point)continue;
            int assigned=0;
            foreach(var other in orders.Values)if(other!=order && other.actor && other.actor.team==order.actor.team && other.objective==point && Active(other.actor))assigned++;
            float score=(point.Owner==order.actor.team?0:100)-assigned*8-Mathf.Min(Vector3.Distance(order.actor.transform.position,point.transform.position)*.025f,20);
            if(score>bestScore){bestScore=score;best=point;}
        }
        order.objective=best;order.reconsiderAt=Time.time+assignmentDuration;
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
            if(!order.boarded)order.loadingUntil=Time.time+loadingTime;
            order.vehicle=rider.MountedVehicle;order.boarded=true;
            if(!TryGetDestination(actor,out var goal))return;
            ai.SetMissionGoal(goal,true);
            ai.flightHeight=55+System.Array.IndexOf(match.participants,actor)%3*20;
            ai.HoldDeparture=!order.vehicle.IsAircraft && Time.time<order.loadingUntil;
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
                if(vehicle.gameObject.scene!=gameObject.scene || vehicle.IsDestroyed || vehicle.Driver || claimed.Contains(vehicle) || !Matches(vehicle,order.role) || !Friendly(vehicle,actor))continue;
                float distance=(vehicle.transform.position-actor.transform.position).sqrMagnitude;
                if(distance>220*220 || distance>=nearest || !vehicle.CanBoard(rider,0))continue;
                nearest=distance;order.vehicle=vehicle;
            }
            if(order.vehicle){claimed.Add(order.vehicle);order.assignedAt=Time.time;order.loadingUntil=Time.time+loadingTime;}
        }
        if(order.vehicle){ai.SetTransportOrder(order.vehicle,true);order.status="Board assigned "+order.role;}
        else {ai.ReleaseMission();order.status="No reachable vehicle / fight on foot";}
    }
    void CommandInfantry(Order order)
    {
        var actor=order.actor;var ai=actor.GetComponent<ArenaVehicleAI>();if(!ai)return;
        if(ai.occupant.IsMounted)
        {
            order.vehicle=ai.occupant.MountedVehicle;
            if(!TryGetDestination(actor,out var goal))return;
            var seat=order.vehicle.seats[ai.occupant.SeatIndex];
            bool dismount=seat.role==Vehicle.SeatRole.Passenger && !order.vehicle.IsAircraft && order.vehicle.CanExit
                && (Vector3.Distance(order.vehicle.transform.position,goal)<dismountRadius || actor.GetComponent<ArenaAIInput>().IsCombatAlert);
            if(dismount){ai.SetTransportOrder(order.vehicle,false);order.retryAt=Time.time+20;order.status="Dismount and capture";}
            else {ai.SetMissionGoal(goal,false);order.status=seat.role==Vehicle.SeatRole.Gunner?"Operate weapon":"Ride to objective";}
            return;
        }
        if(order.vehicle && (order.vehicle.IsDestroyed || Time.time-order.assignedAt>boardingTimeout || Vector3.Distance(actor.transform.position,order.vehicle.transform.position)>65 || !Friendly(order.vehicle,actor)))
        {Release(order);order.retryAt=Time.time+5;}
        if(order.vehicle && Time.time<order.retryAt){order.vehicle=null;ai.ReleaseMission();}
        if(!order.vehicle && Time.time>=order.retryAt && !actor.GetComponent<ArenaAIInput>().IsCombatAlert)
        {
            float best=65*65;
            foreach(var driverOrder in orders.Values)
            {
                var vehicle=driverOrder.vehicle;
                if(driverOrder.role==Role.Infantry || !vehicle || vehicle.IsAircraft || vehicle.IsDestroyed || driverOrder.actor.team!=actor.team
                    || Time.time>=driverOrder.loadingUntil || !Friendly(vehicle,actor) || vehicle.FirstAvailableSeat(ai.occupant)<1)continue;
                float distance=(vehicle.transform.position-actor.transform.position).sqrMagnitude;
                if(distance>=best)continue;
                best=distance;order.vehicle=vehicle;order.objective=driverOrder.objective;order.assignedAt=Time.time;
            }
        }
        if(order.vehicle){ai.SetTransportOrder(order.vehicle,true);order.status="Board support vehicle";}
        else {ai.ReleaseMission();order.status="Capture / defend objective";}
    }
}
