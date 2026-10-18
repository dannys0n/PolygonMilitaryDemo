using System;
using UnityEngine;

// NonAlloc queries do not promise the nearest hits when the buffer is full.
// Grow and retry before interpreting the results, preserving dense-scene correctness.
public static class CombatQueries
{
    public static int Raycast(Ray ray, float distance, int layers, QueryTriggerInteraction triggers, ref RaycastHit[] hits)
    {
        if(hits==null || hits.Length==0)hits=new RaycastHit[8];
        int count;
        while ((count = Physics.RaycastNonAlloc(ray, hits, distance, layers, triggers)) == hits.Length)
            Array.Resize(ref hits, hits.Length * 2);
        return count;
    }

    public static int SphereCast(Vector3 origin, float radius, Vector3 direction, float distance, int layers, QueryTriggerInteraction triggers, ref RaycastHit[] hits)
    {
        if(hits==null || hits.Length==0)hits=new RaycastHit[8];
        int count;
        while((count=Physics.SphereCastNonAlloc(origin,radius,direction,hits,distance,layers,triggers))==hits.Length)
            Array.Resize(ref hits,hits.Length*2);
        return count;
    }
    public static int BoxCast(Vector3 origin, Vector3 halfExtents, Vector3 direction, Quaternion orientation, float distance, int layers, QueryTriggerInteraction triggers, ref RaycastHit[] hits)
    {
        if(hits==null || hits.Length==0)hits=new RaycastHit[8];
        int count;
        while((count=Physics.BoxCastNonAlloc(origin,halfExtents,direction,hits,orientation,distance,layers,triggers))==hits.Length)
            Array.Resize(ref hits,hits.Length*2);
        return count;
    }

    public static int OverlapSphere(Vector3 center, float radius, int layers, QueryTriggerInteraction triggers, ref Collider[] hits)
    {
        if(hits==null || hits.Length==0)hits=new Collider[8];
        int count;
        while ((count = Physics.OverlapSphereNonAlloc(center, radius, hits, layers, triggers)) == hits.Length)
            Array.Resize(ref hits, hits.Length * 2);
        return count;
    }
}
