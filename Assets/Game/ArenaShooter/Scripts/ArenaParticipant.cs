using UnityEngine;

public class ArenaParticipant : MonoBehaviour
{
    public enum Team { Red, Blue }
    public Team team;
    public SoldierHealth health;
    [Tooltip("Optional: stationary test targets have health but no player lifecycle.")]
    public SoldierLife player;
    public ArenaMatch match;
    private float respawnAt = -1f;
    public bool IsRespawning => respawnAt >= 0f && health.IsDead;
    public float RespawnRemaining => IsRespawning ? Mathf.Max(0f, respawnAt - Time.time) : 0f;

    void OnEnable()
    {
        health.Died += OnDied;
        health.DamageFilter = CanReceiveDamage;
    }

    void OnDisable()
    {
        health.Died -= OnDied;
        health.DamageFilter = null;
        CancelRespawn();
    }

    bool CanReceiveDamage(GameObject source) => match.CanDamage(this, source);

    void OnDied(GameObject source)
    {
        respawnAt = Time.time + match.settings.respawnDelay;
        match.RecordKill(this, source);
    }

    public void TickRespawn()
    {
        if (IsRespawning && Time.time >= respawnAt) match.Spawn(this);
    }

    public void CancelRespawn() => respawnAt = -1f;

    public void Respawn(Transform spawn)
    {
        CancelRespawn();
        if (player)
        {
            int[] slots = team == Team.Red ? match.settings.redLoadout : match.settings.blueLoadout;
            player.SetGameplayAllowed(false);
            player.loadout.ConfigureAvailableSlots(slots);
            player.Respawn(spawn.position, spawn.rotation);
        }
        else
        {
            transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            health.ResetHealth();
        }
    }
}
