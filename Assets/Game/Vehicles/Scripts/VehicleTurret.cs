using UnityEngine;

public class VehicleTurret : MonoBehaviour
{
    public Vehicle vehicle;
    public Transform yawPivot, pitchPivot, muzzle, cameraPoint;
    public ParticleSystem muzzleFlash;
    public Transform shellEjection;
    public SoldierShellCasing shellCasing;
    public Sprite weaponIcon;
    public float casingScale = 1.7f;
    int pendingFlashes, pendingOwnerCasings;
    public float damage = 34, range = 120, roundsPerMinute = 650, turnRate = 100;
    public Vector2 pitchLimits = new Vector2(-20, 45);
    Vector3 aimDirection = Vector3.forward;
    float nextShot;
    public event System.Action<VehicleOccupant, Vector3> ShotFired;
    void Start() => aimDirection = vehicle.transform.forward;
    public void Aim(Vector3 direction)
    {
        if (direction.sqrMagnitude > .001f)
            aimDirection = direction.normalized;
    }
    void LateUpdate()
    {
        Vector3 local = yawPivot.parent.InverseTransformDirection(aimDirection);
        float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        float up = Mathf.Clamp(Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, pitchLimits.x, pitchLimits.y);
        yawPivot.localRotation = Quaternion.RotateTowards(yawPivot.localRotation, Quaternion.Euler(0, yaw, 0), turnRate * Time.deltaTime);
        pitchPivot.localRotation = Quaternion.RotateTowards(pitchPivot.localRotation, Quaternion.Euler(-up, 0, 0), turnRate * Time.deltaTime);
        // Sample the final turret pose so effects follow the receiver during turns.
        if (pendingFlashes > 0) muzzleFlash.Emit(4 * pendingFlashes);
        pendingFlashes = 0;
        while (pendingOwnerCasings > 0)
        {
            pendingOwnerCasings--;
            var casing = Instantiate(shellCasing, shellEjection.position, shellEjection.rotation);
            casing.Launch(shellEjection.rotation, vehicle.Body.GetPointVelocity(shellEjection.position), casingScale);
        }
    }
    void OnDisable()
    {
        pendingFlashes = pendingOwnerCasings = 0;
        muzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
    public bool TryFire(VehicleOccupant gunner)
    {
        if (vehicle.IsDestroyed || !gunner.IsMounted || gunner.MountedVehicle != vehicle
            || vehicle.seats[gunner.SeatIndex].role != Vehicle.SeatRole.Gunner || Time.time < nextShot)
            return false;
        nextShot = Time.time + 60f / roundsPerMinute;
        RaycastHit nearest = default;
        bool found = false;
        foreach (var hit in Physics.RaycastAll(muzzle.position, muzzle.forward, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            if (hit.transform.IsChildOf(vehicle.transform) || hit.collider is CharacterController)
                continue;
            if (hit.collider.isTrigger && !hit.collider.GetComponent<SoldierHitRegion>())
                continue;
            if (!found || hit.distance < nearest.distance)
            {
                found = true;
                nearest = hit;
            }
        }
        if (found)
        {
            var region = nearest.collider.GetComponent<SoldierHitRegion>();
            if (region)
                region.health.ApplyDamage(damage, gunner.gameObject);
            else
            {
                var other = nearest.collider.GetComponentInParent<Vehicle>();
                if (other)
                    other.ApplyDamage(damage, gunner.gameObject);
            }
        }
        // Infinite ammunition: fire cadence is the only ammunition-related gate.
        pendingFlashes++;
        if (gunner.IsLocal && gunner.soldier.inputSource.isActiveAndEnabled) pendingOwnerCasings++;
        ShotFired?.Invoke(gunner, muzzle.position);
        return true;
    }
}
