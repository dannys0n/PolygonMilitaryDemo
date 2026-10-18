using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

// Visual effects have their own lifetime; they must not be tied to a recycled projectile.
public static class ParticleEffectPool
{
    const int MaximumIdlePerPrefab = 64;
    static readonly Dictionary<ParticleSystem, ObjectPool<PooledParticleEffect>> pools = new();
    static Transform root;
    static Camera[] cameras = new Camera[8];
    static readonly Plane[] planes = new Plane[6];
    const int MaximumTransientPerPrefab = 64;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { pools.Clear(); root = null; }

    public static ParticleSystem Play(ParticleSystem prefab, Vector3 position, Quaternion rotation,
        Vector3? scale = null, bool persistent = false)
    {
        if (!prefab) return null;
        if (!root)
        {
            pools.Clear();
            root = new GameObject("Particle effect pools").transform;
        }
        if (!pools.TryGetValue(prefab, out var pool))
        {
            ObjectPool<PooledParticleEffect> newPool = null;
            newPool = new ObjectPool<PooledParticleEffect>(
                () =>
                {
                    var particles = Object.Instantiate(prefab, root);
                    var effect = particles.gameObject.AddComponent<PooledParticleEffect>();
                    effect.Initialize(particles, newPool);
                    return effect;
                },
                actionOnRelease: effect => { if (effect) effect.gameObject.SetActive(false); },
                // Scene teardown may already have destroyed the pooled GameObjects
                // before Unity's pool manager clears its remaining references.
                actionOnDestroy: effect => { if (effect) Object.Destroy(effect.gameObject); },
                collectionCheck: true, defaultCapacity: 8, maxSize: MaximumIdlePerPrefab);
            pools.Add(prefab, pool = newPool);
        }
        var instance = pool.Get();
        instance.Play(position, rotation, scale, persistent);
        return instance.Particles;
    }

    // Optional short-lived feedback. Callers must tolerate null: simulation never
    // depends on whether an offscreen impact or muzzle flash receives a visual.
    public static ParticleSystem PlayTransient(ParticleSystem prefab, Vector3 position, Quaternion rotation)
    {
        if (!prefab || !Visible(position)) return null;
        if (root && pools.TryGetValue(prefab, out var pool) && pool.CountActive >= MaximumTransientPerPrefab) return null;
        return Play(prefab, position, rotation);
    }

    static bool Visible(Vector3 position)
    {
        int count = Camera.allCamerasCount;
        if (count == 0) return true; // Headless simulation and isolated tests.
        if (cameras.Length < count) System.Array.Resize(ref cameras, Mathf.NextPowerOfTwo(count));
        count = Camera.GetAllCameras(cameras);
        var bounds = new Bounds(position, Vector3.one * 40f);
        for (int i = 0; i < count; i++)
        {
            var camera = cameras[i];
            if (!camera || !camera.isActiveAndEnabled || camera.cameraType != CameraType.Game) continue;
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            if (GeometryUtility.TestPlanesAABB(planes, bounds)) return true;
        }
        return false;
    }

    // Stop emitting now, but preserve the smoke/particle tail until it has faded.
    public static void Stop(ParticleSystem effect)
    {
        if (effect && effect.TryGetComponent<PooledParticleEffect>(out var pooled)) pooled.Stop();
    }
}
