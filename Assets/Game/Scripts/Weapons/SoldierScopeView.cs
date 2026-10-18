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
    public bool HasActiveLens => lens && scopeCamera && texture;
    public RenderTexture ScopeTexture=>texture;
    void LateUpdate()
    {
        var sights=loadout.EquippedSights;
        var sight=sights?sights.SelectedSight:null;
        var attachment=sights?sights.FirstPersonAttachment:null;
        bool equippedInViewer=view.isActiveAndEnabled && view.firstPerson && view.NeedsCameraAnimation
            && loadout.HasFirstPersonPresentation && loadout.FirstPersonView.gameObject.activeInHierarchy
            && sight && sights.UsesPictureInPicture && attachment && attachment.scopeLens
            && attachment.scopeLens.enabled && attachment.scopeLens.gameObject.activeInHierarchy
            && !attachment.scopeLens.forceRenderingOff
            && (view.playerCamera.cullingMask & (1 << attachment.scopeLens.gameObject.layer)) != 0;
        if(!equippedInViewer)
        {
            ReleaseLens();if(scopeCamera)scopeCamera.enabled=false;ReleaseTexture();return;
        }
        bool active=view.AimBlend>.08f;
        if(!active){ReleaseLens();if(scopeCamera)scopeCamera.enabled=false;return;}
        if(lens!=attachment.scopeLens || renderedSight!=sight)
        {
            ReleaseLens();EnsureCamera();
            if(!texture || texture.width!=sight.scopeTextureSize)
            {
                ReleaseTexture();
                texture=new RenderTexture(sight.scopeTextureSize,sight.scopeTextureSize,24,RenderTextureFormat.ARGB32);
                texture.name="Owned Scope Lens";texture.antiAliasing=1;texture.Create();
            }
            lens=attachment.scopeLens;originalMaterial=lens.sharedMaterial;
            lensMaterial=new Material(Shader.Find("Game/Scope Surface"));
            lensMaterial.SetTexture("_BaseMap",texture);lensMaterial.SetColor("_BaseColor",Color.white);
            // Only the visible lens aperture admits the reticle; the housing keeps normal depth testing.
            lensMaterial.SetFloat("_StencilPass",2);lensMaterial.renderQueue=2450;
            lens.sharedMaterial=lensMaterial;renderedSight=sight;
            scopeCamera.targetTexture=texture;
        }
        var camera=view.playerCamera;
        scopeCamera.transform.SetPositionAndRotation(camera.transform.position,camera.transform.rotation);
        // Match the firing ray; the native lens UVs/reticle still move with the gun.
        // A square texture displayed in a small lens shrinks the image again. Account
        // for that projected lens height, rather than treating a raw camera FOV as zoom.
        scopeCamera.fieldOfView=LensFieldOfView(camera,attachment.scopeLens,sight.magnification);
        scopeCamera.cullingMask=camera.cullingMask&~(1<<view.firstPersonLayer);
        scopeCamera.nearClipPlane=camera.nearClipPlane;scopeCamera.farClipPlane=camera.farClipPlane;
        scopeCamera.clearFlags=camera.clearFlags;scopeCamera.backgroundColor=camera.backgroundColor;
        scopeCamera.depth=camera.depth-1;
        // A scheduled monitor captures its lens explicitly before its own view.
        scopeCamera.enabled=camera.enabled;
    }
    public static float LensFieldOfView(Camera viewer, Renderer aperture, float magnification)
    {
        var bounds=aperture.localBounds;
        float low=float.PositiveInfinity,high=float.NegativeInfinity;
        for(int i=0;i<8;i++)
        {
            var corner=bounds.center+Vector3.Scale(bounds.extents,
                new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
            var point=viewer.WorldToViewportPoint(aperture.transform.TransformPoint(corner));
            if(point.z<=viewer.nearClipPlane)continue;
            low=Mathf.Min(low,point.y);high=Mathf.Max(high,point.y);
        }
        // Do not derive an invalid projection while the gun is off screen during ADS entry.
        float height=float.IsInfinity(low)? .1f:Mathf.Max(.001f,high-low);
        float tangent=height*Mathf.Tan(viewer.fieldOfView*Mathf.Deg2Rad*.5f)/Mathf.Max(1f,magnification);
        return Mathf.Clamp(2f*Mathf.Atan(tangent)*Mathf.Rad2Deg,.01f,120f);
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
    void ReleaseTexture()
    {
        if(!texture)return;
        if(scopeCamera)scopeCamera.targetTexture=null;
        texture.Release();Destroy(texture);texture=null;
    }
    void OnDisable(){ReleaseLens();if(scopeCamera)scopeCamera.enabled=false;ReleaseTexture();}
    void OnDestroy(){ReleaseLens();ReleaseTexture();if(scopeCamera)Destroy(scopeCamera.gameObject);}
}
