Shader "Game/UI/Spectator CRT"
{
    Properties
    {
        [PerRendererData] _MainTex ("Camera Feed", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Scanlines ("Scanline Strength", Range(0,0.4)) = 0.12
        _Grain ("Signal Grain", Range(0,0.1)) = 0.018
        _Curvature ("Screen Curvature", Range(0,0.1)) = 0.025
        _Vignette ("Edge Dimming", Range(0,0.5)) = 0.22
        _Brightness ("Brightness", Range(0.5,1.5)) = 1.08
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; float4 local:TEXCOORD1; };
            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _Color, _ClipRect;
            float _Scanlines, _Grain, _Curvature, _Vignette, _Brightness;
            v2f vert(appdata v)
            {
                v2f o; o.local=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex);
                o.uv=v.uv; o.color=v.color*_Color; return o;
            }
            float noise(float2 p) { return frac(sin(dot(p,float2(12.9898,78.233)))*43758.5453); }
            half4 frag(v2f i):SV_Target
            {
                float2 centered=i.uv*2-1;
                float2 uv=(centered*(1+_Curvature*dot(centered,centered)))*.5+.5;
                // Quiet analog character: restrained fringe, phosphor lines and signal grain.
                float shift=_MainTex_TexelSize.x*.65;
                float3 color;
                color.r=tex2D(_MainTex,uv+float2(shift,0)).r;
                color.g=tex2D(_MainTex,uv).g;
                color.b=tex2D(_MainTex,uv-float2(shift,0)).b;
                color *= 1-_Scanlines*(.5+.5*cos(uv.y/_MainTex_TexelSize.y*3.14159265));
                color += (noise(floor(uv/_MainTex_TexelSize.xy)+floor(_Time.y*12))-.5)*_Grain;
                color *= _Brightness*(1-_Vignette*dot(centered,centered)*.5);
                float inside=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
                half4 result=half4(max(0,color)*inside,i.color.a);
                result.rgb *= i.color.rgb;
                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
