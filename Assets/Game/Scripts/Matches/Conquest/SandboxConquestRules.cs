using UnityEngine;

// Three independent infantry encounters plus two small air-only battles. No transport orchestration.
public sealed class SandboxConquestRules : ConquestRules
{
    [System.Serializable] public sealed class Flight
    {
        public MatchParticipant.Team team;
        public SandboxAirCombatAI.Battle battle;
        public Vehicle prefab;
        public Transform openingBase;
        public MatchParticipant[] crew;
        [System.NonSerialized] public Vehicle current;
        [System.NonSerialized] public int respawns;
        [System.NonSerialized] public bool respawning;
        [System.NonSerialized] public double respawnAt;
    }
    public SandboxInfantryZone[] infantryZones=System.Array.Empty<SandboxInfantryZone>();
    public Flight[] flights=System.Array.Empty<Flight>();
    public Transform airCombatCenter;
    [Min(0)] public float vehicleRespawnDelay=3;
    [Header("Player HQ deployment")]
    public Vehicle[] deploymentVehicles=System.Array.Empty<Vehicle>();
    public Vehicle SelectedVehicle { get; private set; }
    public Vehicle PlayerVehicle { get; private set; }
    public void SelectVehicle(Vehicle prefab)
    {
        if(System.Array.IndexOf(deploymentVehicles,prefab)>=0)SelectedVehicle=prefab;
    }
    [Min(.1f)] public float scoreInterval=1;
    int redScore,blueScore;
    float scoreClock;
    bool spawningFlight;
    public override string DisplayName=>"SANDBOX";
    public override bool HasEnded=>false;
    public override bool UsesMatchTimer=>false;
    public override bool UsesCustomSpawning=>infantryZones.Length>0;
    public override string ScoreDescription=>"CONTROL SCORE / NO LIMIT";
    public override int ScoreLimit=>Mathf.Max(100,Mathf.Max(redScore,blueScore));
    public override int TeamScore(MatchParticipant.Team team)=>team==MatchParticipant.Team.Red?redScore:blueScore;
    public override bool NeedsDeployment(MatchParticipant actor)=>actor && actor.IsHumanControlled;
    public override bool CanDeploy(MatchParticipant actor,ConquestCapturePoint point)=>!point || System.Array.Exists(infantryZones,z=>z && z.point==point);
    public override void Begin(MatchController match)
    {
        RetirePlayerVehicle();
        SelectedVehicle=deploymentVehicles.Length>0?deploymentVehicles[0]:null;
        foreach(var flight in flights)
        {
            if(flight.current){foreach(var actor in flight.crew){var rider=actor.GetComponent<VehicleOccupant>();if(rider.IsMounted)rider.Leave(true);}Destroy(flight.current.gameObject);flight.current=null;}
            flight.respawns=0;flight.respawning=false;flight.respawnAt=0;
        }
        base.Begin(match);redScore=blueScore=0;scoreClock=0;UpdateAirCenter();
    }
    Flight CrewFlight(MatchParticipant actor)
    {
        foreach(var f in flights)if(System.Array.IndexOf(f.crew,actor)>=0)return f;return null;
    }
    public override float RespawnDelay(MatchParticipant actor)=>CrewFlight(actor)!=null
        || NeedsDeployment(actor)&&PlayerVehicle&&PlayerVehicle.IsDestroyed?vehicleRespawnDelay:base.RespawnDelay(actor);
    public override bool CanDamage(MatchParticipant victim,MatchParticipant attacker)
    {
        if(!UsesCustomSpawning)return true;
        var first=CrewFlight(victim);var second=CrewFlight(attacker);
        if(first!=null||second!=null)
        {
            // A crew and its aircraft are one combat unit in this mode. Hull damage
            // destroys the vehicle, which then kills its occupants; cockpit hitboxes
            // must not bypass the hull with one infantry-damage cannon hit.
            return first!=null&&second!=null&&first.battle==second.battle
                && first.current&&first.current.IsDestroyed;
        }
        var a=victim.GetComponent<SandboxInfantryInput>();var b=attacker.GetComponent<SandboxInfantryInput>();
        return a&&b&&a.objective==b.objective;
    }
    public override bool TrySpawn(MatchParticipant actor)
    {
        if(actor.IsSpectator||actor.RespawnRemaining>0)return false;
        bool local=NeedsDeployment(actor);
        if(local && !DeploymentApproved(actor))return false;
        var flight=CrewFlight(actor);
        if(flight!=null)
        {
            var rider=actor.GetComponent<VehicleOccupant>();
            if(flight.current&&!flight.current.IsDestroyed&&rider.IsMounted)return true;
            if(flight.respawns>0 && !flight.respawning)QueueFlight(flight);
            if(!spawningFlight && (!flight.respawning || Time.timeAsDouble>=flight.respawnAt))SpawnFlight(flight);
            return rider.IsMounted;
        }
        if(local && !DeploymentPoint(actor))return SpawnPlayerVehicle(actor);
        var input=actor.GetComponent<SandboxInfantryInput>();if(!input)return false;
        if(local)input.Configure(DeploymentPoint(actor));
        foreach(var zone in infantryZones)if(zone.point==input.objective && zone.TryPosition(actor,out var p))
        {
            if(local)LeavePlayerVehicle(actor);
            Vector3 toward=zone.point.transform.position-p;toward.y=0;
            actor.Respawn(p,toward.sqrMagnitude>.01f?Quaternion.LookRotation(toward):Quaternion.identity);
            input.Configure(zone.point);
            if(local)CancelDeployment(actor);
            return !actor.IsAwaitingSpawn;
        }
        return false;
    }
    void LeavePlayerVehicle(MatchParticipant actor)
    {
        var rider=actor.GetComponent<VehicleOccupant>();if(rider && rider.IsMounted)rider.Leave(true);
        RetirePlayerVehicle();
    }
    void RetirePlayerVehicle()
    {
        if(!PlayerVehicle)return;
        foreach(var seat in PlayerVehicle.seats)if(seat.occupant)seat.occupant.Leave(true);
        foreach(var collider in PlayerVehicle.GetComponentsInChildren<Collider>())collider.enabled=false;
        Destroy(PlayerVehicle.gameObject);PlayerVehicle=null;
    }
    bool SpawnPlayerVehicle(MatchParticipant actor)
    {
        var bases=actor.team==MatchParticipant.Team.Red?redBaseSpawns:blueBaseSpawns;
        if(!SelectedVehicle || bases.Length==0 || SelectedVehicle.seats.Length==0)return false;
        var origin=bases[0];
        // Pads are authored on open ground. Lift by the chassis bounds so tall vehicles
        // and aircraft land on their wheels instead of intersecting the floor.
        var bounds=SelectedVehicle.NavigationBounds;
        Vector3 p=origin.position+Vector3.up*Mathf.Max(.5f,-bounds.min.y+.25f);
        LeavePlayerVehicle(actor);
        var v=PlayerVehicle=Instantiate(SelectedVehicle,p,origin.rotation);
        actor.Respawn(p+Vector3.up*2,origin.rotation);
        var rider=actor.GetComponent<VehicleOccupant>();rider.faction=(int)actor.team;
        v.seats[0].occupant=rider;rider.Mount(v,0);
        CancelDeployment(actor);Physics.SyncTransforms();return true;
    }
    public override void Tick(float dt)
    {
        UpdateAirCenter();
        foreach(var flight in flights)
        {
            if(flight.respawns>0 && !flight.respawning && (!flight.current || flight.current.IsDestroyed || System.Array.Exists(flight.crew,a=>!a||a.health.IsDead)))QueueFlight(flight);
            if(flight.respawning && Time.timeAsDouble>=flight.respawnAt)SpawnFlight(flight);
        }
        base.Tick(dt);
    }
    void QueueFlight(Flight flight)
    {
        if(flight.respawning)return;
        flight.respawning=true;flight.respawnAt=Time.timeAsDouble+vehicleRespawnDelay;
        if(flight.current)
        {
            var ai=flight.current.GetComponent<SandboxAirCombatAI>();if(ai)ai.enabled=false;
            if(!flight.current.IsDestroyed)flight.current.ApplyDamage(flight.current.maxHealth,null);
        }
        foreach(var actor in flight.crew)
            if(actor && !actor.health.IsDead)actor.health.ApplyDamage(actor.health.maxHealth,null);
    }
    void UpdateAirCenter()
    {
        if(!airCombatCenter||infantryZones.Length==0)return;
        Vector3 p=Vector3.zero;int n=0;foreach(var z in infantryZones)if(z&&z.point){p+=z.point.transform.position;n++;}
        if(n>0)airCombatCenter.position=p/n;
    }
    void SpawnFlight(Flight flight)
    {
        if(spawningFlight||!flight.prefab||!flight.openingBase)return;
        spawningFlight=true;
        try
        {
            if(flight.current)
            {
                if(!flight.current.IsDestroyed)flight.current.ApplyDamage(flight.current.maxHealth,null);
                // An immediate replacement must not spawn inside the old airborne wreck.
                var oldAI=flight.current.GetComponent<SandboxAirCombatAI>();if(oldAI)oldAI.enabled=false;
                foreach(var collider in flight.current.GetComponentsInChildren<Collider>())collider.enabled=false;
                Destroy(flight.current.gameObject);
            }
            var v=flight.current=Instantiate(flight.prefab,flight.openingBase.position,flight.openingBase.rotation);
            v.DamageFilter=source=>
            {
                var actor=source?source.GetComponentInParent<MatchParticipant>():null;
                if(!actor)return true;
                var attacker=CrewFlight(actor);
                return actor.IsHumanControlled?actor.team!=flight.team:
                    attacker!=null&&attacker.battle==flight.battle&&attacker.team!=flight.team;
            };
            flight.respawns++;
            flight.respawning=false;
            for(int i=0;i<flight.crew.Length;i++)
            {
                var actor=flight.crew[i];var rider=actor.GetComponent<VehicleOccupant>();
                if(rider.IsMounted)rider.Leave(true);
                actor.Respawn(v.transform.position+Vector3.up*2,v.transform.rotation);
                rider.faction=(int)flight.team;
                // Air deployment deliberately seats the crew directly; there is no ground boarding phase.
                v.seats[i].occupant=rider;rider.Mount(v,i);
            }
            if(v.motor is JetMotor jet)v.Body.linearVelocity=v.transform.forward*(jet.maxSpeed*.8f);
            v.gameObject.AddComponent<SandboxAirCombatAI>().Configure(v,flight.team,airCombatCenter,flight.battle,flight.battle==SandboxAirCombatAI.Battle.Jets?260:130);
        }
        finally{spawningFlight=false;}
    }
    protected override void TickScoring(float dt)
    {
        scoreClock+=Mathf.Max(0,dt);float interval=Mathf.Max(.1f,scoreInterval);int ticks=Mathf.FloorToInt(scoreClock/interval);
        if(ticks==0)return;scoreClock-=ticks*interval;
        foreach(var point in points)
        {
            if(point.Owner==MatchParticipant.Team.Red)redScore=(int)System.Math.Min(int.MaxValue,(long)redScore+ticks);
            else if(point.Owner==MatchParticipant.Team.Blue)blueScore=(int)System.Math.Min(int.MaxValue,(long)blueScore+ticks);
        }
    }
    public override bool TryGetObjective(AIInput ai,out Vector3 destination)=>base.TryGetObjective(ai,out destination);
    public override void OnSpawned(MatchParticipant actor,bool wasDead){}
    void OnDisable(){RetirePlayerVehicle();foreach(var f in flights)if(f.current)Destroy(f.current.gameObject);}
}
