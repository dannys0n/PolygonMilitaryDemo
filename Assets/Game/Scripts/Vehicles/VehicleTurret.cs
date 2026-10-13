using UnityEngine;

public class VehicleTurret : MonoBehaviour
{
    public Vehicle vehicle;
    public Transform yawPivot, pitchPivot, muzzle, cameraPoint;
    public ParticleSystem muzzleFlash;
    public Transform shellEjection;
    public SoldierShellCasing shellCasing;
    public Sprite weaponIcon;
    public string weaponName = "MINIGUN";
    public bool spinBarrels = true;
    public Vehicle.SeatRole controlRole = Vehicle.SeatRole.Gunner;
    public bool explosiveVehicleDamage;
    public ParticleSystem impactEffect;
    public VehicleRocket projectile;
    public bool fixedMount;
    public bool ejectCasings = true;
    public AnimationClip cameraShake;
    public Transform barrelAssembly;
    public float barrelSpinUp = 1200f, barrelSpinDown = 400f;
    float barrelAngle, barrelSpeed, lastBarrelShot=float.NegativeInfinity;
    Quaternion barrelRest;
    public float casingScale = 1.7f;
    int pendingFlashes, pendingOwnerCasings;

    public float damage = 34, range = 120, roundsPerMinute = 650, turnRate = 100;
    public Vector2 pitchLimits = new Vector2(-20, 45);
    Vector3 aimDirection = Vector3.forward;
    float nextShot;
    public float ReadyIn => Mathf.Max(0,nextShot-Time.time);
    public bool IsSingleShot => roundsPerMinute < 60;
    Vector3 aimPoint;
    bool aimAtPoint;
    public Vector3 RequestedAimDirection => aimDirection;
    public event System.Action<VehicleOccupant, Vector3> ShotFired;
    public void ResetCadence() => nextShot = 0;
    void Start()
    {
        aimDirection=vehicle.transform.forward;
        if(barrelAssembly) barrelRest=barrelAssembly.localRotation;
    }
    public void AimAt(Ray view)
    {
        float distance = range;
        foreach(var hit in Physics.RaycastAll(view,range,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide))
        {
            if(hit.transform.IsChildOf(vehicle.transform)||hit.collider is CharacterController)continue;
            if(hit.collider.isTrigger&&!hit.collider.GetComponent<SoldierHitRegion>())continue;
            distance=Mathf.Min(distance,hit.distance);
        }
        aimPoint=view.GetPoint(distance);
        aimDirection=view.direction;
        aimAtPoint=true;
    }
    public void Aim(Vector3 direction)
    {
        aimAtPoint=false;
        if (direction.sqrMagnitude > .001f)
            aimDirection = direction.normalized;
    }
    int lastAimFrame = -1;
    void UpdateAim()
    {
        if (lastAimFrame == Time.frameCount) return;
        lastAimFrame = Time.frameCount;
        Vector3 direction = aimAtPoint ? (aimPoint-muzzle.position).normalized : aimDirection;
        Vector3 local = yawPivot.parent.InverseTransformDirection(direction);
        float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        float up = Mathf.Clamp(Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, pitchLimits.x, pitchLimits.y);
        if (!fixedMount)
        {
            yawPivot.localRotation = Quaternion.RotateTowards(yawPivot.localRotation, Quaternion.Euler(0, yaw, 0), turnRate * Time.deltaTime);
            pitchPivot.localRotation = Quaternion.RotateTowards(pitchPivot.localRotation, Quaternion.Euler(-up, 0, 0), turnRate * Time.deltaTime);
        }
    }
    void LateUpdate()
    {
        UpdateAim();
        bool firing=Time.time-lastBarrelShot < 60f/roundsPerMinute+.02f;
        barrelSpeed=Mathf.MoveTowards(barrelSpeed,firing ? roundsPerMinute/6f : 0,(firing?barrelSpinUp:barrelSpinDown)*Time.deltaTime);
        barrelAngle=(barrelAngle+barrelSpeed*6f*Time.deltaTime)%360;
        if (spinBarrels && barrelAssembly) barrelAssembly.localRotation=barrelRest*Quaternion.AngleAxis(barrelAngle,Vector3.forward);
        // Sample the final turret pose so effects follow the receiver during turns.
        if (pendingFlashes > 0 && muzzleFlash) muzzleFlash.Emit(4 * pendingFlashes);
        pendingFlashes = 0;
        while (pendingOwnerCasings > 0)
        {
            pendingOwnerCasings--;
            if(shellCasing && shellEjection)
            {
                var casing = Instantiate(shellCasing, shellEjection.position, shellEjection.rotation);
                casing.Launch(shellEjection.rotation, vehicle.Body.GetPointVelocity(shellEjection.position), casingScale);
            }
        }
    }
    void OnDisable()
    {
        pendingFlashes = pendingOwnerCasings = 0;

        barrelSpeed=0;
        lastBarrelShot=float.NegativeInfinity;
        if(muzzleFlash) muzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
    public bool TryFire(VehicleOccupant gunner)
    {
        if (vehicle.IsDestroyed || !gunner.IsMounted || gunner.MountedVehicle != vehicle
            || vehicle.seats[gunner.SeatIndex].role != controlRole || Time.time < nextShot)
            return false;
        UpdateAim();
        nextShot = Time.time + 60f / roundsPerMinute;
        RaycastHit nearest = default;
        bool found = false;
        foreach (var hit in projectile ? System.Array.Empty<RaycastHit>() : Physics.RaycastAll(muzzle.position, muzzle.forward, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
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
            if(impactEffect)
            {
                var effect=Instantiate(impactEffect,nearest.point+nearest.normal*.03f,Quaternion.FromToRotation(Vector3.forward,nearest.normal));
                effect.Play(true);
                float duration=0;
                foreach(var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main=particles.main;
                    duration=Mathf.Max(duration,main.startDelay.constantMax+main.duration+main.startLifetime.constantMax);
                }
                Destroy(effect.gameObject,duration+1f);
            }
            var region = nearest.collider.GetComponent<SoldierHitRegion>();
            if (region)
                region.health.ApplyDamage(damage, gunner.gameObject);
            else
            {
                var other = nearest.collider.GetComponentInParent<Vehicle>();
                if (other)
                {
                    if(explosiveVehicleDamage) other.ApplyExplosiveDamage(damage, gunner.gameObject);
                    else other.ApplyBulletDamage(damage, gunner.gameObject);
                }
            }
        }
        if (projectile)
        {
            var round = Instantiate(projectile, muzzle.position + muzzle.forward * projectile.launchClearance, muzzle.rotation);
            round.Launch(vehicle, gunner.gameObject);
        }
        // Fire cadence is the gate for this prototype weapon; launcher reserves are managed separately.
        lastBarrelShot=Time.time;
        pendingFlashes++;
        if (ejectCasings && gunner.IsLocal && gunner.soldier.inputSource.isActiveAndEnabled) pendingOwnerCasings++;
        if(gunner.IsLocal)gunner.PlayVehicleCameraShake();
        ShotFired?.Invoke(gunner, muzzle.position);
        return true;
    }
}
