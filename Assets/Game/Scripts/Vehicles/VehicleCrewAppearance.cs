using UnityEngine;

// Changes appearance only. Control seats retain the actor's skeleton, avatar and scale.
public sealed class VehicleCrewAppearance : MonoBehaviour
{
    public SkinnedMeshRenderer body;
    public Mesh airborneBody, groundBody;
    public Material[] crewMaterials;
    public Renderer[] infantryAccessories;
    Mesh originalMesh;
    Material[] originalMaterials;
    bool[] accessoryStates;
    bool initialized, wearingCrew;

    public void Apply(Vehicle vehicle, Vehicle.Seat seat)
    {
        bool controlSeat = vehicle && seat.showCharacter && vehicle.hasRenderedInterior
            && (seat.role == Vehicle.SeatRole.Driver || seat.pose == Vehicle.Seat.Pose.Driving);
        Mesh next = controlSeat ? (vehicle.IsAircraft ? airborneBody : groundBody) : null;
        if (!body) return;
        if (!initialized)
        {
            originalMesh = body.sharedMesh;
            originalMaterials = body.sharedMaterials;
            accessoryStates = new bool[infantryAccessories.Length];
            initialized = true;
        }
        if (next == body.sharedMesh || (!next && !wearingCrew)) return;
        if (next && !wearingCrew)
            for (int i = 0; i < infantryAccessories.Length; i++)
                if (infantryAccessories[i]) accessoryStates[i] = infantryAccessories[i].gameObject.activeSelf;
        body.sharedMesh = next ? next : originalMesh;
        body.sharedMaterials = next ? crewMaterials : originalMaterials;
        for (int i = 0; i < infantryAccessories.Length; i++)
            if (infantryAccessories[i]) infantryAccessories[i].gameObject.SetActive(next ? false : accessoryStates[i]);
        wearingCrew = next;
        GetComponent<SoldierFirstPersonBody>()?.RefreshAppearance();
    }

    public void Restore() => Apply(null, default);
}
