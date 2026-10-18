using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Use URP's single-camera path for small, explicitly scheduled views. Do not
// repeat the main camera stack, post effects or shadow maps for a monitor.
public static class PreviewCameraRenderer
{
    static readonly UniversalRenderPipeline.SingleCameraRequest request = new();
    public static void Render(Camera camera)
    {
        if (!camera || !camera.targetTexture) return;
        if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset)
        { camera.Render(); return; }
        var data = camera.GetUniversalAdditionalCameraData();
        bool shadows=data.renderShadows, post=data.renderPostProcessing;
        var color=data.requiresColorOption; var depth=data.requiresDepthOption;
        try
        {
            data.renderShadows=false;data.renderPostProcessing=false;
            data.requiresColorOption=CameraOverrideOption.Off;data.requiresDepthOption=CameraOverrideOption.Off;
            request.destination=camera.targetTexture;
            RenderPipeline.SubmitRenderRequest(camera,request);
        }
        finally
        {
            request.destination=null;
            data.renderShadows=shadows;data.renderPostProcessing=post;
            data.requiresColorOption=color;data.requiresDepthOption=depth;
        }
    }
}
