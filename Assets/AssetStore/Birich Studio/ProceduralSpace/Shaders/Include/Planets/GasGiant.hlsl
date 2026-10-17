#ifndef BIRICH_PLANET_GASGIANT_INCLUDED
#define BIRICH_PLANET_GASGIANT_INCLUDED

// Gas giant: latitude bands, turbulence, and anchored storm vortices.
//
// Every kind must give its surface a LONGITUDE-DEPENDENT feature. A sphere carrying only
// latitude structure is symmetric about its spin axis and looks perfectly static however fast
// it turns — the single most common way a procedural planet gives itself away.
SurfaceResult Surface_GasGiant(SurfaceCtx s)
{
    float3 sp = s.sp; float3 q = s.q; float lon = s.lon; float lat = s.lat;
    float seed = s.seed; float fscale = s.fscale; float3 knobs = s.knobs; bool hires = s.hires;
    float3 c1 = s.c1, c2 = s.c2, c3 = s.c3;

    SurfaceResult o = SurfaceResultInit(c1);
    float3 col = o.color; float emissive = o.emissive; float3 aux = o.aux;

    // Latitude bands (the kind's identity) + turbulence following longitude.
    // Band count and waviness are per-planet: one giant is calm and wide-banded,
    // the next is a tight, storm-torn ribbon stack.
    // NOTE (here and below): octave counts are LITERALS, never runtime values.
    // The NVIDIA GL compiler miscompiles noise loops with dynamic trip counts —
    // every fbm3/ridged3 call collapses to a constant and surfaces go flat.
    float wavy = hires ? 0.25 + frac(seed * 5.171) * 0.45 : 0.0;
    float turb = fbm3(q * 0.55, 5);
    float lat2 = lat;
    if (hires) lat2 += (fbm3(q * 0.9 + 11.0, 4) - 0.5) * wavy * 1.6;
    float band = sin(lat2 * knobs.y + (turb - 0.5) * 3.2);
    if (hires) band += 0.4 * sin(lat2 * (knobs.y * 2.3) + (turb - 0.5) * 6.0 + seed);
    float bt = saturate(band * 0.5 + 0.5);
    col = lerp(c2, c1, bt);
    if (hires)
    {
        col = lerp(col, c1 * 1.18, smoothstep(0.78, 0.98, bt) * 0.5);  // bright zonal jets
        col = lerp(col, c2 * 0.8,  smoothstep(0.22, 0.02, bt) * 0.4);  // dark belt cores
        col = lerp(col, c1 * 1.12,
                   smoothstep(0.55, 0.9, fbm3(q * 1.7 + 3.0, 4)) * 0.35); // pale zones
    }
    
    // STORMS: 1-3 anchored vortices at fixed longitude/latitude. These sell
    // rotation — the band pattern alone is axis-symmetric and never changes.
    //
    // The loop bound is a CONSTANT with unused storms masked out, never a
    // per-planet value: varying trip counts nested inside the dynamic planet
    // loop are miscompiled by the NVIDIA GL compiler (surfaces silently
    // collapse), and a masked constant loop costs nearly the same.
    float stormCount = 1.0 + floor(frac(seed * 3.719) * 2.99);
    [unroll] for (int s = 0; s < 3; s++)
    {
        float on = s < (int)stormCount ? 1.0 : 0.0;
        float fs = seed * 2.3 + s * 7.31;
        float2 spot = float2(fs, -0.55 + frac(fs * 1.37) * 1.1);
        float dl = lon - spot.x;
        dl = atan2(sin(dl), cos(dl));             // shortest longitude difference
        float el = 0.35 + frac(fs * 2.11) * 0.35; // storm size varies
        float sd = length(float2(dl * 0.45, (lat - spot.y) * 1.7)) / el;
        if (hires) sd *= 1.0 + 0.08 * sin(Now() * 0.6 + fs);   // the storm breathes
        float3 stormCol = s == 0 ? c3 : lerp(c1 * 1.2, c3, frac(fs * 3.3));
        col = lerp(col, stormCol, smoothstep(1.0, 0.3, sd) * 0.85 * on);
        if (hires) col = lerp(col, stormCol * 1.25, smoothstep(0.35, 0.06, sd) * on); // hot eye
    }

    o.color = col; o.emissive = emissive; o.aux = aux;
    return o;
}

#endif
