#ifndef BIRICH_PLANET_ICE_INCLUDED
#define BIRICH_PLANET_ICE_INCLUDED

// Ice world: fracture networks, high albedo, wide polar caps.
//
// Every kind must give its surface a LONGITUDE-DEPENDENT feature. A sphere carrying only
// latitude structure is symmetric about its spin axis and looks perfectly static however fast
// it turns — the single most common way a procedural planet gives itself away.
SurfaceResult Surface_Ice(SurfaceCtx s)
{
    float3 sp = s.sp; float3 q = s.q; float lon = s.lon; float lat = s.lat;
    float seed = s.seed; float fscale = s.fscale; float3 knobs = s.knobs; bool hires = s.hires;
    float3 c1 = s.c1, c2 = s.c2, c3 = s.c3;

    SurfaceResult o = SurfaceResultInit(c1);
    float3 col = o.color; float emissive = o.emissive; float3 aux = o.aux;

    float cracks = ridged3(q * (1.4 * knobs.x), 4);  // fracture density per-planet
    col = lerp(c2, c1, fbm3(q * 0.8, 4));
    col = lerp(col, c3, smoothstep(0.62, 0.92, cracks) * 0.8);
    if (hires)
    {
        float fine = ridged3(q * 3.4 + 8.0, 3);           // hairline fracture web
        col = lerp(col, c2 * 0.85, smoothstep(0.7, 0.95, fine) * 0.35);
        // Subsurface scattering: deep fractures glow faint blue — frozen oceans
        // are never just white.
        col = lerp(col, float3(0.3, 0.55, 0.9), smoothstep(0.62, 0.92, cracks) * 0.25);
    }
    float cap = smoothstep(0.72, 1.05, abs(lat));         // polar cap
    col = lerp(col, c3, cap);

    o.color = col; o.emissive = emissive; o.aux = aux;
    return o;
}

#endif
