namespace BirichStudio.ProceduralSpace
{
    /// <summary>
    /// Sky <b>archetype</b> — the top-level composition decision. It determines which
    /// sub-generators run and how the screen is shared between them; every seed draws
    /// one archetype first.
    ///
    /// Why an archetype layer exists: if the planet / nebula / star generators ran freely,
    /// every sky would be "a bit of everything" and they would all look alike. The archetype
    /// decides <i>what dominates</i> — one sky is ruled by a giant planet, another by a gas
    /// cloud, another by bare void. Variety comes from this top decision, not from object count.
    /// </summary>
    public enum SkyArchetype
    {
        /// <summary>Bare void: dense stars, a hint of dust, a few distant small planets. Quiet and readable.</summary>
        DeepSpace = 0,

        /// <summary>Solar system: a distant sun plus several planets of different kinds, some with moons.</summary>
        SolarSystem = 1,

        /// <summary>Nebula field: a colored gas structure dominates, stars fade behind the gas, a single planet in silhouette.</summary>
        NebulaField = 2,

        /// <summary>Orbit: a SINGLE giant planet dominates from the edge (rings + moons), everything else stays tiny.</summary>
        PlanetOrbit = 3,

        /// <summary>Star cluster: very dense, hue-varied stars, almost no planets.</summary>
        StarCluster = 4,

        /// <summary>Galaxy vista: a tilted spiral galaxy disc rules the distance, foreground stays sparse.</summary>
        GalaxyVista = 5,

        /// <summary>Asteroid field: a rubble belt sweeps across the sky, planets stay small and distant.</summary>
        AsteroidField = 6,

        /// <summary>
        /// Orbital system: a star at the centre with its planets arranged on concentric orbits and
        /// actually revolving around it. Every other archetype scatters bodies that then sit still;
        /// this one is the only sky that shows a system as a system — the arrangement is legible,
        /// and the slow procession around the star is what makes it read as one.
        /// </summary>
        OrbitalSystem = 7,
    }

    /// <summary>
    /// Planet kind. The shader's <c>PlanetSurface</c> branches read this value — each kind has
    /// its own surface generator, its own color logic and its own 3D cue.
    ///
    /// One rule shared by all: the surface must contain a <b>longitude-dependent</b> feature.
    /// A sphere carrying only a latitude pattern (bands) is symmetric around its spin axis and
    /// looks STATIC no matter how fast it rotates.
    /// </summary>
    public enum PlanetKind
    {
        /// <summary>Rocky: crater fields, dark maria patches, sharp airless terminator.</summary>
        Rocky = 0,

        /// <summary>Gas giant: latitude bands + turbulence + a longitude-locked storm spot, thick atmosphere.</summary>
        GasGiant = 1,

        /// <summary>Ice: high albedo, crack network, wide polar caps, thin bright halo.</summary>
        Ice = 2,

        /// <summary>Lava: dark crust + self-lit cracks (ignores the terminator, glows on the night side too).</summary>
        Lava = 3,

        /// <summary>Terran: continents + ocean + polar cap + a cloud layer rotating at its OWN speed.</summary>
        Terran = 4,

        /// <summary>Star: self-lit, no terminator, granulation + corona.</summary>
        Star = 5,

        /// <summary>Desert: sheared dune banding, dark canyon lines, bright ergs, faint frosted poles.</summary>
        Desert = 6,

        /// <summary>Toxic: one unbroken, heavily swirled acid cloud deck — Venus with the saturation up.</summary>
        Toxic = 7,
    }

    /// <summary>Character of the nebula texture — different structures out of the same noise.</summary>
    public enum NebulaStyle
    {
        /// <summary>Barely there — the sensed dust of deep space.</summary>
        Wispy = 0,

        /// <summary>Clumpy, voluminous cloud masses.</summary>
        Billow = 1,

        /// <summary>Thin, long filaments (ridged noise) — supernova-remnant feel.</summary>
        Filament = 2,
    }
}
