Shader "Flooded_Grounds/URP/Tree Leaves" {
    Properties {
 _MainTex ("Atlas Albedo / Alpha",2D)="white" {}
 _BumpSpecMap ("Packed Normal (GA) / Specular (R)",2D)="bump" {}
 _TranslucencyMap ("Translucency (B) / Gloss (A)",2D)="white" {}
 _Color ("Tint",Color)=(1,1,1,1)
 _Cutoff ("Alpha Cutoff",Range(0,1))=.3
 _TranslucencyColor ("Translucency Tint",Color)=(.73,.85,.41,1)
 _TranslucencyViewDependency ("Translucency View Dependence",Range(0,1))=.7
 _ShadowStrength ("Leaf Shadow Strength",Range(0,1))=.8
 _WindStrength ("Wind Multiplier",Range(0,2))=1
 [HideInInspector] _TreeInstanceColor ("Tree Tint",Vector)=(1,1,1,1)
 [HideInInspector] _TreeInstanceScale ("Tree Scale",Vector)=(1,1,1,1)
 [HideInInspector] _SquashAmount ("Squash",Float)=1
}
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TreeLeaf" "Queue"="AlphaTest" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_BumpSpecMap); SAMPLER(sampler_BumpSpecMap);
        TEXTURE2D(_TranslucencyMap); SAMPLER(sampler_TranslucencyMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST, _Color, _TranslucencyColor;
        float _Cutoff, _TranslucencyViewDependency, _ShadowStrength, _WindStrength;
        CBUFFER_END
        float4 _TreeInstanceColor, _TreeInstanceScale, _SquashPlaneNormal;
        float _SquashAmount;
        float4 _Wind, _FGWind;
        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0; float2 lightmapUV:TEXCOORD1; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; half4 tangentWS:TEXCOORD2; float2 uv:TEXCOORD3; float3 positionOS:TEXCOORD4; half3 normalOS:TEXCOORD5; half4 color:COLOR; DECLARE_LIGHTMAP_OR_SH(lightmapUV,vertexSH,6); float fog:TEXCOORD7; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
        void Deform(inout Attributes v) {
            float billboard=1-abs(v.tangentOS.w);
            float3 rightOS=TransformWorldToObjectDir(UNITY_MATRIX_I_V._m00_m10_m20,false);
            float3 upOS=TransformWorldToObjectDir(UNITY_MATRIX_I_V._m01_m11_m21,false);
            v.positionOS.xyz+=(rightOS*v.normalOS.x+upOS*v.normalOS.y)*billboard;
            v.normalOS=lerp(v.normalOS,TransformWorldToObjectDir(-UNITY_MATRIX_I_V._m02_m12_m22),billboard);
            v.tangentOS.xyz=lerp(v.tangentOS.xyz,rightOS,billboard);
            v.tangentOS.w=lerp(v.tangentOS.w,-1,billboard);
            float3 instanceScale=dot(abs(_TreeInstanceScale.xyz),float3(1,1,1))>.001?_TreeInstanceScale.xyz:float3(1,1,1);
            v.positionOS.xyz*=instanceScale;
            float4 wind=_Wind;
            if(dot(abs(wind),float4(1,1,1,1))<.001) wind=float4(TransformWorldToObjectDir(_FGWind.xyz,false),_FGWind.w);
            wind*=_WindStrength;
            float4 anim=float4(v.color.xy,v.lightmapUV.xy);
            float objectPhase=dot(GetObjectToWorldMatrix()._m03_m13_m23,float3(1,1,1));
            float branchPhase=objectPhase+anim.x;
            float vertexPhase=dot(v.positionOS.xyz,(anim.y+branchPhase).xxx);
            float2 wavesIn=_Time.yy+float2(vertexPhase,branchPhase);
            float4 waves=frac(wavesIn.xxyy*float4(1.975,.793,.375,.193))*2-1;
            waves=abs(frac(waves+.5)*2-1);waves=waves*waves*(3-2*waves);
            float2 sum=waves.xz+waves.yw;
            float3 bend=anim.y*.1*v.normalOS;bend.y=anim.w*.3;
            v.positionOS.xyz+=((sum.xyx*bend)+(wind.xyz*sum.y*anim.w))*wind.w;
            v.positionOS.xyz+=anim.z*wind.xyz;
            float3 projected=v.positionOS.xyz-(dot(_SquashPlaneNormal.xyz,v.positionOS.xyz)+_SquashPlaneNormal.w)*_SquashPlaneNormal.xyz;
            v.positionOS.xyz=lerp(projected,v.positionOS.xyz,_SquashAmount);
            float3 tint=dot(abs(_TreeInstanceColor.rgb),float3(1,1,1))>.001?_TreeInstanceColor.rgb:float3(1,1,1);
            v.color.rgb=tint*_Color.rgb;
        }
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
            s=(SurfaceData)0;s.alpha=1;s.occlusion=saturate(i.color.a);s.smoothness=.1;
            float2 uv=i.uv*_MainTex_ST.xy+_MainTex_ST.zw;
            half4 c=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv);
            clip(c.a-_Cutoff);
            s.albedo=c.rgb*i.color.rgb;
            half4 packed=SAMPLE_TEXTURE2D(_BumpSpecMap,sampler_BumpSpecMap,uv);
            // Tree Creator's atlas stores normal X/Y in alpha/green, not red/green.
            s.normalTS=UnpackNormalMapRGorAG(half4(1,packed.g,1,packed.a));
            s.smoothness=SAMPLE_TEXTURE2D(_TranslucencyMap,sampler_TranslucencyMap,uv).a*.3;
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
            half4 c=UniversalFragmentPBR(d,s);
            Light light=GetMainLight(d.shadowCoord);
            half nl=dot(d.normalWS,light.direction);
            half back=lerp(saturate(-nl),saturate(dot(d.viewDirectionWS,-light.direction)),_TranslucencyViewDependency);
            half trans=SAMPLE_TEXTURE2D(_TranslucencyMap,sampler_TranslucencyMap,i.uv*_MainTex_ST.xy+_MainTex_ST.zw).b;
            half wrap=max(0,nl*.6+.4)-saturate(nl);
            c.rgb+=s.albedo*light.color*(wrap+back*trans*_TranslucencyColor.rgb*2)*light.distanceAttenuation*lerp(1,light.shadowAttenuation,_ShadowStrength)*s.occlusion; c.rgb=MixFog(c.rgb,i.fog); return c;
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
