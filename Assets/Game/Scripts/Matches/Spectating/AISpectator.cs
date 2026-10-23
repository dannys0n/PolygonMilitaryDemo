using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Minimal observation controls and feedback for the AI-versus-AI test scene.
[DefaultExecutionOrder(300)]
[UnityEngine.Scripting.APIUpdating.MovedFrom(true, null, "Game.Runtime", "ArenaAISpectator")]
public class AISpectator : MonoBehaviour
{
    public MatchController match;
    public Camera spectatorCamera;
    public GameObject hudPrefab;
    GameplayHud followHud;
    public float moveSpeed = 15f;
    public float lookSensitivity = 2f;
    [Header("Follow camera")]
    [Min(.5f)] public float orbitDistance = 2.5f;
    public float zoomSpeed = 2f;
    [Min(0f)] public float deathWatchDuration = 2f;
    [Tooltip("Wide lens used only while rendering the alternate monitor.")]
    [Range(60f,110f)] public float insetFieldOfView = 85f;
    [Range(5,60)] public int insetFramesPerSecond = 30;
    public Camera InsetCamera => insetTexture && observedView ? (FirstPersonSpectating ? spectatorCamera : observedView.playerCamera) : null;
    [Tooltip("Optional local player: spectate while dead and return on respawn.")]
    public MatchParticipant localPlayer;
    bool deathWatch, restorePerspective, killerDied, localDeathView, localWasFirstPerson;
    bool releasingPresentation;
    float deathWatchUntil;
    MatchParticipant pendingKiller;
    SoldierLocalInput localInput;
    bool localInputEnabled;
    public MatchParticipant FollowedCharacter { get; private set; }
    Transform followedHead;
    public bool FirstPersonSpectating { get; private set; }
    public Camera PresentationCamera => FirstPersonSpectating && observedView ? observedView.playerCamera : spectatorCamera;
    SoldierFirstPersonView observedView;
    bool previousFirstPerson, previousCameraEnabled, previousSpectatorEnabled;
    bool previousPostProcessing;
    int previousCameraMask, previousSpectatorMask;
    RenderTexture previousCameraTarget, previousSpectatorTarget, insetTexture;
    Renderer[] hiddenBody;
    UnityEngine.Rendering.ShadowCastingMode[] previousShadows;
    bool[] bodyBatchingOverrides;
    MaterialPropertyBlock bodyBatchingProperties;
    static readonly int PerCameraVisibility = Shader.PropertyToID("_ArenaPerCameraVisibility");
    float yaw, pitch;
    public bool ShowControls { get; private set; } = true;
    public bool IsWatchingDeath => deathWatch || VehicleRespawnRemaining>0;
    MatchParticipant respawnWatchTarget;
    bool respawnWatchFirstPerson;
    public float VehicleRespawnRemaining => respawnWatchTarget ? respawnWatchTarget.RespawnRemaining : 0;
    public bool IsSpectating => !localPlayer || localDeathView || ManualSpectating;
    public bool ManualSpectating { get; private set; }
    public enum FollowFilter { All, Infantry, GroundVehicles, AirborneVehicles }
    public FollowFilter Filter { get; private set; }
    bool waitingForFilteredTarget, filteredFirstPerson;
    public bool WaitingForFilteredTarget => waitingForFilteredTarget;
    float nextFilteredSearch;
    public bool MatchesFilter(MatchParticipant actor)
    {
        if (!actor) return false;
        var occupant = actor.GetComponent<VehicleOccupant>();
        bool mounted = occupant && occupant.IsMounted && occupant.MountedVehicle;
        switch (Filter)
        {
            case FollowFilter.Infantry: return !mounted;
            case FollowFilter.GroundVehicles: return mounted && !occupant.MountedVehicle.IsAircraft;
            case FollowFilter.AirborneVehicles: return mounted && occupant.MountedVehicle.IsAircraft;
            default: return true;
        }
    }
    public void SetFilter(FollowFilter value)
    {
        Filter = value;
        if (IsSpectating && !deathWatch && (!FollowedCharacter || !MatchesFilter(FollowedCharacter))) CycleCharacter(1);
    }
    public void CycleFilter() => SetFilter((FollowFilter)(((int)Filter + 1) % 4));
    public bool CanFollowFiltered(MatchParticipant actor) => CanFollowAlive(actor) && !actor.IsSpectator && MatchesFilter(actor);
    SpectatorInterest interest;
    public bool InterestMode => interest && interest.Enabled;
    public void SetInterestMode(bool value) => interest.SetEnabled(value);
    void Awake()
    {
        var insetRenderer = GetComponent<SpectatorInsetRenderer>();
        if (!insetRenderer) insetRenderer = gameObject.AddComponent<SpectatorInsetRenderer>();
        insetRenderer.spectator = this;
        interest = GetComponent<SpectatorInterest>();
        if (!interest) interest = gameObject.AddComponent<SpectatorInterest>();
        interest.spectator = this;
        interest.SetEnabled(true);
    }
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
            // Enter observation with a free camera; following is an explicit choice.
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
    public bool DeploymentActive { get; private set; }
    public void SetDeploymentActive(bool value)
    {
        if(DeploymentActive==value)return;
        DeploymentActive=value;
        if(!value || !IsSpectating)return;
        // Deployment owns the view: discard all follow/death/respawn transitions.
        StopFollowing();
        spectatorCamera.enabled=true;
        yaw=transform.eulerAngles.y;pitch=Mathf.DeltaAngle(0f,transform.eulerAngles.x);
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
        if (!IsSpectating || DeploymentActive) return;
        if(respawnWatchTarget)
        {
            var target=respawnWatchTarget;
            if(target==localPlayer && target.RespawnRemaining<=0)respawnWatchTarget=null;
            else if(target!=localPlayer && CanFollowFiltered(target))
            {
                var next = InterestMode ? interest.Choose(Time.time) : target;
                if (next) FollowWithPerspective(next, respawnWatchFirstPerson);
            }
        }
        if (!localPlayer && Input.GetKeyDown(KeyCode.Backspace)) { CancelDeathWatch(); match.BeginMatch(); }
        if (deathWatch && Time.time >= deathWatchUntil) CompleteDeathWatch();
        if (ControlsBlocked) return;
        if (Input.GetKeyDown(KeyCode.I)) SetInterestMode(!InterestMode);
        if (Input.GetKeyDown(KeyCode.V)) CycleFilter();
        if (!deathWatch && FollowedCharacter && !CanFollowFiltered(FollowedCharacter)) CycleCharacter(1);
        if (waitingForFilteredTarget && Time.unscaledTime >= nextFilteredSearch)
        {
            nextFilteredSearch = Time.unscaledTime + .5f;
            CycleCharacter(1);
        }
        if (Input.GetKeyDown(KeyCode.L)) ShowControls = !ShowControls;
        if (Input.GetKeyDown(KeyCode.F))
        {
            SetInterestMode(false);
            if (FollowedCharacter || waitingForFilteredTarget) StopFollowing();
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
        yaw += Input.GetAxis("Mouse X") * lookSensitivity * MouseSensitivity.Value;
        pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSensitivity * MouseSensitivity.Value, -85f, 85f);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0);
        if (FollowedCharacter) return;
        Vector3 move = new Vector3(Input.GetAxisRaw("Horizontal"),
            (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f), Input.GetAxisRaw("Vertical"));
        transform.position += transform.TransformDirection(Vector3.ClampMagnitude(move, 1f)) * moveSpeed
            * (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f) * Time.deltaTime;
    }

    public void Follow(MatchParticipant character)
        => FollowWithPerspective(character, FirstPersonSpectating);

    void FollowWithPerspective(MatchParticipant character, bool perspective)
    {
        if(DeploymentActive)return;
        respawnWatchTarget=null;
        waitingForFilteredTarget = false;
        CancelDeathWatch();
        if (FollowedCharacter) FollowedCharacter.health.Died -= OnFollowedCharacterDied;
        ReleasePerspective();
        FollowedCharacter = character;
        bool alternatePerspective = interest.RecordFollow(character);
        FollowedCharacter.health.Died += OnFollowedCharacterDied;
        followedHead = character.player.actions.animator.GetBoneTransform(HumanBodyBones.Head);
        ShowFollowHud(character);
        PreparePerspective();
        FirstPersonSpectating = perspective ^ alternatePerspective;
        RouteCameras();
    }

    public void StopFollowing()
    {
        respawnWatchTarget=null;
        waitingForFilteredTarget = false;
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

    void ShowFollowHud(MatchParticipant character)
    {
        if (!followHud) followHud = FindAnyObjectByType<GameplayHud>(FindObjectsInactive.Include);
        if (!followHud)
        {
            // Configure the shared prefab before activating its player event subscriptions.
            var container = new GameObject("Spectator HUD");
            container.SetActive(false);
            followHud = Instantiate(hudPrefab, container.transform).GetComponent<GameplayHud>();
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
        interest.EndFollowOnDeath();
        if(wasMounted){BeginVehicleRespawnWatch(FollowedCharacter,FirstPersonSpectating);return;}
        BeginDeathWatch(source, FirstPersonSpectating);
    }
    void BeginVehicleRespawnWatch(MatchParticipant actor,bool perspective)
    {
        var camera=FirstPersonSpectating && observedView?observedView.playerCamera:spectatorCamera;
        var position=camera.transform.position;var rotation=camera.transform.rotation;
        StopFollowing();
        respawnWatchTarget=actor;respawnWatchFirstPerson=perspective;
        transform.SetPositionAndRotation(position,rotation);
        yaw=rotation.eulerAngles.y;pitch=Mathf.DeltaAngle(0,rotation.eulerAngles.x);
        spectatorCamera.enabled=true;
    }

    void BeginDeathWatch(GameObject source, bool perspective)
    {
        if(DeploymentActive)return;
        CancelDeathWatch();
        restorePerspective = perspective;
        ExitPerspective();
        // The orbit camera watches the victim; the killer is tracked independently.
        deathWatch = true; deathWatchUntil = Time.time + deathWatchDuration;
        pendingKiller = source ? source.GetComponentInParent<MatchParticipant>() : null;
        killerDied = !CanFollowAlive(pendingKiller) || pendingKiller == FollowedCharacter;
        if (pendingKiller) pendingKiller.health.Died += OnPendingKillerDied;
    }

    void OnPendingKillerDied(GameObject source) => killerDied = true;

    bool CanFollowAlive(MatchParticipant actor) => actor && actor.match == match && actor.player
        && actor.isActiveAndEnabled && !actor.health.IsDead && !actor.IsAwaitingSpawn;

    void CompleteDeathWatch()
    {
        var next = InterestMode ? interest.Choose(Time.time) : !killerDied && CanFollowFiltered(pendingKiller) ? pendingKiller : null;
        if (!next && !InterestMode)
            foreach (var actor in match.participants)
                if (CanFollowFiltered(actor) && actor != FollowedCharacter && actor != pendingKiller) { next = actor; break; }
        bool perspective = restorePerspective;
        if (!next)
        {
            StopFollowing(); waitingForFilteredTarget = true; filteredFirstPerson = perspective;
            nextFilteredSearch = Time.unscaledTime + .5f; return;
        }
        CancelDeathWatch();
        FollowWithPerspective(next, perspective);
    }

    void CancelDeathWatch()
    {
        if (pendingKiller) pendingKiller.health.Died -= OnPendingKillerDied;
        pendingKiller = null; deathWatch = false;
    }

    void OnLocalPlayerDied(GameObject source)
    {
        bool vehicleDeath=localPlayer.match.rules is SandboxConquestRules sandbox
            && sandbox.PlayerVehicle && sandbox.PlayerVehicle.IsDestroyed;
        localDeathView = true;
        localWasFirstPerson = localPlayer.player.view.firstPerson;
        localInputEnabled = localInput.enabled; localInput.enabled = false;
        localPlayer.player.view.SetFirstPerson(false);
        localPlayer.player.view.playerCamera.enabled = false;
        spectatorCamera.enabled = true;
        yaw = localPlayer.transform.eulerAngles.y; pitch = 15f;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        if(DeploymentActive){StopFollowing();return;}
        if(vehicleDeath)
        {
            transform.position=localPlayer.transform.position+Vector3.up*3;
            BeginVehicleRespawnWatch(localPlayer,localWasFirstPerson);return;
        }
        Follow(localPlayer);
        BeginDeathWatch(source, localWasFirstPerson);
    }

    void OnLocalPlayerRespawned()
    {
        if (ManualSpectating || !localDeathView) return;
        StopFollowing(); localDeathView = false;
        if(releasingPresentation)localPlayer.player.view.RestoreCameraPerspective(localWasFirstPerson);
        else localPlayer.player.view.SetFirstPerson(localWasFirstPerson);
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
        if (wasMounted && observedView.TryGetComponent<SoldierFirstPersonBody>(out var body)) body.PrepareForVehicleSpectating();
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
        RestoreBody();
        RestoreBodyBatching();
        bodyBatchingProperties ??= new MaterialPropertyBlock();
        hiddenBody = observedView.thirdPersonRoot.GetComponentsInChildren<Renderer>(true);
        previousShadows = new ShadowCastingMode[hiddenBody.Length];
        bodyBatchingOverrides = new bool[hiddenBody.Length];
        for (int i = 0; i < hiddenBody.Length; i++)
        {
            previousShadows[i] = hiddenBody[i].shadowCastingMode;
            if (hiddenBody[i] is MeshRenderer mesh && !mesh.HasPropertyBlock())
            {
                // GPU Resident Drawer cannot represent per-camera visibility edits.
                // An individual property block selects Unity's documented fallback.
                bodyBatchingOverrides[i] = true;
                bodyBatchingProperties.Clear();
                bodyBatchingProperties.SetFloat(PerCameraVisibility, 1);
                mesh.SetPropertyBlock(bodyBatchingProperties);
            }
        }
        bodyDirty = false;
    }

    void RestoreBodyBatching()
    {
        if (bodyBatchingOverrides == null || hiddenBody == null) return;
        for (int i = 0; i < hiddenBody.Length; i++)
            if (bodyBatchingOverrides[i] && hiddenBody[i] is MeshRenderer mesh && mesh)
            {
                mesh.GetPropertyBlock(bodyBatchingProperties);
                if (bodyBatchingProperties.GetFloat(PerCameraVisibility) == 1) mesh.SetPropertyBlock(null);
            }
        bodyBatchingOverrides = null;
    }

    Camera insetLensCamera;
    float insetOriginalFieldOfView;

    void BeforeCamera(ScriptableRenderContext context, Camera camera)
    {
        if (insetTexture && camera.targetTexture == insetTexture)
        {
            insetLensCamera = camera;
            insetOriginalFieldOfView = camera.fieldOfView;
            camera.fieldOfView = insetFieldOfView;
        }
        if (!observedView || camera != observedView.playerCamera) return;
        if (observedOccupant && observedOccupant.IsMounted
            && !observedOccupant.MountedVehicle.seats[observedOccupant.SeatIndex].weaponMountedView) return;
        if (FollowedCharacter && FollowedCharacter.health.IsDead) { RestoreBody(); return; }
        if (bodyDirty) CacheBody();
        for (int i = 0; i < hiddenBody.Length; i++)
            if (hiddenBody[i]) hiddenBody[i].shadowCastingMode = ShadowCastingMode.ShadowsOnly;
    }

    void AfterCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera == insetLensCamera)
        {
            camera.fieldOfView = insetOriginalFieldOfView;
            insetLensCamera = null;
        }
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
        observedView.playerCamera.enabled = FirstPersonSpectating;
        spectatorCamera.enabled = !FirstPersonSpectating;
    }

    void ExitPerspective()
    {
        FirstPersonSpectating = false;
        RouteCameras();
    }

    void ReleasePerspective()
    {
        RestoreBody();
        RestoreBodyBatching();
        if (observedView)
        {
            observedView.GetComponent<SoldierWeaponLoadout>().PresentationChanging -= MarkBodyDirty;
            var camera = observedView.playerCamera;
            var aimRotation = camera.transform.localRotation;
            // Restore render ownership before rebuilding ordinary equipment.
            // Otherwise the still-observed camera can briefly rent FPS visuals
            // solely to discard them, or recreate the pool during scene unload.
            camera.enabled = previousCameraEnabled;
            camera.targetTexture = previousCameraTarget;
            if(releasingPresentation)observedView.RestoreCameraPerspective(previousFirstPerson);
            else observedView.SetFirstPerson(previousFirstPerson);
            if (observedOccupant && observedOccupant.IsMounted)
                foreach (var mesh in observedView.firstPersonRoot.GetComponentsInChildren<Renderer>(true)) mesh.enabled = false;
            camera.transform.localRotation = aimRotation;
            camera.cullingMask = previousCameraMask;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = previousPostProcessing;
            spectatorCamera.targetTexture = previousSpectatorTarget;
            spectatorCamera.cullingMask = previousSpectatorMask;
            spectatorCamera.enabled = previousSpectatorEnabled;
        }
        hiddenBody = null;
        observedView = null;
        observedOccupant = null;
        FirstPersonSpectating = false;
    }

    void CycleCharacter(int direction)
    {
        bool perspective = FirstPersonSpectating || waitingForFilteredTarget && filteredFirstPerson;
        var actors = match.participants;
        if (actors.Length == 0) { StopFollowing(); return; }
        int current = System.Array.IndexOf(actors, FollowedCharacter);
        int start = current < 0 ? (direction > 0 ? -1 : 0) : current;
        for (int i = 1; i <= actors.Length; i++)
        {
            var actor = actors[(start + direction * i + actors.Length * 2) % actors.Length];
            if (CanFollowFiltered(actor)) { FollowWithPerspective(actor, perspective); return; }
        }
        StopFollowing();
        waitingForFilteredTarget = true; filteredFirstPerson = perspective;
    }

    void LateUpdate()
    {
        if (!FollowedCharacter) return;
        bool mounted = observedOccupant && observedOccupant.IsMounted;
        if (mounted)
        {
            if (!wasMounted && observedView.TryGetComponent<SoldierFirstPersonBody>(out var body)) body.PrepareForVehicleSpectating();
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
        releasingPresentation = true;
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
        releasingPresentation = false;
    }

}
