using UnityEngine;

public class VehicleLauncher : MonoBehaviour
{
    public Vehicle vehicle;
    public VehicleRocket projectile;
    public Sprite weaponIcon;
    public Transform[] muzzles;
    public float interval = 2.5f;
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
    public Vector3 DirectionToAimPoint(Ray view, float range)
    {
        float distance = range;
        foreach (var hit in Physics.RaycastAll(view, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(vehicle.transform) && !(hit.collider is CharacterController)) distance = Mathf.Min(distance,hit.distance);
        Vector3 direction = (view.GetPoint(distance)-AimRay.origin).normalized;
        Vector3 lateralVelocity = Vector3.ProjectOnPlane(vehicle.Body.linearVelocity,direction);
        float forwardSpeed = Mathf.Sqrt(Mathf.Max(1,projectile.speed*projectile.speed-lateralVelocity.sqrMagnitude));
        return (direction*forwardSpeed-lateralVelocity).normalized;
    }
    public float ReadyIn => Mathf.Max(0,nextShot-Time.time);
    public bool TryFire(VehicleOccupant pilot)
    {
        if (vehicle.IsDestroyed || pilot.MountedVehicle != vehicle || vehicle.seats[pilot.SeatIndex].role != Vehicle.SeatRole.Driver || Time.time < nextShot)
            return false;
        nextShot = Time.time + interval;
        var muzzle = muzzles[barrel++ % muzzles.Length];
        var rocket = Instantiate(projectile, muzzle.position + muzzle.forward * projectile.launchClearance, muzzle.rotation);
        rocket.Launch(vehicle, pilot.gameObject);
        return true;
    }
}
