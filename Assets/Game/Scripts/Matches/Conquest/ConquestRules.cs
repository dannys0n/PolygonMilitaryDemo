using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class ConquestRules : ArenaMatchRules
{
    [Header("Tickets")]
    [Min(1)] public int startingTickets=300;
    [Min(.1f)] public float bleedInterval=2;
    [Min(1)] public int ticketsPerExtraFlag=1;
    [Header("Map")]
    public ConquestCapturePoint[] points;
    public ConquestSquads squads;
    public Transform[] redBaseSpawns, blueBaseSpawns;
    [Header("AI objectives")]
    [Min(1)] public float objectiveDecisionInterval=4;
    [Min(1)] public int defendersPerFlag=2;
    [Min(0)] public float postCaptureDefenseTime=6;
    public override string DisplayName => "CONQUEST";
    public override int ScoreLimit => startingTickets;
    public override bool HasEnded => redTickets<=0 || blueTickets<=0;
    public int RedTickets => redTickets;
    public int BlueTickets => blueTickets;
    public override int TeamScore(ArenaParticipant.Team team) => team==ArenaParticipant.Team.Red ? redTickets : blueTickets;
    ArenaMatch match;
    int redTickets,blueTickets;
    float captureClock,bleedClock;
    readonly Dictionary<ArenaParticipant,ConquestCapturePoint> deployments=new Dictionary<ArenaParticipant,ConquestCapturePoint>();
    readonly Dictionary<ArenaParticipant,ArenaParticipant> squadDeployments=new Dictionary<ArenaParticipant,ArenaParticipant>();
    readonly HashSet<ArenaParticipant> approved=new HashSet<ArenaParticipant>();
    readonly Dictionary<ArenaParticipant,Assignment> assignments=new Dictionary<ArenaParticipant,Assignment>();
    readonly List<Transform> candidates=new List<Transform>();
    NavMeshPath query;
    struct Assignment { public ConquestCapturePoint point; public Vector3 destination; public float reconsiderAt; }
    public override void Begin(ArenaMatch owner)
    {
        match=owner;redTickets=blueTickets=startingTickets;captureClock=bleedClock=0;
        deployments.Clear();squadDeployments.Clear();approved.Clear();assignments.Clear();
        query ??= new NavMeshPath();
        foreach(var point in points)point.ResetCapture();
    }
    public override void Tick(float deltaTime)
    {
        captureClock+=Mathf.Max(0,deltaTime);
        if(captureClock>=.2f)
        {
            foreach(var point in points)
                if(point.Tick(match.participants,captureClock) && point.Owner.HasValue)
                    foreach(var actor in match.participants)
                        if(!actor.health.IsDead && !actor.IsAwaitingSpawn && actor.team==point.Owner.Value && point.Contains(actor.transform.position))
                        { actor.Captures++; actor.ObjectiveScore+=200; }
            captureClock=0;
        }
        TickScoring(deltaTime);
    }
    protected virtual void TickScoring(float deltaTime)
    {
        bleedClock+=Mathf.Max(0,deltaTime);
        while(bleedClock>=bleedInterval && !HasEnded)
        {
            bleedClock-=bleedInterval;
            int red=0,blue=0;
            foreach(var point in points){if(point.Owner==ArenaParticipant.Team.Red)red++;else if(point.Owner==ArenaParticipant.Team.Blue)blue++;}
            int threshold=points.Length/2;
            if(red>threshold)blueTickets=Mathf.Max(0,blueTickets-(red-threshold)*ticketsPerExtraFlag);
            if(blue>threshold)redTickets=Mathf.Max(0,redTickets-(blue-threshold)*ticketsPerExtraFlag);
        }
    }
    public virtual bool CanDeploy(ArenaParticipant actor, ConquestCapturePoint point) => !point || point.Owner==actor.team
        && point.Count(actor.team==ArenaParticipant.Team.Red?ArenaParticipant.Team.Blue:ArenaParticipant.Team.Red)==0;
    public void SelectDeployment(ArenaParticipant actor, ConquestCapturePoint point) { deployments[actor]=point;squadDeployments.Remove(actor); }
    public void SelectSquadDeployment(ArenaParticipant actor,ArenaParticipant member){squadDeployments[actor]=member;deployments[actor]=null;}
    public void ApproveDeployment(ArenaParticipant actor) => approved.Add(actor);
    public void CancelDeployment(ArenaParticipant actor) => approved.Remove(actor);
    protected bool DeploymentApproved(ArenaParticipant actor) => approved.Contains(actor);
    protected ConquestCapturePoint DeploymentPoint(ArenaParticipant actor) => deployments.TryGetValue(actor,out var point)?point:null;
    public override bool NeedsDeployment(ArenaParticipant actor) => actor.player && actor.GetComponent<SoldierLocalInput>();
    public override IEnumerable<ArenaParticipant> SquadMembers(ArenaParticipant actor) => squads ? squads.Members(actor) : null;
    public override bool TryChooseSpawn(ArenaParticipant actor, Dictionary<Transform,float> history, out Transform spawn)
    {
        spawn=null;
        if(HasEnded || actor.RespawnRemaining>0 || actor.NeedsClassSelection || NeedsDeployment(actor) && !approved.Contains(actor))return false;
        // Dedicated crews return to HQ near vehicle pads instead of repeatedly
        // deploying into the middle of infantry flag fights.
        if(squads && squads.IsVehicleSquad(actor) && actor.GetComponent<ArenaVehicleAI>()
            && ArenaSpawnSelection.TryChooseFrom(match,actor,actor.team==ArenaParticipant.Team.Red?redBaseSpawns:blueBaseSpawns,history,out spawn))return true;
        if(squads)
        {
            ArenaParticipant mate=null;
            if(NeedsDeployment(actor))squadDeployments.TryGetValue(actor,out mate);
            else
            {
                float closest=float.PositiveInfinity;
                foreach(var member in squads.Members(actor))
                {
                    float distance=(actor.transform.position-member.transform.position).sqrMagnitude;
                    if(distance>=closest || !squads.CanDeploy(actor,member))continue;
                    closest=distance;mate=member;
                }
            }
            if(mate && squads.TryChooseSpawn(actor,mate,history,out spawn))return true;
        }
        candidates.Clear();
        if(NeedsDeployment(actor))
        {
            deployments.TryGetValue(actor,out var desired);
            if(desired && CanDeploy(actor,desired))candidates.AddRange(desired.spawnPoints);
        }
        else
        {
            // Reinforce a useful owned objective; bases remain the reliable fallback.
            ConquestCapturePoint choice=null;float best=float.NegativeInfinity;
            foreach(var point in points)
            {
                if(!CanDeploy(actor,point))continue;
                float score=point.Count(actor.team==ArenaParticipant.Team.Red?ArenaParticipant.Team.Blue:ArenaParticipant.Team.Red)*20;
                foreach(var enemy in match.participants)
                    if(enemy.team!=actor.team && !enemy.health.IsDead && !enemy.IsAwaitingSpawn)
                        score=Mathf.Max(score,30-Vector3.Distance(point.transform.position,enemy.transform.position)*.2f);
                score+=Random.value*5;
                if(score>best){best=score;choice=point;}
            }
            if(choice)candidates.AddRange(choice.spawnPoints);
        }
        bool found=ArenaSpawnSelection.TryChooseFrom(match,actor,candidates,history,out spawn);
        if(!found)found=ArenaSpawnSelection.TryChooseFrom(match,actor,actor.team==ArenaParticipant.Team.Red?redBaseSpawns:blueBaseSpawns,history,out spawn);
        return found;
    }
    public override void OnSpawned(ArenaParticipant actor, bool wasDead)
    {
        approved.Remove(actor);assignments.Remove(actor);
        if(!wasDead)return;
        if(actor.team==ArenaParticipant.Team.Red)redTickets=Mathf.Max(0,redTickets-1);else blueTickets=Mathf.Max(0,blueTickets-1);
    }
    public override bool TryGetObjective(ArenaAIInput ai, out Vector3 destination)
    {
        var actor=ai.participant;
        bool hasAssignment=assignments.TryGetValue(actor,out var current);
        bool captured=hasAssignment && current.point && current.point.Owner==actor.team;
        bool defending=captured && (current.point.Count(actor.team==ArenaParticipant.Team.Red?ArenaParticipant.Team.Blue:ArenaParticipant.Team.Red)>0
            || Time.time-ai.LastCombatTime<postCaptureDefenseTime);
        if(hasAssignment && (defending || !captured && Time.time<current.reconsiderAt))
        {destination=current.destination;return true;}
        ConquestCapturePoint selected=null;Vector3 selectedPosition=default;float best=float.NegativeInfinity;
        foreach(var point in points)
        {
            int assigned=0;
            foreach(var pair in assignments)
                if(pair.Key!=actor && pair.Key && pair.Key.team==actor.team && !pair.Key.health.IsDead && !pair.Key.IsAwaitingSpawn && pair.Value.point==point)assigned++;
            bool friendly=point.Owner==actor.team;
            int enemies=point.Count(actor.team==ArenaParticipant.Team.Red?ArenaParticipant.Team.Blue:ArenaParticipant.Team.Red);
            // Capturing is not a permanent parking assignment. Quiet owned flags only
            // receive patrol visits when there are no neutral/enemy objectives to attack.
            float priority=friendly ? enemies>0 ? 65 : 0 : point.Owner.HasValue ? 45 : 48;
            float angle=Mathf.Repeat(System.Array.IndexOf(match.participants,actor)*137.5f,360);
            Vector3 proposed=point.transform.position+Quaternion.Euler(0,angle,0)*Vector3.forward*(point.radius*.5f);
            if(!NavMesh.SamplePosition(proposed,out var hit,2,new NavMeshQueryFilter{agentTypeID=ai.agent.agentTypeID,areaMask=ai.agent.areaMask}) || !point.Contains(hit.position))continue;
            if(!ArenaAITactics.Reachable(ai,hit.position,query,out var reachable,out float distance))continue;
            // Distribution is a tie-breaker, never a reason to camp at a quiet owned flag.
            float score=priority-Mathf.Min(distance*.12f,12)-Mathf.Min(assigned*2,12)+(current.point==point?3:0)
                - (captured && !defending && current.point==point ? 35 : 0);
            if(score<=best)continue;
            best=score;selected=point;selectedPosition=reachable;
        }
        if(!selected){destination=default;return false;}
        assignments[actor]=new Assignment{point=selected,destination=selectedPosition,reconsiderAt=Time.time+objectiveDecisionInterval+Random.value};
        destination=selectedPosition;return true;
    }
}
