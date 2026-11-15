// Two layers of drifting clouds on a dome around the camera, projected as if
// on a flat sky ceiling so they thin out toward the horizon. Drawn after the
// skybox (queue 2550) with depth test, so the map always hides it. No fog.
Shader "Saltworks/SkyClouds"
{
    Properties
    {
        _MainTex ("Cloud noise", 2D) = "white" {}
        _Color ("Lit colour", Color) = (1,1,1,1)
        _Shade ("Shadow colour", Color) = (0.6,0.65,0.75,1)
        _Cover ("Cover", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent-450" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _Shade;
            float _Cover;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = max(d.y, 0.0);
                float2 uv = d.xz / (y + 0.2);
                float t = _Time.x;
                float n1 = tex2D(_MainTex, uv * 0.16 + float2(t * 0.5, t * 0.17)).r;
                float n2 = tex2D(_MainTex, uv * 0.41 + float2(-t * 0.8, t * 0.4)).r;
                float n = n1 * 0.68 + n2 * 0.32;
                float a = smoothstep(1.0 - _Cover, 1.0 - _Cover + 0.32, n);
                float horizon = smoothstep(0.02, 0.3, y);
                fixed3 col = lerp(_Shade.rgb, _Color.rgb, saturate((n2 - 0.25) * 1.6));
                return fixed4(col, a * horizon * _Color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
