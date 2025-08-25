Shader "Custom/Cover_check"
{
    Properties
    {
        _MainTex("Main Texture", 2D) = "white"{}
        _Cutoff("Alpha cutoff", Range(0, 1)) = 0.5
        _Color("Color", Color) = (1, 1, 1, 1)
        _FactorEdge1("Edge factors", Vector) = (1, 1, 1, 1)
        _FactorInside("Inside factor", Float) = 1
    }

    SubShader{
        LOD 300
        Pass {
            Tags {"LightMode" = "ForwardBase"}
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma hull hull
            #pragma domain doma
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #include "AutoLight.cginc"

            struct tessellationFactors {
                float edge[3] : SV_TessFactor;
                float inside : SV_InsideTessFactor;
            };

            struct tessellationControlPoint {
                float3 positionWS : INTERNALTESSPOS;
                float3 normalWS : NORMAL;
                float2 uv : TEXCOORD0;
            };

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
            float4 _UserPos;
            float4 _EnvPos;
            float _Cutoff;
            float _IsMorph;
            sampler2D _CustomShadowMap;
            float4x4 _LightViewProjection;
            fixed4 _Color;
            float _IsColor;

            tessellationControlPoint vert(appdata_base v) {
                tessellationControlPoint output;
                output.positionWS = mul(unity_ObjectToWorld, v.vertex).xyz;
                output.normalWS = UnityObjectToWorldNormal(v.normal);
                output.uv = v.texcoord;
                return output;
            }

            float CheckDis(tessellationControlPoint p1, tessellationControlPoint p2) {
                float3 envDiff_p1 = p1.positionWS - _EnvPos;
                float dist_p1 = length(float4(envDiff_p1.x, 0, envDiff_p1.z, 0));
                float3 envDiff_p2 = p2.positionWS - _EnvPos;
                float dist_p2 = length(float4(envDiff_p2.x, 0, envDiff_p2.z, 0));
                //if (_InvisibleR < 95) {
                //if (!((dist_p1 > _MorphR && dist_p2 > _MorphR) || (dist_p1 < _NearR && dist_p2 < _NearR)))
                //if (dist_p1 < _MorphR || dist_p2 < _MorphR)
                    return distance(p1.positionWS, p2.positionWS) / _FactorEdge1.w;
                /*}
                else {
                    if ((dist_p1 - _NearR) * (dist_p2 - _NearR) < 0)
                        return distance(p1.positionWS, p2.positionWS) / _FactorEdge1.w;
                }*/

                return 1;
            }



            tessellationFactors PatchConstantFunction(InputPatch<tessellationControlPoint, 3> patch) {
                tessellationFactors f;
                f.edge[0] = CheckDis(patch[1], patch[2]);
                f.edge[1] = CheckDis(patch[0], patch[2]);
                f.edge[2] = CheckDis(patch[0], patch[1]);
                /*f.edge[0] = 1;
                f.edge[1] = 1;
                f.edge[2] = 1;
                */
                f.inside = _FactorInside;
                return f;
            }

            [domain("tri")]
            [outputcontrolpoints(3)]
            [outputtopology("triangle_cw")]
            [patchconstantfunc("PatchConstantFunction")]
            [partitioning("integer")]
            tessellationControlPoint hull(InputPatch<tessellationControlPoint, 3> patch, uint id : SV_OutputControlPointID) {
                return patch[id];
            }

            #define BARYCENTRIC_INTERPOLATE(fieldName) \
		                patch[0].fieldName * barycentricCoordinates.x + \
		                patch[1].fieldName * barycentricCoordinates.y + \
		                patch[2].fieldName * barycentricCoordinates.z

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
                    //t = length(V - O);
                    t = IntersectRayCylinder(O, r, U, d);
                    mV = U + d * t;
                }
                else if (r > r1) {
                    float a = r2 - r;
                    float wU = a / (r2 - r1);
                    float wO = 1 - wU;
                    float3 dU = normalize(V - U);
                    float3 dO = normalize(V - O);
                    d = normalize(dU * wU + dO * wO);
                    //t = length(V - U) * wU + length(V - O) * wO;
                    t = IntersectRayCylinder(O, r, U, d);
                    mV = U + d * t;
                }
                else {
                    mV = V;
                }
                return mV;
            }

            [domain("tri")]
            Interpolators doma(
            tessellationFactors factors,
            OutputPatch<tessellationControlPoint, 3> patch,
            float3 barycentricCoordinates : SV_DomainLocation) {

            Interpolators output;

            float4 positionWS = float4(BARYCENTRIC_INTERPOLATE(positionWS), 1);
            float3 normalWS = BARYCENTRIC_INTERPOLATE(normalWS);
            float2 uvWS = BARYCENTRIC_INTERPOLATE(uv);

            output.positionWS = positionWS;
            float4 positionOS = mul(unity_WorldToObject, positionWS);
            output.ori = UnityObjectToClipPos(positionOS);

            float4 envDiff, userDiff;
            float dist;

            if (_IsMorph > 0) {
                float3 pos = Morph(_EnvPos, _UserPos, _NearR, _MorphR, positionWS.xyz);
                positionWS = float4(pos, 1);
                //    envDiff = positionWS - _EnvPos;
                //    dist = length(float4(envDiff.x, 0, envDiff.z, 0));
                //    userDiff = positionWS - _UserPos;
                //    if (dist > _MorphR) { //Enviornment Section
                //        positionWS = _UserPos + normalize(envDiff) * length(envDiff);
                //    }
                //    else if (dist > _NearR) { //Morph Section
                //        float wU = (_MorphR - dist) / (_MorphR - _NearR);
                //        float wO = 1.0f - wU;
                //        /*float4 dU = normalize(userDiff);
                //        float4 dO = normalize(envDiff);
                //        float4 d = dU * wU + dO * wO;
                //        float t = length(userDiff) * wU + length(envDiff) * wO;
                //        positionWS = _UserPos + normalize(d) * t;*/
                //        //positionWS = _UserPos + normalize(userDiff * wU + envDiff * wO) * (length(userDiff) * wU + length(envDiff) * wO);
                //        float4 dU = envDiff * _NearR / dist + _EnvPos - _UserPos;
                //        float4 dO = envDiff * _MorphR / dist;
                //        positionWS = _UserPos + dU * wU + dO * wO;
                //    }
                }
                output.positionMorphed = positionWS;
                float4 vertexOS = mul(unity_WorldToObject, positionWS);
                output.pos = UnityObjectToClipPos(vertexOS);
                half nl = max(0, dot(normalWS, _WorldSpaceLightPos0.xyz));
                output.diff = nl * _LightColor0.rgb;
                output.ambient = ShadeSH9(half4(normalWS, 1));
                output.uv = uvWS;

                return output;
            }


            float4 frag(Interpolators i) : SV_Target{
                if (_IsColor > 0) {
                    return _Color;
                }

                float3 envDiff = i.positionMorphed.xyz - _EnvPos;
                float dist = length(float2(envDiff.x, envDiff.z));
                clip(_InvisibleR - dist);

                /*float3 morphedDiff = i.positionMorphed.xyz - _EnvPos;
                float morphedDist = length(float2(morphedDiff.x, morphedDiff.z));
                if (abs(morphedDist - _NearR) < 0.1 && abs(i.positionMorphed.y) < 0.8)
                    return fixed4(1, 0, 0, 1);
                else if (abs(morphedDist - _MorphR) < 0.1 && abs(i.positionMorphed.y) < 0.8)
                    return fixed4(0, 1, 0, 1);
                else if (abs(morphedDist - _InvisibleR) < 0.15 && abs(i.positionMorphed.y) < 0.8)
                return fixed4(0, 0, 1, 1);*/
                
                fixed4 col = tex2D(_MainTex, i.uv);
                clip(col.a - _Cutoff);

                float bias = 0.005f;
                float4 shadowCoord = mul(_LightViewProjection, i.positionWS);
                shadowCoord.xyz /= shadowCoord.w;
                shadowCoord.xyz = shadowCoord.xyz * 0.5 + 0.5;
                float depth = tex2D(_CustomShadowMap, shadowCoord.xy).r;
                fixed shadow = (shadowCoord.z - bias) <= depth ? 1.0 : 0.0;

                fixed3 lighting = i.diff * shadow + i.ambient;
                col.rgb *= lighting;
                return col;
            }
            ENDCG
        }
    }
}
