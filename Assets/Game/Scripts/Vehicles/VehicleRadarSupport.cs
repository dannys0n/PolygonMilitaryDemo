using UnityEngine;

// Team radio link: an occupied operational radar enables nearby friendly AA launchers.
public class VehicleRadarSupport : MonoBehaviour
{
    public Vehicle vehicle;
    public float linkRange = 1500;
    public static bool HasFriendlyRadar(Vehicle requester)
    {
        int faction = Faction(requester);
        if (faction < 0) return false;
        foreach (var candidate in Vehicle.Active)
        {
            if (!candidate || candidate.IsDestroyed || candidate.gameObject.scene != requester.gameObject.scene) continue;
            var radar = candidate.GetComponent<VehicleRadarSupport>();
            if (!radar || !radar.isActiveAndEnabled || Faction(candidate) != faction) continue;
            if ((candidate.transform.position - requester.transform.position).sqrMagnitude <= radar.linkRange * radar.linkRange) return true;
        }
        return false;
    }
    static int Faction(Vehicle vehicle)
    {
        if (!vehicle) return -1;
        foreach (var seat in vehicle.seats) if (seat.occupant && seat.occupant.faction >= 0) return seat.occupant.faction;
        return -1;
    }
}
