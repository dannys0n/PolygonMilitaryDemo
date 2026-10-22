using UnityEngine;

[CreateAssetMenu(menuName = "Game/Vehicle catalog")]
public class VehicleCatalog : ScriptableObject
{
    public Vehicle[] transports = System.Array.Empty<Vehicle>();
    public Vehicle[] combatVehicles = System.Array.Empty<Vehicle>();
}
