#ifndef BIRICH_SURFACE_CONTEXT_INCLUDED
#define BIRICH_SURFACE_CONTEXT_INCLUDED

// The contract every planet kind implements. One file per kind lives in Planets/, each defining
// Surface_<Kind> and (for solid bodies) Height_<Kind>; ProceduralSpace.shader only dispatches.
//
// Passing a struct rather than a dozen loose parameters is what keeps that split worth having:
// adding a cue that every kind can use — the seed-derived variation knobs were exactly this —
// otherwise means editing eight signatures and eight call sites, which is the kind of friction
// that quietly discourages improving the surfaces at all.

// Must match PlanetKind (SpaceTypes.cs)
#define KIND_ROCKY  0
#define KIND_GAS    1
#define KIND_ICE    2
#define KIND_LAVA   3
#define KIND_TERRAN 4
#define KIND_STAR   5
#define KIND_DESERT 6
#define KIND_TOXIC  7

struct SurfaceCtx
{
    /// Rotated 3D unit vector on the sphere. Noise is sampled from THIS, never from
    /// (longitude, latitude): the lon/lat plane wraps at ±π and leaves a vertical seam that
    /// rotates into view once per revolution, while a 3D point on the sphere cannot wrap.
    float3 sp;

    /// sp scaled into noise space, with the pixel-mode coarsening already applied. Kinds should
    /// build their frequencies from this rather than from sp, so the coarsening reaches them.
    float3 q;

    /// Spherical coordinates. Safe for anything AXIS-SYMMETRIC — latitude bands, polar caps, a
    /// storm's anchor — because those computations are immune to the wrap that makes lon/lat
    /// unusable for noise.
    float lon, lat;

    float seed;
    float fscale;      // per-kind feature scale from the composer

    /// Per-planet structural variation from the seed: x feature scale, y band / dune frequency,
    /// z free 0..1. This is what stops two planets of the same kind from differing only in
    /// palette — they differ in STRUCTURE: band count, continent size, crater density, activity.
    float3 knobs;

    /// True in smooth mode. Kinds use it to add a high-resolution detail tier; the base tier is
    /// authored for a body only ~34 texels across, where fine detail lands on one or two pixels
    /// and dissolves into noise.
    bool hires;

    float3 c1, c2, c3; // palette: base, shadow/secondary, accent
};

struct SurfaceResult
{
    float3 color;

    /// Self-illumination, 0..1. Emissive areas ignore the terminator, so lava keeps glowing on
    /// the night side and a star has no terminator at all.
    float emissive;

    /// x: night-side city lights, y: open water (sun glint), z: rocky bodies' base crater
    /// octave, handed to the relief pass so it is evaluated once rather than twice.
    float3 aux;
};

SurfaceResult SurfaceResultInit(float3 baseColor)
{
    SurfaceResult r;
    r.color = baseColor;
    r.emissive = 0.0;
    r.aux = 0.0;
    return r;
}

#endif // BIRICH_SURFACE_CONTEXT_INCLUDED
