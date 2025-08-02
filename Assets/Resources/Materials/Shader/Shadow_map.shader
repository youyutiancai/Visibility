Shader "Custom/Shadow_map"
{
    Properties
    {
    }

    SubShader{
        LOD 300
        Pass {
            Tags {"LightMode" = "ForwardBase"}
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight 
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "HLSLSupport.cginc"
            #include "UnityShadowLibrary.cginc"
            #include "AutoLight.cginc"

            sampler2D _MainTex;
            sampler2D _CustomShadowMap;
            float4x4 _LightViewProjection;
            float _Cutoff;

            struct Interpolators {
                float2 uv : TEXCOORD0;
                float4 pos : SV_POSITION;
                float4 positionWS : TEXCOORD5;
            };
                
                
            Interpolators vert(appdata_base v)
            {
                Interpolators output;
                output.uv = v.texcoord;
                output.positionWS = mul(unity_ObjectToWorld, v.vertex);
                output.pos = UnityObjectToClipPos(v.vertex);
                return output;
            }


            float4 frag(Interpolators i) : SV_Target{
                // float4 col = tex2D(_MainTex, i.uv);
                // clip(col.a - _Cutoff);

                float bias = 0;
                float4 shadowCoord = mul(_LightViewProjection, i.positionWS);
                shadowCoord.xyz /= shadowCoord.w;
                shadowCoord.xyz = shadowCoord.xyz * 0.5 + 0.5;
                //float depth = UNITY_SAMPLE_SHADOW(_ShadowMapTexture, i._ShadowCoord.xyz);
                //float depth = tex2D(_CustomShadowMap, shadowCoord.xy).r;
                //float shadow = (shadowCoord.z - bias) <= depth ? 1.0 : 0.0;
                return float4(shadowCoord.z, 0, 0, 1);
            }
            ENDCG
        }
    }
}
