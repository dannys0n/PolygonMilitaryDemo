using System;
using UnityEngine;

// Sections of an authored reload montage. Magazine reloads leave this disabled.
[Serializable]
public struct RoundReloadTiming
{
    public bool enabled;
    [Min(.01f)] public float openingDuration;
    [Min(.01f)] public float insertionDuration;
    [Min(.01f)] public float closingDuration;
    [Range(0f, 1f)] public float insertAt;
    [Min(1)] public int bakedInsertions;
    public float AuthoredDuration => openingDuration + insertionDuration * bakedInsertions + closingDuration;
    public bool IsValid => enabled && openingDuration > 0f && insertionDuration > 0f && closingDuration > 0f && bakedInsertions > 0;
}

// Gameplay owns the clock and ammunition. Presentation only follows section changes.
public sealed class RoundReloadClock
{
    public enum Phase { Opening, Inserting, Closing, Complete }
    public Phase Current { get; private set; }
    public bool StopRequested { get; private set; }
    public float NormalizedTime { get; private set; }
    public float Duration { get; private set; }
    RoundReloadTiming timing;
    float started, sectionStart, scale;
    bool inserted;

    public void Begin(RoundReloadTiming settings, float duration, int rounds, float now)
    {
        timing = settings;
        scale = Mathf.Max(.01f, duration) / timing.AuthoredDuration;
        Duration = (timing.openingDuration + timing.insertionDuration * Mathf.Max(1, rounds) + timing.closingDuration) * scale;
        started = sectionStart = now;
        Current = Phase.Opening;
        StopRequested = inserted = false;
        NormalizedTime = 0f;
    }

    public void RequestStop() => StopRequested = true;
    public float Progress(float now) => Mathf.Clamp01((now - started) / Mathf.Max(.01f, Duration));

    // Catch up missed boundaries without dropping inserts during a slow frame.
    // Return true only when animation playback needs to jump to another section.
    public bool Tick(float now, SoldierWeaponLoadout.WeaponState ammo, SoldierWeaponDefinition definition)
    {
        bool changed = false;
        while (Current != Phase.Complete)
        {
            float length = (Current == Phase.Opening ? timing.openingDuration
                : Current == Phase.Inserting ? timing.insertionDuration : timing.closingDuration) * scale;
            if (Current == Phase.Inserting && !inserted && now >= sectionStart + length * timing.insertAt)
            {
                ammo.ReloadRounds(definition, 1);
                inserted = true;
            }
            if (now < sectionStart + length) break;
            sectionStart += length;
            changed = true;
            if (Current == Phase.Closing) Current = Phase.Complete;
            else if (Current == Phase.Inserting && (StopRequested || !ammo.CanReload(definition)))
            {
                Current = Phase.Closing;
                Duration = sectionStart - started + timing.closingDuration * scale;
            }
            else { Current = Phase.Inserting; inserted = false; }
        }
        float offset = Current == Phase.Opening ? 0f : Current == Phase.Inserting ? timing.openingDuration
            : timing.openingDuration + timing.insertionDuration * timing.bakedInsertions;
        NormalizedTime = Current == Phase.Complete ? 1f
            : (offset + (now - sectionStart) / scale) / timing.AuthoredDuration;
        return changed;
    }
}
