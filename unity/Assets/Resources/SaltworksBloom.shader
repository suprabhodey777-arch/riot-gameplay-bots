// A cheap bloom for OnRenderImage (Saltworks.Glow): a soft-knee bright pass at
// half resolution, a short box-filtered mip chain down and back up, and a
// composite, plus the tonemap and grade. Pass 4 is tonemap and grade alone. Pass 0 prefilter, 1 downsample, 2 upsample (lower level + this
// level into a new target), 3 composite (source + _BloomTex x intensity).
Shader "Hidden/Saltworks/Bloom"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BloomTex;
    // x threshold, y soft knee, z intensity
    float4 _Params;
    // Colour grading globals (Saltworks.WorldLook): x saturation, y contrast, z vignette, w on.
    float4 _SaltGrade;
    float4 _SaltGradeLift;
    float4 _SaltGradeGain;

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    v2f vert (appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        return o;
    }

    half3 Box4(float2 uv, float d)
    {
        float4 o = _MainTex_TexelSize.xyxy * float4(-d, -d, d, d);
        half3 s = tex2D(_MainTex, uv + o.xy).rgb + tex2D(_MainTex, uv + o.zy).rgb
                + tex2D(_MainTex, uv + o.xw).rgb + tex2D(_MainTex, uv + o.zw).rgb;
        return s * 0.25;
    }

    half3 Prefilter(half3 c)
    {
        half br = max(c.r, max(c.g, c.b));
        half knee = max(_Params.y, 1e-4);
        half soft = clamp(br - _Params.x + knee, 0, 2 * knee);
        soft = soft * soft / (4 * knee + 1e-4);
        half contrib = max(soft, br - _Params.x) / max(br, 1e-4);
        return c * contrib;
    }

    half4 fragPre (v2f i) : SV_Target { return half4(Prefilter(Box4(i.uv, 1)), 1); }
    half4 fragDown (v2f i) : SV_Target { return half4(Box4(i.uv, 1), 1); }
    // The lower level (_MainTex, tent-ish 4 taps) plus this level (_BloomTex), into a fresh target.
    half4 fragUp (v2f i) : SV_Target { return half4(Box4(i.uv, 0.5) + tex2D(_BloomTex, i.uv).rgb, 1); }

    // Filmic tone curve (the project renders in gamma space): straight up to the knee, then a soft
    // shoulder that approaches 1, so sunlit floors and sky keep their colour instead of clipping to white.
    half3 Tone(half3 x)
    {
        const half knee = 0.55;
        half3 over = max(x - knee, 0);
        half3 hi = knee + (1 - knee) * (1 - exp(-over / (1 - knee)));
        return lerp(x, hi, step(knee, x));
    }

    // Tonemap (always), then the per-palette grade when it is on (w): lift tints the shadows, gain the
    // highlights, then saturation, a gentle contrast and a static vignette. One pass, no extra samples.
    half3 Grade(half3 c, float2 uv)
    {
        half3 col = Tone(max(c, 0) * _SaltGradeGain.a);
        if (_SaltGrade.w > 0.5)
        {
            col = col * _SaltGradeGain.rgb + _SaltGradeLift.rgb * (1 - saturate(col));
            half l = dot(col, half3(0.2126, 0.7152, 0.0722));
            col = lerp(l.xxx, col, _SaltGrade.x);
            col = max((col - 0.4) * _SaltGrade.y + 0.4, 0);
            float2 d = uv - 0.5;
            col *= 1 - _SaltGrade.z * dot(d, d) * 1.6;
        }
        return col;
    }

    half4 fragFinal (v2f i) : SV_Target
    {
        half4 c = tex2D(_MainTex, i.uv);
        float2 uvb = i.uv;
        #if UNITY_UV_STARTS_AT_TOP
        if (_MainTex_TexelSize.y < 0) uvb.y = 1 - uvb.y;
        #endif
        c.rgb += tex2D(_BloomTex, uvb).rgb * _Params.z;
        c.rgb = Grade(c.rgb, i.uv);
        return c;
    }

    // The whole look in one full-screen pass with one texture fetch: used on every preset without bloom.
    half4 fragGrade (v2f i) : SV_Target
    {
        half4 c = tex2D(_MainTex, i.uv);
        c.rgb = Grade(c.rgb, i.uv);
        return c;
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragPre
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDown
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragUp
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragFinal
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragGrade
            ENDCG
        }
    }
    Fallback Off
}
