Shader "BackHome/CasualToon"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Range(0, 2)) = 1

        [Header(Flat Cartoon)]
        _Brightness("Brightness", Range(0.8, 1.6)) = 1.04
        _ShadeSteps("Shade Steps", Range(2, 5)) = 2
        _ShadeSoftness("Form Softness", Range(0, 1)) = 0.85
        _ShadeFloor("Shadow Brightness", Range(0.9, 1)) = 0.94
        _ShadowTint("Shadow Tint", Color) = (0.98, 0.98, 0.97, 1)
        _KeyTint("Key Light Tint", Color) = (1, 1, 1, 1)
        _Saturation("Saturation", Range(0.5, 1.8)) = 1.04
        _Contrast("Contrast", Range(0.7, 1.4)) = 1

        [Header(Specular)]
        _SpecularColor("Specular Color", Color) = (1, 1, 1, 0)
        _SpecularSize("Specular Size", Range(0.01, 1)) = 0.08
        _SpecularSoftness("Specular Softness", Range(0.001, 0.5)) = 0.04

        [Header(Rim)]
        _RimColor("Rim Color", Color) = (1, 1, 1, 0)
        _RimPower("Rim Power", Range(0.5, 10)) = 4
        _RimLightMask("Rim Light Mask", Range(0, 1)) = 0.5

        [Header(Ambient)]
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.2
        _AmbientTint("Ambient Tint", Color) = (1, 1, 1, 1)

        [Header(Emission)]
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)
        _EmissionMap("Emission", 2D) = "white" {}

        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0

        [HideInInspector] _Opacity("Opacity", Range(0, 1)) = 1
        [HideInInspector] _SrcBlend("Src Blend", Float) = 1
        [HideInInspector] _DstBlend("Dst Blend", Float) = 0
        [HideInInspector] _ZWrite("ZWrite", Float) = 1
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
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ToonVert
            #pragma fragment ToonFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _BumpScale;
                half _Brightness;
                half _ShadeSteps;
                half _ShadeSoftness;
                half _ShadeFloor;
                half4 _ShadowTint;
                half4 _KeyTint;
                half _Saturation;
                half _Contrast;
                half4 _SpecularColor;
                half _SpecularSize;
                half _SpecularSoftness;
                half4 _RimColor;
                half _RimPower;
                half _RimLightMask;
                half _AmbientStrength;
                half4 _AmbientTint;
                half4 _EmissionColor;
                half _Opacity;
                half _SrcBlend;
                half _DstBlend;
                half _ZWrite;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half4 color : COLOR;
                float fogFactor : TEXCOORD3;
                float4 tangentWS : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            half3 ApplySaturation(half3 c, half sat)
            {
                half luma = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
                return lerp(luma.xxx, c, sat);
            }

            half3 ApplyContrast(half3 c, half contrast)
            {
                return saturate((c - 0.5h) * contrast + 0.5h);
            }

            // XP Hero grade: the painted texture is the color. Light only lays a
            // soft top wash on it, and the dark side stays almost as bright.
            half FlatShade(half ndl)
            {
                half wrap = lerp(0.7h, 1.35h, saturate(_ShadeSoftness));
                half t = saturate((ndl + wrap) / (1.0h + wrap));
                t = t * t * (3.0h - 2.0h * t);

                // Older materials stored a deep floor (around 0.56-0.72). Never go below 0.9,
                // so a painted texture stays one color with a hint of form.
                half floorVal = max(_ShadeFloor, 0.9h);
                return lerp(floorVal, 1.0h, t);
            }

            half ToonSpecular(half3 normalWS, half3 lightDir, half3 viewDir, half size, half soft)
            {
                half3 halfDir = normalize(lightDir + viewDir);
                half nh = saturate(dot(normalWS, halfDir));
                half tight = saturate(size) * 0.35h;
                half threshold = 1.0h - tight;
                return smoothstep(threshold - soft, threshold + soft, nh);
            }

            half3 NeutralLight(half3 lightColor)
            {
                half luma = max(dot(lightColor, half3(0.2126h, 0.7152h, 0.0722h)), 1e-4h);
                half3 tint = lightColor / luma;
                return lerp(half3(1.0h, 1.0h, 1.0h), tint, 0.18h);
            }

            half3 ShadeMainLight(half3 baseRgb, half3 normalWS, half3 viewDir, Light light)
            {
                half ndl = dot(normalWS, light.direction);
                half shade = FlatShade(ndl);

                half shadowAtten = saturate(light.shadowAttenuation * light.distanceAttenuation);
                // Cast shadow is a light stain, not a second dark shape on the texture.
                shade *= lerp(0.96h, 1.0h, shadowAtten);

                half3 shadowCol = baseRgb * _ShadowTint.rgb;
                half3 litCol = baseRgb * _KeyTint.rgb;
                half3 diffuse = lerp(shadowCol, litCol, shade) * NeutralLight(light.color);

                half specMask = ToonSpecular(normalWS, light.direction, viewDir, _SpecularSize, _SpecularSoftness);
                specMask *= shade * _SpecularColor.a * 0.22h;
                diffuse += _SpecularColor.rgb * specMask;

                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDir)), _RimPower);
                half rimLight = saturate(ndl * 0.5h + 0.5h);
                half rim = fresnel * lerp(1.0h, rimLight, _RimLightMask) * _RimColor.a * 0.12h;
                diffuse += _RimColor.rgb * rim;

                return diffuse;
            }

            half3 ShadeAdditionalLight(half3 baseRgb, half3 normalWS, Light light)
            {
                half atten = saturate(light.distanceAttenuation * light.shadowAttenuation);
                // A glow, not another shade band. normalWS is referenced so the interpolator stays live.
                half facing = saturate(dot(normalWS, light.direction) * 0.15h + 0.85h);
                return baseRgb * light.color * facing * atten * 0.12h;
            }

            Varyings ToonVert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrmInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                o.positionCS = posInputs.positionCS;
                o.positionWS = posInputs.positionWS;
                o.normalWS = nrmInputs.normalWS;
                real tangentSign = input.tangentOS.w * GetOddNegativeScale();
                o.tangentWS = float4(nrmInputs.tangentWS, tangentSign);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.color = half4(input.color);
                o.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return o;
            }

            half4 ToonFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 albedoSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 baseRgb = albedoSample.rgb * _BaseColor.rgb * input.color.rgb;
                // Pull saturation and contrast toward the texture so painted colors stay even.
                half sat = lerp(1.0h, _Saturation, 0.45h);
                half contrast = lerp(1.0h, _Contrast, 0.4h);
                baseRgb = ApplySaturation(baseRgb, sat);
                baseRgb = ApplyContrast(baseRgb, contrast);

                float3 vertexN = normalize(input.normalWS);
                float3 bitangentWS = input.tangentWS.w * cross(vertexN, input.tangentWS.xyz);
                float3x3 tangentToWorld = float3x3(input.tangentWS.xyz, bitangentWS, vertexN);
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                float3 mappedN = normalize(TransformTangentToWorld(normalTS, tangentToWorld));
                // Vertex form does the shading. The normal map only hints, so it does not break the flat color.
                float3 normalWS = normalize(lerp(vertexN, mappedN, saturate(_BumpScale) * 0.22h));
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 color = ShadeMainLight(baseRgb, normalWS, viewDir, mainLight);

                // A small fill only. Stacking full SH on the lit side was washing the albedo out.
                half3 sh = SampleSH(normalWS) * _AmbientTint.rgb;
                color += baseRgb * sh * (_AmbientStrength * 0.18h);

                #ifdef _ADDITIONAL_LIGHTS
                uint lightsCount = GetAdditionalLightsCount();
                UNITY_LOOP
                for (uint li = 0u; li < lightsCount; li++)
                {
                    Light addLight = GetAdditionalLight(li, input.positionWS);
                    color += ShadeAdditionalLight(baseRgb, normalWS, addLight);
                }
                #endif

                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb;
                color += emission;
                // Older materials used ~1.25 to climb out of the old dark bands.
                half bright = lerp(1.0h, _Brightness, 0.4h);
                color *= bright;

                color = MixFog(color, input.fogFactor);
                // _SrcBlend/_DstBlend/_ZWrite drive the pass state. Touch them so the
                // SRP Batcher keeps those properties in UnityPerMaterial.
                // _ShadeSteps is unused by the flat grade. Touch it so the SRP batcher
                // keeps the property inside UnityPerMaterial.
                half opacity = saturate(_Opacity + (_SrcBlend + _DstBlend + _ZWrite + _ShadeSteps) * 0);
                return half4(color, opacity);
            }
            ENDHLSL
        }

        // Deferred copies depth before UniversalForwardOnly runs. Without this pass the
        // player never lands in _CameraDepthTexture, so the outline only sees Lit creatures.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CasualDepthVert
            #pragma fragment CasualDepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct CasualDepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct CasualDepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CasualDepthVaryings CasualDepthVert(CasualDepthAttributes input)
            {
                CasualDepthVaryings output = (CasualDepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half CasualDepthFrag(CasualDepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CasualDepthNormalsVert
            #pragma fragment CasualDepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct CasualDepthNormalAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct CasualDepthNormalVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CasualDepthNormalVaryings CasualDepthNormalsVert(CasualDepthNormalAttributes input)
            {
                CasualDepthNormalVaryings output = (CasualDepthNormalVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.normalWS = normalInput.normalWS;
                return output;
            }

            void CasualDepthNormalsFrag(
                CasualDepthNormalVaryings input
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                    outNormalWS = half4(PackFloat2To888(remappedOctNormalWS), 0.0);
                #else
                    outNormalWS = half4(normalWS, 0.0);
                #endif

                #ifdef _WRITE_RENDERING_LAYERS
                    outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
