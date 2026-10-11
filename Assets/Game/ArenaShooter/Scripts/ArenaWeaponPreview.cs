using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// A presentation-only model. No player, weapon firing, or scope camera is instantiated.
public class ArenaWeaponPreview : MonoBehaviour, IDragHandler
{
    public UnityEngine.UI.RawImage image;
    [Serializable] public class WeaponSource { public SoldierWeaponDefinition weapon; public SoldierWeaponView view; }
    public WeaponSource[] sources;
    public int previewLayer = 31;
    GameObject stage, model;
    Camera previewCamera;
    RenderTexture texture;
    bool dirty;
    float orbit;

    void EnsureStage()
    {
        if (stage) return;
        stage = new GameObject("Class Weapon Preview Stage");
        stage.transform.position = new Vector3(0, -10000, 0);
        previewCamera = new GameObject("Preview Camera").AddComponent<Camera>();
        previewCamera.transform.SetParent(stage.transform, false);
        previewCamera.enabled = false;
        previewCamera.cullingMask = 1 << previewLayer;
        previewCamera.orthographic = true;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(.025f,.031f,.037f,1);
        previewCamera.nearClipPlane = .01f;
        previewCamera.farClipPlane = 20;
        texture = new RenderTexture(1536,768,24) { name = "Class Weapon Preview" };
        texture.Create(); image.texture = texture;
        foreach (float yaw in new[]{-60f,130f})
        {
            var light = new GameObject("Preview Light").AddComponent<Light>();
            light.transform.SetParent(stage.transform, false);
            light.type = LightType.Directional; light.intensity = yaw < 0 ? 2f : 1f;
            light.cullingMask = 1 << previewLayer;
            light.transform.rotation = Quaternion.Euler(35,yaw,0);
        }
    }

    public void Show(ArenaWeaponCatalog.Entry entry, WeaponSightDefinition optic, WeaponAttachmentDefinition[] attachments)
    {
        EnsureStage();
        if(model){model.SetActive(false);Destroy(model);}
        model = Instantiate(entry.basePrefab, stage.transform, false);
        model.SetActive(true); model.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
        foreach(var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        foreach(var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        foreach(var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        var sockets = model.GetComponentsInChildren<Transform>(true);
        var opticMount = sockets.FirstOrDefault(t=>t.name=="OpticMount");
        var sourceView = sources.FirstOrDefault(s=>s.weapon==entry.weapon)?.view;
        var source = sourceView ? sourceView.GetComponent<SoldierWeaponAttachments>() : null;
        foreach(var attachment in attachments)
        {
            string socketName = attachment.mount switch {
                WeaponAttachmentDefinition.MountKind.Muzzle=>"MuzzleMount",
                WeaponAttachmentDefinition.MountKind.Rail=>"RailMount",
                WeaponAttachmentDefinition.MountKind.RailLeft=>"LeftRailMount",
                WeaponAttachmentDefinition.MountKind.Grip=>"GripMount",
                WeaponAttachmentDefinition.MountKind.RearIron=>"RearIronMount",
                WeaponAttachmentDefinition.MountKind.FrontIron=>"FrontIronMount",
                WeaponAttachmentDefinition.MountKind.SightRiser=>"OpticMount",
                WeaponAttachmentDefinition.MountKind.Handguard=>"HandguardMount", _=>null};
            var socket=sockets.FirstOrDefault(t=>t.name==socketName);
            if(!socket)continue;
            var part=Instantiate(attachment.prefab,socket,false);
            part.transform.SetLocalPositionAndRotation(attachment.positionOffset,Quaternion.Euler(attachment.rotationOffset));
            part.transform.localScale=attachment.scale;
            if(socket.TryGetComponent<WeaponAttachmentSocket>(out var placement))placement.Place(attachment,part.transform);
        }
        var chosenOptic=optic?optic:(sourceView ? sourceView.GetComponent<SoldierWeaponSights>()?.defaultSight : null);
        bool hasOptic=chosenOptic && chosenOptic.prefab && opticMount;
        if(hasOptic)
        {
            var part=Instantiate(chosenOptic.prefab,opticMount,false);
            part.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
            var support=opticMount.TryGetComponent<WeaponAttachmentSocket>(out var socket)?socket.SupportFor(chosenOptic):default;
            var riser=attachments.FirstOrDefault(a=>a.mount==WeaponAttachmentDefinition.MountKind.SightRiser);
            float lift=Mathf.Max(riser?riser.sightLift:0,support.riser?support.riser.sightLift:0);
            part.transform.localPosition=Vector3.up*lift;
            if(support.riser)part.transform.localPosition-=new Vector3(support.contactPoint.x,support.contactPoint.y,0);
            if(support.riser && !riser)
            {
                var rail=Instantiate(support.riser.prefab,part.transform,false);
                rail.transform.SetLocalPositionAndRotation(support.riser.positionOffset+Vector3.forward*support.contactPoint.z-part.transform.localPosition,Quaternion.Euler(support.riser.rotationOffset));
                rail.transform.localScale=Vector3.Scale(support.riser.scale,support.supportScale);
            }
        }
        if(source)
            foreach(var mount in source.mounts)
            {
                bool replaced=attachments.Any(a=>a.mount==mount.kind);
                bool iron=mount.kind==WeaponAttachmentDefinition.MountKind.RearIron || mount.kind==WeaponAttachmentDefinition.MountKind.FrontIron;
                if(!replaced && !(hasOptic&&iron))continue;
                foreach(var renderer in mount.builtInThirdPerson)
                    foreach(var candidate in model.GetComponentsInChildren<Renderer>(true).Where(r=>r.name==renderer.name))candidate.enabled=false;
            }
        if(hasOptic)
            foreach(var iron in model.GetComponentsInChildren<WeaponSightAttachment>(true))
                if(iron.transform.parent!=opticMount)iron.gameObject.SetActive(false);
        foreach(var t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=previewLayer;
        foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))renderer.shadowCastingMode=ShadowCastingMode.Off;
        Frame(); dirty=true;
    }
    void Frame()
    {
        var renderers=model.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();
        if(renderers.Length==0)return;
        var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
        previewCamera.aspect=2;
        previewCamera.orthographicSize=Mathf.Max(bounds.size.y*.72f,bounds.size.z*.30f);
        previewCamera.transform.position=bounds.center+Quaternion.Euler(0,orbit,0)*Vector3.left*4;
        previewCamera.transform.LookAt(bounds.center,Vector3.up);
    }
    public void OnDrag(PointerEventData eventData){if(!model)return;orbit-=eventData.delta.x*.3f;Frame();dirty=true;}
    void LateUpdate(){if(!dirty||!stage)return;RenderPipeline.SubmitRenderRequest(previewCamera,new UniversalRenderPipeline.SingleCameraRequest{destination=texture});dirty=false;}
    void OnDisable(){Release();}
    void OnEnable(){if(stage){stage.SetActive(true);dirty=true;}}
    void OnDestroy(){Release();}
    void Release(){if(stage){stage.SetActive(false);Destroy(stage);stage=null;model=null;}if(texture){image.texture=null;texture.Release();Destroy(texture);texture=null;}}
}
