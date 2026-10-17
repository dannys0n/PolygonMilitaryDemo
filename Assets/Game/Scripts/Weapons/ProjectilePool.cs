using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Scene-owned pools: idle objects are bounded, active shots are never stolen.
public static class ProjectilePool
{
    const int MaximumIdlePerPrefab = 128;
    static readonly Dictionary<GameObject, Stack<PooledProjectile>> pools = new();
    static Transform root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { pools.Clear(); root = null; }

    public static T Spawn<T>(T prefab, Vector3 position, Quaternion rotation) where T : Component
    {
        if (!root)
        {
            pools.Clear();
            root = new GameObject("Projectile pools").transform;
        }
        if (!pools.TryGetValue(prefab.gameObject, out var pool))
            pools.Add(prefab.gameObject, pool = new Stack<PooledProjectile>());
        PooledProjectile instance = null;
        while (pool.Count > 0 && !instance) instance = pool.Pop();
        if (!instance)
        {
            var clone = Object.Instantiate(prefab.gameObject, position, rotation, root);
            instance = clone.AddComponent<PooledProjectile>();
            instance.Initialize(prefab.gameObject);
        }
        instance.Prepare(position, rotation);
        return instance.GetComponent<T>();
    }

    internal static void Store(PooledProjectile instance, GameObject prefab)
    {
        instance.gameObject.SetActive(false);
        if (root && pools.TryGetValue(prefab, out var pool) && pool.Count < MaximumIdlePerPrefab)
            pool.Push(instance);
        else Object.Destroy(instance.gameObject);
    }

    public static void Release(Component projectile, float tailSeconds = 0)
    {
        if (projectile.TryGetComponent<PooledProjectile>(out var pooled)) pooled.Release(tailSeconds);
        else Object.Destroy(projectile.gameObject, tailSeconds);
    }

    public static void IgnoreCollision(Component projectile, Collider first, Collider second)
    {
        if (Physics.GetIgnoreCollision(first, second)) return;
        Physics.IgnoreCollision(first, second);
        if (projectile.TryGetComponent<PooledProjectile>(out var pooled)) pooled.TrackIgnore(first, second);
    }
}
