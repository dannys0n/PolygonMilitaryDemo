using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class VehicleRocket : MonoBehaviour
{
    public ParticleSystem impactEffect, exhaust;
    public float launchClearance = .4f;
    public bool useGravity;
    [Tooltip("Ballistic cannon round, not a missile or launcher rocket.")]
    public bool ballistic;
    [System.NonSerialized] public bool showImpactVisual=true;
    [Tooltip("Detonate at the end of flight; intended for guided missiles with self-destruct.")]
    public bool explodeOnExpiry;
    public float speed = 65, radius = 4, infantryDamage = 140, vehicleDamage = 350, lifetime = 5;
    Rigidbody body;
    Transform owner;
    GameObject source;
    Sprite damageIcon;
    Vector3 previous;
    bool exploded;
    float expiresAt=float.PositiveInfinity;
    bool defaultExplodeOnExpiry;
    bool cached;
    Collider[] projectileColliders;
    Renderer[] projectileRenderers;
    RaycastHit[] sweepHits = new RaycastHit[8];
    float exhaustTail;
    void Awake()
    {
        if(cached)return;
        cached=true;
        defaultExplodeOnExpiry=explodeOnExpiry;
        projectileColliders=GetComponentsInChildren<Collider>(true);
        projectileRenderers=GetComponentsInChildren<Renderer>(true);
        if(exhaust)
            foreach(var particles in exhaust.GetComponentsInChildren<ParticleSystem>(true))
                exhaustTail=Mathf.Max(exhaustTail,particles.main.startLifetime.constantMax);
        ProjectileCollision.ConfigureColliders(projectileColliders);
    }
    public void ResetForReuse()
    {
        Awake();
        exploded=false; owner=null; source=null; damageIcon=null;
        expiresAt=float.PositiveInfinity;
        explodeOnExpiry=defaultExplodeOnExpiry; showImpactVisual=true;
    }
    public float RemainingLifetime => Mathf.Max(0,expiresAt-Time.time);
    public void LimitRemainingLifetime(float seconds, bool detonate = false)
    {
        expiresAt=Mathf.Min(expiresAt,Time.time+Mathf.Max(0,seconds));
        explodeOnExpiry|=detonate;
    }
    public void Launch(Vehicle vehicle, GameObject shooter, Sprite weaponIcon = null)
    {
        Launch(shooter, vehicle.transform, vehicle.Body.GetPointVelocity(transform.position), weaponIcon);
    }
    public void Launch(GameObject shooter, Transform ignoredOwner, Vector3 inheritedVelocity, Sprite weaponIcon = null)
    {
        owner = ignoredOwner;
        source = shooter;
        damageIcon = weaponIcon;
        body = GetComponent<Rigidbody>();
        body.useGravity = useGravity;
        previous = transform.position;
        body.linearVelocity = transform.forward * speed + inheritedVelocity;
        // Damage comes from the swept hit/explosion, never a high-speed rigidbody impulse.
        // Solid cannon rounds could shove an aircraft hard enough to cause a second crash kill.
        if(ballistic)
        {
            foreach(var renderer in projectileRenderers)renderer.enabled=false;
            if(exhaust)exhaust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        else if (exhaust) exhaust.Play(true);
        expiresAt=Time.time+Mathf.Max(.01f,lifetime);
    }
    void ReturnToPool()
    {
        float tail=0;
        if (exhaust && !ballistic)
        {
            exhaust.Stop(true,ParticleSystemStopBehavior.StopEmitting);
            tail=exhaustTail;
        }
        ProjectilePool.Release(this,tail);
    }
    void Expire()
    {
        if (exploded) return;
        if (explodeOnExpiry) Explode(body ? body.position : transform.position, Vector3.up);
        else
        {
            exploded=true;
            ReturnToPool();
        }
    }
    void FixedUpdate()
    {
        if (!body || exploded) return;
        if(Time.time>=expiresAt){Expire();return;}
        Vector3 delta = body.position - previous;
        RaycastHit nearest = default;
        float distance = delta.magnitude;
        bool found = false;
        int count=CombatQueries.Raycast(new Ray(previous,delta.normalized),distance,
            ProjectileCollision.WithoutProjectiles(Physics.DefaultRaycastLayers),QueryTriggerInteraction.Ignore,ref sweepHits);
        for(int i=0;i<count;i++)
        {
            var hit=sweepHits[i];
            if (!IgnoreHit(hit.collider) && hit.distance <= distance)
            {
                nearest = hit; distance = hit.distance; found = true;
            }
        }
        if (found)
        {
            Explode(nearest.point,nearest.normal);
            return;
        }
        previous = body.position;
    }
    void OnCollisionEnter(Collision collision)
    {
        if (body && !IgnoreHit(collision.collider) && collision.contactCount > 0)
        {
            var contact=collision.GetContact(0);
            Explode(contact.point,contact.normal);
        }
    }
    void OnTriggerEnter(Collider collider)
    {
        if(!body || exploded || IgnoreHit(collider))return;
        if(collider.isTrigger && !collider.GetComponent<SoldierHitRegion>())return;
        // ClosestPoint does not support terrain or concave scene meshes. Recover
        // their actual surface from this step's travel instead of logging on impact.
        bool meshSurface=collider is TerrainCollider || collider is MeshCollider mesh && !mesh.convex;
        Vector3 point=meshSurface?body.position:collider.ClosestPoint(body.position);
        Vector3 normal=body.position-point;
        Vector3 travel=body.position-previous;
        if(meshSurface && travel.sqrMagnitude>.000001f
            && collider.Raycast(new Ray(previous,travel.normalized),out var hit,travel.magnitude+.05f))
        {point=hit.point;normal=hit.normal;}
        if(normal.sqrMagnitude<.0001f)normal=-body.linearVelocity;
        Explode(point,normal.sqrMagnitude>.0001f?normal.normalized:Vector3.up);
    }
    bool IgnoreHit(Collider collider)
    {
        return collider.transform.IsChildOf(transform) || (owner && collider.transform.IsChildOf(owner));
    }
    void Explode(Vector3 point,Vector3 normal)
    {
        if (exploded)
            return;
        exploded = true;
        ExplosionDamage.Apply(point,normal,radius,infantryDamage,vehicleDamage,source,transform,damageIcon);
        if(showImpactVisual && impactEffect)
        {
            ParticleEffectPool.PlayTransient(impactEffect,point+normal*.03f,Quaternion.FromToRotation(Vector3.forward,normal));
        }
        ReturnToPool();
    }
}
