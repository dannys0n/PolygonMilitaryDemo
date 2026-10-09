Shader "Flooded_Grounds/PBR_Water" {
    Properties {
	_Color ("Main Color", Color) = (1,1,1,1)
	_Emis("Self-Ilumination", Range(0,1)) = 0.1
	_Smth("Smoothness", Range(0,1)) = 0.9
	_Parallax ("Height", Range (0.005, 0.08)) = 0.02
	_MainTex ("Base (RGB) Gloss (A)", 2D) = "white" {}
	_BumpMap ("Normalmap", 2D) = "bump" {}
	_BumpMap2 ("Normalmap2", 2D) = "bump" {}
	_BumpLerp("Normalmap2 Blend", Range(0,1)) = 0.5
	_ParallaxMap ("Heightmap", 2D) = "black" {}
	_ScrollSpeed("Scroll Speed", float) = 0.2
	_WaveFreq("Wave Frequency", float) = 20
	_WaveHeight("Wave Height", float) = 0.1
	
	//_Opacity("Opacity", Range(0,1)) = 0.8
	
}
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_BumpMap2); SAMPLER(sampler_BumpMap2);
        TEXTURE2D(_ParallaxMap); SAMPLER(sampler_ParallaxMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST;
        float4 _BumpMap_ST;
        float4 _BumpMap2_ST;
        float4 _ParallaxMap_ST;
        half4 _Color; float _Emis,_Smth,_Parallax,_BumpLerp,_ScrollSpeed,_WaveFreq,_WaveHeight;
        CBUFFER_END

        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0; float2 lightmapUV:TEXCOORD1; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; half4 tangentWS:TEXCOORD2; float2 uv:TEXCOORD3; float3 positionOS:TEXCOORD4; half3 normalOS:TEXCOORD5; half4 color:COLOR; DECLARE_LIGHTMAP_OR_SH(lightmapUV,vertexSH,6); float fog:TEXCOORD7; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
        void Deform(inout Attributes v) { v.positionOS.y=sin(_Time.x*_WaveFreq+(v.positionOS.x+v.positionOS.z*2)*8)*_WaveHeight; }
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

            float2 scroll=float2(_ScrollSpeed,_ScrollSpeed*.5)*_Time.x;
            float2 uv=i.uv*_MainTex_ST.xy+_MainTex_ST.zw;
            half3 bitangent=cross(i.normalWS,i.tangentWS.xyz)*i.tangentWS.w;
            float3 view=GetWorldSpaceNormalizeViewDir(i.positionWS);
            float3 viewTS=float3(dot(view,i.tangentWS.xyz),dot(view,bitangent),dot(view,i.normalWS));
            float h=SAMPLE_TEXTURE2D(_ParallaxMap,sampler_ParallaxMap,i.uv*_ParallaxMap_ST.xy+_ParallaxMap_ST.zw+scroll*.2).r;
            float2 offset=(h-.5)*_Parallax*viewTS.xy/(abs(viewTS.z)+.42);
            half4 color=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv+scroll+offset)*_Color;
            float2 normalUV=i.uv*_BumpMap_ST.xy+_BumpMap_ST.zw;
            half3 n1=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,normalUV+scroll+offset));
            half3 n2=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,normalUV+float2((1-_ScrollSpeed)*_Time.x,(1-_ScrollSpeed*_Time.x)*.5)+offset));
            half3 n3=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap2,sampler_BumpMap2,i.uv*_BumpMap2_ST.xy+_BumpMap2_ST.zw));
            s.albedo=color.rgb; s.emission=color.rgb*_Emis; s.smoothness=_Smth;
            s.normalTS=normalize(lerp(n1+half3(n2.xy,0),n3,_BumpLerp));
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
