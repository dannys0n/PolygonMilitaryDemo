using UnityEngine;

public class ArenaParticipant : MonoBehaviour
{
    public enum Team { Red, Blue }
    public bool IsHumanControlled => player && player.GetComponent<SoldierController>().IsHumanControlled;
    public bool IsLocalPlayer => player && player.GetComponent<SoldierController>().IsLocallyControlled;
    public Team team;
    public SoldierHealth health;
    [Tooltip("Optional: stationary test targets have health but no player lifecycle.")]
    public SoldierLife player;
    [Tooltip("Optional in a scene with one ArenaMatch; assigned automatically for newly added actors.")]
    public ArenaMatch match;
    public int Kills { get; internal set; }
    public int Deaths { get; internal set; }
    public int Captures { get; internal set; }
    public int ObjectiveScore { get; internal set; }
    public int Score => Kills * 100 + ObjectiveScore;
    public float LastShotTime { get; private set; } = float.NegativeInfinity;
    public Vector3 LastShotPosition { get; private set; }
    private float respawnAt = -1f;
    private float nextSpawnAttempt;
    public bool IsRespawning => respawnAt >= 0f && health.IsDead;
    public bool IsAwaitingSpawn { get; internal set; }
    public bool IsSpectator { get; private set; }
    public void SetSpectating(bool value)
    {
        IsSpectator = value;
        IsAwaitingSpawn = true;
        CancelRespawn();
        if (value) player.SetGameplayAllowed(false);
        else { respawnAt = Time.time; nextSpawnAttempt = Time.time; }
    }
    public float RespawnRemaining => IsRespawning ? Mathf.Max(0f, respawnAt - Time.time) : 0f;

    void Awake()
    {
        if (!match) match = ArenaMatch.FindSceneMatch(gameObject.scene);
    }

    void OnEnable()
    {
        health.Died += OnDied;
        health.DamageFilter = CanReceiveDamage;
        if (player) player.actions.ShotFired += RecordShot;
        match.Register(this);
    }

    void OnDisable()
    {
        health.Died -= OnDied;
        health.DamageFilter = null;
        if (player) player.actions.ShotFired -= RecordShot;
        CancelRespawn();
        match.Unregister(this);
    }

    bool CanReceiveDamage(GameObject source) => match.CanDamage(this, source);
    void RecordShot() => RecordShotAt(transform.position);
    public void RecordShotAt(Vector3 origin) { LastShotTime = Time.time; LastShotPosition = origin; }

    void OnDied(GameObject source)
    {
        respawnAt = Time.time + (match.rules ? match.rules.RespawnDelay(this) : match.settings.respawnDelay);
        nextSpawnAttempt = respawnAt;
        match.RecordKill(this, source);
    }

    public void TickRespawn()
    {
        if (IsSpectator || respawnAt < 0f || !health.IsDead && !IsAwaitingSpawn || Time.time < nextSpawnAttempt) return;
        nextSpawnAttempt = Time.time + .5f;
        match.Spawn(this);
    }

    public void CancelRespawn() => respawnAt = -1f;

    public void Respawn(Transform spawn) => Respawn(spawn.position, spawn.rotation);

    public void Respawn(Vector3 position, Quaternion rotation)
    {
        if (IsSpectator) return;
        CancelRespawn();
        LastShotTime = float.NegativeInfinity;
        IsAwaitingSpawn = false;
        if (player)
        {
            player.SetGameplayAllowed(false);
            var equipment=match.GetComponent<ArenaRandomLoadouts>();
            if(!equipment)equipment=match.gameObject.AddComponent<ArenaRandomLoadouts>();
            equipment.Apply(this);
            player.Respawn(position, rotation);
        }
        else
        {
            transform.SetPositionAndRotation(position, rotation);
            health.ResetHealth();
        }
    }
}
