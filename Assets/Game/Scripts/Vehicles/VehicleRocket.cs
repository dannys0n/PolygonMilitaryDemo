using System.Collections.Generic;
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
    Vector3 previous;
    bool exploded;
    float expiresAt=float.PositiveInfinity;
    public float RemainingLifetime => Mathf.Max(0,expiresAt-Time.time);
    public void LimitRemainingLifetime(float seconds, bool detonate = false)
    {
        expiresAt=Mathf.Min(expiresAt,Time.time+Mathf.Max(0,seconds));
        explodeOnExpiry|=detonate;
    }
    static readonly HashSet<VehicleRocket> active = new HashSet<VehicleRocket>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => active.Clear();
    void OnDisable() => active.Remove(this);
    public void Launch(Vehicle vehicle, GameObject shooter)
    {
        Launch(shooter, vehicle.transform, vehicle.Body.GetPointVelocity(transform.position));
    }
    public void Launch(GameObject shooter, Transform ignoredOwner, Vector3 inheritedVelocity)
    {
        owner = ignoredOwner;
        source = shooter;
        body = GetComponent<Rigidbody>();
        body.useGravity = useGravity;
        previous = transform.position;
        body.linearVelocity = transform.forward * speed + inheritedVelocity;
        var projectileColliders = GetComponentsInChildren<Collider>(true);
        if (owner)
            foreach (var c in owner.GetComponentsInChildren<Collider>(true))
                foreach (var projectileCollider in projectileColliders)
                    Physics.IgnoreCollision(projectileCollider, c);
        // Ballistic rounds are not interceptors; ignore other rounds as well as owner volleys.
        foreach (var other in active)
            if (other && (ballistic || other.ballistic || (owner && other.owner == owner)))
                foreach (var otherCollider in other.GetComponentsInChildren<Collider>(true))
                    foreach (var projectileCollider in projectileColliders)
                        Physics.IgnoreCollision(projectileCollider, otherCollider);
        active.Add(this);
        if(ballistic)
        {
            foreach(var renderer in GetComponentsInChildren<Renderer>(true))renderer.enabled=false;
            if(exhaust)exhaust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        else if (exhaust) exhaust.Play(true);
        expiresAt=Time.time+Mathf.Max(.01f,lifetime);
    }
    void StopExhaust()
    {
        if (!exhaust || ballistic) return;
        exhaust.transform.SetParent(null,true);
        exhaust.Stop(true,ParticleSystemStopBehavior.StopEmitting);
        Destroy(exhaust.gameObject,exhaust.main.startLifetime.constantMax+1f);
    }
    void Expire()
    {
        if (exploded) return;
        if (explodeOnExpiry) Explode(body ? body.position : transform.position, Vector3.up);
        else
        {
            StopExhaust();
            Destroy(gameObject);
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
        foreach (var hit in Physics.RaycastAll(previous,delta.normalized,delta.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if (!IgnoreHit(hit.collider) && hit.distance <= distance)
            {
                nearest = hit; distance = hit.distance; found = true;
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
    bool IgnoreHit(Collider collider)
    {
        if (collider.transform.IsChildOf(transform) || (owner && collider.transform.IsChildOf(owner))) return true;
        var round = collider.GetComponentInParent<VehicleRocket>();
        return round && (ballistic || round.ballistic || (owner && round.owner == owner));
    }
    void Explode(Vector3 point,Vector3 normal)
    {
        if (exploded)
            return;
        exploded = true;
        ExplosionDamage.Apply(point,normal,radius,infantryDamage,vehicleDamage,source,transform);
        if(showImpactVisual && impactEffect)
        {
        var effect = Instantiate(impactEffect,point+normal*.03f,Quaternion.FromToRotation(Vector3.forward,normal));
        effect.Play(true);
        float effectDuration=0;
        foreach(var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            var settings=particles.main;
            effectDuration=Mathf.Max(effectDuration,settings.startDelay.constantMax+settings.duration+settings.startLifetime.constantMax);
        }
        Destroy(effect.gameObject,effectDuration+1f);
        }
        StopExhaust();
        Destroy(gameObject);
    }
}
