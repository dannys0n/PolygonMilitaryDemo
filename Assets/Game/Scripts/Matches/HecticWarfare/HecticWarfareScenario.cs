using System.Collections.Generic;
using UnityEngine;

// Event scheduling and deployment are mode policy; soldier/vehicle motors remain shared.
[DefaultExecutionOrder(80)]
public sealed class HecticWarfareScenario : MonoBehaviour
{
    public ArenaMatch match;
    public HecticWarfareRules rules;
    public HecticVehicleDepot redDepot,blueDepot;
    [Header("Population")]
    [Min(4)] public int playersPerTeam=64;
    public GameObject redPlayerPrefab,bluePlayerPrefab;
    public bool anyWeapons;
    public SoldierWeaponDefinition[] dropUtilities;
    public Vehicle transportPlane;
    public GameObject pilotPrefab;
    public Vector3 battlefieldCenter;
    public float battlefieldRadius=2700;
    public float eventInterval=90,eventDuration=60,firstEventDelay=25;
    public float unusedVehicleLifetime=30;
    public int openingDeploymentsPerFrame=4;
    readonly Queue<ArenaParticipant> opening=new Queue<ArenaParticipant>();
    readonly Dictionary<ArenaParticipant,int> planeVisits=new Dictionary<ArenaParticipant,int>();
    readonly Dictionary<Vehicle,float> unattended=new Dictionary<Vehicle,float>();
    readonly List<Vehicle> vehicles=new List<Vehicle>();
    HecticTransportPass[] passes=new HecticTransportPass[2];
    int eventNumber;
    float nextEvent,eventEnds,nextCleanup;
    public int EventNumber=>eventNumber;
    public bool AirEventActive=>Time.time<eventEnds;
    void Awake()
    {
        foreach(ArenaParticipant.Team team in System.Enum.GetValues(typeof(ArenaParticipant.Team)))
        {
            int count=0;
            foreach(var actor in gameObject.scene.GetRootGameObjects())
                foreach(var member in actor.GetComponentsInChildren<ArenaParticipant>(true))if(member.team==team)count++;
            var prefab=team==ArenaParticipant.Team.Red?redPlayerPrefab:bluePlayerPrefab;
            var container=new GameObject("Hectic "+team+" population");container.SetActive(false);
            for(int i=count;i<playersPerTeam;i++)
            {
                var actor=Instantiate(prefab,container.transform);actor.name=team+" Bot "+(i+1);
                var participant=actor.GetComponent<ArenaParticipant>();participant.match=match;participant.team=team;
                var ai=actor.GetComponent<ArenaAIInput>();ai.agent.enabled=false;ai.perceptionInterval=.25f;ai.coverChance=0;
                var vehicleAI=actor.GetComponent<ArenaVehicleAI>();vehicleAI.cruiseSpeed=30;vehicleAI.flightHeight=45;
                var view=actor.GetComponent<SoldierFirstPersonView>();view.firstPerson=false;view.playerCamera.enabled=false;
                actor.transform.position=battlefieldCenter+Vector3.up*10;
            }
            container.SetActive(true);
        }
    }
    public void ResetEvent(){opening.Clear();planeVisits.Clear();nextEvent=Time.time+firstEventDelay;eventEnds=0;eventNumber=0;foreach(var pass in passes)if(pass)Destroy(pass.gameObject);}
    public void QueueOpening(ArenaParticipant actor)=>opening.Enqueue(actor);
    void Start()
    {
        foreach(var actor in match.participants)
        {
            if(!actor.GetComponent<HecticDropSupply>())actor.gameObject.AddComponent<HecticDropSupply>();
            if(actor.GetComponent<ArenaAIInput>()&&!actor.GetComponent<HecticAirDeploymentInput>())actor.gameObject.AddComponent<HecticAirDeploymentInput>();
        }
    }
    void Update()
    {
        if(!match.IsRunning)return;
        int count=Mathf.Min(opening.Count,Mathf.Max(1,openingDeploymentsPerFrame));
        while(count-->0){var actor=opening.Dequeue();if(actor&&!match.Spawn(actor)&&actor.IsAwaitingSpawn)opening.Enqueue(actor);}
        if(Time.time>=nextEvent){StartAirEvent();nextEvent=Time.time+Mathf.Max(eventDuration+10,eventInterval);}
        if(Time.time>=nextCleanup){nextCleanup=Time.time+.5f;EnforceBoundaryAndCleanup();}
    }
    public void StartAirEvent()
    {
        eventNumber++;eventEnds=Time.time+eventDuration;
        for(int team=0;team<2;team++)
        {
            if(passes[team])passes[team].Retire();
            var g=new GameObject("Transport pass "+eventNumber+" team "+team);g.transform.SetParent(transform,false);
            var pass=g.AddComponent<HecticTransportPass>();passes[team]=pass;
            pass.scenario=this;pass.team=(ArenaParticipant.Team)team;pass.prefab=transportPlane;pass.pilotPrefab=pilotPrefab;
            pass.altitude=110+team*40;pass.duration=eventDuration;pass.cruiseSpeed=75;
            pass.Begin();
        }
    }
    public bool TryDeploy(ArenaParticipant actor)
    {
        var pass=passes[(int)actor.team];
        if(AirEventActive&&pass&&(!planeVisits.TryGetValue(actor,out int last)||last!=eventNumber)&&pass.TryDeploy(actor))
        {planeVisits[actor]=eventNumber;return true;}
        if(rules.squads)
        {
            foreach(var member in rules.squads.Members(actor))
                if(rules.squads.TryChooseSpawn(actor,member,new Dictionary<Transform,float>(),out var location))
                {actor.Respawn(location.position,location.rotation);return !actor.IsAwaitingSpawn;}
        }
        return (actor.team==ArenaParticipant.Team.Red?redDepot:blueDepot).TryDeploy(actor);
    }
    void EnforceBoundaryAndCleanup()
    {
        foreach(var actor in match.participants)
        {
            if(!actor||actor.IsAwaitingSpawn||actor.IsSpectator||actor.health.IsDead)continue;
            if(Vector3.ProjectOnPlane(actor.transform.position-battlefieldCenter,Vector3.up).sqrMagnitude>battlefieldRadius*battlefieldRadius||actor.transform.position.y< -30)
                actor.health.ApplyDamage(actor.health.maxHealth,null);
        }
        vehicles.Clear();vehicles.AddRange(Vehicle.Active);
        foreach(var vehicle in vehicles)
        {
            if(!vehicle||vehicle.gameObject.scene!=gameObject.scene||vehicle.IsDestroyed)continue;
            bool used=vehicle.IsOccupied;
            if(!used)foreach(var actor in match.participants)
                if(actor.player&&actor.player.controller.Support==vehicle.Body&&!actor.health.IsDead){used=true;break;}
            if(used){unattended[vehicle]=Time.time;continue;}
            if(!unattended.TryGetValue(vehicle,out float since)){unattended[vehicle]=Time.time;continue;}
            if(Time.time-since>=unusedVehicleLifetime)vehicle.ApplyDamage(vehicle.maxHealth,null);
            if(Vector3.ProjectOnPlane(vehicle.transform.position-battlefieldCenter,Vector3.up).sqrMagnitude>(battlefieldRadius+50)*(battlefieldRadius+50))vehicle.ApplyDamage(vehicle.maxHealth,null);
        }
        foreach(var vehicle in new List<Vehicle>(unattended.Keys))if(!vehicle||vehicle.IsDestroyed)unattended.Remove(vehicle);
    }
}
