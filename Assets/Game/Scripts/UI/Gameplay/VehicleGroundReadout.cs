using UnityEngine;

// Driver speedometers and station azimuth/elevation use measured telemetry.
// Speedometer units follow the tentative vehicle family; fuel/oil/transmission gauges need real simulation.
// HMMWV controls reference: TM 9-2320-280-10, speedometer and hand/service brake descriptions.
public static class VehicleGroundReadout
{
    public static float SpeedKmh(Vehicle vehicle) => Vector3.ProjectOnPlane(vehicle.Body.linearVelocity, Vector3.up).magnitude * 3.6f;
    public static float GaugeMaximum(Vehicle vehicle, bool imperial)
    {
        // Scale choices are presentation calibrations; these do not invent an engine RPM or gear model.
        switch (vehicle.instrumentReference)
        {
            case Vehicle.InstrumentReference.HMMWV: case Vehicle.InstrumentReference.M113:
            case Vehicle.InstrumentReference.Bradley: return imperial ? 60 : 100;
            case Vehicle.InstrumentReference.T72: return 80;
            case Vehicle.InstrumentReference.Leopard2: return 100;
            default:
                var motor = vehicle.motor as HummerMotor;
                return Mathf.Max(imperial ? 60 : 80, motor ? Mathf.Ceil(motor.maxSpeed * (imperial ? 2.236936f : 3.6f) / 20f) * 20 : 0);
        }
    }
    public static string FamilyCaption(Vehicle vehicle)
    {
        switch (vehicle.instrumentReference)
        {
            case Vehicle.InstrumentReference.HIMARS: return "LAUNCH VEHICLE";
            case Vehicle.InstrumentReference.Tor: return "AIR DEFENCE";
            case Vehicle.InstrumentReference.Stryker: return "LAV / CREW DISPLAY";
            case Vehicle.InstrumentReference.Abrams: return "DRIVER DISPLAY";
            case Vehicle.InstrumentReference.Bradley: return "IFV DRIVER";
            case Vehicle.InstrumentReference.M113: return "TRACK DRIVER";
            default: return "VEHICLE";
        }
    }
    public static string Station(Vehicle vehicle)
    {
        var systems = vehicle.GetComponent<VehicleWeaponSystems>();
        var weapon = systems ? systems.Current(Vehicle.SeatRole.Driver) ?? systems.Current(Vehicle.SeatRole.Gunner) : null;
        if (weapon != null && weapon.launcher)
            return "READY " + weapon.launcher.Missiles + " / " + weapon.launcher.capacity
                + (weapon.launcher.Missiles < weapon.launcher.capacity ? "\n+1 " + weapon.launcher.RechargeRemaining.ToString("0.0") + " S" : "");
        if (vehicle.turret)
        {
            Vector3 direction = vehicle.transform.InverseTransformDirection(vehicle.turret.muzzle.forward);
            float azimuth = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float elevation = Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            return "AZ " + azimuth.ToString("+000;-000;000") + "°\nEL " + elevation.ToString("+00;-00;00") + "°";
        }
        Vector3 up = vehicle.transform.InverseTransformDirection(Vector3.up);
        float roll = Mathf.Atan2(up.x, up.y) * Mathf.Rad2Deg;
        float pitch = Mathf.Asin(Mathf.Clamp(vehicle.transform.forward.y, -1, 1)) * Mathf.Rad2Deg;
        return "ROLL " + roll.ToString("+0;-0;0") + "°\nPITCH " + pitch.ToString("+0;-0;0") + "°";
    }
}
