Shader "Hidden/WriteLinearDepth"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
            };
            struct v2f {
                float4 pos   : SV_POSITION;
                float  zEye  : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float4 world = mul(unity_ObjectToWorld, v.vertex);
                float3 view  = mul(UNITY_MATRIX_V, float4(world.xyz,1)).xyz;
                o.zEye = -view.z;                     // eye-space depth (>0 in front)
                o.pos  = mul(UNITY_MATRIX_VP, float4(world.xyz,1));
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                // If you prefer 0..1 linearized, divide by _ProjectionParams.z (far plane).
                return float4(i.zEye, 0, 0, 0);
            }
            ENDCG
        }
    }
    FallBack Off
}
