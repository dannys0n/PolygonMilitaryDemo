using System.Collections.Generic;
using UnityEngine;

// A company is a temporary transport plan, not another individual decision tree.
public class HecticCompanySupervisor : MonoBehaviour
{
    public HecticAIOrchestrator orchestrator;
    public float planningInterval=3, embarkTimeout=18, unloadRadius=35;
    public sealed class Company
    {
        public ArenaParticipant.Team team;
        public ConquestCapturePoint objective;
        public float formedAt;
        public readonly List<ArenaVehicleAI> troops=new List<ArenaVehicleAI>();
        public readonly List<Vehicle> fleet=new List<Vehicle>();
    }
    public readonly List<Company> Companies=new List<Company>();
    readonly Dictionary<ArenaVehicleAI,Company> companyOf=new Dictionary<ArenaVehicleAI,Company>();
    readonly Dictionary<ArenaVehicleAI,BoardingOrder> boarding=new Dictionary<ArenaVehicleAI,BoardingOrder>();
    readonly HashSet<Vehicle> claimed=new HashSet<Vehicle>();
    public struct BoardingOrder {public Vehicle vehicle;public int seat;public bool physical;public Transform entry;public Vector3 localDestination;public float expires;}
    float nextPlan;
    void Awake(){if(!orchestrator)orchestrator=GetComponent<HecticAIOrchestrator>();}
    void Update()
    {
        if(!orchestrator||!orchestrator.match||!orchestrator.match.IsRunning)return;
        if(Time.time<nextPlan)return;nextPlan=Time.time+planningInterval;
        if(Companies.Count==0||AllPlansExpired())BuildCompanies();
    }
    bool AllPlansExpired(){foreach(var c in Companies)if(Time.time-c.formedAt<45)return false;return true;}
    public void BuildCompanies()
    {
        foreach(var pair in boarding)if(pair.Value.vehicle)pair.Value.vehicle.ReleaseReservation(pair.Key.occupant);
        Companies.Clear();companyOf.Clear();boarding.Clear();claimed.Clear();
        foreach(var order in orchestrator.Orders)
        {
            if(!order.point)continue;
            var company=Companies.Find(c=>c.team==order.team&&c.objective==order.point);
            if(company==null){company=new Company{team=order.team,objective=order.point,formedAt=Time.time};Companies.Add(company);}
            foreach(var ai in order.members)if(ai&&ai.TryGetComponent<ArenaVehicleAI>(out var transport))
            {company.troops.Add(transport);companyOf[transport]=company;}
        }
        foreach(var company in Companies)PlanFleet(company);
    }
    void PlanFleet(Company company)
    {
        var waiting=new List<ArenaVehicleAI>();
        foreach(var troop in company.troops)
        {
            if(troop.infantry.life.health.IsDead||troop.infantry.participant.IsAwaitingSpawn)continue;
            var deployment=troop.GetComponent<HecticAirDeploymentInput>();if(deployment&&deployment.IsActive)continue;
            if(troop.occupant.IsMounted){var v=troop.occupant.MountedVehicle;if(!company.fleet.Contains(v))company.fleet.Add(v);claimed.Add(v);}
            else if(!troop.infantry.IsCombatAlert)waiting.Add(troop);
        }
        // HQ deployment already seats everyone. Reassign at most two seated passengers
        // to physical cargo boarding while parked, so this path is exercised naturally.
        foreach(var v in company.fleet)
        {
            if(!v||!v.CanExit||!v.hasRenderedInterior||!v.TryGetComponent<VehicleDoors>(out var access)||access.doors.Length==0||v.motor is JetMotor)continue;
            int extra=0;
            foreach(var troop in company.troops)
            {
                if(extra>=2)break;
                if(!troop.occupant.IsMounted||troop.occupant.MountedVehicle!=v||v.seats[troop.occupant.SeatIndex].role!=Vehicle.SeatRole.Passenger)continue;
                troop.occupant.Leave(false);waiting.Add(troop);extra++;
            }
        }
        var candidates=new List<Vehicle>();
        foreach(var v in Vehicle.Active)
        {
            if(!v||v.IsDestroyed||(claimed.Contains(v)&&!company.fleet.Contains(v))||(v.IsOccupied&&!company.fleet.Contains(v))||v.Body.linearVelocity.sqrMagnitude>4||v.gameObject.scene!=gameObject.scene)continue;
            bool nearby=false;foreach(var troop in waiting)if((troop.transform.position-v.transform.position).sqrMagnitude<10000){nearby=true;break;}
            if(nearby)candidates.Add(v);
        }
        // High-capacity transports consume fewer drivers and produce fewer traffic jams.
        candidates.Sort((a,b)=>(b.seats.Length+(company.fleet.Contains(b)?100:0)).CompareTo(a.seats.Length+(company.fleet.Contains(a)?100:0)));
        foreach(var v in candidates)
        {
            if(waiting.Count==0)break;
            bool driver=v.Driver&&v.Driver.faction==(int)company.team;
            var doors=v.GetComponent<VehicleDoors>();
            if(doors&&doors.doors.Length>0&&v.hasRenderedInterior&&!(v.motor is JetMotor))
            {
                int extra=Mathf.Min(2,Mathf.Max(0,waiting.Count-(driver?0:1)));
                for(int rider=0;rider<extra;rider++)
                {
                    var pick=waiting[waiting.Count-1];waiting.RemoveAt(waiting.Count-1);
                    var door=doors.doors[0];if(!door.open)doors.Toggle(0);
                    boarding[pick]=new BoardingOrder{vehicle=v,seat=-1,physical=true,entry=door.interaction,
                        localDestination=CargoPoint(v,door.interaction,rider),expires=Time.time+embarkTimeout};
                }
            }
            for(int seat=0;seat<v.seats.Length&&waiting.Count>0;seat++)
            {
                ArenaVehicleAI pick=null;float best=float.PositiveInfinity;
                foreach(var troop in waiting)
                {
                    if(!v.CanBoard(troop.occupant,seat))continue;
                    float distance=(troop.transform.position-v.transform.position).sqrMagnitude;
                    if(distance<best){best=distance;pick=troop;}
                }
                if(!pick)continue;
                // Only crew aircraft with a real pilot; a jet is independent reconnaissance.
                if(!v.Reserve(pick.occupant,seat))continue;
                boarding[pick]=new BoardingOrder{vehicle=v,seat=seat,entry=v.NearestEntryPoint(pick.transform.position),expires=Time.time+embarkTimeout};
                waiting.Remove(pick);driver|=v.seats[seat].role==Vehicle.SeatRole.Driver;
            }
            if(!driver)continue;
            claimed.Add(v);if(!company.fleet.Contains(v))company.fleet.Add(v);

        }
    }
    public static Vector3 CargoPoint(Vehicle vehicle,Transform entry,int rider)
    {
        Vector3 local=vehicle.transform.InverseTransformPoint(entry.position);
        // NavigationBounds includes tail/engine volume. Step inward from the actual
        // door rather than sending helicopter riders through the rear bulkhead.
        Vector3 inward=Vector3.ProjectOnPlane(vehicle.NavigationBounds.center-local,Vector3.up).normalized;
        Vector3 tangent=Vector3.Cross(Vector3.up,inward);
        return local+inward*1.6f+tangent*(rider==0?-.45f:.45f);
    }
    public bool TryBoard(ArenaVehicleAI troop,out BoardingOrder order)
    {
        if(boarding.TryGetValue(troop,out order)&&order.vehicle&&!order.vehicle.IsDestroyed&&Time.time<order.expires)return true;
        boarding.Remove(troop);order=default;return false;
    }
    public void FinishBoarding(ArenaVehicleAI troop){boarding.Remove(troop);}
    public bool TryGoal(ArenaVehicleAI troop,out Vector3 goal)
    {
        goal=default;
        // A mixed-squad transport follows its pilot's company; passengers dismount together.
        var leader=troop;
        Vehicle carrier=troop.occupant.IsMounted?troop.occupant.MountedVehicle:troop.GetComponent<HecticPhysicalBoarding>()?.Carrier;
        if(carrier&&carrier.Driver&&carrier.Driver.TryGetComponent<ArenaVehicleAI>(out var pilot))leader=pilot;
        if(!companyOf.TryGetValue(leader,out var c)||!c.objective)return false;
        var current=orchestrator?orchestrator.ObjectivePoint(leader.infantry.participant):null;
        goal=(current?current:c.objective).transform.position;return true;
    }
    public bool HoldDeparture(ArenaVehicleAI driver)
    {
        if(!companyOf.TryGetValue(driver,out var c)||Time.time-c.formedAt>=embarkTimeout)return false;
        // Jets scout immediately; other vehicles wait only for their own assigned riders.
        var vehicle=driver.occupant.MountedVehicle;if(vehicle.motor is JetMotor)return false;
        foreach(var pair in boarding)if(pair.Value.vehicle==vehicle&&pair.Key&&!pair.Key.infantry.life.health.IsDead&&Time.time<pair.Value.expires)return true;
        return false;
    }
    public bool ShouldUnload(ArenaVehicleAI troop)
    {
        if(!troop.occupant.IsMounted||!TryGoal(troop,out var goal))return false;
        var v=troop.occupant.MountedVehicle;
        return v.seats[troop.occupant.SeatIndex].role==Vehicle.SeatRole.Passenger&&v.CanExit
            &&(Vector3.ProjectOnPlane(v.transform.position-goal,Vector3.up).sqrMagnitude<unloadRadius*unloadRadius||troop.infantry.IsCombatAlert);
    }
}
