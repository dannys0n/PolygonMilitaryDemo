using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class VehicleRocket : MonoBehaviour
{
    public ParticleSystem impactEffect, exhaust;
    public float launchClearance = .4f;
    public float speed = 65, radius = 4, infantryDamage = 140, vehicleDamage = 350, lifetime = 5;
    Rigidbody body;
    Vehicle owner;
    GameObject source;
    Vector3 previous;
    bool exploded;
    public void Launch(Vehicle vehicle, GameObject shooter)
    {
        owner = vehicle;
        source = shooter;
        body = GetComponent<Rigidbody>();
        previous = transform.position;
        body.linearVelocity = transform.forward * speed + vehicle.Body.linearVelocity;
        var collider = GetComponent<Collider>();
        foreach (var c in vehicle.GetComponentsInChildren<Collider>())
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
        Vector3 delta = body.position - previous;
        RaycastHit nearest = default;
        float distance = delta.magnitude;
        bool found = false;
        foreach (var hit in Physics.RaycastAll(previous,delta.normalized,delta.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(owner.transform) && !hit.transform.IsChildOf(transform) && hit.distance <= distance)
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
        if (owner && !collision.transform.IsChildOf(owner.transform))
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
        var healths = new HashSet<SoldierHealth>();
        var vehicles = new HashSet<Vehicle>();
        foreach (var c in Physics.OverlapSphere(point, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            var h = c.GetComponentInParent<SoldierHealth>();
            var v = c.GetComponentInParent<Vehicle>();
            // Cover blocks blast damage; the small origin offset avoids hitting the impact face itself.
            Vector3 target = c.bounds.center;
            bool blocked = false;
            foreach (var hit in Physics.RaycastAll(point + normal * .15f, (target - point).normalized, Vector3.Distance(point, target) - .1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (hit.collider != c && !hit.transform.IsChildOf(transform) && !hit.collider.GetComponentInParent<SoldierLife>() && !hit.collider.GetComponentInParent<Vehicle>())
                {
                    blocked = true;
                    break;
                }
            if (blocked)
                continue;
            float falloff = 1 - Mathf.Clamp01(Vector3.Distance(point, c.ClosestPoint(point)) / radius);
            if (h && healths.Add(h))
                h.ApplyDamage(infantryDamage * falloff, source);
            if (v && vehicles.Add(v))
                v.ApplyDamage(vehicleDamage * falloff, source);
        }
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
