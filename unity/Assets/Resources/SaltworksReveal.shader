// Silhouette for enemies revealed by recon: a coloured unlit shell drawn with
// ZTest Always, so it shows through walls. Bright at the rim, faint in the
// middle, which reads as an outline. Lives in Resources so it is included in
// player builds.
Shader "Saltworks/RevealThroughWalls"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.3, 0.4, 0.9)
        _RimPower ("Rim Power", Range(0.5, 6)) = 2.2
        _Fill ("Fill", Range(0, 1)) = 0.22
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Lighting Off
        Cull Back
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            fixed4 _Color;
            half _RimPower;
            half _Fill;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = WorldSpaceViewDir(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                half rim = 1.0 - saturate(dot(normalize(i.normal), normalize(i.viewDir)));
                half a = saturate(_Fill + pow(rim, _RimPower) * (1.0 - _Fill));
                return fixed4(_Color.rgb, _Color.a * a);
            }
            ENDCG
        }
    }
    Fallback Off
}
