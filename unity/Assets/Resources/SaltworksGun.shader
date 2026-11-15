// First-person weapons, gloves and the guns bots carry: gunmetal and polymer
// with real highlights. Vertex colour RGB is the albedo and A the material
// mask (1 = metal, 0 = polymer, paint or cloth), as in Saltworks/WorldLit.
// Lighting is done here rather than by the Standard model so the gun always
// reads well whatever the player is standing in: a wrapped key light from the
// sun, the scene's three-colour ambient, a specular highlight, a soft "studio"
// reflection strip from above (so bevels and barrels catch a line of light),
// and a cool rim. The result is tone-mapped in the shader because the weapon
// camera draws after the full-screen grade. Point lights (the muzzle flash)
// add in a second pass. No keywords, no textures required.
Shader "Saltworks/Gun"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _MainTex ("Grain (A = smoothness variation)", 2D) = "white" {}
        _MetalGloss ("Metal smoothness", Range(0, 1)) = 0.72
        _PolyGloss ("Non-metal smoothness", Range(0, 1)) = 0.38
        _Rim ("Rim strength", Range(0, 2)) = 0.55
        _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "UnityLightingCommon.cginc"

    sampler2D _MainTex;
    float4 _MainTex_ST;
    fixed4 _Color;
    half _MetalGloss, _PolyGloss, _Rim;
    fixed4 _EmissionColor;

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 n : TEXCOORD1;
        float3 wp : TEXCOORD2;
        fixed4 col : COLOR;
    };

    v2f vert (appdata_full v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
        o.n = UnityObjectToWorldNormal(v.normal);
        o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
        o.col = v.color;
        return o;
    }

    half3 Tone(half3 x)
    {
        const half knee = 0.55;
        half3 over = max(x - knee, 0);
        half3 hi = knee + (1 - knee) * (1 - exp(-over / (1 - knee)));
        return lerp(x, hi, step(knee, x));
    }

    half3 Ambient3(float3 n)
    {
        half3 sky = unity_AmbientSky.rgb, eq = unity_AmbientEquator.rgb, gr = unity_AmbientGround.rgb;
        return n.y > 0 ? lerp(eq, sky, n.y) : lerp(eq, gr, -n.y);
    }

    half4 fragBase (v2f i) : SV_Target
    {
        half4 g = tex2D(_MainTex, i.uv);
        half3 albedo = i.col.rgb * _Color.rgb * g.rgb;
        half metal = i.col.a;
        half smooth = lerp(_PolyGloss, _MetalGloss, metal) * g.a;
        float3 n = normalize(i.n);
        float3 v = normalize(_WorldSpaceCameraPos - i.wp);
        float3 l = normalize(_WorldSpaceLightPos0.xyz);
        float3 hv = normalize(l + v);
        half nl = dot(n, l);
        half nv = saturate(dot(n, v));
        half3 sun = _LightColor0.rgb;

        // Diffuse: metals keep only a little; a wrapped key so the shaded side still has form.
        half wrap = saturate(nl * 0.7 + 0.3);
        half3 diff = albedo * (1 - metal * 0.6) * (Ambient3(n) * 1.05 + sun * wrap * wrap);

        // Specular: tight and tinted on metal, broad and grey on polymer.
        half3 f0 = lerp(half3(0.04, 0.04, 0.04), albedo * 1.1 + 0.1, metal);
        half power = exp2(2 + smooth * 9);
        half spec = pow(saturate(dot(n, hv)), power) * (power * 0.04 + 0.25) * saturate(nl * 4);
        half fres = pow(1 - nv, 5);
        half3 f = f0 + (1 - f0) * fres * smooth * 0.6;

        // A soft overhead light strip in the reflection: gives every bevel and barrel a highlight line.
        float3 r = reflect(-v, n);
        half strip = smoothstep(0.55, 0.72, r.y) * (1 - smoothstep(0.84, 0.97, r.y));
        half3 env = Ambient3(r) * 0.8 + (unity_AmbientSky.rgb * 0.7 + sun * 0.3) * strip * lerp(0.4, 1.3, smooth);
        half3 col = diff + f * (env + sun * spec);

        // Rim from the sky side.
        half rim = pow(1 - nv, 3) * saturate(n.y * 0.6 + 0.6);
        col += (unity_AmbientSky.rgb + sun * 0.25) * rim * _Rim * lerp(0.5, 1, smooth);
        col += _EmissionColor.rgb;
        return half4(Tone(col), 1);
    }

    half4 fragAdd (v2f i) : SV_Target
    {
        half3 albedo = i.col.rgb * _Color.rgb;
        float3 n = normalize(i.n);
        float3 toL = _WorldSpaceLightPos0.xyz - i.wp * _WorldSpaceLightPos0.w;
        float d2 = dot(toL, toL);
        float3 l = toL * rsqrt(max(d2, 1e-6));
        half atten = _WorldSpaceLightPos0.w > 0.5 ? 1 / (1 + d2 * 6) : 0;
        half nl = saturate(dot(n, l) * 0.6 + 0.4);
        return half4((albedo + 0.06) * _LightColor0.rgb * nl * atten, 1);
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragBase
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            Tags { "LightMode"="ForwardAdd" }
            Blend One One
            ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragAdd
            #pragma target 3.0
            ENDCG
        }
    }
    FallBack "Diffuse"
}
