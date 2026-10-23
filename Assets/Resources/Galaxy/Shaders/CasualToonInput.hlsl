#ifndef BACKHOME_CASUAL_TOON_INPUT_INCLUDED
#define BACKHOME_CASUAL_TOON_INPUT_INCLUDED

// Shared material block. Every pass includes this so the layout stays identical.
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
    half _WindDegrees;
    half _WindReach;
CBUFFER_END

// Filled by EnvironmentWind. xyz = world wind direction, the rest is unused.
float4 _EnvWindDir;
// x = slowest sway speed, y = fastest, z = how fast a gust travels across the ground.
float4 _EnvWindParams;

// Foliage meshes are authored Z-up and the prefab turns them -90 degrees on X,
// so object-space Z is the prop's up (away from the planet center).
float3 CasualWindOffsetOS(float3 positionOS)
{
    float degrees = _WindDegrees;
    if (degrees <= 0.001)
        return float3(0, 0, 0);

    float3 upDir = TransformObjectToWorldDir(float3(0, 0, 1));
    float upLen = length(upDir);
    float3 upWS = upLen > 1e-5 ? upDir / upLen : float3(0, 1, 0);

    float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
    float3 positionWS = TransformObjectToWorld(positionOS);
    float height = dot(positionWS - originWS, upWS);

    // Below the mesh pivot (the trunk / roots) stays planted.
    float reach = max(_WindReach, 0.05);
    float mask = saturate(height / reach);
    mask *= mask;
    if (mask <= 0)
        return float3(0, 0, 0);

    float3 windDir = _EnvWindDir.xyz;
    float windLen2 = dot(windDir, windDir);
    windDir = windLen2 > 1e-6 ? windDir * rsqrt(windLen2) : float3(0.92, 0, 0.38);

    float3 side = windDir - upWS * dot(windDir, upWS);
    float sideLen2 = dot(side, side);
    side = sideLen2 > 1e-6 ? side * rsqrt(sideLen2) : float3(1, 0, 0);

    float2 seed = originWS.xz + originWS.y;
    float plant = frac(sin(dot(seed, float2(12.9898, 78.233))) * 43758.5453);

    float minSpeed = _EnvWindParams.x;
    float maxSpeed = _EnvWindParams.y;
    float freq = _EnvWindParams.z;
    if (maxSpeed < 0.01)
    {
        minSpeed = 0.5;
        maxSpeed = 1.0;
        freq = 0.4;
    }

    // Each plant keeps its own tempo, and that tempo drifts inside the range.
    float wander = 0.5 + 0.5 * sin(_Time.y * 0.085 + plant * 6.2831853);
    float speed = lerp(minSpeed, maxSpeed, saturate(plant * 0.7 + wander * 0.3));

    // A gust travels along the wind, so neighbors lean a moment apart.
    float phase = dot(originWS, windDir) * freq + plant * 6.2831853;
    float wave = sin(_Time.y * speed + phase);

    float angle = wave * radians(degrees) * mask;
    float lean = height * sin(angle);
    float settle = height * (cos(angle) - 1.0);

    float3 offsetWS = side * lean + upWS * settle;
    return mul((float3x3)GetWorldToObjectMatrix(), offsetWS);
}

// Depth and outline passes must reference every material member, or the
// compiler drops them and the per-material buffer no longer matches.
float CasualToonKeepMaterialLayout()
{
    return (_BaseMap_ST.x + _BaseColor.r + _BumpScale + _Brightness + _ShadeSteps
        + _ShadeSoftness + _ShadeFloor + _ShadowTint.r + _KeyTint.r + _Saturation
        + _Contrast + _SpecularColor.r + _SpecularSize + _SpecularSoftness
        + _RimColor.r + _RimPower + _RimLightMask + _AmbientStrength + _AmbientTint.r
        + _EmissionColor.r + _Opacity + _SrcBlend + _DstBlend + _ZWrite
        + _WindDegrees + _WindReach) * 0;
}

#endif
