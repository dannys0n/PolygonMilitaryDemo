using UnityEngine;

// Local controls only. Other drivers can call the same player action methods.
[DefaultExecutionOrder(-20)]
public class SoldierLocalInput : MonoBehaviour
{
    public SoldierMovement movement;
    public SoldierLook look;
    public SoldierActions actions;
    public SoldierWeaponLoadout loadout;
    public SoldierFirstPersonView view;

    void Start() => SetCursorLocked(true);

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) SetCursorLocked(false);
        if (Input.GetMouseButtonDown(0)) SetCursorLocked(true);
        bool active = Cursor.lockState == CursorLockMode.Locked;
        view.SetAimRequested(active && Input.GetMouseButton(1));
        look.ApplyLookDelta(active
            ? new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * look.sensitivity
            : Vector2.zero);
        if (active && Input.GetKeyDown(KeyCode.Space)) movement.RequestJump();
        Vector2 move = active
            ? new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"))
            : Vector2.zero;
        movement.SimulateMovement(move, Time.deltaTime, active && Input.GetKey(KeyCode.LeftShift));
        if (!active) return;

        if (Input.GetKeyDown(KeyCode.V)) view.SetFirstPerson(!view.firstPerson);
        // Prototype hit-reaction preview; damage will call this through gameplay.
        if (Input.GetKeyDown(KeyCode.H)) actions.PlayHitReaction();
        bool reload = Input.GetKeyDown(KeyCode.R);
        if (reload) actions.TryReload();
        else if (actions.fireMode == SoldierActions.FireMode.Automatic
            ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0)) actions.TryFire();

        for (int i = 0; i < Mathf.Min(loadout.slots.Length, 9); i++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i))) loadout.TrySelectWeapon(i);
        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (wheel != 0f) loadout.TrySelectWeapon((loadout.selectedSlot +
            (wheel > 0f ? 1 : loadout.slots.Length - 1)) % loadout.slots.Length);
    }

    static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void OnDisable() => view.SetAimRequested(false);
}
