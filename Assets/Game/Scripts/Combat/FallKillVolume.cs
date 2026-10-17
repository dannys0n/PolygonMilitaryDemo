using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
public sealed class FallKillVolume : MonoBehaviour
{
    [Tooltip("Optional match roster for checks that do not rely on a trigger being crossed.")]
    public ArenaMatch match;
    [Tooltip("Disable for perimeter trigger walls; the under-map volume owns the single roster safety check.")]
    public bool pollRoster=true;
    public bool enforceMapFootprint;
    [Min(100)] public float maximumHeightAboveFloor=5000;
    public int EscapedActors { get; private set; }
    double nextCheck;
    BoxCollider box;
    void Reset() => GetComponent<BoxCollider>().isTrigger=true;
    void Awake()
    {
        box=GetComponent<BoxCollider>();box.isTrigger=true;
        if(pollRoster&&!match)foreach(var candidate in FindObjectsByType<ArenaMatch>())
            if(candidate.gameObject.scene==gameObject.scene){match=candidate;break;}
    }
    public bool OutsideWorld(Vector3 position)
    {
        var bounds=(box?box:GetComponent<BoxCollider>()).bounds;
        return !float.IsFinite(position.x)||!float.IsFinite(position.y)||!float.IsFinite(position.z)
            || position.y<bounds.max.y || enforceMapFootprint && (position.y>bounds.max.y+maximumHeightAboveFloor
            ||position.x<bounds.min.x||position.x>bounds.max.x||position.z<bounds.min.z||position.z>bounds.max.z);
    }
    void FixedUpdate()
    {
        if(!pollRoster || !match || !match.IsRunning || Time.timeAsDouble<nextCheck)return;
        nextCheck=Time.timeAsDouble+.25;
        // Continuous collision detection does not guarantee trigger callbacks. A
        // half-space test still catches a body that crosses the entire box in one step.
        foreach(var actor in match.participants)
        {
            if(!actor || actor.IsSpectator || actor.IsAwaitingSpawn || actor.health.IsDead)continue;
            var occupant=actor.GetComponent<VehicleOccupant>();if(occupant&&occupant.IsMounted)continue;
            var position=actor.player?actor.player.controller.body.position:actor.transform.position;
            if(!OutsideWorld(position))continue;
            EscapedActors++;
            Debug.LogWarning($"World safety: {actor.name} escaped at {position}; velocity {(actor.player?actor.player.controller.Velocity:Vector3.zero)}. Returning through normal death/respawn.",actor);
            actor.health.ApplyDamage(actor.health.maxHealth,null);
        }
        foreach(var vehicle in Vehicle.Active)
            if(vehicle && vehicle.gameObject.scene==gameObject.scene && !vehicle.IsDestroyed && OutsideWorld(vehicle.Body.position))
                vehicle.ApplyDamage(vehicle.maxHealth,null);
    }
    void OnTriggerEnter(Collider other) => Kill(other);
    // Also catches actors whose damage was temporarily blocked when they entered.
    void OnTriggerStay(Collider other) => Kill(other);

    static void Kill(Collider other)
    {
        var vehicle=other.GetComponentInParent<Vehicle>();
        if(vehicle && !vehicle.IsDestroyed)vehicle.ApplyDamage(vehicle.maxHealth,null);
        var health=other.GetComponentInParent<SoldierHealth>();
        if(health && !health.IsDead)health.ApplyDamage(health.maxHealth,null);
    }

    void OnDrawGizmosSelected()
    {
        var box=GetComponent<BoxCollider>();
        Gizmos.matrix=transform.localToWorldMatrix;
        Gizmos.color=new Color(1,.15f,.05f,.12f);Gizmos.DrawCube(box.center,box.size);
        Gizmos.color=new Color(1,.3f,.05f,.8f);Gizmos.DrawWireCube(box.center,box.size);
    }
}
