using System.Collections.Generic;
using UnityEngine;
public static class ExplosionDamage
{
    public static void Apply(Vector3 point,Vector3 normal,float radius,float infantryDamage,float vehicleDamage,GameObject source,Transform ignored)
    {
        var healths=new HashSet<SoldierHealth>();var vehicles=new HashSet<Vehicle>();
        foreach(var c in Physics.OverlapSphere(point,radius,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide))
        {
            var h=c.GetComponentInParent<SoldierHealth>();var v=c.GetComponentInParent<Vehicle>();
            if(!h&&!v)continue;
            Vector3 target=c.bounds.center;bool blocked=false;
            foreach(var hit in Physics.RaycastAll(point+normal*.15f,(target-point).normalized,Mathf.Max(0,Vector3.Distance(point,target)-.1f),Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                if(hit.collider!=c&&!hit.transform.IsChildOf(ignored)&&!hit.collider.GetComponentInParent<SoldierLife>()&&!hit.collider.GetComponentInParent<Vehicle>()){blocked=true;break;}
            if(blocked)continue;
            float falloff=1-Mathf.Clamp01(Vector3.Distance(point,c.ClosestPoint(point))/radius);
            if(h&&healths.Add(h))h.ApplyDamage(infantryDamage*falloff,source);
            if(v&&vehicles.Add(v))v.ApplyExplosiveDamage(vehicleDamage*falloff,source);
        }
    }
}
