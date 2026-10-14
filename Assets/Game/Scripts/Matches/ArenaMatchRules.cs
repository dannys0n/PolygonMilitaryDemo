using System.Collections.Generic;
using UnityEngine;

// Optional mode rules; the existing kill-score mode is retained when unassigned.
public abstract class ArenaMatchRules : MonoBehaviour
{
    public abstract string DisplayName { get; }
    public abstract int ScoreLimit { get; }
    public abstract bool HasEnded { get; }
    public abstract int TeamScore(ArenaParticipant.Team team);
    public abstract void Begin(ArenaMatch match);
    public abstract void Tick(float deltaTime);
    public abstract bool TryChooseSpawn(ArenaParticipant actor, Dictionary<Transform,float> history, out Transform spawn);
    public abstract void OnSpawned(ArenaParticipant actor, bool wasDead);
    // Modes with vehicle/airborne deployment bypass the infantry NavMesh placement path.
    public virtual bool HandlesOpening => false;
    public virtual void SpawnOpening() { }
    public virtual bool TrySpawnDirect(ArenaParticipant actor,out bool spawned){spawned=false;return false;}
    public virtual bool NeedsDeployment(ArenaParticipant actor) => false;
    // Null keeps the HUD's nearby-team display for modes without squads.
    public virtual IEnumerable<ArenaParticipant> SquadMembers(ArenaParticipant actor) => null;
    public virtual bool TryGetObjective(ArenaAIInput ai, out Vector3 destination) { destination=default; return false; }
}
