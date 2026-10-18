using System.Collections.Generic;
using UnityEngine;

// High-rate cannon rounds need swept damage, not a GameObject/PhysX body per shot.
// Missiles, grenades and guided rockets retain their existing pooled objects.
public sealed class BallisticRounds : MonoBehaviour
{
    struct Round
    {
        public Vector3 position, velocity;
        public VehicleRocket settings;
        public Transform owner;
        public GameObject source;
        public Sprite icon;
        public float expires;
        public bool impactVisual;
    }
    static BallisticRounds instance;
    readonly List<Round> rounds = new List<Round>(256);
    RaycastHit[] hits = new RaycastHit[16];
    public static int ActiveCount => instance ? instance.rounds.Count : 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => instance = null;

    public static void Fire(VehicleRocket settings, Vector3 position, Vector3 direction,
        Vector3 inheritedVelocity, Transform owner, GameObject source, Sprite icon, bool impactVisual)
    {
        if (!instance) instance = new GameObject("Ballistic rounds").AddComponent<BallisticRounds>();
        instance.rounds.Add(new Round {
            settings = settings, position = position,
            velocity = direction.normalized * settings.speed + inheritedVelocity,
            owner = owner, source = source, icon = icon, impactVisual = impactVisual,
            expires = Time.time + Mathf.Max(.01f, settings.lifetime)
        });
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        int layers = ProjectileCollision.WithoutProjectiles(Physics.DefaultRaycastLayers);
        for (int i = rounds.Count - 1; i >= 0; i--)
        {
            var round = rounds[i];
            if (!round.settings) { Remove(i); continue; }
            if (Time.time >= round.expires)
            {
                if (round.settings.explodeOnExpiry) Impact(round, round.position, Vector3.up);
                Remove(i); continue;
            }
            if (round.settings.useGravity) round.velocity += Physics.gravity * dt;
            Vector3 travel = round.velocity * dt;
            float distance = travel.magnitude;
            bool found = false;
            RaycastHit nearest = default;
            if (distance > .00001f)
            {
                int count = CombatQueries.Raycast(new Ray(round.position, travel / distance), distance,
                    layers, QueryTriggerInteraction.Collide, ref hits);
                for (int h = 0; h < count; h++)
                {
                    var hit = hits[h];
                    if (round.owner && hit.transform.IsChildOf(round.owner)) continue;
                    if (hit.collider.isTrigger && !hit.collider.GetComponent<SoldierHitRegion>()) continue;
                    if (!found || hit.distance < nearest.distance) { nearest = hit; found = true; }
                }
            }
            if (found) { Remove(i); Impact(round, nearest.point, nearest.normal); }
            else { round.position += travel; rounds[i] = round; }
        }
    }
    void Remove(int i)
    {
        int last = rounds.Count - 1;
        rounds[i] = rounds[last];
        rounds.RemoveAt(last);
    }
    void Impact(Round round, Vector3 point, Vector3 normal)
    {
        var config = round.settings;
        ExplosionDamage.Apply(point, normal, config.radius, config.infantryDamage,
            config.vehicleDamage, round.source, transform, round.icon);
        if (round.impactVisual && config.impactEffect)
            ParticleEffectPool.PlayTransient(config.impactEffect, point + normal * .03f,
                Quaternion.FromToRotation(Vector3.forward, normal));
    }
}
