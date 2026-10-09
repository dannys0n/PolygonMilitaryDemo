Shader "Flooded_Grounds/PBR_TopBlend" {
    Properties {
        _MainTex ("Base Albedo (RGB)", 2D) = "white" {}
        _Spc("Base Metalness(R) Smoothness(A)", 2D) = "black" {}
        _BumpMap ("Base Normal", 2D) = "bump" {} 
        _AO("Base AO", 2D)= "white" {}
        _layer1Tex ("Layer1 Albedo (RGB) Smoothness (A)", 2D) = "white" {}
        _layer1Metal ("Layer1 Metalness", Range(0,1)) = 0
        _layer1Norm("Layer 1 Normal", 2D) = "bump" {}
        _layer1Breakup ("Layer1 Breakup (R)", 2D) = "white" {}
        _layer1BreakupAmnt ("Layer1 Breakup Amount", Range(0,1)) = 0.5
        _layer1Tiling("Layer1 Tiling", float) = 10
        _Power ("Layer1 Blend Amount", float ) = 1
        _Shift("Layer1 Blend Height", float) = 1           
        _DetailBump ("Detail Normal", 2D) = "bump" {}  
        _DetailInt ("DetailNormal Intensity", Range(0,1)) = 0.4
        _DetailTiling("DetailNormal Tiling", float) = 2  
    }
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_Spc); SAMPLER(sampler_Spc);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_AO); SAMPLER(sampler_AO);
        TEXTURE2D(_layer1Tex); SAMPLER(sampler_layer1Tex);
        TEXTURE2D(_layer1Norm); SAMPLER(sampler_layer1Norm);
        TEXTURE2D(_layer1Breakup); SAMPLER(sampler_layer1Breakup);
        TEXTURE2D(_DetailBump); SAMPLER(sampler_DetailBump);
        CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST;
        float4 _Spc_ST;
        float4 _BumpMap_ST;
        float4 _AO_ST;
        float4 _layer1Tex_ST;
        float4 _layer1Norm_ST;
        float4 _layer1Breakup_ST;
        float4 _DetailBump_ST;
        float _layer1Metal,_layer1BreakupAmnt,_layer1Tiling,_Power,_Shift,_DetailInt,_DetailTiling;
        CBUFFER_END

        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0; float2 lightmapUV:TEXCOORD1; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; half4 tangentWS:TEXCOORD2; float2 uv:TEXCOORD3; float3 positionOS:TEXCOORD4; half3 normalOS:TEXCOORD5; half4 color:COLOR; DECLARE_LIGHTMAP_OR_SH(lightmapUV,vertexSH,6); float fog:TEXCOORD7; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
        void Deform(inout Attributes v) {  }
        Varyings Vert(Attributes v) {
            Varyings o=(Varyings)0; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            Deform(v);
            VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);
            VertexNormalInputs n=GetVertexNormalInputs(v.normalOS,v.tangentOS);
            o.positionCS=p.positionCS; o.fog=ComputeFogFactor(p.positionCS.z); o.positionWS=p.positionWS; o.normalWS=n.normalWS;
            o.tangentWS=half4(n.tangentWS,v.tangentOS.w*GetOddNegativeScale());
            o.uv=v.uv; o.positionOS=v.positionOS.xyz; o.normalOS=v.normalOS; o.color=v.color;
            OUTPUT_LIGHTMAP_UV(v.lightmapUV,unity_LightmapST,o.lightmapUV); OUTPUT_SH(o.normalWS,o.vertexSH);
            return o;
        }
        half3 NormalWorld(Varyings i,half3 normalTS) {
            half3 bitangent=cross(i.normalWS,i.tangentWS.xyz)*i.tangentWS.w;
            return NormalizeNormalPerPixel(TransformTangentToWorld(normalTS,half3x3(i.tangentWS.xyz,bitangent,i.normalWS)));
        }
        void Surface(Varyings i,out SurfaceData s) {
            s=(SurfaceData)0; s.alpha=1; s.occlusion=1; s.normalTS=half3(0,0,1);

            float2 uv=i.uv*_MainTex_ST.xy+_MainTex_ST.zw;
            half3 main=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv).rgb;
            half3 norm=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,i.uv*_BumpMap_ST.xy+_BumpMap_ST.zw));
            half4 spec=SAMPLE_TEXTURE2D(_Spc,sampler_Spc,uv);
            half4 layer=SAMPLE_TEXTURE2D(_layer1Tex,sampler_layer1Tex,uv*_layer1Tiling);
            half3 layerNormal=UnpackNormal(SAMPLE_TEXTURE2D(_layer1Norm,sampler_layer1Norm,uv*_layer1Tiling));
            half breakup=SAMPLE_TEXTURE2D(_layer1Breakup,sampler_layer1Breakup,uv*_layer1Tiling).r;
            half blend=(NormalWorld(i,norm+half3(layerNormal.xy*.6,0)).y*_Power+_Shift)*lerp(1,breakup,_layer1BreakupAmnt);
            blend=saturate(blend*blend*blend);
            half3 detail=UnpackNormal(SAMPLE_TEXTURE2D(_DetailBump,sampler_DetailBump,uv*_DetailTiling));
            s.albedo=lerp(main,layer.rgb,blend);
            s.normalTS=normalize(lerp(norm,layerNormal,blend)+half3(detail.xy*_DetailInt,0));
            s.metallic=lerp(spec.r,_layer1Metal,blend); s.smoothness=lerp(spec.a,layer.a,blend);
            s.occlusion=SAMPLE_TEXTURE2D(_AO,sampler_AO,i.uv*_AO_ST.xy+_AO_ST.zw).r;
        }
        half4 Frag(Varyings i):SV_Target {
            UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            SurfaceData s; Surface(i,s);
            InputData d=(InputData)0;
            d.positionWS=i.positionWS; d.normalWS=NormalWorld(i,s.normalTS);
            d.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);
            d.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
            d.bakedGI=SAMPLE_GI(i.lightmapUV,i.vertexSH,d.normalWS);
            d.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
            d.shadowMask=SAMPLE_SHADOWMASK(i.lightmapUV);
            d.vertexLighting=VertexLighting(i.positionWS,d.normalWS);
            half4 c=UniversalFragmentPBR(d,s); c.rgb=MixFog(c.rgb,i.fog); return c;
        }
        half4 DepthFrag(Varyings i):SV_Target { SurfaceData s; Surface(i,s); return 0; }
        float3 _LightDirection; float3 _LightPosition;
        Varyings ShadowVert(Attributes v) {
            Varyings o=Vert(v);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirection=normalize(_LightPosition-o.positionWS);
            #else
                float3 lightDirection=_LightDirection;
            #endif
            o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,lightDirection));
            #if UNITY_REVERSED_Z
                o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE*o.positionCS.w);
            #else
                o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE*o.positionCS.w);
            #endif
            return o;
        }

        ENDHLSL
        Pass {
            Name "ForwardLit" Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            ENDHLSL
        }
        Pass {
            Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
        Pass {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass {
            Name "DepthNormals" Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing
            half4 NormalsFrag(Varyings i):SV_Target { SurfaceData s; Surface(i,s); return half4(NormalWorld(i,s.normalTS),0); }
            ENDHLSL
        }
    }
    FallBack Off
}
