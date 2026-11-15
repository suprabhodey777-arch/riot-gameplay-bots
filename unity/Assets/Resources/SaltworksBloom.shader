// A cheap bloom for OnRenderImage (Saltworks.Glow): a soft-knee bright pass at
// half resolution, a short box-filtered mip chain down and back up, and a
// composite. Pass 0 prefilter, 1 downsample, 2 upsample (lower level + this
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

    half4 fragFinal (v2f i) : SV_Target
    {
        half4 c = tex2D(_MainTex, i.uv);
        float2 uvb = i.uv;
        #if UNITY_UV_STARTS_AT_TOP
        if (_MainTex_TexelSize.y < 0) uvb.y = 1 - uvb.y;
        #endif
        c.rgb += tex2D(_BloomTex, uvb).rgb * _Params.z;
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
    }
    Fallback Off
}
