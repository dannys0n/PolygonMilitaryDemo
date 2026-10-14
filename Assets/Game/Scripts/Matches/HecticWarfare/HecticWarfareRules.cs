using System.Collections.Generic;
using UnityEngine;

public sealed class HecticWarfareRules : ConquestRules
{
    public HecticWarfareScenario scenario;
    public HecticAIOrchestrator orchestrator;
    ArenaMatch owner;
    public override string DisplayName=>scenario&&scenario.anyWeapons?"HECTIC WARFARE / ANY WEAPONS":"HECTIC WARFARE";
    public override bool HandlesOpening=>true;
    public override bool NeedsDeployment(ArenaParticipant actor)=>false;
    public override void Begin(ArenaMatch match)
    {base.Begin(match);owner=match;if(scenario)scenario.ResetEvent();}
    public override void SpawnOpening()
    {foreach(var actor in owner.participants){actor.IsAwaitingSpawn=true;scenario.QueueOpening(actor);}}
    public override bool TrySpawnDirect(ArenaParticipant actor,out bool spawned)
    {
        spawned=false;
        if(actor.IsSpectator||actor.RespawnRemaining>0||actor.NeedsClassSelection)return true;
        bool wasDead=actor.health.IsDead;
        if(scenario.TryDeploy(actor))
        {spawned=!actor.IsAwaitingSpawn;if(spawned)OnSpawned(actor,wasDead);}
        return true;
    }
    public override bool TryGetObjective(ArenaAIInput ai,out Vector3 destination)
    {
        if(orchestrator&&orchestrator.TryGetObjective(ai,out destination))return true;
        return base.TryGetObjective(ai,out destination);
    }
}
