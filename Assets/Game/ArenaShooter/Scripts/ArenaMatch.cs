using UnityEngine;

[DefaultExecutionOrder(50)]
public class ArenaMatch : MonoBehaviour
{
    public ArenaMatchSettings settings;
    public ArenaParticipant[] participants;
    public Transform[] redSpawns = System.Array.Empty<Transform>();
    public Transform[] blueSpawns = System.Array.Empty<Transform>();
    [Tooltip("Map patrol routes and encounter areas, separate from respawn locations.")]
    public Transform[] patrolPoints = System.Array.Empty<Transform>();
    public bool IsRunning { get; private set; }
    public int RedScore { get; private set; }
    public int BlueScore { get; private set; }
    public float TimeRemaining { get; private set; }
    public string Result { get; private set; } = "";
    public event System.Action MatchEnded;
    readonly System.Collections.Generic.Dictionary<Transform, float> spawnHistory = new System.Collections.Generic.Dictionary<Transform, float>();

    void Start() => BeginMatch();
    void Update() => Tick(Time.deltaTime);

    public void BeginMatch()
    {
        RedScore = BlueScore = 0;
        spawnHistory.Clear();
        TimeRemaining = settings.matchDuration;
        Result = "";
        IsRunning = true;
        foreach (ArenaParticipant participant in participants) Spawn(participant);
    }

    // Explicit entry point also makes local rule checks independent of real-time waits.
    public void Tick(float deltaTime)
    {
        if (!IsRunning) return;
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
        if (!IsRunning) return false;
        // Environmental damage has no attacker; self damage is allowed, without awarding a point.
        var attacker = source ? source.GetComponentInParent<ArenaParticipant>() : null;
        if (!attacker || attacker == victim) return true;
        if (attacker.match != this) return false;
        return settings.friendlyFire || attacker.team != victim.team;
    }

    internal void RecordKill(ArenaParticipant victim, GameObject source)
    {
        if (!IsRunning) return;
        var attacker = source ? source.GetComponentInParent<ArenaParticipant>() : null;
        if (!attacker || attacker == victim || attacker.match != this || attacker.team == victim.team) return;
        if (attacker.team == ArenaParticipant.Team.Red) RedScore++;
        else BlueScore++;
        if (RedScore >= settings.scoreLimit || BlueScore >= settings.scoreLimit) EndMatch();
    }

    public void Spawn(ArenaParticipant participant)
    {
        if (!IsRunning) return;
        if (!ArenaSpawnSelection.TryChoose(this, participant, spawnHistory, out var spawn)) return;
        participant.Respawn(spawn);
        spawnHistory[spawn] = Time.time;
        // Successive spawns in the same frame must see the previous actor's new collider position.
        Physics.SyncTransforms();
    }

    public void EndMatch()
    {
        if (!IsRunning) return;
        IsRunning = false;
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
        DrawPoints(redSpawns, Color.red);
        DrawPoints(blueSpawns, Color.blue);
        DrawPoints(patrolPoints, Color.yellow);
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
