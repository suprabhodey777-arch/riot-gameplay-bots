// The world's lit surface (built-in pipeline, Standard lighting): albedo
// texture x colour x vertex colour, a procedural normal map, and a per-vertex
// material mask in vertex alpha (1 = metal: _Metallic/_Glossiness, 0 = paint or
// polymer: no metal, _PolyGloss). Merged meshes bake their colours and baked
// occlusion into the vertex colours, so one material draws many parts.
// _VertexAO = 0 ignores vertex colours (for meshes without them). _Weather adds
// world-space tone variation, grime and stains (static map surfaces only).
// No shader keywords, so nothing can be stripped from a player build.
Shader "Saltworks/WorldLit"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo (A = smoothness)", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal strength", Float) = 1
        _Glossiness ("Metal smoothness", Range(0, 1)) = 0.5
        _Metallic ("Metallic", Range(0, 1)) = 0
        _PolyGloss ("Non-metal smoothness", Range(0, 1)) = 0.2
        _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _VertexAO ("Vertex colour weight", Range(0, 1)) = 1
        _Weather ("Weathering (static world surfaces)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;
        // Global tileable noise (Textures.WeatherNoise): r broad blotches, g vertical streaks, b fine.
        sampler2D _SaltNoise;

        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
            float3 worldPos;
            float3 worldNormal;
            INTERNAL_DATA
        };

        fixed4 _Color;
        half _Glossiness;
        half _Metallic;
        half _PolyGloss;
        half _BumpScale;
        fixed4 _EmissionColor;
        half _VertexAO;
        half _Weather;

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 tex = tex2D(_MainTex, IN.uv_MainTex);
            fixed3 vc = lerp(fixed3(1, 1, 1), IN.color.rgb, _VertexAO);
            half metalMask = lerp(1.0, IN.color.a, _VertexAO);
            half3 albedo = tex.rgb * _Color.rgb * vc;
            half rough = 1;
            // Weathering in world space, so no two stretches of wall or floor look the same: broad tone
            // variation, grime rising from the ground and drip streaks on walls, dark stains on floors.
            // (No branches: every sample is taken, then blended by the masks.)
            float3 wn = WorldNormalVector(IN, float3(0, 0, 1));
            float3 an = abs(wn);
            float3 wp = IN.worldPos;
            float2 side = an.x > an.z ? wp.zy : wp.xy;
            half isFloor = step(0.5, an.y);
            float2 muv = lerp(side, wp.xz, isFloor);
            half broad = tex2D(_SaltNoise, muv * 0.043).r;
            half mid = tex2D(_SaltNoise, muv * 0.19 + 0.37).b;
            half tone = lerp(0.78, 1.12, broad) * lerp(0.92, 1.06, mid);
            // Floors: dark stains that are also a little rougher.
            half stain = smoothstep(0.56, 0.78, tex2D(_SaltNoise, muv * 0.071 + 0.61).r * 0.6 + mid * 0.4) * isFloor;
            tone *= 1 - 0.3 * stain;
            rough = 1 - 0.5 * stain * _Weather;
            // Walls: drip streaks from above, dusty grime rising from the ground.
            half wall = (1 - isFloor) * _Weather;
            half streak = tex2D(_SaltNoise, float2(side.x * 0.21, side.y * 0.035)).g;
            half grime = saturate(1 - wp.y / 1.1);
            grime = saturate(grime * grime * (0.45 + broad)) * wall;
            tone *= 1 - 0.34 * smoothstep(0.5, 0.85, streak) * saturate(wp.y * 0.6) * (1 - isFloor);
            half3 dust = half3(0.60, 0.52, 0.42) * dot(albedo, half3(0.4, 0.4, 0.4)) * 1.6;
            albedo = lerp(albedo, dust, grime * 0.55);
            tone *= 1 - 0.3 * grime;
            albedo *= lerp(1, tone, _Weather);
            o.Albedo = albedo;
            float3 n = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            n.xy *= _BumpScale;
            o.Normal = normalize(n);
            o.Metallic = _Metallic * metalMask;
            o.Smoothness = lerp(_PolyGloss, _Glossiness, metalMask) * tex.a * rough;
            o.Emission = _EmissionColor.rgb;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
