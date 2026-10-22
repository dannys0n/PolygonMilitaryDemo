using UnityEngine;

public interface ISurfaceImpactReceiver
{
    void ReceiveImpact(Vector3 point, Vector3 normal);
}

public static class SurfaceImpact
{
    public static void Report(Collider surface, Vector3 point, Vector3 normal)
    {
        if (surface && surface.TryGetComponent<ISurfaceImpactReceiver>(out var receiver))
            receiver.ReceiveImpact(point, normal);
    }
}
