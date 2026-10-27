Shader "BackHome/PlanetHologram"
{
    Properties
    {
        [MainTexture] _BaseMap("Planet", 2D) = "white" {}
        _Tint("Tint", Color) = (0.82, 1, 1, 1)
        [HDR] _RimColor("Rim", Color) = (0.55, 0.95, 1, 1)
        _CoreAlpha("Core Alpha", Range(0.05, 1)) = 0.62
        _RimPower("Rim Power", Range(0.6, 6)) = 2.2
        _RimStrength("Rim Strength", Range(0, 2)) = 1.05
        _ScanFrequency("Scan Frequency", Float) = 36
        _ScanSpeed("Scan Speed", Float) = 0.28
        _ScanStrength("Scan Strength", Range(0, 0.6)) = 0.16
        _Brightness("Brightness", Range(0.4, 2)) = 1.15
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
            Name "Hologram"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Tint;
                half4 _RimColor;
                half _CoreAlpha;
                half _RimPower;
                half _RimStrength;
                half _ScanFrequency;
                half _ScanSpeed;
                half _ScanStrength;
                half _Brightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = normalize(input.viewDirWS);
                half facing = saturate(dot(normalWS, viewDirWS));
                half fresnel = pow(1.0h - facing, _RimPower);

                half sweep = sin((input.uv.y + _TimeParameters.x * _ScanSpeed) * _ScanFrequency);
                half scan = lerp(1.0h - _ScanStrength, 1.0h, sweep * 0.5h + 0.5h);

                half3 color = albedo * _Tint.rgb * _Brightness * scan;
                color += _RimColor.rgb * fresnel * _RimStrength;
                half alpha = saturate(_CoreAlpha + fresnel * 0.7h) * scan;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
