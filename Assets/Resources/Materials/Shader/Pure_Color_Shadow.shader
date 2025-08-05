Shader "Custom/Pure_Color_Shadow"
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
                float4 positionWS : TEXCOORD1;  // World position
            };

            fixed4 _Color;
            float _Cutoff;
            sampler2D _CustomShadowMap;
            float4x4 _LightViewProjection;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);

                // Compute the world position here
                o.positionWS = mul(unity_ObjectToWorld, v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = _Color;
                clip(col.a - _Cutoff);

                // Diffuse lighting calculation
                float3 normal = normalize(i.worldNormal);
                float nl = max(0, dot(normal, _WorldSpaceLightPos0.xyz)); // For directional lights 
                float3 diffuse = nl * _LightColor0;  // Diffuse lighting

                // Ambient lighting calculation
                float3 ambient = ShadeSH9(float4(normal, 1)); // Precomputed ambient lighting

                // Shadow calculation (assuming you have your shadow setup correct)
                float bias = 0.005f;
                float4 shadowCoord = mul(_LightViewProjection, i.positionWS);
                shadowCoord.xyz /= shadowCoord.w;
                shadowCoord.xyz = shadowCoord.xyz * 0.5 + 0.5;
                float depth = tex2D(_CustomShadowMap, shadowCoord.xy).r;
                fixed shadow = (shadowCoord.z - bias) <= depth ? 1.0 : 0.0;

                // Apply lighting and shadow to color
                col.rgb *= (diffuse * shadow + ambient);

                return col;
            }
            ENDCG
        }
    }
}
