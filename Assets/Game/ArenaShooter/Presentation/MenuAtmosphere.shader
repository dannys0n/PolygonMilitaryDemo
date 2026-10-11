Shader "Game/UI/Menu Atmosphere"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Enum(Atmosphere,0,Marker,1,Spill,2)] _Mode ("Effect", Float) = 0
        _DustTex ("Dust Texture", 2D) = "black" {}
        _DustStrength ("Dust Visibility", Range(0,4)) = 0.8
        _LightRadius ("Filament Dust Reach", Range(0.01,0.5)) = 0.2
        _LightStrength ("Filament Dust Brightness", Range(0,1)) = 0.12
        _Warmth ("Light Color", Color) = (0.9,0.72,0.5,1)
        _Intensity ("Marker Brightness", Range(0,3)) = 1
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
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="False" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One One
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
            sampler2D _MainTex, _DustTex;
            float4 _Color, _ClipRect, _Warmth;
            float _Mode, _DustStrength, _Intensity, _LightRadius, _LightStrength;
            float4 _Filaments[6];
            float _FilamentStrengths[6];

            // Shared by the home marker and both Settings selection lines.
            float filamentEnergy(float distance, float pixelWidth)
            {
                float core=exp(-pow(distance/max(.045,pixelWidth*.65),2));
                float innerHalo=exp(-pow(distance/.14,2));
                float halo=exp(-pow(distance/.3,2));
                return (core*18+innerHalo*1.1+halo*.25)*_Intensity;
            }
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.local=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color*_Color; return o;
            }
            half4 frag(v2f i):SV_Target
            {
                float energy;
                if (_Mode < .5)
                {
                    float aspect=_ScreenParams.x/_ScreenParams.y;
                    // Static texture, exposed only by the currently visible filaments.
                    float dust=tex2D(_DustTex,i.uv).r;
                    float2 position=float2(i.uv.x*aspect,i.uv.y);
                    float illumination=0;
                    [unroll] for(int n=0;n<6;n++)
                    {
                        float2 start=_Filaments[n].xy*float2(aspect,1);
                        float2 end=_Filaments[n].zw*float2(aspect,1);
                        float2 segment=end-start;
                        float t=saturate(dot(position-start,segment)/max(dot(segment,segment),.000001));
                        float2 offset=position-(start+segment*t);
                        illumination+=exp(-dot(offset,offset)*3/(_LightRadius*_LightRadius))*_FilamentStrengths[n];
                    }
                    energy=illumination*_LightStrength*(.025+pow(max(dust,0),.7)*_DustStrength);
                }
                else if (_Mode < 1.5)
                {
                    float x=i.uv.x-.5;
                    float ends=smoothstep(0,.18,i.uv.y)*(1-smoothstep(.82,1,i.uv.y));
                    energy=filamentEnergy(x,fwidth(i.uv.x))*ends*smoothstep(0,.1,i.uv.x)*(1-smoothstep(.9,1,i.uv.x));
                }
                else
                {
                    float ends=smoothstep(0,.2,i.uv.y)*(1-smoothstep(.8,1,i.uv.y));
                    energy=exp(-i.uv.x*5)*smoothstep(0,.1,i.uv.x)*ends*.35*_Intensity;
                }
                float3 light=1-exp(-energy*_Warmth.rgb);
                float opacity=i.color.a*tex2D(_MainTex,i.uv).a;
                #ifdef UNITY_UI_CLIP_RECT
                opacity*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(opacity-.001);
                #endif
                return half4(light*i.color.rgb*opacity,0);
            }
            ENDCG
        }
    }
}
