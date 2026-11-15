// The world's lit surface (built-in pipeline, Standard lighting): albedo
// texture x colour x vertex colour, a procedural normal map, and a per-vertex
// material mask in vertex alpha (1 = metal: _Metallic/_Glossiness, 0 = paint or
// polymer: no metal, _PolyGloss). Merged meshes bake their colours and baked
// occlusion into the vertex colours, so one material draws many parts.
// _VertexAO = 0 ignores vertex colours (for meshes without them).
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

        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
        };

        fixed4 _Color;
        half _Glossiness;
        half _Metallic;
        half _PolyGloss;
        half _BumpScale;
        fixed4 _EmissionColor;
        half _VertexAO;

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 tex = tex2D(_MainTex, IN.uv_MainTex);
            fixed3 vc = lerp(fixed3(1, 1, 1), IN.color.rgb, _VertexAO);
            half metalMask = lerp(1.0, IN.color.a, _VertexAO);
            o.Albedo = tex.rgb * _Color.rgb * vc;
            float3 n = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            n.xy *= _BumpScale;
            o.Normal = normalize(n);
            o.Metallic = _Metallic * metalMask;
            o.Smoothness = lerp(_PolyGloss, _Glossiness, metalMask) * tex.a;
            o.Emission = _EmissionColor.rgb;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
