Shader "Game/Environment/Shadowed Light Shafts"
{
    Properties
    {
        [HDR] _Tint("Scattering color",Color)=(1,.94,.82,1)
        _Density("Haze density",Range(0,.05))=.006
        _NoiseScale("Dust scale",Float)=.16
        _Drift("Dust drift",Float)=.06
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off ZTest Always Cull Front
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Density, _NoiseScale, _Drift;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o;o.positionWS=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float3 origin=TransformWorldToObject(_WorldSpaceCameraPos);
                float3 worldRay=normalize(i.positionWS-_WorldSpaceCameraPos);
                float3 ray=mul((float3x3)unity_WorldToObject,worldRay);
                float3 safeRay=sign(ray+1e-8)*max(abs(ray),1e-6);
                float3 a=(-.5-origin)/safeRay,b=(.5-origin)/safeRay;
                float3 lo=min(a,b),hi=max(a,b);
                float entry=max(0,max(lo.x,max(lo.y,lo.z)));
                float exit=min(hi.x,min(hi.y,hi.z));
                float2 uv=GetNormalizedScreenSpaceUV(i.positionCS);
                float eye=LinearEyeDepth(SampleSceneDepth(uv),_ZBufferParams);
                float forwardDot=max(.01,dot(worldRay,-UNITY_MATRIX_V[2].xyz));
                exit=min(exit,eye/forwardDot);
                if(exit<=entry)discard;
                const int steps=12;
                float stepLength=(exit-entry)/steps;
                float opacity=0, lightEnergy=0;
                [unroll]for(int n=0;n<steps;n++)
                {
                    float t=entry+(n+.5)*stepLength;
                    float3 p=_WorldSpaceCameraPos+worldRay*t;
                    float3 local=origin+ray*t;
                    float edge=smoothstep(0,.16,.5-max(abs(local.x),max(abs(local.y),abs(local.z))));
                    float noise=.75+.25*sin(p.x*_NoiseScale+_Time.y*_Drift)*sin(p.z*_NoiseScale*.73+p.y*.31);
                    float density=_Density*stepLength*edge*noise;
                    Light sun=GetMainLight(TransformWorldToShadowCoord(p));
                    float contribution=(1-opacity)*(1-exp(-density));
                    // Shadow sampling makes obstacles carve beams through the haze.
                    lightEnergy+=contribution*(.10+.9*sun.shadowAttenuation);
                    opacity+=contribution;
                }
                Light sun=GetMainLight();
                float forwardScatter=.7+.6*pow(saturate(dot(worldRay,sun.direction)),6);
                return half4(_Tint.rgb*sun.color*lightEnergy*forwardScatter,opacity*.45);
            }
            ENDHLSL
        }
    }
}
