Shader "BackHome/PlanetTilesSplat"
{
    Properties
    {
        _Splat0("Splat 0 (R)", 2D) = "white" {}
        _Splat1("Splat 1 (G)", 2D) = "white" {}
        _Splat2("Splat 2 (B)", 2D) = "white" {}
        _Splat3("Splat 3 (A)", 2D) = "white" {}
        _BaseColor("Color", Color) = (1, 1, 1, 1)
        _BlendSharpness("Blend Sharpness", Range(1, 4)) = 1.65
        _ShadeFloor("Shade Floor", Range(0.3, 1)) = 0.96
        _ShadeCeil("Shade Ceil", Range(0.5, 1.2)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PlanetTileBevel.hlsl"

            TEXTURE2D(_Splat0);
            TEXTURE2D(_Splat1);
            TEXTURE2D(_Splat2);
            TEXTURE2D(_Splat3);
            SAMPLER(sampler_Splat0);

            CBUFFER_START(UnityPerMaterial)
                float4 _Splat0_ST;
                half4 _BaseColor;
                half _BlendSharpness;
                half _ShadeFloor;
                half _ShadeCeil;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = TRANSFORM_TEX(input.uv, _Splat0);
                o.color = input.color;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.positionWS = posWS;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                [branch]
                if (PlanetSeaCovers(input.positionWS))
                    return half4(0, 0, 0, 1);

                half4 w = max(input.color, 0);
                w = pow(w, half4(_BlendSharpness, _BlendSharpness, _BlendSharpness, _BlendSharpness));
                half sum = w.r + w.g + w.b + w.a + 1e-5;
                w /= sum;

                half4 c0 = SAMPLE_TEXTURE2D(_Splat0, sampler_Splat0, input.uv);
                half4 c1 = SAMPLE_TEXTURE2D(_Splat1, sampler_Splat0, input.uv);
                half4 c2 = SAMPLE_TEXTURE2D(_Splat2, sampler_Splat0, input.uv);
                half4 c3 = SAMPLE_TEXTURE2D(_Splat3, sampler_Splat0, input.uv);

                half3 albedo = c0.rgb * w.r + c1.rgb * w.g + c2.rgb * w.b + c3.rgb * w.a;
                half3 n = normalize(input.normalWS);
                half3 lightDir = normalize(half3(0.35, 0.9, 0.25));
                half ndl = dot(n, lightDir);
                half wrap = 0.85h;
                half t = saturate((ndl + wrap) / (1.0h + wrap));
                t = t * t * (3.0h - 2.0h * t);
                // R is Grass_brightened, A is Grass_darked. Dirt and clay stay flat.
                half grass = saturate(w.r + w.a);
                half bevel = lerp(1.0h, PlanetTileBevel(input.uv), grass);
                half shade = lerp(_ShadeFloor, _ShadeCeil, t) * bevel;
                return half4(albedo * _BaseColor.rgb * shade, 1);
            }
            ENDHLSL
        }

        // Used only when URP actually runs a depth prepass (it cannot copy the
        // camera depth, or something reads depth before opaques). Forward and
        // Forward+ copy depth after the color pass, so this is not a second
        // draw of the planet on those paths. Removing it makes the sea and the
        // outline miss the ground whenever that prepass is the depth source.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PlanetDepthPosVert
            #pragma fragment PlanetDepthFrag
            #pragma multi_compile_instancing
            #include "PlanetDepth.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PlanetDepthVert
            #pragma fragment PlanetDepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "PlanetDepth.hlsl"
            ENDHLSL
        }

        // Marks visible planet pixels so the outline pass can skip the ground itself.
        Pass
        {
            Name "PlanetStencil"
            Tags { "LightMode" = "PlanetStencil" }

            ColorMask 0
            ZWrite Off
            ZTest Equal
            Cull Back

            Stencil
            {
                Ref 1
                ReadMask 1
                WriteMask 1
                Comp Always
                Pass Replace
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PlanetDepthPosVert
            #pragma fragment PlanetStencilFrag
            #pragma multi_compile_instancing
            #include "PlanetDepth.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
