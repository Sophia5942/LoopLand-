// Clear plastic cup for VRChat PC + Quest: unlit tint with a fresnel rim and a soft fake highlight.
Shader "LoopLand/Drinks/Cup"
{
    Properties
    {
        _Color ("Tint (alpha = how see-through)", Color) = (0.92, 0.96, 1, 0.12)
        _RimColor ("Rim (alpha = rim opacity)", Color) = (1, 1, 1, 0.65)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _Highlight ("Highlight", Range(0, 1)) = 0.35
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
            fixed4 _Color, _RimColor;
            float _RimPower, _Highlight;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wnormal : TEXCOORD0;
                float3 vdir : TEXCOORD1;
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
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}
