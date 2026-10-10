using UnityEngine;

// Keyboard/mouse adapter. It never moves the character or starts gameplay actions directly.
public class SoldierLocalInput : SoldierInputSource
{
    public float sensitivity = 2f;
    public bool InputBlocked { get; set; }

    void Start() => SetCursorLocked(true);

    public override SoldierInputFrame ReadInput()
    {
        if (InputBlocked) return default;
        if (Input.GetKeyDown(KeyCode.Escape)) SetCursorLocked(false);
        if (Input.GetMouseButtonDown(0)) SetCursorLocked(true);
        if (Cursor.lockState != CursorLockMode.Locked) return default;

        float wheel = Input.GetAxis("Mouse ScrollWheel");
        var input = new SoldierInputFrame
        {
            move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")),
            lookDelta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * sensitivity,
            aim = Input.GetMouseButton(1),
            sprint = Input.GetKey(KeyCode.LeftShift),
            jump = Input.GetKeyDown(KeyCode.Space),
            reload = Input.GetKeyDown(KeyCode.R),
            grenadeThrow = Input.GetKeyDown(KeyCode.G), grenadeCycle = Input.GetKeyDown(KeyCode.B),
            fireHeld = Input.GetMouseButton(0),
            firePressed = Input.GetMouseButtonDown(0),
            toggleView = Input.GetKeyDown(KeyCode.V),
            previewHitReaction = Input.GetKeyDown(KeyCode.H),
            weaponCycle = wheel > 0f ? 1 : wheel < 0f ? -1 : 0
        };
        for (int i = 1; i <= 9; i++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i - 1))) input.weaponSelection = i;
        return input;
    }

    static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void OnDisable() => SetCursorLocked(false);
}
