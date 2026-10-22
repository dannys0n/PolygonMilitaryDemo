using UnityEngine;

// Keyboard/mouse adapter. It never moves the character or starts gameplay actions directly.
public class SoldierLocalInput : SoldierInputSource
{
    public float sensitivity = 2f;
    public float EffectiveSensitivity => sensitivity * ArenaMouseSensitivity.Value;
    SoldierFirstPersonView view;
    void Awake() => view=GetComponent<SoldierFirstPersonView>();
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
            lookDelta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * EffectiveSensitivity * view.AimSensitivity,
            aim = Input.GetMouseButton(1),
            cycleSight = Input.GetKeyDown(KeyCode.T),
            utilitySelection=Input.GetKeyDown(KeyCode.Q)?1:Input.GetKeyDown(KeyCode.X)?2:0,
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

    public override VehicleControlFrame ReadVehicleInput(VehicleOccupant occupant)
    {
        if(InputBlocked || Time.timeScale==0)return default;
        if(occupant.IsMounted)
        {
            if(Input.GetKeyDown(KeyCode.Escape))SetCursorLocked(false);
            if(Input.GetMouseButtonDown(0))SetCursorLocked(true);
        }
        var mouse=new Vector2(Input.GetAxisRaw("Mouse X"),Input.GetAxisRaw("Mouse Y"));
        var vehicle=occupant.MountedVehicle;
        if(vehicle && vehicle.IsAircraft && vehicle.seats[occupant.SeatIndex].role==Vehicle.SeatRole.Driver && !vehicle.FlightAssistEnabled)
            mouse=Vector2.ClampMagnitude(mouse,1f);
        var frame=new VehicleControlFrame {
            active=true, lookAllowed=Cursor.lockState==CursorLockMode.Locked,
            interactPressed=Input.GetKeyDown(KeyCode.E),interactHeld=Input.GetKey(KeyCode.E),interactReleased=Input.GetKeyUp(KeyCode.E),
            fire=Input.GetMouseButton(0),aim=Input.GetMouseButton(1),
            boost=Input.GetKey(KeyCode.LeftShift),brake=Input.GetKey(KeyCode.Space),lower=Input.GetKey(KeyCode.LeftControl),handbrake=Input.GetKey(KeyCode.Z),horn=Input.GetKeyDown(KeyCode.H),
            toggleView=Input.GetKeyDown(KeyCode.V),toggleFreelook=Input.GetKeyDown(KeyCode.C),toggleFlightAssist=Input.GetKeyDown(KeyCode.T),countermeasures=Input.GetKeyDown(KeyCode.X),
            move=new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical")),lookDelta=mouse*EffectiveSensitivity,
            seatPageDelta=(Input.GetKeyDown(KeyCode.PageDown)?1:0)-(Input.GetKeyDown(KeyCode.PageUp)?1:0)
        };
        for(int i=0;i<12;i++)if(Input.GetKeyDown((KeyCode)((int)KeyCode.F1+i)))frame.seatSelection=i+1;
        for(int i=0;i<9;i++)if(Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1+i)))frame.weaponSelection=i+1;
        float scroll=Input.GetAxisRaw("Mouse ScrollWheel");frame.weaponCycle=Mathf.Abs(scroll)>.01f?(scroll>0?1:-1):0;
        return frame;
    }

    static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void OnDisable() => SetCursorLocked(false);
}
