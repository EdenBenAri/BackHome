Shader "BackHome/CampRadiusRing"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Straight ("Straight", Float) = 1.9
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CampRadiusRing"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -2, -2

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Straight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // UV is in units of half the stroke width. x runs along the dash,
                // y across it. The solid part is a stadium: flat middle, round caps.
                float straight = max(_Straight, 0.0);
                float2 p = float2(max(abs(input.uv.x) - straight, 0.0), input.uv.y);
                float dist = length(p);
                half mask = (half)(1.0 - smoothstep(0.86, 1.0, dist));
                return half4(_Color.rgb, mask * (half)_Color.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
