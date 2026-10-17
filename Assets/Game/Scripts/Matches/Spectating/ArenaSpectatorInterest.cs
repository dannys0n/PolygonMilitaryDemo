using System.Collections.Generic;
using UnityEngine;

// A lightweight director; camera ownership and death transitions remain in the spectator.
public sealed class ArenaSpectatorInterest : MonoBehaviour
{
    public ArenaAISpectator spectator;
    public bool Enabled { get; private set; }
    readonly Dictionary<GameObject, float> damagedAt = new();
    readonly Dictionary<ArenaParticipant, float> watchedAt = new();
    ArenaParticipant tracked;
    int lastType = -1, consecutive, perspectiveSwaps;
    float followedAt, lastActionAt, nextCheck;

    void OnEnable() => CombatEvents.DamageConfirmed += OnDamage;
    void OnDisable() { CombatEvents.DamageConfirmed -= OnDamage; Enabled = false; }
    void OnDamage(CombatEvents.ConfirmedDamage hit)
    {
        if (!Enabled) return;
        Remember(hit.target); Remember(hit.attacker);
    }
    void Remember(GameObject source)
    {
        if (!source) return;
        var actor = source.GetComponentInParent<ArenaParticipant>();
        var vehicle = source.GetComponentInParent<Vehicle>();
        if (actor) damagedAt[actor.gameObject] = Time.time;
        if (vehicle) damagedAt[vehicle.gameObject] = Time.time;
    }
    public static int UnitType(ArenaParticipant actor)
    {
        var seat = actor.GetComponent<VehicleOccupant>();
        return seat && seat.IsMounted && seat.MountedVehicle ? (seat.MountedVehicle.IsAircraft ? 2 : 1) : 0;
    }
    static Vehicle Mount(ArenaParticipant actor)
    {
        var seat = actor ? actor.GetComponent<VehicleOccupant>() : null;
        return seat && seat.IsMounted ? seat.MountedVehicle : null;
    }
    float ActionAt(ArenaParticipant actor)
    {
        float time = actor.LastShotTime;
        if (damagedAt.TryGetValue(actor.gameObject, out float damage)) time = Mathf.Max(time, damage);
        var vehicle = Mount(actor);
        if (vehicle)
        {
            if (damagedAt.TryGetValue(vehicle.gameObject, out damage)) time = Mathf.Max(time, damage);
            // Watching the pilot should still recognize the gunner's combat.
            foreach (var crew in spectator.match.participants)
                if (crew && Mount(crew) == vehicle) time = Mathf.Max(time, crew.LastShotTime);
        }
        return time;
    }
    public void SetEnabled(bool value)
    {
        Enabled = value;
        if (!value) return;
        tracked = null; lastType = -1; consecutive = perspectiveSwaps = 0;
        watchedAt.Clear(); damagedAt.Clear(); nextCheck = 0;
        if (spectator.FollowedCharacter) RecordFollow(spectator.FollowedCharacter);
    }
    public void EndFollowOnDeath()
    {
        // A respawn can reuse the same participant. Its next life is a new
        // follow, while the camera cadence and unit-type streak carry on.
        tracked = null;
    }
    public bool RecordFollow(ArenaParticipant actor)
    {
        if (!Enabled || !actor || actor == tracked) return false;
        // Initial acquisition is not a swap. Keep this cadence independent of
        // unit-type streaks, including switches after the death camera finishes.
        bool alternatePerspective = lastType >= 0 && ++perspectiveSwaps % 3 == 0;
        tracked = actor;
        int type = UnitType(actor);
        consecutive = type == lastType ? consecutive + 1 : 1;
        lastType = type;
        followedAt = lastActionAt = Time.time;
        watchedAt[actor] = Time.time;
        return alternatePerspective;
    }
    public ArenaParticipant Choose(float now)
    {
        ArenaParticipant best = null;
        float bestScore = float.NegativeInfinity;
        bool differentTypeExists = false, differentVehicleExists = false;
        var current = spectator.FollowedCharacter;
        var currentMount = Mount(current);
        int consecutiveLimit = lastType == 0 ? 5 : 3;
        foreach (var actor in spectator.match.participants)
        {
            if (actor == current || !spectator.CanFollowFiltered(actor)) continue;
            if (UnitType(actor) != lastType) differentTypeExists = true;
            if (!currentMount || Mount(actor) != currentMount) differentVehicleExists = true;
        }
        foreach (var actor in spectator.match.participants)
        {
            if (actor == current || !spectator.CanFollowFiltered(actor)) continue;
            if (consecutive >= consecutiveLimit && differentTypeExists && UnitType(actor) == lastType) continue;
            if (differentVehicleExists && currentMount && Mount(actor) == currentMount) continue;
            float activityAge = Mathf.Max(0, now - ActionAt(actor));
            float novelty = watchedAt.TryGetValue(actor, out float watched) ? Mathf.Clamp(now - watched, 0, 60) : 60;
            float score = (activityAge <= 3 ? 100 + (3 - activityAge) * 5 : 0) + novelty;
            if (score > bestScore) { best = actor; bestScore = score; }
        }
        return best;
    }
    public void Tick(float now)
    {
        if (!Enabled || !spectator.IsSpectating || spectator.ControlsBlocked || spectator.IsWatchingDeath) return;
        var current = spectator.FollowedCharacter;
        if (current && current != tracked) RecordFollow(current);
        if (current)
        {
            lastActionAt = Mathf.Max(lastActionAt, ActionAt(current));
            if (now - followedAt < 15 && now - lastActionAt < 3) return;
        }
        var next = Choose(now);
        if (next) spectator.Follow(next);
        // With only one eligible unit, keep watching rather than flickering into free camera.
    }
    void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + .25f;
        Tick(Time.time);
    }
}
