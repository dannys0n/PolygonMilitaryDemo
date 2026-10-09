using UnityEngine;
using UnityEngine.AI;

// Choose a ride -> reserve a seat -> walk to it -> steer -> safely dismount.
// Travel, arrival and obstacle avoidance follow Reynolds' separation of action/steering/motor.
[DefaultExecutionOrder(-15)]
public class ArenaVehicleAI : MonoBehaviour
{
    public enum TravelState
    {
        OnFoot, Boarding, Driving, Landing, Passenger
    }
    public ArenaAIInput infantry;
    public VehicleOccupant occupant;
    public float minimumTrip = 26, vehicleSearchRadius = 35, decisionInterval = .6f, reuseDelay = 12;
    public float cruiseSpeed = 12, flightHeight = 13;
    public TravelState State { get; private set; }
    public Vector3 Goal { get; private set; }
    ArenaVehicleScenario scenario;
    Vehicle reserved;
    Transform reservedEntry;
    int reservedSeat, corner, recoveries;
    NavMeshPath path;
    float nextDecision, nextPath, retryAt, boardedAt, progressAt, reverseUntil;
    Vector3 progressPosition;
    bool landing;

    void Awake()
    {
        path = new NavMeshPath();
        scenario = FindAnyObjectByType<ArenaVehicleScenario>();
    }
    void Start()
    {
        occupant.faction = (int)infantry.participant.team;
        nextDecision = Time.time + Random.Range(0f, 2f);
    }
    void OnDisable()
    {
        CancelBoarding();
        if (occupant.IsMounted)
            occupant.Leave(true);
    }
    void Update()
    {
        if (!infantry.participant.match.IsRunning || infantry.life.health.IsDead || infantry.participant.IsAwaitingSpawn)
        {
            CancelBoarding();
            if (occupant.IsMounted)
                occupant.Leave(true);
            return;
        }
        if (occupant.IsMounted)
        {
            DriveOrRide();
            return;
        }
        if (State != TravelState.OnFoot && State != TravelState.Boarding)
        {
            State = TravelState.OnFoot;
            retryAt = Time.time + reuseDelay;
        }
        if (reserved)
        {
            if (reserved.IsDestroyed || infantry.IsCombatAlert || infantry.Target || !reserved.Reserve(occupant, reservedSeat))
            {
                CancelBoarding();
                return;
            }
            infantry.BoardingTarget = reservedEntry;
            if (Vector3.Distance(transform.position, infantry.BoardingTarget.position) < 2.8f && reserved.Board(occupant, reservedSeat))
            {
                infantry.BoardingTarget = null;
                reserved = null;
                State = TravelState.Driving;
                boardedAt = progressAt = Time.time;
                progressPosition = transform.position;
                recoveries = 0;
                nextPath = 0;
                landing = false;
            }
            else if (Time.time - progressAt > 15)
                CancelBoarding();
            return;
        }
        if (Time.time < nextDecision || Time.time < retryAt || infantry.IsCombatAlert || infantry.Target)
            return;
        nextDecision = Time.time + decisionInterval;
        if (Vector3.Distance(transform.position, infantry.Destination) < minimumTrip)
            return;
        float best = 0;
        Vehicle choice = null;
        int seatIndex = 0;
        Vector3 goal = infantry.Destination;
        foreach (var v in Vehicle.Active)
        {
            if (v.gameObject.scene != gameObject.scene || v.IsDestroyed || !v.motor.CanDisembark || v.Body.linearVelocity.magnitude > 2)
                continue;
            float distance = Vector3.Distance(transform.position, v.seats[0].exit.position);
            if (distance > vehicleSearchRadius)
                continue;
            int seat = v.FirstAvailableSeat(occupant);
            if (seat < 0) continue;
            {
                if (!v.CanBoard(occupant, seat))
                    continue;
                bool driver = v.seats[seat].role == Vehicle.SeatRole.Driver;
                if (!driver && !v.Driver)
                    continue; // Do not occupy an unpiloted passenger seat indefinitely.
                Vector3 proposed = infantry.Destination;
                if (v.motor is HelicopterMotor && (!scenario.allowAIHelicopters || !ChooseLanding(v, out proposed)))
                    continue;
                if (driver && v.motor is HummerMotor && !PlanRoad(v, proposed))
                    continue;
                if (!ArenaAITactics.Reachable(infantry, v.NearestEntryPoint(transform.position).position, path, out _, out float walk))
                    continue;
                float trip = Vector3.Distance(v.transform.position, proposed);
                if (driver && trip < minimumTrip)
                    continue;
                float footTime = Vector3.Distance(transform.position, infantry.Destination) / infantry.soldier.movement.moveSpeed;
                float rideTime = walk / infantry.soldier.movement.moveSpeed + trip / cruiseSpeed + 3f
                    + Vector3.Distance(proposed, infantry.Destination) / infantry.soldier.movement.moveSpeed
                    + (v.motor is HelicopterMotor ? 8f : 0f);
                float score = footTime - rideTime;
                if (score < 3f)
                    continue;
                if (score <= best)
                    continue;
                best = score;
                choice = v;
                seatIndex = seat;
                goal = proposed;
            }
        }
        if (!choice || !choice.Reserve(occupant, seatIndex))
            return;
        reserved = choice;
        reservedEntry = choice.NearestEntryPoint(transform.position);
        reservedSeat = seatIndex;
        Goal = goal;
        State = TravelState.Boarding;
        infantry.BoardingTarget = reservedEntry;
        progressAt = Time.time;
    }
    bool ChooseLanding(Vehicle v, out Vector3 goal)
    {
        goal = default;
        float best = float.PositiveInfinity;
        foreach (var p in scenario.landingZones)
        {
            bool occupied = false;
            foreach (var other in Vehicle.Active)
                if (other != v && Vector3.Distance(other.transform.position, p.position) < 14)
                {
                    occupied = true;
                    break;
                }
            foreach (var actor in infantry.participant.match.participants)
                if (actor.TryGetComponent<ArenaVehicleAI>(out var pilot) && pilot != this && pilot.occupant.IsMounted
                    && pilot.occupant.MountedVehicle.motor is HelicopterMotor && Vector3.Distance(pilot.Goal, p.position) < 14)
                    occupied = true;
            if (occupied)
                continue;
            float distance = Vector3.Distance(v.transform.position, p.position);
            if (distance < minimumTrip)
                continue;
            float cost = distance / cruiseSpeed + Vector3.Distance(p.position, infantry.Destination) / infantry.soldier.movement.moveSpeed;
            if (cost < best)
            {
                best = cost;
                goal = p.position;
            }
        }
        return best < float.PositiveInfinity;
    }
    void CancelBoarding()
    {
        if (reserved)
            reserved.ReleaseReservation(occupant);
        reserved = null;
        infantry.BoardingTarget = null;
        State = TravelState.OnFoot;
        retryAt = Time.time + 3;
    }
    void DriveOrRide()
    {
        var v = occupant.MountedVehicle;
        var role = v.seats[occupant.SeatIndex].role;
        if (role == Vehicle.SeatRole.Gunner)
        {
            State = TravelState.Passenger;
            UseTurret(v);
        }
        else if (role == Vehicle.SeatRole.Passenger)
            State = TravelState.Passenger;
        if (role != Vehicle.SeatRole.Driver)
        {
            if (!v.Driver && Time.time - boardedAt > 5 && v.CanExit)
                occupant.Leave(false);
            if (v.Health < v.maxHealth * .25f && v.CanExit)
                occupant.Leave(false);
            return;
        }
        if (Time.time - boardedAt < 2f)
        {
            v.DriverInput = new VehicleInput { brake = true, lift = -.3f };
            return;
        }
        bool threat = NearbyEnemy(15) != null;
        if (v.motor is HelicopterMotor heli)
            Fly(v, heli, threat);
        else
            Drive(v, threat);
    }
    bool PlanRoad(Vehicle v, Vector3 goal)
    {
        var filter = new NavMeshQueryFilter { agentTypeID = scenario.vehicleAgentType, areaMask = NavMesh.AllAreas };
        return NavMesh.SamplePosition(v.transform.position, out var from, 5, filter)
            && NavMesh.SamplePosition(goal, out var to, 6, filter)
            && NavMesh.CalculatePath(from.position, to.position, filter, path) && path.status == NavMeshPathStatus.PathComplete;
    }
    void Drive(Vehicle v, bool threat)
    {
        State = TravelState.Driving;
        float distance = Vector3.ProjectOnPlane(Goal - v.transform.position, Vector3.up).magnitude;
        bool bail = distance < 6 || v.Health < v.maxHealth * .3f || (threat && Time.time - boardedAt > 4) || recoveries >= 3;
        if (bail)
        {
            v.DriverInput = new VehicleInput { brake = true };
            if (v.CanExit)
            {
                occupant.Leave(false);
                retryAt = Time.time + reuseDelay;
            }
            return;
        }
        if (Time.time >= nextPath)
        {
            nextPath = Time.time + 1;
            if (!PlanRoad(v, Goal))
            {
                recoveries++;
                v.DriverInput = new VehicleInput { brake = true };
                return;
            }
            corner = 1;
        }
        var corners = path.corners;
        if (corners.Length < 2)
            return;
        corner = Mathf.Clamp(corner, 1, corners.Length - 1);
        while (corner < corners.Length - 1 && Vector3.Distance(v.transform.position, corners[corner]) < 5)
            corner++;
        Vector3 local = v.transform.InverseTransformPoint(corners[corner]);
        float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        float desired = cruiseSpeed * Mathf.Lerp(1, .25f, Mathf.Clamp01(Mathf.Abs(angle) / 65));
        float speed = Vector3.Dot(v.Body.linearVelocity, v.transform.forward);
        bool blocked = false;
        foreach (var hit in Physics.SphereCastAll(v.transform.position + Vector3.up, .8f, v.transform.forward, Mathf.Max(4, speed * .7f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(v.transform) && !(hit.collider is WheelCollider) && hit.normal.y < .65f)
            {
                blocked = true;
                break;
            }
        if (Vector3.Distance(progressPosition, v.transform.position) > 2)
        {
            progressPosition = v.transform.position;
            progressAt = Time.time;
        }
        if (Time.time - progressAt > 5)
        {
            reverseUntil = Time.time + 1.4f;
            progressAt = Time.time;
            recoveries++;
        }
        if (Time.time < reverseUntil)
            v.DriverInput = new VehicleInput { move = new Vector2(-Mathf.Sign(angle) * .8f, -.55f) };
        else
            v.DriverInput = new VehicleInput { move = new Vector2(Mathf.Clamp(angle / 32, -1, 1), blocked ? 0 : Mathf.Clamp((desired - speed) * .3f, 0, 1)), brake = blocked || speed > desired + 1 };
    }
    void Fly(Vehicle v, HelicopterMotor heli, bool threat)
    {
        if (v.Health < v.maxHealth * .35f)
            landing = true;
        Vector3 difference = Goal - v.transform.position;
        float distance = Vector3.ProjectOnPlane(difference, Vector3.up).magnitude;
        if (distance < 7)
            landing = true;
        State = landing ? TravelState.Landing : TravelState.Driving;
        float altitude = GroundHeight(v.transform.position);
        float desiredHeight = landing ? Goal.y + .05f : altitude + flightHeight;
        Vector3 velocity = Vector3.ProjectOnPlane(difference, Vector3.up).normalized * Mathf.Min(landing ? 3 : 12, distance * .8f);
        if (v.transform.position.y - altitude < flightHeight * .65f && !landing)
            velocity = Vector3.zero; // Climb before translating.
        foreach (var hit in Physics.SphereCastAll(v.transform.position + Vector3.up * 2, 2, velocity.normalized, 12, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(v.transform))
            {
                velocity = Vector3.zero;
                desiredHeight = Mathf.Max(desiredHeight, v.transform.position.y + 4);
                break;
            }
        Vector3 local = v.transform.InverseTransformDirection(velocity - v.Body.linearVelocity);
        float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(v.transform.forward, Vector3.up), Vector3.ProjectOnPlane(difference, Vector3.up), Vector3.up);
        v.DriverInput = new VehicleInput
        {
            move = Vector2.ClampMagnitude(new Vector2(local.x, local.z) * .16f, 1),
            yaw = landing ? 0 : Mathf.Clamp(yaw / 45, -1, 1),
            lift = Mathf.Clamp((desiredHeight - v.transform.position.y) * .5f, -.6f, 1)
        };
        if (landing && heli.Grounded && v.CanExit && Time.time - boardedAt > 4)
        {
            occupant.Leave(false);
            retryAt = Time.time + reuseDelay;
        }
        if (Time.time - boardedAt > 70)
            landing = true;
    }
    static float GroundHeight(Vector3 position)
    {
        float height = 0;
        float nearest = float.PositiveInfinity;
        foreach (var hit in Physics.RaycastAll(position + Vector3.up * 30, Vector3.down, 100, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.collider.GetComponentInParent<Vehicle>() && !hit.collider.GetComponentInParent<SoldierLife>() && hit.distance < nearest)
            {
                nearest = hit.distance;
                height = hit.point.y;
            }
        return height;
    }
    ArenaParticipant NearbyEnemy(float radius)
    {
        ArenaParticipant best = null;
        float distance = radius;
        foreach (var actor in infantry.participant.match.participants)
        {
            if (actor.team == infantry.participant.team || actor.health.IsDead || actor.IsAwaitingSpawn)
                continue;
            float d = Vector3.Distance(actor.transform.position, transform.position);
            if (d < distance)
            {
                distance = d;
                best = actor;
            }
        }
        return best;
    }
    void UseTurret(Vehicle v)
    {
        var enemy = NearbyEnemy(100);
        if (!enemy)
            return;
        Vector3 direction = enemy.transform.position + Vector3.up * 1.25f - v.turret.muzzle.position;
        v.turret.Aim(direction);
        if (Vector3.Angle(v.turret.muzzle.forward, direction) < 4)
            v.turret.TryFire(occupant);
    }
}
