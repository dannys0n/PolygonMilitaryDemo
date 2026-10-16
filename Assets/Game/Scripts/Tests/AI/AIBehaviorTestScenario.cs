using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Small repeatable scenarios. UI and unit tests invoke the exact same public commands.
public sealed class AIBehaviorTestScenario : MonoBehaviour
{
    public enum Scenario { PlaneCargoExit, TransportOverflow, InfantryCombat, VehicleCombat }
    public Scenario scenario;
    public ArenaMatch match;
    public Vehicle vehiclePrefab;
    public VehicleCatalog vehicleCatalog;
    public Vehicle[] VehicleOptions { get; private set; }=System.Array.Empty<Vehicle>();
    Vector2 vehicleScroll;
    int vehicleSelection, nextActorId;
    public bool EnterRequested { get; private set; }
    public Vector3 transportDestination=new Vector3(60,0,80);
    public Vehicle opposingVehiclePrefab;
    public Vehicle groundVehiclePrefab;
    public Vehicle Opponent { get; private set; }
    public bool CrewReady { get; private set; }
    public int ShotsFired { get; private set; }
    public int VehicleDamageHits { get; private set; }
    public int AircraftGroundHits { get; private set; }
    public GameObject actorPrefab;
    public BehaviorTestCamera observer;
    public int cargoCount=6;
    public Vehicle Vehicle { get; private set; }
    public readonly List<ArenaParticipant> Actors=new List<ArenaParticipant>();
    public bool Running { get; private set; }
    public int ErrorCount { get; private set; }
    public int CompletedCount {get{int count=0;foreach(var actor in Actors){var task=actor.GetComponent<TransportTaskInput>();if(task&&task.Completed)count++;}return count;}}
    readonly HashSet<ArenaParticipant> pendingPlayers=new HashSet<ArenaParticipant>();
    GameObject population;
    VehicleOccupant pilot;
    bool flight;
    float altitude;
    string status="Reset, then trigger one behavior. RMB + WASD / Q,E to inspect.";
    void OnEnable(){Application.logMessageReceived+=Log;CombatEvents.DamageConfirmed+=CountVehicleHit;}
    void OnDisable(){Application.logMessageReceived-=Log;CombatEvents.DamageConfirmed-=CountVehicleHit;}
    void CountVehicleHit(CombatEvents.ConfirmedDamage hit)
    {
        if(hit.attacker && hit.attacker.GetComponentInParent<ArenaParticipant>()
            && (Vehicle && hit.target==Vehicle.gameObject || Opponent && hit.target==Opponent.gameObject))
        {
            VehicleDamageHits++;
            var rider=hit.attacker.GetComponent<VehicleOccupant>();
            var victim=hit.target.GetComponent<Vehicle>();
            if(rider && rider.IsMounted && rider.MountedVehicle.IsAircraft && victim && !victim.IsAircraft)AircraftGroundHits++;
        }
    }
    void Log(string message,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error){ErrorCount++;status=message;}}
    void Start()
    {
        match.enabled=false;
        if(vehicleCatalog)VehicleOptions=vehicleCatalog.transports.Concat(vehicleCatalog.combatVehicles).Where(v=>v).Distinct().OrderBy(v=>v.displayName).ThenBy(v=>v.name).ToArray();
        vehicleSelection=System.Array.IndexOf(VehicleOptions,vehiclePrefab);
        ResetScenario();
    }
    public void SelectVehicle(int index)
    {
        if(index<0||index>=VehicleOptions.Length)return;
        vehicleSelection=index;vehiclePrefab=VehicleOptions[index];ResetScenario();
        status="Selected "+Vehicle.displayName+". Add players, then toggle group entry / exit.";
    }
    public void StartTransportTrip()
    {
        if(!Vehicle||!Vehicle.Driver){status="Fill functional seats first.";return;}
        foreach(var actor in Actors)if(actor.GetComponent<VehicleOccupant>().IsMounted)
        {
            var infantry=actor.GetComponent<ArenaAIInput>();infantry.enabled=true;actor.GetComponent<SoldierController>().inputSource=infantry;
            var ai=actor.GetComponent<ArenaVehicleAI>();ai.enabled=true;ai.SetTravelGoal(transportDestination);
        }
        Running=true;status="Production vehicle AI driving/flying to the test destination. Passenger exits remain available.";
    }
    Vector3 DoorOutward(VehicleDoors.Door door)
    {
        var direction=Vector3.ProjectOnPlane(door.interaction.position-Vehicle.transform.TransformPoint(Vehicle.NavigationBounds.center),Vector3.up);
        return direction.sqrMagnitude>.01f?direction.normalized:-Vehicle.transform.forward;
    }
    ArenaParticipant Spawn(Vector3 position,int index,ArenaParticipant.Team team=ArenaParticipant.Team.Red)
    {
        var staging=new GameObject("Actor initialization");staging.transform.SetParent(population.transform,false);staging.SetActive(false);
        var g=Instantiate(actorPrefab,staging.transform);g.name="Test actor "+index;
        var actor=g.GetComponent<ArenaParticipant>();actor.match=match;actor.team=team;
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
        flight=false;Running=false;Unloading=false;EnterRequested=false;nextActorId=0;CrewReady=false;ShotsFired=VehicleDamageHits=AircraftGroundHits=0;pilot=null;observer.follow=null;
        // Mounted actors are parented to seat anchors, not the population root.
        foreach(var actor in Actors)if(actor){var rider=actor.GetComponent<VehicleOccupant>();if(rider.IsMounted)rider.Leave(true);actor.gameObject.SetActive(false);Destroy(actor.gameObject);}
        if(population){population.SetActive(false);Destroy(population);}if(Vehicle)Destroy(Vehicle.gameObject);if(Opponent)Destroy(Opponent.gameObject);Opponent=null;
        Actors.Clear();pendingPlayers.Clear();population=new GameObject("Scenario actors");population.SetActive(false);
        if(vehiclePrefab)Vehicle=Instantiate(vehiclePrefab,Vector3.up*.2f,Quaternion.identity);
        ErrorCount=0;status="Ready. Each command can be reset and repeated.";
    }
    public void AddPlayer()=>StartCoroutine(AddDynamicPlayer());
    IEnumerator AddDynamicPlayer()
    {
        if(!Vehicle)yield break;
        int id=nextActorId++;
        Vector3 position=Vehicle.transform.position+Vehicle.transform.right*(Vehicle.NavigationBounds.extents.x+5)+Vehicle.transform.forward*((id%8-3.5f)*2.4f);
        var actor=Spawn(position,id);pendingPlayers.Add(actor);population.SetActive(true);yield return null;
        if(!actor||!Actors.Contains(actor))yield break;
        if(!match.IsRunning)match.BeginMatch();
        if(!ArenaSpawnSelection.TryFindPosition(match,actor,position,out var validPosition))
        {RemovePlayer(actor);status="No valid nearby spawn position; remove a player or select another vehicle.";yield break;}
        actor.SelectClass(match.settings.classes[0]);actor.Respawn(validPosition,Vehicle.transform.rotation);
        actor.GetComponent<ArenaAIInput>().enabled=true;actor.GetComponent<ArenaVehicleAI>().enabled=true;
        pendingPlayers.Remove(actor);ApplyTransportOrder(actor);Running=true;
    }
    public void RemovePlayer(){if(Actors.Count>0)RemovePlayer(Actors[Actors.Count-1]);}
    public void RemovePlayerAt(int index){if(index>=0&&index<Actors.Count)RemovePlayer(Actors[index]);}
    void RemovePlayer(ArenaParticipant actor)
    {
        var ai=actor.GetComponent<ArenaVehicleAI>();ai.SetTransportOrder(null,false);
        var rider=actor.GetComponent<VehicleOccupant>();if(rider.IsMounted)rider.Leave(true);
        if(observer.follow==actor.transform)observer.follow=null;
        pendingPlayers.Remove(actor);Actors.Remove(actor);actor.gameObject.SetActive(false);Destroy(actor.gameObject);
    }
    public void EnterAll()=>SetAllTransportState(true);
    public void ExitAll()=>SetAllTransportState(false);
    public void SetAllTransportState(bool enter)
    {
        EnterRequested=enter;
        foreach(var actor in Actors)ApplyTransportOrder(actor);
        status=EnterRequested?"Enter requested. AI finds/reserves seats and approaches; excess players wait outside for a free seat.":"Exit requested. AI disembarks through normal exit checks and clears the vehicle.";
    }
    void ApplyTransportOrder(ArenaParticipant actor)
    {
        if(!actor||pendingPlayers.Contains(actor))return;
        actor.GetComponent<ArenaVehicleAI>().SetTransportOrder(Vehicle,EnterRequested);
        actor.GetComponent<SoldierController>().inputSource=actor.GetComponent<ArenaAIInput>();
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
        pilot=Vehicle.Driver;status="All functional seats occupied. Extra AI wait for a free seat.";
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
        if(!Vehicle||scenario!=Scenario.PlaneCargoExit)return;
        if(!flight){Vehicle.Body.isKinematic=false;Vehicle.Body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;Vehicle.enabled=true;Vehicle.Body.linearVelocity=Vehicle.transform.forward*85;foreach(var actor in Actors)if(!actor.GetComponent<VehicleOccupant>().IsMounted)actor.player.controller.body.linearVelocity=Vehicle.Body.GetPointVelocity(actor.transform.position);flight=true;}
        var doors=Vehicle.GetComponent<VehicleDoors>();if(!doors||doors.doors.Length==0){status="This vehicle has no physical cargo door; use passenger exits.";return;}if(!doors.doors[0].open)doors.ToggleAll();
        var exit=Vehicle.transform.InverseTransformPoint(doors.doors[0].interaction.position+DoorOutward(doors.doors[0])*3)+Vector3.up;
        foreach(var actor in Actors)if(!actor.GetComponent<VehicleOccupant>().IsMounted)
        {var goal=exit;if(scenario==Scenario.PlaneCargoExit)goal.x=Mathf.Clamp(Vehicle.transform.InverseTransformPoint(actor.transform.position).x,-.7f,.7f);actor.GetComponent<TransportTaskInput>().Command(TransportTaskInput.Task.ExitCargo,Vehicle,goal);}
        Running=true;status="Cargo exit active: normal movement, jump and landing detection.";
    }
    public bool Unloading { get; private set; }
    public void UnloadPassengers()=>StartCoroutine(Unload());
    IEnumerator Unload()
    {
        Unloading=true;float deadline=Time.time+20;
        foreach(var actor in Actors)
        {
            var occupant=actor.GetComponent<VehicleOccupant>();
            if(!occupant.IsMounted||Vehicle.seats[occupant.SeatIndex].role!=Vehicle.SeatRole.Passenger)continue;
            var infantry=actor.GetComponent<ArenaAIInput>();infantry.enabled=true;
            actor.GetComponent<SoldierController>().inputSource=infantry;
            var ai=actor.GetComponent<ArenaVehicleAI>();ai.enabled=true;ai.SetTransportOrder(Vehicle,false);
            while(occupant.IsMounted&&Time.time<deadline)yield return new WaitForSeconds(.2f);
        }
        Unloading=false;status="Passenger exits finished; driver and gunner retained.";
    }
    public void StartCombat()=>StartCoroutine(Combat());
    IEnumerator Combat()
    {
        ResetWorld();for(int i=0;i<4;i++)Spawn(new Vector3(i%2==0?-10:10,.2f,(i/2)*5),i);
        population.SetActive(true);yield return null;match.BeginMatch();
        for(int i=0;i<Actors.Count;i++){var a=Actors[i];a.team=i%2==0?ArenaParticipant.Team.Red:ArenaParticipant.Team.Blue;a.SelectClass(match.settings.classes[0]);a.Respawn(new Vector3(i%2==0?-10:10,.2f,(i/2)*5),Quaternion.Euler(0,i%2==0?90:-90,0));var ai=a.GetComponent<ArenaAIInput>();ai.enabled=true;a.GetComponent<SoldierController>().inputSource=ai;}
        Running=true;status="Two versus two: production perception, bursts, damage, death and ragdolls.";
    }
    public void StartVehicleCombat()=>StartCoroutine(VehicleCombat());
    public void StartAirGroundCombat()=>StartCoroutine(VehicleCombat(true));
    void CountShot(VehicleOccupant shooter,Vector3 origin)=>ShotsFired++;
    IEnumerator VehicleCombat(bool groundTarget=false)
    {
        ResetWorld();yield return null; // Finish disposing the previous crew before rebuilding.
        if(!Vehicle||!opposingVehiclePrefab){status="Missing combat vehicle prefab.";yield break;}
        var opponentPrefab=groundTarget?groundVehiclePrefab:opposingVehiclePrefab;
        if(!opponentPrefab){status="Missing ground target prefab.";yield break;}
        Opponent=Instantiate(opponentPrefab);
        var vehicles=new[]{Vehicle,Opponent};
        bool aircraft=Vehicle.IsAircraft;
        float separation=Vehicle.motor is JetMotor?140:aircraft?55:45;
        float height=Vehicle.motor is JetMotor?100:aircraft?25:.6f;
        for(int side=0;side<2;side++)
        {
            var vehicle=vehicles[side];
            Vector3 position=groundTarget?(side==0?new Vector3(0,Vehicle.motor is JetMotor?65:30,Vehicle.motor is JetMotor?-210:-80):new Vector3(0,.6f,0))
                :new Vector3(0,height,side==0?-separation:separation);
            vehicle.transform.SetPositionAndRotation(position,Quaternion.Euler(0,side==0?0:180,0));
            vehicle.enabled=false;vehicle.Body.collisionDetectionMode=CollisionDetectionMode.Discrete;vehicle.Body.isKinematic=true;
            foreach(var gun in vehicle.GetComponentsInChildren<VehicleTurret>(true))gun.ShotFired+=CountShot;
            foreach(var launcher in vehicle.GetComponentsInChildren<VehicleLauncher>(true))launcher.ShotFired+=CountShot;
            for(int seat=0;seat<vehicle.seats.Length;seat++)
                Spawn(vehicle.transform.position+Vector3.right*(10+seat*2),Actors.Count,side==0?ArenaParticipant.Team.Red:ArenaParticipant.Team.Blue);
        }
        population.SetActive(true);yield return null;match.BeginMatch();
        int index=0;
        foreach(var vehicle in vehicles)
        {
            for(int seat=0;seat<vehicle.seats.Length;seat++)
            {
                var actor=Actors[index++];actor.Respawn(vehicle.transform.position+Vector3.right*10,vehicle.transform.rotation);
                var occupant=actor.GetComponent<VehicleOccupant>();occupant.faction=(int)actor.team;
                var ai=actor.GetComponent<ArenaAIInput>();ai.enabled=true;actor.GetComponent<SoldierController>().inputSource=ai;
                // A controlled initial condition, not a boarding test: the complete crew starts seated.
                vehicle.seats[seat].occupant=occupant;occupant.Mount(vehicle,seat);
                actor.GetComponent<ArenaVehicleAI>().enabled=true;
                if(groundTarget)actor.GetComponent<ArenaVehicleAI>().SetMissionGoal(Vector3.zero,true);
            }
            vehicle.Body.isKinematic=false;vehicle.Body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;vehicle.enabled=true;
            if(vehicle.motor is JetMotor jet){jet.startingThrottle=.8f;vehicle.Body.linearVelocity=vehicle.transform.forward*jet.maxSpeed*.7f;}
        }
        CrewReady=true;Running=true;observer.follow=Vehicle.transform;
        status="Opposing full crews: production targeting, steering, firing and damage. Reset to repeat.";
    }
    void VehicleTelemetry(Vehicle vehicle,string team)
    {
        if(!vehicle)return;
        int occupied=0;foreach(var seat in vehicle.seats)if(seat.occupant)occupied++;
        GUILayout.Label(team+" "+vehicle.displayName+" | seats "+occupied+"/"+vehicle.seats.Length+" | health "+vehicle.Health.ToString("F0")+" | speed "+vehicle.Body.linearVelocity.magnitude.ToString("F0")+" m/s");
        if(vehicle.Driver && vehicle.Driver.TryGetComponent<ArenaVehicleAI>(out var ai))
            GUILayout.Label(ai.Maneuver+" / extensions "+ai.ExtensionCount+" / target "+(ai.CombatTarget?ai.CombatTarget.name:"none")
                +(Opponent && Vehicle?" / separation "+Vector3.Distance(Vehicle.Body.worldCenterOfMass,Opponent.Body.worldCenterOfMass).ToString("F0")+" m":""));
        if(GUILayout.Button("Follow "+team+" vehicle"))observer.follow=vehicle.transform;
    }
    // Reproducible starting poses only; all subsequent motion comes from production AI/motors.
    public void SetAircraftClosePass()
    {
        if(!CrewReady || !Vehicle.IsAircraft || !Opponent)return;
        bool jet=Vehicle.motor is JetMotor;
        for(int i=0;i<2;i++)
        {
            var v=i==0?Vehicle:Opponent;
            Vector3 position=jet?new Vector3(i*35,110,i*60):new Vector3(0,45,i==0?-40:40);
            Quaternion rotation=Quaternion.Euler(0,jet?0:i==0?0:180,0);
            v.Body.position=position;v.Body.rotation=rotation;
            v.transform.SetPositionAndRotation(position,rotation);
            v.Body.linearVelocity=rotation*Vector3.forward*(jet?((JetMotor)v.motor).maxSpeed*.75f:8);
            v.Body.angularVelocity=Vector3.zero;
            if(v.Driver.TryGetComponent<ArenaVehicleAI>(out var ai))ai.SetTravelGoal(new Vector3(0,0,150));
        }
        Physics.SyncTransforms();status=jet?"Turning-chase setup. Watch pursuit / extension / reapproach.":"Closing-pass setup. Watch braking, stand-off and separation.";
    }
    public void AttackAndDismountPassengers()
    {
        if(!CrewReady || !Vehicle || !Opponent || !Opponent.Driver)return;
        Vehicle.ApplyBulletDamage(1,Opponent.Driver.gameObject);
        UnloadPassengers();status="Vehicle hit reports attacker position; dismounted infantry must orient/search, then fire with LOS.";
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
        else if(scenario==Scenario.TransportOverflow)
        {
            GUILayout.Label(Vehicle?Vehicle.displayName:"No vehicle");
            if(GUILayout.Button("Spawn a player"))AddPlayer();
            if(GUILayout.Button("Remove last player"))RemovePlayer();
            GUILayout.Label("Group order: "+(EnterRequested?"ENTER / stay seated":"EXIT / stay outside"));
            GUILayout.BeginHorizontal();if(GUILayout.Button("ENTER ALL"))EnterAll();if(GUILayout.Button("EXIT ALL"))ExitAll();GUILayout.EndHorizontal();
            int seated=Actors.Count(a=>a.GetComponent<VehicleOccupant>().IsMounted);
            GUILayout.Label("Seated: "+seated+" / waiting or outside: "+(Actors.Count-seated));
            if(GUILayout.Button("Spawn up to seats + 2 players"))for(int i=Actors.Count;i<Vehicle.seats.Length+2;i++)AddPlayer();
        }
        else if(scenario==Scenario.VehicleCombat)
        {
            if(GUILayout.Button("Start / restart full-crew engagement"))StartVehicleCombat();
            if(vehiclePrefab.IsAircraft && GUILayout.Button("Aircraft vs ground armor"))StartAirGroundCombat();
            if(CrewReady && Vehicle.IsAircraft && GUILayout.Button("Reproduce turning chase / closing pass"))SetAircraftClosePass();
            if(CrewReady && !Vehicle.IsAircraft && GUILayout.Button("Attack vehicle + dismount passengers"))AttackAndDismountPassengers();
            GUILayout.Label("Weapon shots: "+ShotsFired+" / damaging hits: "+VehicleDamageHits+" / air-ground hits: "+AircraftGroundHits);VehicleTelemetry(Vehicle,"Red");VehicleTelemetry(Opponent,"Blue");
        }
        else if(GUILayout.Button("Start 2 vs 2 infantry combat"))StartCombat();
        if(GUILayout.Button("Free camera"))observer.follow=null;
        foreach(var actor in Actors.ToArray())
        {
            var rider=actor.GetComponent<VehicleOccupant>();
            string label=scenario==Scenario.TransportOverflow
                ?actor.name+" / "+(rider.IsMounted?rider.MountedVehicle.seats[rider.SeatIndex].name:EnterRequested?actor.GetComponent<ArenaVehicleAI>().BoardingStatus:"outside")
                :actor.name+" / "+(actor.health.IsDead?"dead":actor.health.CurrentHealth.ToString("F0"));
            GUILayout.BeginHorizontal();if(GUILayout.Button(label))observer.follow=actor.transform;
            if(scenario==Scenario.TransportOverflow&&GUILayout.Button("Remove",GUILayout.Width(65)))RemovePlayer(actor);GUILayout.EndHorizontal();
        }
        GUILayout.EndArea();
        if(scenario==Scenario.TransportOverflow&&VehicleOptions.Length>0)
        {
            GUILayout.BeginArea(new Rect(414,12,290,Mathf.Min(560,Screen.height-24)),GUI.skin.box);
            GUILayout.Label("Select vehicle ("+VehicleOptions.Length+")");
            vehicleScroll=GUILayout.BeginScrollView(vehicleScroll);
            for(int i=0;i<VehicleOptions.Length;i++)
                if(GUILayout.Button((i==vehicleSelection?"> ":"")+VehicleOptions[i].displayName))SelectVehicle(i);
            GUILayout.EndScrollView();GUILayout.EndArea();
        }
    }
}
