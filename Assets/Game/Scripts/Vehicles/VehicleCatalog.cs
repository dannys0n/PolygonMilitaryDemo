using UnityEngine;

[CreateAssetMenu(menuName = "Arena/Vehicle catalog")]
public class VehicleCatalog : ScriptableObject
{
    public Vehicle[] transports = System.Array.Empty<Vehicle>();
    public Vehicle[] combatVehicles = System.Array.Empty<Vehicle>();
}
