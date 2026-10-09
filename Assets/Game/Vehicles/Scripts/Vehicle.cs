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
        [NonSerialized] public VehicleOccupant occupant, reservation;
        [NonSerialized] public float reservedUntil;
    }
    public static event Action<Vehicle> Spawned;
    public static readonly List<Vehicle> Active = new List<Vehicle>();
    public string displayName;
    public VehicleMotor motor;
    public VehicleTurret turret;
    public ParticleSystem destructionEffect;
    public Transform cameraTarget;
    public Seat[] seats;
    public float maxHealth = 900f;
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
        motor.Simulate(IsDestroyed ? default : DriverInput, Driver && !IsDestroyed);
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
    public bool ApplyDamage(float amount, GameObject source)
    {
        if (IsDestroyed || amount <= 0 || (DamageFilter != null && !DamageFilter(source)))
            return false;
        Health = Mathf.Max(0, Health - amount);
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
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", new Color(.16f, .17f, .18f));
            r.SetPropertyBlock(block);
        }
        var effect = Instantiate(destructionEffect, transform.position + Vector3.up, Quaternion.identity);
        effect.transform.localScale = Vector3.one * 2f;
        effect.Play(true);
        Destroy(effect.gameObject, 2f);
        Destroyed?.Invoke(this);
        return true;
    }
    void OnCollisionEnter(Collision collision)
    {
        float speed = collision.relativeVelocity.magnitude;
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
    public float yaw, lift, heading, viewPitch;
    public bool brake, handbrake, boost, directionalFlight;
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
