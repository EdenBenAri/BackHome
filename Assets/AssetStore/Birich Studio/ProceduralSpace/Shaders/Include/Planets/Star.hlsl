#ifndef BIRICH_PLANET_STAR_INCLUDED
#define BIRICH_PLANET_STAR_INCLUDED

// Star: self-lit granulation and convection cells; no terminator.
//
// Every kind must give its surface a LONGITUDE-DEPENDENT feature. A sphere carrying only
// latitude structure is symmetric about its spin axis and looks perfectly static however fast
// it turns — the single most common way a procedural planet gives itself away.
SurfaceResult Surface_Star(SurfaceCtx s)
{
    float3 sp = s.sp; float3 q = s.q; float lon = s.lon; float lat = s.lat;
    float seed = s.seed; float fscale = s.fscale; float3 knobs = s.knobs; bool hires = s.hires;
    float3 c1 = s.c1, c2 = s.c2, c3 = s.c3;

    SurfaceResult o = SurfaceResultInit(c1);
    float3 col = o.color; float emissive = o.emissive; float3 aux = o.aux;

    float gran = fbm3(q * (hires ? 4.0 : 2.2) + Now() * 0.05, 4);
    col = lerp(c1, c3, gran);
    if (hires)
    {
        float cells = worley3(q * 6.0 + Now() * 0.03);     // convection cells
        col = lerp(col, c3 * 1.2, smoothstep(0.5, 0.05, cells) * 0.25);
    }
    emissive = 1.0;                                        // no terminator

    o.color = col; o.emissive = emissive; o.aux = aux;
    return o;
}

#endif
