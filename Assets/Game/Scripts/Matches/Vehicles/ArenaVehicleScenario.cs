using UnityEngine;

// Arena rules bind to spawned vehicles before their first gameplay/physics frame.
public class ArenaVehicleScenario : MonoBehaviour
{
    public ArenaMatch match;
    public int vehicleAgentType;
    public bool allowAIHelicopters = true;
    public bool autoSelectLocalClass = true;
    public Transform[] landingZones;

    void OnEnable()
    {
        match.MatchEnded += OnEnded;
        Vehicle.Spawned += Configure;
        foreach (var vehicle in Vehicle.Active)
            Configure(vehicle);
    }

    void OnDisable()
    {
        match.MatchEnded -= OnEnded;
        Vehicle.Spawned -= Configure;
        foreach (var vehicle in Vehicle.Active)
            if (vehicle.gameObject.scene == gameObject.scene)
            {
                if (vehicle.turret) vehicle.turret.ShotFired -= OnVehicleShot;
                foreach (var launcher in vehicle.GetComponentsInChildren<VehicleLauncher>(true))
                    launcher.ShotFired -= OnVehicleShot;
            }
        OnEnded();
    }

    void Start()
    {
        if (!autoSelectLocalClass) return;
        foreach (var actor in match.participants)
            if (actor.GetComponent<SoldierLocalInput>() && !actor.SelectedClass)
                actor.SelectClass(match.settings.classes[0]);
    }

    void Configure(Vehicle vehicle)
    {
        if (vehicle.gameObject.scene != gameObject.scene)
            return;
        if (vehicle.turret)
        {
            vehicle.turret.ShotFired -= OnVehicleShot;
            vehicle.turret.ShotFired += OnVehicleShot;
        }
        foreach (var launcher in vehicle.GetComponentsInChildren<VehicleLauncher>(true))
        {
            launcher.ShotFired -= OnVehicleShot;
            launcher.ShotFired += OnVehicleShot;
        }
        vehicle.DamageFilter = source =>
        {
            if (!match.IsRunning)
                return false;
            var actor = source ? source.GetComponentInParent<ArenaParticipant>() : null;
            if (!actor || match.settings.friendlyFire)
                return true;
            foreach (var seat in vehicle.seats)
                if (seat.occupant && seat.occupant.faction == (int)actor.team)
                    return false;
            return true;
        };
    }

    void OnVehicleShot(VehicleOccupant gunner, Vector3 origin)
    {
        if (gunner.TryGetComponent<ArenaParticipant>(out var actor))
            actor.RecordShotAt(origin);
    }

    void OnEnded()
    {
        foreach (var actor in match.participants)
        {
            var occupant = actor.GetComponent<VehicleOccupant>();
            if (occupant && occupant.IsMounted)
                occupant.Leave(true);
            if (actor.player)
                actor.player.SetGameplayAllowed(false);
        }
    }
}
