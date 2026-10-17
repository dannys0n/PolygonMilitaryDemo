using UnityEngine;

public class ArenaParticipant : MonoBehaviour
{
    public enum Team { Red, Blue }
    public Team team;
    public SoldierHealth health;
    [Tooltip("Optional: stationary test targets have health but no player lifecycle.")]
    public SoldierLife player;
    [Tooltip("Optional in a scene with one ArenaMatch; assigned automatically for newly added actors.")]
    public ArenaMatch match;
    public ArenaClassDefinition SelectedClass { get; private set; }
    public ArenaClassDefinition ActiveClass { get; private set; }
    Vector3 openingPosition;
    Quaternion openingRotation;
    bool waitingForClass;
    public bool NeedsClassSelection => player && GetComponent<SoldierLocalInput>() && !SelectedClass;
    public void SelectClass(ArenaClassDefinition choice)
    {
        if (!choice.isCustom && System.Array.IndexOf(match.settings.classes, choice) < 0)
            throw new System.ArgumentException("Class is not allowed by this match.");
        choice.ResolveSlots(player.loadout);
        var previous = SelectedClass;
        SelectedClass = choice.isCustom ? Instantiate(choice) : choice;
        if (previous && previous.isCustom) Destroy(previous);
        if (waitingForClass && match.IsRunning && (!match.rules || !match.rules.NeedsDeployment(this)))
        {
            waitingForClass = false;
            Respawn(openingPosition, openingRotation);
        }
    }
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
        if (player) player.actions.FireStarted += RecordShot;
        match.Register(this);
    }

    void OnDisable()
    {
        health.Died -= OnDied;
        health.DamageFilter = null;
        if (player) player.actions.FireStarted -= RecordShot;
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
        var randomEquipment = match.GetComponent<ArenaRandomLoadouts>();
        if (player && randomEquipment) randomEquipment.Apply(this);
        else if (player && match.settings.randomizeAIClassOnRespawn && GetComponent<ArenaAIInput>())
            SelectClass(match.settings.classes[Random.Range(0, match.settings.classes.Length)]);
        if (player && !SelectedClass)
        {
            if (NeedsClassSelection)
            {
                openingPosition = position; openingRotation = rotation;
                waitingForClass = true; IsAwaitingSpawn = true;
                player.SetGameplayAllowed(false);
                if (match.rules && match.rules.NeedsDeployment(this))
                {
                    // Keep the waiting camera at the opening base, not the prefab's old position.
                    player.controller.enabled = false;
                    transform.SetPositionAndRotation(position, rotation);
                    player.look.ResetLook();
                    player.controller.enabled = true;
                }
                return;
            }
            SelectClass(match.settings.classes[Random.Range(0, match.settings.classes.Length)]);
        }
        waitingForClass = false;
        CancelRespawn();
        LastShotTime = float.NegativeInfinity;
        IsAwaitingSpawn = false;
        if (player)
        {
            int[] slots = SelectedClass.ResolveSlots(player.loadout);
            ActiveClass = SelectedClass;
            player.SetGameplayAllowed(false);
            player.loadout.ConfigureAvailableSlots(slots);
            player.loadout.SetClassSights(SelectedClass.primarySight, SelectedClass.secondarySight);
            player.loadout.SetClassAttachments(SelectedClass.primaryAttachments,SelectedClass.secondaryAttachments);
            player.Respawn(position, rotation);
            var utilities=GetComponent<SoldierUtilitySlots>();if(utilities)utilities.Configure(SelectedClass.utilities);
            GetComponent<SoldierGrenades>().SelectType(SelectedClass.grenadeIndex);
        }
        else
        {
            transform.SetPositionAndRotation(position, rotation);
            health.ResetHealth();
        }
    }
}
