using UnityEngine;

// Prototype seekers: finite acquisition cone, line of sight, lead pursuit and bounded turning.
public sealed class VehicleMissileGuidance : MonoBehaviour
{
    public enum Guidance { AirToAir, LaserSpot, AirDefence, CommandLineOfSight }
    public Guidance guidance;
    public float acquisitionAngle = 20, acquisitionRange = 1600, degreesPerSecond = 100;
    public float seekerDelay = .15f;
    Rigidbody body;
    Vehicle target;
    Vector3 aimPoint;
    float launchTime;
    bool initialized;
    VehicleLauncher director;
    public void Initialize(Vehicle source, Ray aim, VehicleLauncher launcher = null)
    {
        body = GetComponent<Rigidbody>(); launchTime = Time.time; initialized = true;
        director = launcher;
        aimPoint = aim.GetPoint(acquisitionRange);
        int faction = -1;
        foreach (var seat in source.seats) if (seat.occupant) { faction = seat.occupant.faction; break; }
        float best = float.PositiveInfinity;
        foreach (var candidate in Vehicle.Active)
        {
            if (candidate == source || candidate.IsDestroyed || !candidate.IsOccupied) continue;
            bool flying = candidate.instruments == Vehicle.InstrumentLayout.FixedWing || candidate.instruments == Vehicle.InstrumentLayout.Helicopter;
            if ((guidance == Guidance.AirToAir || guidance == Guidance.AirDefence) && !flying) continue;
            bool hostile = false;
            foreach (var seat in candidate.seats)
                if (seat.occupant && seat.occupant.faction >= 0 && seat.occupant.faction != faction) hostile = true;
            if (!hostile) continue;
            Vector3 offset = candidate.cameraTarget.position - aim.origin;
            float angle = Vector3.Angle(aim.direction, offset);
            if (offset.magnitude > acquisitionRange || angle > acquisitionAngle || angle >= best) continue;
            bool blocked = false;
            foreach (var hit in Physics.RaycastAll(aim.origin, offset.normalized, offset.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(source.transform) && !hit.transform.IsChildOf(candidate.transform)
                    && !hit.transform.IsChildOf(transform) && !(hit.collider is CharacterController)) { blocked = true; break; }
            if (!blocked) { target = candidate; best = angle; }
        }
        // Laser designation strikes the selected visible surface when no vehicle was acquired.
        if (guidance == Guidance.LaserSpot || guidance == Guidance.CommandLineOfSight)
            foreach (var hit in Physics.RaycastAll(aim, acquisitionRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(source.transform) && !hit.transform.IsChildOf(transform)
                    && !(hit.collider is CharacterController) && hit.distance < Vector3.Distance(aim.origin, aimPoint)) aimPoint = hit.point;
    }
    void FixedUpdate()
    {
        if (!initialized || Time.time < launchTime + seekerDelay || !body) return;
        if (!target && guidance != Guidance.LaserSpot && guidance != Guidance.CommandLineOfSight) return; // Unguided flight on seeker failure.
        Vector3 point = aimPoint;
        float speed = body.linearVelocity.magnitude;
        if (guidance == Guidance.CommandLineOfSight && director)
        {
            Ray command = director.RequestedAimRay;
            // SACLOS approximation: correct toward the maintained sight line, not an autonomous seeker.
            float ahead = Mathf.Max(40, Vector3.Dot(body.position - command.origin, command.direction) + speed * .5f);
            point = command.GetPoint(ahead);
        }
        else if (target && !target.IsDestroyed)
        {
            point = target.cameraTarget.position;
            float time = Vector3.Distance(body.position, point) / Mathf.Max(1, speed);
            point += target.Body.linearVelocity * Mathf.Min(time, 3f);
        }
        else if (target) { target = null; return; }
        Vector3 direction = point - body.position;
        if (direction.sqrMagnitude < .04f) return;
        Quaternion rotation = Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(direction), degreesPerSecond * Time.fixedDeltaTime);
        body.MoveRotation(rotation);
        body.linearVelocity = rotation * Vector3.forward * speed;
    }
}
