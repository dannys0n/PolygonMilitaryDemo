using UnityEngine;

public class VehicleLauncher : MonoBehaviour
{
    public Vehicle vehicle;
    public Vehicle.SeatRole controlRole = Vehicle.SeatRole.Driver;
    public VehicleRocket projectile;
    public Sprite weaponIcon;
    public Transform[] muzzles;
    public GameObject[] tubeRounds = System.Array.Empty<GameObject>();
    public float interval = .2f;
    public int capacity = 8;
    public float rechargeInterval = .75f;
    int missiles;
    float nextRecharge;
    public int Missiles => missiles;
    public float RechargeRemaining => missiles < capacity ? Mathf.Max(0,nextRecharge-Time.time) : 0;
    void Awake() { missiles=capacity; nextRecharge=float.PositiveInfinity; }
    void Update() { if(!vehicle.IsDestroyed) Recharge(Time.time); }
    void Recharge(float now)
    {
        while(missiles<capacity && now>=nextRecharge)
        {
            if(tubeRounds.Length>0)
            {
                int tube=((barrel-capacity+missiles)%tubeRounds.Length+tubeRounds.Length)%tubeRounds.Length;
                tubeRounds[tube].SetActive(true);
            }
            missiles++; nextRecharge += rechargeInterval;
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
            if (!hit.transform.IsChildOf(vehicle.transform) && !(hit.collider is CharacterController) && !hit.collider.GetComponentInParent<VehicleRocket>()) distance = Mathf.Min(distance,hit.distance);
        Vector3 direction = (view.GetPoint(distance)-AimRay.origin).normalized;
        Vector3 lateralVelocity = Vector3.ProjectOnPlane(vehicle.Body.linearVelocity,direction);
        float forwardSpeed = Mathf.Sqrt(Mathf.Max(1,projectile.speed*projectile.speed-lateralVelocity.sqrMagnitude));
        return (direction*forwardSpeed-lateralVelocity).normalized;
    }
    public float ReadyIn => Mathf.Max(Mathf.Max(0,nextShot-Time.time),missiles==0 ? RechargeRemaining : 0);
    public bool TryFire(VehicleOccupant pilot)
    {
        Recharge(Time.time);
        if (missiles==0 || vehicle.IsDestroyed || pilot.MountedVehicle != vehicle || vehicle.seats[pilot.SeatIndex].role != controlRole || Time.time < nextShot)
            return false;
        nextShot = Time.time + interval;
        if(missiles==capacity) nextRecharge=Time.time+rechargeInterval;
        missiles--;
        int tubeIndex=barrel++ % muzzles.Length;
        var muzzle = muzzles[tubeIndex];
        if(tubeRounds.Length>tubeIndex) tubeRounds[tubeIndex].SetActive(false);
        var rocket = Instantiate(projectile, muzzle.position + muzzle.forward * projectile.launchClearance, muzzle.rotation);
        rocket.Launch(vehicle, pilot.gameObject);
        if (rocket.TryGetComponent<VehicleMissileGuidance>(out var guidance)) guidance.Initialize(vehicle, RequestedAimRay, this);
        return true;
    }
}
