using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Explicit buttons and telemetry for manual checks; production AI receives every command.
public sealed class CombinedArmsTestScenario : MonoBehaviour
{
    public bool accuracyTest;
    public MatchController match;
    public CombinedArmsOrchestrator orchestrator;
    public GameObject actorPrefab;
    public Vehicle[] vehiclePrefabs;
    public BehaviorTestCamera observer;
    public int actorsPerTeam=12;
    readonly List<MatchParticipant> actors=new List<MatchParticipant>();
    readonly List<Vehicle> vehicles=new List<Vehicle>();
    GameObject population;
    bool settingUp,holdInfantry;
    int errors;
    Vector2 scroll;
    string status="Choose a scenario, then inspect production orders and focus telemetry.";
    void Start(){match.enabled=false;orchestrator.enabled=false;Time.timeScale=1;}
    void OnEnable()=>Application.logMessageReceived+=Log;
    void OnDisable()=>Application.logMessageReceived-=Log;
    void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception){errors++;status=message;}}
    void Update(){if(match.IsRunning)match.Tick(Time.deltaTime);}
    public void ResetScenario()
    {
        StopAllCoroutines();settingUp=false;orchestrator.enabled=false;
        foreach(var actor in actors)if(actor){var rider=actor.GetComponent<VehicleOccupant>();if(rider&&rider.IsMounted)rider.Leave(true);actor.gameObject.SetActive(false);Destroy(actor.gameObject);}
        actors.Clear();foreach(var vehicle in vehicles)if(vehicle){vehicle.gameObject.SetActive(false);Destroy(vehicle.gameObject);}vehicles.Clear();
        if(population)Destroy(population);population=new GameObject("Test population");population.SetActive(false);
        observer.follow=null;errors=0;status="Reset. Ready.";
    }
    public void StartScenario(){if(!settingUp){ResetScenario();StartCoroutine(Setup());}}
    IEnumerator Setup()
    {
        settingUp=true;yield return null;
        int count=accuracyTest?2:actorsPerTeam;
        for(int side=0;side<2;side++)
        {
            if(!accuracyTest)for(int i=0;i<vehiclePrefabs.Length;i++)
            {
                var point=new Vector3((i%3-1)*25,.6f,(side==0?-1:1)*(70+i/3*35));
                vehicles.Add(Instantiate(vehiclePrefabs[i],point,Quaternion.Euler(0,side==0?0:180,0)));
            }
            for(int i=0;i<count;i++)
            {
                var g=Instantiate(actorPrefab,population.transform);g.name=(side==0?"Red":"Blue")+" test "+(i+1);
                var actor=g.GetComponent<MatchParticipant>();actor.match=match;actor.team=(MatchParticipant.Team)side;
                g.transform.position=new Vector3(i*3,1,side==0?-42:42);
                g.GetComponent<SoldierFirstPersonView>().firstPerson=false;
                g.GetComponent<SoldierFirstPersonView>().playerCamera.enabled=false;
                actors.Add(actor);
            }
        }
        population.SetActive(true);yield return null;match.BeginMatch();
        for(int i=0;i<actors.Count;i++)
        {
            var actor=actors[i];int side=(int)actor.team,index=i%count;
            var point=accuracyTest?new Vector3(side==0?-12:12,.15f,index*8):new Vector3((index%4-1.5f)*4,.2f,(side==0?-1:1)*(62+index/4*4));
            actor.Respawn(point,Quaternion.Euler(0,accuracyTest?(side==0?90:-90):(side==0?0:180),0));
            var ai=actor.GetComponent<AIInput>();ai.enabled=true;ai.HoldMovement=false;
            actor.GetComponent<SoldierController>().SetControl(ai, SoldierControlRole.AI, false);
            var drive=actor.GetComponent<VehicleAI>();drive.enabled=!accuracyTest;
        }
        orchestrator.enabled=!accuracyTest;orchestrator.Replan();settingUp=false;
        status=accuracyTest?"2 vs 2. After 1.5s focus: zero artificial aim error or tracking delay.":"12 vs 12. Production boarding, loading, driving, gunners, objectives and aircraft combat.";
    }
    public void SetOrder(bool enter)
    {
        orchestrator.enabled=false;
        foreach(var actor in actors)
        {
            if(!actor||actor.health.IsDead)continue;
            var ai=actor.GetComponent<VehicleAI>();if(!ai)continue;
            Vehicle nearest=null;float best=float.PositiveInfinity;
            if(ai.occupant.IsMounted)nearest=ai.occupant.MountedVehicle;
            else foreach(var vehicle in vehicles)if(vehicle&&!vehicle.IsDestroyed){float d=(actor.transform.position-vehicle.transform.position).sqrMagnitude;if(d<best){best=d;nearest=vehicle;}}
            ai.MissionManaged=true;ai.SetTransportOrder(nearest,enter);
        }
    }
    public void Depart()
    {
        orchestrator.enabled=false;
        foreach(var actor in actors)if(actor && actor.GetComponent<VehicleOccupant>().IsMounted)
        {var ai=actor.GetComponent<VehicleAI>();ai.SetMissionGoal(Vector3.zero,true);ai.HoldDeparture=false;}
    }
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12,12,470,Screen.height-24),GUI.skin.box);
        GUILayout.Label(accuracyTest?"INFANTRY FOCUS / ACCURACY":"COMBINED ARMS ORCHESTRATOR");
        GUILayout.Label(status);GUILayout.Label("RMB + WASD / Q,E free camera. Runtime errors: "+errors);
        if(GUILayout.Button("Start / restart scenario"))StartScenario();
        if(GUILayout.Button("Clear actors and vehicles"))ResetScenario();
        if(!accuracyTest)
        {
            if(GUILayout.Button(orchestrator.enabled?"Pause orchestrator orders":"Resume orchestrator")){orchestrator.enabled=!orchestrator.enabled;orchestrator.Replan();}
            GUILayout.BeginHorizontal();if(GUILayout.Button("Board nearest"))SetOrder(true);if(GUILayout.Button("Dismount"))SetOrder(false);if(GUILayout.Button("Depart"))Depart();GUILayout.EndHorizontal();
        }
        else
        {
            if(GUILayout.Button(holdInfantry?"Allow tactical movement":"Hold position (keep aiming)"))
            {holdInfantry=!holdInfantry;foreach(var actor in actors)actor.GetComponent<AIInput>().HoldPosition=holdInfantry;}
            if(GUILayout.Button("Restore health"))foreach(var actor in actors)if(!actor.health.IsDead)actor.health.ResetHealth();
        }
        if(GUILayout.Button("Free camera"))observer.follow=null;
        scroll=GUILayout.BeginScrollView(scroll);
        foreach(var actor in actors)if(actor)
        {
            var ai=actor.GetComponent<AIInput>();var rider=actor.GetComponent<VehicleOccupant>();
            string line=actor.name+" | "+(actor.health.IsDead?"DEAD":rider.IsMounted?rider.MountedVehicle.displayName:ai.State.ToString());
            if(accuracyTest)line+="\nTarget: "+(ai.Target?ai.Target.name:"none")+" / focus "+ai.FocusSeconds.ToString("F2")+"s / error "+ai.CurrentAimError.ToString("F2")+" deg";
            if(GUILayout.Button(line))observer.follow=actor.transform;
        }
        if(!accuracyTest)foreach(var order in orchestrator.Orders)GUILayout.Label(order.actor.name+" / "+order.role+" / "+(order.objective?order.objective.letter:"-")+" / "+order.status);
        GUILayout.EndScrollView();GUILayout.EndArea();
    }
}
