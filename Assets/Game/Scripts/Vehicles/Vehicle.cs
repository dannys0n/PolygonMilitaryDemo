using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Shared seat ownership and lifecycle; motors accept the same commands from humans and AI.
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Rigidbody))]
public class Vehicle : MonoBehaviour
{
    public enum InstrumentLayout { Road, Armored, Helicopter, FixedWing, Drone }
    [Header("Driver / pilot instruments")]
    public InstrumentLayout instruments;
    public enum InstrumentReference { Generic, HMMWV, M113, Stryker, MRAP, Bradley, Abrams, Leopard2, T72, HIMARS, Tor, FMTV, Technical, Apache, Cobra, BlackHawk, LittleBird, F15, Su27, Reaper, C17 }
    [Tooltip("Tentative real-world reference inferred from the model; this selects the adapted instrument layout.")]
    public InstrumentReference instrumentReference;
    [TextArea] public string instrumentReferenceNotes;
    public enum SeatRole
    {
        Driver, Gunner, Passenger
    }
    [Serializable]
    public class Seat
    {
        public enum Pose { Automatic, Sitting, Driving, Turret }
        public string name;
        public SeatRole role;
        [Tooltip("Override the mounted pose when the chair differs from the station role, such as a seated aircraft gunner.")]
        public Pose pose;
        public Transform anchor, exit;
        [Tooltip("Disable for remote stations or functional seats without a modeled chair.")]
        public bool showCharacter = true;
        [Tooltip("Exposed weapon station: FPS follows the weapon camera pivot and hides its operator. Seated cockpit stations leave this disabled.")]
        public bool weaponMountedView;
        [Tooltip("Functional station without an authored cabin chair; use the external camera for this seat.")]
        public bool externalView;
        [Tooltip("Leaving this station puts the local player in the cabin, rather than outside the hull.")]
        public bool disembarkInside;
        [Tooltip("Authored first-person eye point, relative to the vehicle seat.")]
        public Transform eye;
        public Transform leftGrip, rightGrip;
        [Tooltip("Marker position on the 160-pixel vehicle seat diagram. Front of vehicle points up.")]
        public Vector2 diagramPosition;
        [Tooltip("Show driver/pilot instruments at a monitoring seat, such as a commander or co-pilot station.")]
        public bool showVehicleInstruments;
        [NonSerialized] public VehicleOccupant occupant, reservation;
        [NonSerialized] public float reservedUntil;
    }
    public static event Action<Vehicle> Spawned;
    public static readonly List<Vehicle> Active = new List<Vehicle>();
    public string displayName;
    public Sprite seatDiagram;
    [HideInInspector] public Vector2 seatDiagramCenter;
    [HideInInspector] public float seatDiagramHalfExtent=1;
    public VehicleMotor motor;
    public JetFlightAssist jetAssist = new JetFlightAssist();
    public HelicopterFlightAssist helicopterAssist = new HelicopterFlightAssist();
    public bool IsAircraft => motor is HelicopterMotor || motor is JetMotor;
    public bool FlightAssistEnabled
    {
        get => motor is JetMotor ? jetAssist.assistEnabled : helicopterAssist.assistEnabled;
        set { if (motor is JetMotor) jetAssist.assistEnabled = value; else helicopterAssist.assistEnabled = value; }
    }
    public Quaternion RequestedFlightAttitude => motor is JetMotor ? jetAssist.RequestedAttitude : helicopterAssist.RequestedAttitude;
    public VehicleTurret turret;
    public ParticleSystem destructionEffect;
    public Transform cameraTarget;
    [Tooltip("Physical aim point used by missile seekers and their lock marker; independent of camera framing.")]
    public Transform targetPoint;
    [Tooltip("Disable when the model has no rendered cabin. Occupants are hidden and both camera modes use the external view.")]
    public bool hasRenderedInterior = true;
    [Tooltip("Show an external TPS camera monitor in cockpit view, for vehicles with limited windshield visibility.")]
    public bool cockpitExternalMonitor;
    [Tooltip("Extra space around the chassis box from which players may enter.")]
    public Vector3 entryPadding = new Vector3(1.5f, 1f, 1.5f);
    public Seat[] seats;
    public float maxHealth = 900f;
    [Range(0f,1f)] public float bulletDamageMultiplier=.5f;
    [Range(0f,1f)] public float explosiveDamageMultiplier=.8f;
    public float Health { get; private set; }
    public bool IsDestroyed => Health <= 0;
    public Rigidbody Body { get; private set; }
    public VehicleInput DriverInput { get; set; }
    public event Action<Vehicle> Destroyed;
    public Func<GameObject, bool> DamageFilter;
    public VehicleOccupant Driver => seats[0].occupant;
    public bool IsOccupied
    {
        get
        {
            foreach (var s in seats)
            if (s.occupant)
                return true;
            return false;
        }
    }
    public bool CanExit => !IsDestroyed && Body.linearVelocity.magnitude < 3f && motor.CanDisembark;
    NavMeshObstacle obstacle;
    Bounds colliderBounds;
    bool colliderBoundsReady;
    // The authored carving box describes the chassis, in vehicle-local coordinates.
    public Bounds NavigationBounds
    {
        get
        {
            if (!obstacle) obstacle = GetComponent<NavMeshObstacle>();
            if (obstacle) return new Bounds(obstacle.center, obstacle.size);
            if (!colliderBoundsReady)
            {
                bool found = false;
                foreach (var box in GetComponentsInChildren<BoxCollider>(true))
                {
                    if (box.isTrigger) continue;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var point = box.center + Vector3.Scale(box.size * .5f,
                            new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                        point = transform.InverseTransformPoint(box.transform.TransformPoint(point));
                        if (!found) { colliderBounds = new Bounds(point, Vector3.zero); found = true; }
                        else colliderBounds.Encapsulate(point);
                    }
                }
                if (!found) colliderBounds = new Bounds(Vector3.zero, new Vector3(2, 2, 4));
                colliderBoundsReady = true;
            }
            return colliderBounds;
        }
    }

    void Awake()
    {
        Body = GetComponent<Rigidbody>();
        int characters=LayerMask.NameToLayer("Characters");
        foreach(var collider in GetComponentsInChildren<Collider>(true))
            if(characters>=0 && !(collider is WheelCollider) && collider.attachedRigidbody==Body)
                collider.excludeLayers &= ~(1<<characters);
        if(Body.collisionDetectionMode==CollisionDetectionMode.Discrete)
            Body.collisionDetectionMode=CollisionDetectionMode.Continuous;
        obstacle = GetComponent<NavMeshObstacle>();
        Health = maxHealth;
    }


    void OnEnable()
    {
        Active.Add(this);
        Spawned?.Invoke(this);
    }
    void OnDisable()
    {
        Active.Remove(this);
        foreach (var s in seats)
        if (s.occupant)
            s.occupant.Leave(true);
    }
    void FixedUpdate()
    {
        if (!IsDestroyed) motor.Simulate(PrepareDriverInput(DriverInput, Driver), Driver);
        // Carving is only useful while parked; a dynamic body never also owns a NavMeshAgent.
        if (obstacle)
            obstacle.enabled = !Driver && Body.linearVelocity.sqrMagnitude < .1f;
    }
    public VehicleInput PrepareDriverInput(VehicleInput input, bool powered)
        => motor is JetMotor jet ? jetAssist.ProvideInput(input, jet, powered)
            : motor is HelicopterMotor helicopter ? helicopterAssist.ProvideInput(input, helicopter, powered) : input;
    public bool CanBoard(VehicleOccupant actor, int index)
    {
        if (IsDestroyed || actor.IsMounted || actor.life.health.IsDead || index < 0 || index >= seats.Length)
            return false;
        var seat = seats[index];
        // Seats being authored are unavailable until their placement and exit are assigned.
        if (!seat.anchor || !seat.exit)
            return false;
        if (seat.occupant || (!actor.IsLocal && seat.reservation && seat.reservation != actor && Time.time < seat.reservedUntil))
            return false;
        foreach (var s in seats)
            if (s.occupant && s.occupant.faction >= 0 && actor.faction >= 0 && s.occupant.faction != actor.faction)
                return false;
        return true;
    }
    public int FirstAvailableSeat(VehicleOccupant actor)
    {
        for (int i = 0; i < seats.Length; i++) if (CanBoard(actor, i)) return i;
        return -1;
    }
    // Human entry prefers vacant seats, then a friendly AI seat in the same authored order.
    public int FirstEntrySeat(VehicleOccupant actor)
    {
        int available=FirstAvailableSeat(actor);if(available>=0)return available;
        for(int i=0;i<seats.Length;i++)if(CanDisplaceAI(actor,i))return i;
        return -1;
    }
    bool CanDisplaceAI(VehicleOccupant actor,int index)
    {
        if(!actor||!actor.IsLocal||actor.IsMounted||actor.life.health.IsDead||IsDestroyed||index<0||index>=seats.Length)return false;
        var seat=seats[index];var occupant=seat.occupant;
        if(!seat.anchor||!seat.exit||!occupant||occupant.IsLocal||occupant.life.health.IsDead
            ||!(occupant.soldier.inputSource is ArenaAIInput))return false;
        foreach(var other in seats)
            if(other.occupant&&actor.faction>=0&&other.occupant.faction>=0&&actor.faction!=other.occupant.faction)return false;
        return true;
    }
    public Transform NearestEntryPoint(Vector3 position)
    {
        Transform closest = transform;
        float closestDistance = float.PositiveInfinity;
        foreach (var seat in seats)
        {
            if (!seat.exit) continue;
            float distance = (seat.exit.position - position).sqrMagnitude;
            if (distance < closestDistance)
            {
                closest = seat.exit;
                closestDistance = distance;
            }
        }
        return closest;
    }
    public Bounds EntryBounds
    {
        get
        {
            var bounds = NavigationBounds;
            bounds.Expand(entryPadding * 2f);
            float bottom = Mathf.Min(bounds.min.y, -.5f);
            // Include landing gear, rather than starting the box at the elevated fuselage.
            if (motor is HelicopterMotor aircraft)
                foreach (var gear in aircraft.landingGear)
                    bottom = Mathf.Min(bottom, transform.InverseTransformPoint(gear.transform.TransformPoint(gear.center)).y - gear.radius - .5f);
            if (motor is JetMotor jet)
                foreach (var gear in jet.landingGear)
                    bottom = Mathf.Min(bottom, transform.InverseTransformPoint(gear.transform.TransformPoint(gear.center)).y - gear.radius - .5f);
            bounds.SetMinMax(new Vector3(bounds.min.x, bottom, bounds.min.z), bounds.max);
            return bounds;
        }
    }
    public bool IsInsideEntryBox(Vector3 position) => EntryBounds.Contains(transform.InverseTransformPoint(position));
    void DrawEntryBox()
    {
        var bounds = EntryBounds;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, .55f, .12f, .65f);
        Gizmos.DrawWireCube(bounds.center, bounds.size);
        Gizmos.matrix = Matrix4x4.identity;
    }
    public bool BoardFirstAvailable(VehicleOccupant actor) => Board(actor, FirstEntrySeat(actor));
    public bool Reserve(VehicleOccupant actor, int index)
    {
        if (!CanBoard(actor, index))
            return false;
        seats[index].reservation = actor;
        seats[index].reservedUntil = Time.time + 8f;
        return true;
    }
    public void ReleaseReservation(VehicleOccupant actor)
    {
        foreach (var s in seats)
        if (s.reservation == actor)
            s.reservation = null;
    }
    public bool Board(VehicleOccupant actor, int index)
    {
        if ((!CanBoard(actor, index) && !CanDisplaceAI(actor,index)) || (!actor.IsLocal && Body.linearVelocity.magnitude > 3f)
            || !IsInsideEntryBox(actor.transform.position))
            return false;
        if (!actor.IsLocal && !motor.CanDisembark)
            return false;
        // Validate entry before ejecting anyone; a remote/out-of-range request has no side effects.
        if(seats[index].occupant&&!seats[index].occupant.Leave(true))return false;
        seats[index].reservation = null;
        seats[index].occupant = actor;
        actor.Mount(this, index);
        return true;
    }
    // Weapon damage is reduced once here. Collisions and forced cleanup remain raw damage.
    public bool ApplyBulletDamage(float amount, GameObject source) => ApplyDamage(amount*bulletDamageMultiplier,source);
    public bool ApplyExplosiveDamage(float amount, GameObject source) => ApplyDamage(amount*explosiveDamageMultiplier,source);
    public bool ApplyDamage(float amount, GameObject source)
    {
        if (IsDestroyed || amount <= 0 || (DamageFilter != null && !DamageFilter(source)))
            return false;
        float previous=Health;
        Health = Mathf.Max(0, Health - amount);
        CombatEvents.ConfirmDamage(source,gameObject,previous-Health,IsDestroyed);
        if (!IsDestroyed)
            return true;
        DriverInput = default;
        foreach (var s in seats)
        if (s.occupant)
        {
            var actor = s.occupant;
            actor.Leave(true);
            actor.life.health.ApplyDamage(actor.life.health.maxHealth, source);
        }
        motor.enabled = false;
        if (turret) turret.enabled = false;
        if (TryGetComponent<VehicleLauncher>(out var launcher)) launcher.enabled = false;
        foreach (var s in seats) s.reservation = null;
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", new Color(.16f, .17f, .18f));
            r.SetPropertyBlock(block);
        }
        Destroyed?.Invoke(this);
        return true;
    }
    void OnCollisionEnter(Collision collision)
    {
        // Rocket impacts are accounted for by their explosion, not crash damage.
        if(collision.collider.GetComponentInParent<VehicleRocket>())return;
        float speed = 0;
        for(int i=0;i<collision.contactCount;i++)
        {
            Vector3 normal=collision.GetContact(i).normal;
            float impact=Mathf.Abs(Vector3.Dot(collision.relativeVelocity,normal));
            // Driveable ramp/ground contacts only damage the car for a hard downward landing.
            if(motor is HummerMotor && normal.y>=.6f)
                impact=Mathf.Max(0,-collision.relativeVelocity.y);
            speed=Mathf.Max(speed,impact);
        }
        if (speed > 12f)
            ApplyDamage((speed - 12f) * 25f, null);
    }
    void OnDrawGizmosSelected()
    {
        DrawEntryBox();
        foreach (var s in seats)
        if (s.anchor && s.exit)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(s.exit.position, .35f);
            Gizmos.DrawLine(s.anchor.position, s.exit.position);
        }
    }
}

public struct VehicleInput
{
    public Vector2 move;
    public float yaw, lift, heading, movementHeading, viewPitch, pitchRate;
    public bool brake, handbrake, boost, directionalFlight, aim;
}

public abstract class VehicleMotor : MonoBehaviour
{
    public Vehicle vehicle;
    public abstract bool CanDisembark
    {
        get;
    }
    public abstract void Simulate(VehicleInput input, bool powered);
}
