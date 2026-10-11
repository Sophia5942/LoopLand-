// Soda stream from the nozzle: unlit, flowing bands in world space (so stretching the stream doesn't stretch them).
// _Color is set per slot by DrinkSlot (MaterialPropertyBlock).
Shader "LoopLand/Drinks/Stream"
{
    Properties
    {
        _Color ("Color", Color) = (0.3, 0.12, 0.04, 0.9)
        _FlowSpeed ("Flow Speed", Float) = 3
        _BandScale ("Band Scale", Float) = 40
    }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        ZWrite Off
        Cull Back
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float _FlowSpeed, _BandScale;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
                float3 wnormal : TEXCOORD1;
                float3 vdir : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wnormal = UnityObjectToWorldNormal(v.normal);
                o.vdir = _WorldSpaceCameraPos - o.wpos;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float band = frac(i.wpos.y * _BandScale + _Time.y * _FlowSpeed);
                float shine = smoothstep(0.35, 0.5, band) * (1.0 - smoothstep(0.5, 0.65, band));
                float rim = 1.0 - saturate(dot(normalize(i.wnormal), normalize(i.vdir)));
                fixed4 c = _Color;
                c.rgb = c.rgb * (0.85 + 0.35 * shine) + rim * rim * 0.25;
                c.a = saturate(_Color.a + rim * 0.2);
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}
