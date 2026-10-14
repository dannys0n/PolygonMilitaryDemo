using System.Collections;
using UnityEngine;

// Garage-only flight target: supplies pilot commands to the regular assist/motor pipeline.
[DefaultExecutionOrder(-50)]
public class VehicleGarageFlightTarget : MonoBehaviour
{
    public Vehicle vehicle;
    public GameObject pilotPrefab;
    public Vector3 orbitCenter;
    public float radius = 350, altitude = 75, cruiseSpeed = 60, flareDelay = 12;
    VehicleOccupant pilot;
    GameObject pilotContainer;
    float flareAt;
    bool flareReleased;

    IEnumerator Start()
    {
        if (!vehicle || !(vehicle.motor is JetMotor) || !pilotPrefab) { enabled = false; yield break; }
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
        // Let the normal soldier lifecycle initialize before mounting the real seat.
        yield return null;
        if (!vehicle || vehicle.IsDestroyed || !pilot) yield break;
        vehicle.seats[0].occupant = pilot;
        pilot.Mount(vehicle, 0);
        vehicle.Body.linearVelocity = vehicle.transform.forward * cruiseSpeed;
        flareAt = Time.time + flareDelay;
    }

    void FixedUpdate()
    {
        if (!pilot || !pilot.IsMounted || vehicle.IsDestroyed) return;
        Vector3 outward = Vector3.ProjectOnPlane(vehicle.transform.position - orbitCenter, Vector3.up);
        if (outward.sqrMagnitude < 1) outward = Vector3.right;
        float radialError = outward.magnitude - radius;
        Vector3 direction = Vector3.Cross(outward.normalized, Vector3.up) - outward.normalized * Mathf.Clamp(radialError * .01f, -.7f, .7f);
        float heightError = orbitCenter.y + altitude - vehicle.transform.position.y;
        direction.y = Mathf.Clamp(heightError * .015f - vehicle.Body.linearVelocity.y * .02f, -.3f, .3f);
        var jet = (JetMotor)vehicle.motor;
        vehicle.DriverInput = new VehicleInput
        {
            directionalFlight = true,
            heading = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg,
            viewPitch = -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg,
            move = new Vector2(0, Mathf.Clamp((cruiseSpeed - jet.Airspeed) * .12f, -1, 1))
        };
        if (!flareReleased && Time.time >= flareAt)
        {
            var defenses = vehicle.GetComponent<VehicleCountermeasures>();
            if (defenses) defenses.TryDeploy(pilot);
            flareReleased = true;
        }
    }

    void OnDestroy()
    {
        if (pilot) Destroy(pilot.gameObject);
        if (pilotContainer) Destroy(pilotContainer);
    }
}
