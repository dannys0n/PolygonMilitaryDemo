using UnityEngine;

// Minimal observation controls and feedback for the AI-versus-AI test scene.
[DefaultExecutionOrder(200)]
public class ArenaAISpectator : MonoBehaviour
{
    public ArenaMatch match;
    public Camera spectatorCamera;
    public float moveSpeed = 15f;
    public float lookSensitivity = 2f;
    [Header("Follow camera")]
    [Min(.5f)] public float orbitDistance = 2.5f;
    public float zoomSpeed = 2f;
    [Min(0f)] public float deathWatchDuration = 2f;
    [Tooltip("Optional local player: spectate while dead and return on respawn.")]
    public ArenaParticipant localPlayer;
    bool deathWatch, restorePerspective, killerDied, localDeathView, localWasFirstPerson;
    float deathWatchUntil;
    ArenaParticipant pendingKiller;
    SoldierLocalInput localInput;
    bool localInputEnabled;
    public ArenaParticipant FollowedCharacter { get; private set; }
    Transform followedHead;
    public bool FirstPersonSpectating { get; private set; }
    SoldierFirstPersonView observedView;
    bool previousFirstPerson, previousCameraEnabled, previousSpectatorEnabled;
    int previousCameraMask;
    Renderer[] hiddenBody;
    UnityEngine.Rendering.ShadowCastingMode[] previousShadows;
    float yaw, pitch;
    bool showLabels = true;

    void OnEnable()
    {
        if (!localPlayer) return;
        localPlayer.health.Died += OnLocalPlayerDied;
        localPlayer.player.Respawned += OnLocalPlayerRespawned;
        localInput = localPlayer.GetComponent<SoldierLocalInput>();
    }

    void Start()
    {
        yaw = transform.eulerAngles.y; pitch = Mathf.DeltaAngle(0f, transform.eulerAngles.x);
        if (localPlayer) { spectatorCamera.enabled = false; return; }
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

    void Update()
    {
        if (localPlayer && !localDeathView) return;
        if (!localPlayer && Input.GetKeyDown(KeyCode.Backspace)) { CancelDeathWatch(); match.BeginMatch(); }
        if (deathWatch && Time.time >= deathWatchUntil) CompleteDeathWatch();
        if (Input.GetKeyDown(KeyCode.Tab)) showLabels = !showLabels;
        if (Input.GetKeyDown(KeyCode.F))
        {
            if (FollowedCharacter) StopFollowing();
            else CycleCharacter(1);
        }
        if (Input.GetKeyDown(KeyCode.X)) CycleCharacter(1);
        if (Input.GetKeyDown(KeyCode.Z)) CycleCharacter(-1);
        if (Input.GetKeyDown(KeyCode.C))
        {
            if (deathWatch) return;
            if (FirstPersonSpectating) ExitPerspective();
            else
            {
                if (!FollowedCharacter) CycleCharacter(1);
                if (FollowedCharacter) EnterPerspective();
            }
        }
        if (FollowedCharacter && !FollowedCharacter.isActiveAndEnabled) StopFollowing();
        if (FirstPersonSpectating && (!FollowedCharacter || !observedView)) StopFollowing();
        if (FollowedCharacter && !FirstPersonSpectating)
            orbitDistance = Mathf.Clamp(orbitDistance - Input.GetAxis("Mouse ScrollWheel") * zoomSpeed, .5f, 8f);
        bool looking = !FirstPersonSpectating && Input.GetMouseButton(1);
        Cursor.lockState = looking ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !looking;
        if (!looking) return;
        yaw += Input.GetAxis("Mouse X") * lookSensitivity;
        pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSensitivity, -85f, 85f);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0);
        if (FollowedCharacter) return;
        Vector3 move = new Vector3(Input.GetAxisRaw("Horizontal"),
            (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f), Input.GetAxisRaw("Vertical"));
        transform.position += transform.TransformDirection(Vector3.ClampMagnitude(move, 1f)) * moveSpeed
            * (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f) * Time.deltaTime;
    }

    public void Follow(ArenaParticipant character)
    {
        CancelDeathWatch();
        bool perspective = FirstPersonSpectating;
        if (FollowedCharacter) FollowedCharacter.health.Died -= OnFollowedCharacterDied;
        ExitPerspective();
        FollowedCharacter = character;
        FollowedCharacter.health.Died += OnFollowedCharacterDied;
        followedHead = character.player.actions.animator.GetBoneTransform(HumanBodyBones.Head);
        if (perspective) EnterPerspective();
    }

    public void StopFollowing()
    {
        CancelDeathWatch();
        if (FollowedCharacter) FollowedCharacter.health.Died -= OnFollowedCharacterDied;
        ExitPerspective();
        FollowedCharacter = null; followedHead = null;
    }

    void OnFollowedCharacterDied(GameObject source)
    {
        BeginDeathWatch(source, FirstPersonSpectating);
    }

    void BeginDeathWatch(GameObject source, bool perspective)
    {
        CancelDeathWatch();
        restorePerspective = perspective;
        ExitPerspective();
        // The orbit camera watches the victim; the killer is tracked independently.
        deathWatch = true; deathWatchUntil = Time.time + deathWatchDuration;
        pendingKiller = source ? source.GetComponentInParent<ArenaParticipant>() : null;
        killerDied = !CanFollowAlive(pendingKiller) || pendingKiller == FollowedCharacter;
        if (pendingKiller) pendingKiller.health.Died += OnPendingKillerDied;
    }

    void OnPendingKillerDied(GameObject source) => killerDied = true;

    bool CanFollowAlive(ArenaParticipant actor) => actor && actor.match == match && actor.player
        && actor.isActiveAndEnabled && !actor.health.IsDead && !actor.IsAwaitingSpawn;

    void CompleteDeathWatch()
    {
        var next = !killerDied && CanFollowAlive(pendingKiller) ? pendingKiller : null;
        if (!next)
            foreach (var actor in match.participants)
                if (CanFollowAlive(actor) && actor != FollowedCharacter && actor != pendingKiller) { next = actor; break; }
        bool perspective = restorePerspective;
        if (!next) { deathWatchUntil = Time.time + .25f; return; }
        CancelDeathWatch();
        Follow(next);
        if (perspective) EnterPerspective();
    }

    void CancelDeathWatch()
    {
        if (pendingKiller) pendingKiller.health.Died -= OnPendingKillerDied;
        pendingKiller = null; deathWatch = false;
    }

    void OnLocalPlayerDied(GameObject source)
    {
        localDeathView = true;
        localWasFirstPerson = localPlayer.player.view.firstPerson;
        localInputEnabled = localInput.enabled; localInput.enabled = false;
        localPlayer.player.view.SetFirstPerson(false);
        localPlayer.player.view.playerCamera.enabled = false;
        spectatorCamera.enabled = true;
        yaw = localPlayer.transform.eulerAngles.y; pitch = 15f;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        Follow(localPlayer);
        BeginDeathWatch(source, localWasFirstPerson);
    }

    void OnLocalPlayerRespawned()
    {
        if (!localDeathView) return;
        StopFollowing(); localDeathView = false;
        localPlayer.player.view.SetFirstPerson(localWasFirstPerson);
        localPlayer.player.view.playerCamera.enabled = true;
        spectatorCamera.enabled = false;
        localInput.enabled = localInputEnabled;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    void EnterPerspective()
    {
        observedView = FollowedCharacter.player.view;
        FirstPersonSpectating = true;
        var camera = observedView.playerCamera;
        previousFirstPerson = observedView.firstPerson;
        previousCameraEnabled = camera.enabled;
        previousCameraMask = camera.cullingMask;
        previousSpectatorEnabled = spectatorCamera.enabled;
        var aimRotation = camera.transform.localRotation;
        observedView.SetFirstPerson(true);
        camera.transform.localRotation = aimRotation;
        // Other soldiers remain visible. Only the observed body becomes shadow-only.
        camera.cullingMask |= 1 << observedView.thirdPersonLayer;
        hiddenBody = observedView.thirdPersonRoot.GetComponentsInChildren<Renderer>(true);
        previousShadows = new UnityEngine.Rendering.ShadowCastingMode[hiddenBody.Length];
        for (int i = 0; i < hiddenBody.Length; i++)
        {
            previousShadows[i] = hiddenBody[i].shadowCastingMode;
            hiddenBody[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }
        spectatorCamera.enabled = false;
        camera.enabled = true;
    }

    void ExitPerspective()
    {
        if (!FirstPersonSpectating) return;
        if (observedView)
        {
            var camera = observedView.playerCamera;
            var aimRotation = camera.transform.localRotation;
            observedView.SetFirstPerson(previousFirstPerson);
            camera.transform.localRotation = aimRotation;
            camera.cullingMask = previousCameraMask;
            camera.enabled = previousCameraEnabled;
        }
        for (int i = 0; i < hiddenBody.Length; i++)
            if (hiddenBody[i]) hiddenBody[i].shadowCastingMode = previousShadows[i];
        spectatorCamera.enabled = previousSpectatorEnabled;
        observedView = null;
        FirstPersonSpectating = false;
    }

    void CycleCharacter(int direction)
    {
        var actors = match.participants;
        if (actors.Length == 0) { StopFollowing(); return; }
        int current = System.Array.IndexOf(actors, FollowedCharacter);
        int start = current < 0 ? (direction > 0 ? -1 : 0) : current;
        for (int i = 1; i <= actors.Length; i++)
        {
            var actor = actors[(start + direction * i + actors.Length * 2) % actors.Length];
            if (CanFollowAlive(actor)) { Follow(actor); return; }
        }
        StopFollowing();
    }

    void LateUpdate()
    {
        if (!FollowedCharacter) return;
        // Follow the animated head after movement/animation; keep yaw independent of the bot.
        Vector3 pivot = followedHead.position;
        Vector3 backward = -transform.forward;
        float distance = orbitDistance;
        foreach (var hit in Physics.SphereCastAll(pivot, .15f, backward, distance,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<ArenaParticipant>()) continue;
            distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - .05f));
        }
        transform.position = pivot + backward * distance;
    }

    void OnDisable()
    {
        if (localPlayer)
        {
            OnLocalPlayerRespawned();
            localPlayer.health.Died -= OnLocalPlayerDied;
            localPlayer.player.Respawned -= OnLocalPlayerRespawned;
        }
        StopFollowing(); Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

    void OnGUI()
    {
        if (localPlayer && !localDeathView) return;
        GUI.Box(new Rect(12, 12, 650, 110), "AI arena (" + match.TeamSize(ArenaParticipant.Team.Red) + " v "
            + match.TeamSize(ArenaParticipant.Team.Blue) + "): Red " + match.RedScore + " : " + match.BlueScore + " Blue");
        GUI.Label(new Rect(24, 38, 510, 25), "Hold RMB + WASD to fly | Q/E down/up | Shift faster | Tab labels");
        GUI.Label(new Rect(24, 63, 510, 25), (match.IsRunning ? Mathf.CeilToInt(match.TimeRemaining) + "s" : match.Result)
            + " | Backspace restarts");
        GUI.Label(new Rect(24, 88, 630, 25), "F follow/free | Z/X bot | C FPS/orbit | RMB orbit | Scroll zoom | "
            + (FollowedCharacter ? "Following: " + FollowedCharacter.name : "Free camera"));
        if (!showLabels) return;
        foreach (var participant in match.participants)
        {
            if (FirstPersonSpectating && participant == FollowedCharacter) continue;
            var camera = FirstPersonSpectating ? observedView.playerCamera : spectatorCamera;
            Vector3 screen = camera.WorldToScreenPoint(participant.transform.position + Vector3.up * 2.1f);
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
