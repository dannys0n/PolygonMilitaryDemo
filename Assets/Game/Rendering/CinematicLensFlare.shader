Shader "Hidden/Game/CinematicLensFlare" {
 SubShader { Tags {"RenderPipeline"="UniversalPipeline"} ZWrite Off ZTest Always Cull Off
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
 TEXTURE2D_X_FLOAT(_OpticsDepth);TEXTURE2D_X(_OpticsBright);TEXTURE2D_X(_OpticsRays);
 float4 _Optics;float _RayIntensity;
 float3 Highlight(float2 uv){if(any(uv<0)||any(uv>1))return 0;return SAMPLE_TEXTURE2D_X(_OpticsBright,sampler_LinearClamp,uv).rgb*smoothstep(.04,.18,length(uv-.5));}
 float EyeDepth(float2 uv){return LinearEyeDepth(SAMPLE_TEXTURE2D_X(_OpticsDepth,sampler_PointClamp,uv).r,_ZBufferParams);}
 float3 Extract(Varyings input):SV_Target {float3 c=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,input.texcoord).rgb;float l=max(c.r,max(c.g,c.b));float soft=saturate((l-_Optics.x*.7f)/max(_Optics.x*.6f,.001f));float2 footprint=_BlitTexture_TexelSize.xy*2;float depth=EyeDepth(input.texcoord);depth=min(depth,EyeDepth(input.texcoord+footprint));depth=min(depth,EyeDepth(input.texcoord-footprint));depth=min(depth,EyeDepth(input.texcoord+float2(footprint.x,-footprint.y)));depth=min(depth,EyeDepth(input.texcoord+float2(-footprint.x,footprint.y)));float worldOnly=smoothstep(1.5,3.0,depth);return min(c,24)*soft*max(l-_Optics.x,0)/max(l,.001f)*worldOnly;}
 float3 Source(float2 uv){if(any(uv<0)||any(uv>1))return 0;return SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv).rgb;}
 float3 Scatter(Varyings input):SV_Target {
 float2 uv=input.texcoord;float aspect=_ScreenParams.x/_ScreenParams.y;float3 glare=0,rays=0;
 // Continuous weighted aperture streaks; quarter resolution keeps the convolution affordable.
 [unroll]for(int axis=0;axis<4;axis++){float angle=axis*.785398+0.12;float2 direction=float2(cos(angle)/aspect,sin(angle));
 [unroll]for(int tap=1;tap<=16;tap++){float t=tap/16.0;float weight=exp(-t*4);float2 offset=direction*(t*.12);rays+=(Source(uv+offset)+Source(uv-offset))*weight;}}
 float total=0;
 [unroll]for(int y=-2;y<=2;y++) [unroll]for(int x=-2;x<=2;x++){float weight=exp(-(x*x+y*y)*.4);float2 off=float2(x/aspect,y)*.009;glare+=Source(uv+off)*weight;total+=weight;}
 return glare*(_Optics.z/total)+rays*(_RayIntensity/8);
 }
 float3 Defocus(Varyings input):SV_Target {
 float3 sum=0;float total=0;
 [unroll]for(int y=-2;y<=2;y++) [unroll]for(int x=-2;x<=2;x++){float w=exp(-(x*x+y*y)*.5);sum+=Source(input.texcoord+float2(x,y)*_BlitTexture_TexelSize.xy*1.5)*w;total+=w;}
 return sum/total;
 }
 float3 Composite(Varyings input):SV_Target {
 float2 uv=input.texcoord;float3 original=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv).rgb;
 float2 mirrored=1-uv;float2 axis=.5-mirrored;float3 ghosts=0;
 [unroll]for(int i=0;i<4;i++){float2 p=mirrored+axis*(i*.38-.12);float2 dispersion=normalize(axis+float2(.00001,.00001))*.0007*(i+1);float3 g=float3(Highlight(p+dispersion).r,Highlight(p).g,Highlight(p-dispersion).b);ghosts+=g*pow(.55,i);}
 // A broad off-axis halo, projected through the lens rather than drawn over the source.
 float2 radial=normalize(axis+float2(.00001,.00001));ghosts+=Highlight(mirrored+radial*.32)*.22;
 float3 scatter=SAMPLE_TEXTURE2D_X(_OpticsRays,sampler_LinearClamp,uv).rgb;
 float edge=saturate(1-length((uv-.5)*1.3));return original+_Optics.y*(scatter+ghosts*_Optics.w*edge*smoothstep(.035,.16,length(axis)));
 }
 ENDHLSL
 Pass { HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Extract
 ENDHLSL }
 Pass { HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Scatter
 ENDHLSL }
 Pass { HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Composite
 ENDHLSL }
 Pass { HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Defocus
 ENDHLSL }
 }
}
