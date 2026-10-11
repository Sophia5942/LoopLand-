// Clear glass for VRChat PC + Quest: unlit tint with a fresnel rim, a soft fake highlight and a logo.
// Also used as the logo decal on your own glasses (tint and rim alpha 0, logo rect = the whole decal).
// _Color (tint) and _LogoColor are set per cup by DrinkCup (MaterialPropertyBlock). Logo alpha 0 = no logo.
Shader "DrinkSystem/Cup"
{
    Properties
    {
        _Color ("Tint (alpha = how see-through)", Color) = (0.92, 0.96, 1, 0.12)
        _RimColor ("Rim (alpha = rim opacity)", Color) = (1, 1, 1, 0.65)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _Highlight ("Highlight", Range(0, 1)) = 0.35
        _LogoTex ("Logo (transparent background)", 2D) = "black" {}
        _LogoColor ("Logo Color (white = original colours)", Color) = (1, 1, 1, 1)
        _LogoRect ("Logo Rect on the cup (U center, V center, U size, V size)", Vector) = (0.25, 0.5, 0.16, 0.42)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        ZWrite Off
        Cull Back
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color, _RimColor, _LogoColor;
            float4 _LogoRect;
            float _RimPower, _Highlight;
            sampler2D _LogoTex;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wnormal : TEXCOORD0;
                float3 vdir : TEXCOORD1;
                float2 luv : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wnormal = UnityObjectToWorldNormal(v.normal);
                o.vdir = _WorldSpaceCameraPos - mul(unity_ObjectToWorld, v.vertex).xyz;
                o.luv = (v.uv - _LogoRect.xy) / _LogoRect.zw + 0.5;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.wnormal);
                float3 v = normalize(i.vdir);
                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
                float3 l = normalize(float3(0.4, 0.8, 0.3));
                float spec = pow(saturate(dot(reflect(-l, n), v)), 24.0) * _Highlight;
                fixed4 c = _Color;
                c.rgb += _RimColor.rgb * rim * 0.5 + spec;
                c.a = saturate(_Color.a + rim * _RimColor.a + spec);
                float inside = step(0.0, i.luv.x) * step(i.luv.x, 1.0) * step(0.0, i.luv.y) * step(i.luv.y, 1.0);
                fixed4 t = tex2D(_LogoTex, saturate(i.luv));
                float logo = t.a * inside * _LogoColor.a;
                // white _LogoColor = the logo's own colours; any other colour tints its shading (gold -> silver)
                float lum = dot(t.rgb, float3(0.3, 0.59, 0.11));
                float tintAmount = saturate(distance(_LogoColor.rgb, float3(1, 1, 1)) * 4.0);
                float3 logoRgb = lerp(t.rgb, lum * 1.35 * _LogoColor.rgb, tintAmount);
                c.rgb = lerp(c.rgb, logoRgb, logo);
                c.a = max(c.a, logo);
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}
