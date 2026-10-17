#ifndef BIRICH_PLANET_TERRAN_INCLUDED
#define BIRICH_PLANET_TERRAN_INCLUDED

// Terran world: continents, depth-shaded seas, climate bands, night-side cities.
//
// Every kind must give its surface a LONGITUDE-DEPENDENT feature. A sphere carrying only
// latitude structure is symmetric about its spin axis and looks perfectly static however fast
// it turns — the single most common way a procedural planet gives itself away.
SurfaceResult Surface_Terran(SurfaceCtx s)
{
    float3 sp = s.sp; float3 q = s.q; float lon = s.lon; float lat = s.lat;
    float seed = s.seed; float fscale = s.fscale; float3 knobs = s.knobs; bool hires = s.hires;
    float3 c1 = s.c1, c2 = s.c2, c3 = s.c3;

    SurfaceResult o = SurfaceResultInit(c1);
    float3 col = o.color; float emissive = o.emissive; float3 aux = o.aux;

    // Continent scale and sea level are per-planet: one world is a pangaea, the
    // next an ocean dotted with archipelagos.
    // The texel floor, as in Lava: three of these six octaves land below the pixel grid, and
    // this field drives the NARROWEST threshold in the package — a coastline 0.06 wide — so the
    // shoreline crawled as the world turned. Three octaves clear the grid.
    //
    // No sea-level correction, unlike the ridged fields: fbm is symmetric about 0.5 and
    // renormalizes per octave count, so dropping octaves moves the land fraction by under a
    // percent. Ridged noise is one-sided and does not have that property.
    float3 qL = q * (0.7 * knobs.x) + knobs.z * 3.0;
    float land = hires ? fbm3(qL, 6) : fbm3(qL, 3);
    float sea = 0.46 + frac(seed * 4.377) * 0.10;
    float isLand = smoothstep(sea, sea + (hires ? 0.045 : 0.06), land);
    float3 ocean = c2;
    if (hires) ocean = lerp(c2 * 0.5, c2, smoothstep(sea - 0.22, sea, land)); // depth-shaded seas
    
    float3 landCol = c1;
    if (hires)
    {
        // Climate bands: arid tint near the equator, tundra grays toward the
        // poles — jittered so the boundaries are weather, not rulers.
        float clim = abs(lat) + (fbm3(q * 1.1 + 23.0, 3) - 0.5) * 0.3;
        landCol = lerp(landCol, float3(0.76, 0.62, 0.38), smoothstep(0.28, 0.05, clim) * 0.55);
        landCol = lerp(landCol, landCol * 0.62 + float3(0.22, 0.22, 0.2),
                       smoothstep(0.55, 0.85, clim) * 0.7);
    }
    col = lerp(ocean, landCol, isLand);
    col = lerp(col, landCol * 0.72, smoothstep(sea + 0.12, sea + 0.30, land) * 0.6); // highlands
    if (hires)
    {
        col = lerp(col, float3(0.85, 0.8, 0.6),
                   smoothstep(sea + 0.06, sea + 0.10, land) *
                   (1.0 - smoothstep(sea + 0.10, sea + 0.16, land)) * 0.5);  // coastal shelf
        col = lerp(col, float3(0.94, 0.96, 1.0),
                   smoothstep(sea + 0.28, sea + 0.4, land) * isLand);        // snow peaks
    }
    float capEdge = 0.72 + frac(seed * 5.911) * 0.16;      // polar cap size varies
    float cap = smoothstep(capEdge, capEdge + 0.3, abs(lat));
    col = lerp(col, float3(0.92, 0.95, 1.0), cap);
    aux.y = (1.0 - isLand) * (1.0 - cap);                  // open water → sun glint
    if (hires)
        aux.x = smoothstep(0.7, 0.95, fbm3(q * 3.3 + 17.0, 4)) * isLand * (1.0 - cap); // city clusters

    o.color = col; o.emissive = emissive; o.aux = aux;
    return o;
}

#endif
