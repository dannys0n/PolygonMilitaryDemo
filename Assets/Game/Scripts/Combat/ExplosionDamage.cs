using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
public static class ExplosionDamage
{
    sealed class QueryContext
    {
        public readonly HashSet<SoldierHealth> healths=new HashSet<SoldierHealth>();
        public readonly Dictionary<Vehicle,float> vehicles=new Dictionary<Vehicle,float>();
        public Collider[] overlaps=new Collider[32];
        public RaycastHit[] hits=new RaycastHit[16];
        public void Clear(){healths.Clear();vehicles.Clear();}
    }
    // Damage callbacks can trigger another explosion: rent a context per invocation.
    static readonly ObjectPool<QueryContext> contexts=new ObjectPool<QueryContext>(
        ()=>new QueryContext(),actionOnRelease:context=>context.Clear(),maxSize:16);
    public static void Apply(Vector3 point,Vector3 normal,float radius,float infantryDamage,float vehicleDamage,GameObject source,Transform ignored,Sprite weaponIcon=null)
    {
        var context=contexts.Get();
        try
        {
            var healths=context.healths;var vehicles=context.vehicles;
            int layers=ProjectileCollision.WithoutProjectiles(Physics.DefaultRaycastLayers);
            int count=CombatQueries.OverlapSphere(point,radius,layers,QueryTriggerInteraction.Collide,ref context.overlaps);
            for(int i=0;i<count;i++)
            {
                var c=context.overlaps[i];
                var h=c.GetComponentInParent<SoldierHealth>();var v=c.GetComponentInParent<Vehicle>();
                if(!h&&!v)continue;
                if(h&&healths.Contains(h)&&!v)continue;
                Vector3 target=c.bounds.center;bool blocked=false;
                int hitCount=CombatQueries.Raycast(new Ray(point+normal*.15f,(target-point).normalized),
                    Mathf.Max(0,Vector3.Distance(point,target)-.1f),layers,QueryTriggerInteraction.Ignore,ref context.hits);
                for(int j=0;j<hitCount;j++)
                {
                    var hit=context.hits[j];
                    if(hit.collider!=c&&!hit.transform.IsChildOf(ignored)&&!hit.collider.GetComponentInParent<SoldierLife>()&&!hit.collider.GetComponentInParent<Vehicle>()){blocked=true;break;}
                }
                if(blocked)continue;
                float falloff=1-Mathf.Clamp01(Vector3.Distance(point,c.ClosestPoint(point))/radius);
                if(h&&healths.Add(h))h.ApplyDamage(infantryDamage*falloff,source,weaponIcon,point);
                // Compound hulls receive one hit using the closest exposed collider, independent of overlap order.
                if(v && (!vehicles.TryGetValue(v,out float strongest) || falloff>strongest))vehicles[v]=falloff;
            }
            foreach(var hit in vehicles)hit.Key.ApplyExplosiveDamage(vehicleDamage*hit.Value,source,weaponIcon);
        }
        finally { contexts.Release(context); }
    }
}
