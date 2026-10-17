Shader "BackHome/CasualSea"
{
    Properties
    {
        [Header(Color)]
        _ShallowColor("Shore Color", Color) = (0.66, 0.91, 0.98, 1)
        _DeepColor("Sea Color", Color) = (0.18, 0.64, 0.93, 1)
        _CrestColor("Ripple Color", Color) = (0.84, 0.97, 1, 1)
        _FoamColor("Foam Color", Color) = (0.97, 0.99, 1, 1)

        [Header(Shore)]
        _ShoreWidth("Foam Width", Range(0.2, 8)) = 2.1
        _DepthDistance("Shallow Distance", Range(0.5, 20)) = 6
        _DepthPower("Shallow Softness", Range(0.3, 2)) = 0.75

        [Header(Ripples)]
        _WaveHeight("Wave Height", Range(0, 1)) = 0.16
        _WaveFrequency("Wave Size", Range(0.05, 2)) = 0.38
        _WaveSpeed("Wave Speed", Range(0, 3)) = 0.55
        _CrestStrength("Ripple Strength", Range(0, 1)) = 0.42
        _RippleNormal("Glint Breakup", Range(0, 6)) = 2.2

        [Header(Highlights)]
        _GlintStrength("Sun Glint", Range(0, 1.5)) = 0.65
        _SparkleScale("Sparkle Size", Range(0.1, 4)) = 0.42
        _SparkleSpeed("Sparkle Speed", Range(0, 6)) = 2.4
        _ShadeFloor("Shadow Brightness", Range(0.5, 1)) = 0.88
    }

    SubShader
    {
        // Transparent queue so the opaque depth texture is the ground under the sea.
        // Blend is off and ZWrite is on, so the surface still covers the planet solidly.
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend Off
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _CrestColor;
                half4 _FoamColor;
                half _ShoreWidth;
                half _DepthDistance;
                half _DepthPower;
                half _WaveHeight;
                half _WaveFrequency;
                half _WaveSpeed;
                half _CrestStrength;
                half _RippleNormal;
                half _GlintStrength;
                half _SparkleScale;
                half _SparkleSpeed;
                half _ShadeFloor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            void SeaFrame(float3 normalWS, out float3 tangent, out float3 bitangent)
            {
                float3 n = normalize(normalWS);
                float3 axis = abs(n.y) > 0.92 ? float3(1, 0, 0) : float3(0, 1, 0);
                tangent = normalize(cross(axis, n));
                bitangent = cross(n, tangent);
            }

            void SeaWaves(float3 positionWS, float3 normalWS, out float height, out float crest, out float3 slopeWS)
            {
                float3 tangent;
                float3 bitangent;
                SeaFrame(normalWS, tangent, bitangent);
                float2 uv = float2(dot(positionWS, tangent), dot(positionWS, bitangent));

                float time = _Time.y * _WaveSpeed;
                float2 k1 = float2(0.85, 0.28) * _WaveFrequency;
                float2 k2 = float2(-0.36, 0.74) * _WaveFrequency;
                float phase1 = dot(uv, k1) + time;
                float phase2 = dot(uv, k2) - time * 0.72;

                float s1 = sin(phase1);
                float s2 = sin(phase2);
                height = (s1 * 0.62 + s2 * 0.38) * _WaveHeight;

                float2 gradient = (cos(phase1) * 0.62 * k1 + cos(phase2) * 0.38 * k2) * _WaveHeight;
                slopeWS = tangent * gradient.x + bitangent * gradient.y;

                // Broad painted bands, only on the crests, like a casual water sprite.
                float band = s1 * 0.5 + 0.5;
                crest = smoothstep(0.58, 0.94, band);
            }

            float Sparkle(float3 positionWS, float3 normalWS, float crest)
            {
                float3 tangent;
                float3 bitangent;
                SeaFrame(normalWS, tangent, bitangent);
                float2 uv = float2(dot(positionWS, tangent), dot(positionWS, bitangent));
                float2 cell = floor(uv * _SparkleScale);
                float rnd = frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
                float twinkle = sin(_Time.y * _SparkleSpeed + rnd * 6.28318) * 0.5 + 0.5;
                float dotOn = step(0.992, rnd) * smoothstep(0.5, 1.0, twinkle);
                return dotOn * smoothstep(0.35, 0.85, crest);
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float height, crest;
                float3 slopeWS;
                SeaWaves(positionWS, normalWS, height, crest, slopeWS);
                positionWS += normalize(normalWS) * height;

                output.positionWS = positionWS;
                output.normalWS = normalWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float height, crest;
                float3 slopeWS;
                SeaWaves(input.positionWS, input.normalWS, height, crest, slopeWS);

                float3 normalWS = normalize(input.normalWS - slopeWS * _RippleNormal);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);

                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                // Eye-depth gap to the ground. Large over open water, small on the beach.
                float thickness = max(sceneEye - surfaceEye, 0);

                float deep = saturate(pow(saturate(thickness / max(_DepthDistance, 0.01)), _DepthPower));
                half3 color = lerp(_ShallowColor.rgb, _DeepColor.rgb, deep);
                color = lerp(color, _CrestColor.rgb, crest * _CrestStrength * deep);

                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), 3.0);
                color = lerp(color, _CrestColor.rgb, fresnel * 0.22);

                Light mainLight = GetMainLight();
                float3 lightDir = mainLight.direction;
                if (dot(lightDir, lightDir) < 1e-4)
                    lightDir = normalize(float3(0.35, 0.9, 0.25));

                float ndl = saturate(dot(normalWS, lightDir));
                float shade = lerp(_ShadeFloor, 1.0, smoothstep(0.02, 0.65, ndl));
                color *= shade;

                float3 halfDir = normalize(lightDir + viewDir);
                float glint = smoothstep(0.72, 0.98, saturate(dot(normalWS, halfDir)));
                float sparkle = Sparkle(input.positionWS, input.normalWS, crest);
                color += (glint * _GlintStrength + sparkle) * half3(1, 1, 1);

                float shore = saturate(1.0 - thickness / max(_ShoreWidth, 0.01));
                shore = saturate(shore + (crest - 0.35) * 0.18);
                float foam = smoothstep(0.22, 0.78, shore);
                float foamLine = smoothstep(0.72, 0.98, shore);
                color = lerp(color, _FoamColor.rgb, saturate(foam * 0.72 + foamLine));

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
