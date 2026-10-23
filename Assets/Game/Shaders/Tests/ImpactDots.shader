Shader "Game/Tests/Impact Dots"
{
    Properties { _ClipMin("Clip minimum",Vector)=(-100000,-100000,-100000,0) _ClipMax("Clip maximum",Vector)=(100000,100000,100000,0) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _ClipMin,_ClipMax;
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; float3 localPosition:TEXCOORD1; };
            Varyings vert(Attributes input)
            {
                Varyings output;output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.localPosition=input.positionOS.xyz;output.uv=input.uv;output.color=input.color;return output;
            }
            half4 frag(Varyings input):SV_Target
            {
                clip(input.localPosition-_ClipMin.xyz);clip(_ClipMax.xyz-input.localPosition);
                float radius=length(input.uv*2-1);
                half edge=saturate((1-radius)/max(fwidth(radius),.001));
                half fill=input.color.b>input.color.r?lerp(.18h,.8h,smoothstep(.92,1.,radius)):1.h;
                return half4(input.color.rgb,input.color.a*edge*fill);
            }
            ENDHLSL
        }
    }
}
