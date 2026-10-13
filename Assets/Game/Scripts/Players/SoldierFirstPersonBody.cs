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
    Transform collapsedHead, animatedHead;
    bool headlessActive, initialized;

    void Awake()
    {
        properties = new MaterialPropertyBlock();
        occupant = GetComponent<VehicleOccupant>();
        if (soldier.inputSource is SoldierLocalInput) BuildParts();
    }

    void BuildParts()
    {
        initialized = true;
        animatedHead = soldier.actions.animator.GetBoneTransform(HumanBodyBones.Head);
        var headObject = new GameObject("First-person hidden head");
        collapsedHead = headObject.transform;
        collapsedHead.SetParent(transform, false);
        collapsedHead.localScale = Vector3.zero;
        foreach (var renderer in view.thirdPersonRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (Array.Exists(renderer.bones, bone => bone && (bone == animatedHead || bone.IsChildOf(animatedHead))))
                {
                    var child = new GameObject("First-person body: " + renderer.name);
                    child.transform.SetParent(transform, false);
                    child.layer = renderer.gameObject.layer;
                    var firstPerson = child.AddComponent<SkinnedMeshRenderer>();
                    firstPerson.sharedMesh = renderer.sharedMesh;
                    firstPerson.sharedMaterials = renderer.sharedMaterials;
                    firstPerson.bones = Array.ConvertAll(renderer.bones,
                        bone => bone && (bone == animatedHead || bone.IsChildOf(animatedHead)) ? collapsedHead : bone);
                    firstPerson.rootBone = renderer.rootBone;
                    firstPerson.localBounds = renderer.localBounds;
                    firstPerson.updateWhenOffscreen = true;
                    firstPerson.quality = renderer.quality;
                    firstPerson.lightProbeUsage = renderer.lightProbeUsage;
                    firstPerson.reflectionProbeUsage = renderer.reflectionProbeUsage;
                    firstPerson.probeAnchor = renderer.probeAnchor;
                    firstPerson.renderingLayerMask = renderer.renderingLayerMask;
                    firstPerson.shadowCastingMode = ShadowCastingMode.Off;
                    firstPerson.forceRenderingOff = true;
                    parts.Add(new BodyPart { fullBody = renderer, firstPerson = firstPerson });
                }
    }

    void LateUpdate()
    {
        properties ??= new MaterialPropertyBlock();
        if (!initialized && soldier.inputSource is SoldierLocalInput) BuildParts();
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
            // Keep the FPS skinning layout stable before Unity updates its vertex buffers.
            bool firstPerson = occupant && occupant.IsMounted ? occupant.CockpitView : view.firstPerson;
            target.forceRenderingOff = source.forceRenderingOff || !firstPerson;
            part.previousShadows = source.shadowCastingMode;
            source.GetPropertyBlock(properties);
            target.SetPropertyBlock(properties);
        }
    }

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeforeCamera;
        RenderPipelineManager.endCameraRendering += AfterCamera;
        Camera.onPreCull += BeforeBuiltInCamera;
        Camera.onPostRender += AfterBuiltInCamera;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        RenderPipelineManager.endCameraRendering -= AfterCamera;
        Camera.onPreCull -= BeforeBuiltInCamera;
        Camera.onPostRender -= AfterBuiltInCamera;
        SetHeadless(false);
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
        if (!(soldier.inputSource is SoldierLocalInput) && renderStates.Count == 0) return;
        renderStates.Push((camera, headlessActive));
        bool firstPerson = occupant && occupant.IsMounted
            ? camera == occupant.ActiveVehicleCamera && occupant.CockpitView
            : camera == view.playerCamera && view.firstPerson;
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
