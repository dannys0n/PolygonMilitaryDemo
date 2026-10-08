using UnityEngine;

// A source describes intent. The controller and gameplay components decide what happens.
public struct SoldierInputFrame
{
    public Vector2 move;
    // Degrees this frame: X yaw, Y upward look. Mouse sensitivity belongs to the source.
    public Vector2 lookDelta;
    public bool aim, sprint, jump, reload;
    public bool fireHeld, firePressed;
    // One-based position in AvailableSlots; zero means no selection request.
    public int weaponSelection;
    public int weaponCycle;
    public bool toggleView, previewHitReaction;
}

public abstract class SoldierInputSource : MonoBehaviour
{
    // Called once each frame, even when gameplay is blocked. One-shot requests are not queued.
    public abstract SoldierInputFrame ReadInput();
}
