using UnityEngine;

namespace BirichStudio.ProceduralSpace
{
    /// <summary>
    /// A reusable style preset for <see cref="SpaceBackground"/>: everything that defines HOW a
    /// sky is generated (palettes, archetype weights, scale classes, parallax band, pixel-art
    /// settings) lives here. WHICH sky comes out is decided by the seed on the component.
    ///
    /// One profile can drive any number of backgrounds; duplicating a profile and tweaking a few
    /// fields is the intended way to build custom looks.
    /// </summary>
    [CreateAssetMenu(fileName = "SpaceBackgroundProfile",
                     menuName = "Birich Studio/Procedural Space/Background Profile")]
    public class SpaceBackgroundProfile : ScriptableObject
    {
#if UNITY_EDITOR
        /// <summary>
        /// Push edits straight to the sky.
        ///
        /// A profile is an asset, and Unity only calls OnValidate on the object that changed — so
        /// nothing told the backgrounds using it to recompose, and every adjustment sat invisible
        /// until the component happened to be touched. For a tool whose entire purpose is tuning
        /// a look, a settings panel that does not show its own effect is barely a panel at all.
        /// </summary>
        private void OnValidate()
        {
            // Values that switch a feature OFF instead of turning it down are the worst kind of
            // setting, because the control that says the feature is on is still sitting right
            // there. A reference height of 0 disabled camera fitting under a ticked "Fit To
            // Camera" box, and a spacing of 0 removed the separation test entirely and stacked
            // every body on one spot. Neither reads as "I set this too low"; both read as "this
            // tool is broken". Below 1 still allows deliberate overlap, which is a real look.
            referenceViewHeight = Mathf.Max(1f, referenceViewHeight);
            separationFactor = Mathf.Max(0.5f, separationFactor);
            spread = new Vector2(Mathf.Max(1f, spread.x), Mathf.Max(1f, spread.y));

            // A block is a whole number of screen pixels, so zero is not a small block — it is a
            // division by zero. Mathf.Clamp also clamps DOWN to max, so a zero in either component
            // is enough: the surface PPU comes out infinite, every planet surface in the sky
            // renders as NaN, and nothing is logged to say why. The second Max also keeps the
            // range the right way round, since a reversed one clamps every value to its top.
            surfaceBlockRange = new Vector2Int(
                Mathf.Max(1, surfaceBlockRange.x),
                Mathf.Max(Mathf.Max(1, surfaceBlockRange.x), surfaceBlockRange.y));

            for (int i = SpaceBackground.Live.Count - 1; i >= 0; i--)
            {
                SpaceBackground bg = SpaceBackground.Live[i];
                if (bg == null) { SpaceBackground.Live.RemoveAt(i); continue; }
                if (bg.Profile == this) bg.Rebuild();
            }
        }
#endif

        [Tooltip("What this preset is for, in a sentence. Ten assets named like paint colours " +
                 "tell a buyer nothing about which one suits their game; the description is how " +
                 "a preset library becomes browsable instead of guessable.")]
        [TextArea(2, 4)] public string description = "";

        [Header("Mode")]
        [Tooltip("On: pixel-art rendering — all layers snap to a pixel grid, brightness is " +
                 "posterized and banding is broken with ordered dithering. Off: smooth rendering " +
                 "with anti-aliased edges and continuous gradients.")]
        // Off by default. Smooth is what "space background" means to someone who has not yet
        // decided anything, and it is the mode the shipped prefab and the create-menu use — a
        // profile made from scratch should land in the same place they do, or the package looks
        // like it has two personalities depending on how you entered it. Pixel art is the
        // deliberate choice on top, not the starting point.
        public bool pixelArt = false;

        [Tooltip("On: super-casual cartoon shading. Planets use a few flat light bands and a " +
                 "simple rim, stars are chunky sparkles instead of photographic points, and " +
                 "surface detail is drawn in big shapes.")]
        public bool cartoon = false;

        [Tooltip("Pixel grid density in pixels per world unit (pixel-art mode). Match your " +
                 "project's PPU for authentic results.")]
        public float pixelsPerUnit = 12f;

        [Header("Archetypes")]
        [Tooltip("Draw weights per archetype — in enum order: DeepSpace, SolarSystem, NebulaField, " +
                 "PlanetOrbit, StarCluster, GalaxyVista, AsteroidField, OrbitalSystem. Raise a " +
                 "value to see that sky more often; set it to 0 to disable the archetype " +
                 "entirely. An archetype past the end of this array is never drawn.")]
        public float[] archetypeWeights = { 1.2f, 1.0f, 0.9f, 1.0f, 0.7f, 0.8f, 0.8f, 0.8f };

        [Tooltip("Scales how many bodies each archetype places. The archetype still decides the " +
                 "SHAPE of the sky — one giant, a scattered handful, an empty void — this only " +
                 "changes how crowded that shape is.\n\n" +
                 "A hero body is never scaled away, including at 0: an archetype built around one " +
                 "giant is DEFINED by having it, and a crowding control has no business " +
                 "overruling the shape of the sky.")]
        [Range(0f, 3f)] public float bodyCountScale = 1f;

        [Tooltip("Relative chance of each planet kind, in enum order: Rocky, GasGiant, Ice, Lava, " +
                 "Terran, (Star — placed by archetypes, not drawn here), Desert, Toxic. Set one " +
                 "to 0 to keep that kind out of your game entirely.\n\n" +
                 "Raising one kind only ever converts bodies TO that kind; the worlds it does not " +
                 "win stay exactly as they were, so you can tune one entry at a time.\n\n" +
                 "Size still has the last word: gas giants and toxic worlds are never drawn " +
                 "small, so raising them changes nothing in a sky made of distant specks. Band " +
                 "structure and swirling cloud decks cannot read across a handful of pixels, and " +
                 "a kind that cannot be recognised is not that kind — it is mush.")]
        public float[] planetKindWeights = { 1f, 1f, 1f, 1f, 1f, 0f, 1f, 1f };

        [Tooltip("Scales every body's radius. Use it to fit the sky to your camera rather than " +
                 "editing the three scale classes below — it keeps the relation between hero, " +
                 "mid and small bodies intact, which is what stops a giant from turning into " +
                 "just another disc.")]
        [Range(0.2f, 3f)] public float planetScale = 1f;

        [Tooltip("Always place one large star in the near sky, in front of the smaller bodies.")]
        public bool featuredCloseStar = false;

        [Tooltip("Always place a sun. If this sky type already has one, that sun is kept and " +
                 "enlarged when it would otherwise read as a distant point.")]
        public bool featuredSun = false;

        [Header("Scale classes (radius, world units)")]
        [Tooltip("HERO: covers a significant part of the screen. At most ONE per sky.")]
        public Vector2 heroRadius = new Vector2(9f, 15f);

        [Tooltip("MID: a prominent body that does not dominate.")]
        public Vector2 midRadius = new Vector2(2.2f, 4f);

        [Tooltip("SMALL: a distant planet reading as little more than a dot.")]
        public Vector2 smallRadius = new Vector2(0.7f, 1.8f);

        [Tooltip("Frame the sky to the camera instead of to fixed world units.\n\n" +
                 "Everything here is calibrated for a view about 17 units tall. A project whose " +
                 "camera sees less than that gets planets too large to fit and bodies falling " +
                 "outside the frame — not because anything is wrong, but because the sky is " +
                 "measured in world units and your camera is not. With this on, body sizes and " +
                 "placement follow the view, so the composition frames the same way at any " +
                 "camera size.")]
        public bool autoFitToCamera = true;

        [Tooltip("View height, in world units, the settings below are written for. Only used " +
                 "when Fit To Camera is on: the sky scales by your camera's height divided by " +
                 "this. Leave it at 17 unless you deliberately re-tuned the sizes below for a " +
                 "different frame.")]
        [Min(1f)] public float referenceViewHeight = 17f;

        [Tooltip("Shifts the whole composition in world units. Use it to keep a corner of the " +
                 "frame clear — for a HUD, or for wherever the game actually happens.")]
        public Vector2 skyOffset = Vector2.zero;

        [Header("Placement")]
        [Tooltip("HALF-extent of the area body centers are scattered over, in world units — the " +
                 "area is twice this in each direction. It also sets how far apart a body's " +
                 "repeats are placed, so a small value brings the repeat into the same frame.")]
        public Vector2 spread = new Vector2(18f, 10f);

        [Tooltip("Two body centers must be at least this multiple of their summed radii apart. " +
                 "Raise for an airier sky; side-by-side clumping becomes impossible.\n\n" +
                 "1 puts two bodies exactly edge to edge. Below that they overlap on purpose, " +
                 "which is a legitimate look — a moon crossing its planet — but the sky gets " +
                 "harder to read the further you go.")]
        [Min(0.5f)] public float separationFactor = 2.2f;

        [Tooltip("Minimum distance the hero body is pushed away from the center — a giant in the " +
                 "middle swallows the play area and kills readability.")]
        public float heroEdgePush = 11f;

        [Header("Parallax")]
        [Tooltip("Scales the whole parallax ladder at once — every layer and both ends of the " +
                 "body band. Raise it for a sky that reacts strongly to camera motion, lower it " +
                 "for a distant backdrop that barely moves. The RATIOS between layers are what " +
                 "produce depth, and this leaves them untouched.")]
        [Range(0f, 3f)] public float parallaxStrength = 1f;

        [Tooltip("Parallax of the NEAREST body (fastest drift).\n\n" +
                 "On-screen shift = camera displacement × parallax. Keep the band narrow and low: " +
                 "high values make the sky read as a near layer sliding along with the player " +
                 "instead of a distant backdrop.")]
        [Range(0f, 1f)] public float parallaxSmall = 0.1f;

        [Tooltip("Parallax of the FARTHEST body (slowest drift → reads huge and remote).\n\n" +
                 "Keep this below the near value. Bodies are painted in parallax order, so " +
                 "inverting the band would also invert which body covers which.")]
        [Range(0f, 1f)] public float parallaxLarge = 0.025f;

        [Tooltip("Parallax of the nebula / gas layer. Keep near zero — it is the deepest thing " +
                 "in the sky.")]
        [Range(0f, 1f)] public float nebulaParallax = 0.03f;

        [Tooltip("Parallax of the far star layer (dense, dim, behind the planets).")]
        [Range(0f, 1f)] public float farStarParallax = 0.1f;

        [Tooltip("Parallax of the mid star layer — the middle rung of the depth ladder, still " +
                 "behind the planets.")]
        [Range(0f, 1f)] public float midStarParallax = 0.2f;

        [Tooltip("Parallax of the near star layer (sparse, bright, twinkling, in front of the " +
                 "planets). The gap between the layers is what makes camera motion read as depth.")]
        [Range(0f, 1f)] public float nearStarParallax = 0.35f;

        [Tooltip("Constant sky drift in world units per second — a virtual camera glide. Every " +
                 "layer moves at its own parallax rate (near stars fastest, giants slowest), so " +
                 "the sky reads as slow travel through space even when the camera stands still. " +
                 "Zero disables it.")]
        public Vector2 driftVelocity = new Vector2(0.12f, 0.015f);

        [Tooltip("Repeat the planets on a tile so sustained motion never empties the sky.\n\n" +
                 "The star fields, gas and dust are computed from position and so are endless, " +
                 "but there are only a handful of bodies at fixed places: with drift on — or in " +
                 "a game whose camera keeps travelling — they all eventually leave the frame and " +
                 "nothing replaces them. Wrapping makes each body repeat far enough apart that " +
                 "at most one copy is ever in view.\n\n" +
                 "Turn it off only for a camera that stays put, where the repeat would be the " +
                 "only thing it could ever show you.")]
        public bool wrapBodies = true;

        [Tooltip("Extra spacing between a body and its repeats, as a multiple of the placement " +
                 "area. Larger means the sky takes longer to come around, at the cost of gaps " +
                 "with nothing in them.")]
        [Range(1.2f, 4f)] public float wrapSpacing = 2.6f;

        [Header("Rotation")]
        [Tooltip("Seconds for a full revolution. A background is looked at for a long time and " +
                 "only glanced at directly, so rotation should be something you notice having " +
                 "happened rather than something you can watch happening — a planet visibly " +
                 "turning pulls the eye off whatever the scene is actually about. Too slow is " +
                 "the safer error: below roughly one revolution per five minutes it simply " +
                 "reads as static.")]
        public Vector2 spinPeriod = new Vector2(85f, 210f);

        [Tooltip("Axis tilt range in degrees, so not everything spins around the same axis.")]
        public Vector2 tiltRange = new Vector2(-38f, 38f);

        [Header("Surface (pixel-art mode)")]
        [Tooltip("Target number of texture pixels across a body's DIAMETER.\n\n" +
                 "Surface chunkiness cannot come from one fixed PPU: a value that looks right on " +
                 "a giant collapses a small body to a handful of pixels and the pattern is lost. " +
                 "Deriving PPU from the radius keeps detail density roughly constant across sizes.")]
        public float targetSurfaceTexels = 34f;

        [Tooltip("Bounds of one surface block in SCREEN PIXELS (x = finest, y = coarsest).\n\n" +
                 "Surface PPU cannot be a free number: unless it divides the screen grid exactly, " +
                 "block edges land fractionally on screen pixels and single-pixel lines crawl " +
                 "along them as the camera moves. Block size is therefore rounded to WHOLE screen " +
                 "pixels first and the PPU derived from that.")]
        public Vector2Int surfaceBlockRange = new Vector2Int(2, 4);

        [Header("Star field")]
        [Tooltip("Global multiplier applied to the star densities chosen by the archetype (all " +
                 "three layers together). Thins or thickens the sky without breaking the ratio " +
                 "between archetypes: a star cluster still reads denser than deep space.")]
        [Range(0.1f, 2f)] public float starDensityScale = 0.42f;

        [Tooltip("Multiplier on how strongly star hues vary between warm and cool. The archetype " +
                 "sets the character — a cluster is more colourful than empty void — and this " +
                 "scales that family. 1 leaves the archetype's own choice alone; 0 gives a white " +
                 "star field.")]
        [Range(0f, 2f)] public float starHueVariety = 1f;

        [Tooltip("Brightness pulsing of near stars (they never fully disappear).")]
        [Range(0f, 1f)] public float twinkleAmount = 0.5f;

        [Tooltip("Fraction of stars that fully blink out and reappear. Keep small — scarcity is " +
                 "what makes the motion precious.")]
        [Range(0f, 1f)] public float blinkFraction = 0.1f;

        [Tooltip("Multiplier on the nebula density each archetype composes. The archetype still " +
                 "decides the character (a nebula sky stays denser than deep space); this scales " +
                 "the whole family up or down.")]
        [Range(0f, 2f)] public float nebulaIntensity = 1f;

        [Header("Rings & moons")]
        [Tooltip("Master chance of a ring. Each kind carries its own odds on top: gas giants " +
                 "always qualify, ice and rock rarely, terran worlds and stars never — a ringed " +
                 "rocky world should stay an event rather than the norm. 0 removes every ring.")]
        [Range(0f, 1f)] public float ringChance = 0.5f;

        [Tooltip("Probability of moons on mid and hero bodies. Small bodies never get them: a " +
                 "moon of a distant speck lands on one pixel and cannot be told from noise.")]
        [Range(0f, 1f)] public float moonChance = 0.6f;

        [Tooltip("Maximum moons a single body can get. This is a cap, not a target — lowering it " +
                 "trims the bodies that wanted more and leaves the others untouched. 0 removes " +
                 "every moon.")]
        [Range(0, 3)] public int maxMoonsPerBody = 2;

        [Header("Base gradient")]
        [Tooltip("Deep end of the screen-locked background gradient.")]
        public Color baseColor = new Color(0.031f, 0.024f, 0.055f);

        [Tooltip("Far end of the background gradient.")]
        public Color baseColor2 = new Color(0.078f, 0.051f, 0.125f);

        [Tooltip("Direction of the background gradient in world space.")]
        public Vector2 gradientDirection = new Vector2(0.35f, 1f);

        [Tooltip("How fast the gradient sweeps across world space.")]
        public float gradientScale = 0.012f;

        [Header("Light")]
        [Tooltip("On: the sky lights itself. When the composed sky contains a star, light " +
                 "emanates FROM it — each body is lit from its own angle toward that star and " +
                 "dims with distance from it. With no star, a light angle is drawn from the " +
                 "seed. Off: the fixed direction below is used for every sky.")]
        public bool proceduralLight = true;

        [Tooltip("Direction of the sun lighting every body (xy in view space). Used when " +
                 "Procedural Light is off.")]
        public Vector2 lightDirection = new Vector2(-0.55f, 0.45f);

        [Tooltip("How far the sun sits toward the viewer (used when Procedural Light is off). " +
                 "Near 1 the light comes from behind the camera and discs read flat and evenly " +
                 "lit; low values swing the terminator across the visible face and bodies model " +
                 "themselves as spheres.")]
        [Range(0.05f, 1f)] public float lightElevation = 0.45f;

        [Tooltip("Range the sun's elevation is drawn from when Procedural Light is on. Widen it " +
                 "for skies that swing between fully lit worlds and dramatic crescents.")]
        public Vector2 lightElevationRange = new Vector2(0.14f, 0.45f);

        [Tooltip("How far the sky's star's output rises and falls over time. Real stars vary far " +
                 "less than this — the Sun by about a tenth of a percent across eleven years — " +
                 "so treat it as a stylistic amplitude on a real phenomenon, not a simulation.\n\n" +
                 "The rhythm is built from three periods that never line up, so the star never " +
                 "repeats the same shape and reads as alive rather than as a blinking light. " +
                 "The whole sky breathes with it: planets, moons, rings and the belt are lit by " +
                 "this star, and the change reaches distant bodies later than near ones.\n\n" +
                 "0 = a perfectly steady sun.")]
        [Range(0f, 1f)] public float starPulse = 0.45f;

        // The headers below say which RENDERING MODE each control reaches. A setting that silently
        // does nothing in the mode you are working in is worse than a missing setting: you turn
        // the knob, see no change, and conclude the feature is broken.
        [Header("Deep sky (both modes)")]
        [Tooltip("Density of genuinely bright stars — the few that read as points of light with " +
                 "a halo instead of specks. Scarcity is the point: they give the field its scale. " +
                 "Both modes draw them, each in its own language.")]
        [Range(0f, 0.3f)] public float brightStarDensity = 0.05f;

        [Tooltip("Interstellar dust: cold clouds that emit nothing and absorb the galaxy and " +
                 "star layers behind them. Without absorption a sky can only get brighter and " +
                 "flattens into an even wash, so this is what carves structure into the void.")]
        [Range(0f, 1f)] public float dustAmount = 0.55f;

        [Header("Deep sky (smooth mode only)")]
        [Tooltip("Diffraction spikes on bright stars. Subtle values read as light overwhelming " +
                 "the eye; high values read as drawn star shapes. Pixel mode draws its bright " +
                 "stars as a plus on the pixel grid instead, and ignores this.")]
        [Range(0f, 1f)] public float starSpikes = 0.45f;

        [Tooltip("Number of diffraction rays on bright stars. Six is the JWST signature (three " +
                 "mirror supports), four the classic Hubble cross. Every star shares the same " +
                 "pattern and orientation because it comes from the instrument, not the star.")]
        [Range(2, 8)] public int starSpikeCount = 6;

        [Tooltip("Density of faint elliptical smudges — other galaxies, too far to resolve. " +
                 "They say the void keeps going past the stars in it. Keep them rare and dim: " +
                 "once they read as objects rather than a suspicion of light, they look painted.")]
        [Range(0f, 0.2f)] public float distantGalaxyDensity = 0.035f;

        [Header("Body shading (both modes)")]
        [Tooltip("Darkness of the shadowed side. 1 = fully black night side. Space is not a " +
                 "room: there is no floor or wall bouncing light back, so an unlit face is lit " +
                 "only by starlight and by whatever the body itself is near. Values below ~0.9 " +
                 "read as a studio fill light and quietly flatten every body in the sky.")]
        [Range(0f, 1f)] public float shadowDarkness = 0.96f;

        [Tooltip("Ambient fill on the shadowed side.")]
        [Range(0f, 1f)] public float planetAmbient = 0.035f;

        [Tooltip("Master scale on the atmospheric glow every body wears at its limb. Keep it low: " +
                 "wide halos make planets look like light sources and fill the void between them, " +
                 "and it is the darkness between bodies that carries the sense of depth. " +
                 "0 = airless everywhere.\n\n" +
                 "Both modes shade the limb with it. Past the disc, smooth mode extends a soft " +
                 "halo on every body; pixel mode extends only a star's corona, posterized into " +
                 "rings — a soft halo outside a hard pixel edge on a mere planet would just look " +
                 "like a mistake.")]
        [Range(0f, 2f)] public float atmosphereGlow = 0.6f;

        [Header("Body shading (pixel-art mode only)")]
        [Tooltip("Number of brightness steps across the terminator. Smooth mode uses a continuous " +
                 "twilight gradient and ignores this.")]
        [Range(1, 16)] public int terminatorSteps = 5;

        [Tooltip("Surface brightness quantization steps. Fewer steps = bolder posterized look.")]
        [Range(2, 32)] public int surfaceColorSteps = 8;

        [Tooltip("How far shading shifts hue as well as value — shadows toward " +
                 "cool violet, lit faces toward warm. Darkening a colour alone only walks it " +
                 "toward grey; shifting hue with value is what gives a limited palette its life. " +
                 "0 = pure value shading.")]
        [Range(0f, 1f)] public float pixelHueShift = 0.6f;

        [Tooltip("0 = hard banded steps, 1 = fully dithered transitions.")]
        [Range(0f, 1f)] public float ditherAmount = 1f;

        [Header("Shooting stars")]
        [Tooltip("Occasional streaks crossing the sky. They spawn around the visible area and " +
                 "work with every archetype.")]
        public bool shootingStars = true;

        [Tooltip("Average seconds between streaks (per track). Each streak jitters its own " +
                 "timing so the rhythm never turns metronomic.")]
        public float shootingStarPeriod = 9f;

        [Tooltip("Streak speed in world units per second.")]
        public float shootingStarSpeed = 30f;

        [Tooltip("Tail length in world units.")]
        public float shootingStarTail = 4f;

        [Tooltip("Streak brightness. Values above 1 glow nicely with bloom.")]
        [Range(0f, 3f)] public float shootingStarBrightness = 1.3f;

        [Tooltip("How many streaks can be airborne at once (independent tracks).")]
        [Range(1, 2)] public int shootingStarTracks = 1;

        [Tooltip("Streak color. Slightly blue-white reads as the classic meteor.")]
        public Color shootingStarColor = new Color(0.9f, 0.95f, 1f);

        [Header("Nebula palette")]
        [Tooltip("Groups of three: (gas A, gas B, hot core). Every sky draws one group — with a " +
                 "single palette all nebulae would share one color and the archetype difference " +
                 "would visually disappear.")]
        public Color[] nebulaPalette =
        {
            new Color(0.42f, 0.16f, 0.58f), new Color(0.16f, 0.13f, 0.45f), new Color(0.72f, 0.34f, 0.42f), // violet
            new Color(0.16f, 0.42f, 0.52f), new Color(0.10f, 0.18f, 0.42f), new Color(0.45f, 0.70f, 0.70f), // teal
            new Color(0.55f, 0.20f, 0.22f), new Color(0.30f, 0.12f, 0.30f), new Color(0.85f, 0.48f, 0.28f), // crimson
            new Color(0.22f, 0.42f, 0.34f), new Color(0.12f, 0.20f, 0.38f), new Color(0.62f, 0.76f, 0.48f), // emerald
        };
    }
}
