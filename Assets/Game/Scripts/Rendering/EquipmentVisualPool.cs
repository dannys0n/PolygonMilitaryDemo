using System.Collections.Generic;
using UnityEngine;

// A bounded, scene-owned cache for disposable equipment visuals. Inventory and
// ammunition remain on the actor; no inactive arsenal is preloaded per soldier.
public static class EquipmentVisualPool
{
    const int MaximumIdle = 64, MaximumPerPrefab = 2;
    static readonly Dictionary<GameObject, Stack<PooledEquipmentVisual>> idle = new();
    static Transform root;
    static int idleCount;
    public static int IdleCount => root ? idleCount : 0;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { idle.Clear(); root = null; idleCount = 0; }

    public static GameObject Rent(GameObject prefab, Transform parent)
    {
        if (!Application.isPlaying) return Object.Instantiate(prefab, parent, false);
        if (!root)
        {
            Reset();
            root = new GameObject("Equipment visual pool").transform;
            root.gameObject.SetActive(false);
        }
        PooledEquipmentVisual item = null;
        if (idle.TryGetValue(prefab, out var stack))
        {
            while (stack.Count > 0 && !item) { item = stack.Pop(); idleCount--; }
            if(stack.Count==0)idle.Remove(prefab);
        }
        if (!item)
        {
            var instance = Object.Instantiate(prefab, root, false);
            item = instance.AddComponent<PooledEquipmentVisual>();
            item.Initialize(prefab);
        }
        item.gameObject.SetActive(false);
        item.transform.SetParent(parent, false);
        item.Restore();
        item.gameObject.SetActive(true);
        return item.gameObject;
    }

    public static void Return(GameObject instance)
    {
        if (!instance) return;
        instance.SetActive(false);
        if (!Application.isPlaying) { Object.DestroyImmediate(instance); return; }
        if (!root || !instance.TryGetComponent<PooledEquipmentVisual>(out var item))
        { instance.transform.SetParent(null);Object.Destroy(instance); return; }
        if (item.parked) return;
        idle.TryGetValue(item.prefab, out var stack);
        if (idleCount >= MaximumIdle || (stack != null && stack.Count >= MaximumPerPrefab))
        { instance.transform.SetParent(null);Object.Destroy(instance); return; }
        if(stack==null)idle.Add(item.prefab,stack=new());
        item.parked = true;
        instance.transform.SetParent(root, false);
        stack.Push(item); idleCount++;
    }
}
