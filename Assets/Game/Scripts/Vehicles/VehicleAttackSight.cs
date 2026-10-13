using UnityEngine;

// Presentation references for an attack vehicle; weapon simulation can supply readiness/ammo later.
public class VehicleAttackSight : MonoBehaviour
{
    public enum SightStyle { Abrams, Leopard, T72, Autocannon, RocketDirector }
    public Vehicle vehicle;
    public SightStyle style;
    public Transform barrel;
    public Vector3 muzzleLocalPosition, forwardLocal=Vector3.forward;
    public Sprite weaponIcon;
    public string weaponName="MAIN GUN", ammunitionType="APFSDS";
    public float range=4000;
    public bool Operational=>vehicle.turret&&vehicle.turret.enabled&&!vehicle.IsDestroyed;
    public Ray WeaponRay=>vehicle.turret?new Ray(vehicle.turret.muzzle.position,vehicle.turret.muzzle.forward):new Ray(barrel.TransformPoint(muzzleLocalPosition),barrel.TransformDirection(forwardLocal).normalized);
    public bool DisplaysForSeat(int index)
    {
        var seat=vehicle.seats[index];
        return vehicle.turret?seat.role==Vehicle.SeatRole.Gunner:seat.role==Vehicle.SeatRole.Driver||seat.showVehicleInstruments;
    }
}
