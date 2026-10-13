using UnityEngine;

// One live vehicle per point. Destruction callbacks may clear Current synchronously.
public class VehicleSpawnPoint : MonoBehaviour
{
    public Vehicle prefab;
    public VehicleCatalog catalog;
    public bool combatPool;
    public bool groundOnly;
    public Vector3 clearanceCenter = new Vector3(0, 1.4f, 0);
    public Vector3 clearanceSize = new Vector3(3, 2.4f, 5);
    public float respawnDelay = 15, abandonedDelay = 90, cleanupDelay = 7;
    public Vehicle Current { get; private set; }
    float spawnAt, unattendedAt;
    bool hasBeenOccupied;

    void Start() => Spawn();

    void Update()
    {
        var vehicle = Current;
        if (!vehicle)
        {
            if (Time.time >= spawnAt) Spawn();
            return;
        }
        if (vehicle.IsDestroyed) return;
        if (vehicle.IsOccupied)
        {
            hasBeenOccupied = true;
            unattendedAt = Time.time;
        }
        else if (hasBeenOccupied && Time.time - unattendedAt > abandonedDelay
            && Vector3.Distance(vehicle.transform.position, transform.position) > 8)
        {
            DespawnUnused(vehicle);
            return;
        }
        if (vehicle.transform.position.y < -25)
        {
            if (vehicle.IsOccupied) vehicle.ApplyDamage(vehicle.maxHealth, null);
            else DespawnUnused(vehicle);
        }
    }

    void Spawn()
    {
        foreach (var collider in Physics.OverlapBox(transform.TransformPoint(clearanceCenter), clearanceSize * .5f,
            transform.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            spawnAt = Time.time + 1;
            return;
        }
        var choices = catalog ? (combatPool ? catalog.combatVehicles : catalog.transports) : null;
        if (groundOnly && choices != null)
            choices = System.Array.FindAll(choices, v => !v.IsAircraft);
        var selected = choices != null && choices.Length > 0 ? choices[Random.Range(0, choices.Length)] : prefab;
        Current = Instantiate(selected, transform.position, transform.rotation);
        Current.Destroyed += OnDestroyed;
        unattendedAt = Time.time;
        hasBeenOccupied = false;
    }

    // Recycling an empty vehicle is not a combat death and must not trigger blast effects.
    void DespawnUnused(Vehicle vehicle)
    {
        vehicle.Destroyed -= OnDestroyed;
        Current = null;
        spawnAt = Time.time + respawnDelay;
        vehicle.gameObject.SetActive(false);
        Destroy(vehicle.gameObject);
    }

    void OnDestroyed(Vehicle wreck)
    {
        wreck.Destroyed -= OnDestroyed;
        Current = null;
        spawnAt = Time.time + respawnDelay;
        Destroy(wreck.gameObject, cleanupDelay);
    }

    void OnDestroy()
    {
        if (!Current) return;
        Current.Destroyed -= OnDestroyed;
        Destroy(Current.gameObject);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1, .68f, 0);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(clearanceCenter, clearanceSize);
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 4);
    }
}
