#ifndef BIRICH_PLANET_ROCKY_INCLUDED
#define BIRICH_PLANET_ROCKY_INCLUDED

// Rocky body: volcanic plains, highlands, and a record of impact craters.
//
// Every kind must give its surface a LONGITUDE-DEPENDENT feature. A sphere carrying only
// latitude structure is symmetric about its spin axis and looks perfectly static however fast
// it turns — the single most common way a procedural planet gives itself away.
SurfaceResult Surface_Rocky(SurfaceCtx s)
{
    float3 sp = s.sp; float3 q = s.q; float lon = s.lon; float lat = s.lat;
    float seed = s.seed; float fscale = s.fscale; float3 knobs = s.knobs; bool hires = s.hires;
    float3 c1 = s.c1, c2 = s.c2, c3 = s.c3;

    SurfaceResult o = SurfaceResultInit(c1);
    float3 col = o.color; float emissive = o.emissive; float3 aux = o.aux;

    // Ground first: dark volcanic plains flooding the low country, bright
    // highlands between. A cratered body with a uniform base tone reads as one
    // material sprayed with holes; the large-scale light/dark division is what
    // makes it look like terrain that craters were then punched into.
    // Literal octave count, never `hires ? 4 : 3`: a trip count the compiler cannot see as a
    // constant is miscompiled by the NVIDIA GL driver and the whole noise call collapses to a
    // flat value. One octave is not worth reopening that.
    float maria = smoothstep(0.48, 0.74, fbm3(q * 0.45, 4));
    col = lerp(c1, c2, maria);
    
    // Craters as a height field, not as rings. The interior darkens because it
    // is shadowed, sunken ground; the rim brightens because an impact throws
    // fresh, unweathered material out of the hole. Crucially the highlight is
    // NOT a symmetric ring around the pit: a ring of equal brightness all the
    // way round cannot be produced by a light coming from one side, and the eye
    // reads it as a drawn outline. The directional part of the lighting comes
    // from the relief normal, which reads this same field.
    // ── CRATER SIZE, AND WHY IT IS NOT THE GLOBAL ONE ─────────────────────────────────────
    // The shared pixel-art coarsening enlarges every feature by the same factor, and for most
    // of them that is enough. Craters need more, because only part of a crater is legible: the
    // dark floor is what says "hole", and it covers 72% of a radius that is itself a fraction
    // of a lattice cell. Run the numbers on a body thirty texels across and the floor lands on
    // barely two of them — craters drawn perfectly correctly and simply too small to survive
    // the grid, which is indistinguishable from not being drawn at all.
    //
    // So pixel mode gets FEWER, BIGGER craters: about four across the disc instead of six, each
    // wide enough to hold a floor, a rim and a shadow. That is also what pixel art of a cratered
    // moon has always looked like, for exactly this reason.
    //
    // Only the albedo diverges. Relief runs in smooth mode alone, so nothing reads this field at
    // a different scale than it was drawn at.
    float craterBase;
    float cr = CraterField(q, 1.1 * knobs.x * (hires ? 1.0 : 0.72), craterBase);
    // Carried out for the relief pass, which reads the same field and would otherwise evaluate
    // it a second time. Only meaningful in smooth mode, the only mode that builds a relief
    // normal.
    aux.z = craterBase;

    // Regolith grain, divided by fscale so small bodies (which already sample denser)
    // don't turn into noisy golf balls. Computed before the craters use it, because it
    // does two jobs.
    float grain = hires ? fbm3(q * (6.0 / max(fscale, 1.0)), 3) : 0.5;

    // ── HOW HARD THE ALBEDO WORKS ─────────────────────────────────────────────────────────
    // Smooth mode has a relief normal built from this very field; pixel mode has none. So the
    // two modes have to say "crater" with different instruments, and giving them the same
    // albedo means one of them is always wrong.
    //
    // On a real cratered body the floor is barely darker than the ground around it — what you
    // are seeing is a lit wall opposite a shadowed one, which is why the pattern of light flips
    // when the sun moves. Painting a dark disc with a bright ring round it produces a shape no
    // light source can explain, and the eye reads it as a drawn symbol rather than a hole — the
    // single loudest way a cratered body gives itself away. Smooth mode therefore keeps its
    // albedo response faint and lets the relief carry the crater.
    //
    // Pixel mode gets the loud version, because a flat-shaded surface has nothing else to say
    // it with — the same reason a pixel artist draws the shadow into the sprite.
    float floorAmt  = hires ? 0.42 : 0.70;
    float ejectaAmt = hires ? 0.16 : 0.30;

    // Floor: sunken ground, in its own shadow and swept clear of bright dust.
    col = lerp(col, c2 * 0.68, saturate(-cr) * floorAmt);

    // Ejecta. The second job of the grain is to BREAK this up, because an evenly bright
    // ring all the way around a pit is the loudest procedural tell there is: no light
    // arriving from one direction can brighten a rim uniformly, so the eye files it as a
    // drawn outline rather than as ground. Real ejecta lands in streaks and patches, and
    // it is the patchiness — not the brightness — that reads as thrown material.
    //
    // Kept weak on purpose. The directional half of a crater's look belongs to the relief
    // normal, which reads this same field; the albedo only has to say "this material is
    // younger than what is around it".
    col = lerp(col, c3, saturate(cr) * (0.35 + 0.9 * grain) * ejectaAmt);

    if (hires) col *= 0.94 + 0.12 * grain;

    o.color = col; o.emissive = emissive; o.aux = aux;
    return o;
}

#endif
