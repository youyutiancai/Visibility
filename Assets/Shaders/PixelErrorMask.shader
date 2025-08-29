Shader "Hidden/PixelErrorMask"
{
    Properties
    {
        _GT  ("Ground Truth Depth", 2D) = "white" {}
        _RCV ("Received Depth",    2D) = "white" {}
        _Eps ("Epsilon tolerance", Float) = 0.0001
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _GT;
            sampler2D _RCV;
            float _Eps;

            struct appdata {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };
            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float dGT  = tex2D(_GT,  i.uv).r;
                float dRCV = tex2D(_RCV, i.uv).r;

                // Ignore skybox / background (depth = 1 in Linear01)
                if (dGT >= 0.999 && dRCV >= 0.999)
                    return 0;

                // Wrong if difference exceeds epsilon
                float wrong = (abs(dGT - dRCV) > _Eps) ? 1.0 : 0.0;

                return float4(wrong, wrong, wrong, 1);
            }
            ENDCG
        }
    }
}
