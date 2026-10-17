using System.Collections.Generic;
using UnityEngine;
[RequireComponent(typeof(Rigidbody))]
public class GrenadeProjectile : MonoBehaviour
{
    GrenadeDefinition definition;GameObject owner;
    void OnDisable(){CancelInvoke();definition=null;owner=null;}
    public void Launch(GrenadeDefinition data,GameObject source,Vector3 velocity)
    {
        definition=data;owner=source;
        var body=GetComponent<Rigidbody>();body.isKinematic=false;body.linearVelocity=velocity;body.angularVelocity=Random.onUnitSphere*8;
        foreach(var collider in source.GetComponentsInChildren<Collider>(true))ProjectilePool.IgnoreCollision(this,GetComponent<Collider>(),collider);
        Invoke(nameof(Detonate),data.fuse);
    }
    void Detonate()
    {
        GetComponent<Collider>().enabled=false;
        ParticleEffectPool.Play(definition.effect,transform.position,Quaternion.Euler(definition.effectRotation),Vector3.one*definition.effectScale);
        if(definition.kind==GrenadeDefinition.GrenadeKind.Frag)
            ExplosionDamage.Apply(transform.position,Vector3.up,definition.radius,definition.infantryDamage,definition.vehicleDamage,owner,transform);
        else if(definition.kind==GrenadeDefinition.GrenadeKind.Flashbang)
        {
            var affected=new HashSet<SoldierGrenades>();
            foreach(var collider in Physics.OverlapSphere(transform.position,definition.radius,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide))
            {
                var actor=collider.GetComponentInParent<SoldierGrenades>();if(!actor||!affected.Add(actor)||actor.life.health.IsDead)continue;
                Vector3 eye=actor.life.look.aimCamera.transform.position,toFlash=transform.position-eye;bool blocked=false;
                foreach(var hit in Physics.RaycastAll(transform.position+Vector3.up*.1f,(eye-transform.position).normalized,Vector3.Distance(eye,transform.position),Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                    if(!hit.transform.IsChildOf(actor.transform)&&!hit.transform.IsChildOf(transform)&&!hit.collider.GetComponentInParent<SoldierHealth>()){blocked=true;break;}
                if(blocked)continue;
                float facing=Mathf.Clamp01(Vector3.Dot(actor.life.look.aimCamera.transform.forward,toFlash.normalized));
                float strength=Mathf.Clamp01(1-toFlash.magnitude/definition.radius)*Mathf.Lerp(.35f,1,facing);
                actor.ApplyFlash(strength,definition.flashDuration);
            }
        }
        ProjectilePool.Release(this);
    }
}
