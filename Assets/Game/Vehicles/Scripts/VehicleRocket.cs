using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class VehicleRocket : MonoBehaviour
{
    public ParticleSystem impactEffect;
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
        Destroy(gameObject, lifetime);
    }
    void FixedUpdate()
    {
        Vector3 delta = body.position - previous;
        foreach (var hit in Physics.RaycastAll(previous, delta.normalized, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(owner.transform) && !hit.transform.IsChildOf(transform))
            {
                Explode(hit.point);
                return;
            }
        previous = body.position;
    }
    void OnCollisionEnter(Collision collision)
    {
        if (owner && !collision.transform.IsChildOf(owner.transform))
            Explode(collision.GetContact(0).point);
    }
    void Explode(Vector3 point)
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
            foreach (var hit in Physics.RaycastAll(point + Vector3.up * .15f, (target - point).normalized, Vector3.Distance(point, target) - .1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
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
        var effect = Instantiate(impactEffect, point, Quaternion.identity);
        effect.Play(true);
        Destroy(effect.gameObject, 2f);
        Destroy(gameObject);
    }
}
