#ifndef BIRICH_PLANET_DESERT_INCLUDED
#define BIRICH_PLANET_DESERT_INCLUDED

// Desert world: sheared dune seas cut by canyon lines.
//
// Every kind must give its surface a LONGITUDE-DEPENDENT feature. A sphere carrying only
// latitude structure is symmetric about its spin axis and looks perfectly static however fast
// it turns — the single most common way a procedural planet gives itself away.
SurfaceResult Surface_Desert(SurfaceCtx s)
{
    float3 sp = s.sp; float3 q = s.q; float lon = s.lon; float lat = s.lat;
    float seed = s.seed; float fscale = s.fscale; float3 knobs = s.knobs; bool hires = s.hires;
    float3 c1 = s.c1, c2 = s.c2, c3 = s.c3;

    SurfaceResult o = SurfaceResultInit(c1);
    float3 col = o.color; float emissive = o.emissive; float3 aux = o.aux;

    // Dune seas: banded ridges sheared by noise, cut by darker canyon lines.
    // Dune frequency is per-planet.
    float shear = fbm3(q * 0.8, 4);
    float dunes = sin(lat * knobs.y + lon * 2.0 + (shear - 0.5) * 6.0);
    col = lerp(c2, c1, saturate(dunes * 0.5 + 0.5));
    col = lerp(col, c1 * 1.15, smoothstep(0.6, 0.9, fbm3(q * 1.3 + 5.0, 4)) * 0.4); // bright ergs
    // The texel floor, as in Lava: four octaves of ridged noise put a fifth of the canyon
    // field's amplitude below the pixel grid, and it drives a threshold, so canyon edges crawled
    // a texel at a time while the planet turned. Two octaves clear the grid (3.5 texels at the
    // finest) and the threshold is raised to keep the same fraction of the surface in canyon.
    float canyon, canyonLo;
    if (hires) { canyon = ridged3(q * 1.1 + 9.0, 4); canyonLo = 0.680; }
    else       { canyon = ridged3(q * 1.1 + 9.0, 2); canyonLo = 0.767; }
    col = lerp(col, c2 * 0.6, smoothstep(canyonLo, 0.95, canyon) * 0.55);
    float cap = smoothstep(0.85, 1.15, abs(lat));
    col = lerp(col, c3, cap * 0.6);                        // subtle frosted poles

    o.color = col; o.emissive = emissive; o.aux = aux;
    return o;
}

#endif
