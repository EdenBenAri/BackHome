Shader "BackHome/VFX/Dust Puff"
{
    Properties
    {
        _BaseColor ("Puff Color", Color) = (1, 0.98, 0.92, 1)
        _EdgeSoftness ("Edge Softness", Range(0.01, 0.3)) = 0.1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "DustPuff"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _EdgeSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                half fog : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * 2.0 - 1.0;
                output.color = input.color * _BaseColor;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // A few overlapping round lobes keep the silhouette readable at mobile scale.
                float2 p = input.uv;
                float d = length(p - float2(-0.02, -0.05)) - 0.60;
                d = min(d, length(p - float2(-0.36, -0.06)) - 0.43);
                d = min(d, length(p - float2(0.35, -0.10)) - 0.40);
                d = min(d, length(p - float2(0.04, 0.29)) - 0.38);
                half alpha = 1.0 - smoothstep(-_EdgeSoftness, max(fwidth(d), 0.001), d);
                half shade = lerp(0.92h, 1.0h, saturate(p.y * 0.5 + 0.5));
                return half4(MixFog(input.color.rgb * shade, input.fog), input.color.a * alpha);
            }
            ENDHLSL
        }
    }
}
