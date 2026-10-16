using UnityEngine;

// Capture, deployment, squads and death/respawn stay owned by conquest.
public sealed class SandboxConquestRules : ConquestRules
{
    public CombinedArmsOrchestrator orchestrator;
    [Min(.1f)] public float scoreInterval=1;
    int redScore,blueScore;
    float scoreClock;
    public override string DisplayName => "COMBINED ARMS SANDBOX";
    public override bool HasEnded => false;
    public override bool UsesMatchTimer => false;
    public override string ScoreDescription => "CONTROL SCORE / NO LIMIT";
    public override int ScoreLimit => Mathf.Max(100,Mathf.Max(redScore,blueScore));
    public override int TeamScore(ArenaParticipant.Team team) => team==ArenaParticipant.Team.Red?redScore:blueScore;
    public override void Begin(ArenaMatch owner){base.Begin(owner);redScore=blueScore=0;scoreClock=0;}
    public override bool TryChooseSpawn(ArenaParticipant actor,System.Collections.Generic.Dictionary<Transform,float> history,out Transform spawn)
    {
        if(orchestrator && orchestrator.IsCrew(actor) && !NeedsDeployment(actor))
        {
            spawn=null;
            return actor.RespawnRemaining<=0 && !actor.NeedsClassSelection
                && ArenaSpawnSelection.TryChooseFrom(actor.match,actor,actor.team==ArenaParticipant.Team.Red?redBaseSpawns:blueBaseSpawns,history,out spawn);
        }
        return base.TryChooseSpawn(actor,history,out spawn);
    }
    protected override void TickScoring(float deltaTime)
    {
        scoreClock+=Mathf.Max(0,deltaTime);
        float interval=Mathf.Max(.1f,scoreInterval);
        int ticks=Mathf.FloorToInt(scoreClock/interval);
        if(ticks==0)return;
        scoreClock-=ticks*interval;
        foreach(var point in points)
        {
            if(point.Owner==ArenaParticipant.Team.Red)redScore=(int)System.Math.Min(int.MaxValue,(long)redScore+ticks);
            else if(point.Owner==ArenaParticipant.Team.Blue)blueScore=(int)System.Math.Min(int.MaxValue,(long)blueScore+ticks);
        }
    }
    public override bool TryGetObjective(ArenaAIInput ai,out Vector3 destination)
    {
        if(orchestrator && orchestrator.isActiveAndEnabled && orchestrator.TryGetDestination(ai.participant,out destination))return true;
        return base.TryGetObjective(ai,out destination);
    }
}
