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
    public Transform[] redBaseSpawns, blueBaseSpawns;
    [Header("AI objectives")]
    [Min(1)] public float objectiveDecisionInterval=4;
    [Min(1)] public int defendersPerFlag=2;
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
    readonly HashSet<ArenaParticipant> approved=new HashSet<ArenaParticipant>();
    readonly Dictionary<ArenaParticipant,Assignment> assignments=new Dictionary<ArenaParticipant,Assignment>();
    readonly List<Transform> candidates=new List<Transform>();
    NavMeshPath query;
    struct Assignment { public ConquestCapturePoint point; public Vector3 destination; public float reconsiderAt; }
    public override void Begin(ArenaMatch owner)
    {
        match=owner;redTickets=blueTickets=startingTickets;captureClock=bleedClock=0;
        deployments.Clear();approved.Clear();assignments.Clear();
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
    public bool CanDeploy(ArenaParticipant actor, ConquestCapturePoint point) => !point || point.Owner==actor.team
        && point.Count(actor.team==ArenaParticipant.Team.Red?ArenaParticipant.Team.Blue:ArenaParticipant.Team.Red)==0;
    public void SelectDeployment(ArenaParticipant actor, ConquestCapturePoint point) { deployments[actor]=point; }
    public void ApproveDeployment(ArenaParticipant actor) => approved.Add(actor);
    public void CancelDeployment(ArenaParticipant actor) => approved.Remove(actor);
    public override bool NeedsDeployment(ArenaParticipant actor) => actor.player && actor.GetComponent<SoldierLocalInput>();
    public override bool TryChooseSpawn(ArenaParticipant actor, Dictionary<Transform,float> history, out Transform spawn)
    {
        spawn=null;
        if(TeamScore(actor.team)<=0 || actor.RespawnRemaining>0 || actor.NeedsClassSelection || NeedsDeployment(actor) && !approved.Contains(actor))return false;
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
        if(assignments.TryGetValue(actor,out var current) && Time.time<current.reconsiderAt)
        {destination=current.destination;return true;}
        ConquestCapturePoint selected=null;Vector3 selectedPosition=default;float best=float.NegativeInfinity;
        foreach(var point in points)
        {
            int assigned=0;
            foreach(var pair in assignments)
                if(pair.Key!=actor && pair.Key && pair.Key.team==actor.team && !pair.Key.health.IsDead && !pair.Key.IsAwaitingSpawn && pair.Value.point==point)assigned++;
            bool friendly=point.Owner==actor.team;
            int enemies=point.Count(actor.team==ArenaParticipant.Team.Red?ArenaParticipant.Team.Blue:ArenaParticipant.Team.Red);
            float priority=friendly ? enemies>0 ? 65 : assigned<defendersPerFlag ? 24 : 3 : point.Owner.HasValue ? 45 : 48;
            float angle=Mathf.Repeat(System.Array.IndexOf(match.participants,actor)*137.5f,360);
            Vector3 proposed=point.transform.position+Quaternion.Euler(0,angle,0)*Vector3.forward*(point.radius*.5f);
            if(!NavMesh.SamplePosition(proposed,out var hit,2,new NavMeshQueryFilter{agentTypeID=ai.agent.agentTypeID,areaMask=ai.agent.areaMask}) || !point.Contains(hit.position))continue;
            if(!ArenaAITactics.Reachable(ai,hit.position,query,out var reachable,out float distance))continue;
            float score=priority-distance*.12f-assigned*6+(current.point==point?3:0);
            if(score<=best)continue;
            best=score;selected=point;selectedPosition=reachable;
        }
        if(!selected){destination=default;return false;}
        assignments[actor]=new Assignment{point=selected,destination=selectedPosition,reconsiderAt=Time.time+objectiveDecisionInterval+Random.value};
        destination=selectedPosition;return true;
    }
}
