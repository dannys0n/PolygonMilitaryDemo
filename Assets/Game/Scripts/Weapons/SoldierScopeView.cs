using UnityEngine;
using UnityEngine.Rendering.Universal;

// One private lens texture/camera for the active viewer. Other soldiers' sights
// remain ordinary meshes; their scopes never render another copy of the world.
[DefaultExecutionOrder(250)]
public class SoldierScopeView : MonoBehaviour
{
    public SoldierFirstPersonView view;
    public SoldierWeaponLoadout loadout;
    Camera scopeCamera;
    RenderTexture texture;
    Renderer lens;
    Material lensMaterial,originalMaterial;
    WeaponSightDefinition renderedSight;
    public Camera ScopeCamera=>scopeCamera;
    public RenderTexture ScopeTexture=>texture;
    void LateUpdate()
    {
        var sights=loadout.EquippedSights;
        var sight=sights?sights.SelectedSight:null;
        var attachment=sights?sights.FirstPersonAttachment:null;
        bool active=view.firstPerson && view.playerCamera.enabled && sight && sight.pictureInPicture
            && attachment && attachment.scopeLens && view.AimBlend>.08f;
        if(!active){ReleaseLens();if(scopeCamera)scopeCamera.enabled=false;return;}
        if(lens!=attachment.scopeLens || renderedSight!=sight)
        {
            ReleaseLens();EnsureCamera();
            if(!texture || texture.width!=sight.scopeTextureSize)
            {
                if(texture){texture.Release();Destroy(texture);}
                texture=new RenderTexture(sight.scopeTextureSize,sight.scopeTextureSize,24,RenderTextureFormat.ARGB32);
                texture.name="Owned Scope Lens";texture.antiAliasing=1;texture.Create();
            }
            lens=attachment.scopeLens;originalMaterial=lens.sharedMaterial;
            lensMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            lensMaterial.SetTexture("_BaseMap",texture);lensMaterial.SetColor("_BaseColor",Color.white);
            lensMaterial.SetFloat("_Cull",0);lens.sharedMaterial=lensMaterial;renderedSight=sight;
            scopeCamera.targetTexture=texture;
        }
        var camera=view.playerCamera;
        scopeCamera.transform.SetPositionAndRotation(camera.transform.position,camera.transform.rotation);
        // Match the firing ray; the native lens UVs/reticle still move with the gun.
        scopeCamera.fieldOfView=sight.scopeFieldOfView;
        scopeCamera.cullingMask=camera.cullingMask&~(1<<view.firstPersonLayer);
        scopeCamera.nearClipPlane=camera.nearClipPlane;scopeCamera.farClipPlane=camera.farClipPlane;
        scopeCamera.clearFlags=camera.clearFlags;scopeCamera.backgroundColor=camera.backgroundColor;
        scopeCamera.depth=camera.depth-1;scopeCamera.enabled=true;
    }
    void EnsureCamera()
    {
        if(scopeCamera)return;
        scopeCamera=new GameObject("Owned Scope Camera").AddComponent<Camera>();
        scopeCamera.enabled=false;scopeCamera.allowHDR=false;scopeCamera.allowMSAA=false;
        var data=scopeCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        data.renderShadows=false;data.renderPostProcessing=false;
        data.requiresColorOption=CameraOverrideOption.Off;data.requiresDepthOption=CameraOverrideOption.Off;
    }
    void ReleaseLens()
    {
        if(lens)lens.sharedMaterial=originalMaterial;
        if(lensMaterial)Destroy(lensMaterial);
        lens=null;lensMaterial=null;renderedSight=null;
    }
    void OnDisable(){ReleaseLens();if(scopeCamera)scopeCamera.enabled=false;}
    void OnDestroy(){ReleaseLens();if(scopeCamera)Destroy(scopeCamera.gameObject);if(texture){texture.Release();Destroy(texture);}}
}
