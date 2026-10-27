Shader "BackHome/HologramBeam"
{
    Properties
    {
        [HDR] _Color("Beam", Color) = (0.35, 0.85, 1, 1)
        [HDR] _CoreColor("Core", Color) = (0.75, 1, 1, 1)
        _Intensity("Intensity", Range(0, 3)) = 1
        _Fill("Fill", Range(0, 0.4)) = 0.07
        _LineCount("Line Count", Float) = 16
        _LineSharpness("Line Sharpness", Range(1, 24)) = 7
        _BeamCount("Bright Beams", Float) = 5
        _RiseFrequency("Rise Frequency", Float) = 2.4
        _RiseSpeed("Rise Speed", Float) = 0.32
        _ScanFrequency("Scan Frequency", Float) = 28
        _ScanSpeed("Scan Speed", Float) = 0.35
        _BaseGlow("Base Glow", Range(0, 2)) = 0.7
        _TopFade("Top Fade", Range(0.4, 1)) = 0.82
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
            Name "Beam"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _CoreColor;
                half _Intensity;
                half _Fill;
                half _LineCount;
                half _LineSharpness;
                half _BeamCount;
                half _RiseFrequency;
                half _RiseSpeed;
                half _ScanFrequency;
                half _ScanSpeed;
                half _BaseGlow;
                half _TopFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half isBase = input.color.r;
                half corner = input.color.b;
                float2 uv = input.uv;

                half column = abs(sin(uv.x * PI * _LineCount));
                half lines = pow(saturate(column), _LineSharpness);
                half beam = pow(saturate(sin(uv.x * PI * _BeamCount)), 12);
                beam = saturate(beam + corner * 0.85h);

                half rise = frac(uv.y * _RiseFrequency - _TimeParameters.x * _RiseSpeed);
                half packet = smoothstep(0.02h, 0.16h, rise) * smoothstep(0.7h, 0.28h, rise);
                half scan = sin((uv.y + _TimeParameters.x * _ScanSpeed) * _ScanFrequency) * 0.5h + 0.5h;

                half heightMask = smoothstep(0.0h, 0.04h, uv.y) * (1.0h - smoothstep(_TopFade, 1.0h, uv.y));
                half foot = 1.0h - smoothstep(0.0h, 0.2h, uv.y);

                half cone = _Fill + lines * 0.42h + beam * 0.7h;
                cone *= lerp(0.65h, 1.0h, scan);
                cone *= lerp(0.4h, 1.0h, packet);
                cone *= heightMask;
                cone += foot * _BaseGlow * (0.12h + beam * 0.45h);

                half2 p = uv * 2.0h - 1.0h;
                half box = 1.0h - max(abs(p.x), abs(p.y));
                half edge = smoothstep(0.0h, 0.12h, box);
                half baseScan = sin((uv.y * 7.0h + _TimeParameters.x * _ScanSpeed) * 10.0h) * 0.5h + 0.5h;
                half baseLines = pow(saturate(abs(sin(uv.x * PI * 14.0h))), 6.0h);
                half baseAlpha = edge * (0.04h + baseScan * 0.08h + baseLines * 0.16h) * _BaseGlow;

                half alpha = lerp(cone, baseAlpha, isBase) * _Intensity;
                half3 color = lerp(_Color.rgb, _CoreColor.rgb, saturate(beam + isBase * baseScan));
                return half4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
