Shader "BackHome/AreaKingGlow"
{
    Properties
    {
        _InnerColor ("Warm Inner Glow", Color) = (1, 0.055, 0.015, 1)
        _OuterColor ("Red Outer Glow", Color) = (1, 0.025, 0.015, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.28
        _PulseAmount ("Pulse Amount", Range(0, 0.25)) = 0.04
        _PulseSpeed ("Pulse Speed", Range(0, 5)) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "AreaKingGlow"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // Back faces cover the volume even when the camera is inside it.
            // Scene depth clips the ray before terrain or the king's body.
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _InnerColor;
                half4 _OuterColor;
                float _Opacity;
                float _PulseAmount;
                float _PulseSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float pulse = sin(_Time.y * _PulseSpeed);
                float radius = 0.98 + pulse * _PulseAmount * 0.05;
                float3 directionWS = -GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 originWS = GetCameraPositionWS();
                if (unity_OrthoParams.w > 0.5)
                    originWS = input.positionWS - directionWS * dot(input.positionWS - originWS, directionWS);

                float3 origin = TransformWorldToObject(originWS);
                float3 direction = normalize(TransformWorldToObjectDir(directionWS, false));
                // Ray / unit sphere intersection; object scale makes it an ellipsoid.
                float closestT = -dot(origin, direction);
                float3 closest = origin + direction * closestT;
                float halfChordSquared = radius * radius - dot(closest, closest);
                clip(halfChordSquared);
                float halfChord = sqrt(max(0.0, halfChordSquared));
                float enter = max(0.0, closestT - halfChord);
                float leave = closestT + halfChord;

                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                float depth = SampleSceneDepth(screenUV);
                #if !UNITY_REVERSED_Z
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
                #endif
                float3 sceneWS = ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP);
                float sceneT = dot(TransformWorldToObject(sceneWS) - origin, direction);
                leave = min(leave, sceneT);
                clip(leave - enter);

                // Analytic integral of a soft 3D density (1 - distance squared).
                // No shell edge, flat footprint, or ray-marching loop is needed.
                float a = enter - closestT;
                float b = leave - closestT;
                float density = halfChordSquared * (b - a) - (b * b * b - a * a * a) / 3.0;
                density = saturate(density * 0.75 / (radius * radius * radius));
                half4 color = lerp(_InnerColor, _OuterColor, saturate(length(closest) / radius));
                half alpha = color.a * _Opacity * density * (1.0 + pulse * _PulseAmount);
                return half4(color.rgb, saturate(alpha));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
