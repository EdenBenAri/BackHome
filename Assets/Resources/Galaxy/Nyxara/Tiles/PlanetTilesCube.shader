Shader "BackHome/PlanetTilesCube"
{
    Properties
    {
        _BaseMap("Texture", 2D) = "white" {}
        _BaseColor("Color", Color) = (1, 1, 1, 1)
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

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
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
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
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

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 n = normalize(input.normalWS);
                // Soft top wash. Sides stay close to the tile color, like a flat cartoon map.
                half3 lightDir = normalize(half3(0.35, 0.9, 0.25));
                half ndl = dot(n, lightDir);
                half wrap = 0.85h;
                half t = saturate((ndl + wrap) / (1.0h + wrap));
                t = t * t * (3.0h - 2.0h * t);
                half shade = lerp(_ShadeFloor, _ShadeCeil, t) * PlanetTileBevel(input.uv);
                half3 rgb = tex.rgb * _BaseColor.rgb * input.color.rgb * shade;
                return half4(rgb, 1);
            }
            ENDHLSL
        }

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
