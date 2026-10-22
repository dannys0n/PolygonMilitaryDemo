using System.Collections.Generic;
using UnityEngine;

// Sandbox aircraft only: a small pursuit/extend state machine supplies the same
// stick, pedal and throttle inputs as a player. Motors retain all physics ownership.
[DefaultExecutionOrder(-110)]
public sealed class SandboxAirCombatAI : MonoBehaviour
{
    public enum Battle { Jets, Helicopters }
    public enum FlightState { Orbit, Pursuit, Extend }
    static readonly List<SandboxAirCombatAI> active = new List<SandboxAirCombatAI>();
    public static IReadOnlyList<SandboxAirCombatAI> Active => active;
    public Vehicle vehicle;
    public ArenaParticipant.Team team;
    public Transform arenaCenter;
    public Battle battle;
    public float orbitRadius = 250, detectionRange = 1600;
    public float pursuitSeconds = 7, extensionSeconds = 4, helicopterSpacing = 100;
    public ArenaDirectPilot pilot = new ArenaDirectPilot();
    public FlightState State { get; private set; }
    public Vehicle Target => enemy ? enemy.vehicle : null;
    SandboxAirCombatAI enemy;
    VehicleWeaponSystems systems;
    float nextDecision, pursuitStarted, extendUntil, flightAltitude;
    Vector3 extendPoint;
    readonly RaycastHit[] hits = new RaycastHit[32];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => active.Clear();
    void OnEnable() { if (!active.Contains(this)) active.Add(this); }
    void OnDisable() { active.Remove(this); if (vehicle) vehicle.DriverInput = default; }
    public void Configure(Vehicle aircraft, ArenaParticipant.Team faction, Transform center, Battle domain, float radius = 250)
    {
        vehicle = aircraft; team = faction; arenaCenter = center; battle = domain;
        orbitRadius = Mathf.Max(100, radius);
        systems = vehicle.GetComponent<VehicleWeaponSystems>();
        flightAltitude = vehicle.Body.position.y;
        pilot.Reset(); enemy = null; nextDecision = 0; State = FlightState.Orbit;
    }
    bool Operational => vehicle && !vehicle.IsDestroyed && vehicle.Driver && !vehicle.Driver.life.health.IsDead;
    static Vector3 Point(Vehicle aircraft) => VehicleMissileGuidance.TargetPoint(aircraft);
    void Update()
    {
        if (!Operational) return;
        if (Time.time >= nextDecision)
        {
            nextDecision = Time.time + .3f;
            if (!enemy || !enemy.Operational || enemy.team == team || enemy.battle != battle)
            {
                enemy = null;
                float best = detectionRange * detectionRange;
                foreach (var candidate in active)
                {
                    if (candidate == this || !candidate.Operational || candidate.team == team || candidate.battle != battle) continue;
                    float distance = (Point(candidate.vehicle) - Point(vehicle)).sqrMagnitude;
                    if (distance < best) { best = distance; enemy = candidate; }
                }
                pursuitStarted = Time.time;
            }
        }
        if (!enemy || !enemy.Operational) return;
        // Each station fires its own weapon system; crew never run infantry targeting.
        foreach (var seat in vehicle.seats)
            if (seat.occupant && seat.occupant.IsAI && seat.role != Vehicle.SeatRole.Passenger)
                FireStation(seat.occupant);
    }
    void FixedUpdate()
    {
        if (!Operational || !vehicle.Driver.IsAI) return;
        Vector3 position = vehicle.Body.position;
        Vector3 center = arenaCenter ? arenaCenter.position : Vector3.zero;
        Vector3 destination;
        bool pursuing = enemy && enemy.Operational;
        if (battle == Battle.Jets)
        {
            if (pursuing && Time.time >= extendUntil &&
                (Time.time - pursuitStarted > pursuitSeconds || Vector3.Distance(position, Point(enemy.vehicle)) < 65))
            {
                // Commit to a short straight separation before another intercept, rather
                // than continuously tightening an endless two-plane turn circle.
                extendUntil = Time.time + extensionSeconds;
                extendPoint = position + vehicle.transform.forward * 500;
                extendPoint.y = Mathf.Max(flightAltitude, position.y);
                pursuitStarted = extendUntil;
            }
            if (Time.time < extendUntil) { State = FlightState.Extend; destination = extendPoint; }
            else if (pursuing)
            {
                State = FlightState.Pursuit;
                Vector3 target = Point(enemy.vehicle);
                float leadTime = Mathf.Min(1.2f, Vector3.Distance(position, target) / 350);
                destination = target + enemy.vehicle.Body.linearVelocity * leadTime;
            }
            else { State = FlightState.Orbit; destination = OrbitPoint(center, position); }
            destination += Separation(position);
            destination = AvoidObstacle(position, destination);
            Vector3 direction = (destination - position).normalized;
            var intent = new VehicleInput { move = Vector2.up, directionalFlight = true,
                heading = Heading(direction), movementHeading = Heading(direction),
                viewPitch = Pitch(direction), aim = State == FlightState.Pursuit, boost = false };
            vehicle.DriverInput = pilot.ReadInput(vehicle, intent);
        }
        else
        {
            State = pursuing ? FlightState.Pursuit : FlightState.Orbit;
            destination = pursuing ? Point(enemy.vehicle) : OrbitPoint(center, position);
            Vector3 offset = destination - position;
            Vector3 horizontal = Vector3.ProjectOnPlane(offset, Vector3.up);
            float range = horizontal.magnitude;
            Vector3 originalDestination = destination;
            destination = AvoidObstacle(position, destination);
            if ((destination - originalDestination).sqrMagnitude > 1)
            { offset = destination-position; horizontal = Vector3.ProjectOnPlane(offset, Vector3.up); range=horizontal.magnitude; }
            Vector3 desiredVelocity = horizontal.normalized * Mathf.Clamp((range - helicopterSpacing) * .25f, -8, 20);
            if (pursuing) desiredVelocity += Vector3.ProjectOnPlane(enemy.vehicle.Body.linearVelocity, Vector3.up) * .5f;
            desiredVelocity += Separation(position) * .35f;
            float heading = Heading(horizontal.sqrMagnitude > 1 ? horizontal : vehicle.transform.forward);
            float maxSpeed = vehicle.motor is HelicopterMotor motor ? motor.maxSpeed : 30;
            Vector3 localMove = Quaternion.Inverse(Quaternion.Euler(0, heading, 0)) * desiredVelocity;
            // Hold the assigned altitude lane while the gunner tracks the target.
            float desiredAltitude = (destination-originalDestination).sqrMagnitude>1 ? Mathf.Max(flightAltitude,destination.y) : flightAltitude;
            var intent = new VehicleInput { move = Vector2.ClampMagnitude(new Vector2(localMove.x, localMove.z) / Mathf.Max(1, maxSpeed), .7f),
                lift = Mathf.Clamp((desiredAltitude - position.y) * .15f, -1, 1),
                heading = heading, movementHeading = heading, viewPitch = Pitch(offset.normalized),
                directionalFlight = true, aim = range < helicopterSpacing * 1.8f };
            vehicle.DriverInput = pilot.ReadInput(vehicle, intent);
        }
    }
    Vector3 OrbitPoint(Vector3 center, Vector3 position)
    {
        Vector3 radial = Vector3.ProjectOnPlane(position - center, Vector3.up);
        if (radial.sqrMagnitude < 1) radial = Vector3.forward;
        float angle = Mathf.Atan2(radial.x, radial.z) + .65f;
        return center + new Vector3(Mathf.Sin(angle) * orbitRadius, flightAltitude - center.y, Mathf.Cos(angle) * orbitRadius);
    }
    Vector3 Separation(Vector3 position)
    {
        Vector3 correction = Vector3.zero;
        foreach (var other in active)
        {
            if (other == this || !other.Operational) continue;
            Vector3 relative = other.vehicle.Body.position - position;
            Vector3 relativeVelocity = other.vehicle.Body.linearVelocity - vehicle.Body.linearVelocity;
            float time = Mathf.Clamp(-Vector3.Dot(relative, relativeVelocity) / Mathf.Max(1, relativeVelocity.sqrMagnitude), 0, 2);
            Vector3 closest = relative + relativeVelocity * time;
            float safety = battle == Battle.Jets ? 40 : 35;
            if (closest.magnitude >= safety || relative.magnitude > 220) continue;
            Vector3 away = -closest;
            if (away.sqrMagnitude < 1) away = Vector3.Cross(relative.normalized, Vector3.up);
            if (away.sqrMagnitude < .01f) away = Vector3.up * (GetEntityId().GetHashCode() < other.GetEntityId().GetHashCode() ? -1 : 1);
            correction += away.normalized * (safety - closest.magnitude) * 4;
        }
        return Vector3.ClampMagnitude(correction, 140);
    }
    Vector3 AvoidObstacle(Vector3 position, Vector3 desired)
    {
        Vector3 direction = vehicle.Body.linearVelocity.sqrMagnitude > 4 ? vehicle.Body.linearVelocity.normalized : vehicle.transform.forward;
        int count = Physics.SphereCastNonAlloc(position, 5, direction, hits, Mathf.Max(55, vehicle.Body.linearVelocity.magnitude * 2), ProjectileCollision.WithoutProjectiles(Physics.DefaultRaycastLayers), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            var collider=hit.collider;
            if (collider.transform.IsChildOf(vehicle.transform) || collider.GetComponentInParent<VehicleOccupant>()) continue;
            if (collider.GetComponentInParent<Vehicle>()) continue; // Predictive separation owns aircraft avoidance.
            return position + Vector3.up * 160 + vehicle.transform.forward * 80;
        }
        return desired;
    }
    void FireStation(VehicleOccupant actor)
    {
        if (!systems || !enemy || !enemy.Operational) return;
        var role = vehicle.seats[actor.SeatIndex].role;
        Vector3 physicalPoint = Point(enemy.vehicle);
        VehicleWeaponSystems.Weapon selected = null;
        int choice = -1, ordinal = 0;
        float best = float.NegativeInfinity;
        foreach (var weapon in systems.weapons)
        {
            if (weapon.role != role) continue;
            int index = ordinal++;
            if (!systems.IsAvailable(weapon)) continue;
            Vector3 origin = weapon.launcher ? weapon.launcher.NextShotRay.origin : weapon.gun && weapon.gun.muzzle ? weapon.gun.muzzle.position : vehicle.Body.position;
            if ((physicalPoint - origin).sqrMagnitude > weapon.range * weapon.range) continue;
            bool launcher = weapon.system == VehicleWeaponSystems.FireSystem.Launcher;
            if (launcher)
            {
                if (!weapon.launcher || !weapon.launcher.projectile || weapon.launcher.Missiles <= 0 || weapon.ReadyIn > 0) continue;
                var guidance = weapon.launcher.projectile.GetComponent<VehicleMissileGuidance>();
                if (weapon.launcher.requiresLock && !guidance) continue;
                if (guidance && !guidance.CanAcquire(vehicle, enemy.vehicle, new Ray(origin, (physicalPoint-origin).normalized), weapon.launcher.NextShotRay)) continue;
            }
            else if (!weapon.gun || !weapon.gun.muzzle) continue;
            float score = launcher ? 30 : 20;
            if (score > best) { best = score; selected = weapon; choice = index; }
        }
        if (selected == null) return;
        if (systems.Current(actor) != selected) systems.Select(actor, choice);
        Transform muzzle = selected.launcher ? selected.launcher.NextMuzzle : selected.gun.muzzle;
        Vector3 aim = selected.launcher ? physicalPoint : Lead(physicalPoint, muzzle.position, selected.projectile);
        Vector3 direction = aim - muzzle.position;
        systems.AimAt(actor, new Ray(muzzle.position, direction.normalized));
        // AI holds the same seeker lock as a player; never fire a stale lock at
        // another aircraft or repeatedly issue rejected fire inputs while acquiring.
        if(selected.launcher && selected.launcher.requiresLock
            && (!selected.launcher.IsLocked || selected.launcher.LockTarget!=enemy.vehicle))return;
        float angle = selected.launcher ? 20 : battle == Battle.Jets ? 22 : 12;
        if (Vector3.Angle(muzzle.forward, direction) <= angle && ClearShot(muzzle.position, physicalPoint)) systems.TryFire(actor);
    }
    Vector3 Lead(Vector3 point, Vector3 origin, VehicleRocket projectile)
    {
        if (!projectile || !projectile.ballistic) return point;
        Vector3 relativeVelocity = enemy.vehicle.Body.linearVelocity - vehicle.Body.GetPointVelocity(origin);
        float time = Vector3.Distance(point, origin) / Mathf.Max(1, projectile.speed);
        for (int i=0;i<3;i++) time = Mathf.Min(2, (point-origin+relativeVelocity*time).magnitude / Mathf.Max(1, projectile.speed));
        return point + relativeVelocity*time - (projectile.useGravity ? Physics.gravity*(.5f*time*time) : Vector3.zero);
    }
    bool ClearShot(Vector3 origin, Vector3 point)
    {
        Vector3 offset = point-origin;
        int count = Physics.RaycastNonAlloc(origin, offset.normalized, hits, offset.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i=0;i<count;i++)
        {
            var hit=hits[i];
            if (hit.transform.IsChildOf(vehicle.transform) || hit.transform.IsChildOf(enemy.vehicle.transform) || hit.collider.GetComponentInParent<VehicleRocket>()) continue;
            var occupant=hit.collider.GetComponentInParent<VehicleOccupant>();
            if (occupant && occupant.IsMounted && (occupant.MountedVehicle==vehicle || occupant.MountedVehicle==enemy.vehicle)) continue;
            return false;
        }
        return true;
    }
    static float Heading(Vector3 direction) => Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
    static float Pitch(Vector3 direction) => -Mathf.Atan2(direction.y,new Vector2(direction.x,direction.z).magnitude)*Mathf.Rad2Deg;
}
