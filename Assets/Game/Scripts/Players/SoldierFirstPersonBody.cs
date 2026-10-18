using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Uses the same skeleton, with camera-specific geometry for the owner's visible body.
[DefaultExecutionOrder(210)]
public class SoldierFirstPersonBody : MonoBehaviour
{
    public SoldierFirstPersonView view;
    public SoldierController soldier;
    class BodyPart
    {
        public SkinnedMeshRenderer fullBody, firstPerson;
        public ShadowCastingMode previousShadows;
    }
    readonly List<BodyPart> parts = new();
    readonly Stack<(Camera camera, bool headless)> renderStates = new();
    MaterialPropertyBlock properties;
    VehicleOccupant occupant;
    SoldierHealth health;
    Transform collapsedHead, animatedHead;
    bool headlessActive, initialized, subscribed, copiesActive;

    void Awake()
    {
        occupant = GetComponent<VehicleOccupant>();
        health = GetComponent<SoldierHealth>();
        if (soldier.inputSource is SoldierLocalInput) BuildParts();
    }

    void BuildParts()
    {
        if (initialized) return;
        initialized = true;
        properties = new MaterialPropertyBlock();
        animatedHead = soldier.actions.animator.GetBoneTransform(HumanBodyBones.Head);
        var headObject = new GameObject("First-person hidden head");
        collapsedHead = headObject.transform;
        collapsedHead.SetParent(transform, false);
        collapsedHead.SetPositionAndRotation(animatedHead.position, animatedHead.rotation);
        collapsedHead.localScale = Vector3.zero;
        foreach (var renderer in view.thirdPersonRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (Array.Exists(renderer.bones, bone => bone && (bone == animatedHead || bone.IsChildOf(animatedHead))))
                {
                    var child = new GameObject("First-person body: " + renderer.name);
                    child.transform.SetParent(transform, false);
                    child.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                    var rootScale = transform.lossyScale;
                    var skinScale = renderer.transform.lossyScale;
                    child.transform.localScale = new Vector3(skinScale.x / rootScale.x, skinScale.y / rootScale.y, skinScale.z / rootScale.z);
                    child.layer = renderer.gameObject.layer;
                    var firstPerson = child.AddComponent<SkinnedMeshRenderer>();
                    firstPerson.sharedMesh = renderer.sharedMesh;
                    firstPerson.sharedMaterials = renderer.sharedMaterials;
                    firstPerson.bones = Array.ConvertAll(renderer.bones,
                        bone => bone && (bone == animatedHead || bone.IsChildOf(animatedHead)) ? collapsedHead : bone);
                    firstPerson.rootBone = renderer.rootBone;
                    firstPerson.localBounds = renderer.localBounds;
                    firstPerson.updateWhenOffscreen = false;
                    firstPerson.quality = renderer.quality;
                    firstPerson.lightProbeUsage = renderer.lightProbeUsage;
                    firstPerson.reflectionProbeUsage = renderer.reflectionProbeUsage;
                    firstPerson.probeAnchor = renderer.probeAnchor;
                    firstPerson.renderingLayerMask = renderer.renderingLayerMask;
                    firstPerson.shadowCastingMode = ShadowCastingMode.Off;
                    firstPerson.forceRenderingOff = true;
                    firstPerson.enabled = false;
                    parts.Add(new BodyPart { fullBody = renderer, firstPerson = firstPerson, previousShadows = renderer.shadowCastingMode });
                }
        if (isActiveAndEnabled) Subscribe();
    }
    public void PrepareForVehicleSpectating()
    {
        if (!initialized) BuildParts();
    }

    void LateUpdate()
    {
        if (!initialized && soldier.inputSource is SoldierLocalInput) BuildParts();
        if (!initialized) return;
        bool needed = NeedsBodyCopy();
        if (!needed)
        {
            if (copiesActive)
                foreach (var part in parts) if (part.firstPerson) part.firstPerson.enabled = false;
            copiesActive = false;
            return;
        }
        copiesActive = true;
        if (collapsedHead) collapsedHead.SetPositionAndRotation(animatedHead.position, animatedHead.rotation);
        foreach (var part in parts)
        {
            var source = part.fullBody;
            var target = part.firstPerson;
            target.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            var parentScale = transform.lossyScale;
            var sourceScale = source.transform.lossyScale;
            target.transform.localScale = new Vector3(sourceScale.x / parentScale.x, sourceScale.y / parentScale.y, sourceScale.z / parentScale.z);
            target.enabled = source.enabled && source.gameObject.activeInHierarchy;
            // The copy is visible only during its owning first-person camera render.
            target.forceRenderingOff = true;
            part.previousShadows = source.shadowCastingMode;
            source.GetPropertyBlock(properties);
            target.SetPropertyBlock(properties);
        }
    }

    bool NeedsBodyCopy()
    {
        if (occupant && occupant.IsMounted)
        {
            var seat = occupant.MountedVehicle.seats[occupant.SeatIndex];
            if (!occupant.MountedVehicle.hasRenderedInterior || seat.weaponMountedView) return false;
            var vehicleCamera = occupant.ActiveVehicleCamera;
            return (soldier.inputSource is SoldierLocalInput && occupant.CockpitView
                    && vehicleCamera && vehicleCamera.isActiveAndEnabled)
                || (view.firstPerson && view.NeedsCameraAnimation);
        }
        return soldier.inputSource is SoldierLocalInput && view.firstPerson && view.NeedsCameraAnimation;
    }

    void OnEnable()
    {
        if (initialized) Subscribe();
    }

    void Subscribe()
    {
        if (subscribed) return;
        subscribed = true;
        RenderPipelineManager.beginCameraRendering += BeforeCamera;
        RenderPipelineManager.endCameraRendering += AfterCamera;
        Camera.onPreCull += BeforeBuiltInCamera;
        Camera.onPostRender += AfterBuiltInCamera;
    }

    void OnDisable()
    {
        subscribed = false;
        RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        RenderPipelineManager.endCameraRendering -= AfterCamera;
        Camera.onPreCull -= BeforeBuiltInCamera;
        Camera.onPostRender -= AfterBuiltInCamera;
        SetHeadless(false);
        foreach (var part in parts) if (part.firstPerson) part.firstPerson.enabled = false;
        copiesActive = false;
        renderStates.Clear();
    }

    void OnDestroy()
    {
        foreach (var part in parts) if (part.firstPerson) Destroy(part.firstPerson.gameObject);
        if (collapsedHead) Destroy(collapsedHead.gameObject);
    }

    void BeforeCamera(ScriptableRenderContext context, Camera camera) => BeginCamera(camera);
    void AfterCamera(ScriptableRenderContext context, Camera camera) => EndCamera(camera);
    void BeforeBuiltInCamera(Camera camera)
    {
        if (!GraphicsSettings.currentRenderPipeline) BeginCamera(camera);
    }
    void AfterBuiltInCamera(Camera camera)
    {
        if (!GraphicsSettings.currentRenderPipeline) EndCamera(camera);
    }

    void BeginCamera(Camera camera)
    {
        if (!initialized) return;
        bool owner = soldier.inputSource is SoldierLocalInput;
        bool vehiclePerspective = occupant && occupant.IsMounted && camera == view.playerCamera && view.firstPerson;
        if (health.IsDead) { SetHeadless(false); renderStates.Clear(); return; }
        if (!owner && !vehiclePerspective && renderStates.Count == 0) return;
        if (renderStates.Count == 0)
            foreach (var part in parts) if (part.fullBody) part.previousShadows = part.fullBody.shadowCastingMode;
        renderStates.Push((camera, headlessActive));
        bool firstPerson = occupant && occupant.IsMounted
            ? (camera == occupant.ActiveVehicleCamera && occupant.CockpitView)
                || (vehiclePerspective && occupant.MountedVehicle.hasRenderedInterior)
            : owner && camera == view.playerCamera && view.firstPerson;
        if (occupant && occupant.IsMounted && occupant.MountedVehicle.seats[occupant.SeatIndex].weaponMountedView) firstPerson = false;
        SetHeadless(firstPerson);
    }

    void EndCamera(Camera camera)
    {
        if (renderStates.Count == 0 || renderStates.Peek().camera != camera) return;
        SetHeadless(renderStates.Pop().headless);
    }

    void SetHeadless(bool enabled)
    {
        foreach (var part in parts)
        {
            if (!part.fullBody || !part.firstPerson) continue;
            part.fullBody.shadowCastingMode = enabled ? ShadowCastingMode.ShadowsOnly : part.previousShadows;
            part.firstPerson.forceRenderingOff = !enabled || part.fullBody.forceRenderingOff;
        }
        headlessActive = enabled;
    }
}
