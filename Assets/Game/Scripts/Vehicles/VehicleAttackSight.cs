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
    [Header("Tank optical ADS")]
    [Range(1,12)] public float opticalMagnification=8;
    [Tooltip("Optional authored optical origin. Without one the camera uses a clear point immediately beyond the muzzle.")]
    public Transform opticalEye;
    public float OpticalFieldOfView=>2*Mathf.Atan(Mathf.Tan(70*Mathf.Deg2Rad*.5f)/opticalMagnification)*Mathf.Rad2Deg;
    public Vector3 OpticalPosition=>opticalEye?opticalEye.position:WeaponRay.origin+WeaponRay.direction*.25f;
    public float range=4000;
    public float minimumLaserRange;
    [TextArea(2,5)] public string referenceNotes;
    public bool IsDocumentedTankSight=>style==SightStyle.Abrams||style==SightStyle.Leopard||style==SightStyle.T72;
    // Source plates and the exact reference variant stay with the presentation.
    public string SightReference=>style==SightStyle.Abrams?"M1A1 GPS daylight: FM 3-20.12; https://www.kotsch88.de/f_m1a1_2.htm":
        style==SightStyle.Leopard?"Leopard 2 EMES-15 day channel, figure 5: https://www.kotsch88.de/f_leopard2.htm":
        style==SightStyle.T72?"T-72A TPD-K1, figure 10 (static reference scales): https://www.kotsch88.de/f_t-72m.htm":
        "Generic prototype director; no verified vehicle-specific sight plate";
    public bool Operational=>!vehicle.IsDestroyed && ((vehicle.turret&&vehicle.turret.enabled)||vehicle.GetComponent<VehicleLauncher>());
    public Ray WeaponRay=>vehicle.turret?new Ray(vehicle.turret.muzzle.position,vehicle.turret.muzzle.forward):new Ray(barrel.TransformPoint(muzzleLocalPosition),barrel.TransformDirection(forwardLocal).normalized);
    public bool DisplaysForSeat(int index)
    {
        var seat=vehicle.seats[index];
        var launcher=vehicle.GetComponent<VehicleLauncher>();
        return vehicle.turret ? seat.role==vehicle.turret.controlRole : launcher ? seat.role==launcher.controlRole : seat.role==Vehicle.SeatRole.Driver||seat.showVehicleInstruments;
    }
}
