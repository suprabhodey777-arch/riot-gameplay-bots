// Character shading (built-in pipeline): a soft two-tone toon ramp for the sun
// with a cool tinted shadow side, plain falloff for point lights, a banded
// specular masked by vertex alpha (0 cloth, ~0.4 leather, 1 metal) and a rim
// light so figures separate from the map. Vertex colour RGB is the albedo; the
// texture adds a faint fabric grain. No keywords of its own, so nothing is
// stripped from a player build. Shadow casting comes from the fallback.
Shader "Saltworks/HeroToon"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _MainTex ("Grain", 2D) = "white" {}
        _ShadeColor ("Shadow side tint", Color) = (0.55, 0.58, 0.72, 1)
        _ShadeLift ("Shadow side lift", Range(0, 1)) = 0.28
        _Band ("Terminator softness", Range(0.01, 0.6)) = 0.16
        _RimColor ("Rim colour", Color) = (0.9, 0.95, 1, 1)
        _Rim ("Rim strength", Range(0, 2)) = 0.42
        _RimPower ("Rim power", Range(0.5, 8)) = 3.2
        _Spec ("Specular", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Toon fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        fixed4 _ShadeColor;
        fixed4 _RimColor;
        half _ShadeLift;
        half _Band;
        half _Rim;
        half _RimPower;
        half _Spec;

        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
            float3 viewDir;
        };

        half4 LightingToon (SurfaceOutput s, half3 lightDir, half3 viewDir, half atten)
        {
            half ndl = dot(s.Normal, lightDir);
            half lit = smoothstep(-_Band, _Band, ndl);
            // Directional light (the sun): the shadow side keeps a cool tinted lift. Point lights just fall off.
            half sunLight = _WorldSpaceLightPos0.w < 0.5 ? 1.0 : 0.0;
            half k = lit * atten;
            half3 shade = _ShadeColor.rgb * _ShadeLift * sunLight * (1.0 - k);
            half3 h = normalize(lightDir + viewDir);
            half sp = pow(saturate(dot(s.Normal, h)), 40.0);
            sp = smoothstep(0.3, 0.7, sp) * s.Specular * _Spec * k;
            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (k + shade) + _LightColor0.rgb * sp * 0.5;
            c.a = s.Alpha;
            return c;
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 g = tex2D(_MainTex, IN.uv_MainTex);
            fixed3 alb = IN.color.rgb * _Color.rgb * lerp(1.0, g.rgb, 0.45);
            o.Albedo = alb;
            o.Specular = IN.color.a;
            o.Gloss = 1;
            o.Normal = float3(0, 0, 1);
            half rim = 1.0 - saturate(dot(normalize(IN.viewDir), o.Normal));
            rim = pow(rim, _RimPower) * _Rim;
            o.Emission = rim * (_RimColor.rgb * 0.55 + alb * 0.6);
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
