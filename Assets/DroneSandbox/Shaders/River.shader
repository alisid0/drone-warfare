// River water for URP. Two layers of ripples flow downstream; looking through the surface you see the
// riverbed (refracted) fading into murky green-brown with depth; the sky is reflected mostly at low angles
// (Fresnel); the sun leaves sharp glints; and the water thins out to nothing at the banks.
// Needs the depth and opaque textures (on in the URP asset). Mesh UVs are in metres: u across, v downstream.
Shader "DroneSandbox/River"
{
    Properties
    {
        [Normal][NoScaleOffset] _RippleMap ("Ripples (normal map)", 2D) = "bump" {}
        _RippleSize ("Ripple size (metres)", Float) = 7
        _RippleStrength ("Ripple strength", Range(0, 2)) = 0.35
        _FlowSpeed ("Flow speed (metres per second)", Float) = 0.4
        _ShallowColor ("Shallow tint", Color) = (0.70, 0.72, 0.58, 1)
        _DeepColor ("Deep colour", Color) = (0.085, 0.13, 0.10, 1)
        _Clarity ("Clarity (metres)", Float) = 1.1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.9
        _Refraction ("Refraction", Range(0, 0.1)) = 0.025
        _ShoreFade ("Shore fade (metres)", Float) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "RiverForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #define _SCREENSPACEREFLECTIONS_OFF 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_RippleMap);
            SAMPLER(sampler_RippleMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _RippleSize;
                float _RippleStrength;
                float _FlowSpeed;
                float _Clarity;
                float _Smoothness;
                float _Refraction;
                float _ShoreFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.uv = input.uv;
                output.normalWS = normal.normalWS;
                output.tangentWS = float4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            // Two ripple layers of different sizes drifting downstream at different speeds, so the pattern
            // never visibly repeats.
            half3 Ripples(float2 uv)
            {
                float t = _Time.y * _FlowSpeed;
                float2 uvA = (uv + float2(0.0, -t)) / _RippleSize;
                float2 uvB = (float2(uv.x * 0.8 - uv.y * 0.6, uv.x * 0.6 + uv.y * 0.8) + float2(3.7, -t * 1.6)) / (_RippleSize * 0.37);
                half3 a = UnpackNormalScale(SAMPLE_TEXTURE2D(_RippleMap, sampler_RippleMap, uvA), _RippleStrength);
                half3 b = UnpackNormalScale(SAMPLE_TEXTURE2D(_RippleMap, sampler_RippleMap, uvB), _RippleStrength * 0.6);
                return normalize(half3(a.xy + b.xy, a.z * b.z));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3 normalWS = normalize(TransformTangentToWorld(Ripples(input.uv),
                    half3x3(input.tangentWS.xyz, bitangent, input.normalWS)));
                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

                // Water between the surface and whatever lies behind it, in metres of view depth.
                float surface = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float behind = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float thickness = max(behind - surface, 0.0);

                // Refraction: bend the view of the bed with the ripples, unless that would show something
                // standing in front of the water.
                float2 bentUV = screenUV + normalWS.xz * _Refraction * saturate(thickness);
                float bentBehind = LinearEyeDepth(SampleSceneDepth(bentUV), _ZBufferParams);
                if (bentBehind < surface)
                {
                    bentUV = screenUV;
                    bentBehind = behind;
                }
                half3 bed = SampleSceneColor(bentUV);

                // Light reaching the water, for the murk colour.
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 light = sun.color * (0.45 * sun.shadowAttenuation) + SampleSH(half3(0, 1, 0));

                // The bed fades into murky water with depth.
                half murk = 1.0 - exp(-max(bentBehind - surface, 0.0) / _Clarity);
                half3 underwater = lerp(bed * _ShallowColor.rgb, _DeepColor.rgb * light, murk);

                // Sky reflection, strongest when looking along the surface.
                half3 reflected = GlossyEnvironmentReflection(reflect(-viewWS, normalWS), input.positionWS,
                    1.0 - _Smoothness, 1.0, screenUV);
                half fresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(normalWS, viewWS)), 5.0);
                half3 color = lerp(underwater, reflected, fresnel);

                // Sun glints.
                half3 halfway = normalize(sun.direction + viewWS);
                color += sun.color * (pow(saturate(dot(normalWS, halfway)), 1600.0) * 1.6 * sun.shadowAttenuation);

                color = MixFog(color, input.fogFactor);
                return half4(color, saturate(thickness / _ShoreFade));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
