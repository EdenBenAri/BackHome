Shader "BackHome/ToonOutline"
{
    Properties
    {
        [Header(Ink)]
        _OutlineColor("Mob Ink", Color) = (0, 0, 0, 1)
        _KingOutlineColor("King Ink", Color) = (0.86, 0.09, 0.12, 1)
        _OutlinePixels("Mob Thickness (pixels)", Range(1, 8)) = 4
        _PlayerOutlinePixels("Player Thickness (pixels)", Range(1, 8)) = 2
        [HideInInspector] _UseKing("Use King Ink", Float) = 0
        [HideInInspector] _UsePlayer("Use Player Thickness", Float) = 0

        [Header(Distance)]
        _FadeStart("Fade Start", Float) = 90
        _FadeEnd("Fade End", Float) = 160
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _OutlineColor;
            float4 _KingOutlineColor;
            float _OutlinePixels;
            float _PlayerOutlinePixels;
            float _FadeStart;
            float _FadeEnd;
            float _UseKing;
            float _UsePlayer;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
        };

        Varyings VertBody(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            return output;
        }

        float2 ScreenDir(float4 fromCS, float4 toCS)
        {
            float2 fromPx = (fromCS.xy / max(fromCS.w, 1e-4)) * (_ScreenParams.xy * 0.5);
            float2 toPx = (toCS.xy / max(toCS.w, 1e-4)) * (_ScreenParams.xy * 0.5);
            float2 delta = toPx - fromPx;
            float len2 = dot(delta, delta);
            return len2 > 0.04 ? delta * rsqrt(len2) : float2(0, 0);
        }

        Varyings VertInk(Attributes input)
        {
            Varyings output;
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float3 meshN = TransformObjectToWorldNormal(input.normalOS);
            float meshLen2 = dot(meshN, meshN);
            meshN = meshLen2 > 1e-8 ? meshN * rsqrt(meshLen2) : float3(0, 1, 0);

            float3 radial = TransformObjectToWorldDir(input.positionOS.xyz);
            float radialLen2 = dot(radial, radial);
            radial = radialLen2 > 1e-8 ? radial * rsqrt(radialLen2) : meshN;

            float3 toCam = _WorldSpaceCameraPos.xyz - positionWS;
            float camLen2 = dot(toCam, toCam);
            float3 camDir = camLen2 > 1e-6 ? toCam * rsqrt(camLen2) : float3(0, 1, 0);
            float3 pushedWS = positionWS + camDir * 0.35;

            float4 posCS = TransformWorldToHClip(pushedWS);
            float2 dirN = ScreenDir(posCS, TransformWorldToHClip(pushedWS + meshN * 0.2));
            float2 dirR = ScreenDir(posCS, TransformWorldToHClip(pushedWS + radial * 0.2));
            float2 dir = dot(dirN, dirN) >= dot(dirR, dirR) ? dirN : dirR;

            float fadeEnd = max(_FadeEnd, _FadeStart + 0.01);
            float fade = 1.0 - smoothstep(_FadeStart, fadeEnd, posCS.w);
            float thickness = lerp(_OutlinePixels, _PlayerOutlinePixels, saturate(_UsePlayer));
            float pixels = max(thickness, 1.0) * fade;
            posCS.xy += dir * pixels * (2.0 / _ScreenParams.xy) * posCS.w;

            output.positionCS = posCS;
            return output;
        }

        half4 FragInk() : SV_Target
        {
            half3 ink = lerp(_OutlineColor.rgb, _KingOutlineColor.rgb, saturate(_UseKing));
            return half4(ink, 1);
        }

        half4 FragEmpty() : SV_Target
        {
            return 0;
        }
        ENDHLSL

        Pass
        {
            Name "Mask"
            Cull Back
            ZWrite Off
            ZTest LEqual
            ColorMask 0

            Stencil
            {
                Ref 16
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex VertBody
            #pragma fragment FragEmpty
            ENDHLSL
        }

        Pass
        {
            Name "Ink"
            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask RGB

            Stencil
            {
                Ref 16
                Comp NotEqual
                Pass Keep
            }

            HLSLPROGRAM
            #pragma vertex VertInk
            #pragma fragment FragInk
            ENDHLSL
        }

        Pass
        {
            Name "Clear"
            Cull Back
            ZWrite Off
            ZTest Always
            ColorMask 0

            Stencil
            {
                Ref 0
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex VertBody
            #pragma fragment FragEmpty
            ENDHLSL
        }
    }

    FallBack Off
}
