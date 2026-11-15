// The sky: a gradient from the zenith to a warm horizon, a glow around the sun
// that is strongest near the horizon, a soft sun disc, and the haze colour
// below the horizon so distant ground fades into it. WorldLook sets the colours
// and the sun direction per palette. No keywords, one cheap pass.
Shader "Saltworks/Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.3, 0.45, 0.7, 1)
        _Horizon ("Horizon", Color) = (0.95, 0.75, 0.55, 1)
        _Ground ("Below horizon", Color) = (0.6, 0.5, 0.45, 1)
        _SunColor ("Sun colour", Color) = (1, 0.8, 0.55, 1)
        _SunDir ("Direction to the sun", Vector) = (0, 0.5, 0.86, 0)
        _SunGlow ("Sun glow", Range(0, 2)) = 0.6
        _SunDisc ("Sun disc brightness", Range(0, 8)) = 3
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Zenith, _Horizon, _Ground, _SunColor;
            float4 _SunDir;
            half _SunGlow, _SunDisc;

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

            half4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 sun = normalize(_SunDir.xyz);
                float up = saturate(d.y);
                // Most of the colour change happens in the lowest quarter of the sky.
                half h = pow(1 - up, 4);
                half3 col = lerp(_Zenith.rgb, _Horizon.rgb, h);
                float sd = saturate(dot(d, sun));
                // A wide warm wash on the sun's side of the horizon, a tighter halo, then the disc.
                half wash = pow(sd, 3) * pow(1 - up, 2);
                half halo = pow(sd, 48);
                col += _SunColor.rgb * (wash * 0.45 + halo * 0.5) * _SunGlow;
                col += _SunColor.rgb * smoothstep(0.9990, 0.9996, sd) * _SunDisc;
                // Below the horizon: the haze colour, so the far ground and the sky meet without a line.
                half below = smoothstep(0.0, -0.06, d.y);
                col = lerp(col, _Ground.rgb, below);
                return half4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
