Shader "BackHome/StageCloud"
{
    Properties
    {
        _ShadowColor ("Soft lavender shade", Color) = (.48, .57, .76, 1)
        _CloudColor ("Cloud body", Color) = (.84, .9, 1, 1)
        _HighlightColor ("Creamy tops", Color) = (1, .99, .96, 1)
        _BobHeight ("Breathing height", Range(0, .3)) = .14
        _BobSpeed ("Breathing speed", Range(0, 2)) = .7
        [HideInInspector] _Reveal ("Reveal", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "Clouds"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back
            ZWrite On
            Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half4 _CloudColor;
                half4 _HighlightColor;
                float _BobHeight;
                float _BobSpeed;
                float _Reveal;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 centerOS : TEXCOORD0;
                half4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half2 shade : TEXCOORD0;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float reveal = smoothstep(0.0, 1.0, saturate(_Reveal * 1.22 - input.color.g * .22));
                float3 up = normalize(input.centerOS);
                float bob = sin(_Time.y * _BobSpeed + input.color.g * 6.283185) * _BobHeight;
                // Clouds shrink and sink into the land as it opens; no alpha overdraw,
                // dither noise, depth texture, or transparent sorting during the reveal.
                float3 position = lerp(input.positionOS.xyz, input.centerOS, reveal)
                    + up * (bob * (1.0 - reveal) - reveal * 4.0);
                output.positionCS = TransformObjectToHClip(position);
                output.shade = input.color.rb;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half light = smoothstep(.1h, .8h, input.shade.x);
                half3 color = lerp(_ShadowColor.rgb, _CloudColor.rgb, light);
                color = lerp(color, _HighlightColor.rgb, smoothstep(.6h, .98h, input.shade.x) * .65h);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
