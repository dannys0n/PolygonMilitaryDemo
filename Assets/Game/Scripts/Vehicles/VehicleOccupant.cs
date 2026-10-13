using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;

// Composition on the vehicle-ready player variants. Ordinary soldier prefabs remain on foot.
[DefaultExecutionOrder(200)]
public class VehicleOccupant : MonoBehaviour
{
    public SoldierLife life;
    public SoldierController soldier;
    public int faction = -1;
    public Vehicle MountedVehicle { get; private set; }
    public int SeatIndex { get; private set; }
    public Transform EntryPoint { get; private set; }
    public Vector3 EntryLocalPosition { get; private set; }
    public Quaternion EntryLocalRotation { get; private set; }
    public Vector3 ExitLocalPosition { get; private set; }
    public bool IsMounted => MountedVehicle;
    public bool IsLocal => soldier.inputSource is SoldierLocalInput;
    readonly Dictionary<Behaviour, bool> behaviours = new Dictionary<Behaviour, bool>();
    readonly Dictionary<Renderer, bool> renderers = new Dictionary<Renderer, bool>();
    readonly Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode> shadowModes = new Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode>();
    readonly Dictionary<Collider, bool> colliders = new Dictionary<Collider, bool>();
    readonly Dictionary<Renderer, bool> forcedVisibility = new Dictionary<Renderer, bool>();
    SkinnedMeshRenderer[] seatedBody;
    Transform originalParent;
    VehicleSeatPose seatPose;
    Camera vehicleCamera;
    Animation vehicleCameraShake;
    public Camera ActiveVehicleCamera => vehicleCamera;
    AudioListener vehicleListener;
    AudioListener[] listeners;
    bool[] listenerEnabled;
    bool previousCameraEnabled;
    float lastVehicleYaw, yaw, pitch = 14, flightYaw, flightPitch, exitHeld, hornUntil, freelookYaw, freelookPitch;
    Vector3 departureVelocity;
    public bool CockpitView { get; private set; }
    public bool Freelook { get; private set; }
    public float HornFeedback => Mathf.Clamp01((hornUntil - Time.time) * 3f);
    public float ViewYaw => yaw;
    public string Prompt { get; private set; }

    void OnEnable()
    {
        life.health.Damaged += OnDamaged;
        life.Respawning += BeforeRespawn;
    }
    void OnDisable()
    {
        life.health.Damaged -= OnDamaged;
        life.Respawning -= BeforeRespawn;
        if (IsMounted)
            Leave(true);
    }
    void OnDamaged(float amount, GameObject source)
    {
        if (life.health.IsDead && IsMounted)
            Leave(true);
    }
    void BeforeRespawn()
    {
        if (IsMounted)
            Leave(true);
    }
    void Update()
    {
        if (!IsLocal)
            return;
        var local = (SoldierLocalInput)soldier.inputSource;
        if (!local.isActiveAndEnabled || local.InputBlocked || Time.timeScale == 0)
            return;
        if (life.health.IsDead || !life.actions.enabled && !IsMounted)
        {
            Prompt = "";
            return;
        }
        if (!IsMounted)
        {
            if (departureVelocity.sqrMagnitude > .01f && life.controller.enabled)
            {
                if (life.controller.isGrounded) departureVelocity = Vector3.zero;
                else life.controller.Move(departureVelocity * Time.deltaTime);
            }
            Vehicle best = null;
            int seat = -1;
            float distance = 3.2f;
            foreach (var v in Vehicle.Active)
            {
                if (v.gameObject.scene != gameObject.scene) continue;
                int available = v.FirstAvailableSeat(this);
                if (available < 0) continue;
                float d = Vector3.Distance(transform.position, v.NearestEntryPoint(transform.position).position);
                if (d < distance) { distance = d; best = v; seat = available; }
            }
            Prompt = best ? "[E] ENTER " + best.displayName + " / " + best.seats[seat].name : "";
            if (best && Cursor.lockState == CursorLockMode.Locked && Input.GetKeyDown(KeyCode.E))
                best.BoardFirstAvailable(this);
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (Input.GetMouseButtonDown(0))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        Prompt = "HOLD [E] EXIT   [F1-F" + MountedVehicle.seats.Length + "] SEATS";
        if (Cursor.lockState != CursorLockMode.Locked) { MountedVehicle.DriverInput = default; return; }
        exitHeld = Input.GetKey(KeyCode.E) ? exitHeld + Time.deltaTime : 0;
        if (exitHeld >= .35f) { Leave(false); exitHeld = 0; return; }
        for (int i=0;i<MountedVehicle.seats.Length;i++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.F1+i))) SwitchSeat(i);
        if (Input.GetKeyDown(KeyCode.C)) CockpitView = !CockpitView;
        if (Input.GetKeyDown(KeyCode.V))
        {
            Freelook = !Freelook;
            if (Freelook && MountedVehicle.seats[SeatIndex].role != Vehicle.SeatRole.Driver) { flightYaw = yaw; flightPitch = pitch; }
            if (Freelook) { freelookYaw = yaw; freelookPitch = pitch; }
            else { yaw = freelookYaw; pitch = freelookPitch; }
        }
        var vehicle = MountedVehicle;
        var role = vehicle.seats[SeatIndex].role;
        bool aircraft = vehicle.motor is HelicopterMotor;
        if (aircraft && role == Vehicle.SeatRole.Driver && Input.GetKeyDown(KeyCode.T))
        {
            var helicopter = (HelicopterMotor)vehicle.motor; helicopter.assistEnabled = !helicopter.assistEnabled;
            yaw = flightYaw = vehicleCamera.transform.eulerAngles.y;
            pitch = flightPitch = Mathf.DeltaAngle(0, vehicleCamera.transform.eulerAngles.x);
        }
        Vector2 mouse = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        if (aircraft && role == Vehicle.SeatRole.Driver && !((HelicopterMotor)vehicle.motor).assistEnabled)
            mouse = Vector2.ClampMagnitude(mouse,1f);
        mouse *= local.sensitivity;
        if (!aircraft && role == Vehicle.SeatRole.Driver) yaw += Mathf.DeltaAngle(lastVehicleYaw, vehicle.transform.eulerAngles.y);
        lastVehicleYaw = vehicle.transform.eulerAngles.y;
        yaw += mouse.x; pitch = Mathf.Clamp(pitch - mouse.y, -60, 60);
        if (role == Vehicle.SeatRole.Gunner)
            pitch = Mathf.Clamp(pitch, -vehicle.turret.pitchLimits.y, -vehicle.turret.pitchLimits.x);
        if (role == Vehicle.SeatRole.Driver)
        {
            if (!aircraft) { flightYaw = vehicle.transform.eulerAngles.y; flightPitch = CockpitView ? 0 : 14; }
            else if (!Freelook)
            {
                flightYaw = yaw; flightPitch = pitch;
                var helicopter = (HelicopterMotor)vehicle.motor;
                if (helicopter.assistEnabled)
                {
                    Quaternion view = Quaternion.Euler(pitch,yaw,0);
                    Vector3 origin = CameraPosition(vehicle,view,false,true,true);
                    var aimLauncher = vehicle.GetComponent<VehicleLauncher>();
                    Vector3 direction = aimLauncher ? aimLauncher.DirectionToAimPoint(new Ray(origin,view*Vector3.forward),180) : view*Vector3.forward;
                    flightYaw = Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
                    flightPitch = -Mathf.Asin(Mathf.Clamp(direction.y,-1,1))*Mathf.Rad2Deg;
                }
                flightPitch = Mathf.Clamp(flightPitch,-helicopter.maxAimPitch,helicopter.maxAimPitch);
            }
            vehicle.DriverInput = new VehicleInput {
                move = Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")),1),
                lift = (Input.GetKey(KeyCode.Space)?1:0) - (Input.GetKey(KeyCode.LeftControl)?1:0),
                heading = flightYaw, movementHeading = Freelook ? freelookYaw : yaw, viewPitch = flightPitch, directionalFlight = aircraft,
                aim = aircraft && !Freelook && Input.GetMouseButton(1),
                pitchRate = Freelook ? 0 : Mathf.Clamp(-mouse.y * .35f,-1,1), yaw = Freelook ? 0 : Mathf.Clamp(mouse.x * .35f,-1,1),
                handbrake = !aircraft && Input.GetKey(KeyCode.Z), boost = !aircraft && Input.GetKey(KeyCode.LeftShift)
            };
            if (!aircraft && Input.GetKeyDown(KeyCode.H)) hornUntil = Time.time + .35f;
            if (aircraft && Input.GetMouseButton(0) && vehicle.TryGetComponent<VehicleLauncher>(out var launcher)) launcher.TryFire(this);
        }
        if (role == Vehicle.SeatRole.Gunner)
                {
            if (!Freelook)
            {
                Quaternion view = Quaternion.Euler(pitch,yaw,0);
                vehicle.turret.AimAt(new Ray(CameraPosition(vehicle,view,true,aircraft,false),view*Vector3.forward));
            }
            if(Input.GetMouseButton(0)) vehicle.turret.TryFire(this);
        }
        if (vehicleCamera) vehicleCamera.fieldOfView = Input.GetMouseButton(1) && (aircraft || role==Vehicle.SeatRole.Gunner) ? 50 : 70;
    }

    public void Mount(Vehicle vehicle, int seat)
    {
        EntryPoint = vehicle.NearestEntryPoint(transform.position);
        EntryLocalPosition = vehicle.transform.InverseTransformPoint(transform.position);
        EntryLocalRotation = Quaternion.Inverse(vehicle.transform.rotation) * transform.rotation;
        MountedVehicle = vehicle;
        SeatIndex = seat;
        originalParent = transform.parent;
        life.actions.CancelActions();
        life.movement.ResetMovement();
        behaviours.Clear();
        renderers.Clear(); forcedVisibility.Clear();
        shadowModes.Clear();
        colliders.Clear();
        foreach (var b in new Behaviour[] { soldier, life.actions, life.movement, life.look, life.loadout, life.view, GetComponent<SoldierWeaponPresentation>() })
            SaveDisable(b);
        foreach (var b in GetComponentsInChildren<Behaviour>(true))
            if (b is Animator || b is RigBuilder || b is IRigConstraint || b is SoldierMotionGrip || b is SoldierFootIK)
                SaveDisable(b);
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            renderers[r] = r.enabled; forcedVisibility[r] = r.forceRenderingOff;
            shadowModes[r] = r.shadowCastingMode;
            r.enabled = false;
        }
        foreach (var c in GetComponentsInChildren<Collider>(true))
        {
            colliders[c] = c.enabled;
            c.enabled = false;
        }
        var agent = GetComponent<NavMeshAgent>();
        if (agent)
            SaveDisable(agent);
        transform.SetParent(vehicle.seats[seat].anchor, false);
        transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        seatPose = life.actions.animator.GetComponent<VehicleSeatPose>();
        if (!seatPose) seatPose = life.actions.animator.gameObject.AddComponent<VehicleSeatPose>();
        var seated = vehicle.seats[seat];
        if(seated.showCharacter) seatPose.Begin(life.actions.animator, seated, seated.leftGrip, seated.rightGrip);
        seatedBody = System.Array.FindAll(life.view.thirdPersonRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true),
            skin => skin.rootBone && skin.rootBone.IsChildOf(life.actions.animator.transform));
        UpdateSeatedVisibility();
        if (!IsLocal)
            return;
        previousCameraEnabled = life.view.playerCamera.enabled;
        life.view.playerCamera.enabled = false;
        listeners = GetComponentsInChildren<AudioListener>(true);
        listenerEnabled = new bool[listeners.Length];
        for (int i = 0; i < listeners.Length; i++)
        {
            listenerEnabled[i] = listeners[i].enabled;
            listeners[i].enabled = false;
        }
        if (!vehicleCamera)
        {
            var go = new GameObject("Vehicle owner camera", typeof(Camera), typeof(AudioListener));
            vehicleCamera = go.GetComponent<Camera>();
            vehicleListener = go.GetComponent<AudioListener>();
            var shake = new GameObject("Weapon camera shake",typeof(Animation));
            shake.transform.SetParent(go.transform,false);
            vehicleCameraShake=shake.GetComponent<Animation>();
            vehicleCameraShake.playAutomatically=false;
        }
        ResetVehicleCameraShake();
        if (vehicle.turret) vehicleCameraShake.AddClip(vehicle.turret.cameraShake,"FireShake");
        vehicleCamera.CopyFrom(life.view.playerCamera);
        vehicleCamera.cullingMask = (life.view.playerCamera.cullingMask | (1 << life.view.thirdPersonLayer)) & ~(1 << life.view.firstPersonLayer);
        vehicleCamera.fieldOfView = 70;
        vehicleCamera.nearClipPlane = .1f;
        vehicleCamera.enabled = vehicleListener.enabled = true;
        lastVehicleYaw = yaw = flightYaw = vehicle.transform.eulerAngles.y;
        pitch = flightPitch = vehicle.motor is HelicopterMotor ? 0 : 14; CockpitView = Freelook = false; exitHeld = 0; departureVelocity = Vector3.zero;
    }
    void SaveDisable(Behaviour b)
    {
        if (behaviours.ContainsKey(b))
            return;
        behaviours[b] = b.enabled;
        b.enabled = false;
    }
    public bool Leave(bool force)
    {
        if (!IsMounted)
            return false;
        var vehicle = MountedVehicle;
        var seat = vehicle.seats[SeatIndex];
        if (!force && !IsLocal && !vehicle.CanExit)
            return false;
        if (!FindExit(vehicle, seat, out var position))
        {
            if (!force && !IsLocal) return false;
            position = seat.exit.position;
            if (!force && Physics.CheckCapsule(position + Vector3.up*.4f, position + Vector3.up*1.5f, .3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
        }
        departureVelocity = IsLocal ? vehicle.Body.linearVelocity : Vector3.zero;
        if (force && position == default)
            position = seat.exit.position;
        var agent = GetComponent<NavMeshAgent>();
        bool restoreAgent = agent && behaviours.TryGetValue(agent, out bool agentWasEnabled) && agentWasEnabled;
        // Registration occurs when enabled, so validate BEFORE restoring the agent.
        // Forced exits/deaths may be off the infantry mesh; respawn restores it later.
        bool validAgentExit = restoreAgent && NavMesh.SamplePosition(position, out _, .25f,
            new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask });
        ExitLocalPosition = vehicle.transform.InverseTransformPoint(position);
        seat.occupant = null;
        vehicle.ReleaseReservation(this);
        if (seat.role == Vehicle.SeatRole.Driver)
            vehicle.DriverInput = default;
        seatPose.End();
        MountedVehicle = null;
        transform.SetParent(originalParent, true);
        transform.SetPositionAndRotation(position, Quaternion.Euler(0, vehicle.transform.eulerAngles.y, 0));
        foreach (var pair in colliders)
        if (pair.Key)
            pair.Key.enabled = pair.Value;
        foreach (var pair in renderers)
        if (pair.Key)
            pair.Key.enabled = pair.Value;
        foreach (var pair in forcedVisibility) if(pair.Key) pair.Key.forceRenderingOff=pair.Value;
        foreach (var pair in shadowModes)
            if (pair.Key) pair.Key.shadowCastingMode = pair.Value;
        foreach (var pair in behaviours)
        if (pair.Key && pair.Key != agent)
            pair.Key.enabled = pair.Value;
        life.movement.ResetMovement();
        if (restoreAgent && validAgentExit && !life.health.IsDead)
            agent.enabled = true;
        if (IsLocal)
        {
            ResetVehicleCameraShake();
            vehicleCamera.enabled = vehicleListener.enabled = false;
            life.view.playerCamera.enabled = previousCameraEnabled;
            for (int i = 0; i < listeners.Length; i++)
            if (listeners[i])
                listeners[i].enabled = listenerEnabled[i];
        }
        return true;
    }
    bool FindExit(Vehicle vehicle, Vehicle.Seat seat, out Vector3 result)
    {
        result = default;
        if (IsLocal)
        {
            float side = Vector3.Dot(vehicle.transform.right, Quaternion.Euler(0,yaw,0)*Vector3.forward);
            if (Mathf.Abs(side) > .15f) foreach (var candidate in vehicle.seats)
                if (Mathf.Sign(candidate.exit.localPosition.x)==Mathf.Sign(side)) { seat=candidate;break; }
        }
        for (int i = 0; i < 4; i++)
        {
            Vector3 proposed = seat.exit.position + vehicle.transform.right * (i % 2 == 0 ? 1 : -1) * (i / 2);
            RaycastHit ground = default;
            bool found = false;
            foreach (var hit in Physics.RaycastAll(proposed + Vector3.up * 2, Vector3.down, 4, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(vehicle.transform) && (!found || hit.distance < ground.distance))
                {
                    ground = hit;
                    found = true;
                }
            if (!found)
                continue;
            proposed = ground.point + Vector3.up * .08f;
            var navigation = GetComponent<NavMeshAgent>();
            if (!IsLocal && navigation)
            {
                // Exit onto this infantry agent's mesh, not just any physical floor.
                // Reject other floors and distant projections rather than teleporting.
                if (!NavMesh.SamplePosition(proposed, out var mesh, .75f,
                    new NavMeshQueryFilter { agentTypeID = navigation.agentTypeID, areaMask = navigation.areaMask })
                    || Mathf.Abs(mesh.position.y - ground.point.y) > .2f
                    || Vector3.ProjectOnPlane(mesh.position-proposed, Vector3.up).magnitude > .2f) continue;
                proposed = new Vector3(proposed.x, mesh.position.y + .08f, proposed.z);
            }
            var cc = life.controller;
            Vector3 center = proposed + cc.center;
            float half = Mathf.Max(0, cc.height * .5f - cc.radius);
            bool blocked = false;
            foreach (var c in Physics.OverlapCapsule(center + Vector3.up * half, center - Vector3.up * half, cc.radius * .95f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!c.transform.IsChildOf(transform) && c != ground.collider)
                {
                    blocked = true;
                    break;
                }
            if (!blocked)
            {
                result = proposed;
                return true;
            }
        }
        return false;
    }
    public bool SwitchSeat(int index)
    {
        if (!IsMounted || index<0 || index>=MountedVehicle.seats.Length || index==SeatIndex) return false;
        var vehicle=MountedVehicle; var seat=vehicle.seats[index];
        if (vehicle.IsDestroyed || life.health.IsDead) return false;
        var previousSeat=vehicle.seats[SeatIndex];
        var displaced=seat.occupant;
        if (displaced)
        {
            // Players may trade seats with an AI driver/pilot, never another player.
            if (!IsLocal || seat.role!=Vehicle.SeatRole.Driver
                || !(displaced.soldier.inputSource is ArenaAIInput) || displaced.life.health.IsDead
                || (faction>=0 && displaced.faction>=0 && faction!=displaced.faction)) return false;
        }
        seat.reservation=null;
        previousSeat.reservation=null;
        if (previousSeat.role==Vehicle.SeatRole.Driver || seat.role==Vehicle.SeatRole.Driver)
            vehicle.DriverInput=default;
        int previousIndex=SeatIndex;
        previousSeat.occupant=displaced;
        seat.occupant=this;
        if (displaced) displaced.MoveToSeat(previousIndex);
        MoveToSeat(index);
        return true;
    }
    void MoveToSeat(int index)
    {
        SeatIndex=index;
        var vehicle=MountedVehicle;
        var seat=vehicle.seats[index];
        seatPose.End();
        transform.SetParent(seat.anchor,false);transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
        if(seat.showCharacter) seatPose.Begin(life.actions.animator, seat, seat.leftGrip, seat.rightGrip);
        UpdateSeatedVisibility();
        if(IsLocal)ResetVehicleCameraShake();
        flightYaw=yaw=vehicle.transform.eulerAngles.y;flightPitch=pitch=0;Freelook=false;
        lastVehicleYaw=vehicle.transform.eulerAngles.y;
    }
    void UpdateSeatedVisibility()
    {
        var seat=MountedVehicle.seats[SeatIndex];
        bool visible=seat.showCharacter && !(IsLocal && CockpitView && seat.role==Vehicle.SeatRole.Gunner);
        foreach(var skin in seatedBody) if(skin){skin.enabled=visible;skin.forceRenderingOff=!visible;skin.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;}
    }
    void LateUpdate()
    {
        if(IsMounted) UpdateSeatedVisibility();
        if (!IsMounted || !IsLocal)
            return;
        var v = MountedVehicle;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
        bool gunner = v.seats[SeatIndex].role == Vehicle.SeatRole.Gunner;
        bool aircraft=v.motor is HelicopterMotor;
        bool pilot = aircraft && v.seats[SeatIndex].role == Vehicle.SeatRole.Driver;
        if (pilot && !Freelook && !((HelicopterMotor)v.motor).assistEnabled)
            rotation = CockpitView ? v.Body.rotation : Quaternion.LookRotation(v.transform.forward,
                !((HelicopterMotor)v.motor).assistEnabled || Mathf.Abs(Vector3.Dot(v.transform.forward,Vector3.up))>.98f ? v.transform.up : Vector3.up);
        Vector3 desired = CameraPosition(v,rotation,gunner,aircraft,pilot);
        var shake=vehicleCameraShake.transform;
        vehicleCamera.transform.SetPositionAndRotation(desired+rotation*shake.localPosition,rotation*shake.localRotation);
    }
    Vector3 CameraPosition(Vehicle v, Quaternion rotation, bool gunner, bool aircraft, bool pilot, bool? cockpitOverride = null)
    {
        bool cockpit = cockpitOverride ?? CockpitView;
        Vector3 target = gunner ? v.turret.cameraPoint.position : v.cameraTarget.position;
        if (pilot && ((HelicopterMotor)v.motor).assistEnabled && !cockpit)
            target = v.transform.position + Vector3.up * v.cameraTarget.localPosition.y;
        Vector3 desired;
        if (cockpit) desired = v.seats[SeatIndex].eye ? v.seats[SeatIndex].eye.position : gunner ? target : v.seats[SeatIndex].anchor.position + v.transform.up * (aircraft ? 1.15f : 1f);
        else desired = target + rotation * (aircraft ? new Vector3(4,1.5f,-20) : new Vector3(0,1,-7));
        if (!cockpit)
        {
            Vector3 offset = desired - target;
            float distance = offset.magnitude;
            foreach (var hit in Physics.SphereCastAll(target, .3f, offset.normalized, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(v.transform)) distance = Mathf.Min(distance, Mathf.Max(.3f, hit.distance - .2f));
            desired = target + offset.normalized * distance;
        }
        return desired;
    }
    public void GetSpectatorCameraPose(bool cockpit, out Vector3 position, out Quaternion rotation)
    {
        var v = MountedVehicle;
        bool gunner = v.seats[SeatIndex].role == Vehicle.SeatRole.Gunner;
        bool aircraft = v.motor is HelicopterMotor;
        bool pilot = aircraft && v.seats[SeatIndex].role == Vehicle.SeatRole.Driver;
        // Local occupants already own the live view; AI view comes from its control intent.
        rotation = IsLocal ? Quaternion.Euler(pitch, yaw, 0)
            : gunner ? Quaternion.LookRotation(v.turret.RequestedAimDirection, Vector3.up)
            : aircraft ? Quaternion.Euler(v.DriverInput.viewPitch, v.DriverInput.heading, 0)
            : Quaternion.Euler(cockpit ? 0 : 14, v.transform.eulerAngles.y, 0);
        if (pilot && !((HelicopterMotor)v.motor).assistEnabled && (!IsLocal || !Freelook))
            rotation = cockpit ? v.Body.rotation : Quaternion.LookRotation(v.transform.forward, v.transform.up);
        position = CameraPosition(v, rotation, gunner, aircraft, pilot, cockpit);
    }

    public void PlayVehicleCameraShake()
    {
        vehicleCameraShake["FireShake"].time=0;
        vehicleCameraShake.Play("FireShake",PlayMode.StopAll);
    }
    void ResetVehicleCameraShake()
    {
        vehicleCameraShake.Stop();
        vehicleCameraShake.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
    }
    void OnDestroy()
    {
        if (vehicleCamera)
            Destroy(vehicleCamera.gameObject);
    }
}

