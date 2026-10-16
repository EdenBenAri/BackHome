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

#endif
