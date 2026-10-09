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
