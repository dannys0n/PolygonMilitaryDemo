using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Small repeatable scenarios. UI and unit tests invoke the exact same public commands.
public sealed class AIBehaviorTestScenario : MonoBehaviour
{
    public enum Scenario { PlaneCargoExit, TransportOverflow, InfantryCombat }
    public Scenario scenario;
    public ArenaMatch match;
    public Vehicle vehiclePrefab;
    public GameObject actorPrefab;
    public BehaviorTestCamera observer;
    public int cargoCount=6;
    public Vehicle Vehicle { get; private set; }
    public readonly List<ArenaParticipant> Actors=new List<ArenaParticipant>();
    public bool Running { get; private set; }
    public int ErrorCount { get; private set; }
    public int CompletedCount {get{int count=0;foreach(var actor in Actors){var task=actor.GetComponent<TransportTaskInput>();if(task&&task.Completed)count++;}return count;}}
    GameObject population;
    VehicleOccupant pilot;
    bool flight;
    float altitude;
    string status="Reset, then trigger one behavior. RMB + WASD / Q,E to inspect.";
    void OnEnable()=>Application.logMessageReceived+=Log;
    void OnDisable()=>Application.logMessageReceived-=Log;
    void Log(string message,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error){ErrorCount++;status=message;}}
    void Start(){match.enabled=false;ResetScenario();}
    ArenaParticipant Spawn(Vector3 position,int index)
    {
        var staging=new GameObject("Actor initialization");staging.transform.SetParent(population.transform,false);staging.SetActive(false);
        var g=Instantiate(actorPrefab,staging.transform);g.name="Test actor "+index;
        var actor=g.GetComponent<ArenaParticipant>();actor.match=match;actor.team=ArenaParticipant.Team.Red;
        var nav=g.GetComponent<UnityEngine.AI.NavMeshAgent>();if(nav)nav.enabled=false;
        var ai=g.GetComponent<ArenaAIInput>();if(ai)ai.enabled=false;
        var vehicleAI=g.GetComponent<ArenaVehicleAI>();if(vehicleAI)vehicleAI.enabled=false;
        var view=g.GetComponent<SoldierFirstPersonView>();view.firstPerson=false;view.playerCamera.enabled=false;
        var source=g.AddComponent<TransportTaskInput>();g.GetComponent<SoldierController>().inputSource=source;
        g.transform.position=position;Actors.Add(actor);staging.SetActive(true);return actor;
    }
    public void ResetScenario(){StopAllCoroutines();ResetWorld();}
    void ResetWorld()
    {
        flight=false;Running=false;Unloading=false;pilot=null;observer.follow=null;
        if(population){population.SetActive(false);Destroy(population);}if(Vehicle)Destroy(Vehicle.gameObject);
        Actors.Clear();population=new GameObject("Scenario actors");population.SetActive(false);
        if(vehiclePrefab)Vehicle=Instantiate(vehiclePrefab,Vector3.up*.2f,Quaternion.identity);
        ErrorCount=0;status="Ready. Each command can be reset and repeated.";
    }
    public void FillSeats()=>StartCoroutine(Fill());
    IEnumerator Fill()
    {
        if(Actors.Count>0){status="Reset before filling again.";yield break;}
        for(int i=0;i<Vehicle.seats.Length;i++)Spawn(new Vector3(8,1,i*2),i);
        population.SetActive(true);yield return null;match.BeginMatch();
        for(int i=0;i<Actors.Count;i++)
        {
            Actors[i].Respawn(Vehicle.transform.TransformPoint(Vehicle.EntryBounds.center),Quaternion.identity);
            var occupant=Actors[i].GetComponent<VehicleOccupant>();occupant.faction=0;
            if(!Vehicle.Board(occupant,i)){status="Seat "+i+" cannot be boarded yet; reset after vehicle settles.";yield break;}
        }
        pilot=Vehicle.Driver;status="All functional seats occupied. Add two physical riders to test overflow.";
    }
    public void AddOverflowRiders()=>StartCoroutine(Overflow());
    IEnumerator Overflow()
    {
        if(!Vehicle||!Vehicle.IsOccupied){status="Fill seats first.";yield break;}
        var doors=Vehicle.GetComponent<VehicleDoors>();if(!doors||doors.doors.Length==0){status="Vehicle has no cargo access.";yield break;}
        var door=doors.doors[0];if(!door.open)doors.Toggle(0);
        int start=Actors.Count;
        Vector3 entry=door.interaction.position;
        for(int i=0;i<2;i++)Spawn(entry+new Vector3(i==0?-1:1,0,-2),start+i);
        population.SetActive(true);yield return null;
        for(int i=start;i<Actors.Count;i++)
        {
            Actors[i].Respawn(entry+new Vector3(i==start?-.7f:.7f,0,-2),Quaternion.identity);
            var local=Vehicle.transform.InverseTransformPoint(entry);Vector3 inward=Vector3.ProjectOnPlane(Vehicle.NavigationBounds.center-local,Vector3.up).normalized;
            Actors[i].GetComponent<TransportTaskInput>().Command(TransportTaskInput.Task.BoardCargo,Vehicle,local+inward*1.6f+Vector3.right*(i==start?-.45f:.45f));
        }
        Running=true;status="Two overflow riders walking/jumping in. Functional seats remain full.";
    }
    public void LoadPlaneCargo()=>StartCoroutine(LoadCargo());
    IEnumerator LoadCargo()
    {
        ResetWorld();altitude=35;Vehicle.transform.position=Vector3.up*altitude;
        Vehicle.enabled=false;Vehicle.Body.collisionDetectionMode=CollisionDetectionMode.Discrete;Vehicle.Body.isKinematic=true;
        var jet=Vehicle.motor as JetMotor;if(jet)jet.startingThrottle=.7f;
        var driver=Spawn(Vehicle.transform.position+Vector3.up*3,-1);
        for(int i=0;i<cargoCount;i++)Spawn(Vehicle.transform.TransformPoint(new Vector3(i%2==0?-.7f:.7f,2.1f,-9+i/2f*2)),i);
        population.SetActive(true);yield return null;match.BeginMatch();
        pilot=driver.GetComponent<VehicleOccupant>();Vehicle.seats[0].occupant=pilot;pilot.Mount(Vehicle,0);
        for(int i=1;i<Actors.Count;i++){var position=Vehicle.transform.TransformPoint(new Vector3(i%2==0?-.7f:.7f,2.1f,-9+(i-1)/2f*2));Actors[i].Respawn(position,Vehicle.transform.rotation);Actors[i].player.controller.body.linearVelocity=Vector3.zero;}
        flight=false;
        var doors=Vehicle.GetComponent<VehicleDoors>();if(doors&&!doors.doors[0].open)doors.ToggleAll();
        status="Cargo loaded and held for inspection. Start flight and exit when ready.";
    }
    public void ExitCargo()
    {
        if(!Vehicle)return;
        if(scenario==Scenario.PlaneCargoExit&&!flight){Vehicle.Body.isKinematic=false;Vehicle.Body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;Vehicle.enabled=true;Vehicle.Body.linearVelocity=Vehicle.transform.forward*85;foreach(var actor in Actors)if(!actor.GetComponent<VehicleOccupant>().IsMounted)actor.player.controller.body.linearVelocity=Vehicle.Body.GetPointVelocity(actor.transform.position);flight=true;}
        var doors=Vehicle.GetComponent<VehicleDoors>();if(doors&&!doors.doors[0].open)doors.ToggleAll();
        var exit=Vehicle.transform.InverseTransformPoint(doors.doors[0].interaction.position)+new Vector3(0,1,-3);
        foreach(var actor in Actors)if(!actor.GetComponent<VehicleOccupant>().IsMounted)
        {var goal=exit;goal.x=Mathf.Clamp(Vehicle.transform.InverseTransformPoint(actor.transform.position).x,-.7f,.7f);actor.GetComponent<TransportTaskInput>().Command(TransportTaskInput.Task.ExitCargo,Vehicle,goal);}
        Running=true;status="Cargo exit active: normal movement, jump and landing detection.";
    }
    public bool Unloading { get; private set; }
    public void UnloadPassengers()=>StartCoroutine(Unload());
    IEnumerator Unload()
    {
        Unloading=true;float deadline=Time.time+20;int index=0;
        foreach(var actor in Actors)
        {
            var occupant=actor.GetComponent<VehicleOccupant>();
            if(!occupant.IsMounted||Vehicle.seats[occupant.SeatIndex].role!=Vehicle.SeatRole.Passenger)continue;
            while(occupant.IsMounted&&Time.time<deadline)
            {
                if(occupant.Leave(false)){actor.GetComponent<TransportTaskInput>().MoveTo(Vehicle.transform.position+Vehicle.transform.right*(8+index%2*2)+Vehicle.transform.forward*((index/2)*3-6));index++;}
                yield return new WaitForSeconds(.2f);
            }
        }
        Unloading=false;status="Passenger exits finished; driver and gunner retained.";
    }
    public void StartCombat()=>StartCoroutine(Combat());
    IEnumerator Combat()
    {
        ResetWorld();for(int i=0;i<4;i++)Spawn(new Vector3(i%2==0?-10:10,.2f,(i/2)*5),i);
        population.SetActive(true);yield return null;match.BeginMatch();
        for(int i=0;i<Actors.Count;i++){var a=Actors[i];a.team=i%2==0?ArenaParticipant.Team.Red:ArenaParticipant.Team.Blue;a.Respawn(new Vector3(i%2==0?-10:10,.2f,(i/2)*5),Quaternion.Euler(0,i%2==0?90:-90,0));var ai=a.GetComponent<ArenaAIInput>();ai.enabled=true;a.GetComponent<SoldierController>().inputSource=ai;}
        Running=true;status="Two versus two: production perception, bursts, damage, death and ragdolls.";
    }
    void FixedUpdate()
    {
        if(!flight||!Vehicle||Vehicle.IsDestroyed)return;
        var jet=(JetMotor)Vehicle.motor;var body=Vehicle.Body;
        float q=.5f*jet.airDensity*Mathf.Max(25,body.linearVelocity.sqrMagnitude);
        float trim=Mathf.Clamp((body.mass*Physics.gravity.magnitude/(q*jet.wingArea)-jet.liftBias)/jet.liftSlope*Mathf.Rad2Deg-jet.wingIncidence,0,8);
        float climb=Mathf.Clamp((altitude-body.position.y)*.02f-body.linearVelocity.y*.08f,-.2f,.2f);
        Vehicle.DriverInput=new VehicleInput{directionalFlight=true,heading=0,viewPitch=Mathf.Clamp(-Mathf.Atan(climb)*Mathf.Rad2Deg-trim,-16,12),move=new Vector2(0,Mathf.Clamp((85-jet.Airspeed)*.12f,-1,1))};
    }
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12,12,390,Screen.height-24),GUI.skin.box);GUILayout.Label(scenario.ToString());GUILayout.Label(status);GUILayout.Label("Actors: "+Actors.Count+" / completed: "+CompletedCount+" / runtime errors: "+ErrorCount);
        if(GUILayout.Button("Reset scenario"))ResetScenario();
        if(scenario==Scenario.PlaneCargoExit){if(GUILayout.Button("Load plane cargo (hold setup)"))LoadPlaneCargo();if(GUILayout.Button("Start flight and exit cargo"))ExitCargo();}
        else if(scenario==Scenario.TransportOverflow){if(GUILayout.Button("Fill functional seats"))FillSeats();if(GUILayout.Button("Add two physical overflow riders"))AddOverflowRiders();if(GUILayout.Button("Unload passengers (keep driver/gunner)"))UnloadPassengers();if(GUILayout.Button("Exit physical cargo"))ExitCargo();}
        else if(GUILayout.Button("Start 2 vs 2 infantry combat"))StartCombat();
        if(GUILayout.Button("Free camera"))observer.follow=null;
        foreach(var actor in Actors)if(GUILayout.Button(actor.name+" / "+(actor.health.IsDead?"dead":actor.health.CurrentHealth.ToString("F0"))))observer.follow=actor.transform;
        GUILayout.EndArea();
    }
}
