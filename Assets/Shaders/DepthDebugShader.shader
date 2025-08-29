Shader "Hidden/DepthDebugRange"
{
  Properties {
    _MainTex ("Depth (meters in R)", 2D) = "black" {}
    _NearVis ("Visible Near", Float) = 0
    _FarVis  ("Visible Far", Float) = 10
    _Gamma   ("Gamma", Float) = 1.0
  }

  SubShader {
    Tags { "Queue"="Overlay" "IgnoreProjector"="True" "RenderType"="Transparent" }
    ZTest Always Cull Off ZWrite Off
    Pass {
      CGPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      #include "UnityCG.cginc"

      sampler2D _MainTex;
      float _NearVis, _FarVis, _Gamma;

      struct appdata {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
      };
      struct v2f {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
      };

      v2f vert (appdata v) {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv  = v.uv;
        return o;
      }

      float4 frag(v2f i) : SV_Target {
        float dMeters = tex2D(_MainTex, i.uv).r;
        // remap into [0,1] based on chosen window
        float lin01 = saturate( (dMeters - _NearVis) / max(_FarVis - _NearVis, 1e-6) );
        // gamma adjustment for visibility
        lin01 = pow(lin01, 1.0 / max(_Gamma, 1e-6));
        return float4(lin01, lin01, lin01, 1);
      }
      ENDCG
    }
  }
  FallBack Off
}