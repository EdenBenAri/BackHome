using System.Collections.Generic;
using UnityEngine;

namespace BirichStudio.ProceduralSpace
{
    /// <summary>
    /// Everything the shader needs for one composed sky, packed into upload-ready arrays.
    /// Produced by <see cref="SkyComposer"/>, consumed by <see cref="SpaceBackground"/>.
    /// </summary>
    internal sealed class SkyData
    {
        public const int MaxPlanets = 6;   // must match MAX_PLANETS in the shader
        public const int MaxMoons = 6;     // must match MAX_MOONS in the shader

        public SkyArchetype Archetype;

        public readonly Vector4[] PlanetA = new Vector4[MaxPlanets];
        public readonly Vector4[] PlanetB = new Vector4[MaxPlanets];
        public readonly Vector4[] PlanetC = new Vector4[MaxPlanets];
        public readonly Vector4[] PlanetD = new Vector4[MaxPlanets];

        /// Orbit around the system's star: x radius (0 = the body is fixed where it was placed),
        /// y phase, z angular speed, w foreshortening of the orbit's vertical axis.
        public readonly Vector4[] PlanetE = new Vector4[MaxPlanets];
        public readonly Vector4[] PlanetCol = new Vector4[MaxPlanets];
        public readonly Vector4[] PlanetCol2 = new Vector4[MaxPlanets];
        public readonly Vector4[] PlanetCol3 = new Vector4[MaxPlanets];
        public int PlanetCount;

        public readonly Vector4[] MoonA = new Vector4[MaxMoons];
        public readonly Vector4[] MoonB = new Vector4[MaxMoons];
        public readonly Vector4[] MoonCol = new Vector4[MaxMoons];
        public int MoonCount;

        public float NebAmount, NebContrast, FarStarDensity, MidStarDensity, NearStarDensity, HueVariety;
        public NebulaStyle NebStyle;
        public Color NebColA, NebColB, NebColC;
        /// xy: offset into the gas noise field, z: cloud-scale multiplier. Both per sky.
        public Vector4 NebSeed;

        /// xy: offset into the star lattice, zw: the same for distant galaxies.
        public Vector4 StarSeed;

        // Galaxy disc (GalaxyVista archetype). GalaxyA.z (radius) 0 = no galaxy.
        public Vector4 GalaxyA, GalaxyB, GalaxyC;
        public Color GalaxyColA, GalaxyColB;

        // Asteroid belt (AsteroidField archetype). BeltA.w (half width) 0 = no belt.
        public Vector4 BeltA, BeltB, BeltC;
        public Color BeltCol;

        // Lighting. LightDir.xy is the fallback direction for skies without a star and
        // LightDir.z the sun's elevation toward the viewer (low = dramatic crescent);
        // LightDir.w is the PHASE of the star's brightness pulse, so two skies on screen at
        // once never breathe in step (see StarLightPulse in the shader).
        // SunPos.z = 1 marks a positional sun at SunPos.xy (SunPos.w = falloff reference).
        public Vector4 LightDir;
        public Vector4 SunPos;
    }

    /// <summary>
    /// The sky <b>composer</b>: draws a <see cref="SkyArchetype"/> from the seed, runs that
    /// archetype's sub-generators (nebula / star field / celestial bodies) and packs the result
    /// for the <c>Birich Studio/ProceduralSpace</c> material.
    ///
    /// <b>Three-tier decision hierarchy</b> — variety comes from here, not from object count:
    ///   1. <i>Archetype</i>  — what rules the screen? (void / solar system / nebula / orbit / cluster …)
    ///   2. <i>Generator</i>  — within that archetype: nebula thickness, star density, body count
    ///   3. <i>Kind</i>       — every body is generated per its own <see cref="PlanetKind"/>
    ///
    /// <b>Scale contract</b>: a typical camera window is ~30×17 units, so a radius-5 planet covers
    /// a third of the screen — even a "small-looking" body is huge. Three scale classes handle
    /// this, and HERO can only ever be single; everything sharing its sky is forced tiny.
    ///
    /// Placement is rejection sampling: a new body is refused if it sits closer to any placed body
    /// than the sum of their radii times the separation factor. Clumping cannot happen.
    ///
    /// Bodies are sorted far-to-near (ascending parallax) → the shader paints later entries on top.
    /// </summary>
    internal static class SkyComposer
    {
        // ── Determinism: independent decision streams ──────────────────────────────────────────
        //
        // Drawing everything below from ONE sequential generator quietly makes every setting
        // global. A stream is a queue: whoever draws first shifts everyone behind them, so
        // changing the planet-kind weights — a decision about kinds — also changes how many
        // numbers that decision consumes, and from that point on the positions, the moons, the
        // light angle, the nebula and the star field all come out different. Nothing is wrong
        // with the seed; the sky simply re-deals itself.
        //
        // So each decision gets its OWN stream, addressed by (seed, domain, slot) rather than by
        // position in a queue. The nebula's numbers cannot move because a planet changed type, and
        // planet 3 cannot move because planet 1 did. That is the difference between a seed that
        // identifies a sky and a seed that only identifies a sequence.

        private const int DomArchetype = 1;
        private const int DomCounts    = 2;   // how many bodies of each scale class
        private const int DomFeature   = 3;   // galaxy disc / asteroid belt / orbital layout
        private const int DomNebula    = 4;
        private const int DomStars     = 5;
        private const int DomLight     = 6;
        private const int DomPlace     = 7;   // per body: radius, position, spin, tilt, depth
        private const int DomKind      = 8;   // per body: which world it is
        private const int DomLook      = 9;   // per body: palette, ring, clouds
        private const int DomMoonRoll  = 10;  // per body: does it have moons, and how many
        private const int DomMoonParam = 11;  // per body: those moons' orbits

        // Slot numbering for bodies. The classes are given separate ranges on purpose: with one
        // running counter, adding a small body would renumber nothing but removing a mid one would
        // shift every small body's slot and move the whole sky. Gaps cost nothing.
        private const int SlotHero  = 0;
        private const int SlotMid   = 1;    // 1 … 7
        private const int SlotSmall = 8;    // 8 … 15
        private const int SlotFeaturedSun = 24;
        private const int SlotFeaturedStar = 25;

        /// <summary>
        /// A tiny deterministic PRNG (xorshift32), one instance per decision stream.
        ///
        /// It replaces <see cref="System.Random"/> for two reasons. Streams are now created in
        /// quantity, and the framework generator carries a 56-entry state array per instance —
        /// the wrong shape for that. More importantly its algorithm is not contractual and has
        /// already changed once between .NET versions, so a seed only promises the same sky on the
        /// runtime that composed it. An asset that ships seeds to other people's projects cannot
        /// live with that; this is fixed forever.
        /// </summary>
        private sealed class Rng
        {
            private uint _s;

            public Rng(uint state) { _s = state != 0u ? state : 0x9E3779B9u; }

            public uint NextUInt()
            {
                unchecked
                {
                    _s ^= _s << 13;
                    _s ^= _s >> 17;
                    _s ^= _s << 5;
                    return _s;
                }
            }

            /// <summary>Uniform in [0,1), built from the HIGH bits — xorshift's low bits are its weak ones.</summary>
            public double NextDouble() => (NextUInt() >> 8) * (1.0 / 16777216.0);

            /// <summary>
            /// Uniform in [0, maxExclusive). Takes the HIGH bits, by multiplying the draw into a
            /// 64-bit product and keeping the top half — the same bits <see cref="NextDouble"/>
            /// keeps and for the same reason. A plain modulo consumes the LOW bits, and in an
            /// xorshift those are the weak ones: Next(2) was literally bit zero of the shift
            /// register, and bit zero decided every binary choice the composer makes — spin
            /// direction, orbit direction, belt drift, which side the galaxy sits on.
            /// </summary>
            public int Next(int maxExclusive) =>
                maxExclusive <= 0 ? 0 : (int)(((ulong)NextUInt() * (ulong)maxExclusive) >> 32);

            public int Next(int minInclusive, int maxExclusive) =>
                maxExclusive <= minInclusive
                    ? minInclusive
                    : minInclusive + Next(maxExclusive - minInclusive);
        }

        /// <summary>
        /// The stream for one (seed, domain, slot) address. Fully mixed rather than merely
        /// combined: adjacent slots must not produce visibly related skies, and a plain add or
        /// xor of small numbers leaves exactly that kind of structure behind.
        /// </summary>
        private static Rng Stream(int seed, int domain, int slot)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)domain * 0x85EBCA77u;
                h ^= (uint)slot * 0xC2B2AE3Du;
                h ^= h >> 15; h *= 0x2545F491u;
                h ^= h >> 13; h *= 0x27D4EB2Fu;
                h ^= h >> 16;
                return new Rng(h);
            }
        }

        private struct Body
        {
            public Vector2 center;
            public float radius, parallax, spin, tilt, seed, surfacePPU, atmosphere;
            public float ringInner, ringOuter, cloudAmount, cloudSpin, featureScale;

            // Orbit around the system's star (OrbitalSystem archetype). orbitRadius 0 = fixed.
            public float orbitRadius, orbitPhase, orbitSpeed, orbitSquash;

            /// <summary>
            /// 0 = near, 1 = far. Distance is not something a static image can state by size
            /// alone — a small disc is equally readable as a nearby moon or a remote world — so
            /// every body carries an explicit depth and the shader spends it on the cues that do
            /// state it: contrast, brightness, and how much surface detail survives.
            /// </summary>
            public float depth;
            public PlanetKind kind;
            public Color c1, c2, c3;
            public int moons;

            /// <summary>
            /// This body's stream address. Kept on the body because moons are built after the
            /// far-to-near sort, and the sort is exactly what a stable address must survive.
            /// </summary>
            public int slot;

            /// <summary>
            /// This body's position was placed for a reason and must not be tidied back into the
            /// placement area. Two cases: a hero is deliberately pushed OUT past
            /// <c>heroEdgePush</c> — further than the area it was drawn from — and the star of an
            /// orbital system is the point its planets are drawn around, so moving it moves the
            /// star out of its own system while the orbits stay where they were.
            /// </summary>
            public bool anchored;
        }

        private struct Moon
        {
            public int parent;
            public float orbitRadius, phase, orbitSpeed, radius, inclination, seed, surfacePPU;
            public Color color;
        }

        // Scratch lists reused between rebuilds — composition runs on OnValidate in the editor,
        // so it should not allocate garbage every slider drag.
        /// Current view scale (see Compose). Held here rather than passed through every helper:
        /// almost all of them touch a radius or a position, so it would be in every signature.
        private static float Fit = 1f;

        private static readonly List<Body> Bodies = new List<Body>();
        private static readonly List<Moon> Moons = new List<Moon>();

        /// <summary>
        /// Compose one sky from <paramref name="seed"/> into <paramref name="data"/>.
        ///
        /// A setting changes only what it is about. Turning down lava worlds re-rolls which worlds
        /// they are and what colour they came out; it does not move the planets, re-arrange the
        /// moons, swing the light around or hand you a different nebula. That is not a nicety —
        /// tuning is impossible without it, because you cannot judge a change you cannot isolate.
        /// See the stream domains above for how it is arranged.
        /// </summary>
        /// <param name="viewScale">
        /// How much larger the camera's view is than the profile's reference height. Body sizes
        /// and placement are multiplied by it, so a sky written for one camera frames the same
        /// way on another. 1 leaves everything in the profile's own units.
        /// </param>
        public static void Compose(SpaceBackgroundProfile p, int seed, SkyArchetype? forced,
                                   SkyData data, float viewScale = 1f)
        {
            Fit = Mathf.Max(0.01f, viewScale);
            Bodies.Clear();
            Moons.Clear();

            // One stream per sky-level decision. None of them can disturb another.
            Rng arch = Stream(seed, DomArchetype, 0);
            Rng counts = Stream(seed, DomCounts, 0);
            Rng feat = Stream(seed, DomFeature, 0);
            Rng gas = Stream(seed, DomNebula, 0);
            Rng stars = Stream(seed, DomStars, 0);
            Rng light = Stream(seed, DomLight, 0);

            SkyArchetype archetype = forced ?? PickArchetype(arch, p);
            data.Archetype = archetype;

            float nebAmount, nebContrast, farDensity, nearDensity, hueVariety;
            NebulaStyle nebStyle;

            data.GalaxyA = Vector4.zero;   // no galaxy unless the archetype composes one
            data.BeltA = Vector4.zero;     // no belt either

            switch (archetype)
            {
                case SkyArchetype.SolarSystem:
                    nebStyle = NebulaStyle.Wispy; nebAmount = 0.07f; nebContrast = 2.6f;
                    farDensity = 0.0055f; nearDensity = 0.0015f; hueVariety = 0.35f;
                    AddStar(seed, p);
                    AddBodies(seed, p, counts, small: counts.Next(1, 3), mid: counts.Next(1, 3), hero: false);
                    break;

                case SkyArchetype.NebulaField:
                    // Gas rules: stars fade behind it, a single body stays in silhouette.
                    nebStyle = gas.Next(2) == 0 ? NebulaStyle.Billow : NebulaStyle.Filament;
                    nebAmount = Range(gas, 0.45f, 0.7f); nebContrast = 1.7f;
                    farDensity = 0.003f; nearDensity = 0.0009f; hueVariety = 0.25f;
                    AddBodies(seed, p, counts, small: counts.Next(0, 2), mid: 1, hero: false);
                    break;

                case SkyArchetype.PlanetOrbit:
                    // ONE hero dominates; everything near it is forced tiny.
                    nebStyle = NebulaStyle.Wispy; nebAmount = 0.12f; nebContrast = 2.4f;
                    farDensity = 0.005f; nearDensity = 0.0013f; hueVariety = 0.3f;
                    AddBodies(seed, p, counts, small: counts.Next(1, 3), mid: 0, hero: true);
                    break;

                case SkyArchetype.StarCluster:
                    nebStyle = NebulaStyle.Billow; nebAmount = 0.22f; nebContrast = 2.0f;
                    farDensity = 0.013f; nearDensity = 0.004f; hueVariety = 0.65f;
                    AddBodies(seed, p, counts, small: counts.Next(0, 2), mid: 0, hero: false);
                    break;

                case SkyArchetype.GalaxyVista:
                    // The galaxy is the hero: bodies stay small and sparse, nebula stays faint so
                    // the disc doesn't have to fight colored fog for attention.
                    nebStyle = NebulaStyle.Wispy; nebAmount = 0.08f; nebContrast = 2.8f;
                    farDensity = 0.007f; nearDensity = 0.0018f; hueVariety = 0.5f;
                    ComposeGalaxy(feat, p, data);
                    AddBodies(seed, p, counts, small: counts.Next(0, 3), mid: 0, hero: false);
                    break;

                case SkyArchetype.AsteroidField:
                    // Rubble rules the foreground; distant small planets give it scale.
                    nebStyle = NebulaStyle.Wispy; nebAmount = 0.09f; nebContrast = 2.6f;
                    farDensity = 0.006f; nearDensity = 0.0015f; hueVariety = 0.35f;
                    ComposeBelt(feat, p, data);
                    AddBodies(seed, p, counts, small: counts.Next(1, 3), mid: counts.Next(0, 2), hero: false);
                    break;

                case SkyArchetype.OrbitalSystem:
                    // The star is the subject, so the sky around it stays quiet: thin gas, modest
                    // star field, nothing competing with the arrangement.
                    nebStyle = NebulaStyle.Wispy; nebAmount = 0.06f; nebContrast = 2.8f;
                    farDensity = 0.005f; nearDensity = 0.0012f; hueVariety = 0.35f;
                    ComposeOrbitalSystem(seed, p, feat);
                    break;

                default: // DeepSpace (and not-yet-specialized archetypes)
                    nebStyle = NebulaStyle.Wispy; nebAmount = 0.1f; nebContrast = 2.8f;
                    farDensity = 0.006f; nearDensity = 0.0016f; hueVariety = 0.4f;
                    AddBodies(seed, p, counts, small: counts.Next(1, 4), mid: 0, hero: false);
                    break;
            }

            // Requested on top of whatever archetype the seed drew, so the game sky always
            // has a readable sun and a large nearby star.
            AddFeaturedBodies(seed, p);

            // Bodies choose their own spots independently, so they can land on each other; this is
            // where they settle out. Run before the sort, because it works on the whole set.
            Relax(p);

            // Far-to-near: the shader draws the arrays in order, painting over what came before.
            // Ties broken by slot. parallaxStrength = 0, or a band with both ends equal, gives
            // every body the SAME parallax — both are legal slider positions — and List.Sort is
            // an unstable introsort, so which body covered which was left to whatever order the
            // runtime's sort happened to leave. That is a different sky on a different .NET, from
            // the same seed, which is the one promise this file exists to keep.
            Bodies.Sort((x, y) =>
            {
                int c = x.parallax.CompareTo(y.parallax);
                return c != 0 ? c : x.slot.CompareTo(y.slot);
            });
            BuildMoons(seed, p);
            ComposeLight(light, p, data);

            PickNebulaColors(gas, p, out data.NebColA, out data.NebColB, out data.NebColC);
            // ── Per-sky gas identity ──────────────────────────────────────────────────────
            // The archetype decides how MUCH gas there is and what character it has; these decide
            // which particular cloud you are looking at. Without them every sky sampled the same
            // noise field at the same scale, so all the archetype's work produced one nebula in
            // several colours.
            data.NebSeed = new Vector4(Range(gas, -900f, 900f), Range(gas, -900f, 900f),
                                       Range(gas, 0.65f, 1.55f), 0f);

            // The star field needs the same treatment as the gas. Its lattice is anchored to
            // world space so stars hold still under a moving camera, but WHICH cells hold a
            // star was decided by fixed offsets — so every sky in the package shipped the same
            // constellation. Whole numbers, because the lattice hash floors its input and a
            // fractional offset would quietly do nothing.
            data.StarSeed = new Vector4(Mathf.Round(Range(stars, -4000f, 4000f)),
                                        Mathf.Round(Range(stars, -4000f, 4000f)),
                                        Mathf.Round(Range(stars, -4000f, 4000f)),
                                        Mathf.Round(Range(stars, -4000f, 4000f)));

            // Style is drawn per sky rather than fixed per archetype: wispy, billowing and
            // filamentary gas look genuinely different, and pinning one to each archetype threw
            // away two thirds of that for no reason. NebulaField keeps the structured styles,
            // since a wispy nebula field is a contradiction.
            if (archetype != SkyArchetype.NebulaField)
            {
                int roll = gas.Next(10);
                nebStyle = roll < 5 ? NebulaStyle.Wispy
                         : roll < 8 ? NebulaStyle.Billow : NebulaStyle.Filament;
            }

            // Contrast varies per sky too — it is the difference between a soft haze and a
            // hard-edged cloud, and one value for a whole archetype throws that away.
            nebContrast *= Range(gas, 0.8f, 1.3f);

            data.NebAmount = nebAmount * p.nebulaIntensity;
            data.NebStyle = nebStyle;
            data.NebContrast = nebContrast;
            data.FarStarDensity = farDensity * p.starDensityScale;
            // Mid layer sits between the two archetype-chosen densities (geometric mean keeps
            // the ratio meaningful for both sparse and dense skies).
            data.MidStarDensity = Mathf.Sqrt(farDensity * nearDensity) * p.starDensityScale;
            data.NearStarDensity = nearDensity * p.starDensityScale;
            data.HueVariety = hueVariety;

            Pack(data);
        }

        // ── Scaled scale classes ───────────────────────────────────────────────────────────────
        // planetScale multiplies all three together rather than being applied at each use site.
        // Every comparison against a class boundary (is this body large enough for a gas giant?
        // for moons?) has to move with it, and a scale that only reached the drawn radius would
        // leave those thresholds behind — small bodies would start qualifying as giants.
        private static Vector2 HeroR(SpaceBackgroundProfile p) => p.heroRadius * p.planetScale * Fit;
        private static Vector2 MidR(SpaceBackgroundProfile p) => p.midRadius * p.planetScale * Fit;
        private static Vector2 SmallR(SpaceBackgroundProfile p) => p.smallRadius * p.planetScale * Fit;
        private static Vector2 Spread(SpaceBackgroundProfile p) => p.spread * Fit;

        /// <summary>
        /// Scales a body count, keeping the fraction. Rounding 1.5 down every time would make
        /// 1.5 behave exactly like 1; carrying the remainder as a probability means the average
        /// over many skies is the number actually asked for.
        /// </summary>
        private static int ScaleCount(Rng rng, int count, float scale)
        {
            float wanted = count * Mathf.Max(0f, scale);
            int whole = Mathf.FloorToInt(wanted);
            return whole + (Range(rng, 0f, 1f) < wanted - whole ? 1 : 0);
        }

        // ── Feature composition ────────────────────────────────────────────────────────────────

        /// <summary>
        /// A star with planets on concentric orbits, revolving around it.
        ///
        /// Everything else here scatters bodies with rejection sampling, which is right when the
        /// sky is a view out of a window: real skies are not arranged. This one is the opposite
        /// on purpose — the whole point is that the arrangement is READABLE as a system, so the
        /// orbits are regularly spaced and the planets sit on them.
        ///
        /// Periods lengthen with orbital radius, as Kepler requires. That detail is not pedantry:
        /// with equal periods the planets hold their relative angles forever and the system turns
        /// like a solid wheel, which reads as a rotating picture rather than as bodies in orbit.
        /// Differential rates are what make it look like gravity.
        /// </summary>
        private static void ComposeOrbitalSystem(int seed, SpaceBackgroundProfile p, Rng layout)
        {
            // The star sits off-centre: dead centre gives a symmetric, diagrammatic composition,
            // and the orbits then leave the frame evenly on all sides with nothing to look at.
            Vector2 sunAt = new Vector2(Range(layout, -6f, 6f), Range(layout, -3.5f, 3.5f)) * Fit
                          + p.skyOffset;
            float sunRadius = Range(layout, 2.2f, 3.4f) * p.planetScale * Fit;

            Rng sunRng = Stream(seed, DomPlace, SlotHero);
            Color core = new Color(1f, Range(sunRng, 0.82f, 0.95f), Range(sunRng, 0.45f, 0.7f));
            Bodies.Add(new Body
            {
                slot = SlotHero,
                anchored = true,      // the planets orbit this point; it cannot be nudged
                center = sunAt,
                radius = sunRadius,
                depth = 0f,
                parallax = ParallaxFor(p, 0.35f),
                spin = SpinFor(sunRng, p),
                tilt = Range(sunRng, p.tiltRange.x, p.tiltRange.y) * Mathf.Deg2Rad,
                seed = Range(sunRng, 0f, 100f),
                surfacePPU = SurfacePPUFor(p, sunRadius),
                atmosphere = 1.4f,
                featureScale = 1f,
                kind = PlanetKind.Star,
                c1 = core, c2 = core, c3 = new Color(1f, 0.55f, 0.2f),
            });

            // Seen from anywhere but straight above, an orbit is an ellipse. One shared
            // foreshortening for the whole system is what says these orbits share a plane —
            // giving each its own would read as unrelated bodies that happen to be circling.
            float squash = Range(layout, 0.28f, 0.62f);
            int count = layout.Next(3, Mathf.Min(5, SkyData.MaxPlanets - 1) + 1);
            float orbit = sunRadius + Range(layout, 3.5f, 5f) * Fit;

            for (int i = 0; i < count; i++)
            {
                int slot = SlotMid + i;
                Rng place = Stream(seed, DomPlace, slot);
                Rng kindRng = Stream(seed, DomKind, slot);
                Rng look = Stream(seed, DomLook, slot);
                Rng moonRoll = Stream(seed, DomMoonRoll, slot);

                // Capped against the star. What holds a system together is MASS, not size, and a
                // star outweighs its largest planet by a factor of a thousand while being only
                // ten times its radius — but this archetype is the one view where that ratio is
                // actually legible, because we are outside the system and everything in frame is
                // at roughly the same distance. A planet drawn larger than the star it orbits
                // reads as wrong immediately, even to someone who could not say why.
                //
                // (The scattered SolarSystem archetype deliberately does NOT do this: there we
                // are inside the system looking at a distant sun past nearby planets, so the
                // sun's small apparent size is the correct reading.)
                float radius = Range(place, SmallR(p).x, Mathf.Min(MidR(p).y * 0.8f, sunRadius * 0.55f));
                float spin = SpinFor(place, p);
                float tilt = Range(place, p.tiltRange.x, p.tiltRange.y) * Mathf.Deg2Rad;
                float surfSeed = Range(place, 0f, 100f);

                PlanetKind kind = PickKind(kindRng, p, radius);

                // Ring first, clouds next, palette last — and all three drawn unconditionally.
                // A draw made only for some kinds would put the stream back in the state this
                // whole rework exists to remove, just one body smaller.
                bool ring = WantsRing(look, kind, p);
                float ringIn = Range(look, 1.35f, 1.5f);
                float ringOut = Range(look, 1.9f, 2.4f);
                float cloudRoll = Range(look, 0.35f, 0.6f);
                float cloudRate = Range(look, 0.12f, 0.3f);
                Palette(look, kind, out Color c1, out Color c2, out Color c3);

                // Period from the orbit's size, then jittered: exact Kepler across a handful of
                // planets is a metronome, and the eye reads perfect regularity as machinery.
                float period = Mathf.Pow(orbit, 1.5f) * Range(layout, 2.2f, 3.0f);

                // Depth follows the orbit so the outer planets recede — the cue that turns a flat
                // ring of discs into a system with near and far sides.
                float depth = Mathf.Clamp01(0.15f + i / Mathf.Max(1f, count - 1f) * 0.6f);

                bool wantsMoons = radius >= MidR(p).x && Range(moonRoll, 0f, 1f) < p.moonChance;
                int moonCount = MoonCountFor(moonRoll, p);

                Bodies.Add(new Body
                {
                    slot = slot,
                    center = sunAt,                    // the shader places it from the orbit
                    radius = radius,
                    depth = depth,
                    parallax = ParallaxFor(p, depth),
                    spin = spin,
                    tilt = tilt,
                    seed = surfSeed,
                    surfacePPU = SurfacePPUFor(p, radius),
                    atmosphere = AtmosphereFor(kind),
                    ringInner = ring ? ringIn : 0f,
                    ringOuter = ring ? ringOut : 0f,
                    cloudAmount = kind == PlanetKind.Terran ? cloudRoll : 0f,
                    cloudSpin = spin * cloudRate,
                    featureScale = FeatureScaleFor(kind),
                    kind = kind,
                    c1 = c1, c2 = c2, c3 = c3,
                    moons = wantsMoons ? moonCount : 0,
                    orbitRadius = orbit,
                    orbitPhase = Range(layout, 0f, Mathf.PI * 2f),
                    orbitSpeed = 6.2831853f / period * (layout.Next(6) == 0 ? -1f : 1f),
                    orbitSquash = squash,
                });

                orbit += Range(layout, 3.2f, 5.2f) * Fit;
            }
        }

        /// <summary>
        /// Decides where the sky's light comes from.
        ///
        /// If the composed sky contains a star, that star becomes a real POSITIONAL light: the
        /// shader lights every body from its own angle toward it. This is what makes a solar
        /// system read as one system rather than as unrelated spheres sharing a light rig —
        /// planets flanking the sun turn their lit faces toward it, and the far ones sit in
        /// visibly weaker light.
        ///
        /// Without a star, an off-screen sun is assumed and only its angle matters; it is drawn
        /// from the seed so every sky has its own light, still deterministic.
        /// </summary>
        private static void ComposeLight(Rng rng, SpaceBackgroundProfile p, SkyData data)
        {
            if (!p.proceduralLight)
            {
                Vector2 fixedDir = p.lightDirection.normalized;
                data.LightDir = new Vector4(fixedDir.x, fixedDir.y, p.lightElevation, 0f);
                data.SunPos = Vector4.zero;
                return;
            }

            // Elevation matters more than angle: it decides whether this sky shows fully lit
            // globes or dramatic crescents, so it is drawn per sky just like the angle.
            float angle = Range(rng, 0f, Mathf.PI * 2f);
            float elevation = Range(rng, Mathf.Min(p.lightElevationRange.x, p.lightElevationRange.y),
                                         Mathf.Max(p.lightElevationRange.x, p.lightElevationRange.y));
            // w: the pulse phase. Where this sky's star sits in its own brightness cycle,
            // derived from values ALREADY DRAWN rather than from a fresh one. A new draw here
            // would sit in the middle of this stream and shift every decision behind it, so
            // adding a pulse would have moved skies that have nothing to do with it — the
            // failure mode rule 1 of the determinism contract exists to prevent.
            data.LightDir = new Vector4(Mathf.Cos(angle), Mathf.Sin(angle), Mathf.Max(0.08f, elevation), angle);
            data.SunPos = Vector4.zero;

            for (int i = 0; i < Bodies.Count; i++)
            {
                if (Bodies[i].kind != PlanetKind.Star) continue;
                // Falloff reference: roughly the sky's diagonal, so the dimming spans the whole
                // composition instead of clipping to its minimum a few units out.
                // Scaled with everything else: the falloff has to span the composition it is
                // lighting, and the composition grows with the camera.
                float reference = Mathf.Max(1f, Spread(p).magnitude * 1.6f);
                data.SunPos = new Vector4(Bodies[i].center.x, Bodies[i].center.y, 1f, reference);
                // A star that is actually in frame phases from its own seed, so the body and its
                // rhythm belong to each other: reseeding the sky gives it a different one.
                data.LightDir.w = Mathf.Repeat(Bodies[i].seed * 7.919f, Mathf.PI * 2f);
                break;
            }
        }

        /// <summary>Compose the spiral galaxy disc for the GalaxyVista archetype.</summary>
        private static void ComposeGalaxy(Rng rng, SpaceBackgroundProfile p, SkyData data)
        {
            // Fit-scaled like the bodies. Without it the disc kept a fixed world size while the
            // planets grew with the camera, so the same sky read as "a galaxy behind planets" on
            // one camera and "a smudge between planets" on another.
            float radius = Range(rng, 7f, 12f) * Fit;
            // Push the disc off-center so it composes with the scene instead of sitting like a
            // logo in the middle.
            Vector2 center = new Vector2(Range(rng, 4f, 11f) * (rng.Next(2) == 0 ? 1f : -1f),
                                         Range(rng, -4.5f, 4.5f)) * Fit + p.skyOffset;

            data.GalaxyA = new Vector4(center.x, center.y, radius, 0.045f);
            data.GalaxyB = new Vector4(Range(rng, 0f, Mathf.PI * 2f),      // screen rotation
                                       Range(rng, 0.25f, 0.55f),           // inclination squash
                                       Range(rng, 0f, 100f),               // seed
                                       rng.Next(2, 4));                    // arm count 2-3
            data.GalaxyC = new Vector4(Range(rng, 0.75f, 1.05f),           // brightness
                                       Range(rng, 2.4f, 3.8f),             // winding
                                       Range(rng, 0.12f, 0.2f),            // core size
                                       6.2831853f / Range(rng, 110f, 220f)); // slow majestic spin

            // Core is always warm white; arms borrow the nebula palette so the sky stays coherent.
            data.GalaxyColA = new Color(1f, Range(rng, 0.86f, 0.95f), Range(rng, 0.7f, 0.82f));
            PickNebulaColors(rng, p, out Color armA, out _, out _);
            data.GalaxyColB = Color.Lerp(armA, new Color(0.75f, 0.82f, 1f), Range(rng, 0.25f, 0.55f));
        }

        /// <summary>Compose the rubble band for the AsteroidField archetype.</summary>
        private static void ComposeBelt(Rng rng, SpaceBackgroundProfile p, SkyData data)
        {
            // Everything with a length here is Fit-scaled — band position, band width, the lattice
            // the rocks sit on and the rocks themselves. Scaling only some of them would change
            // the rubble's grain rather than its size, which is a different belt, not the same
            // belt seen from a different camera.
            data.BeltA = new Vector4(Range(rng, -3f, 3f) * Fit + p.skyOffset.x,
                                     Range(rng, -3f, 3f) * Fit + p.skyOffset.y,
                                     Range(rng, -0.45f, 0.45f) + (rng.Next(2) == 0 ? 0f : Mathf.PI * 0.5f),
                                     Range(rng, 1.6f, 2.8f) * Fit);        // half width
            // ── THE ONE-CELL BUDGET ────────────────────────────────────────────────────────
            // A rock is drawn by its own cell and by nobody else — the shader does no neighbour
            // scan, which is what keeps the belt affordable. So a rock that reaches past its cell
            // wall is not drawn out there; it is SLICED, along a perfectly straight line, and a
            // straight-edged asteroid is the one shape that gives the whole field away.
            //
            // The budget: a centre sits at worst 0.35 of a cell from the wall, and the silhouette
            // reaches 1.486 × the radius (1.05 from the elongation, 1.415 from the shape noise).
            // So the radius cannot exceed 0.35 / 1.486 = 0.235 — the old maximum was 0.42.
            //
            // The radius is a fraction OF A CELL, so shrinking both together would shrink the
            // rubble by the square and leave a belt of specks. The cell grows instead: rocks keep
            // their size in world units and simply stand further apart. Density is traded, not
            // size — one rock per cell is the ceiling, so a coarser lattice can only hold fewer,
            // and rock chance is raised to give most of it back.
            data.BeltB = new Vector4(Range(rng, 1.30f, 1.90f) * Fit,       // cell size
                                     Range(rng, 0.55f, 0.80f),             // rock chance
                                     Range(rng, 0.15f, 0.4f) * (rng.Next(2) == 0 ? 1f : -1f), // drift
                                     0.16f);                               // parallax: near-ish layer
            data.BeltC = new Vector4(Range(rng, 0f, 100f),                 // seed
                                     1f,                                   // brightness
                                     Range(rng, 0.100f, 0.140f),           // min rock radius (of a cell)
                                     Range(rng, 0.185f, 0.235f));          // max rock radius (of a cell)

            float warm = Range(rng, 0.5f, 0.62f);
            data.BeltCol = new Color(warm, warm * Range(rng, 0.88f, 0.96f), warm * Range(rng, 0.76f, 0.88f));
        }

        // ── Archetype & palette draws ──────────────────────────────────────────────────────────

        /// <summary>
        /// Weighted choice that behaves like a control instead of like a dial on a slot machine.
        ///
        /// The obvious implementation — sum the weights, draw one number in [0,total), walk the
        /// list subtracting — is correct on average and wrong to use. It lays the options end to
        /// end on a line and drops a single dart: raising ONE weight widens that option's segment
        /// and slides every segment after it along, so the dart lands in a different neighbour.
        /// Ask for more rocky worlds and the ice world that did not become rocky becomes a gas
        /// giant instead, because ice's slice moved and gas's slice moved into where the dart was.
        /// Nothing is broken and the result is still correctly distributed, which is exactly why
        /// it is so confusing: you changed one number and the sky argues back.
        ///
        /// So each option gets its OWN draw and they race: option i finishes at −ln(u_i)/w_i and
        /// the earliest wins. This is the standard exponential race, and it produces the same
        /// distribution (P(i) = w_i / Σw) with the property the line does not have — a weight only
        /// moves its own finishing time. Raise rocky and rocky wins races it used to lose; every
        /// race it still loses is settled between exactly the same two options as before. Turn it
        /// back down and the sky you had comes back.
        /// </summary>
        /// <returns>The chosen index, or -1 if every weight was zero.</returns>
        private static int PickWeighted(Rng rng, float[] weights, int count)
        {
            int best = -1;
            float bestKey = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                // Drawn for EVERY option, including the ones weighted out. Skipping the draw for a
                // zero weight would make the stream depend on which options are enabled, which is
                // the coupling this whole file is arranged to avoid.
                double u = 1.0 - rng.NextDouble();      // (0,1] — ln(0) is not a finishing time
                float w = weights[i];
                if (w <= 0f) continue;

                float key = (float)(-System.Math.Log(u) / w);
                if (key < bestKey) { bestKey = key; best = i; }
            }

            return best;
        }

        /// Scratch for PickWeighted, so a slider drag does not allocate an array per body.
        private static readonly float[] Weights = new float[16];

        /// Must match SkyArchetype — and is checked against it once, in the editor, because the
        /// failure is otherwise silent: a newly added archetype past this count is simply never
        /// drawn, and nothing anywhere says so.
        private const int ArchetypeCount = 8;

#if UNITY_EDITOR
        static SkyComposer()
        {
            int actual = System.Enum.GetValues(typeof(SkyArchetype)).Length;
            if (actual != ArchetypeCount)
                Debug.LogError($"[ProceduralSpace] SkyComposer.ArchetypeCount is {ArchetypeCount} " +
                               $"but SkyArchetype has {actual} entries — archetypes past the count " +
                               "will never be drawn.");
        }
#endif

        private static SkyArchetype PickArchetype(Rng rng, SpaceBackgroundProfile p)
        {
            // A literal, not Enum.GetValues: that call allocates an array and goes through
            // reflection, and Compose runs on every OnValidate — i.e. on every frame of every
            // slider drag — to be told the number 8.
            int n = Mathf.Min(ArchetypeCount, Weights.Length);
            for (int i = 0; i < n; i++) Weights[i] = WeightAt(p, i);

            int pick = PickWeighted(rng, Weights, n);
            return pick < 0 ? SkyArchetype.DeepSpace : (SkyArchetype)pick;
        }

        private static float WeightAt(SpaceBackgroundProfile p, int i) =>
            p.archetypeWeights != null && i < p.archetypeWeights.Length
                ? Mathf.Max(0f, p.archetypeWeights[i]) : 0f;

        /// <summary>Draws one triple from the palette (gas A, gas B, hot core).</summary>
        private static void PickNebulaColors(Rng rng, SpaceBackgroundProfile p,
                                             out Color a, out Color b, out Color c)
        {
            int groups = p.nebulaPalette != null ? p.nebulaPalette.Length / 3 : 0;
            if (groups <= 0)
            {
                a = new Color(0.42f, 0.16f, 0.58f);
                b = new Color(0.16f, 0.13f, 0.45f);
                c = new Color(0.72f, 0.34f, 0.42f);
                return;
            }

            // The group sets the family; the jitter keeps two skies from the same group from
            // being the same nebula twice. Four palette groups otherwise means four nebulae.
            int g = rng.Next(groups) * 3;
            a = Jitter(rng, p.nebulaPalette[g], 0.07f);
            b = Jitter(rng, p.nebulaPalette[g + 1], 0.05f);
            c = Jitter(rng, p.nebulaPalette[g + 2], 0.08f);
        }

        // ── Body generation ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A large nearby star, and a sun when the archetype did not already place one.
        ///
        /// The sun stays the farther of the two, so <see cref="ComposeLight"/> — which takes the
        /// first star in far-to-near order — keeps lighting the sky from the sun rather than
        /// from the foreground star.
        /// </summary>
        private static void AddFeaturedBodies(int seed, SpaceBackgroundProfile p)
        {
            if (!p.featuredCloseStar && !p.featuredSun) return;

            int existingSun = -1;
            for (int i = 0; i < Bodies.Count; i++)
            {
                if (Bodies[i].kind != PlanetKind.Star) continue;
                existingSun = i;
                break;
            }

            bool addSun = p.featuredSun && existingSun < 0;
            bool addStar = p.featuredCloseStar;
            int needed = (addSun ? 1 : 0) + (addStar ? 1 : 0);
            if (p.featuredSun && existingSun >= 0)
            {
                Body sun = Bodies[existingSun];
                float minSun = 2.6f * p.planetScale * Fit;
                if (sun.radius < minSun)
                {
                    sun.radius = minSun;
                    sun.atmosphere = Mathf.Max(sun.atmosphere, 1.65f);
                    Bodies[existingSun] = sun;
                }
            }
            if (needed == 0) return;

            MakeRoom(needed);

            Rng layout = Stream(seed, DomPlace, SlotFeaturedSun);
            Vector2 ext = Spread(p);
            float side = layout.Next(2) == 0 ? 1f : -1f;

            if (addSun)
            {
                float radius = Range(layout, 2.6f, 3.5f) * p.planetScale * Fit;
                Vector2 center = new Vector2(side * ext.x * 0.62f, ext.y * 0.5f) + p.skyOffset;
                Color core = new Color(1f, 0.94f, 0.68f);
                Bodies.Add(new Body
                {
                    slot = SlotFeaturedSun,
                    anchored = true,
                    center = center,
                    radius = radius,
                    depth = 0.45f,
                    parallax = ParallaxFor(p, 0.45f),
                    spin = SpinFor(layout, p),
                    tilt = Range(layout, p.tiltRange.x, p.tiltRange.y) * Mathf.Deg2Rad,
                    seed = Range(layout, 0f, 100f),
                    surfacePPU = SurfacePPUFor(p, radius),
                    atmosphere = 1.75f,
                    featureScale = 1f,
                    kind = PlanetKind.Star,
                    c1 = core,
                    c2 = core,
                    c3 = new Color(1f, 0.58f, 0.18f),
                });
            }

            if (!addStar) return;

            float starRadius = Range(layout, 5.4f, 7.2f) * p.planetScale * Fit;
            Vector2 starAt = new Vector2(-side * ext.x * 0.46f, -ext.y * 0.18f) + p.skyOffset;
            Color star = new Color(0.8f, 0.9f, 1f);
            Bodies.Add(new Body
            {
                slot = SlotFeaturedStar,
                anchored = true,
                center = starAt,
                radius = starRadius,
                depth = 0.05f,
                parallax = ParallaxFor(p, 0.05f),
                spin = SpinFor(layout, p),
                tilt = Range(layout, p.tiltRange.x, p.tiltRange.y) * Mathf.Deg2Rad,
                seed = Range(layout, 0f, 100f),
                surfacePPU = SurfacePPUFor(p, starRadius),
                atmosphere = 1.45f,
                // Negative cloud amount is the shader's flag for a point star (core, halo,
                // diffraction spikes) instead of a self-lit sphere.
                cloudAmount = -1f,
                featureScale = 1.1f,
                kind = PlanetKind.Star,
                c1 = star,
                c2 = new Color(0.94f, 0.97f, 1f),
                c3 = new Color(0.5f, 0.7f, 1f),
            });
        }

        /// <summary>Drop the smallest ordinary worlds until the featured bodies fit in the shader's planet cap.</summary>
        private static void MakeRoom(int needed)
        {
            while (Bodies.Count + needed > SkyData.MaxPlanets)
            {
                int victim = -1;
                float smallest = float.MaxValue;
                for (int i = 0; i < Bodies.Count; i++)
                {
                    if (Bodies[i].kind == PlanetKind.Star) continue;
                    if (Bodies[i].radius >= smallest) continue;
                    smallest = Bodies[i].radius;
                    victim = i;
                }
                if (victim < 0) return;
                Bodies.RemoveAt(victim);
            }
        }

        /// <summary>The solar-system archetype's sun — small but self-lit, with a strong corona.</summary>
        private static void AddStar(int seed, SpaceBackgroundProfile p)
        {
            Rng rng = Stream(seed, DomPlace, SlotHero);
            float radius = Range(rng, 1.1f, 2.2f) * p.planetScale * Fit;
            float spin = SpinFor(rng, p);
            float tilt = Range(rng, p.tiltRange.x, p.tiltRange.y) * Mathf.Deg2Rad;
            float surfSeed = Range(rng, 0f, 100f);
            Color core = new Color(1f, Range(rng, 0.82f, 0.95f), Range(rng, 0.45f, 0.7f));
            Vector2 center = PickCenter(rng, p, radius, false);
            // The sun is the light source for everything else in frame, so it stays crisp and
            // fully bright: a hazy, receding star would leave the sky lit from a body that looks
            // further away than the things it is lighting.
            Bodies.Add(new Body
            {
                slot = SlotHero,
                center = center,
                radius = radius,
                depth = 0f,
                parallax = ParallaxFor(p, 0.35f),
                spin = spin,
                tilt = tilt,
                seed = surfSeed,
                surfacePPU = SurfacePPUFor(p, radius),
                atmosphere = 1.4f,          // strong corona
                featureScale = 1f,
                kind = PlanetKind.Star,
                c1 = core,
                c2 = core,
                c3 = new Color(1f, 0.55f, 0.2f),
            });
        }

        private static void AddBodies(int seed, SpaceBackgroundProfile p, Rng counts,
                                      int small, int mid, bool hero)
        {
            // The hero is never scaled away: an archetype that asked for one giant is defined by
            // having it, and a count multiplier is about how crowded the sky is, not about
            // overruling its shape.
            if (hero) AddBody(seed, SlotHero, p, HeroR(p), true);
            mid = ScaleCount(counts, mid, p.bodyCountScale);
            small = ScaleCount(counts, small, p.bodyCountScale);
            for (int i = 0; i < mid; i++) AddBody(seed, SlotMid + i, p, MidR(p), false);
            for (int i = 0; i < small; i++) AddBody(seed, SlotSmall + i, p, SmallR(p), false);
        }

        /// <summary>
        /// One body, composed from its own four streams.
        ///
        /// The split is the point: <i>place</i> answers where and how big, <i>kind</i> answers what
        /// it is, <i>look</i> answers what it looks like, <i>moons</i> answers what orbits it. Ask
        /// for fewer lava worlds and only the middle two answers move — the sky you had keeps its
        /// layout, and the change reads as the change you made instead of as a new seed.
        ///
        /// Within <i>place</i>, everything is drawn BEFORE the position: rejection sampling makes a
        /// variable number of attempts, so anything drawn after it would shift whenever a body
        /// upstream changed size and forced one more retry.
        /// </summary>
        private static void AddBody(int seed, int slot, SpaceBackgroundProfile p,
                                    Vector2 radiusRange, bool hero)
        {
            if (Bodies.Count >= SkyData.MaxPlanets) return;

            Rng place = Stream(seed, DomPlace, slot);
            Rng kindRng = Stream(seed, DomKind, slot);
            Rng look = Stream(seed, DomLook, slot);
            Rng moonRoll = Stream(seed, DomMoonRoll, slot);

            float radius = Range(place, radiusRange.x, radiusRange.y);
            float spin = SpinFor(place, p);
            float tilt = Range(place, p.tiltRange.x, p.tiltRange.y) * Mathf.Deg2Rad;
            float surfSeed = Range(place, 0f, 100f);

            // A hero dominates the composition, so it is never allowed to be the faintest thing
            // on screen; the small bodies are the ones free to recede. Beyond that the draw is
            // deliberately wide — what makes a sky read as deep is bodies at DIFFERENT depths in
            // the same frame, and a narrow band would put them all back on one plane.
            float depth = hero ? Range(place, 0.15f, 0.5f) : Range(place, 0.1f, 1f);

            Vector2 center = PickCenter(place, p, radius, hero);

            PlanetKind kind = PickKind(kindRng, p, radius);

            // Every look draw is unconditional, including the ones a given kind will not use. A
            // draw made only for gas giants is a draw whose absence shifts everything behind it,
            // which is the failure this rework exists to remove.
            bool ring = WantsRing(look, kind, p);
            float ringIn = Range(look, 1.35f, 1.5f);
            float ringOut = Range(look, 1.9f, 2.4f);
            float cloudRoll = Range(look, 0.35f, 0.6f);
            // Clouds rotate at a speed DIFFERENT from the surface — the relative slide of the two
            // layers separates depth, one of the strongest 3D cues available.
            float cloudRate = Range(look, 0.12f, 0.3f);
            Palette(look, kind, out Color c1, out Color c2, out Color c3);

            // Moons only on sufficiently large bodies: a tiny dot's moon shrinks to one pixel
            // and cannot be told apart from noise.
            bool wantsMoons = radius >= MidR(p).x && Range(moonRoll, 0f, 1f) < p.moonChance;
            int moonCount = MoonCountFor(moonRoll, p);

            Bodies.Add(new Body
            {
                slot = slot,
                // A hero is placed OUTSIDE the area everything else is scattered in — that is what
                // heroEdgePush does — so the tidy-up at the end of Relax would drag it straight
                // back toward the middle of the frame, which is the one thing the setting exists
                // to prevent. It stops responding above about half the placement area otherwise.
                anchored = hero,
                center = center,
                radius = radius,
                depth = depth,
                parallax = ParallaxFor(p, depth),
                spin = spin,
                tilt = tilt,
                seed = surfSeed,
                surfacePPU = SurfacePPUFor(p, radius),
                atmosphere = AtmosphereFor(kind),
                ringInner = ring ? ringIn : 0f,
                ringOuter = ring ? ringOut : 0f,
                cloudAmount = kind == PlanetKind.Terran ? cloudRoll : 0f,
                cloudSpin = spin * cloudRate,
                featureScale = FeatureScaleFor(kind),
                kind = kind,
                c1 = c1, c2 = c2, c3 = c3,
                moons = wantsMoons ? moonCount : 0,
            });
        }

        /// <summary>
        /// The body's own spot, before anything else in the sky is taken into account.
        ///
        /// Exactly two draws, always — deliberately NOT rejection sampling, which would keep
        /// drawing candidates until one cleared every body already placed. That is correct and it
        /// is DISCONTINUOUS: a body sits at candidate #1 until the moment that candidate stops
        /// clearing, and then it is at candidate #3 on the other side of the frame. Since every
        /// radius change moves the clearance thresholds, dragging the planet-size slider would
        /// teleport planets. A setting that reorganises the composition while you nudge it is a
        /// setting you cannot tune with.
        ///
        /// Separation is enforced afterwards by <see cref="Relax"/> instead, which pushes rather
        /// than re-rolls, and moves continuously with the values that drive it.
        /// </summary>
        private static Vector2 PickCenter(Rng rng, SpaceBackgroundProfile prof, float radius, bool hero)
        {
            Vector2 ext = Spread(prof);
            Vector2 c = new Vector2(Range(rng, -ext.x, ext.x), Range(rng, -ext.y, ext.y));

            if (hero)
            {
                if (c.sqrMagnitude < 1e-4f) c = Vector2.right;
                c = c.normalized * Mathf.Max(c.magnitude, prof.heroEdgePush * Fit + radius * 0.35f);
            }

            // Offset applied AFTER the hero push, so the push is measured from the composition's
            // own centre. Measuring it from the world origin — which is what adding the offset
            // first did — meant that shifting the sky also quietly moved the giant closer to the
            // middle of it.
            return c + prof.skyOffset;
        }

        /// <summary>
        /// Pushes overlapping bodies apart until they clear each other.
        ///
        /// Bigger bodies move less: the share of each push is weighted by the OTHER body's area,
        /// so a pebble gets out of a giant's way rather than shouldering it across the frame. That
        /// is what keeps the composition recognisably the same one while you adjust it.
        ///
        /// Centres are held inside the placement area at the end of every pass. In a sky too
        /// crowded to satisfy, that means the bodies settle with some overlap instead of being
        /// pushed out of the frame — which is the better failure: overlapping bodies are drawn
        /// correctly (they are sorted by depth) and a body shoved past the edge is simply gone.
        /// </summary>
        private static void Relax(SpaceBackgroundProfile p)
        {
            float sep = Mathf.Max(0.5f, p.separationFactor);
            Vector2 limit = Spread(p) * 1.15f;

            // Sixteen passes over at most six bodies is free, and far more than convergence needs.
            for (int pass = 0; pass < 16; pass++)
            {
                bool moved = false;

                for (int i = 0; i < Bodies.Count; i++)
                for (int j = i + 1; j < Bodies.Count; j++)
                {
                    Body a = Bodies[i], b = Bodies[j];
                    // A body on an orbit is positioned by the shader from its orbit; nudging its
                    // stored centre would move the whole orbit, star included.
                    if (a.orbitRadius > 0f || b.orbitRadius > 0f) continue;

                    float need = (a.radius + b.radius) * sep;
                    Vector2 d = b.center - a.center;
                    float dist = d.magnitude;
                    if (dist >= need) continue;

                    // Two bodies at the same point have no axis to separate along, so one is
                    // chosen from the pair's indices — deterministic, and different for each pair.
                    Vector2 dir = dist > 1e-4f
                        ? d / dist
                        : new Vector2(Mathf.Cos(i * 2.399f + j), Mathf.Sin(i * 2.399f + j));

                    float push = need - dist;
                    float ma = a.radius * a.radius, mb = b.radius * b.radius;
                    float total = Mathf.Max(1e-4f, ma + mb);

                    a.center -= dir * (push * mb / total);
                    b.center += dir * (push * ma / total);
                    Bodies[i] = a;
                    Bodies[j] = b;
                    moved = true;
                }

                for (int i = 0; i < Bodies.Count; i++)
                {
                    Body b = Bodies[i];
                    if (b.orbitRadius > 0f || b.anchored) continue;
                    Vector2 rel = b.center - p.skyOffset;
                    rel.x = Mathf.Clamp(rel.x, -limit.x, limit.x);
                    rel.y = Mathf.Clamp(rel.y, -limit.y, limit.y);
                    b.center = rel + p.skyOffset;
                    Bodies[i] = b;
                }

                if (!moved) break;
            }
        }

        /// <summary>
        /// Which kind of world this body is.
        ///
        /// Two things decide it, and they are not equals. The profile's weights say what the
        /// player should meet — a game can remove lava worlds from its universe entirely. SIZE
        /// then has the last word, because some kinds simply cannot be read small: a gas giant's
        /// identity is its banding, and bands across a body a few pixels wide are just noise.
        /// Letting the weights override that would let a user ask for something the renderer
        /// cannot deliver and get mush instead of a refusal.
        /// </summary>
        private static PlanetKind PickKind(Rng rng, SpaceBackgroundProfile p, float radius)
        {
            bool large = radius >= MidR(p).x;

            float[] intrinsic = large ? LargeOdds : SmallOdds;

            int n = Mathf.Min(intrinsic.Length, Weights.Length);
            for (int i = 0; i < n; i++) Weights[i] = intrinsic[i] * WeightFor(p, i);

            // Every kind weighted out: fall back to rock rather than to nothing. Rock is the one
            // body type that reads at any size, so it is the safe answer.
            int pick = PickWeighted(rng, Weights, n);
            return pick < 0 ? PlanetKind.Rocky : (PlanetKind)pick;
        }

        // Intrinsic odds — what suits a body of this size, before the user's preferences. Order
        // follows PlanetKind. Star is 0 in both rows: stars are placed by the archetypes that mean
        // to have one, never drawn as a planet. GasGiant and Toxic are 0 when small, which is why
        // raising their weight changes nothing on a sky made of specks.
        //                                          Rocky  Gas    Ice    Lava   Terran Star   Desert Toxic

        // ── Palette tables ─────────────────────────────────────────────────────────────────
        // Static because Palette runs per body per compose, and compose runs on every frame
        // of every slider drag. Nine array literals rebuilt on each call is pure garbage, and
        // this file's contract is that composing does not allocate.
        private static readonly Color[] Gas =
        {
                        new Color(0.84f, 0.64f, 0.36f), // amber
                        new Color(0.40f, 0.62f, 0.68f), // teal
                        new Color(0.66f, 0.42f, 0.78f), // violet
                        new Color(0.78f, 0.46f, 0.40f), // rust
                        new Color(0.86f, 0.82f, 0.70f), // cream
                        new Color(0.42f, 0.50f, 0.80f), // deep blue
        };

        private static readonly Color[] Ice =
        {
                        new Color(0.70f, 0.83f, 0.95f), // blue white
                        new Color(0.62f, 0.86f, 0.86f), // teal
                        new Color(0.80f, 0.78f, 0.92f), // pale violet
                        new Color(0.88f, 0.90f, 0.88f), // near white
        };

        private static readonly Color[] LavaCrust =
        {
                        new Color(0.33f, 0.22f, 0.20f), // basalt
                        new Color(0.30f, 0.18f, 0.24f), // dark plum
                        new Color(0.36f, 0.26f, 0.18f), // scorched brown
        };

        private static readonly Color[] LavaMelt =
        {
                        new Color(1.00f, 0.45f, 0.12f), // orange
                        new Color(1.00f, 0.26f, 0.16f), // deep red
                        new Color(1.00f, 0.72f, 0.30f), // yellow hot
                        new Color(0.70f, 0.85f, 1.00f), // white hot — a very young world
        };

        private static readonly Color[] TerranLand =
        {
                        new Color(0.34f, 0.54f, 0.32f), // green
                        new Color(0.52f, 0.50f, 0.28f), // ochre
                        new Color(0.46f, 0.34f, 0.42f), // violet
                        new Color(0.30f, 0.48f, 0.46f), // blue green
        };

        private static readonly Color[] TerranSea =
        {
                        new Color(0.10f, 0.24f, 0.46f), // deep blue
                        new Color(0.08f, 0.32f, 0.38f), // teal
                        new Color(0.20f, 0.20f, 0.34f), // slate
        };

        private static readonly Color[] DesertSand =
        {
                        new Color(0.85f, 0.70f, 0.46f), // sand
                        new Color(0.80f, 0.50f, 0.34f), // rust
                        new Color(0.88f, 0.84f, 0.66f), // pale gold
                        new Color(0.72f, 0.62f, 0.58f), // dust grey
        };

        private static readonly Color[] ToxicAcid =
        {
                        new Color(0.60f, 0.72f, 0.30f), // acid green
                        new Color(0.78f, 0.70f, 0.30f), // sulfur
                        new Color(0.70f, 0.45f, 0.62f), // violet haze
                        new Color(0.55f, 0.62f, 0.70f), // ashen
        };

        private static readonly Color[] RockTones =
        {
                        new Color(0.55f, 0.53f, 0.50f), // grey
                        new Color(0.52f, 0.40f, 0.32f), // brown
                        new Color(0.58f, 0.36f, 0.28f), // rust
                        new Color(0.34f, 0.34f, 0.38f), // dark basalt
                        new Color(0.62f, 0.58f, 0.48f), // pale regolith
        };

        private static readonly float[] LargeOdds = { 0.09f, 0.28f, 0.14f, 0.12f, 0.16f, 0f,    0.12f, 0.09f };
        private static readonly float[] SmallOdds = { 0.38f, 0f,    0.20f, 0.14f, 0.14f, 0f,    0.14f, 0f    };

        private static float WeightFor(SpaceBackgroundProfile p, int kind) =>
            p.planetKindWeights != null && kind < p.planetKindWeights.Length
                ? Mathf.Max(0f, p.planetKindWeights[kind])
                : 1f;

        /// <summary>
        /// Whether this body wears a ring.
        ///
        /// Not a gas-giant privilege, which would be neither true nor useful: a ring is debris
        /// that never coalesced, and any body massive enough to hold it can have one. Restricting
        /// them to giants would make the most striking silhouette in the package appear only when
        /// a giant happens to be drawn. Ice and rock keep a lower chance, so a ringed rocky world
        /// stays an event rather than the norm.
        /// </summary>
        private static bool WantsRing(Rng rng, PlanetKind kind, SpaceBackgroundProfile p)
        {
            float odds = kind switch
            {
                PlanetKind.GasGiant => 1.0f,
                PlanetKind.Ice => 0.45f,
                PlanetKind.Rocky => 0.25f,
                PlanetKind.Desert => 0.2f,
                PlanetKind.Toxic => 0.2f,
                _ => 0f,   // no rings on terran worlds or stars
            };
            return odds > 0f && Range(rng, 0f, 1f) < p.ringChance * odds;
        }

        private static float AtmosphereFor(PlanetKind kind) => kind switch
        {
            PlanetKind.GasGiant => 0.55f,
            PlanetKind.Terran => 0.5f,
            PlanetKind.Ice => 0.3f,
            PlanetKind.Lava => 0.35f,
            PlanetKind.Star => 1.4f,
            PlanetKind.Desert => 0.22f,
            PlanetKind.Toxic => 0.6f,   // the whole planet IS atmosphere
            _ => 0.06f,     // rocky: airless, sharp edge
        };

        private static float FeatureScaleFor(PlanetKind kind) => kind switch
        {
            PlanetKind.Rocky => 1.3f,
            PlanetKind.Terran => 0.95f,
            PlanetKind.Lava => 1.15f,
            PlanetKind.Desert => 1.05f,
            PlanetKind.Toxic => 0.9f,
            _ => 1f,
        };

        /// <summary>
        /// Palette for one body.
        ///
        /// Every kind draws from a family of variants rather than a fixed triple, and that is
        /// most of what keeps skies from feeling repetitive. The STRUCTURE already varies per
        /// planet (band count, continent size, crater density, activity), but under a fixed
        /// palette an ice world is always the same blue and a lava world always the same orange,
        /// so the eye — which sorts by colour long before it sorts by pattern — files them as one
        /// again. Structural variety is invisible under a fixed palette.
        /// </summary>
        private static void Palette(Rng rng, PlanetKind kind, out Color c1, out Color c2, out Color c3)
        {
            switch (kind)
            {
                case PlanetKind.GasGiant:
                {
                    c1 = Jitter(rng, Gas[rng.Next(Gas.Length)], 0.10f);
                    c2 = new Color(c1.r * 0.42f, c1.g * 0.40f, c1.b * 0.52f);
                    // The storm is usually a warm bruise, but a cold-cored giant is striking and
                    // rare enough to stay a surprise.
                    c3 = rng.Next(5) == 0
                        ? new Color(0.85f, 0.92f, 1f)
                        : new Color(Mathf.Min(1f, c1.r * 1.25f + 0.2f), c1.g * 0.55f, c1.b * 0.45f);
                    break;
                }

                case PlanetKind.Ice:
                {
                    c1 = Jitter(rng, Ice[rng.Next(Ice.Length)], 0.06f);
                    c2 = new Color(c1.r * 0.40f, c1.g * 0.50f, c1.b * 0.65f);
                    c3 = new Color(0.95f, 0.98f, 1f);
                    break;
                }

                case PlanetKind.Lava:
                {
                    // Crust stays clearly brighter than the void: an almost-black crust vanishes
                    // against the background and the planet decays into disconnected orange dots.
                    c1 = Jitter(rng, LavaCrust[rng.Next(LavaCrust.Length)], 0.08f);
                    c2 = new Color(c1.r * 0.55f, c1.g * 0.50f, c1.b * 0.60f);
                    c3 = LavaMelt[rng.Next(LavaMelt.Length)];
                    break;
                }

                case PlanetKind.Terran:
                {
                    // Vegetation is not obliged to be green: its colour follows whatever the local
                    // chemistry favours, and an ochre or violet biosphere is one of the cheapest
                    // ways to say "this is not Earth" without changing a single generator.
                    c1 = Jitter(rng, TerranLand[rng.Next(TerranLand.Length)], 0.10f);
                    c2 = Jitter(rng, TerranSea[rng.Next(TerranSea.Length)], 0.06f);
                    c3 = new Color(0.92f, 0.95f, 1f);
                    break;
                }

                case PlanetKind.Desert:
                {
                    c1 = Jitter(rng, DesertSand[rng.Next(DesertSand.Length)], 0.08f);
                    c2 = new Color(c1.r * 0.55f, c1.g * 0.45f, c1.b * 0.42f);
                    c3 = new Color(0.95f, 0.9f, 0.8f);
                    break;
                }

                case PlanetKind.Toxic:
                {
                    c1 = Jitter(rng, ToxicAcid[rng.Next(ToxicAcid.Length)], 0.08f);
                    c2 = new Color(c1.r * 0.40f, c1.g * 0.45f, c1.b * 0.55f);
                    c3 = new Color(Mathf.Min(1f, c1.r * 1.3f), Mathf.Min(1f, c1.g * 1.35f), c1.b * 1.1f);
                    break;
                }

                default: // Rocky
                {
                    Color baseRock = Jitter(rng, RockTones[rng.Next(RockTones.Length)], 0.10f);
                    float g = Range(rng, 0.85f, 1.15f);
                    c1 = new Color(baseRock.r * g, baseRock.g * g, baseRock.b * g);
                    c2 = new Color(c1.r * 0.42f, c1.g * 0.40f, c1.b * 0.42f);
                    c3 = new Color(0.82f, 0.80f, 0.76f);
                    break;
                }
            }
        }

        /// <summary>
        /// Nudges a colour off its exact family value. Without it a family of four variants gives
        /// four planets and no more: the eye recognises an exact repeat instantly, while a couple
        /// of percent of drift reads as two individuals of the same type.
        /// </summary>
        private static Color Jitter(Rng rng, Color c, float amount) => new Color(
            Mathf.Clamp01(c.r + Range(rng, -amount, amount)),
            Mathf.Clamp01(c.g + Range(rng, -amount, amount)),
            Mathf.Clamp01(c.b + Range(rng, -amount, amount)));

        private static void BuildMoons(int seed, SpaceBackgroundProfile p)
        {
            // Built AFTER sorting: moons reference their owner by index and sorting shifts indices.
            // The STREAM is keyed by the owner's slot, not by that shifting index — a moon must not
            // change because a body somewhere else in the sky got closer and re-ordered the list.
            for (int i = 0; i < Bodies.Count && Moons.Count < SkyData.MaxMoons; i++)
            {
                Body b = Bodies[i];
                if (b.moons <= 0) continue;
                Rng rng = Stream(seed, DomMoonParam, b.slot);
                for (int m = 0; m < b.moons && Moons.Count < SkyData.MaxMoons; m++)
                {
                    float mr = b.radius * Range(rng, 0.13f, 0.24f);
                    Moons.Add(new Moon
                    {
                        parent = i,
                        orbitRadius = b.radius * Range(rng, 1.55f, 2.5f),
                        phase = Range(rng, 0f, 6.2831853f),
                        // Orbit periods follow the spin: a moon whipping around its planet in a
                        // few seconds is the single most distracting thing a quiet backdrop can
                        // contain, and it destroys the sense of scale that makes the planet read
                        // as large in the first place.
                        orbitSpeed = 6.2831853f / Range(rng, 55f, 140f) * (rng.Next(2) == 0 ? 1f : -1f),
                        radius = mr,
                        inclination = Range(rng, -50f, 50f) * Mathf.Deg2Rad,
                        seed = Range(rng, 0f, 100f),
                        surfacePPU = SurfacePPUFor(p, mr), // a moon derives from its own radius, not its owner's
                        color = new Color(Range(rng, 0.45f, 0.68f), Range(rng, 0.44f, 0.64f), Range(rng, 0.42f, 0.62f)),
                    });
                }
            }
        }

        // ── Derived values ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Aims for the target texel count across the body's diameter, but never uses the raw
        /// result: the block size is first rounded to a WHOLE number of screen pixels and the PPU
        /// derived from it. Every surface block is then exactly 2, 3 or 4 screen pixels —
        /// fractional blocks produce single-pixel lines crawling along edges as the camera pans.
        /// </summary>
        private static float SurfacePPUFor(SpaceBackgroundProfile p, float radius)
        {
            float screenPPU = Mathf.Max(1f, p.pixelsPerUnit);
            float raw = p.targetSurfaceTexels / Mathf.Max(0.01f, radius * 2f);
            float block = Mathf.Clamp(Mathf.Round(screenPPU / Mathf.Max(raw, 0.01f)),
                                      p.surfaceBlockRange.x, p.surfaceBlockRange.y);
            return screenPPU / block;
        }

        /// <summary>
        /// Parallax follows DEPTH, not size. Deriving it from the radius forced one reading of
        /// the sky — every large body far, every small one near — when a small disc is just as
        /// likely to be a nearby moon, and it left the two cues free to contradict each other:
        /// a body could be drawn as distant yet slide across the frame like a near one.
        /// </summary>
        private static float ParallaxFor(SpaceBackgroundProfile p, float depth) =>
            Mathf.Lerp(p.parallaxSmall, p.parallaxLarge, Mathf.Clamp01(depth)) * p.parallaxStrength;

        /// <summary>
        /// How many moons this body would like, capped by the profile.
        ///
        /// The draw always spans the full 1–3 range and the cap is applied afterwards, which makes
        /// the setting behave the way a maximum should: lowering it from 3 to 2 trims the bodies
        /// that wanted three and leaves the one-moon bodies exactly as they were. Drawing inside
        /// the cap instead would re-roll every body in the sky each time the limit moved.
        ///
        /// It also fixes an outright bug — <c>Next(1, 0 + 1)</c> is an empty range, and an empty
        /// range returns its minimum, so a maximum of ZERO moons produced one moon.
        /// </summary>
        private static int MoonCountFor(Rng rng, SpaceBackgroundProfile p) =>
            Mathf.Clamp(1 + rng.Next(3), 0, Mathf.Max(0, p.maxMoonsPerBody));

        private static float SpinFor(Rng rng, SpaceBackgroundProfile p) =>
            6.2831853f / Mathf.Max(1f, Range(rng, p.spinPeriod.x, p.spinPeriod.y))
            * (rng.Next(2) == 0 ? 1f : -1f);

        private static float Range(Rng rng, float min, float max) =>
            min + (float)rng.NextDouble() * (max - min);

        // ── Packing ────────────────────────────────────────────────────────────────────────────

        private static void Pack(SkyData data)
        {
            for (int i = 0; i < SkyData.MaxPlanets; i++)
            {
                data.PlanetA[i] = Vector4.zero;
                data.PlanetE[i] = Vector4.zero;   // no orbit unless the archetype composed one
            }
            for (int i = 0; i < SkyData.MaxMoons; i++) data.MoonA[i] = new Vector4(-1f, 0f, 0f, 0f);

            data.PlanetCount = Mathf.Min(Bodies.Count, SkyData.MaxPlanets);
            for (int i = 0; i < data.PlanetCount; i++)
            {
                Body b = Bodies[i];
                data.PlanetA[i] = new Vector4(b.center.x, b.center.y, b.radius, b.parallax);
                data.PlanetB[i] = new Vector4(b.seed, b.spin, b.tilt, (float)b.kind);
                data.PlanetC[i] = new Vector4(b.surfacePPU, b.atmosphere, b.ringInner, b.ringOuter);
                data.PlanetD[i] = new Vector4(b.cloudAmount, b.cloudSpin, b.featureScale, b.depth);
                data.PlanetE[i] = new Vector4(b.orbitRadius, b.orbitPhase, b.orbitSpeed, b.orbitSquash);
                data.PlanetCol[i] = b.c1;
                data.PlanetCol2[i] = b.c2;
                data.PlanetCol3[i] = b.c3;
            }

            data.MoonCount = Mathf.Min(Moons.Count, SkyData.MaxMoons);
            for (int i = 0; i < data.MoonCount; i++)
            {
                Moon m = Moons[i];
                data.MoonA[i] = new Vector4(m.parent, m.orbitRadius, m.phase, m.orbitSpeed);
                data.MoonB[i] = new Vector4(m.radius, m.inclination, m.seed, m.surfacePPU);
                data.MoonCol[i] = m.color;
            }
        }
    }
}
