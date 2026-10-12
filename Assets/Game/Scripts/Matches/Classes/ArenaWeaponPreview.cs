using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.ResourceManagement.AsyncOperations;

// One cached preview stage, one selected render-only weapon, and its changed attachment slots.
public class ArenaWeaponPreview : MonoBehaviour, IDragHandler
{
    public UnityEngine.UI.RawImage image;
    public int previewLayer=31;
    public string LoadError {get;private set;}
    public bool IsLoading {get;private set;}
    public int ModelRebuilds {get;private set;}
    public int LoadedPartCount=>parts.Count;
    public string CurrentWeaponId=>weaponId;
    GameObject stage,model;
    Camera previewCamera;
    RenderTexture texture;
    Light keyLight,fillLight,rimLight;
    bool dirty;
    float orbit=20f,viewingDistance;
    const float PreviewElevation=12f;
    int generation;
    string weaponId;
    AsyncOperationHandle<GameObject> weaponHandle;
    readonly Dictionary<string,PartInstance> parts=new Dictionary<string,PartInstance>();
    readonly Dictionary<Renderer,bool> builtInVisibility=new Dictionary<Renderer,bool>();
    class PartInstance {public string id;public GameObject model;public AsyncOperationHandle<GameObject> handle;}
    class DesiredPart {public ArenaClassMenuCatalog.Part part;public Transform parent;public Vector3 position,scale;public Quaternion rotation;}

    static async Task<GameObject> Result(AsyncOperationHandle<GameObject> handle)
    {
        if(!handle.IsDone){var completion=new TaskCompletionSource<bool>();handle.Completed+=_=>completion.TrySetResult(true);await completion.Task;}
        if(handle.Status!=AsyncOperationStatus.Succeeded)throw handle.OperationException??new InvalidOperationException("Preview asset did not load");
        return handle.Result;
    }
    public async void Show(ArenaClassMenuCatalog catalog,ArenaClassMenuCatalog.Weapon weapon,string opticId,string[] attachmentIds)
    {
        int request=++generation;IsLoading=true;LoadError=null;
        try
        {
            EnsureStage();stage.SetActive(true);
            if(model&&weaponId==weapon.id)model.SetActive(true);
            bool changed=weaponId!=weapon.id;
            if(changed)
            {
                if(model)model.SetActive(false);
                var handle=Addressables.LoadAssetAsync<GameObject>(weapon.previewAddress);
                GameObject prefab;
                try{prefab=await Result(handle);}catch{Addressables.Release(handle);throw;}
                if(!this||request!=generation){Addressables.Release(handle);return;}
                ClearModel();weaponHandle=handle;weaponId=weapon.id;
                model=Instantiate(prefab,stage.transform,false);model.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.Euler(-3,0,0));ModelRebuilds++;
                Prepare(model);
                foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))builtInVisibility[renderer]=renderer.enabled;
            }
            var desired=BuildParts(catalog,weapon,opticId,attachmentIds);
            foreach(var key in parts.Keys.Where(key=>!desired.ContainsKey(key)).ToArray())RemovePart(key);
            foreach(var pair in desired)
            {
                if(parts.TryGetValue(pair.Key,out var current)&&current.id==pair.Value.part.id)
                {Place(current.model,pair.Value);continue;}
                var handle=Addressables.LoadAssetAsync<GameObject>(pair.Value.part.previewAddress);
                GameObject prefab;
                try{prefab=await Result(handle);}catch{Addressables.Release(handle);throw;}
                if(!this||request!=generation){Addressables.Release(handle);return;}
                RemovePart(pair.Key);
                var instance=Instantiate(prefab,pair.Value.parent,false);Place(instance,pair.Value);Prepare(instance);
                parts[pair.Key]=new PartInstance{id=pair.Value.part.id,model=instance,handle=handle};
            }
            if(request!=generation)return;
            foreach(var renderer in builtInVisibility.Keys)if(renderer)renderer.enabled=builtInVisibility[renderer];
            bool hasOptic=desired.ContainsKey("optic");
            foreach(var builtIn in weapon.builtIns)
                if(attachmentIds.Any(id=>catalog.FindPart(id)?.mount==builtIn.mount)||(hasOptic&&(builtIn.mount==WeaponAttachmentDefinition.MountKind.FrontIron||builtIn.mount==WeaponAttachmentDefinition.MountKind.RearIron)))
                    foreach(var renderer in builtInVisibility.Keys.Where(r=>r&&builtIn.rendererNames.Contains(r.name)))renderer.enabled=false;
            Frame(changed);dirty=true;
        }
        catch(Exception error){if(this&&request==generation){LoadError="PREVIEW UNAVAILABLE";Debug.LogException(error,this);}}
        finally{if(this&&request==generation)IsLoading=false;}
    }
    Dictionary<string,DesiredPart> BuildParts(ArenaClassMenuCatalog catalog,ArenaClassMenuCatalog.Weapon weapon,string opticId,string[] ids)
    {
        var desired=new Dictionary<string,DesiredPart>();
        foreach(var id in ids)
        {
            var part=catalog.FindPart(id);if(part==null||string.IsNullOrEmpty(part.previewAddress))continue;
            var socket=weapon.sockets.FirstOrDefault(s=>s.mount==part.mount);if(socket==null)continue;
            var parent=model.transform.Find(socket.path);if(!parent)continue;
            var placement=socket.placements.FirstOrDefault(p=>p.part==part.id);
            desired[part.mount.ToString()]=new DesiredPart{part=part,parent=parent,position=placement!=null?placement.position:part.position,rotation=placement!=null?placement.rotation:Quaternion.Euler(part.rotation),scale=placement!=null?placement.scale:part.scale};
        }
        var optic=catalog.FindPart(opticId);var opticSocket=weapon.sockets.FirstOrDefault(s=>s.mount==WeaponAttachmentDefinition.MountKind.SightRiser);
        if(optic!=null&&!string.IsNullOrEmpty(optic.previewAddress)&&opticSocket!=null)
        {
            var parent=model.transform.Find(opticSocket.path);if(parent)
            {
                var support=opticSocket.supports.FirstOrDefault(s=>s.sight==optic.id);
                var supplied=ids.Select(catalog.FindPart).FirstOrDefault(p=>p!=null&&p.mount==WeaponAttachmentDefinition.MountKind.SightRiser);
                var required=support!=null?catalog.FindPart(support.riser):null;
                var lift=Mathf.Max(supplied!=null?supplied.sightLift:0,required!=null?required.sightLift:0);
                var position=Vector3.up*lift;
                if(required!=null)position-=new Vector3(support.contactPoint.x,support.contactPoint.y,0);
                desired["optic"]=new DesiredPart{part=optic,parent=parent,position=position,rotation=Quaternion.identity,scale=Vector3.one};
                if(required!=null&&supplied==null)desired["support"]=new DesiredPart{part=required,parent=parent,position=required.position+Vector3.forward*support.contactPoint.z,rotation=Quaternion.Euler(required.rotation),scale=Vector3.Scale(required.scale,support.scale)};
            }
        }
        return desired;
    }
    static void Place(GameObject part,DesiredPart desired){part.transform.SetParent(desired.parent,false);part.transform.SetLocalPositionAndRotation(desired.position,desired.rotation);part.transform.localScale=desired.scale;}
    void Prepare(GameObject instance){foreach(var t in instance.GetComponentsInChildren<Transform>(true))t.gameObject.layer=previewLayer;foreach(var r in instance.GetComponentsInChildren<Renderer>(true))r.shadowCastingMode=ShadowCastingMode.Off;}
    void RemovePart(string key){if(!parts.TryGetValue(key,out var part))return;if(part.model){part.model.SetActive(false);Destroy(part.model);}if(part.handle.IsValid())Addressables.Release(part.handle);parts.Remove(key);}
    void ClearModel(){foreach(var key in parts.Keys.ToArray())RemovePart(key);builtInVisibility.Clear();if(model){model.SetActive(false);Destroy(model);model=null;}if(weaponHandle.IsValid())Addressables.Release(weaponHandle);weaponHandle=default;weaponId=null;}
    void EnsureStage()
    {
        if(stage)return;stage=new GameObject("Class Weapon Preview Stage");stage.transform.position=new Vector3(0,-10000,0);
        previewCamera=new GameObject("Preview Camera").AddComponent<Camera>();previewCamera.transform.SetParent(stage.transform,false);previewCamera.enabled=false;previewCamera.cullingMask=1<<previewLayer;previewCamera.fieldOfView=30;previewCamera.clearFlags=CameraClearFlags.SolidColor;previewCamera.backgroundColor=Color.black;previewCamera.nearClipPlane=.01f;previewCamera.farClipPlane=20;
        texture=new RenderTexture(1536,768,24){name="Class Weapon Preview"};texture.Create();image.texture=texture;
        keyLight=CreateLight("Preview Key",new Color(1,.94f,.85f),8);fillLight=CreateLight("Preview Fill",new Color(.85f,.92f,1),2.5f);rimLight=CreateLight("Preview Rim",new Color(.72f,.84f,1),10);
    }
    Light CreateLight(string name,Color color,float intensity){var light=new GameObject(name).AddComponent<Light>();light.transform.SetParent(stage.transform,false);light.type=LightType.Spot;light.color=color;light.intensity=intensity;light.spotAngle=85;light.innerSpotAngle=50;light.range=10;light.shadows=LightShadows.None;light.cullingMask=1<<previewLayer;return light;}
    void Frame(bool fit=false)
    {
        var renderers=model.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();if(renderers.Length==0)return;
        var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);previewCamera.aspect=(float)texture.width/texture.height;
        var direction=Quaternion.Euler(0,orbit,0)*Vector3.left;direction=direction*Mathf.Cos(PreviewElevation*Mathf.Deg2Rad)+Vector3.up*Mathf.Sin(PreviewElevation*Mathf.Deg2Rad);
        var rotation=Quaternion.LookRotation(-direction,Vector3.up);var inverse=Quaternion.Inverse(rotation);float vertical=Mathf.Tan(previewCamera.fieldOfView*.5f*Mathf.Deg2Rad),horizontal=vertical*previewCamera.aspect,distance=.1f;
        for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2){var corner=inverse*Vector3.Scale(bounds.extents,new Vector3(x,y,z));distance=Mathf.Max(distance,Mathf.Abs(corner.x)*1.15f/horizontal-corner.z,Mathf.Abs(corner.y)*1.15f/vertical-corner.z);}
        if(fit)viewingDistance=distance;previewCamera.transform.SetPositionAndRotation(bounds.center+direction*viewingDistance,rotation);
        PositionLight(keyLight,bounds.center,new Vector3(-1.6f,1.8f,-1.2f));PositionLight(fillLight,bounds.center,new Vector3(-1.2f,.6f,1.8f));PositionLight(rimLight,bounds.center,new Vector3(1.2f,1.4f,.8f));
    }
    static void PositionLight(Light light,Vector3 center,Vector3 offset){light.transform.position=center+offset;light.transform.LookAt(center);}
    public void OnDrag(PointerEventData data){if(!model||IsLoading)return;orbit-=data.delta.x*.3f;Frame();dirty=true;}
    void LateUpdate(){if(!dirty||!stage||!image.isActiveAndEnabled)return;RenderPipeline.SubmitRenderRequest(previewCamera,new UniversalRenderPipeline.SingleCameraRequest{destination=texture});dirty=false;}
    void OnDisable(){generation++;IsLoading=false;if(stage)stage.SetActive(false);}
    void OnEnable(){if(stage){stage.SetActive(true);dirty=true;}}
    void OnDestroy(){generation++;ClearModel();if(stage)Destroy(stage);if(texture){if(image)image.texture=null;texture.Release();Destroy(texture);}}
}
