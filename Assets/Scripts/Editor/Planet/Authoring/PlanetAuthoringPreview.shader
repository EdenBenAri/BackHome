Shader "Hidden/BackHome/PlanetAuthoringPreview"
{
    Properties
    {
        _MainTex("Atlas", 2D) = "white" {}
        _Splat0("Grass", 2D) = "white" {}
        _Splat1("Dirt", 2D) = "white" {}
        _Splat2("Sand", 2D) = "white" {}
        _Splat3("Path", 2D) = "white" {}
        _Splat4("Void", 2D) = "white" {}
        _Splat5("Dark Void", 2D) = "white" {}
        _Splat6("Crimson", 2D) = "white" {}
        _Splat7("Dark Crimson", 2D) = "white" {}
        _UseSplat("Splat", Float) = 0
        _Sharpness("Blend", Float) = 1.65
        _BaseColor("Tint", Color) = (1,1,1,1)
        _ReferenceTex("Reference", 2D) = "white" {}
        _ReferenceOpacity("Reference opacity", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_Splat0); SAMPLER(sampler_Splat0);
            TEXTURE2D(_Splat1); TEXTURE2D(_Splat2); TEXTURE2D(_Splat3);
            TEXTURE2D(_Splat4); TEXTURE2D(_Splat5); TEXTURE2D(_Splat6); TEXTURE2D(_Splat7);
            TEXTURE2D(_ReferenceTex); SAMPLER(sampler_ReferenceTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _UseSplat, _Sharpness;
            float _ReferenceOpacity;
            float4 _ReferenceST;
            float4x4 _ReferenceWorldToUV;
            CBUFFER_END
            struct A { float4 p:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; float4 color:COLOR; float4 splatHigh:TEXCOORD1; };
            struct V { float4 p:SV_POSITION; float3 normal:TEXCOORD1; float2 uv:TEXCOORD0; float4 color:COLOR; float2 referenceUV:TEXCOORD2; float4 splatHigh:TEXCOORD3; };
            V Vert(A a) { V o; o.p=TransformObjectToHClip(a.p.xyz); o.normal=a.normal; o.uv=a.uv; o.color=a.color; o.splatHigh=a.splatHigh;
                o.referenceUV=mul(_ReferenceWorldToUV,float4(TransformObjectToWorld(a.p.xyz),1)).xy; return o; }
            half4 Frag(V i):SV_Target
            {
                // Consistent editor lighting makes relief readable in Scene and Prefab Mode,
                // without depending on a gameplay light rig inside the inactive sphere.
                half light = .55h + .45h * saturate(dot(normalize(i.normal), normalize(float3(-.4,1,-.3))));
                half4 ground;
                if (_UseSplat < .5) ground = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv)*_BaseColor*half4(light,light,light,1);
                else {
                float4 w=pow(max(i.color,0),_Sharpness);
                float4 extra=pow(max(i.splatHigh,0),_Sharpness);
                float4 ones=float4(1,1,1,1);
                float sum=max(dot(w,ones)+dot(extra,ones),.0001);
                w/=sum; extra/=sum;
                half3 albedo = SAMPLE_TEXTURE2D(_Splat0,sampler_Splat0,i.uv).rgb*w.x
                    +SAMPLE_TEXTURE2D(_Splat1,sampler_Splat0,i.uv).rgb*w.y
                    +SAMPLE_TEXTURE2D(_Splat2,sampler_Splat0,i.uv).rgb*w.z
                    +SAMPLE_TEXTURE2D(_Splat3,sampler_Splat0,i.uv).rgb*w.w;
                if (dot(extra,ones) > .0001)
                    albedo += SAMPLE_TEXTURE2D(_Splat4,sampler_Splat0,i.uv).rgb*extra.x
                        +SAMPLE_TEXTURE2D(_Splat5,sampler_Splat0,i.uv).rgb*extra.y
                        +SAMPLE_TEXTURE2D(_Splat6,sampler_Splat0,i.uv).rgb*extra.z
                        +SAMPLE_TEXTURE2D(_Splat7,sampler_Splat0,i.uv).rgb*extra.w;
                ground = half4(albedo,1)*_BaseColor*half4(light,light,light,1);
                }
                if (_ReferenceOpacity > 0 && all(i.referenceUV >= 0) && all(i.referenceUV <= 1))
                {
                    half4 reference = SAMPLE_TEXTURE2D(_ReferenceTex,sampler_ReferenceTex,i.referenceUV*_ReferenceST.xy+_ReferenceST.zw);
                    ground.rgb = lerp(ground.rgb,reference.rgb,_ReferenceOpacity*reference.a);
                }
                return ground;
            }
            ENDHLSL
        }
    }
}
