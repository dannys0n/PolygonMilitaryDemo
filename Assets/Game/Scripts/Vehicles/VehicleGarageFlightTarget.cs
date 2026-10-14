using System.Collections;
using UnityEngine;

// Garage-only spawner and pilot input. Normal assist and motors own every flight force.
[DefaultExecutionOrder(-120)]
public class VehicleGarageFlightTarget : MonoBehaviour
{
    public Vehicle vehiclePrefab;
    public GameObject pilotPrefab;
    public Vector3 orbitCenter;
    public float radius = 350, altitude = 75, cruiseSpeed = 60, flareDelay = 12, respawnDelay = 8, orbitPhase;
    Vehicle vehicle;
    VehicleOccupant pilot;
    GameObject pilotContainer;
    float flareAt;
    bool flareReleased;

    IEnumerator Start() { yield return Spawn(); }
    IEnumerator Spawn()
    {
        float phase = orbitPhase * Mathf.Deg2Rad;
        Vector3 outward = new Vector3(Mathf.Cos(phase), 0, Mathf.Sin(phase));
        Vector3 tangent = Vector3.Cross(outward, Vector3.up);
        vehicle = Instantiate(vehiclePrefab, orbitCenter + outward * radius + Vector3.up * altitude, Quaternion.LookRotation(tangent));
        vehicle.name = vehiclePrefab.name + " airborne garage target";
        vehicle.Destroyed += OnDestroyed;
        vehicle.hasRenderedInterior = false;
        foreach (var seat in vehicle.seats) seat.showCharacter = false;
        if (vehicle.motor is JetMotor jet) jet.startingThrottle = .55f;
        var defenses = vehicle.GetComponent<VehicleCountermeasures>();
        if (defenses) defenses.automaticForAI = false;
        pilotContainer = new GameObject("Garage target pilot");
        pilotContainer.SetActive(false);
        var actor = Instantiate(pilotPrefab, pilotContainer.transform);
        foreach (var brain in actor.GetComponents<ArenaAIInput>()) brain.enabled = false;
        foreach (var brain in actor.GetComponents<ArenaVehicleAI>()) brain.enabled = false;
        var participant = actor.GetComponent<ArenaParticipant>();
        if (participant) participant.enabled = false;
        pilot = actor.GetComponent<VehicleOccupant>();
        pilot.faction = 1;
        pilotContainer.SetActive(true);
        yield return null;
        if (!vehicle || vehicle.IsDestroyed || !pilot) yield break;
        vehicle.seats[0].occupant = pilot;
        pilot.Mount(vehicle, 0);
        vehicle.Body.linearVelocity = tangent * cruiseSpeed;
        flareAt = Time.time + flareDelay;
        flareReleased = false;
    }
    void OnDestroyed(Vehicle destroyed)
    {
        destroyed.Destroyed -= OnDestroyed;
        vehicle = null;
        if (pilot) Destroy(pilot.gameObject);
        if (pilotContainer) Destroy(pilotContainer);
        StartCoroutine(Respawn());
    }
    IEnumerator Respawn() { yield return new WaitForSeconds(respawnDelay); yield return Spawn(); }
    void FixedUpdate()
    {
        if (!pilot || !pilot.IsMounted || !vehicle || vehicle.IsDestroyed) return;
        Vector3 outward = Vector3.ProjectOnPlane(vehicle.transform.position - orbitCenter, Vector3.up);
        if (outward.sqrMagnitude < 1) outward = Vector3.right;
        Vector3 direction = Vector3.Cross(outward.normalized, Vector3.up) - outward.normalized * Mathf.Clamp((outward.magnitude - radius) * .01f, -.7f, .7f);
        float heightError = orbitCenter.y + altitude - vehicle.transform.position.y;
        float heading = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        if (vehicle.motor is JetMotor jet)
        {
            direction.y = Mathf.Clamp(heightError * .015f - vehicle.Body.linearVelocity.y * .02f, -.3f, .3f);
            vehicle.DriverInput = new VehicleInput { directionalFlight = true, heading = heading,
                viewPitch = -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg,
                move = new Vector2(0, Mathf.Clamp((cruiseSpeed - jet.Airspeed) * .12f, -1, 1)) };
        }
        else if (vehicle.motor is HelicopterMotor helicopter)
            vehicle.DriverInput = new VehicleInput { directionalFlight = true, heading = heading, movementHeading = heading,
                move = new Vector2(0, Mathf.Clamp01(cruiseSpeed / helicopter.maxSpeed)),
                lift = Mathf.Clamp(heightError * .1f - vehicle.Body.linearVelocity.y * .1f, -1, 1) };
        if (!flareReleased && Time.time >= flareAt)
        {
            var defenses = vehicle.GetComponent<VehicleCountermeasures>();
            if (defenses) defenses.TryDeploy(pilot);
            flareReleased = true;
        }
    }
    void OnDestroy()
    {
        if (vehicle) { vehicle.Destroyed -= OnDestroyed; Destroy(vehicle.gameObject); }
        if (pilot) Destroy(pilot.gameObject);
        if (pilotContainer) Destroy(pilotContainer);
    }
}
