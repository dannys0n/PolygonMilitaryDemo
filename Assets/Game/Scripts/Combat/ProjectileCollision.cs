using UnityEngine;

public static class ProjectileCollision
{
    public const string LayerName = "Projectiles";
    static int layer = -1;
    public static int Layer => layer;
    public static int WithoutProjectiles(int layers) => layer < 0 ? layers : layers & ~(1 << layer);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Configure()
    {
        layer = LayerMask.NameToLayer(LayerName);
        if (layer >= 0) Physics.IgnoreLayerCollision(layer, layer, true);
    }

    public static void ConfigureColliders(Collider[] colliders)
    {
        foreach (var collider in colliders)
        {
            // Damage is swept/trigger-driven. Owner immunity is checked in IgnoreHit,
            // so owner pairs do not need per-shot PhysX bookkeeping either.
            collider.isTrigger = true;
            if (layer >= 0) collider.gameObject.layer = layer;
        }
    }
}
