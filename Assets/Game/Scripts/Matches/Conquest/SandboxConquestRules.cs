using UnityEngine;

// Three independent infantry encounters plus two small air-only battles. No transport orchestration.
public sealed class SandboxConquestRules : ConquestRules
{
    [System.Serializable] public sealed class Flight
    {
        public ArenaParticipant.Team team;
        public SandboxAirCombatAI.Battle battle;
        public Vehicle prefab;
        public Transform openingBase;
        public ArenaParticipant[] crew;
        [System.NonSerialized] public Vehicle current;
        [System.NonSerialized] public int respawns;
    }
    public SandboxInfantryZone[] infantryZones=System.Array.Empty<SandboxInfantryZone>();
    public Flight[] flights=System.Array.Empty<Flight>();
    public Transform airCombatCenter;
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
    public override int TeamScore(ArenaParticipant.Team team)=>team==ArenaParticipant.Team.Red?redScore:blueScore;
    public override bool NeedsDeployment(ArenaParticipant actor)=>false;
    public override void Begin(ArenaMatch match)
    {
        foreach(var flight in flights)
        {
            if(flight.current){foreach(var actor in flight.crew){var rider=actor.GetComponent<VehicleOccupant>();if(rider.IsMounted)rider.Leave(true);}Destroy(flight.current.gameObject);flight.current=null;}
            flight.respawns=0;
        }
        base.Begin(match);redScore=blueScore=0;scoreClock=0;UpdateAirCenter();
    }
    Flight CrewFlight(ArenaParticipant actor)
    {
        foreach(var f in flights)if(System.Array.IndexOf(f.crew,actor)>=0)return f;return null;
    }
    public override float RespawnDelay(ArenaParticipant actor)=>CrewFlight(actor)!=null?0:base.RespawnDelay(actor);
    public override bool CanDamage(ArenaParticipant victim,ArenaParticipant attacker)
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
    public override bool TrySpawn(ArenaParticipant actor)
    {
        if(actor.IsSpectator||actor.RespawnRemaining>0)return false;
        var flight=CrewFlight(actor);
        if(flight!=null)
        {
            var rider=actor.GetComponent<VehicleOccupant>();
            if(flight.current&&!flight.current.IsDestroyed&&rider.IsMounted)return true;
            if(!spawningFlight)SpawnFlight(flight);
            return rider.IsMounted;
        }
        var input=actor.GetComponent<SandboxInfantryInput>();if(!input)return false;
        foreach(var zone in infantryZones)if(zone.point==input.objective && zone.TryPosition(actor,out var p))
        {
            Vector3 toward=zone.point.transform.position-p;toward.y=0;
            actor.Respawn(p,toward.sqrMagnitude>.01f?Quaternion.LookRotation(toward):Quaternion.identity);
            input.Configure(zone.point);
            return !actor.IsAwaitingSpawn;
        }
        return false;
    }
    public override void Tick(float dt)
    {
        UpdateAirCenter();
        foreach(var flight in flights)
            if(flight.current && (flight.current.IsDestroyed || System.Array.Exists(flight.crew,a=>!a||a.health.IsDead)))SpawnFlight(flight);
        base.Tick(dt);
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
                var actor=source?source.GetComponentInParent<ArenaParticipant>():null;
                if(!actor)return true;
                var attacker=CrewFlight(actor);
                return attacker!=null&&attacker.battle==flight.battle&&attacker.team!=flight.team;
            };
            flight.respawns++;
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
            if(point.Owner==ArenaParticipant.Team.Red)redScore=(int)System.Math.Min(int.MaxValue,(long)redScore+ticks);
            else if(point.Owner==ArenaParticipant.Team.Blue)blueScore=(int)System.Math.Min(int.MaxValue,(long)blueScore+ticks);
        }
    }
    public override bool TryGetObjective(ArenaAIInput ai,out Vector3 destination)=>base.TryGetObjective(ai,out destination);
    public override void OnSpawned(ArenaParticipant actor,bool wasDead){}
    void OnDisable(){foreach(var f in flights)if(f.current)Destroy(f.current.gameObject);}
}
