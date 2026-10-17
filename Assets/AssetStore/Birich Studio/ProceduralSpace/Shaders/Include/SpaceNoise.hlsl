#ifndef BIRICH_SPACE_NOISE_INCLUDED
#define BIRICH_SPACE_NOISE_INCLUDED

// Shared procedural primitives for Birich Studio / ProceduralSpace.
//
// Two rules here are load-bearing and must survive any edit: the lattice hashes are INTEGER
// arithmetic (float-trick hashes get miscompiled into visible grid artifacts by some drivers),
// and every loop carries [loop] with a literal trip count (unrolled 27-iteration cell loops are
// most of this shader stack compile time, and dynamic trip counts get miscompiled outright).

// ── Noise ──────────────────────────────────────────────────────────────────────────
// Integer-lattice hash, like hash31 below. The old float-trick version multiplied
// coordinates into the thousands where fp32 keeps only a few significant bits of
// fraction; some drivers' FMA/precision choices then leave neighboring lattice cells
// CORRELATED and the value-noise grid becomes visible as large square patches in the
// nebula. Integer math is bit-exact on every driver — the whole class of artifact
// dies here. All call sites address lattice points (whole numbers plus constant
// offsets ≥1 apart), so flooring is part of the contract.
float hash21(float2 p)
{
    uint2 v = uint2(int2(floor(p)) + 262144);
    v = v * 1664525u + 1013904223u;
    v.x += v.y * 1664525u;
    v.y += v.x * 1664525u;
    v ^= v >> 16;
    v.x += v.y * 1664525u;
    return float(v.x & 0xFFFFFFu) * (1.0 / 16777216.0);
}

float vnoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i);
    float b = hash21(i + float2(1, 0));
    float c = hash21(i + float2(0, 1));
    float d = hash21(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// NOTE: all octave loops carry [loop] — octave counts arrive as runtime values
// (uniform-branch dependent), and letting a driver try to unroll them is exactly the
// kind of place miscompiles hide.
float fbm(float2 p, int octaves)
{
    float s = 0.0, a = 0.5, norm = 0.0;
    [loop] for (int k = 0; k < octaves; k++)
    {
        s += a * vnoise(p); norm += a;
        p *= 2.03; a *= 0.5;
    }
    return s / max(norm, 1e-5);
}

/// Ridged noise — produces filaments / cracks (nebula strands, lava fissures).
float ridged(float2 p, int octaves)
{
    float s = 0.0, a = 0.5, norm = 0.0;
    [loop] for (int k = 0; k < octaves; k++)
    {
        float v = 1.0 - abs(vnoise(p) * 2.0 - 1.0);
        s += a * v * v; norm += a;
        p *= 2.11; a *= 0.5;
    }
    return s / max(norm, 1e-5);
}

// ── 3D noise: MANDATORY for sphere surfaces ────────────────────────────────────────
// Sampling in (longitude, latitude) space makes the noise DISCONTINUOUS at the ±π
// longitude wrap and a vertical SEAM appears across the sphere; the seam rotates with
// the planet and periodically faces the camera. Sampling from the 3D point on the
// sphere removes the very concept of wrapping — the seam becomes mathematically
// impossible. Polar pinching disappears too.
// Integer-lattice hash. A float-trick hash (frac/dot/frac) is fast but fragile: under
// some driver + fast-math combinations its intermediate precision collapses and every
// lattice point returns the SAME value — the whole 3D noise stack then flattens to a
// constant and planet surfaces lose all detail while 2D elements keep working.
// Integer arithmetic has no precision to lose: bit-identical on every backend.
float hash31(float3 p)
{
    uint3 v = uint3(int3(floor(p)) + 262144);
    v = v * 1664525u + 1013904223u;
    v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
    v ^= v >> 16;
    v.x += v.y * v.z;
    return float(v.x & 0xFFFFFFu) * (1.0 / 16777216.0);
}

float vnoise3(float3 p)
{
    float3 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = hash31(i);
    float n100 = hash31(i + float3(1, 0, 0));
    float n010 = hash31(i + float3(0, 1, 0));
    float n110 = hash31(i + float3(1, 1, 0));
    float n001 = hash31(i + float3(0, 0, 1));
    float n101 = hash31(i + float3(1, 0, 1));
    float n011 = hash31(i + float3(0, 1, 1));
    float n111 = hash31(i + float3(1, 1, 1));
    float x00 = lerp(n000, n100, f.x);
    float x10 = lerp(n010, n110, f.x);
    float x01 = lerp(n001, n101, f.x);
    float x11 = lerp(n011, n111, f.x);
    return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
}

float fbm3(float3 p, int octaves)
{
    float s = 0.0, a = 0.5, norm = 0.0;
    [loop] for (int k = 0; k < octaves; k++)
    {
        s += a * vnoise3(p); norm += a;
        p *= 2.03; a *= 0.5;
    }
    return s / max(norm, 1e-5);
}

float ridged3(float3 p, int octaves)
{
    float s = 0.0, a = 0.5, norm = 0.0;
    [loop] for (int k = 0; k < octaves; k++)
    {
        float v = 1.0 - abs(vnoise3(p) * 2.0 - 1.0);
        s += a * v * v; norm += a;
        p *= 2.11; a *= 0.5;
    }
    return s / max(norm, 1e-5);
}

/// 3D Worley (cellular) — distance to the nearest seed. The right tool for crater
/// fields: thresholding fbm gives amorphous blobs, craters must be ROUND and discrete.
/// 27 neighbors are scanned; only pixels inside a planet disc pay for it.
float worley3(float3 p)
{
    float3 i = floor(p), f = frac(p);
    float d = 8.0;
    // One flat [loop] rather than three nested, for the same reason as CraterOctave: nested
    // loops carrying `continue` are the shape that most reliably defeats an optimiser's loop
    // analysis, and a defeated [loop] means 27 unrolled copies of this body at every call
    // site — which is most of this shader stack's compile time.
    [loop] for (int c = 0; c < 27; c++)
    {
        float3 g = float3(c % 3, (c / 3) % 3, c / 9) - 1.0;
        float3 o = float3(hash31(i + g), hash31(i + g + 31.7), hash31(i + g + 77.3));
        d = min(d, length(g + o - f));
    }
    return d;
}

// ── Impact craters ─────────────────────────────────────────────────────────────────
// Worley distance alone — the obvious way to do this — gives exactly one crater per
// lattice cell, all the same size, evenly spaced. That is bubble wrap: it reads as a
// pattern stamped onto a ball, which is precisely what makes a cratered body look
// fake. Real cratered ground is a RECORD: many impacts at many scales over billions
// of years, most of the surface empty, a few great basins, small pits everywhere and
// frequently cut through by the large ones.
//
// So each cell decides for itself whether it was hit at all, and if so with what
// radius, depth and degree of erosion; where craters overlap, the youngest takes the
// ground, the way an impact actually excavates it. Returns a height field — negative
// inside a bowl, positive on its raised rim — which both the colour pass and the
// relief normal read, so a crater is lit as the pit it is rather than outlined as a
// ring drawn around it.
// How far a crater reaches, in crater radii: 1.0 is the rim crest, the rest is the
// ejecta blanket settling back to the plain. Craters are capped small enough that this
// whole reach stays inside one cell, which is what makes the single-ring neighbour scan
// exact rather than merely close — a crater cut off at the cell border leaves a straight
// seam across the surface, and straight lines are not something impacts make.
//
// Reach and radius trade against each other under that one-cell budget, and the trade is
// won by RADIUS. A generous ejecta blanket is a smooth mid-tone that pixel art cannot
// spend levels on anyway, while the crater's dark floor is the entire reason the feature
// reads at all — so the blanket is kept short and the hole made wide.
#define CRATER_REACH 1.2

// One coverage figure for the base octave, shared by every reading of it. Relief and albedo
// are two views of the SAME craters, and because coverage is a threshold on the same hash, a
// mismatch does not shift the craters — it makes one set a subset of the other, so a fraction
// of the craters get drawn with no relief under them and read as stains. That bug has now
// happened twice by hand-copying the number, so it stops being a number.
#define CRATER_COVERAGE      0.92
#define CRATER_COVERAGE_FINE 0.80

float CraterOctave(float3 p, float seedOff, float coverage)
{
    float3 i = floor(p), f = frac(p);

    // Craters COMPETE for the ground rather than adding to each other.
    //
    // Summing their profiles is the obvious way and the wrong one. Two overlapping rims
    // stack into a bright ridge and three into a lump, so a dense field becomes a mass of
    // welded tubes instead of a pitted surface — and no amount of shading fixes that,
    // because the shape being shaded is wrong. An impact is excavation: the fresh crater
    // DESTROYS the ground it lands on, including whatever crater was there before. So each
    // crater is weighted by its youth and by how far inside it the point lies, and the
    // winner takes the ground. Where a young crater overlaps an old one you get a clean bite out of
    // the old rim, which is what a real cratered surface is made of.
    //
    // The weights are normalised rather than max()'d so the field stays continuous:
    // every crater's influence falls to zero at its own edge, so the hand-off between
    // two craters never steps.
    float acc = 0.0, wsum = 0.0;

    // [loop], not the unrolled default. These bounds are constant, so the compiler
    // would happily expand 27 copies of the body — and this function is called five
    // times per pixel on a rocky body (twice for colour, three times for the relief
    // gradient), which turns into tens of thousands of instructions for the optimizer
    // to chew through. That is paid as a frozen editor on every shader import, and
    // again by everyone who ever imports the package.
    [loop] for (int c = 0; c < 27; c++)
    {
        float3 g = float3(c % 3, (c / 3) % 3, c / 9) - 1.0;
        float3 cell = i + g + seedOff;
        if (hash31(cell) > coverage) continue;      // this cell was never hit

        // Steep size law: craters are overwhelmingly small, with the occasional basin.
        // The exponent matters more than the bounds — a flat draw gives a field of
        // similar dents, which reads as a pattern, while the real record is a scatter of
        // pits under a few big ones.
        float radius = lerp(0.16, 0.72, pow(hash31(cell + 5.51), 2.4));

        // ── REJECT BEFORE PAYING FOR THE OFFSET ───────────────────────────────────────
        // The crater's centre is somewhere in its cell, so the closest it can possibly be
        // is the distance to the cell's nearest corner. Testing that first throws out the
        // roughly twenty of twenty-six neighbours that cannot reach this point, BEFORE
        // their three offset hashes are computed. Every cell dropped here would have hit
        // the real distance test one line further down, so the field is unchanged.
        float3 lo = max(max(f - g - 1.0, g - f), 0.0);
        float bound = radius * (CRATER_REACH * 1.15);
        if (dot(lo, lo) > bound * bound) continue;

        float3 o = float3(hash31(cell + 31.7), hash31(cell + 77.3), hash31(cell + 13.1));

        float3 dv = g + o - f;
        float dist = length(dv);
        if (dist > bound) continue;

        // Irregular outline. A circle is the one shape an impact does not leave: the hole
        // takes the shape of the rock that failed around it, so real rims are scalloped
        // and faintly polygonal. The distortion axis is the crater's own offset vector,
        // which is already in hand — the whole wobble costs one dot product.
        float3 axis = normalize(o - 0.5 + 1e-4);
        float lobe = dot(dv / max(dist, 1e-4), axis);
        float d = dist / max(radius * (1.0 + 0.17 * (lobe * lobe - 0.34)), 1e-4);
        if (d >= CRATER_REACH) continue;

        float age = hash31(cell + 91.13);           // 1 = freshest
        float rimH = 0.20 + 0.16 * age;

        // Profile, in units of depth. A crater is a hole with a FLAT floor, a wall
        // climbing to the crest, and ejecta thrown outside it — not a paraboloid. The old
        // bowl put its brightest ring inside the crater and never reached zero at the
        // edge, which is most of why the pits read as raised doughnuts.
        float prof;
        if (d < 1.0)
        {
            // The floor runs out to 0.72 of the radius, not 0.55. That number is set by the
            // coarsest medium this has to survive: in pixel art a body is a few dozen texels
            // across, so a crater whose dark floor covers only half its own radius quantizes
            // to nothing while its mid-tone wall rounds to the plain — the craters simply
            // stop existing below a certain size. A wide floor and a short wall is also the
            // truer profile for anything but the freshest impact.
            prof = lerp(-1.0, rimH, smoothstep(0.72, 1.0, d));
            // Central peak: the floor rebounds after a large enough impact. Only the big
            // ones get it, and it is the detail that says "basin" instead of "dent".
            prof += smoothstep(0.56, 0.70, radius) * 0.26 * saturate(1.0 - d * 3.0);
        }
        else
        {
            float e = (d - 1.0) * (1.0 / (CRATER_REACH - 1.0));
            prof = rimH * (1.0 - e) * (1.0 - e);
        }

        // Erosion: old craters survive only as shallow ghosts, and having both fresh and
        // worn ones in the same view is most of what reads as age.
        float t = saturate(1.0 - d * (1.0 / CRATER_REACH));
        float w = (0.04 + age * age * age) * t * t * t;
        acc += prof * (0.25 + 0.75 * age) * w;
        wsum += w;
    }

    return acc / max(wsum, 1e-4);
}

/// Two size classes of crater over one another — the small ones peppering the large.
// Two octaves, both close to saturated. A cratered body is SATURATED with craters — every
// new impact lands on older ones, which is why the Moon has no smooth ground left outside
// the maria. The old field's half-empty coverage left bare plain between tidy pits, and
// bare plain between pits is the look of a texture rather than of four billion years.
// baseOctave is handed back because the relief pass needs exactly this value and would
// otherwise evaluate the identical expression a second time — same point, same scale, same
// seed, same coverage — which on a rocky body is a whole 27-cell scan per pixel for nothing.
float CraterField(float3 p, float scale, out float baseOctave)
{
    baseOctave = CraterOctave(p * scale, 0.0, CRATER_COVERAGE);
    return baseOctave + CraterOctave(p * scale * 2.6, 19.3, CRATER_COVERAGE_FINE) * 0.40;
}

#endif // BIRICH_SPACE_NOISE_INCLUDED
