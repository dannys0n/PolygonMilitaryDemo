using System.Collections.Generic;
using UnityEngine;

// Optional mode rules; the existing kill-score mode is retained when unassigned.
public abstract class ArenaMatchRules : MonoBehaviour
{
    public abstract string DisplayName { get; }
    public abstract int ScoreLimit { get; }
    public abstract bool HasEnded { get; }
    public virtual bool UsesMatchTimer => true;
    public virtual string ScoreDescription => "TEAM TICKETS";
    public abstract int TeamScore(ArenaParticipant.Team team);
    public abstract void Begin(ArenaMatch match);
    public abstract void Tick(float deltaTime);
    public abstract bool TryChooseSpawn(ArenaParticipant actor, Dictionary<Transform,float> history, out Transform spawn);
    public abstract void OnSpawned(ArenaParticipant actor, bool wasDead);
    public virtual bool NeedsDeployment(ArenaParticipant actor) => false;
    // Modes with volumes or coupled vehicle crews can own placement instead of point selection.
    public virtual bool UsesCustomSpawning => false;
    public virtual bool TrySpawn(ArenaParticipant actor) => false;
    public virtual float RespawnDelay(ArenaParticipant actor) => actor.match.settings.respawnDelay;
    public virtual bool CanDamage(ArenaParticipant victim,ArenaParticipant attacker) => true;
    // Null keeps the HUD's nearby-team display for modes without squads.
    public virtual IEnumerable<ArenaParticipant> SquadMembers(ArenaParticipant actor) => null;
    public virtual bool TryGetObjective(ArenaAIInput ai, out Vector3 destination) { destination=default; return false; }
}
