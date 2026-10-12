Shader "Game/UI/Hud Incandescence"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Glow ("Halo strength", Range(0,1)) = .24
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 local:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;
            float4 _Color, _ClipRect;
            float _Glow;
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.local=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color*_Color; return o;
            }
            half4 frag(v2f i):SV_Target
            {
                float2 pixel=max(fwidth(i.uv),.000001);
                float core, halo;
                core=tex2D(_MainTex,i.uv).a;
                // White equipment icon glow stays separate from menu filament lighting.
                float2 dx=float2(pixel.x*2,0), dy=float2(0,pixel.y*2);
                halo=(tex2D(_MainTex,i.uv+dx).a+tex2D(_MainTex,i.uv-dx).a
                    +tex2D(_MainTex,i.uv+dy).a+tex2D(_MainTex,i.uv-dy).a)*.25;
                halo=max(0,halo-core)*_Glow;
                float opacity=i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                opacity*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                float coverage=saturate(core+halo*.4)*opacity;
                #ifdef UNITY_UI_ALPHACLIP
                clip(coverage-.001);
                #endif
                return half4(i.color.rgb*(core+halo)*opacity,coverage);
            }
            ENDCG
        }
    }
}