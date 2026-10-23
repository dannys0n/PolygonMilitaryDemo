using UnityEngine;

public interface ISurfaceImpactReceiver
{
    void ReceiveImpact(Vector3 point, Vector3 normal);
}

public interface ISurfaceSpreadReceiver
{
    void ReceiveSpreadImpact(Vector3 point, Vector3 normal, Ray aim, float coneDegrees, float range);
}

public static class SurfaceImpact
{
    public static event System.Action<Vector3,float> Explosion;
    public static void ReportExplosion(Vector3 center,float radius) => Explosion?.Invoke(center,radius);

    public static void Report(Collider surface, Vector3 point, Vector3 normal)
    {
        if (surface && surface.TryGetComponent<ISurfaceImpactReceiver>(out var receiver))
            receiver.ReceiveImpact(point, normal);
    }

    public static void ReportSpread(Collider surface,Vector3 point,Vector3 normal,Ray aim,float coneDegrees,float range)
    {
        if(surface && surface.TryGetComponent<ISurfaceSpreadReceiver>(out var receiver))
            receiver.ReceiveSpreadImpact(point,normal,aim,coneDegrees,range);
        else Report(surface,point,normal);
    }
}
