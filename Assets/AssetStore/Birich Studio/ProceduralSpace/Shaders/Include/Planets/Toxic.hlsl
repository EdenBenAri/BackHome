#ifndef BIRICH_PLANET_TOXIC_INCLUDED
#define BIRICH_PLANET_TOXIC_INCLUDED

// Toxic world: one unbroken, heavily warped acid cloud deck.
//
// Every kind must give its surface a LONGITUDE-DEPENDENT feature. A sphere carrying only
// latitude structure is symmetric about its spin axis and looks perfectly static however fast
// it turns — the single most common way a procedural planet gives itself away.
SurfaceResult Surface_Toxic(SurfaceCtx s)
{
    float3 sp = s.sp; float3 q = s.q; float lon = s.lon; float lat = s.lat;
    float seed = s.seed; float fscale = s.fscale; float3 knobs = s.knobs; bool hires = s.hires;
    float3 c1 = s.c1, c2 = s.c2, c3 = s.c3;

    SurfaceResult o = SurfaceResultInit(c1);
    float3 col = o.color; float emissive = o.emissive; float3 aux = o.aux;

    // One unbroken cloud deck: heavily domain-warped swirls, slowly churning.
    // The warp is what makes it read as weather instead of marble.
    float3 w = q * 0.9;
    if (hires) w += (fbm3(q * 1.6 + Now() * 0.02, 4) - 0.5) * (0.9 + knobs.z * 1.4);
    float deck = fbm3(w, 5);
    float swirl = sin(lat * (knobs.y * 0.5) + (deck - 0.5) * 5.0 + lon * 1.5);
    col = lerp(c2, c1, saturate(swirl * 0.5 + 0.5));
    col = lerp(col, c3, smoothstep(0.62, 0.9, deck) * 0.7); // acid highlights

    o.color = col; o.emissive = emissive; o.aux = aux;
    return o;
}

#endif
