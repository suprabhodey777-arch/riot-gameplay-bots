// Damage post effect: red and blue split outward toward the screen edges, with
// a red edge tint. One full-screen pass, only while DamageFx is enabled.
Shader "Hidden/Saltworks/DamageChroma"
{
    Properties
    {
        _MainTex ("", 2D) = "white" {}
        _Amount ("Amount", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Amount;

            fixed4 frag (v2f_img i) : SV_Target
            {
                float2 d = i.uv - 0.5;
                float r2 = dot(d, d);
                float2 off = d * r2 * _Amount * 0.09;
                fixed4 c = tex2D(_MainTex, i.uv);
                c.r = tex2D(_MainTex, i.uv + off).r;
                c.b = tex2D(_MainTex, i.uv - off).b;
                float edge = saturate(r2 * 2.4) * _Amount;
                c.rgb = lerp(c.rgb, c.rgb * float3(1.3, 0.5, 0.5), edge * 0.55);
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
