// Heat haze over the spawn barriers: the scene behind (one GrabPass per frame,
// shared by every barrier) offset by rising sine ripples, strongest mid-height.
// Only drawn on the High and Ultra presets (WorldFx turns it off below).
Shader "Saltworks/HeatHaze"
{
    Properties
    {
        _Strength ("Strength", Float) = 0.012
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        GrabPass { "_SaltworksHazeGrab" }
        Pass
        {
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _SaltworksHazeGrab;
            float _Strength;
            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 grab : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.grab = ComputeGrabScreenPos(o.pos);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y;
                float fade = sin(saturate(i.uv.y) * 3.14159);
                float2 w = float2(sin(i.uv.y * 38.0 - t * 5.5 + i.uv.x * 60.0), cos(i.uv.y * 27.0 - t * 4.2 + i.uv.x * 41.0));
                float4 g = i.grab;
                g.xy += w * _Strength * fade * g.w;
                fixed4 c = tex2Dproj(_SaltworksHazeGrab, g);
                c.rgb *= _Color.rgb;
                c.a = fade * _Color.a;
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
