// Lightweight liquid for VRChat PC + Quest (no GrabPass, no geometry shader, single pass stereo / multiview safe).
// Same property names as the LiquidGlass shader, so DrinkCup drives either one.
// The script sets _FillHeight (world Y of the surface) and _GlassRemap (world min/max Y of the glass).
// Everything above the surface is clipped. The inside faces are drawn in the foam color, which reads as the flat top.
Shader "LoopLand/Drinks/Liquid"
{
    Properties
    {
        _ColorMain ("Main Color", Color) = (0.18, 0.07, 0.02, 1)
        _FoamColor ("Foam / Surface Color", Color) = (0.79, 0.64, 0.48, 1)
        _EdgeColor ("Edge Color", Color) = (0.42, 0.21, 0.09, 1)
        _Transparency ("Transparency", Range(0, 1)) = 0.1
        _FoamHeight ("Foam Height (fraction of glass)", Range(0, 0.5)) = 0.05
        _FoamLineSmoothness ("Foam Line Smoothness", Range(0, 0.2)) = 0.03
        _EdgeStrength ("Edge Strength", Range(0, 4)) = 1
        _EdgePower ("Edge Power", Range(0.5, 8)) = 3
        [HideInInspector] _FillHeight ("Fill Height (world Y)", Float) = -1000
        [HideInInspector] _GlassRemap ("Glass Min/Max Y", Vector) = (0, 1, 0, 0)
        [HideInInspector] _WobbleX ("Wobble X", Float) = 0
        [HideInInspector] _WobbleZ ("Wobble Z", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        CGINCLUDE
        #include "UnityCG.cginc"
        fixed4 _ColorMain, _FoamColor, _EdgeColor;
        float4 _GlassRemap;
        float _Transparency, _FoamHeight, _FoamLineSmoothness, _EdgeStrength, _EdgePower;
        float _FillHeight, _WobbleX, _WobbleZ;

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
        float Surface (float3 wpos)
        {
            float3 pivot = unity_ObjectToWorld._m03_m13_m23;
            return _FillHeight + (wpos.x - pivot.x) * _WobbleX + (wpos.z - pivot.z) * _WobbleZ;
        }
        float Alpha () { return lerp(1.0, 0.25, _Transparency); }
        ENDCG

        // Inside faces = the liquid surface
        Pass
        {
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target
            {
                clip(Surface(i.wpos) - i.wpos.y);
                fixed4 c = lerp(_ColorMain, _FoamColor, saturate(0.35 + _FoamHeight * 8.0));
                c.a = saturate(Alpha() + 0.15);
                return c;
            }
            ENDCG
        }
        // Outside faces = the body, foam band and edge glow
        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target
            {
                float h = Surface(i.wpos);
                clip(h - i.wpos.y);
                float range = max(_GlassRemap.y - _GlassRemap.x, 0.0001);
                float foamStart = h - _FoamHeight * range;
                float soft = max(_FoamLineSmoothness * range * 0.5, 0.00001);
                float foam = smoothstep(foamStart - soft, foamStart + soft, i.wpos.y);
                fixed4 c = lerp(_ColorMain, _FoamColor, foam);
                float3 n = normalize(i.wnormal);
                float3 v = normalize(i.vdir);
                float rim = pow(1.0 - saturate(dot(n, v)), _EdgePower) * _EdgeStrength;
                c.rgb = c.rgb * (0.8 + 0.2 * saturate(n.y * 0.5 + 0.5)) + _EdgeColor.rgb * rim;
                c.a = saturate(Alpha() + rim * 0.3);
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}
