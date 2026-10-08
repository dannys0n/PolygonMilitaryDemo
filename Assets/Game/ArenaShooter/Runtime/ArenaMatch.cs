using UnityEngine;

[DefaultExecutionOrder(50)]
public class ArenaMatch : MonoBehaviour
{
    public ArenaMatchSettings settings;
    public ArenaParticipant[] participants;
    public Transform[] redSpawns;
    public Transform[] blueSpawns;
    public bool IsRunning { get; private set; }
    public int RedScore { get; private set; }
    public int BlueScore { get; private set; }
    public float TimeRemaining { get; private set; }
    public string Result { get; private set; } = "";
    public event System.Action MatchEnded;
    private int nextRedSpawn, nextBlueSpawn;

    void Start() => BeginMatch();
    void Update() => Tick(Time.deltaTime);

    public void BeginMatch()
    {
        RedScore = BlueScore = nextRedSpawn = nextBlueSpawn = 0;
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
        Transform spawn;
        if (participant.team == ArenaParticipant.Team.Red)
            spawn = redSpawns[nextRedSpawn++ % redSpawns.Length];
        else
            spawn = blueSpawns[nextBlueSpawn++ % blueSpawns.Length];
        participant.Respawn(spawn);
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
}
