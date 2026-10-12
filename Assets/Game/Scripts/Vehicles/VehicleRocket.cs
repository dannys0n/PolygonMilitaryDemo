using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class VehicleRocket : MonoBehaviour
{
    public ParticleSystem impactEffect, exhaust;
    public float launchClearance = .4f;
    public float speed = 65, radius = 4, infantryDamage = 140, vehicleDamage = 350, lifetime = 5;
    Rigidbody body;
    Transform owner;
    GameObject source;
    Vector3 previous;
    bool exploded;
    public void Launch(Vehicle vehicle, GameObject shooter)
    {
        Launch(shooter, vehicle.transform, vehicle.Body.linearVelocity);
    }
    public void Launch(GameObject shooter, Transform ignoredOwner, Vector3 inheritedVelocity)
    {
        owner = ignoredOwner;
        source = shooter;
        body = GetComponent<Rigidbody>();
        previous = transform.position;
        body.linearVelocity = transform.forward * speed + inheritedVelocity;
        var collider = GetComponent<Collider>();
        foreach (var c in owner.GetComponentsInChildren<Collider>())
            Physics.IgnoreCollision(collider, c);
        exhaust.Play(true);
        Invoke(nameof(Expire),lifetime);
    }
    void StopExhaust()
    {
        exhaust.transform.SetParent(null,true);
        exhaust.Stop(true,ParticleSystemStopBehavior.StopEmitting);
        Destroy(exhaust.gameObject,exhaust.main.startLifetime.constantMax+1f);
    }
    void Expire()
    {
        StopExhaust();
        Destroy(gameObject);
    }
    void FixedUpdate()
    {
        if (!body || exploded) return;
        Vector3 delta = body.position - previous;
        RaycastHit nearest = default;
        float distance = delta.magnitude;
        bool found = false;
        foreach (var hit in Physics.RaycastAll(previous,delta.normalized,delta.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(owner) && !hit.transform.IsChildOf(transform) && hit.distance <= distance)
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
        if (owner && !collision.transform.IsChildOf(owner))
        {
            var contact=collision.GetContact(0);
            Explode(contact.point,contact.normal);
        }
    }
    void Explode(Vector3 point,Vector3 normal)
    {
        if (exploded)
            return;
        exploded = true;
        ExplosionDamage.Apply(point,normal,radius,infantryDamage,vehicleDamage,source,transform);
        var effect = Instantiate(impactEffect,point+normal*.03f,Quaternion.FromToRotation(Vector3.forward,normal));
        effect.Play(true);
        float effectDuration=0;
        foreach(var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            var settings=particles.main;
            effectDuration=Mathf.Max(effectDuration,settings.startDelay.constantMax+settings.duration+settings.startLifetime.constantMax);
        }
        Destroy(effect.gameObject,effectDuration+1f);
        StopExhaust();
        Destroy(gameObject);
    }
}
