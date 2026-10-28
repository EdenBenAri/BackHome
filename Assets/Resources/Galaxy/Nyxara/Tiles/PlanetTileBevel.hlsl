#ifndef BACKHOME_PLANET_TILE_BEVEL_INCLUDED
#define BACKHOME_PLANET_TILE_BEVEL_INCLUDED

// Soft rounded tile: brighter center, darker edges, thin seam.
// Matches the flat cartoon ground tiles (one cell = one uv repeat).
half PlanetTileBevel(float2 uv)
{
    float2 cell = abs(frac(uv) - 0.5) * 2.0;
    float square = max(cell.x, cell.y);
    float edge = lerp(square, length(cell), 0.42);
    half pillow = (half)lerp(1.04, 0.96, smoothstep(0.55, 1.0, edge));
    half seam = (half)smoothstep(0.97, 1.0, square);
    return lerp(pillow, pillow * 0.94h, seam);
}

// Filled by NyxaraSeaFit. x = sea radius, y = how far under the surface a point
// must sit before the sea is guaranteed to cover it, waves included.
float4 _PlanetSeaCenter;
float4 _PlanetSeaParams;

// The sea is opaque (Blend Off) and draws after the ground. Its color replaces
// the splat completely. Skipping the samples here still writes depth, which the
// shore foam reads. Above water, and while the camera is under the surface, the
// full splat stays.
bool PlanetSeaCovers(float3 positionWS)
{
    float seaRadius = _PlanetSeaParams.x;
    if (seaRadius <= 1.0)
        return false;

    float3 center = _PlanetSeaCenter.xyz;
    if (length(_WorldSpaceCameraPos.xyz - center) <= seaRadius)
        return false;

    return length(positionWS - center) < seaRadius - _PlanetSeaParams.y;
}

#endif
