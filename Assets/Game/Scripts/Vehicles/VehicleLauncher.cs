using UnityEngine;

public class VehicleLauncher : MonoBehaviour
{
    public Vehicle vehicle;
    public Vehicle.SeatRole controlRole = Vehicle.SeatRole.Driver;
    public VehicleRocket projectile;
    public Sprite weaponIcon;
    public ParticleSystem launchFlash;
    public AnimationClip cameraShake;
    public Transform[] muzzles;
    public GameObject[] tubeRounds = System.Array.Empty<GameObject>();
    public event System.Action<VehicleOccupant,Vector3> ShotFired;
    public float interval = .2f;
    public int capacity = 8;
    public float rechargeInterval = .75f;
    [Tooltip("Restore the entire missile load after the delay from the last shot, rather than one missile per interval.")]
    public bool rechargeFullMagazine;
    [Tooltip("Require an uninterrupted seeker lock before firing. Prototype launchers can leave this off.")]
    public bool requiresLock;
    [Min(.05f)] public float lockDuration = 1.25f;
    public enum TargetLockState { Searching, Acquiring, Locked }
    public TargetLockState LockState { get; private set; }
    public Vehicle LockTarget { get; private set; }
    public float LockProgress { get; private set; }
    public bool IsLocked => LockState == TargetLockState.Locked;
    public float LastLockLostTime { get; private set; } = float.NegativeInfinity;
    public float LastRejectedFireTime { get; private set; } = float.NegativeInfinity;
    VehicleMissileGuidance seeker;
    VehicleWeaponSystems selectedSystems;
    int missiles;
    float nextRecharge;
    public int Missiles { get { RefreshReload(); return missiles; } }
    public float RechargeRemaining { get { RefreshReload(); return missiles < capacity ? Mathf.Max(0,nextRecharge-Time.time) : 0; } }
    void RefreshReload() { if (vehicle && !vehicle.IsDestroyed) Recharge(Time.time); }
    void Awake() { missiles=capacity; nextRecharge=float.PositiveInfinity; }
    void Update()
    {
        if (!vehicle || vehicle.IsDestroyed) { ClearLock(); return; }
        Recharge(Time.time);
        UpdateLock(Time.deltaTime);
    }
    void OnEnable()
    {
        if (vehicle) selectedSystems = vehicle.GetComponent<VehicleWeaponSystems>();
        if (selectedSystems) selectedSystems.Changed += OnWeaponChanged;
    }
    void OnDisable()
    {
        if (selectedSystems) selectedSystems.Changed -= OnWeaponChanged;
        ClearLock();
    }
    void OnWeaponChanged() { if (!IsSelected()) ClearLock(); }
    bool IsSelected()
    {
        bool occupied = false;
        foreach (var seat in vehicle.seats) if (seat.role == controlRole && seat.occupant) { occupied = true; break; }
        if (!occupied) return false;
        var systems = vehicle.GetComponent<VehicleWeaponSystems>();
        return !systems || systems.Current(controlRole)?.launcher == this;
    }
    void ClearLock()
    {
        if (LockState != TargetLockState.Searching) LastLockLostTime = Time.time;
        LockTarget = null; LockProgress = 0; LockState = TargetLockState.Searching;
    }
    void UpdateLock(float deltaTime)
    {
        if (!requiresLock || !IsSelected() || !projectile || muzzles.Length == 0) { ClearLock(); return; }
        if (!seeker) seeker = projectile.GetComponent<VehicleMissileGuidance>();
        if (!seeker) { ClearLock(); return; }
        Ray launchRay = NextShotRay;
        if (LockState != TargetLockState.Searching && (!LockTarget || !seeker.CanAcquire(vehicle, LockTarget, RequestedAimRay, launchRay))) ClearLock();
        if (!LockTarget)
        {
            LockTarget = seeker.FindTarget(vehicle, RequestedAimRay, launchRay);
            LockProgress = 0;
        }
        if (!LockTarget) { LockState = TargetLockState.Searching; return; }
        LockProgress = Mathf.Clamp01(LockProgress + deltaTime / Mathf.Max(.05f, lockDuration));
        LockState = LockProgress >= 1 ? TargetLockState.Locked : TargetLockState.Acquiring;
    }
    void Recharge(float now)
    {
        while(missiles<capacity && now>=nextRecharge)
        {
            int rounds = rechargeFullMagazine ? capacity - missiles : 1;
            for (int i = 0; i < rounds; i++)
            {
                if(tubeRounds.Length>0)
                {
                    int tube=((barrel-capacity+missiles)%tubeRounds.Length+tubeRounds.Length)%tubeRounds.Length;
                    if(tubeRounds[tube]) tubeRounds[tube].SetActive(true);
                }
                missiles++;
            }
            nextRecharge += Mathf.Max(.01f,rechargeInterval);
        }
        if(missiles==capacity) nextRecharge=float.PositiveInfinity;
    }
    float nextShot;
    int barrel;
    Ray requestedAimRay;
    public Ray RequestedAimRay => requestedAimRay.direction.sqrMagnitude > .001f ? requestedAimRay : AimRay;
    public void SetAim(Ray view) => requestedAimRay = view;
    public Transform NextMuzzle => muzzles[barrel % muzzles.Length];
    // The sight represents the launcher system, independently of the alternating firing tube.
    public Ray AimRay
    {
        get
        {
            Vector3 origin = Vector3.zero, direction = Vector3.zero;
            foreach (var muzzle in muzzles) { origin += muzzle.position; direction += muzzle.forward; }
            direction.Normalize();
            return new Ray(origin / muzzles.Length + direction * projectile.launchClearance,
                direction * projectile.speed + vehicle.Body.linearVelocity);
        }
    }
    public Ray NextShotRay
    {
        get
        {
            var muzzle=NextMuzzle;
            return new Ray(muzzle.position+muzzle.forward*projectile.launchClearance,
                muzzle.forward*projectile.speed+vehicle.Body.linearVelocity);
        }
    }
    public float NextShotRange => (NextMuzzle.forward*projectile.speed+vehicle.Body.linearVelocity).magnitude*projectile.lifetime;
    public Vector3 PredictedImpactPoint
    {
        get
        {
            if (projectile.useGravity)
            {
                Vector3 origin=NextShotRay.origin;
                Vector3 velocity=NextMuzzle.forward*projectile.speed+vehicle.Body.linearVelocity;
                Vector3 previous=origin;
                const int steps=32;
                for(int i=1;i<=steps;i++)
                {
                    float time=projectile.lifetime*i/steps;
                    Vector3 point=origin+velocity*time+Physics.gravity*(.5f*time*time);
                    Vector3 segment=point-previous;
                    float segmentDistance=segment.magnitude;bool found=false;
                    foreach(var hit in Physics.RaycastAll(previous,segment.normalized,segmentDistance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                        if(!hit.transform.IsChildOf(vehicle.transform)&&!hit.collider.GetComponentInParent<VehicleRocket>()&&hit.distance<segmentDistance)
                        {segmentDistance=hit.distance;found=true;}
                    if(found)return previous+segment.normalized*segmentDistance;
                    previous=point;
                }
                return previous;
            }
            Ray ray=NextShotRay;
            float nearest=NextShotRange;
            foreach(var hit in Physics.RaycastAll(ray,nearest,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                if(!hit.transform.IsChildOf(vehicle.transform)&&!hit.collider.GetComponentInParent<VehicleRocket>())
                    nearest=Mathf.Min(nearest,hit.distance);
            return ray.GetPoint(nearest);
        }
    }
    public Vector3 DirectionToAimPoint(Ray view, float range)
    {
        float distance = range;
        foreach (var hit in Physics.RaycastAll(view, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(vehicle.transform) && !(SoldierPhysicsBody.IsMotorCollider(hit.collider)) && !hit.collider.GetComponentInParent<VehicleRocket>()) distance = Mathf.Min(distance,hit.distance);
        Vector3 direction = (view.GetPoint(distance)-AimRay.origin).normalized;
        Vector3 lateralVelocity = Vector3.ProjectOnPlane(vehicle.Body.linearVelocity,direction);
        float forwardSpeed = Mathf.Sqrt(Mathf.Max(1,projectile.speed*projectile.speed-lateralVelocity.sqrMagnitude));
        return (direction*forwardSpeed-lateralVelocity).normalized;
    }
    public float ReadyIn
    {
        get
        {
            RefreshReload();
            return Mathf.Max(Mathf.Max(0,nextShot-Time.time),missiles==0 ? RechargeRemaining : 0);
        }
    }
    public bool TryFire(VehicleOccupant pilot)
    {
        Recharge(Time.time);
        if (missiles==0 || vehicle.IsDestroyed || pilot.MountedVehicle != vehicle || vehicle.seats[pilot.SeatIndex].role != controlRole || Time.time < nextShot)
            return false;
        if (TryGetComponent<VehicleLauncherMount>(out var mount)) mount.UpdateAim();
        if (requiresLock)
        {
            // Revalidate immediately: a lost target or an unselected weapon cannot fire a stale lock.
            if (!seeker) seeker = projectile.GetComponent<VehicleMissileGuidance>();
            if (!IsSelected() || !IsLocked || !seeker || !seeker.CanAcquire(vehicle, LockTarget, RequestedAimRay, NextShotRay))
            {
                if (IsLocked) ClearLock();
                LastRejectedFireTime = Time.time;
                return false;
            }
        }
        var validatedTarget = requiresLock ? LockTarget : null;
        nextShot = Time.time + interval;
        if(rechargeFullMagazine || missiles==capacity) nextRecharge=Time.time+Mathf.Max(.01f,rechargeInterval);
        missiles--;
        int tubeIndex=barrel++ % muzzles.Length;
        var muzzle = muzzles[tubeIndex];
        if(tubeRounds.Length>tubeIndex) tubeRounds[tubeIndex].SetActive(false);
        var rocket = Instantiate(projectile, muzzle.position + muzzle.forward * projectile.launchClearance, muzzle.rotation);
        rocket.Launch(vehicle, pilot.gameObject, weaponIcon);
        if (launchFlash)
        {
            var flash = Instantiate(launchFlash, muzzle.position, muzzle.rotation);
            flash.Emit(1);
            Destroy(flash.gameObject, .5f);
        }
        if (pilot.IsLocal) pilot.PlayVehicleCameraShake(cameraShake);
        if (rocket.TryGetComponent<VehicleMissileGuidance>(out var guidance)) guidance.Initialize(vehicle, RequestedAimRay, this, validatedTarget);
        ShotFired?.Invoke(pilot,muzzle.position);
        return true;
    }
}
