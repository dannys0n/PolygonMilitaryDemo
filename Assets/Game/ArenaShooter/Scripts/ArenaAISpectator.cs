using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Minimal observation controls and feedback for the AI-versus-AI test scene.
[DefaultExecutionOrder(300)]
public class ArenaAISpectator : MonoBehaviour
{
    public ArenaMatch match;
    public Camera spectatorCamera;
    public GameObject hudPrefab;
    ArenaHud followHud;
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
    bool previousPostProcessing;
    int previousCameraMask, previousSpectatorMask;
    RenderTexture previousCameraTarget, previousSpectatorTarget, insetTexture;
    Renderer[] hiddenBody;
    UnityEngine.Rendering.ShadowCastingMode[] previousShadows;
    float yaw, pitch;
    public bool ShowControls { get; private set; } = true;
    public bool IsSpectating => !localPlayer || localDeathView || ManualSpectating;
    public bool ManualSpectating { get; private set; }
    readonly System.Collections.Generic.List<(Renderer mesh, bool enabled)> suspendedMeshes = new();
    readonly System.Collections.Generic.List<(Collider body, bool enabled)> suspendedColliders = new();

    public void SetManualSpectating(bool value)
    {
        if (!localPlayer || value == ManualSpectating) return;
        if (value)
        {
            var occupant = localPlayer.GetComponent<VehicleOccupant>();
            if (occupant && occupant.IsMounted) occupant.Leave(true);
            if (!localDeathView)
            {
                localWasFirstPerson = localPlayer.player.view.firstPerson;
                localInputEnabled = localInput.enabled;
            }
            StopFollowing();
            ManualSpectating = true;
            localPlayer.SetSpectating(true);
            localInput.enabled = false;
            localPlayer.player.view.SetFirstPerson(false);
            localPlayer.player.view.playerCamera.enabled = false;
            suspendedColliders.Clear();
            foreach (var body in localPlayer.GetComponentsInChildren<Collider>(true))
            { suspendedColliders.Add((body, body.enabled)); body.enabled = false; }
            suspendedMeshes.Clear();
            foreach (var mesh in localPlayer.GetComponentsInChildren<Renderer>(true))
            { suspendedMeshes.Add((mesh, mesh.enabled)); mesh.enabled = false; }
            transform.SetPositionAndRotation(localPlayer.transform.position + Vector3.up * 3f, Quaternion.Euler(15, localPlayer.transform.eulerAngles.y, 0));
            spectatorCamera.enabled = true;
            CycleCharacter(1);
        }
        else
        {
            StopFollowing();
            ManualSpectating = false;
            foreach (var item in suspendedMeshes) if (item.mesh) item.mesh.enabled = item.enabled;
            suspendedMeshes.Clear();
            foreach (var item in suspendedColliders) if (item.body) item.body.enabled = item.enabled;
            suspendedColliders.Clear();
            localPlayer.SetSpectating(false);
            // Conquest shows deployment; other modes retry the normal spawn search.
            localDeathView = true;
            if (match.IsRunning) match.Spawn(localPlayer);
        }
    }
    public bool ControlsBlocked { get; set; }
    bool bodyDirty;
    VehicleOccupant observedOccupant;
    bool wasMounted;

    public void SetInsetTexture(RenderTexture texture)
    {
        insetTexture = texture;
        RouteCameras();
    }

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeforeCamera;
        RenderPipelineManager.endCameraRendering += AfterCamera;
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
        if (!IsSpectating) return;
        if (!localPlayer && Input.GetKeyDown(KeyCode.Backspace)) { CancelDeathWatch(); match.BeginMatch(); }
        if (deathWatch && Time.time >= deathWatchUntil) CompleteDeathWatch();
        if (ControlsBlocked) return;
        if (Input.GetKeyDown(KeyCode.L)) ShowControls = !ShowControls;
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
        bool looking = !FollowedCharacter && Input.GetMouseButton(1);
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
        ReleasePerspective();
        FollowedCharacter = character;
        FollowedCharacter.health.Died += OnFollowedCharacterDied;
        followedHead = character.player.actions.animator.GetBoneTransform(HumanBodyBones.Head);
        ShowFollowHud(character);
        PreparePerspective();
        FirstPersonSpectating = perspective;
        RouteCameras();
    }

    public void StopFollowing()
    {
        CancelDeathWatch();
        if (FollowedCharacter) FollowedCharacter.health.Died -= OnFollowedCharacterDied;
        ReleasePerspective();
        FollowedCharacter = null; followedHead = null;
        if (followHud)
        {
            if (localPlayer) followHud.SetPlayer(localPlayer);
            else followHud.gameObject.SetActive(false);
        }
    }

    void ShowFollowHud(ArenaParticipant character)
    {
        if (!followHud) followHud = FindAnyObjectByType<ArenaHud>(FindObjectsInactive.Include);
        if (!followHud)
        {
            // Configure the shared prefab before activating its player event subscriptions.
            var container = new GameObject("Spectator HUD");
            container.SetActive(false);
            followHud = Instantiate(hudPrefab, container.transform).GetComponent<ArenaHud>();
            followHud.match = match;
            followHud.allowLocalShortcuts = false;
            followHud.SetPlayer(character);
            container.SetActive(true);
        }
        else followHud.SetPlayer(character);
        followHud.gameObject.SetActive(true);
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
        if (ManualSpectating || !localDeathView) return;
        StopFollowing(); localDeathView = false;
        localPlayer.player.view.SetFirstPerson(localWasFirstPerson);
        localPlayer.player.view.playerCamera.enabled = true;
        spectatorCamera.enabled = false;
        localInput.enabled = localInputEnabled;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    void EnterPerspective()
    {
        if (!observedView) PreparePerspective();
        FirstPersonSpectating = true;
        RouteCameras();
    }

    void PreparePerspective()
    {
        observedView = FollowedCharacter.player.view;
        observedOccupant = FollowedCharacter.GetComponent<VehicleOccupant>();
        wasMounted = observedOccupant && observedOccupant.IsMounted;
        var camera = observedView.playerCamera;
        previousFirstPerson = observedView.firstPerson;
        previousCameraEnabled = camera.enabled;
        previousCameraMask = camera.cullingMask;
        previousSpectatorMask = spectatorCamera.cullingMask;
        previousSpectatorEnabled = spectatorCamera.enabled;
        previousCameraTarget = camera.targetTexture;
        previousSpectatorTarget = spectatorCamera.targetTexture;
        previousPostProcessing = camera.GetUniversalAdditionalCameraData().renderPostProcessing;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = spectatorCamera.GetUniversalAdditionalCameraData().renderPostProcessing;
        var aimRotation = camera.transform.localRotation;
        camera.enabled = true;
        observedView.SetFirstPerson(true);
        camera.transform.localRotation = aimRotation;
        if (wasMounted)
            foreach (var mesh in observedView.firstPersonRoot.GetComponentsInChildren<Renderer>(true)) mesh.enabled = false;
        // Other soldiers remain visible. Only the observed body becomes shadow-only.
        camera.cullingMask |= 1 << observedView.thirdPersonLayer;
        CacheBody();
        observedView.GetComponent<SoldierWeaponLoadout>().PresentationChanging += MarkBodyDirty;
        spectatorCamera.cullingMask &= ~(1 << observedView.firstPersonLayer);
        observedView.GetComponent<SoldierWeaponLoadout>().RefreshFirstPersonPresentation();
    }

    void MarkBodyDirty() => bodyDirty = true;

    void CacheBody()
    {
        hiddenBody = observedView.thirdPersonRoot.GetComponentsInChildren<Renderer>(true);
        previousShadows = new ShadowCastingMode[hiddenBody.Length];
        for (int i = 0; i < hiddenBody.Length; i++)
            previousShadows[i] = hiddenBody[i].shadowCastingMode;
        bodyDirty = false;
    }

    void BeforeCamera(ScriptableRenderContext context, Camera camera)
    {
        if (!observedView || camera != observedView.playerCamera) return;
        if (bodyDirty) CacheBody();
        for (int i = 0; i < hiddenBody.Length; i++)
            if (hiddenBody[i]) hiddenBody[i].shadowCastingMode = ShadowCastingMode.ShadowsOnly;
    }

    void AfterCamera(ScriptableRenderContext context, Camera camera)
    {
        if (!observedView || camera != observedView.playerCamera) return;
        RestoreBody();
    }

    void RestoreBody()
    {
        if (hiddenBody == null) return;
        for (int i = 0; i < hiddenBody.Length; i++)
            if (hiddenBody[i]) hiddenBody[i].shadowCastingMode = previousShadows[i];
    }

    void RouteCameras()
    {
        if (!observedView) return;
        // Exactly one camera owns the screen; the alternate writes to the HUD texture.
        observedView.playerCamera.targetTexture = FirstPersonSpectating ? null : insetTexture;
        spectatorCamera.targetTexture = FirstPersonSpectating ? insetTexture : null;
        observedView.playerCamera.enabled = FirstPersonSpectating || insetTexture;
        spectatorCamera.enabled = !FirstPersonSpectating || insetTexture;
    }

    void ExitPerspective()
    {
        FirstPersonSpectating = false;
        RouteCameras();
    }

    void ReleasePerspective()
    {
        RestoreBody();
        if (observedView)
        {
            observedView.GetComponent<SoldierWeaponLoadout>().PresentationChanging -= MarkBodyDirty;
            var camera = observedView.playerCamera;
            var aimRotation = camera.transform.localRotation;
            observedView.SetFirstPerson(previousFirstPerson);
            if (observedOccupant && observedOccupant.IsMounted)
                foreach (var mesh in observedView.firstPersonRoot.GetComponentsInChildren<Renderer>(true)) mesh.enabled = false;
            camera.transform.localRotation = aimRotation;
            camera.cullingMask = previousCameraMask;
            camera.enabled = previousCameraEnabled;
            camera.targetTexture = previousCameraTarget;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = previousPostProcessing;
            spectatorCamera.targetTexture = previousSpectatorTarget;
            spectatorCamera.cullingMask = previousSpectatorMask;
            spectatorCamera.enabled = previousSpectatorEnabled;
            observedView.GetComponent<SoldierWeaponLoadout>().RefreshFirstPersonPresentation();
        }
        hiddenBody = null;
        observedView = null;
        observedOccupant = null;
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
        bool mounted = observedOccupant && observedOccupant.IsMounted;
        if (mounted)
        {
            observedOccupant.GetSpectatorCameraPose(true, out var firstPosition, out var firstRotation);
            observedView.playerCamera.transform.SetPositionAndRotation(firstPosition, firstRotation);
            observedView.playerCamera.fieldOfView = observedOccupant.IsLocal && observedOccupant.ActiveVehicleCamera
                ? observedOccupant.ActiveVehicleCamera.fieldOfView : 70f;
            observedOccupant.GetSpectatorCameraPose(false, out var thirdPosition, out var thirdRotation);
            spectatorCamera.transform.SetPositionAndRotation(thirdPosition, thirdRotation);
            spectatorCamera.fieldOfView = observedView.playerCamera.fieldOfView;
        }
        else if (deathWatch)
        {
            // Hold the victim view during the short death watch.
            transform.position = followedHead.position - transform.forward * orbitDistance;
        }
        else
        {
            if (wasMounted)
            {
                observedView.SetFirstPerson(true);
                observedView.GetComponent<SoldierWeaponLoadout>().RefreshFirstPersonPresentation();
                observedView.playerCamera.cullingMask |= 1 << observedView.thirdPersonLayer;
            }
            observedView.GetThirdPersonCameraPose(out var position, out var rotation);
            spectatorCamera.transform.SetPositionAndRotation(position, rotation);
            spectatorCamera.fieldOfView = observedView.ThirdPersonFieldOfView;
        }
        wasMounted = mounted;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        RenderPipelineManager.endCameraRendering -= AfterCamera;
        if (localPlayer)
        {
            if (ManualSpectating) SetManualSpectating(false);
            OnLocalPlayerRespawned();
            localPlayer.health.Died -= OnLocalPlayerDied;
            localPlayer.player.Respawned -= OnLocalPlayerRespawned;
        }
        StopFollowing(); Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

}
