using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Shared seat ownership and lifecycle; motors accept the same commands from humans and AI.
[RequireComponent(typeof(Rigidbody))]
public class Vehicle : MonoBehaviour
{
    public enum SeatRole
    {
        Driver, Gunner, Passenger
    }
    [Serializable]
    public class Seat
    {
        public string name;
        public SeatRole role;
        public Transform anchor, exit;
        public Vector2 diagramPosition;
        [NonSerialized] public VehicleOccupant occupant, reservation;
        [NonSerialized] public float reservedUntil;
    }
    public static event Action<Vehicle> Spawned;
    public static readonly List<Vehicle> Active = new List<Vehicle>();
    public string displayName;
    public Sprite seatDiagram;
    public VehicleMotor motor;
    public VehicleTurret turret;
    public ParticleSystem destructionEffect;
    public Transform cameraTarget;
    public Seat[] seats;
    public float maxHealth = 900f;
    [Range(0f,1f)] public float bulletDamageMultiplier=1f;
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
    // The authored carving box describes the chassis, in vehicle-local coordinates.
    public Bounds NavigationBounds => new Bounds(obstacle.center, obstacle.size);

    void Awake()
    {
        Body = GetComponent<Rigidbody>();
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
        if (!IsDestroyed) motor.Simulate(DriverInput, Driver);
        // Carving is only useful while parked; a dynamic body never also owns a NavMeshAgent.
        if (obstacle)
            obstacle.enabled = !Driver && Body.linearVelocity.sqrMagnitude < .1f;
    }
    public bool CanBoard(VehicleOccupant actor, int index)
    {
        if (IsDestroyed || actor.IsMounted || actor.life.health.IsDead || index < 0 || index >= seats.Length)
            return false;
        var seat = seats[index];
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
    public Transform NearestEntryPoint(Vector3 position)
    {
        Transform closest = seats[0].exit;
        foreach (var seat in seats)
            if ((seat.exit.position - position).sqrMagnitude < (closest.position - position).sqrMagnitude) closest = seat.exit;
        return closest;
    }
    public bool BoardFirstAvailable(VehicleOccupant actor) => Board(actor, FirstAvailableSeat(actor));
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
        if (!CanBoard(actor, index) || (!actor.IsLocal && Body.linearVelocity.magnitude > 3f)
            || Vector3.Distance(actor.transform.position, NearestEntryPoint(actor.transform.position).position) > 3.2f)
            return false;
        if (!actor.IsLocal && !motor.CanDisembark)
            return false;
        seats[index].reservation = null;
        seats[index].occupant = actor;
        actor.Mount(this, index);
        return true;
    }
    // Explosions and collisions use ApplyDamage directly, bypassing bullet armor.
    public bool ApplyBulletDamage(float amount, GameObject source) => ApplyDamage(amount*bulletDamageMultiplier,source);
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
