using UnityEngine;
using UnityEngine.Pool;

public sealed class PooledParticleEffect : MonoBehaviour
{
    public ParticleSystem Particles { get; private set; }
    ObjectPool<PooledParticleEffect> pool;
    ParticleSystem[] systems;
    TrailRenderer[] trails;
    bool[] loops;
    Vector3 defaultScale;
    float duration, tailDuration, returnDeadline, nextCheck;
    bool playing, persistent;

    public void Initialize(ParticleSystem particles, ObjectPool<PooledParticleEffect> owner)
    {
        Particles = particles; pool = owner;
        defaultScale = transform.localScale;
        systems = GetComponentsInChildren<ParticleSystem>(true);
        trails = GetComponentsInChildren<TrailRenderer>(true);
        loops = new bool[systems.Length];
        Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            loops[i] = main.loop;
            // A prefab's Destroy/Disable stop action would invalidate a pooled instance.
            main.stopAction = ParticleSystemStopAction.None;
            float speed = Mathf.Max(.01f, main.simulationSpeed);
            float trail = systems[i].trails.enabled ? systems[i].trails.lifetime.constantMax : 0;
            duration = Mathf.Max(duration, (main.startDelay.constantMax + main.duration + main.startLifetime.constantMax + trail) / speed);
            tailDuration = Mathf.Max(tailDuration, (main.startLifetime.constantMax + trail) / speed);
        }
        foreach (var trail in trails) tailDuration = Mathf.Max(tailDuration, trail.time);
        duration = Mathf.Max(duration, tailDuration);
    }

    public void Play(Vector3 position, Quaternion rotation, Vector3? scale, bool persistent)
    {
        this.persistent = persistent;
        Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = scale ?? defaultScale;
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.loop = persistent && loops[i];
        }
        foreach (var trail in trails) trail.Clear();
        returnDeadline = persistent ? float.PositiveInfinity : Time.time + duration + 1;
        nextCheck = Time.time + .1f;
        playing = true;
        gameObject.SetActive(true);
        Particles.Play(true);
    }

    public void Stop()
    {
        if (!playing) return;
        persistent = false;
        Particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        returnDeadline = Mathf.Min(returnDeadline, Time.time + tailDuration + 1);
    }

    void LateUpdate()
    {
        if (!playing || persistent || Time.time < nextCheck) return;
        nextCheck = Time.time + .1f;
        bool alive = false;
        foreach (var system in systems)
            if (system.gameObject.activeInHierarchy && system.IsAlive(false)) { alive = true; break; }
        if (alive && Time.time < returnDeadline) return;
        // Deadline also reclaims effects paused by off-screen particle culling.
        playing = false;
        Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (var trail in trails) trail.Clear();
        pool.Release(this);
    }
}
