Shader "Game/UI/Spectator Bodycam"
{
    Properties
    {
        [PerRendererData] _MainTex ("Camera Feed", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Distortion ("Wide Lens Distortion", Range(0,0.4)) = 0.14
        _Grain ("Signal Grain", Range(0,0.1)) = 0.018
        _Fringe ("Lens Edge Fringe", Range(0,2)) = 0.65
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
            float _Distortion, _Grain, _Fringe, _Vignette, _Brightness;
            v2f vert(appdata v)
            {
                v2f o; o.local=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex);
                o.uv=v.uv; o.color=v.color*_Color; return o;
            }
            float noise(float2 p) { return frac(sin(dot(p,float2(12.9898,78.233)))*43758.5453); }
            half4 frag(v2f i):SV_Target
            {
                float2 centered=i.uv*2-1;
                float radius=dot(centered,centered);
                // Barrel distortion belongs to the lens image, not the flat monitor edges.
                // Corner normalization keeps every sample inside the rectangular feed.
                float2 uv=centered*(1+_Distortion*radius)/(1+2*_Distortion)*.5+.5;
                float2 fringe=centered*_MainTex_TexelSize.xy*_Fringe*radius;
                float3 color;
                color.r=tex2D(_MainTex,uv+fringe).r;
                color.g=tex2D(_MainTex,uv).g;
                color.b=tex2D(_MainTex,uv-fringe).b;
                float luma=dot(color,float3(.2126,.7152,.0722));
                // Digital sensor noise is strongest in shadows; no analog scanlines.
                float grain=noise(floor(i.uv/_MainTex_TexelSize.xy)+floor(_Time.y*30))-.5;
                color+=grain*_Grain*lerp(1,.25,saturate(luma*2));
                color=(color-.5)*1.04+.5;
                color *= _Brightness*(1-_Vignette*radius*.5);
                half4 result=half4(saturate(color),i.color.a);
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
