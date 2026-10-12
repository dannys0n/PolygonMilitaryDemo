using UnityEngine;

public class VehicleLauncher : MonoBehaviour
{
    public Vehicle vehicle;
    public VehicleRocket projectile;
    public Sprite weaponIcon;
    public Transform[] muzzles;
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
            missiles++; nextRecharge += rechargeInterval;
        }
        if(missiles==capacity) nextRecharge=float.PositiveInfinity;
    }
    float nextShot;
    int barrel;
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
        if (missiles==0 || vehicle.IsDestroyed || pilot.MountedVehicle != vehicle || vehicle.seats[pilot.SeatIndex].role != Vehicle.SeatRole.Driver || Time.time < nextShot)
            return false;
        nextShot = Time.time + interval;
        if(missiles==capacity) nextRecharge=Time.time+rechargeInterval;
        missiles--;
        var muzzle = muzzles[barrel++ % muzzles.Length];
        var rocket = Instantiate(projectile, muzzle.position + muzzle.forward * projectile.launchClearance, muzzle.rotation);
        rocket.Launch(vehicle, pilot.gameObject);
        return true;
    }
}
