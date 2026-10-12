using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
public class CinematicLensFlareFeature : ScriptableRendererFeature
{
 public Shader shader;
 Material material; OpticsPass pass;
 public override void Create(){CoreUtils.Destroy(material);if(shader){material=CoreUtils.CreateEngineMaterial(shader);pass=new OpticsPass(material);}}
 public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data){
 if(material && data.cameraData.postProcessEnabled && data.cameraData.cameraType==CameraType.Game
 && VolumeManager.instance.stack.GetComponent<CinematicLensFlare>().IsActive())renderer.EnqueuePass(pass);
 }
 protected override void Dispose(bool disposing){CoreUtils.Destroy(material);}
 class OpticsPass:ScriptableRenderPass {
 readonly Material material;
 class DrawData {public TextureHandle source,bright,rays,depth;public Material material;public int pass;}
 public OpticsPass(Material m){material=m;renderPassEvent=RenderPassEvent.BeforeRenderingPostProcessing;requiresIntermediateTexture=true;ConfigureInput(ScriptableRenderPassInput.Depth);}
 static readonly int Bright=Shader.PropertyToID("_OpticsBright"),Rays=Shader.PropertyToID("_OpticsRays"),Depth=Shader.PropertyToID("_OpticsDepth");
 public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame){
 var resources=frame.Get<UniversalResourceData>();if(resources.isActiveTargetBackBuffer)return;
 var settings=VolumeManager.instance.stack.GetComponent<CinematicLensFlare>();if(!settings.IsActive())return;
 material.SetVector("_Optics",new Vector4(Mathf.GammaToLinearSpace(settings.threshold.value),settings.intensity.value,settings.glare.value,settings.ghosts.value));material.SetFloat("_RayIntensity",settings.rays.value);
 var source=resources.activeColorTexture;var desc=graph.GetTextureDesc(source);desc.name="Optical highlights";desc.width=Mathf.Max(1,desc.width/4);desc.height=Mathf.Max(1,desc.height/4);desc.depthBufferBits=DepthBits.None;desc.msaaSamples=MSAASamples.None;desc.clearBuffer=false;
 var bright=graph.CreateTexture(desc);desc.name="Optical scattering";var rays=graph.CreateTexture(desc);
 Draw(graph,"Extract distant HDR highlights",source,bright,default,default,0,resources.cameraDepthTexture);
 Draw(graph,"Lens scattering",bright,rays,default,default,1);
 desc.name="Defocused lens reflections";var ghosts=graph.CreateTexture(desc);
 Draw(graph,"Defocus internal reflections",bright,ghosts,default,default,3);
 var outputDesc=graph.GetTextureDesc(source);outputDesc.name="Camera optical composite";outputDesc.depthBufferBits=DepthBits.None;outputDesc.msaaSamples=MSAASamples.None;outputDesc.clearBuffer=false;var output=graph.CreateTexture(outputDesc);
 Draw(graph,"Composite camera optics",source,output,ghosts,rays,2);resources.cameraColor=output;
 }
 void Draw(RenderGraph graph,string name,TextureHandle source,TextureHandle destination,TextureHandle bright,TextureHandle rays,int index,TextureHandle depth=default){
 using(var builder=graph.AddRasterRenderPass<DrawData>(name,out var data)){
 data.source=source;data.bright=bright;data.rays=rays;data.material=material;data.pass=index;data.depth=depth;
 builder.UseTexture(source,AccessFlags.Read);if(depth.IsValid())builder.UseTexture(depth,AccessFlags.Read);if(bright.IsValid())builder.UseTexture(bright,AccessFlags.Read);if(rays.IsValid())builder.UseTexture(rays,AccessFlags.Read);
 builder.SetRenderAttachment(destination,0,AccessFlags.Write);builder.AllowGlobalStateModification(true);
 builder.SetRenderFunc(static(DrawData d,RasterGraphContext ctx)=>{if(d.depth.IsValid())ctx.cmd.SetGlobalTexture(Depth,d.depth);if(d.bright.IsValid())ctx.cmd.SetGlobalTexture(Bright,d.bright);if(d.rays.IsValid())ctx.cmd.SetGlobalTexture(Rays,d.rays);Blitter.BlitTexture(ctx.cmd,d.source,new Vector4(1,1,0,0),d.material,d.pass);});
 }
 }
 }
}
