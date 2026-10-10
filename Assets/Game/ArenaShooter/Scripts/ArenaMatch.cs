using UnityEngine;

[DefaultExecutionOrder(50)]
public class ArenaMatch : MonoBehaviour
{
    public ArenaMatchSettings settings;
    // Runtime roster, not a second manually maintained scene configuration.
    public ArenaParticipant[] participants { get; private set; } = System.Array.Empty<ArenaParticipant>();
    readonly System.Collections.Generic.List<ArenaParticipant> roster = new System.Collections.Generic.List<ArenaParticipant>();
    readonly System.Collections.Generic.List<ArenaParticipant> pendingJoins = new System.Collections.Generic.List<ArenaParticipant>();
    [Tooltip("Shared by both teams for respawns; also used for openings when dedicated anchors are unassigned.")]
    public Transform[] spawnPoints = System.Array.Empty<Transform>();
    [Header("Fixed match opening")]
    public Transform redOpeningSpawn;
    public Transform blueOpeningSpawn;
    [Tooltip("Map patrol routes and encounter areas, separate from respawn locations.")]
    public Transform[] patrolPoints = System.Array.Empty<Transform>();
    public bool IsRunning { get; private set; }
    public int RedScore { get; private set; }
    public int BlueScore { get; private set; }
    public float TimeRemaining { get; private set; }
    public string Result { get; private set; } = "";
    public event System.Action MatchEnded;
    public event System.Action MatchStarted;
    public event System.Action<ArenaParticipant, ArenaParticipant> KillRecorded;
    public Transform RedOpeningPoint { get; private set; }
    public Transform BlueOpeningPoint { get; private set; }
    readonly System.Collections.Generic.Dictionary<Transform, float> spawnHistory = new System.Collections.Generic.Dictionary<Transform, float>();

    void Start() => BeginMatch();
    void Update() => Tick(Time.deltaTime);

    public int TeamSize(ArenaParticipant.Team team)
    {
        int count = 0;
        foreach (var participant in roster) if (participant.team == team) count++;
        return count;
    }

    public void Register(ArenaParticipant participant)
    {
        if (participant.match != this || roster.Contains(participant)) return;
        roster.Add(participant);
        participants = roster.ToArray();
        // Defer placement until Update, after a newly instantiated actor's Awake/Start.
        participant.IsAwaitingSpawn = IsRunning;
        if (IsRunning) pendingJoins.Add(participant);
    }

    public void Unregister(ArenaParticipant participant)
    {
        pendingJoins.Remove(participant);
        participant.IsAwaitingSpawn = true;
        if (roster.Remove(participant)) participants = roster.ToArray();
    }

    public void RefreshParticipants()
    {
        roster.Clear(); pendingJoins.Clear();
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var participant in root.GetComponentsInChildren<ArenaParticipant>())
                if (participant.isActiveAndEnabled && participant.match == this) roster.Add(participant);
        participants = roster.ToArray();
    }

    // The reusable AI prefab can discover its owner in a scene with one match.
    public static ArenaMatch FindSceneMatch(UnityEngine.SceneManagement.Scene scene)
    {
        ArenaMatch selected = null;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var match in root.GetComponentsInChildren<ArenaMatch>())
            {
                if (!match.isActiveAndEnabled) continue;
                if (selected) throw new System.InvalidOperationException("Assign the participant's Match when a scene contains multiple ArenaMatches.");
                selected = match;
            }
        if (!selected) throw new System.InvalidOperationException("ArenaParticipant needs an ArenaMatch in its scene.");
        return selected;
    }

    public void BeginMatch()
    {
        RefreshParticipants();
        RedScore = BlueScore = 0;
        spawnHistory.Clear();
        TimeRemaining = settings.matchDuration;
        Result = "";
        IsRunning = false;
        RedOpeningPoint = BlueOpeningPoint = null;
        foreach (var participant in participants)
        {
            participant.Kills = participant.Deaths = 0;
            participant.CancelRespawn();
            if (participant.player) participant.player.SetGameplayAllowed(false);
        }
        // Plan both complete formations before teleporting anyone. Old positions on a
        // restart must not influence the opening, and partial team starts are rejected.
        if (!ArenaSpawnSelection.TryPlanOpening(this, out var placements, out var red, out var blue))
        {
            Result = "No room for the opening teams. Check spawn points and opening radius.";
            Debug.LogWarning(Result, this);
            return;
        }
        RedOpeningPoint = red; BlueOpeningPoint = blue;
        IsRunning = true;
        foreach (var placement in placements)
            placement.participant.Respawn(placement.position, placement.rotation);
        if (red) spawnHistory[red] = Time.time;
        if (blue) spawnHistory[blue] = Time.time;
        Physics.SyncTransforms();
        MatchStarted?.Invoke();
    }

    // Explicit entry point also makes local rule checks independent of real-time waits.
    public void Tick(float deltaTime)
    {
        if (!IsRunning) return;
        for (int i = pendingJoins.Count - 1; i >= 0; i--)
            if (Spawn(pendingJoins[i])) pendingJoins.RemoveAt(i);
        TimeRemaining = Mathf.Max(0f, TimeRemaining - Mathf.Max(0f, deltaTime));
        if (TimeRemaining == 0f)
        {
            EndMatch();
            return;
        }
        foreach (ArenaParticipant participant in participants) participant.TickRespawn();
    }

    public bool CanDamage(ArenaParticipant victim, GameObject source)
    {
        if (!IsRunning || victim.IsAwaitingSpawn) return false;
        // Environmental damage has no attacker; self damage is allowed, without awarding a point.
        var attacker = source ? source.GetComponentInParent<ArenaParticipant>() : null;
        if (!attacker || attacker == victim) return true;
        if (attacker.IsAwaitingSpawn) return false;
        if (attacker.match != this) return false;
        return settings.friendlyFire || attacker.team != victim.team;
    }

    internal void RecordKill(ArenaParticipant victim, GameObject source)
    {
        if (!IsRunning) return;
        victim.Deaths++;
        var attacker = source ? source.GetComponentInParent<ArenaParticipant>() : null;
        if (!attacker || attacker == victim || attacker.match != this || attacker.team == victim.team) return;
        attacker.Kills++;
        if (attacker.team == ArenaParticipant.Team.Red) RedScore++;
        else BlueScore++;
        KillRecorded?.Invoke(attacker, victim);
        if (RedScore >= settings.scoreLimit || BlueScore >= settings.scoreLimit) EndMatch();
    }

    public bool Spawn(ArenaParticipant participant)
    {
        if (!IsRunning) return false;
        if (!ArenaSpawnSelection.TryChoose(this, participant, spawnHistory, out var spawn)) return false;
        participant.Respawn(spawn);
        spawnHistory[spawn] = Time.time;
        // Successive spawns in the same frame must see the previous actor's new collider position.
        Physics.SyncTransforms();
        return true;
    }

    public void EndMatch()
    {
        if (!IsRunning) return;
        IsRunning = false;
        pendingJoins.Clear();
        Result = RedScore == BlueScore ? "Draw" : RedScore > BlueScore ? "Red wins" : "Blue wins";
        foreach (ArenaParticipant participant in participants)
        {
            participant.CancelRespawn();
            if (participant.player) participant.player.SetGameplayAllowed(false);
        }
        MatchEnded?.Invoke();
    }

    void OnDisable() => EndMatch();

    void OnDrawGizmosSelected()
    {
        DrawPoints(spawnPoints, Color.cyan);
        DrawPoints(patrolPoints, Color.yellow);
        if (Application.isPlaying)
        {
            if (RedOpeningPoint) { Gizmos.color = Color.red; Gizmos.DrawWireSphere(RedOpeningPoint.position, settings.openingTeamRadius); }
            if (BlueOpeningPoint) { Gizmos.color = Color.blue; Gizmos.DrawWireSphere(BlueOpeningPoint.position, settings.openingTeamRadius); }
        }
    }

    static void DrawPoints(Transform[] points, Color color)
    {
        Gizmos.color = color;
        foreach (var point in points)
        {
            Gizmos.DrawWireSphere(point.position + Vector3.up * .5f, .5f);
            Gizmos.DrawLine(point.position, point.position + point.forward * 1.5f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(point.position + Vector3.up, point.name);
#endif
        }
    }
}
