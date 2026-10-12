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
    float orbit = 20f;
    float viewingDistance;
    const float PreviewElevation = 12f;
    Light keyLight, fillLight, rimLight;

    void EnsureStage()
    {
        if (stage) return;
        stage = new GameObject("Class Weapon Preview Stage");
        stage.transform.position = new Vector3(0, -10000, 0);
        previewCamera = new GameObject("Preview Camera").AddComponent<Camera>();
        previewCamera.transform.SetParent(stage.transform, false);
        previewCamera.enabled = false;
        previewCamera.cullingMask = 1 << previewLayer;
        previewCamera.orthographic = false;
        previewCamera.fieldOfView = 30f;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = Color.black;
        previewCamera.nearClipPlane = .01f;
        previewCamera.farClipPlane = 20;
        texture = new RenderTexture(1536,768,24) { name = "Class Weapon Preview" };
        texture.Create(); image.texture = texture;
        keyLight = CreateLight("Preview Key", new Color(1f, .94f, .85f), 8f);
        fillLight = CreateLight("Preview Fill", new Color(.85f, .92f, 1f), 2.5f);
        rimLight = CreateLight("Preview Rim", new Color(.72f, .84f, 1f), 10f);
    }

    Light CreateLight(string lightName, Color color, float intensity)
    {
        var light = new GameObject(lightName).AddComponent<Light>();
        light.transform.SetParent(stage.transform, false);
        light.type = LightType.Spot;
        light.color = color;
        light.intensity = intensity;
        light.spotAngle = 85f;
        light.innerSpotAngle = 50f;
        light.range = 10f;
        light.shadows = LightShadows.None;
        light.cullingMask = 1 << previewLayer;
        return light;
    }

    public void Show(ArenaWeaponCatalog.Entry entry, WeaponSightDefinition optic, WeaponAttachmentDefinition[] attachments)
    {
        EnsureStage();
        if(model){model.SetActive(false);Destroy(model);}
        model = Instantiate(entry.basePrefab, stage.transform, false);
        model.SetActive(true); model.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.Euler(-3f,0,0));
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
        Frame(true); dirty=true;
    }
    void Frame(bool fit = false)
    {
        var renderers=model.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();
        if(renderers.Length==0)return;
        var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
        previewCamera.aspect = (float)texture.width / texture.height;
        var direction = Quaternion.Euler(0, orbit, 0) * Vector3.left;
        direction = direction * Mathf.Cos(PreviewElevation * Mathf.Deg2Rad)
            + Vector3.up * Mathf.Sin(PreviewElevation * Mathf.Deg2Rad);
        var rotation = Quaternion.LookRotation(-direction, Vector3.up);
        var inverse = Quaternion.Inverse(rotation);
        float vertical = Mathf.Tan(previewCamera.fieldOfView * .5f * Mathf.Deg2Rad);
        float horizontal = vertical * previewCamera.aspect;
        float distance = .1f;
        // Fit every bounds corner with a margin, including long barrels and attachments.
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    var corner = inverse * Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                    distance = Mathf.Max(distance, Mathf.Abs(corner.x) * 1.15f / horizontal - corner.z,
                        Mathf.Abs(corner.y) * 1.15f / vertical - corner.z);
                }
        if (fit) viewingDistance = distance;
        previewCamera.transform.SetPositionAndRotation(bounds.center + direction * viewingDistance, rotation);
        PositionLight(keyLight, bounds.center, new Vector3(-1.6f, 1.8f, -1.2f));
        PositionLight(fillLight, bounds.center, new Vector3(-1.2f, .6f, 1.8f));
        PositionLight(rimLight, bounds.center, new Vector3(1.2f, 1.4f, .8f));
    }
    static void PositionLight(Light light, Vector3 center, Vector3 offset)
    {
        light.transform.position = center + offset;
        light.transform.LookAt(center);
    }
    public void OnDrag(PointerEventData eventData){if(!model)return;orbit-=eventData.delta.x*.3f;Frame();dirty=true;}
    void LateUpdate(){if(!dirty||!stage)return;RenderPipeline.SubmitRenderRequest(previewCamera,new UniversalRenderPipeline.SingleCameraRequest{destination=texture});dirty=false;}
    void OnDisable(){Release();}
    void OnEnable(){if(stage){stage.SetActive(true);dirty=true;}}
    void OnDestroy(){Release();}
    void Release(){if(stage){stage.SetActive(false);Destroy(stage);stage=null;model=null;}if(texture){image.texture=null;texture.Release();Destroy(texture);texture=null;}}
}
