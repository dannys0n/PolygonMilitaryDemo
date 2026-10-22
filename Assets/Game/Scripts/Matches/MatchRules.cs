using System.Collections.Generic;
using UnityEngine;

// Optional mode rules; the existing kill-score mode is retained when unassigned.
[UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, "Game.Runtime", "ArenaMatchRules")]
public abstract class MatchRules : MonoBehaviour
{
    public abstract string DisplayName { get; }
    public abstract int ScoreLimit { get; }
    public abstract bool HasEnded { get; }
    public virtual bool UsesMatchTimer => true;
    public virtual string ScoreDescription => "TEAM TICKETS";
    public abstract int TeamScore(MatchParticipant.Team team);
    public abstract void Begin(MatchController match);
    public abstract void Tick(float deltaTime);
    public abstract bool TryChooseSpawn(MatchParticipant actor, Dictionary<Transform,float> history, out Transform spawn);
    public abstract void OnSpawned(MatchParticipant actor, bool wasDead);
    public virtual bool NeedsDeployment(MatchParticipant actor) => false;
    // Modes with volumes or coupled vehicle crews can own placement instead of point selection.
    public virtual bool UsesCustomSpawning => false;
    public virtual bool TrySpawn(MatchParticipant actor) => false;
    public virtual float RespawnDelay(MatchParticipant actor) => actor.match.settings.respawnDelay;
    public virtual bool CanDamage(MatchParticipant victim,MatchParticipant attacker) => true;
    // Null keeps the HUD's nearby-team display for modes without squads.
    public virtual IEnumerable<MatchParticipant> SquadMembers(MatchParticipant actor) => null;
    public virtual bool TryGetObjective(AIInput ai, out Vector3 destination) { destination=default; return false; }
}
