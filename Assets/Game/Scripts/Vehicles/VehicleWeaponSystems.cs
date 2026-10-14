using System;
using UnityEngine;

// One aim mechanism per station; changing ammunition never creates competing pivot writers.
public sealed class VehicleWeaponSystems : MonoBehaviour
{
    public enum FireSystem { Gun, Launcher }
    [Serializable] public class Weapon
    {
        public string name;
        public FireSystem system;
        public Vehicle.SeatRole role;
        public VehicleTurret gun;
        public VehicleLauncher launcher;
        public VehicleRocket projectile;
        public Transform muzzle;
        public float damage = 35, range = 300, roundsPerMinute = 600;
        public bool requiresRadarSupport;
        public bool explosive;
        public bool ejectCasings;
        public Sprite icon;
        [NonSerialized] public float nextShot;
        public bool IsSingleShot => system == FireSystem.Gun && roundsPerMinute < 60;
        public float ReadyIn => system == FireSystem.Launcher && launcher ? launcher.ReadyIn : Mathf.Max(0,nextShot-Time.time);
    }
    public Vehicle vehicle;
    public Weapon[] weapons = Array.Empty<Weapon>();
    int driverSelection, gunnerSelection;
    public event Action Changed;
    public bool IsAvailable(Weapon weapon) => weapon != null && (!weapon.requiresRadarSupport || VehicleRadarSupport.HasFriendlyRadar(vehicle));
    public string SupportStatus(Weapon weapon) => weapon != null && weapon.requiresRadarSupport ? (IsAvailable(weapon) ? "RADAR LINKED" : "RADAR REQUIRED") : "";
    void Update()
    {
        foreach (var weapon in weapons)
            if (weapon.requiresRadarSupport && weapon.launcher) weapon.launcher.enabled = IsAvailable(weapon);
    }
    void Awake()
    {
        ApplyFirst(Vehicle.SeatRole.Driver);
        ApplyFirst(Vehicle.SeatRole.Gunner);
    }
    void ApplyFirst(Vehicle.SeatRole role)
    {
        for (int i = 0; i < weapons.Length; i++) if (weapons[i].role == role) { Select(role, i); break; }
    }
    public Weapon Current(Vehicle.SeatRole role)
    {
        int index = role == Vehicle.SeatRole.Driver ? driverSelection : gunnerSelection;
        return index >= 0 && index < weapons.Length && weapons[index].role == role ? weapons[index] : null;
    }
    public Weapon Current(VehicleOccupant actor) => actor && actor.MountedVehicle == vehicle
        ? Current(vehicle.seats[actor.SeatIndex].role) : null;
    public int SelectedIndex(Vehicle.SeatRole role) => role == Vehicle.SeatRole.Driver ? driverSelection : gunnerSelection;
    public bool Select(VehicleOccupant actor, int ordinal)
    {
        if (!actor || actor.MountedVehicle != vehicle) return false;
        var role = vehicle.seats[actor.SeatIndex].role;
        int count = 0;
        for (int i = 0; i < weapons.Length; i++)
            if (weapons[i].role == role && count++ == ordinal) { Select(role, i); return true; }
        return false;
    }
    public void Cycle(VehicleOccupant actor, int direction)
    {
        if (!actor || actor.MountedVehicle != vehicle) return;
        var role = vehicle.seats[actor.SeatIndex].role;
        int current = SelectedIndex(role);
        for (int n = 1; n <= weapons.Length; n++)
        {
            int index = (current + n * Math.Sign(direction) + weapons.Length) % weapons.Length;
            if (weapons[index].role == role) { Select(role, index); return; }
        }
    }
    void Select(Vehicle.SeatRole role, int index)
    {
        if (role == Vehicle.SeatRole.Driver) driverSelection = index; else gunnerSelection = index;
        var weapon = weapons[index];
        if (weapon.system == FireSystem.Gun && weapon.gun)
        {
            var gun = weapon.gun;
            gun.SetReadyTime(weapon.nextShot);
            gun.weaponName = weapon.name; gun.damage = weapon.damage; gun.range = weapon.range;
            gun.roundsPerMinute = weapon.roundsPerMinute; gun.explosiveVehicleDamage = weapon.explosive;
            gun.projectile = weapon.projectile; gun.ejectCasings = weapon.ejectCasings;
            if (weapon.muzzle)
            {
                gun.muzzle = weapon.muzzle;
                if (gun.muzzleFlash)
                {
                    gun.muzzleFlash.transform.SetParent(weapon.muzzle, false);
                    gun.muzzleFlash.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                }
            }
            if (weapon.icon) gun.weaponIcon = weapon.icon;
        }
        Changed?.Invoke();
    }
    public bool TryFire(VehicleOccupant actor)
    {
        var weapon = Current(actor);
        if (!IsAvailable(weapon)) return false;
        if (weapon.system == FireSystem.Launcher) return weapon.launcher.TryFire(actor);
        if (Time.time < weapon.nextShot) return false;
        // Restore this weapon's absolute deadline; other systems reload independently while deselected.
        weapon.gun.SetReadyTime(weapon.nextShot);
        bool fired = weapon.gun.TryFire(actor);
        if (fired) weapon.nextShot = weapon.gun.NextShotTime;
        return fired;
    }
    public void AimAt(VehicleOccupant actor, Ray view)
    {
        var weapon = Current(actor);
        if (weapon == null) return;
        // Keep the barrel aligned while selecting the coaxial gun or another ammunition type.
        if (weapon.gun) weapon.gun.AimAt(view);
        if (weapon.launcher && weapon.launcher.TryGetComponent<VehicleLauncherMount>(out var mount)) mount.AimAt(view);
        if (weapon.launcher) weapon.launcher.SetAim(view);
    }
}
