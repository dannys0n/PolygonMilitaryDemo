using UnityEngine;

// Contextual occupant intent. Motion/flight assists still produce VehicleInput for motors.
// Camera/UI switches are local presentation; interaction and weapon requests can be forwarded.
public struct VehicleControlFrame
{
    public bool active, lookAllowed;
    public bool interactPressed, interactHeld, interactReleased;
    public bool fire, aim, boost, brake, lower, handbrake, horn;
    public bool toggleView, toggleFreelook, toggleFlightAssist, countermeasures;
    public Vector2 move, lookDelta;
    public int seatSelection, seatPageDelta, weaponSelection, weaponCycle;
}
