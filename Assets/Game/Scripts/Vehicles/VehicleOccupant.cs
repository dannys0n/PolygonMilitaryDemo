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
    public int SeatPage { get; private set; }
    public Transform EntryPoint { get; private set; }
    public Vector3 EntryLocalPosition { get; private set; }
    public Quaternion EntryLocalRotation { get; private set; }
    public Vector3 ExitLocalPosition { get; private set; }
    public bool IsMounted => MountedVehicle;
    public bool IsLocal => soldier.IsLocallyControlled;
    public bool IsAI => soldier.IsAIControlled;
    public bool IsHuman => soldier.IsHumanControlled;
    readonly Dictionary<Behaviour, bool> behaviours = new Dictionary<Behaviour, bool>();
    readonly Dictionary<Renderer, bool> renderers = new Dictionary<Renderer, bool>();
    readonly Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode> shadowModes = new Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode>();
    readonly Dictionary<Collider, bool> colliders = new Dictionary<Collider, bool>();
    readonly Dictionary<Renderer, bool> forcedVisibility = new Dictionary<Renderer, bool>();
    SkinnedMeshRenderer[] seatedBody;
    bool? seatedVisible;
    readonly Dictionary<Rigidbody,bool> mountedBodyCollisions=new Dictionary<Rigidbody,bool>();
    Transform originalParent;
    VehicleSeatPose seatPose;
    SoldierFirstPersonBody firstPersonBody;
    Camera vehicleCamera;
    Animation vehicleCameraShake;
    public Camera ActiveVehicleCamera => vehicleCamera;
    AudioListener vehicleListener;
    AudioListener[] listeners;
    bool[] listenerEnabled;
    bool previousCameraEnabled;
    float lastVehicleYaw, yaw, pitch = 14, flightYaw, flightPitch, exitHeld, hornUntil, freelookYaw, freelookPitch;
    bool interactionConsumed, aimHeld;
    bool cockpitView;
    public bool CockpitView
    {
        get => cockpitView && (!MountedVehicle || (MountedVehicle.hasRenderedInterior && !MountedVehicle.seats[SeatIndex].externalView));
        private set => cockpitView = value;
    }
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
    bool dismountQueued, completingDeferredExit;
    async void DismountAfterHierarchyChange()
    {
        if (dismountQueued || !Application.isPlaying) return;
        dismountQueued = true;
        try
        {
            // Unity forbids reparenting children during their parent's activation callbacks.
            await Awaitable.NextFrameAsync();
            if (this && IsMounted)
            {
                completingDeferredExit = true;
                Leave(true);
            }
        }
        catch (System.OperationCanceledException) { } // Play Mode or application shutdown.
        finally
        {
            if (this) { dismountQueued = false; completingDeferredExit = false; }
        }
    }
    void ReleaseSeatOnDestroy()
    {
        // Destruction needs ownership cleanup, not a physical dismount of a dying hierarchy.
        if (!MountedVehicle) return;
        var vehicle = MountedVehicle;
        if (SeatIndex >= 0 && SeatIndex < vehicle.seats.Length && vehicle.seats[SeatIndex].occupant == this)
            vehicle.seats[SeatIndex].occupant = null;
        vehicle.ReleaseReservation(this);
        MountedVehicle = null;
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
        if(!IsLocal || !soldier.inputSource || !soldier.inputSource.isActiveAndEnabled)return;
        ApplyControl(soldier.inputSource.ReadVehicleInput(this),Time.deltaTime);
    }

    // Accept the same command regardless of where it came from. No device polling here.
    public void ApplyControl(VehicleControlFrame input, float deltaTime)
    {
        aimHeld=input.active && input.lookAllowed && input.aim;
        if(!input.active)
        {
            if(IsMounted && MountedVehicle.seats[SeatIndex].role==Vehicle.SeatRole.Driver)MountedVehicle.DriverInput=default;
            return;
        }
        if (life.health.IsDead || !life.actions.enabled && !IsMounted)
        {
            Prompt = "";
            return;
        }
        if (!IsMounted)
        {
            Vehicle best = null;
            int seat = -1;
            float distance = float.PositiveInfinity;
            foreach (var v in Vehicle.Active)
            {
                if (v.gameObject.scene != gameObject.scene) continue;
                int available = v.FirstEntrySeat(this);
                if (available < 0) continue;
                if (!v.IsInsideEntryBox(transform.position)) continue;
                float d = Vector3.Distance(transform.position, v.transform.TransformPoint(v.NavigationBounds.center));
                if (d < distance) { distance = d; best = v; seat = available; }
            }
            VehicleDoors doorControl = null;
            float doorDistance = float.PositiveInfinity;
            foreach(var v in Vehicle.Active)
            {
                if(v.gameObject.scene!=gameObject.scene || v.IsDestroyed)continue;
                var doors=v.GetComponent<VehicleDoors>();
                if(!doors)continue;
                int index=doors.ClosestDoor(transform.position);
                if(index<0)continue;
                float d=Vector3.Distance(transform.position,doors.doors[index].interaction.position);
                if(d<doorDistance){doorDistance=d;doorControl=doors;}
            }
            Prompt = best ? "HOLD [E] " + (best.seats[seat].occupant?"REPLACE AI / ":"ENTER ") + best.displayName + " / " + best.seats[seat].name : "";
            if(doorControl)Prompt += doorControl.operateTogether ? "   TAP [E] DOORS" : "   TAP [E] DOOR";
            if(input.lookAllowed)
            {
                if(input.interactPressed){exitHeld=0;interactionConsumed=false;}
                if(input.interactHeld && !interactionConsumed)
                {
                    exitHeld+=deltaTime;
                    if(exitHeld>=.35f){interactionConsumed=true;if(best)best.BoardFirstAvailable(this);}
                }
                if(input.interactReleased){if(!interactionConsumed&&doorControl)doorControl.TryToggle(transform.position);exitHeld=0;}
            }
            return;
        }
        Prompt = "HOLD [E] EXIT   [F1-F" + MountedVehicle.seats.Length + "] SEATS";
        if (!input.lookAllowed) { if(MountedVehicle.seats[SeatIndex].role==Vehicle.SeatRole.Driver)MountedVehicle.DriverInput = default; return; }
        if(input.interactPressed){exitHeld=0;interactionConsumed=false;}
        if(input.interactReleased)
        {
            if(!interactionConsumed)
            {
                var doors=MountedVehicle.GetComponent<VehicleDoors>();
                if(doors)
                {
                    if(MountedVehicle.seats[SeatIndex].role==Vehicle.SeatRole.Driver)doors.ToggleAll();
                    else doors.TryToggle(transform.position);
                }
            }
            exitHeld=0;
        }
        if(input.interactHeld)exitHeld+=deltaTime;
        if (exitHeld >= .35f) { interactionConsumed=true; Leave(false); exitHeld = 0; return; }
        int pages=(MountedVehicle.seats.Length+11)/12;
        SeatPage=(SeatPage+input.seatPageDelta%pages+pages)%pages;
        if(input.seatSelection>0 && input.seatSelection<=12)SwitchSeat(SeatPage*12+input.seatSelection-1);
        if (input.toggleView) CockpitView = !CockpitView;
        if (input.toggleFreelook)
        {
            Freelook = !Freelook;
            if (Freelook && MountedVehicle.seats[SeatIndex].role != Vehicle.SeatRole.Driver) { flightYaw = yaw; flightPitch = pitch; }
            if (Freelook) { freelookYaw = yaw; freelookPitch = pitch; }
            else { yaw = freelookYaw; pitch = freelookPitch; }
        }
        var vehicle = MountedVehicle;
        var role = vehicle.seats[SeatIndex].role;
        if(input.countermeasures)vehicle.GetComponent<VehicleCountermeasures>()?.TryDeploy(this);
        bool aircraft = vehicle.IsAircraft;
        var weaponSystems = vehicle.GetComponent<VehicleWeaponSystems>();
        if (weaponSystems)
        {
            if(input.weaponSelection>0)weaponSystems.Select(this,input.weaponSelection-1);
            if(input.weaponCycle!=0)weaponSystems.Cycle(this,input.weaponCycle);
        }
        if (aircraft && role == Vehicle.SeatRole.Driver && input.toggleFlightAssist)
        {
            vehicle.FlightAssistEnabled = !vehicle.FlightAssistEnabled;
            if(vehicleCamera)
            {
                yaw = flightYaw = vehicleCamera.transform.eulerAngles.y;
                pitch = flightPitch = Mathf.DeltaAngle(0, vehicleCamera.transform.eulerAngles.x);
            }
        }
        Vector2 mouse = input.lookDelta;
        bool driverWeapon = role==Vehicle.SeatRole.Driver && (weaponSystems ? weaponSystems.Current(this)!=null : (vehicle.turret && vehicle.turret.controlRole==Vehicle.SeatRole.Driver) || (vehicle.GetComponent<VehicleLauncher>() && vehicle.GetComponent<VehicleLauncher>().controlRole==Vehicle.SeatRole.Driver));
        if ((!aircraft && role == Vehicle.SeatRole.Driver && !driverWeapon) || role == Vehicle.SeatRole.Passenger)
            yaw += Mathf.DeltaAngle(lastVehicleYaw, vehicle.transform.eulerAngles.y);
        lastVehicleYaw = vehicle.transform.eulerAngles.y;
        yaw += mouse.x;
        float lookPitchLimit = vehicle.motor is JetMotor ? 85 : 60;
        pitch = Mathf.Clamp(pitch - mouse.y, -lookPitchLimit, lookPitchLimit);
        if (role == Vehicle.SeatRole.Gunner)
        {
            var launcherMount=vehicle.GetComponent<VehicleLauncherMount>();
            var limits=vehicle.turret ? vehicle.turret.pitchLimits : launcherMount ? launcherMount.pitchLimits : new Vector2(-60,60);
            pitch = Mathf.Clamp(pitch, -limits.y, -limits.x);
        }
        if (weaponSystems)
        {
            Quaternion weaponView = aircraft && role==Vehicle.SeatRole.Driver && !vehicle.FlightAssistEnabled && !Freelook
                ? vehicle.Body.rotation : Quaternion.Euler(pitch,yaw,0);
            if(!Freelook) weaponSystems.AimAt(this,new Ray(CameraPosition(vehicle,weaponView,role==Vehicle.SeatRole.Gunner,aircraft,aircraft&&role==Vehicle.SeatRole.Driver),weaponView*Vector3.forward));
            if(input.fire)weaponSystems.TryFire(this);
        }
        if (role == Vehicle.SeatRole.Driver)
        {
            if (!aircraft) { flightYaw = vehicle.transform.eulerAngles.y; flightPitch = CockpitView ? 0 : 14; }
            else if (!Freelook)
            {
                flightYaw = yaw; flightPitch = pitch;
                float maxAimPitch = vehicle.motor is JetMotor ? 85f : ((HelicopterMotor)vehicle.motor).maxAimPitch;
                if (vehicle.FlightAssistEnabled)
                {
                    Quaternion view = Quaternion.Euler(pitch,yaw,0);
                    Vector3 origin = CameraPosition(vehicle,view,false,true,true);
                    var aimLauncher = vehicle.GetComponent<VehicleLauncher>();
                    // Jets continuously steer toward screen center; only helicopters need weapon parallax correction.
                    Vector3 direction = !(vehicle.motor is JetMotor) && aimLauncher
                        ? aimLauncher.DirectionToAimPoint(new Ray(origin,view*Vector3.forward),180) : view*Vector3.forward;
                    flightYaw = Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
                    flightPitch = -Mathf.Asin(Mathf.Clamp(direction.y,-1,1))*Mathf.Rad2Deg;
                }
                flightPitch = Mathf.Clamp(flightPitch,-maxAimPitch,maxAimPitch);
            }
            vehicle.DriverInput = new VehicleInput {
                move = Vector2.ClampMagnitude(new Vector2(input.move.x, input.move.y),1),
                lift = (input.brake?1:0) - (input.lower?1:0),
                heading = flightYaw, movementHeading = Freelook ? freelookYaw : yaw, viewPitch = flightPitch, directionalFlight = aircraft,
                aim = aircraft && !Freelook && aimHeld,
                pitchRate = Freelook ? 0 : Mathf.Clamp(-mouse.y * .35f,-1,1), yaw = Freelook ? 0 : Mathf.Clamp(mouse.x * .35f,-1,1),
                handbrake = !aircraft && input.handbrake, boost = !aircraft && input.boost
            };
            if (vehicle.motor is JetMotor)
            {
                var jetInput = vehicle.DriverInput;
                jetInput.move = new Vector2(Freelook ? 0 : Mathf.Clamp(mouse.x, -1, 1), input.move.y);
                jetInput.pitchRate = Freelook ? 0 : Mathf.Clamp(-mouse.y, -1, 1);
                jetInput.yaw = input.move.x;
                jetInput.boost = input.boost;
                jetInput.brake = input.brake;
                vehicle.DriverInput = jetInput;
            }
            if (!aircraft && input.horn) hornUntil = Time.time + .35f;
            if (!weaponSystems && aircraft && input.fire && vehicle.TryGetComponent<VehicleLauncher>(out var launcher)) launcher.TryFire(this);
            if (!weaponSystems && !aircraft && !Freelook)
            {
                Quaternion view = Quaternion.Euler(pitch,yaw,0);
                Ray aimRay = new Ray(CameraPosition(vehicle,view,false,false,false),view*Vector3.forward);
                if(vehicle.turret && vehicle.turret.controlRole==Vehicle.SeatRole.Driver)
                {
                    vehicle.turret.AimAt(aimRay);
                    if(input.fire) vehicle.turret.TryFire(this);
                }
                if(vehicle.TryGetComponent<VehicleLauncherMount>(out var mount))
                {
                    mount.AimAt(aimRay);
                    if(input.fire && vehicle.TryGetComponent<VehicleLauncher>(out var groundLauncher)) groundLauncher.TryFire(this);
                }
            }
        }
        if (!weaponSystems && role == Vehicle.SeatRole.Gunner && vehicle.turret)
                {
            if (!Freelook)
            {
                Quaternion view = Quaternion.Euler(pitch,yaw,0);
                vehicle.turret.AimAt(new Ray(CameraPosition(vehicle,view,true,aircraft,false),view*Vector3.forward));
            }
            if(input.fire) vehicle.turret.TryFire(this);
        }
        if (vehicleCamera) vehicleCamera.fieldOfView = OpticalADSActive ? vehicle.GetComponent<VehicleAttackSight>().OpticalFieldOfView : aimHeld && (aircraft || role==Vehicle.SeatRole.Gunner || driverWeapon) ? 50 : 70;
    }

    public void Mount(Vehicle vehicle, int seat)
    {
        EntryPoint = vehicle.NearestEntryPoint(transform.position);
        EntryLocalPosition = vehicle.transform.InverseTransformPoint(transform.position);
        EntryLocalRotation = Quaternion.Inverse(vehicle.transform.rotation) * transform.rotation;
        MountedVehicle = vehicle;
        SeatIndex = seat;
        SeatPage = seat / 12;
        originalParent = transform.parent;
        life.actions.CancelActions();
        life.movement.ResetMovement();
        life.controller.enabled=false;
        behaviours.Clear();
        renderers.Clear(); forcedVisibility.Clear();
        shadowModes.Clear();
        colliders.Clear();
        foreach (var b in new Behaviour[] { soldier, life.actions, life.movement, life.look, life.loadout, life.view, GetComponent<SoldierWeaponPresentation>(), GetComponent<SoldierFirstPersonCamera>(), GetComponent<SoldierScopeView>(), GetComponent<SoldierFirstPersonBody>() })
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
        mountedBodyCollisions.Clear();
        foreach(var body in GetComponentsInChildren<Rigidbody>(true))
        {
            mountedBodyCollisions[body]=body.detectCollisions;
            body.detectCollisions=false;
        }
        var agent = GetComponent<NavMeshAgent>();
        if (agent)
            SaveDisable(agent);
        transform.SetParent(vehicle.seats[seat].anchor, false);
        transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        seatPose = life.actions.animator.GetComponent<VehicleSeatPose>();
        if (!seatPose) seatPose = life.actions.animator.gameObject.AddComponent<VehicleSeatPose>();
        var seated = vehicle.seats[seat];
        GetComponent<VehicleCrewAppearance>()?.Apply(vehicle, seated);
        if(vehicle.hasRenderedInterior && seated.showCharacter) seatPose.Begin(life.actions.animator, seated, seated.leftGrip, seated.rightGrip);
        var humanoidBones = new HashSet<Transform>();
        foreach(HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
            if(bone!=HumanBodyBones.LastBone){var t=life.actions.animator.GetBoneTransform(bone);if(t)humanoidBones.Add(t);}
        seatedBody = System.Array.FindAll(life.view.thirdPersonRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true),
            skin => renderers.TryGetValue(skin,out var wasVisible) && wasVisible
                && System.Array.Exists(skin.bones,bone=>humanoidBones.Contains(bone)));
        firstPersonBody=GetComponent<SoldierFirstPersonBody>();
        seatedVisible=null;
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
        lastVehicleYaw = vehicle.transform.eulerAngles.y;
        yaw = flightYaw = seated.role == Vehicle.SeatRole.Passenger ? seated.anchor.eulerAngles.y : lastVehicleYaw;
        pitch = flightPitch = vehicle.IsAircraft ? 0 : 14; CockpitView = Freelook = false; exitHeld = 0;
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
        if (!gameObject.activeInHierarchy && !completingDeferredExit)
        {
            DismountAfterHierarchyChange();
            return false;
        }
        var vehicle = MountedVehicle;
        var seat = vehicle.seats[SeatIndex];
        if (!force && !IsLocal && !vehicle.CanExit)
            return false;
        Vector3 position;
        bool inside = !force && IsLocal && seat.disembarkInside;
        if(inside)position=seat.exit.position;
        else if (!FindExit(vehicle, seat, out position))
        {
            if (!force && !IsLocal) return false;
            position = seat.exit.position;
            if (IsLocal) position = ExteriorExit(vehicle, seat);
        }
        Vector3 departureVelocity=vehicle.Body.GetPointVelocity(position);
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
        if (seatPose) seatPose.End();
        GetComponent<VehicleCrewAppearance>()?.Restore();
        MountedVehicle = null;
        transform.SetParent(originalParent, true);
        transform.SetPositionAndRotation(position, Quaternion.Euler(0, vehicle.transform.eulerAngles.y, 0));
        foreach(var pair in mountedBodyCollisions)if(pair.Key)pair.Key.detectCollisions=pair.Value;
        mountedBodyCollisions.Clear();
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
        life.controller.enabled=!life.health.IsDead;
        if(life.controller.isActiveAndEnabled && !life.controller.body.isKinematic)
            life.controller.body.linearVelocity=departureVelocity;
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
                if (candidate.exit && Mathf.Sign(vehicle.transform.InverseTransformPoint(candidate.exit.position).x)==Mathf.Sign(side)) { seat=candidate;break; }
        }
        // Preserve authored exits when clear, then search outside the full carved footprint.
        for (int i = 0; i < 4; i++)
        {
            Vector3 proposed = seat.exit.position + vehicle.transform.right * (i % 2 == 0 ? 1 : -1) * (i / 2);
            if (TryGroundExit(vehicle, proposed, out result)) return true;
        }
        var bounds = vehicle.NavigationBounds;
        var local = vehicle.transform.InverseTransformPoint(seat.exit.position);
        var navigation = GetComponent<NavMeshAgent>();
        float clearance = Mathf.Max(life.controller.radius, navigation ? navigation.radius : 0f) + .4f;
        var candidates = new List<Vector3>();
        for (int edge = 0; edge < 4; edge++)
        for (int sample = -1; sample <= 8; sample++)
        {
            bool side = edge < 2;
            float along = sample < 0 ? Mathf.Clamp(side ? local.z : local.x, side ? bounds.min.z : bounds.min.x, side ? bounds.max.z : bounds.max.x)
                : Mathf.Lerp(side ? bounds.min.z : bounds.min.x, side ? bounds.max.z : bounds.max.x, sample / 8f);
            Vector3 point = side
                ? new Vector3(edge == 0 ? bounds.min.x - clearance : bounds.max.x + clearance, local.y, along)
                : new Vector3(along, local.y, edge == 2 ? bounds.min.z - clearance : bounds.max.z + clearance);
            candidates.Add(vehicle.transform.TransformPoint(point));
        }
        candidates.Sort((a,b) => (a-seat.exit.position).sqrMagnitude.CompareTo((b-seat.exit.position).sqrMagnitude));
        foreach (var proposed in candidates)
            if (TryGroundExit(vehicle, proposed, out result)) return true;
        return false;
    }
    bool TryGroundExit(Vehicle vehicle, Vector3 proposed, out Vector3 result)
    {
        result = default;
        RaycastHit ground = default;
        bool found = false;
        float rise = Mathf.Max(2f, vehicle.NavigationBounds.size.y);
        foreach (var hit in Physics.RaycastAll(proposed + Vector3.up * rise, Vector3.down, rise + 8f,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(vehicle.transform) && !hit.transform.IsChildOf(transform)
                && !SoldierPhysicsBody.IsMotorCollider(hit.collider) && (!found || hit.distance < ground.distance))
            { ground = hit; found = true; }
        if (!found) return false;
        proposed = ground.point + Vector3.up * .08f;
        var navigation = GetComponent<NavMeshAgent>();
        if (!IsLocal && navigation)
        {
            if (!NavMesh.SamplePosition(proposed, out var mesh, .75f,
                    new NavMeshQueryFilter { agentTypeID = navigation.agentTypeID, areaMask = navigation.areaMask })
                || Mathf.Abs(mesh.position.y - ground.point.y) > .2f
                || Vector3.ProjectOnPlane(mesh.position - proposed, Vector3.up).magnitude > .2f) return false;
            proposed = new Vector3(proposed.x, mesh.position.y + .08f, proposed.z);
        }
        var cc = life.controller;
        Vector3 center = proposed + cc.center;
        float half = Mathf.Max(0, cc.height * .5f - cc.radius);
        foreach (var c in Physics.OverlapCapsule(center + Vector3.up * half, center - Vector3.up * half,
                     cc.radius * .95f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!c.transform.IsChildOf(transform) && c != ground.collider) return false;
        result = proposed;
        return true;
    }
    Vector3 ExteriorExit(Vehicle vehicle, Vehicle.Seat seat)
    {
        // Airborne/manual exits must never depend on finding ground or a NavMesh.
        var bounds = vehicle.NavigationBounds;
        var local = vehicle.transform.InverseTransformPoint(seat.exit.position);
        float side = Mathf.Sign(local.x - bounds.center.x);
        if (side == 0) side = 1;
        local.x = bounds.center.x + side * (bounds.extents.x + life.controller.radius + .6f);
        local.y = Mathf.Clamp(local.y, bounds.min.y + .2f, bounds.max.y);
        local.z = Mathf.Clamp(local.z, bounds.min.z, bounds.max.z);
        return vehicle.transform.TransformPoint(local);
    }
    public bool SwitchSeat(int index)
    {
        if (!IsMounted || index<0 || index>=MountedVehicle.seats.Length || index==SeatIndex) return false;
        var vehicle=MountedVehicle; var seat=vehicle.seats[index];
        if (!seat.anchor || !seat.exit) return false;
        if (vehicle.IsDestroyed || life.health.IsDead) return false;
        var previousSeat=vehicle.seats[SeatIndex];
        var displaced=seat.occupant;
        if (displaced)
        {
            // Players may trade seats with an AI driver/pilot, never another player.
            if (!IsHuman || seat.role!=Vehicle.SeatRole.Driver
                || !displaced.IsAI || displaced.life.health.IsDead
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
        SeatPage=index/12;
        var vehicle=MountedVehicle;
        var seat=vehicle.seats[index];
        seatPose.End();
        GetComponent<VehicleCrewAppearance>()?.Apply(vehicle, seat);
        transform.SetParent(seat.anchor,false);transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
        if(vehicle.hasRenderedInterior && seat.showCharacter) seatPose.Begin(life.actions.animator, seat, seat.leftGrip, seat.rightGrip);
        else life.actions.animator.enabled=false;
        UpdateSeatedVisibility();
        if(IsLocal)ResetVehicleCameraShake();
        flightYaw=yaw=seat.role==Vehicle.SeatRole.Passenger?seat.anchor.eulerAngles.y:vehicle.transform.eulerAngles.y;flightPitch=pitch=0;Freelook=false;
        lastVehicleYaw=vehicle.transform.eulerAngles.y;
    }
    void UpdateSeatedVisibility()
    {
        var seat=MountedVehicle.seats[SeatIndex];
        bool visible=MountedVehicle.hasRenderedInterior && seat.showCharacter
            && !(IsLocal && CockpitView && seat.weaponMountedView);
        // Only the viewed interior needs the extra headless skin and camera callbacks.
        bool bodyView=visible && !seat.externalView && !seat.weaponMountedView
            && (IsLocal ? CockpitView : life.view.firstPerson && life.view.NeedsCameraAnimation);
        if(firstPersonBody && firstPersonBody.enabled!=bodyView)firstPersonBody.enabled=bodyView;
        if(seatedVisible==visible)return;
        seatedVisible=visible;
        foreach(var skin in seatedBody) if(skin){skin.enabled=visible;skin.forceRenderingOff=!visible;skin.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;}
    }
    public bool OpticalADSActive { get { var sight=MountedVehicle?MountedVehicle.GetComponent<VehicleAttackSight>():null; return IsMounted && IsLocal && sight && sight.IsDocumentedTankSight && sight.DisplaysForSeat(SeatIndex) && aimHeld && !Freelook; } }
    void LateUpdate()
    {
        if(IsMounted) UpdateSeatedVisibility();
        if (!IsMounted || !IsLocal)
            return;
        var v = MountedVehicle;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
        bool gunner = v.seats[SeatIndex].role == Vehicle.SeatRole.Gunner;
        bool aircraft=v.IsAircraft;
        bool pilot = aircraft && v.seats[SeatIndex].role == Vehicle.SeatRole.Driver;
        if (pilot && !Freelook && !v.FlightAssistEnabled)
            rotation = CockpitView ? v.Body.rotation : Quaternion.LookRotation(v.transform.forward,
                !v.FlightAssistEnabled || Mathf.Abs(Vector3.Dot(v.transform.forward,Vector3.up))>.98f ? v.transform.up : Vector3.up);
        Vector3 desired = CameraPosition(v,rotation,gunner,aircraft,pilot);
        var shake=vehicleCameraShake.transform;
        vehicleCamera.transform.SetPositionAndRotation(desired+rotation*shake.localPosition,rotation*shake.localRotation);
    }
    Vector3 CameraPosition(Vehicle v, Quaternion rotation, bool gunner, bool aircraft, bool pilot, bool? cockpitOverride = null)
    {
        // ADS changes only the optical origin and magnification; mouse look remains the desired direction.
        // Both the aim ray and rendered view use this origin, while the barrel marker shows traversal lag.
        if(OpticalADSActive && cockpitOverride != false)return v.GetComponent<VehicleAttackSight>().OpticalPosition;
        bool cockpit = v.hasRenderedInterior && !v.seats[SeatIndex].externalView && (cockpitOverride ?? CockpitView);
        var station=v.seats[SeatIndex];
        Vector3 target = gunner ? v.turret && v.turret.cameraPoint ? v.turret.cameraPoint.position
            : station.eye ? station.eye.position : v.cameraTarget.position : v.cameraTarget.position;
        if (pilot && v.FlightAssistEnabled && !cockpit)
            target = v.transform.position + Vector3.up * v.cameraTarget.localPosition.y;
        Vector3 desired;
        if (cockpit) desired = gunner && v.seats[SeatIndex].weaponMountedView ? target
            : v.seats[SeatIndex].eye ? v.seats[SeatIndex].eye.position : gunner ? target : v.seats[SeatIndex].anchor.position + v.transform.up * (aircraft ? 1.15f : 1f);
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
        cockpit &= v.hasRenderedInterior;
        cockpit &= !v.seats[SeatIndex].externalView;
        bool gunner = v.seats[SeatIndex].role == Vehicle.SeatRole.Gunner;
        bool aircraft = v.IsAircraft;
        bool pilot = aircraft && v.seats[SeatIndex].role == Vehicle.SeatRole.Driver;
        // Local occupants already own the live view; AI view comes from its control intent.
        rotation = IsLocal ? Quaternion.Euler(pitch, yaw, 0)
            : gunner ? Quaternion.LookRotation(v.turret ? v.turret.RequestedAimDirection : v.GetComponent<VehicleLauncherMount>() ? v.GetComponent<VehicleLauncherMount>().muzzle.forward : v.transform.forward, Vector3.up)
            : v.seats[SeatIndex].role == Vehicle.SeatRole.Passenger && cockpit ? v.seats[SeatIndex].anchor.rotation
            : aircraft ? Quaternion.Euler(v.DriverInput.viewPitch, v.DriverInput.heading, 0)
            : Quaternion.Euler(cockpit ? 0 : 14, v.transform.eulerAngles.y, 0);
        if (pilot && !v.FlightAssistEnabled && (!IsLocal || !Freelook))
            rotation = cockpit ? v.Body.rotation : Quaternion.LookRotation(v.transform.forward, v.transform.up);
        position = CameraPosition(v, rotation, gunner, aircraft, pilot, cockpit);
    }

    public void PlayVehicleCameraShake(AnimationClip clip = null)
    {
        if (!vehicleCameraShake) return;
        var state = vehicleCameraShake["FireShake"];
        if (clip && (state == null || state.clip != clip))
        {
            vehicleCameraShake.AddClip(clip, "FireShake");
            state = vehicleCameraShake["FireShake"];
        }
        if (state == null) return;
        state.time=0;
        vehicleCameraShake.Play("FireShake",PlayMode.StopAll);
    }
    void ResetVehicleCameraShake()
    {
        vehicleCameraShake.Stop();
        vehicleCameraShake.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
    }
    void OnDestroy()
    {
        ReleaseSeatOnDestroy();
        if (vehicleCamera)
            Destroy(vehicleCamera.gameObject);
    }
}
