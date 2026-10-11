Shader "Game/Environment/Airborne Dust"
{
    Properties { [HDR] _Tint("Dust tint",Color)=(1,.95,.84,1) _Softness("Intersection softness",Float)=.35 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial) float4 _Tint;float _Softness; CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;float eye:TEXCOORD1;float fog:TEXCOORD2;};
            Varyings Vert(Attributes v)
            {
                Varyings o;float3 world=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(world);o.eye=-TransformWorldToView(world).z;o.uv=v.uv;o.color=v.color;o.fog=ComputeFogFactor(o.positionCS.z);return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float r=dot(i.uv-.5,i.uv-.5)*4;
                float alpha=exp(-r*6)*smoothstep(1,.5,r);
                float scene=LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(i.positionCS)),_ZBufferParams);
                alpha*=saturate((scene-i.eye)/max(.01,_Softness));
                alpha*=saturate((i.eye-.3)/1.2);
                return half4(MixFog(_Tint.rgb*i.color.rgb,i.fog),alpha*i.color.a*_Tint.a);
            }
            ENDHLSL
        }
    }
}
