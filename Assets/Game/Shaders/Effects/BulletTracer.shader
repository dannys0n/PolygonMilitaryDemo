Shader "Game/Effects/BulletTracer"
{
 Properties { [HDR] _Color("Glow",Color)=(3,1.7,.55,1) }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
  Pass
  {
   Blend SrcAlpha One
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_instancing
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _Color;
   CBUFFER_END
   struct Attributes {float4 positionOS:POSITION;half4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct Varyings {float4 positionCS:SV_POSITION;half4 color:COLOR;};
   Varyings Vert(Attributes a){UNITY_SETUP_INSTANCE_ID(a);Varyings o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.color=a.color;return o;}
   half4 Frag(Varyings i):SV_Target{return half4(_Color.rgb*i.color.rgb,_Color.a*i.color.a);}
   ENDHLSL
  }
 }
}
