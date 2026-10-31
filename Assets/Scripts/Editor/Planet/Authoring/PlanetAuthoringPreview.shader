Shader "Hidden/BackHome/PlanetAuthoringPreview"
{
    Properties
    {
        _MainTex("Atlas", 2D) = "white" {}
        _Splat0("Grass", 2D) = "white" {}
        _Splat1("Dirt", 2D) = "white" {}
        _Splat2("Sand", 2D) = "white" {}
        _Splat3("Path", 2D) = "white" {}
        _UseSplat("Splat", Float) = 0
        _Sharpness("Blend", Float) = 1.65
        _BaseColor("Tint", Color) = (1,1,1,1)
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
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _UseSplat, _Sharpness;
            CBUFFER_END
            struct A { float4 p:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct V { float4 p:SV_POSITION; float3 normal:TEXCOORD1; float2 uv:TEXCOORD0; float4 color:COLOR; };
            V Vert(A a) { V o; o.p=TransformObjectToHClip(a.p.xyz); o.normal=a.normal; o.uv=a.uv; o.color=a.color; return o; }
            half4 Frag(V i):SV_Target
            {
                // Consistent editor lighting makes relief readable in Scene and Prefab Mode,
                // without depending on a gameplay light rig inside the inactive sphere.
                half light = .55h + .45h * saturate(dot(normalize(i.normal), normalize(float3(-.4,1,-.3))));
                if (_UseSplat < .5) return SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv)*_BaseColor*half4(light,light,light,1);
                float4 w=pow(max(i.color,0),_Sharpness); w/=max(dot(w,float4(1,1,1,1)),.0001);
                return (SAMPLE_TEXTURE2D(_Splat0,sampler_Splat0,i.uv)*w.x
                    +SAMPLE_TEXTURE2D(_Splat1,sampler_Splat0,i.uv)*w.y
                    +SAMPLE_TEXTURE2D(_Splat2,sampler_Splat0,i.uv)*w.z
                    +SAMPLE_TEXTURE2D(_Splat3,sampler_Splat0,i.uv)*w.w)*_BaseColor*half4(light,light,light,1);
            }
            ENDHLSL
        }
    }
}
