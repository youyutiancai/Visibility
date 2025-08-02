Shader "Custom/Morph_alt"
{
    Properties
    {
        _MainTex("Main Texture", 2D) = "white"{}
        _Cutoff("Alpha cutoff", Range(0, 1)) = 0.5
        _FactorEdge1("Edge factors", Vector) = (1, 1, 1, 3)
        _FactorInside("Inside factor", Float) = 1
        _Color("Color", Color) = (1, 1, 1, 1)
        _IsColor("if use pure color", Float) = 0
    }

        SubShader{
            LOD 300
            Pass {
                Tags {"LightMode" = "ForwardBase"}
                CGPROGRAM
                #pragma target 5.0
                #pragma vertex vert
                #pragma fragment frag
                #include "UnityCG.cginc"
                #include "Lighting.cginc"
                #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
                #include "AutoLight.cginc"

                struct Interpolators {
                    float2 uv : TEXCOORD0;
                    fixed3 diff : TEXCOORD2;
                    fixed3 ambient : TEXCOORD3;
                    float4 pos : SV_POSITION;
                    float4 ori : TEXCOORD4;
                    float4 positionWS : TEXCOORD5;
                    float4 positionMorphed : TEXCOORD6;
                };

                uniform sampler2D _MainTex;
                float4 _FactorEdge1;
                float _FactorInside;
                float _NearR;
                float _MorphR;
                float _InvisibleR;
                float3 _UserPos;
                float3 _EnvPos;
                float _Cutoff;
                float _IsMorph;
                sampler2D _CustomShadowMap;
                float4x4 _LightViewProjection;
                fixed4 _Color;
                float _IsColor;

                float IntersectRayCylinder(float3 O, float r, float3 U, float3 d) {

                    float xd = d.x, zd = d.z;
                    float xU = U.x, zU = U.z;
                    float xO = O.x, zO = O.z;

                    float a = (xd * xd + zd * zd);
                    float b = 2.0f * xd * (xU - xO) + 2.0f * zd * (zU - zO);
                    float c = (xU - xO) * (xU - xO) + (zU - zO) * (zU - zO) - r * r;

                    float t = (-b + sqrt(b * b - 4.0f * a * c)) / (2.0f * a);

                    return t;

                }

                float3 Morph(float3 O, float3 U, float r1, float r2, float3 V) {

                    float3 gpV = V;
                    gpV.y = 0;
                    float3 gpO = O;
                    gpO.y = 0;
                    float r = length(gpV - gpO);
                    float3 mV;
                    float t;
                    float3 d;
                    if (r > r2) {
                        d = normalize(V - O);
                        t = length(V - O);
                        //t = IntersectRayCylinder(O, r, U, d);
                        mV = U + d * t;
                    }
                    else if (r > r1) {
                        float a = r2 - r;
                        float wU = a / (r2 - r1);
                        float wO = 1 - wU;
                        float3 dU = normalize(V - U);
                        float3 dO = normalize(V - O);
                        d = normalize(dU * wU + dO * wO);
                        t = length(V - U) * wU + length(V - O) * wO;
                        //t = IntersectRayCylinder(O, r, U, d);
                        mV = U + d * t;
                    }
                    else {
                        mV = V;
                    }
                    return mV;
                }

                Interpolators vert(appdata_base v)
                {
                    Interpolators output;
                    //output.ori = UnityObjectToClipPos(v.vertex);
                    float3 envDiff, userDiff;
                    float dist;
                    float4 positionWS4 = mul(unity_ObjectToWorld, v.vertex);
                    float3 pos = Morph(_EnvPos, _UserPos, _NearR, _MorphR, positionWS4.xyz);
                    //float3 pos = positionWS4.xyz;
                    //if (_IsMorph > 0) {
                    //    envDiff = pos - _EnvPos;
                    //    dist = length(float2(envDiff.x, envDiff.z));
                    //    userDiff = pos - _UserPos;
                    //    if (dist > _MorphR) { //Enviornment Section
                    //        pos = _UserPos + normalize(envDiff) * length(envDiff);
                    //    }
                    //    else if (dist > _NearR) { //Morph Section
                    //        float wU = (_MorphR - dist) / (_MorphR - _NearR);
                    //        float wO = 1.0f - wU;
                    //        float3 dU = normalize(userDiff);
                    //        float3 dO = normalize(envDiff);
                    //        float3 d = dU * wU + dO * wO;
                    //        float t = length(userDiff) * wU + length(envDiff) * wO;
                    //        pos = _UserPos + normalize(d) * t;
                    //        //positionWS = _UserPos + normalize(userDiff * wU + envDiff * wO) * (length(userDiff) * wU + length(envDiff) * wO);
                    //        /*float4 dU = envDiff * _NearR / dist + _EnvPos - _UserPos;
                    //        float4 dO = envDiff * _MorphR / dist;
                    //        positionWS = _UserPos + dU * wU + dO * wO;*/
                    //    }
                    //}
                    output.positionMorphed = float4(pos, 1);
                    float4 vertexOS = mul(unity_WorldToObject, float4(pos, 1));
                    output.pos = UnityObjectToClipPos(vertexOS);
                    float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                    half nl = max(0, dot(worldNormal, _WorldSpaceLightPos0.xyz));
                    output.diff = nl * _LightColor0.rgb;
                    output.ambient = ShadeSH9(half4(worldNormal, 1));
                    output.uv = v.texcoord;
                    output.positionWS = mul(unity_ObjectToWorld, v.vertex);
                    //UNITY_SETUP_INSTANCE_ID(o);
                    return output;
                }


            float4 frag(Interpolators i) : SV_Target{
                float3 envDiff = i.positionWS.xyz - _EnvPos;
                float dist = length(float2(envDiff.x, envDiff.z));
                //clip(_InvisibleR - dist);
                clip(dist - _InvisibleR);

                /*float3 morphedDiff = i.positionMorphed.xyz - _EnvPos;
                float morphedDist = length(float2(morphedDiff.x, morphedDiff.z));
                if (abs(morphedDist - _NearR) < 0.1 && abs(i.positionMorphed.y) < 0.1)
                    return fixed4(1, 0, 0, 1);
                else if (abs(morphedDist - _MorphR) < 0.1 && abs(i.positionMorphed.y) < 0.1)
                    return fixed4(0, 1, 0, 1);*/
                
                /*float3 morphedDiff = i.positionMorphed.xyz - _EnvPos;
                float morphedDist = length(float2(morphedDiff.x, morphedDiff.z));
                if (abs(morphedDist - 10) < 1 && abs(i.positionMorphed.y) < 0.1)
                    return fixed4(1, 1, 0, 1);
                else if (abs(morphedDist - 17) < 1 && abs(i.positionMorphed.y) < 0.1)
                    return fixed4(1, 0, 1, 1);
                else if (abs(morphedDist - 63) < 1 && abs(i.positionMorphed.y) < 0.1)
                    return fixed4(0, 1, 0, 1);*/

                fixed4 col = tex2D(_MainTex, i.uv);
                clip(col.a - _Cutoff);
                if (_IsColor > 0) {
                    col = _Color;
                }

                float bias = 0.005f;
                float4 shadowCoord = mul(_LightViewProjection, i.positionWS);
                shadowCoord.xyz /= shadowCoord.w;
                shadowCoord.xyz = shadowCoord.xyz * 0.5 + 0.5;
                //fixed shadow = UNITY_SHADOW_ATTENUATION(i, i.positionWS);
                float depth = tex2D(_CustomShadowMap, shadowCoord.xy).r;
                fixed shadow = (shadowCoord.z - bias) <= depth ? 1.0 : 0.0;

                fixed3 lighting = i.diff * shadow + i.ambient;
                col.rgb *= lighting;
                return col;
            }
            ENDCG
        }
            //UsePass "Legacy Shaders/VertexLit/SHADOWCASTER"
        }
}
