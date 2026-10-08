using UnityEngine;

// Minimal observation controls and feedback for the AI-versus-AI test scene.
public class ArenaAISpectator : MonoBehaviour
{
    public ArenaMatch match;
    public Camera spectatorCamera;
    public float moveSpeed = 15f;
    public float lookSensitivity = 2f;
    float yaw, pitch;
    bool showLabels = true;

    void Start()
    {
        yaw = transform.eulerAngles.y; pitch = Mathf.DeltaAngle(0f, transform.eulerAngles.x);
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Backspace)) match.BeginMatch();
        if (Input.GetKeyDown(KeyCode.Tab)) showLabels = !showLabels;
        bool looking = Input.GetMouseButton(1);
        Cursor.lockState = looking ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !looking;
        if (!looking) return;
        yaw += Input.GetAxis("Mouse X") * lookSensitivity;
        pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSensitivity, -85f, 85f);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0);
        Vector3 move = new Vector3(Input.GetAxisRaw("Horizontal"),
            (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f), Input.GetAxisRaw("Vertical"));
        transform.position += transform.TransformDirection(Vector3.ClampMagnitude(move, 1f)) * moveSpeed
            * (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f) * Time.deltaTime;
    }

    void OnDisable() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

    void OnGUI()
    {
        GUI.Box(new Rect(12, 12, 530, 85), "AI arena (" + match.TeamSize(ArenaParticipant.Team.Red) + " v "
            + match.TeamSize(ArenaParticipant.Team.Blue) + "): Red " + match.RedScore + " : " + match.BlueScore + " Blue");
        GUI.Label(new Rect(24, 38, 510, 25), "Hold RMB + WASD to fly | Q/E down/up | Shift faster | Tab labels");
        GUI.Label(new Rect(24, 63, 510, 25), (match.IsRunning ? Mathf.CeilToInt(match.TimeRemaining) + "s" : match.Result)
            + " | Backspace restarts");
        if (!showLabels) return;
        foreach (var participant in match.participants)
        {
            Vector3 screen = spectatorCamera.WorldToScreenPoint(participant.transform.position + Vector3.up * 2.1f);
            if (screen.z <= 0f || screen.x < 0 || screen.x > Screen.width || screen.y < 0 || screen.y > Screen.height) continue;
            var bot = participant.GetComponent<ArenaAIInput>();
            GUI.color = participant.team == ArenaParticipant.Team.Red ? new Color(1f, .4f, .4f) : new Color(.4f, .7f, 1f);
            string state = participant.health.IsDead ? "Respawn " + participant.RespawnRemaining.ToString("F1")
                : bot.State + (bot.soldier.movement.IsRunning ? " / Sprint" : "") + " | " + participant.health.CurrentHealth.ToString("F0") + " HP";
            GUI.Label(new Rect(screen.x - 80f, Screen.height - screen.y, 220f, 40f), participant.name + "\n" + state);
        }
        GUI.color = Color.white;
    }
}
