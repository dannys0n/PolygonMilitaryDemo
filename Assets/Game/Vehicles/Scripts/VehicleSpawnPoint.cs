using UnityEngine;

// One live vehicle per point. Destruction callbacks may clear Current synchronously.
public class VehicleSpawnPoint : MonoBehaviour
{
    public Vehicle prefab;
    public Vector3 clearanceCenter = new Vector3(0, 1.4f, 0);
    public Vector3 clearanceSize = new Vector3(3, 2.4f, 5);
    public float respawnDelay = 15, abandonedDelay = 90, cleanupDelay = 7;
    public Vehicle Current { get; private set; }
    float spawnAt, unattendedAt;

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
        if (vehicle.IsOccupied) unattendedAt = Time.time;
        else if (Time.time - unattendedAt > abandonedDelay
            && Vector3.Distance(vehicle.transform.position, transform.position) > 8)
        {
            vehicle.ApplyDamage(vehicle.maxHealth, null);
            return;
        }
        if (vehicle.transform.position.y < -25) vehicle.ApplyDamage(vehicle.maxHealth, null);
    }

    void Spawn()
    {
        foreach (var collider in Physics.OverlapBox(transform.TransformPoint(clearanceCenter), clearanceSize * .5f,
            transform.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            spawnAt = Time.time + 1;
            return;
        }
        Current = Instantiate(prefab, transform.position, transform.rotation);
        Current.Destroyed += OnDestroyed;
        unattendedAt = Time.time;
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
