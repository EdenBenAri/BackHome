#ifndef BIRICH_PLANET_LAVA_INCLUDED
#define BIRICH_PLANET_LAVA_INCLUDED

// Lava world: dark crust broken into plates whose seams glow.
//
// Every kind must give its surface a LONGITUDE-DEPENDENT feature. A sphere carrying only
// latitude structure is symmetric about its spin axis and looks perfectly static however fast
// it turns — the single most common way a procedural planet gives itself away.
SurfaceResult Surface_Lava(SurfaceCtx s)
{
    float3 sp = s.sp; float3 q = s.q; float lon = s.lon; float lat = s.lat;
    float seed = s.seed; float fscale = s.fscale; float3 knobs = s.knobs; bool hires = s.hires;
    float3 c1 = s.c1, c2 = s.c2, c3 = s.c3;

    SurfaceResult o = SurfaceResultInit(c1);
    float3 col = o.color; float emissive = o.emissive; float3 aux = o.aux;

    // Activity level is per-planet: from nearly-cooled crust with a few embers to
    // a fully torn-open magma world.
    float activity = 0.5 + frac(seed * 6.733) * 0.5;

    // ── DETAIL BELOW THE PIXEL GRID IS NOT DETAIL ─────────────────────────────────────────
    // A body is composed to about 34 texels across, so in pixel mode one texel is roughly a
    // seventeenth of the sphere's radius and an octave finer than two texels per feature cannot
    // draw anything. What it contributes instead is a value that CHANGES as the planet turns,
    // and the quantizer downstream converts that into texels flipping between palette levels.
    // The surface boils.
    //
    // This kind was the worst offender in the package, by a factor of two. Five octaves of
    // RIDGED noise put 23% of the crack field's amplitude below the grid; ridged noise has the
    // steepest gradients of anything here, the value drives a glow THRESHOLD (so a hair of
    // movement is a full on/off rather than a shade), and the result is emissive, so it burns on
    // the night side too and the boiling covered the whole disc instead of just the lit half.
    //
    // Two octaves at 0.7x keeps every octave at 2.6 texels or wider across the entire knobs.x
    // range. A smoother field puts MORE area above any given threshold, so the pixel threshold
    // is raised to hold the glowing fraction where the five-octave version had it — measured
    // against the actual noise, not guessed.
    //
    // Two calls with literal octave counts rather than one with `hires ? 5 : 2`: ridged3 carries
    // [loop], and a trip count that is not a compile-time constant is the shape drivers
    // miscompile (see the loop rule at the top of SpaceNoise.hlsl).
    float3 qc = q * (1.2 * knobs.x);
    float cr, glowLo;
    if (hires) { cr = ridged3(qc,       5); glowLo = lerp(0.750, 0.500, activity); }
    else       { cr = ridged3(qc * 0.7, 2); glowLo = lerp(0.855, 0.592, activity); }
    float glow = smoothstep(glowLo, 0.95, cr);
    col = lerp(c2, c1, fbm3(q * 0.7, 3) * 0.6);
    if (hires)
    {
        // Crust broken into plates whose seams glow, with a phase that travels
        // along the crack network — heat should never look frozen in place.
        float plates = worley3(q * 1.1 + 2.0);
        float seam = 1.0 - smoothstep(0.02, 0.14, plates);
        glow = max(glow, seam * activity);
        glow *= 0.8 + 0.2 * sin(Now() * 0.5 + cr * 9.0);
    }
    // ── ERUPTIONS ─────────────────────────────────────────────────────────────────────────────
    // Two or three volcanic centres anchored at fixed points on the surface, each swelling and
    // subsiding on its own slow cycle. They are what turns a lava world from a texture into a
    // place where something is HAPPENING: the crack network alone is static geology, however
    // brightly it glows, and the eye stops reading it after a second. An eruption gives it an
    // event to come back to.
    //
    // Anchored to the surface, so they rotate away with the planet and return — a plume that
    // stayed put while the crust turned beneath it would read as a stain on the lens.
    [unroll] for (int v = 0; v < 3; v++)
    {
        float fv = seed * 3.1 + v * 11.7;
        if (frac(fv * 1.93) > 0.75) continue;              // not every world has three

        float3 vent = normalize(float3(frac(fv * 2.11) * 2.0 - 1.0,
                                       frac(fv * 3.77) * 2.0 - 1.0,
                                       frac(fv * 5.31) * 2.0 - 1.0) + 1e-4);
        float d = distance(sp, vent);
        float size = 0.18 + frac(fv * 7.13) * 0.22;

        // Each vent breathes on its own period and phase; a shared rhythm would read as the
        // whole planet pulsing rather than as separate volcanoes.
        float cycle = 0.5 + 0.5 * sin(Now() * (0.25 + frac(fv * 4.51) * 0.35) + fv);
        float heat = smoothstep(size, size * 0.15, d) * (0.35 + 0.65 * cycle);

        col = lerp(col, c3 * 1.35, saturate(heat));
        glow = max(glow, heat);
    }

    col = lerp(col, c3, glow);
    emissive = glow;                                       // burns on the night side too

    o.color = col; o.emissive = emissive; o.aux = aux;
    return o;
}

#endif
