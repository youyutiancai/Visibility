Shader "Custom/Pure_color"
{
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 300

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                SHADOW_COORDS(1)
            };

            fixed4 _Color;
            float _Cutoff;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                TRANSFER_SHADOW(o)
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = _Color;
                clip(col.a - _Cutoff);
                return _Color;

                float3 normal = normalize(i.worldNormal);
                float nl = max(0, dot(normal, _WorldSpaceLightPos0.xyz));
                float3 diffuse = nl * _LightColor0.rgb;
                float3 ambient = ShadeSH9(float4(normal, 1));

                float atten = SHADOW_ATTENUATION(i);
                col.rgb *= (diffuse * atten + ambient);
                return col;
            }
            ENDCG
        }

        // UsePass "Legacy Shaders/VertexLit/SHADOWCASTER"
    }
}
