using UnityEngine;

// Prototype seekers: finite acquisition cone, line of sight, lead pursuit and bounded turning.
public sealed class VehicleMissileGuidance : MonoBehaviour
{
    public enum Seeker { Infrared, Radar, Optical }
    public Seeker seeker;
    public static readonly System.Collections.Generic.List<VehicleMissileGuidance> Active = new System.Collections.Generic.List<VehicleMissileGuidance>();
    public Vehicle Target => target;
    public bool HasGuidance => initialized;
    public Seeker SeekerType => guidance==Guidance.LaserSpot || guidance==Guidance.CommandLineOfSight ? Seeker.Optical : seeker;
    void OnEnable()=>Active.Add(this);
    void OnDisable()=>Active.Remove(this);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetRegistry()=>Active.Clear();
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
    public void Initialize(Vehicle source, Ray aim, VehicleLauncher launcher = null, Vehicle validatedTarget = null)
    {
        body = GetComponent<Rigidbody>(); launchTime = Time.time; initialized = true;
        director = launcher;
        aimPoint = aim.GetPoint(acquisitionRange);
        target = launcher && launcher.requiresLock ? validatedTarget : FindTarget(source, aim);
        // Laser designation strikes the selected visible surface when no vehicle was acquired.
        if (guidance == Guidance.LaserSpot || guidance == Guidance.CommandLineOfSight)
            foreach (var hit in Physics.RaycastAll(aim, acquisitionRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(source.transform) && !hit.transform.IsChildOf(transform)
                    && !(hit.collider is CharacterController) && hit.distance < Vector3.Distance(aim.origin, aimPoint)) aimPoint = hit.point;
    }
    // Acquisition and launch share these checks; the lock variant never chooses a new target on launch.
    public Vehicle FindTarget(Vehicle source, Ray aim, Ray? launchRay = null)
    {
        Vehicle bestTarget = null;
        float bestAngle = float.PositiveInfinity;
        foreach (var candidate in Vehicle.Active)
        {
            if (!CanAcquire(source, candidate, aim, launchRay)) continue;
            float angle = Vector3.Angle(aim.direction, TargetPoint(candidate) - aim.origin);
            if (angle < bestAngle) { bestAngle = angle; bestTarget = candidate; }
        }
        return bestTarget;
    }
    public static Vector3 TargetPoint(Vehicle candidate)
    {
        if (!candidate) return Vector3.zero;
        return candidate.targetPoint ? candidate.targetPoint.position
            : candidate.transform.TransformPoint(candidate.NavigationBounds.center);
    }
    public bool CanAcquire(Vehicle source, Vehicle candidate, Ray aim, Ray? launchRay = null)
    {
        if (!source || !candidate || candidate == source || candidate.IsDestroyed || !candidate.IsOccupied) return false;
        bool flying = candidate.IsAircraft;
        if ((guidance == Guidance.AirToAir || guidance == Guidance.AirDefence) && !flying) return false;
        int faction = -1;
        foreach (var seat in source.seats) if (seat.occupant) { faction = seat.occupant.faction; break; }
        if (faction < 0) return false;
        bool hostile = false;
        foreach (var seat in candidate.seats)
            if (seat.occupant && seat.occupant.faction >= 0 && seat.occupant.faction != faction) hostile = true;
        if (!hostile) return false;
        Vector3 point = TargetPoint(candidate), offset = point - aim.origin;
        if (offset.sqrMagnitude > acquisitionRange * acquisitionRange || Vector3.Angle(aim.direction, offset) > acquisitionAngle) return false;
        if (launchRay.HasValue && Vector3.Angle(launchRay.Value.direction, point - launchRay.Value.origin) > acquisitionAngle) return false;
        if (SeekerType == Seeker.Optical && GrenadeSmoke.BlocksLine(aim.origin, point)) return false;
        if (!HasLineOfSight(source, candidate, aim.origin, point)) return false;
        return !launchRay.HasValue || HasLineOfSight(source, candidate, launchRay.Value.origin, point);
    }
    static bool HasLineOfSight(Vehicle source, Vehicle candidate, Vector3 origin, Vector3 point)
    {
        Vector3 offset = point - origin;
        foreach (var hit in Physics.RaycastAll(origin, offset.normalized, offset.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(source.transform) && !hit.transform.IsChildOf(candidate.transform)
                && !hit.collider.GetComponentInParent<VehicleRocket>() && !(hit.collider is CharacterController)) return false;
        return true;
    }
    void FixedUpdate()
    {
        if (!initialized || Time.time < launchTime + seekerDelay || !body) return;
        if(target && SeekerType!=Seeker.Optical)
        {
            foreach(var candidate in VehicleDecoy.Active)
            {
                if(!candidate.Alive || (candidate.signature==VehicleDecoy.Signature.Infrared)!=(SeekerType==Seeker.Infrared))continue;
                Vector3 offset=candidate.transform.position-body.position;
                if(offset.magnitude>acquisitionRange || Vector3.Angle(body.rotation*Vector3.forward,offset)>acquisitionAngle)continue;
                // A decoy must be near the tracked aircraft, not a global invulnerability switch.
                if(Vector3.Distance(candidate.transform.position,TargetPoint(target))>80)continue;
                bool obscured=false;
                foreach(var hit in Physics.RaycastAll(body.position,offset.normalized,offset.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                    if(!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(candidate.transform)
                        && !hit.transform.IsChildOf(target.transform) && !(hit.collider is CharacterController))
                    {obscured=true;break;}
                if(obscured)continue;
                // A fooled seeker goes inertial; never chase a flare or reacquire the aircraft.
                target=null;initialized=false;return;
            }
        }
        if(SeekerType==Seeker.Optical && target && GrenadeSmoke.BlocksLine(body.position,TargetPoint(target)))
        {target=null;initialized=false;return;}
        if(guidance==Guidance.CommandLineOfSight && director && GrenadeSmoke.BlocksLine(director.RequestedAimRay.origin,body.position))
        {target=null;initialized=false;return;}
        if(!target && SeekerType==Seeker.Optical && guidance==Guidance.LaserSpot && GrenadeSmoke.BlocksLine(body.position,aimPoint))
        {initialized=false;return;}
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
            point = TargetPoint(target);
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
