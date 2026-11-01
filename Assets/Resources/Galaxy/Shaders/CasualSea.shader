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
        _CrestStrength("Ripple Strength", Range(0, 1)) = 0.22
        _RippleNormal("Glint Breakup", Range(0, 6)) = 1.4

        [Header(Highlights)]
        _GlintStrength("Sun Glint", Range(0, 1.5)) = 0.3
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
                float3 wavePosition : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 SeaScale()
            {
                float4x4 objectToWorld = GetObjectToWorldMatrix();
                return float3(length(objectToWorld._m00_m10_m20),
                    length(objectToWorld._m01_m11_m21), length(objectToWorld._m02_m12_m22));
            }

            // A continuous 3D field in planet-local axes, measured in world units.
            // Projecting a radial position onto its own tangent frame collapses the
            // coordinates, and switching that frame near a pole creates a visible cap.
            // This field has neither a longitude seam nor a special case at the poles.
            void SeaWaves(float3 p, out float height, out float crest, out float3 gradient)
            {
                float time = _Time.y * _WaveSpeed;
                float3 k1 = float3(0.85, 0.28, 0.43) * _WaveFrequency;
                float3 k2 = float3(-0.36, 0.74, 0.57) * _WaveFrequency * 1.37;
                float3 k3 = float3(0.52, 0.61, -0.59) * _WaveFrequency * 1.91;
                float3 kw = float3(-0.18, 0.31, 0.23) * _WaveFrequency;
                float warp = dot(p, kw) - time * 0.31;
                float phase1 = dot(p, k1) + sin(warp) * 0.8 + time;
                float phase2 = dot(p, k2) + sin(warp * 1.3 + 1.7) * 0.6 - time * 0.72;
                float phase3 = dot(p, k3) + time * 0.46;
                float wave = sin(phase1) * 0.5 + sin(phase2) * 0.3 + sin(phase3) * 0.2;
                height = wave * _WaveHeight;
                gradient = (cos(phase1) * 0.5 * (k1 + cos(warp) * 0.8 * kw)
                    + cos(phase2) * 0.3 * (k2 + cos(warp * 1.3 + 1.7) * 0.78 * kw)
                    + cos(phase3) * 0.2 * k3) * _WaveHeight;
                // Thin, broken crest strokes instead of broad stripes or round spots.
                crest = smoothstep(0.82, 0.98, sin(phase1))
                    * smoothstep(-0.25, 0.65, sin(phase2));
            }

            float Sparkle(float3 p, float crest)
            {
                float3 q = p * _SparkleScale;
                float3 cell = floor(q);
                float rnd = frac(sin(dot(cell, float3(127.1, 311.7, 74.7))) * 43758.5453);
                float twinkle = sin(_Time.y * _SparkleSpeed + rnd * 6.28318) * 0.5 + 0.5;
                // Fade to zero at cell edges: no square patches or cell-boundary pops.
                float spot = 1.0 - smoothstep(0.08, 0.3, length(frac(q) - 0.5));
                return step(0.94, rnd) * spot * smoothstep(0.5, 1.0, twinkle) * crest;
            }

            float SeaThickness(float2 screenUV, float3 positionWS)
            {
                float depth = SampleSceneDepth(screenUV);
                // Empty depth is open water, never a foamy intersection with the sky.
                #if UNITY_REVERSED_Z
                    if (depth <= 0.000001) return _DepthDistance;
                #else
                    if (depth >= 0.999999) return _DepthDistance;
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
                #endif
                // Inverse projection works for both perspective and orthographic views.
                float3 groundWS = ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP);
                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float waterRadius = length(positionWS - centerWS);
                float groundRadius = length(groundWS - centerWS);
                // Radial depth keeps the shore width stable around a spherical ocean.
                // Distant scenery outside the ocean is not its seabed.
                if (groundRadius > waterRadius + 0.02) return _DepthDistance;
                return max(waterRadius - groundRadius, 0.0);
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
                float3 gradient;
                output.wavePosition = input.positionOS.xyz * SeaScale();
                SeaWaves(output.wavePosition, height, crest, gradient);
                positionWS += normalize(normalWS) * height;

                output.positionWS = positionWS;
                output.normalWS = normalWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float height, crest;
                float3 gradient;
                // Sample the same undisplaced coordinates as the vertex stage.
                SeaWaves(input.wavePosition, height, crest, gradient);
                float3 baseNormal = normalize(input.normalWS);
                float3 gradientWS = TransformObjectToWorldNormal(gradient * SeaScale(), false);
                float3 slopeWS = gradientWS - baseNormal * dot(gradientWS, baseNormal);
                float3 normalWS = normalize(baseNormal - slopeWS * _RippleNormal);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float thickness = SeaThickness(screenUV, input.positionWS);

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
                float glint = pow(saturate(dot(normalWS, halfDir)), 64.0) * ndl;
                float sparkle = Sparkle(input.wavePosition, crest);
                color = lerp(color, _CrestColor.rgb, saturate(glint * _GlintStrength + sparkle * 0.35));

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
