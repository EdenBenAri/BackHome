Shader "Birich Studio/ProceduralSpace"
{
    // Fully procedural space background. One quad, one draw call, zero texture assets.
    //
    // ── LAYERS (back to front) with typical parallax factors ───────────────────────────────────
    //   0.00  base gradient          screen-locked (infinitely far)
    //   0.03  nebula / gas           fbm; clumpy or filamented depending on style
    //   0.10  far stars              dense, tiny, dim
    //   ~     planets + moons        each with its own parallax; C# sorts far-to-near
    //   0.35  near stars             sparse, bright, twinkling
    //
    // PARALLAX: sample position = worldPos − camPos × (1 − p). p=0 → locked to the screen
    // (infinitely far), p=1 → locked to the world. Free depth whenever the camera moves.
    //
    // ── WHY ROTATION READS ─────────────────────────────────────────────────────────────────────
    // For every pixel inside a disc a fake sphere normal is derived (z = √(1−r²)); the surface is
    // sampled in longitude/latitude space and longitude shifts over time. Critical rule: every
    // kind's surface must carry a LONGITUDE-DEPENDENT, high-contrast, local feature (storm spot,
    // continent, crater, lava crack). A sphere with only latitude bands is symmetric around its
    // spin axis → it looks static no matter how fast it spins. The terminator stays FIXED in view
    // space; features being born at the lit rim and drowning into shadow is what sells rotation.
    //
    // ── PIXEL ART MODE (_PixelArt) ─────────────────────────────────────────────────────────────
    // 1 → all sampling positions snap to a _PPU screen grid, brightness is posterized and banding
    //     is broken with ordered (Bayer) dithering. Retro, low-res friendly.
    // 0 → continuous sampling, no posterize/dither, anti-aliased edges, stars become soft round
    //     points. A clean, smooth look for non-pixel-art projects.
    // The switch is a uniform float branch, not a keyword: both paths are uniform across the draw
    // (no divergence cost) and it keeps the material count at one.
    //
    // ── SORTING ────────────────────────────────────────────────────────────────────────────────
    // With URP's 2D Renderer, ordering follows sorting order, not render queue — the component
    // forces a strongly negative sorting order. In a 3D/Forward setup the transparent queue and
    // quad placement behind the scene do the job.
    Properties
    {
        [Header(Mode)]
        _PixelArt ("Pixel Art (1 on, 0 off)", Float) = 1
        _PPU ("Pixels Per Unit (pixel grid)", Float) = 12
        _StarGridScale ("Star grid scale", Float) = 1
        _TimeOffset ("Time Offset", Float) = 0

        [Header(Base)]
        _BaseColor   ("Base (deep)",         Color) = (0.031, 0.024, 0.055, 1)
        _BaseColor2  ("Base (gradient end)", Color) = (0.078, 0.051, 0.125, 1)
        _GradientDir ("Gradient direction (xy)", Vector) = (0.35, 1, 0, 0)
        _GradientScale ("Gradient scale", Float) = 0.012

        [Header(Nebula)]
        _NebColA   ("Nebula A", Color) = (0.42, 0.16, 0.58, 1)
        _NebColB   ("Nebula B", Color) = (0.16, 0.13, 0.45, 1)
        _NebColC   ("Nebula C (hot core)", Color) = (0.72, 0.34, 0.42, 1)
        _NebAmount   ("Nebula amount", Range(0, 1)) = 0.18
        _NebScale    ("Nebula scale",  Float) = 0.035
        _NebContrast ("Nebula contrast", Range(0.5, 6)) = 2.2
        _NebStyle    ("Nebula style (0 wispy 1 billow 2 filament)", Float) = 0
        _NebParallax ("Nebula parallax", Range(0, 1)) = 0.03

        [Header(Far stars)]
        _FarStarColor   ("Far star color", Color) = (0.78, 0.74, 0.92, 1)
        _FarStarDensity ("Far density", Range(0, 0.03)) = 0.005
        _FarStarBright  ("Far brightness", Range(0, 2)) = 0.55
        _FarStarParallax("Far parallax",  Range(0, 1)) = 0.10

        [Header(Mid stars)]
        _MidStarColor   ("Mid star color", Color) = (0.9, 0.88, 0.98, 1)
        _MidStarDensity ("Mid density", Range(0, 0.02)) = 0.0025
        _MidStarBright  ("Mid brightness", Range(0, 3)) = 0.85
        _MidStarParallax("Mid parallax",  Range(0, 1)) = 0.2

        [Header(Near stars)]
        _NearStarColor   ("Near star color", Color) = (1, 0.97, 1, 1)
        _NearStarDensity ("Near density", Range(0, 0.02)) = 0.0014
        _NearStarBright  ("Near brightness", Range(0, 3)) = 1.3
        _NearStarParallax("Near parallax",  Range(0, 1)) = 0.35
        _StarHueVariety  ("Star hue variety", Range(0, 1)) = 0.35
        _TwinkleSpeed    ("Twinkle speed",   Float) = 1.6
        _TwinkleAmount   ("Twinkle amount", Range(0, 1)) = 0.5

        [Header(Blinking  (separate from twinkle))]
        _BlinkFraction ("Blinking star fraction", Range(0, 1)) = 0.1
        _BlinkRateMin  ("Blink rate min (rad/s)", Float) = 0.9
        _BlinkRateMax  ("Blink rate max (rad/s)", Float) = 3.2
        _BlinkSharpness("Sharpness (1 pulse, 8 flick)", Range(1, 8)) = 3.5

        [Header(Bright stars and dust  (smooth mode))]
        _BrightStarAmount("Bright star density", Range(0, 0.3)) = 0.05
        _StarSpikes      ("Diffraction spikes", Range(0, 1)) = 0.45
        _StarSpikeCount  ("Spike count (4 Hubble, 6 JWST)", Range(2, 8)) = 6
        _DustAmount      ("Dust lane absorption", Range(0, 1)) = 0.55
        _FarGalaxyAmount ("Distant galaxy density", Range(0, 0.2)) = 0.035

        [Header(Light  (all bodies share one sun))]
        _PlanetLightDir  ("Light direction (xy), elevation (z), pulse phase (w)", Vector) = (-0.55, 0.45, 0.55, 0)
        _PlanetLightSteps("Terminator steps", Float) = 5
        _PlanetShadow    ("Shadow darkness", Range(0, 1)) = 0.9
        _PlanetAmbient   ("Planet ambient", Range(0, 1)) = 0.1
        _PlanetColorSteps("Surface brightness steps", Range(2, 32)) = 8
        _AtmoIntensity   ("Atmosphere glow", Range(0, 2)) = 0.6
        _StarPulse       ("Star pulse (output variation)", Range(0, 1)) = 0.45
        _PixelHueShift   ("Pixel hue shift (cool shadows, warm lights)", Range(0, 1)) = 0.6
        _DitherAmount("Dither (0 hard bands, 1 full dot blend)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        // No blending: frag always returns alpha 1, so SrcAlpha/OneMinusSrcAlpha was the
        // identity — paid for with a full-screen framebuffer read every frame, and a tile read
        // per pixel on tiled mobile GPUs.
        Blend Off
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5   // integer ops for the lattice hash
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_PLANETS 6
            #define MAX_MOONS   6


            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 worldPos : TEXCOORD0; };

            float  _PixelArt, _Cartoon, _PPU, _TimeOffset, _PhoneQuality;
            float  _DebugNoise;   // dev-only: >0 renders raw noise layers for driver triage
            float4 _DriftVec;   // xy: sky drift in units/s — a virtual camera glide through space
            // xy: camera travel in the view plane, in the same units as the quad.
            // Fed every frame by SpaceCameraFit. World XY is not used: it collapses
            // when the camera looks along X and stretches the sky into streaks.
            float4 _SkyTravel;

            float4 _BaseColor, _BaseColor2, _GradientDir;
            float  _GradientScale;

            float4 _NebColA, _NebColB, _NebColC;
            float  _NebAmount, _NebScale, _NebContrast, _NebStyle, _NebParallax;
            // xy: per-sky offset into the gas noise field, z: per-sky cloud-scale multiplier.
            float4 _NebSeed;

            float4 _FarStarColor, _MidStarColor, _NearStarColor;
            float  _FarStarDensity, _FarStarBright, _FarStarParallax;
            float  _MidStarDensity, _MidStarBright, _MidStarParallax;
            float  _NearStarDensity, _NearStarBright, _NearStarParallax;
            float  _StarHueVariety, _TwinkleSpeed, _TwinkleAmount;
            float  _BlinkFraction, _BlinkRateMin, _BlinkRateMax, _BlinkSharpness;
            // Per-sky offset into the star lattice. Without it every sky hashes the same cells
            // and gets the same stars in the same places — see StarLayer.
            float4 _StarSeed;
            // Star lattice density relative to the profile's reference view. A camera that sees
            // twice as much world would otherwise show twice as many stars, each half the size.
            float  _StarGridScale;
            float  _BrightStarAmount, _StarSpikes, _DustAmount, _FarGalaxyAmount, _StarSpikeCount;

            float4 _PlanetLightDir;
            float  _PlanetLightSteps, _PlanetShadow, _PlanetAmbient, _PlanetColorSteps, _DitherAmount;

            // Positional sun: xy position in sky space, z 1 = light emanates FROM that point,
            // w reference distance for falloff. z = 0 → the whole sky uses _PlanetLightDir.
            float4 _SunPos;
            float  _AtmoIntensity;   // master scale on every body's scattering shell
            float  _StarPulse;       // how far the sky's star's output rises and falls
            float  _PixelHueShift;   // pixel mode: how far shadows cool and highlights warm

            // Fed by C# (SpaceBackground.cs). Not declared in Properties: it is a single quad,
            // so SRP Batcher compatibility would buy nothing here.
            //   _PlanetA: xy center, z radius, w parallax
            //   _PlanetB: x seed,   y spin speed, z axis tilt (rad), w kind (PlanetKind)
            //   _PlanetC: x surface PPU, y atmosphere, z ring inner radius, w ring outer radius (0 = none)
            //   _PlanetD: x cloud amount, y cloud spin, z feature scale, w unused
            float4 _PlanetA[MAX_PLANETS];
            float4 _PlanetB[MAX_PLANETS];
            float4 _PlanetC[MAX_PLANETS];
            float4 _PlanetD[MAX_PLANETS];
            //   _PlanetE: x orbit radius around the system's star (0 = fixed), y phase,
            //             z angular speed, w vertical foreshortening of the orbit
            float4 _PlanetE[MAX_PLANETS];
            // xy: tile the bodies repeat on, z: 1 = wrapping on. See the planet loop.
            float4 _BodyTile;
            float4 _PlanetCol[MAX_PLANETS];
            float4 _PlanetCol2[MAX_PLANETS];
            float4 _PlanetCol3[MAX_PLANETS];
            float  _PlanetCount;

            //   _MoonA: x owner planet index, y orbit radius, z phase, w orbit speed
            //   _MoonB: x radius, y orbit inclination (rad), z seed, w surface PPU
            float4 _MoonA[MAX_MOONS];
            float4 _MoonB[MAX_MOONS];
            float4 _MoonCol[MAX_MOONS];
            float  _MoonCount;

            //   _GalaxyA: xy center, z radius (0 = no galaxy), w parallax
            //   _GalaxyB: x screen-plane rotation (rad), y inclination (ellipse squash), z seed, w arm count
            //   _GalaxyC: x brightness, y spiral winding, z core size, w spin speed
            float4 _GalaxyA, _GalaxyB, _GalaxyC;
            float4 _GalaxyColA;   // core (warm)
            float4 _GalaxyColB;   // arms

            //   _BeltA: xy center, z band angle (rad), w half width (0 = no belt)
            //   _BeltB: x cell size (units per rock cell), y rock chance, z drift speed, w parallax
            //   _BeltC: x seed, y brightness, z min rock radius, w max rock radius (cell fractions)
            float4 _BeltA, _BeltB, _BeltC;
            float4 _BeltCol;

            //   _CometA: x average period (s, 0 = disabled), y speed (units/s), z tail length, w brightness
            //   _CometB: x parallax, y seed, z head size, w concurrent tracks (1-2)
            float4 _CometA, _CometB;
            float4 _CometCol;

            float Now() { return _Time.y + _TimeOffset; }

            // ── The sky's light source ─────────────────────────────────────────────────────────
            // When the composed sky contains a star, light is POSITIONAL: every body is lit from
            // its own angle toward that star, so a planet left of the sun is lit on its right and
            // one below it is lit from above. A single shared direction makes the star look
            // pasted on — bodies flanking it would be lit identically instead of turning to face
            // it, and the eye reads the whole system as flat. Without a star (deep space, nebula
            // fields) a distant off-screen sun is assumed and one direction is correct.
            /// How far the sun sits toward the viewer. This is the single strongest control over
            /// how a body reads: near 1 the light is behind the camera and the whole disc is lit
            /// flat (the "sticker" look); low values swing the terminator ACROSS the visible face
            /// and the sphere models itself in light and shadow — the crescent-lit planet of
            /// every real space photograph. Drawn per sky from the seed.
            float LightElevation() { return max(_PlanetLightDir.z, 0.08); }

            float2 LightDirFor(float2 center)
            {
                if (_SunPos.z < 0.5) return _PlanetLightDir.xy;
                float2 toSun = _SunPos.xy - center;
                float dist = length(toSun);
                return dist > 1e-4 ? toSun / dist : float2(1.0, 0.0);
            }

            /// Distance falloff from the positional sun: bodies out at the rim of the system sit
            /// in dimmer light than the ones hugging the star. Bounded (never below ~half) —
            /// this is a depth cue, not a physical inverse-square law, and a truly dark planet
            /// would just read as a hole in the composition.
            float SunAttenFor(float2 center)
            {
                if (_SunPos.z < 0.5) return 1.0;
                float dist = length(_SunPos.xy - center);
                // A wide range is what builds DEPTH: when every body sits at the same value the
                // image flattens into stickers on a backdrop, however good the surfaces are.
                // Bodies out at the rim drop to a third of the light the inner ones get.
                return lerp(1.0, 0.34, saturate(dist / max(_SunPos.w, 1e-3)));
            }

            // ── THE SUN IS NOT A CONSTANT ──────────────────────────────────────────────────────
            // A star's output rises and falls. The real numbers are tiny — the Sun's total
            // irradiance moves about a tenth of a percent across its eleven-year cycle, which no
            // eye could ever catch — so this is an honest phenomenon drawn at a dishonest
            // amplitude, the same bargain every other contrast decision in this file makes.
            //
            // THREE periods with no common multiple, never one sine. A single sine is a blinking
            // bulb: the eye locks onto the period inside two cycles and the star stops being a
            // star and becomes a UI element pulsing. Periods that never line up produce a curve
            // that never repeats its own shape, and that is the whole difference between reading
            // as ALIVE and reading as ANIMATED. The rates are deliberately slow and unequal —
            // roughly 15 s, 40 s and 86 s — so the sky breathes rather than throbs.
            //
            // The phase comes from _PlanetLightDir.w (the star's own seed, packed there by
            // ComposeLight) so two skies on screen at once never breathe in step.
            /// <paramref name="center">the lit body's position. Light takes time to cross the
            /// system, so a world out at the rim sees the brightening after the star does: the
            /// pulse travels outward as a slow wave. Without the delay every body in frame
            /// brightens on the same frame and the image reads as the whole PICTURE flickering
            /// rather than as one object in it varying.</paramref>
            float StarLightPulse(float2 center)
            {
                if (_StarPulse < 0.001) return 1.0;
                // No positional sun: the source is off screen and effectively infinitely far, so
                // there is no distance to delay by and the whole sky breathes together.
                float dist = _SunPos.z < 0.5 ? 0.0 : length(_SunPos.xy - center);
                float t = Now() - dist * 0.30;
                float ph = _PlanetLightDir.w;
                float v = 0.55 * sin(t * 0.410 + ph)
                        + 0.30 * sin(t * 0.157 + ph * 2.7)
                        + 0.15 * sin(t * 0.073 + ph * 5.1);

                // 0.25 at the top of the slider: about a quarter of a stop either way. Past that
                // the star stops looking like a star and starts looking like a fault in the
                // renderer.
                return 1.0 + v * _StarPulse * 0.25;
            }

            // Shared procedural primitives (hashes, fbm/ridged, Worley, crater fields) and the
            // per-kind surface generators live in Include/. They come AFTER the uniform block
            // above because several of them read _PixelArt, _PPU and _DitherAmount directly.
            #include "Include/SpaceNoise.hlsl"
            #include "Include/SurfaceContext.hlsl"

            // Pixel mode: the parallax shift is quantized to the SCREEN grid. Even with a
            // pixel-perfect camera, cam×(1−p) comes out fractional; the layer then drifts
            // sub-pixel and texture blocks land fractionally on screen pixels → single-pixel
            // crawling lines at block edges. Quantizing the shift scrolls the background pixel by
            // pixel, which is the correct behavior for pixel art.
            // Smooth mode: the raw shift is used — continuous scrolling.
            float2 LayerPos(float2 world, float2 cam, float p)
            {
                float2 off = cam * (1.0 - p);
                if (_PixelArt > 0.5) off = floor(off * _PPU + 0.5) / _PPU;
                return world - off;
            }

            /// Pixel mode: snap to the given grid. Smooth mode: identity.
            float2 Snap(float2 p, float ppu)
            {
                if (_PixelArt < 0.5) return p;
                return floor(p * ppu) / max(ppu, 0.0001);
            }

            /// ── NOTHING MOVES BY LESS THAN ONE TEXEL ──────────────────────────────────────────
            /// Pixel mode is not a continuous image with a pixelate filter over it, and rotation
            /// is where the difference stops being philosophy. A sub-texel step cannot MOVE a
            /// feature: there is nowhere for it to move to. All it can do is re-decide, per texel
            /// and independently of its neighbours, which side of a quantization threshold that
            /// texel now falls on. Half of them flip and half do not, in no coherent direction,
            /// and the surface comes out re-pixelated from scratch rather than turned — which is
            /// exactly what a rotating planet looked like before this existed.
            ///
            /// Hand-drawn pixel animation has the opposite property, and not by accident: it
            /// HOLDS a frame, then steps, and every step is at least one whole pixel. So every
            /// animated quantity a body owns is quantized to the coarsest step that can still
            /// show a difference. The image is then stable between steps and genuinely different
            /// across them, which is the entire read of "animated" rather than "noisy".
            ///
            /// Smooth mode has no grid and passes straight through.
            ///
            /// <paramref name="texelRadius"/>: the body's radius measured in its own texels
            /// (radius x ppu). One step then turns the surface by exactly one texel at the centre
            /// of the disc — finer is sub-texel by definition, coarser throws away motion the
            /// grid could have shown.
            float SnapSpin(float angle, float texelRadius)
            {
                if (_PixelArt < 0.5) return angle;
                float step = 1.0 / max(texelRadius, 1.0);
                return floor(angle / step) * step;
            }

            // ── DITHER ─────────────────────────────────────────────────────────────────────────
            // The price of quantization is banding: brightness varies smoothly across a sphere,
            // so floor()-stepping turns iso-brightness curves into visible contour lines. Ordered
            // dithering answers this in pixel-art language: the threshold gets a per-cell offset
            // and a band edge becomes a dotted blend of two steps. Deliberate and retro — a hard
            // band reads as an error.
            //
            // The Bayer matrix is produced arithmetically, without array indexing (dynamic array
            // indexing is trouble on old shader targets).
            // The 4×4 Bayer threshold, built from the low two bits of the cell coordinate by
            // bit reversal — the matrix's actual definition.
            //
            // The previous version derived it arithmetically, with a `y * y` term inside a frac().
            // That works near the origin and quietly dies away from it: at a cell coordinate of a
            // few thousand, y² is in the millions, where fp32 no longer resolves a fraction at
            // all. frac() then returns something almost constant, the threshold stops varying
            // per pixel, and the dither silently degenerates into plain posterization — hard
            // contour bands, exactly where dithering was supposed to hide them.
            //
            // It only surfaced once things were seeded away from the origin, which is the worst
            // kind of latent bug: the feature looked correct for as long as nothing moved.
            float bayer4(float2 a)
            {
                int2 c = int2(floor(a)) & 3;      // two's complement AND gives the right residue
                uint x = (uint)c.x, y = (uint)c.y;
                uint v = ((y >> 1) & 1u);
                v = (v << 1) | ((x >> 1) & 1u);
                v = (v << 1) | (y & 1u);
                v = (v << 1) | (x & 1u);
                return float(v) * (1.0 / 16.0);
            }

            /// Posterize a 0..1 value into `steps` flat bands, dithering ONLY along the seams.
            ///
            /// Dithering a value everywhere is the classic trap: across the wide flat middle of a
            /// band every pixel is coin-flipped against the threshold and a huge area turns into
            /// a 50% checkerboard — the look of a paint program's transparency grid, which is
            /// what a naively dithered sky ends up as. A band's interior is MEANT to be flat;
            /// only its boundary needs breaking up. Measuring the distance to the nearest step
            /// boundary confines the dither to a narrow ribbon along each contour, which is
            /// exactly how a pixel artist shades a gradient by hand.
            /// Fully dithered quantization — the opposite policy to DitherStep, and the right one
            /// for anything TRANSLUCENT. On a solid surface a 50% checkerboard is a defect; on
            /// gas it is the technique — half the pixels of one density and half of the next is
            /// simply what half a step of gas looks like when the palette has no value in
            /// between. Dithering the whole range instead of the seams is how a pixel artist
            /// makes a cloud fade into empty space rather than end at a coastline.
            float DitherFull(float v, float steps, float2 cell)
            {
                float d = lerp(0.5, bayer4(cell), saturate(_DitherAmount));
                return floor(saturate(v) * steps + d) / steps;
            }

            float DitherStep(float v, float steps, float2 cell)
            {
                float q = saturate(v) * steps;
                float mid = 0.5 - abs(frac(q) - 0.5);          // 0 at a seam, 0.5 mid-band
                // Narrow ribbon: roughly the outer fifth of each band. Wider than this and the
                // flat interiors — the part that actually reads as pixel art — disappear under
                // texture.
                float gate = smoothstep(0.30, 0.08, mid);
                float d = lerp(0.5, bayer4(cell), gate * saturate(_DitherAmount));
                return floor(q + d) / steps;
            }

            /// Quantization WITHOUT dither — for palette restriction. Surface color comes from
            /// noise which varies everywhere, so a hard step here produces contour maps (the
            /// desired pixel-art character); dither is unnecessary and harmful on flat regions.
            /// Only LUMINANCE is stepped: quantizing RGB channels independently makes each channel
            /// jump at a different point and tears gradients into colored stripes.
            /// Smooth mode: bypassed entirely.
            float3 QuantizeLuma(float3 col, float steps)
            {
                if (_PixelArt < 0.5) return col;
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                if (lum <= 1e-4) return col;
                float ql = floor(lum * steps + 0.5) / steps;
                return col * (ql / lum);
            }

            // ── Star layer ─────────────────────────────────────────────────────────────────────
            // The position arrives UNSNAPPED; the cell is derived here in one step. Snapping first
            // and re-multiplying by _PPU (a divide→multiply round trip) produces 4.9999999, floor
            // rounds it into the neighboring cell and stars flicker at cell borders.
            //
            // Pixel mode: a star IS its cell pixel (authentic 1-px stars).
            // Smooth mode: the same cell hash places a soft round point INSIDE the cell — density
            // and seeds stay identical between the two modes, only the rendering changes.
            /// <paramref name="grid"/>: multiplier on the star lattice. Values above 1 pack the
            /// cells tighter — used for the unresolved haze layer, where individual stars are
            /// never meant to be picked out.
            float3 StarLayer(float2 layerPos, float density, float seed, float twinkle, float3 tint, float grid)
            {
                // Smooth mode: real star fields are not uniform. Stars crowd along the galactic
                // plane and thin out away from it, and that large-scale structure — rich regions
                // against near-empty ones — is most of what separates a photograph of the sky
                // from graph paper with dots on it.
                // Smooth mode also runs FAR denser than pixel mode. Pixel densities are tuned so
                // that single-pixel stars stay countable on a 360×200 grid; at full resolution
                // that same count leaves the sky nearly empty, and an empty sky reads as a dark
                // backdrop rather than as depth full of stars.
                if (_PixelArt < 0.5)
                    density *= (0.15 + 1.8 * fbm(layerPos * 0.03 + seed * 1.7, 2)) * 3.0;

                float2 cellF = layerPos * _PPU * grid * _StarGridScale;
                float2 cell = floor(cellF);

                // The lattice the stars SIT on is world-anchored (so they hold still while the
                // camera moves), but the lattice we HASH is shifted per sky. Without the shift
                // every sky asks the same cells the same question and gets the same answer: the
                // identical constellation, every time, with only the palette changing around it.
                // `cell` stays untouched because the sub-cell position is measured against it.
                float2 hc = cell + _StarSeed.xy;
                float h = hash21(hc + seed);
                if (h >= density) return 0.0;

                float b = saturate(h / max(density, 1e-6));
                // Stellar magnitude is steeply weighted: a dense wash of barely-there stars with
                // a few genuinely bright ones. The gentle ramp pixel mode uses reads as evenly
                // lit confetti once there are enough pixels to resolve it.
                b = _PixelArt > 0.5 ? 0.25 + 0.75 * b * b : 0.03 + 0.97 * pow(b, 2.2);

                if (twinkle > 0.0)
                {
                    float ph = hash21(hc + seed + 17.3) * 6.2831853;
                    b *= 1.0 - twinkle * (0.5 + 0.5 * sin(Now() * _TwinkleSpeed + ph));
                }

                // ── BLINKING (separate from twinkle) ──────────────────────────────────────────
                // Twinkle only pulses brightness, the pixel never disappears. True blinking needs
                // the pixel to fully vanish.
                //
                // Only a SMALL FRACTION of stars blink, deliberately: if they all did, the sky
                // would boil — the eye reads that as noise, not light, and has nothing to hold
                // on to. With just a few, the gaze catches on them and the sky feels alive —
                // the value of the motion comes from its scarcity.
                //
                // Every blinker gets its own rate and phase from the hash: a shared rhythm would
                // read as "the screen is flickering".
                if (_BlinkFraction > 0.0 && hash21(hc + seed + 41.7) < _BlinkFraction)
                {
                    float ph = hash21(hc + seed + 63.1) * 6.2831853;
                    float rate = lerp(_BlinkRateMin, _BlinkRateMax, hash21(hc + seed + 87.9));
                    float w = 0.5 + 0.5 * sin(Now() * rate + ph);
                    // Sine is soft; sharpen with a power → the pixel stays dark most of the time
                    // and flares briefly. The trough is exactly 0, so it truly DISAPPEARS.
                    b *= pow(w, _BlinkSharpness);
                }

                // Hue variety: warm (orange) ↔ cool (blue) — a real star field is not one color.
                float t = hash21(hc + seed + 5.1);
                float3 warm = float3(1.0, 0.78, 0.62);
                float3 cool = float3(0.68, 0.78, 1.0);
                float3 hue = lerp(cool, warm, t);

                // Pixel mode: snap magnitude to a few fixed levels. A star that can be any
                // brightness produces hundreds of nearly identical greys across the sky, and at
                // one pixel each that is indistinguishable from noise — the field sparkles but
                // never resolves into stars of different sizes. Three or four discrete
                // magnitudes give the eye something to sort: faint dust, ordinary stars, and the
                // few bright ones it can navigate by.
                if (_PixelArt > 0.5) b = floor(b * 3.0 + 0.5) / 3.0;

                float3 starCol = tint * lerp(1.0, hue, _StarHueVariety) * b;

                if (_PixelArt < 0.5)
                {
                    // Soft point: hashed position inside the cell, gaussian-ish falloff. Brighter
                    // stars get a slightly larger core so magnitude reads at a glance.
                    // Cartoon mode draws them as bigger, rounder dots so the sky reads as a
                    // picture book instead of a long-exposure photo.
                    float2 sp = float2(hash21(hc + seed + 2.7), hash21(hc + seed + 9.4));
                    float spread = _Cartoon > 0.5 ? (0.34 + 0.4 * b) : (0.16 + 0.26 * b);
                    float r = length(cellF - cell - sp) / spread;
                    starCol *= exp(-r * r * (_Cartoon > 0.5 ? 1.35 : 3.0));
                }

                return starCol;
            }

            // ── Bright stars ───────────────────────────────────────────────────────────────────
            // The few stars that actually read as points of LIGHT rather than specks: a hard
            // core, a soft halo, and faint diffraction spikes. They live on their own COARSE
            // lattice — one star per large cell — because a halo has to be wider than the cell
            // that owns it or it clips into a square, and because scarcity is the whole point:
            // it is the handful of bright ones that give a star field its scale and its depth.
            float3 BrightStars(float2 layerPos, float3 tint)
            {
                if (_PixelArt > 0.5 || _BrightStarAmount <= 0.001) return 0.0;

                float g = max(_PPU * 0.055 * _StarGridScale, 0.05);
                float2 cf = layerPos * g;
                float2 cell = floor(cf);
                float2 hc = cell + _StarSeed.xy;      // per-sky lattice, as in StarLayer
                float h = hash21(hc + 91.3);
                if (h >= _BrightStarAmount) return 0.0;

                // Jitter is kept off the cell walls so the glow stays inside its own cell.
                float2 jit = 0.3 + 0.4 * float2(hash21(hc + 3.17), hash21(hc + 8.93));
                float2 dv = cf - cell - jit;
                float r = length(dv);

                // A star is a POINT: an intense, almost hard core with only a thread of glow
                // around it. Weighting it the other way — a soft wide halo over a faint core —
                // turns every bright star into a cotton ball, which is the classic tell of a
                // drawn star field.
                float mag = 0.3 + 0.7 * hash21(hc + 17.71);
                float core = exp(-r * r * 5200.0);
                float halo = exp(-r * 34.0);

                // ── DIFFRACTION SPIKES ────────────────────────────────────────────────────────
                // The rays on a bright star in a telescope photograph are not part of the star:
                // they are the instrument, light bending around the supports that hold its
                // mirrors. That is why they matter here — nothing else in the frame says "this
                // is an image of space" as immediately, and they cost one angular term.
                //
                // The count is even and the pattern N-fold symmetric: three supports give the
                // six rays of a JWST image, four give the familiar Hubble cross. cos(angle · N/2)
                // raised to a high power turns those lobes into thin rays.
                //
                // Every star in the sky shares the same spike ORIENTATION, deliberately. The
                // pattern comes from one instrument, so it cannot vary from star to star —
                // rotating them individually is the quickest way to make the whole field look
                // hand-drawn.
                float ang = atan2(dv.y, dv.x);
                // Cartoon sparkles are a thick four-point twinkle. The default rays are the
                // thin diffraction spikes of a telescope photo.
                float spikeCount = _Cartoon > 0.5 ? 4.0 : _StarSpikeCount;
                float spikePower = _Cartoon > 0.5 ? 6.0 : 90.0;
                float lobes = abs(cos(ang * (spikeCount * 0.5)));
                // Brighter stars throw longer rays, as they do through a real aperture.
                float reach = exp(-r * lerp(30.0, 13.0, mag));
                float spike = pow(lobes, spikePower) * reach
                            + pow(lobes, _Cartoon > 0.5 ? 3.0 : 14.0) * exp(-r * 46.0) * 0.35;

                // A ray reaches further than the star's own cell, and this function looks at one
                // cell only — so a ray that leaves is not continued by the neighbour, it is cut,
                // square, and every bright star in the sky is cut at the same radius on a regular
                // lattice. The window closes at the star's ACTUAL distance to its nearest wall, so
                // a well-centred star keeps its full rays and only the ones crowding an edge are
                // trimmed, smoothly, before they can leave.
                float margin = min(min(jit.x, 1.0 - jit.x), min(jit.y, 1.0 - jit.y));
                spike *= 1.0 - smoothstep(margin * 0.72, margin, r);

                float tw = 1.0;
                if (_TwinkleAmount > 0.0)
                {
                    float ph = hash21(hc + 27.3) * 6.2831853;
                    tw = 1.0 - _TwinkleAmount * 0.35 * (0.5 + 0.5 * sin(Now() * _TwinkleSpeed * 0.6 + ph));
                }

                // ── BLINKING ──────────────────────────────────────────────────────────────────
                // Twinkling only breathes; the pixel never leaves. A blinking star goes out and
                // comes back, and that is a different kind of event — it draws the eye because
                // something CHANGED rather than merely wavered.
                //
                // It is applied to the bright stars specifically. Blinking the faint ones is
                // wasted: they are already at the edge of visibility, so their disappearance
                // reads as nothing at all. And only a fraction of these blink, because the value
                // of the effect is entirely in its rarity — a sky where everything blinks is a
                // sky that boils, and the eye stops being able to rest anywhere in it.
                if (_BlinkFraction > 0.0 && hash21(hc + 41.7) < _BlinkFraction * 2.0)
                {
                    float bph = hash21(hc + 63.1) * 6.2831853;
                    float rate = lerp(_BlinkRateMin, _BlinkRateMax, hash21(hc + 87.9)) * 0.35;
                    float w = 0.5 + 0.5 * sin(Now() * rate + bph);
                    // Sharpened so the star spends most of its time dark and flares briefly,
                    // reaching a true zero at the trough — it genuinely goes out.
                    tw *= pow(w, _BlinkSharpness);
                }

                float3 hue = lerp(float3(0.72, 0.82, 1.0), float3(1.0, 0.82, 0.66), hash21(hc + 5.1));
                float3 col = tint * lerp(1.0, hue, _StarHueVariety);
                // Spikes are weighted to actually READ. Held down to a hint they are technically
                // present and visually absent, which is the worst of both: the cost is paid and
                // the signature is missing.
                return col * mag * tw * (core * 1.6 + halo * 0.05 + spike * 0.9 * _StarSpikes);
            }

            // Round cartoon planet: a disc, a few flat light bands, simple stripes.
            float4 CasualStar(float2 dv, float radius)
            {
                float rad = max(radius, 1e-3) * 0.52;
                float2 p = dv / rad;
                float r2 = dot(p, p);
                if (r2 > 1.85) return 0.0;
                float r = sqrt(r2);

                float z = sqrt(saturate(1.0 - r2));
                float3 n = normalize(float3(p, max(z, 0.001)));
                float2 lxy = _PlanetLightDir.xy;
                if (dot(lxy, lxy) < 1e-4) lxy = float2(-0.4, 0.55);
                float3 L = normalize(float3(normalize(lxy), 0.6));
                float lit = saturate(floor(saturate(dot(n, L)) * 3.0) / 2.0);

                float stripe = step(0.5, frac(p.y * 2.2 + 0.15));
                float3 base = lerp(float3(0.99, 0.78, 0.46), float3(0.95, 0.52, 0.36), stripe);
                base = lerp(base, float3(0.98, 0.9, 0.72), smoothstep(0.62, 0.95, abs(p.y)));
                float3 body = base * lerp(0.58, 1.0, lit);

                float edge = smoothstep(0.72, 0.98, r2);
                body = lerp(body, body * 0.4, edge);

                float fill = 1.0 - smoothstep(0.985, 1.015, r);
                float halo = exp(-pow(max(r - 0.94, 0.0) * 4.2, 2.0)) * (1.0 - fill) * 0.5;
                return float4(body * fill + float3(1.0, 0.78, 0.48) * halo, fill);
            }

            // A nearby star. Cartoon skies get CasualStar; the photographic path keeps a
            // point, a halo and diffraction spikes. rgb is premultiplied; a is solid coverage.
            float4 PointStar(float2 sp, float2 center, float radius, float3 tint)
            {
                float2 dv = sp - center;
                if (_Cartoon > 0.5)
                    return CasualStar(dv, radius);

                float reach = max(radius, 1e-3);
                float r = length(dv) / reach;
                if (r > 1.45) return 0.0;

                float core = exp(-pow(r * 16.0, 2.0));
                float halo = exp(-r * 6.5);
                float ang = atan2(dv.y, dv.x);
                float lobes = abs(cos(ang * (_StarSpikeCount * 0.5)));
                float spike = pow(lobes, 80.0) * exp(-r * 2.1)
                            + pow(lobes, 12.0) * exp(-r * 4.2) * 0.45;
                float glow = core * 2.4 + halo * 0.42 + spike * 1.25 * max(_StarSpikes, 0.7);
                glow *= 1.0 - smoothstep(0.9, 1.4, r);

                float cover = smoothstep(0.085, 0.02, r);
                return float4(tint * glow * 1.6, cover);
            }

            // ── Distant galaxies ───────────────────────────────────────────────────────────────
            // Faint elliptical smudges scattered through the field — other galaxies, too far to
            // resolve into anything but a tilted oval of light. Every real deep-field photograph
            // has them, and they do something no star can: they say the void keeps going long
            // past the stars in it. Kept dim and rare; the moment they read as objects rather
            // than as a suspicion of light, they look like paint.
            float3 DistantGalaxies(float2 p)
            {
                if (_PixelArt > 0.5 || _FarGalaxyAmount <= 0.001) return 0.0;

                float g = max(_PPU * 0.03 * _StarGridScale, 0.03);
                float2 cf = p * g;
                float2 cell = floor(cf);
                float2 hc = cell + _StarSeed.zw;      // its own offset, so galaxies and stars
                if (hash21(hc + 55.7) >= _FarGalaxyAmount) return 0.0;   // do not correlate

                float2 jit = 0.3 + 0.4 * float2(hash21(hc + 2.31), hash21(hc + 7.13));
                float2 dv = cf - cell - jit;

                // Random inclination: a disc seen at an angle is an ellipse, and the mix of
                // face-on rounds and edge-on slivers is most of what makes a field read as real.
                float ang = hash21(hc + 11.9) * 6.2831853;
                float ca = cos(ang), sa = sin(ang);
                float2 e = float2(dv.x * ca - dv.y * sa, dv.x * sa + dv.y * ca);
                e.y /= 0.22 + 0.6 * hash21(hc + 19.3);

                float scale = 7.0 + 11.0 * hash21(hc + 23.7);
                float r = length(e) * scale;
                float core = exp(-r * r * 2.0) + exp(-r * 2.6) * 0.35;   // bulge + faint disc

                float3 hue = lerp(float3(1.0, 0.93, 0.80), float3(0.78, 0.86, 1.0), hash21(hc + 29.1));
                return hue * core * 0.42;
            }

            // ── Interstellar dust ──────────────────────────────────────────────────────────────
            // Cold clouds that emit nothing and ABSORB what lies behind them. Deep-space images
            // are shaped as much by these dark lanes as by the glowing gas: without absorption a
            // sky can only ever get brighter, so it flattens into an even wash. Returns the
            // fraction of the background that survives (1 = clear).
            float DustTransmission(float2 world, float2 cam)
            {
                if (_DustAmount <= 0.001) return 1.0;
                // Same reasoning as the nebula: at the pixel-mode scale the dust covers the whole
                // frame evenly, which just dims everything. Lanes need edges to read as lanes.
                float2 dp = LayerPos(world, cam, _NebParallax * 1.4) + _NebSeed.xy * 0.7;
                float ds = _NebScale * 1.6 * (_PixelArt < 0.5 ? 3.2 : 1.0);
                float n = _PhoneQuality > 0.5 ? ridged(dp * ds + 71.3, 2) : ridged(dp * ds + 71.3, 4);
                float lanes = smoothstep(0.40, 0.78, n);
                return 1.0 - lanes * saturate(_DustAmount);
            }

            /// <summary>
            /// The body-space transform as MATRICES: axis tilt around X, spin around Y. Sphere
            /// composes them and applies them with mul(); relief sampling reuses the same matrix
            /// so its offset probes land in exactly the same space.
            ///
            /// Do NOT write these rotations out as scalar arithmetic. That form is obvious and
            /// looks cheaper, and it collapses every 3D surface to a flat colour under NVIDIA's
            /// GLSL compiler. Inside the dynamic body loop it treats the per-pixel
            /// normal's contribution to those products as loop-invariant and hoists it out, so the
            /// sphere vector arrives as a per-draw constant and every noise field freezes. The 2D
            /// layers (stars, nebula, dust) are evaluated outside that loop and keep working,
            /// which makes the symptom read as "planets have no surface" rather than as a
            /// miscompile, and sends you hunting through the noise functions instead.
            ///
            /// [loop] on the body loops is not sufficient on its own: it makes Unity 6 emit GLSL
            /// the driver gets right, while Unity 2022's compiler produces a form that still
            /// breaks. A mul() against a matrix is three dot products, which the driver keeps
            /// intact on both, so this holds whichever Unity version compiled the shader.
            /// </summary>
            float3x3 RotX(float c, float s) { return float3x3(1.0, 0.0, 0.0,  0.0, c, -s,  0.0, s, c); }
            float3x3 RotY(float c, float s) { return float3x3(c, 0.0, -s,  0.0, 1.0, 0.0,  s, 0.0, c); }

            /// Scalar terrain height per kind — deliberately cheaper than the color pass (fewer
            /// octaves). Sampled three times per pixel in smooth mode to build a relief normal:
            /// craters become bowls that catch the sun, mountain ranges throw their flanks into
            /// shadow. Gas / toxic / star have no solid surface and return 0.
            float SurfaceHeight(int kind, float3 sp, float lon, float lat, float seed,
                                float fscale, float3 knobs)
            {
                float3 q = sp * (3.2 * fscale) + seed;

                // ONE noise call, whatever the kind — and that constraint is worth stating,
                // because the obvious version of this function is what made the shader expensive
                // to compile. Relief is built from a gradient, so this runs THREE times per
                // pixel, and HLSL inlines every branch of it each time. A per-kind dispatch with
                // one or two noise calls per branch therefore multiplied out to two dozen
                // inlined noise evaluations — the single largest thing in the program, in
                // service of a subtle bump.
                //
                // Craters are kept exact because a crater IS its relief: the pit catching light
                // on one wall is the whole effect, and a generic bump does not read as one.
                // Everything else takes a shared height field. Ice cracks, lava plates and dune
                // ridges are carried by their ALBEDO, which is untouched; the relief under them
                // only has to move the light a little, and it turns out not to matter whether
                // that bump precisely follows the pattern drawn on top of it.
                // Coverage must match CraterField's base octave exactly. The relief and the
                // albedo are two readings of the SAME craters; a different coverage would
                // populate different cells and light pits that are not drawn.
                if (kind == KIND_ROCKY)
                    return CraterOctave(q * (1.1 * knobs.x), 0.0, CRATER_COVERAGE) * 0.9;

                return fbm3(q * 1.1, 3) - 0.5;
            }

            /// <summary>
            /// Tilts a surface normal by a height field's gradient, with the tilt bounded.
            ///
            /// The bound is the whole point. A finite difference divided by its own step is a
            /// SLOPE, and this height field has cliffs: a crater wall climbs from floor to rim
            /// over roughly one probe step, so the raw gradient comes out several times the length
            /// of the normal it is perturbing. `normalize(n + 5t)` is not a tilted normal, it is a
            /// tangent — the surface turns edge-on and the wall breaks into black and white
            /// speckle instead of shading as a bowl. Every gentle slope stays exactly as it was;
            /// only the cliffs are held back, at about thirty-five degrees.
            /// </summary>
            float3 ReliefNormal(float3 n, float3 t1, float3 t2,
                                float h0, float h1, float h2, float strength)
            {
                float3 g = ((h0 - h1) * t1 + (h0 - h2) * t2) * strength;
                float m = length(g);
                // 0.28 is about sixteen degrees, and the ceiling matters more than it looks.
                // Crater walls are gentle; only the freshest are steep. More to the point, a
                // limiter set high enough to BIND everywhere
                // gives every wall on the body the same slope, so each crater wears an identical
                // ring and the surface reads as stamped. At sixteen degrees the limiter catches
                // only the cliffs and the rest of the field keeps its own shape.
                const float maxTilt = 0.28;
                if (m > maxTilt) g *= maxTilt / m;
                return normalize(n + g);
            }

            /// Per-kind atmosphere tint — used by the fresnel rim and the exterior halo (smooth mode).
            float3 AtmoColor(int kind, float3 c1, float3 c3)
            {
                if (kind == KIND_TERRAN) return float3(0.45, 0.68, 1.0);
                if (kind == KIND_ICE)    return float3(0.70, 0.85, 1.0);
                if (kind == KIND_LAVA)   return float3(1.0, 0.50, 0.25);
                if (kind == KIND_TOXIC)  return float3(0.72, 0.90, 0.35);
                if (kind == KIND_DESERT) return float3(0.95, 0.78, 0.55);
                if (kind == KIND_STAR)   return c3;
                return c1;   // gas giants glow in their own color; rocky barely shows any
            }

            // ── Planet surface dispatch ────────────────────────────────────────────────────
            // The generators themselves live one per file under Include/Planets/. Splitting them
            // out is not tidiness for its own sake: these branches are where the visual work
            // actually happens, and eight of them interleaved in one 200-line function made
            // changing any single kind an exercise in scrolling — and in not disturbing its
            // neighbours by accident.
            #include "Include/Planets/Rocky.hlsl"
            #include "Include/Planets/GasGiant.hlsl"
            #include "Include/Planets/Ice.hlsl"
            #include "Include/Planets/Lava.hlsl"
            #include "Include/Planets/Terran.hlsl"
            #include "Include/Planets/Desert.hlsl"
            #include "Include/Planets/Toxic.hlsl"
            #include "Include/Planets/Star.hlsl"

            float3 PlanetSurface(int kind, float3 sp, float lon, float lat, float seed, float fscale,
                                 float3 knobs, float3 c1, float3 c2, float3 c3,
                                 out float emissive, out float3 aux)
            {
                SurfaceCtx s;
                s.sp = sp;
                s.lon = lon;
                s.lat = lat;
                s.seed = seed;
                s.fscale = fscale;
                s.hires = _PixelArt < 0.5 && _Cartoon < 0.5;
                s.c1 = c1; s.c2 = c2; s.c3 = c3;

                // ── FEATURE SIZE ──────────────────────────────────────────────────────────────
                // Pixel mode gets deliberately COARSER features. A body is only ~34 texels
                // across down here, so detail authored for full resolution lands at one or two
                // texels and the surface dissolves into per-pixel noise — a mush with no
                // readable shape, which is the opposite of what pixel art is for. Fewer, bigger
                // shapes read; fine ones only sparkle. Applied once here, so every kind inherits
                // it instead of each having to remember. Cartoon goes coarser still: big blobs
                // of color, not photographic grain.
                float coarse = _Cartoon > 0.5 ? 0.38 : (s.hires ? 1.0 : 0.62);
                s.q = sp * (3.2 * fscale * coarse) + seed;
                knobs.y *= coarse;      // band / dune frequency follows the same rule
                s.knobs = knobs;

                SurfaceResult o;
                if      (kind == KIND_GAS)    o = Surface_GasGiant(s);
                else if (kind == KIND_ROCKY)  o = Surface_Rocky(s);
                else if (kind == KIND_ICE)    o = Surface_Ice(s);
                else if (kind == KIND_LAVA)   o = Surface_Lava(s);
                else if (kind == KIND_TERRAN) o = Surface_Terran(s);
                else if (kind == KIND_DESERT) o = Surface_Desert(s);
                else if (kind == KIND_TOXIC)  o = Surface_Toxic(s);
                else                          o = Surface_Star(s);

                emissive = o.emissive;
                aux = o.aux;
                return o.color;
            }

            /// Sphere shading: stepped terminator + self-illumination.
            float3 ShadeSphere(float3 n, float3 surf, float emissive, float ambient,
                               float2 cell, float3 L, float atten)
            {
                float ndl = saturate(dot(n, L));

                if (_Cartoon > 0.5)
                {
                    // Three flat bands. A soft gradient reads as a photograph; a couple of
                    // hard steps reads as a cartoon sphere.
                    float bands = 3.0;
                    float lit = saturate(floor(ndl * bands) / (bands - 1.0));
                    float shade = lerp(1.0 - _PlanetShadow, 1.0, lit) * atten
                                + ambient * (1.0 - lit);
                    float3 toon = surf * lerp(shade, 1.0, saturate(emissive));
                    float luma = dot(toon, float3(0.30, 0.59, 0.11));
                    return lerp(luma.xxx, toon, 1.28);
                }

                if (_PixelArt < 0.5)
                {
                    // Smooth mode: wrapped diffuse — the light reaches slightly past 90°, turning
                    // the knife-edge terminator into a twilight band. Space photos have exactly
                    // this: a soft gradient into night, never a hard line.
                    //
                    // Only the DIRECT term carries the sun's distance falloff; ambient does not.
                    // Attenuating ambient too would sink far bodies into featureless black, and
                    // the night side is lit by the rest of the sky, not by this star.
                    // The wrap is kept small. It exists to soften the terminator into twilight,
                    // not to light the night side — every unit of wrap pushes illumination past
                    // 90°, and a generous value quietly turns the dark half into a dim grey half.
                    float wrapped = saturate((dot(n, L) + 0.04) / 1.04);
                    float shade = lerp(1.0 - _PlanetShadow, 1.0, wrapped) * atten
                                + ambient * (1.0 - wrapped);
                    return surf * lerp(shade, 1.0, saturate(emissive));
                }

                float steps = max(_PlanetLightSteps, 1.0);

                // ── TERMINATOR ────────────────────────────────────────────────────────────────
                // Seam-confined dithering (DitherStep) rather than a blanket dither. With the sun
                // low — which is what gives a body its crescent — ndl changes across the whole
                // disc, so a gradient-driven gate stays open everywhere and the entire planet
                // ends up under a checkerboard. Confining the dots to the boundaries BETWEEN
                // light steps leaves flat terraces of light with broken edges: the way a pixel
                // artist shades a sphere, and the reason the shape reads at all.
                float lit = DitherStep(ndl, steps, cell);
                float shade = lerp(1.0 - _PlanetShadow, 1.0, lit) * atten + ambient * (1.0 - lit);

                // ── HUE SHIFT ─────────────────────────────────────────────────────────────────
                // Darkening a colour only walks it toward grey, and a sky full of grey shadows
                // is the flattest thing pixel art can do. Every pixel artist shifts hue WITH
                // value instead: shadows swing cool (violet-blue, the colour of skylight filling
                // them), lit faces swing warm (the colour of the sun). It costs no extra palette
                // entries and it is most of what separates hand-drawn pixel art from a sprite
                // that has simply been multiplied by a grey ramp.
                float3 tint = lerp(float3(0.68, 0.74, 1.14), float3(1.10, 1.03, 0.88), lit);
                tint = lerp(1.0, tint, saturate(_PixelHueShift));

                return surf * tint * lerp(shade, 1.0, saturate(emissive));
            }

            /// A single celestial body disc. rgb = color, a = coverage.
            /// <paramref name="pixelWorld"/>: world-space size of one screen pixel, computed ONCE
            /// outside the planet loop — derivative instructions (fwidth) inside a dynamic loop
            /// force full unrolling and break compilation on GL targets.
            /// <paramref name="center"/> is snapped to the body's texel grid; <paramref
            /// name="centerTrue"/> is where the body actually is. See the two-centres note below.
            float4 Sphere(float2 sp, float2 center, float2 centerTrue, float radius, float2 cell,
                          float ppu, int kind,
                          float seed, float spin, float tilt, float fscale,
                          float cloudAmount, float cloudSpin, float atmosphere,
                          float ringInner, float ringOuter, float depth,
                          float3 c1, float3 c2, float3 c3, float pixelWorld)
            {
                // ── TWO CENTRES: A STILL INTERIOR AND A LIVING HULL ───────────────────────────
                // The body samples its SURFACE from a centre snapped to its own texel grid, so
                // `sp - centre` always lands on exact texel multiples: the interior is the same
                // texels wherever the body sits, and moving never re-quantizes it.
                //
                // The SILHOUETTE is drawn from the true centre, and that split is the whole
                // point rather than a compromise. Snapping both made every planet a
                // mathematically perfect pixel circle — four-fold symmetric, byte-identical
                // frame after frame — and a body that changes neither inside nor at its edge
                // stops reading as an object and starts reading as a stamp. The outline is the
                // one place sub-texel motion earns its keep: it costs a pixel of shimmer along
                // the hull and buys back the irregular, living edge a hand-drawn planet has.
                //
                // Smooth mode has no grid, so Snap is the identity and the two are the same
                // vector — the whole mechanism costs it nothing.
                float2 dG = (sp - centerTrue) / radius;    // coverage, alpha, scattering shell
                float r2G = dot(dG, dG);
                float rlenG = sqrt(r2G);

                float2 d = (sp - center) / radius;         // surface, lighting, relief
                // A texel whose snapped position lands off the sphere while its true position is
                // still inside it is held AT the limb rather than allowed to leave the sphere,
                // where lon/lat stop meaning anything.
                d *= rsqrt(max(dot(d, d), 1.0));
                float r2 = dot(d, d);
                float rlen = sqrt(r2);

                // ── EDGE CONTRACT (smooth mode) ───────────────────────────────────────────────
                // A planet must never carry an outline. The scattering shell is ONE continuous
                // profile evaluated on BOTH sides of the limb — rising inside it, peaking at
                // r=1, decaying outward — and it is ADDED, never scaled by the body's coverage.
                //
                // Scaling it by coverage is exactly what drew the black ring: coverage goes to
                // zero at the limb, so the glow that belongs there vanished and left an unlit
                // gap between the halo outside and the lit surface inside. Compositing is
                // therefore premultiplied — the body occludes with `alpha`, the shell emits
                // regardless of it.
                //
                // Both widths grow with atmosphere: an airless rock stays nearly crisp, a cloud
                // world wears a deep glowing envelope.
                // Shell widths, in fractions of the body's radius. These are DELIBERATELY small:
                // a real planet wears a thread of atmosphere, not a bloom cloud. When the halo
                // reached out a full radius every body looked like it was emitting light, and —
                // worse — the glows filled the gaps between bodies, so the void that carries all
                // the depth in a space image was washed out. A star is the one exception: its
                // corona genuinely extends, and it should be the only source of light in frame.
                float hw = kind == KIND_STAR ? 0.22 : (0.012 + atmosphere * 0.04);
                float soft = 0.02 + atmosphere * 0.035;   // inward rise / edge softness

                // This body's own light: toward the star when the sky has one (see LightDirFor).
                float2 lightXY = LightDirFor(center);
                // Kept APART on purpose. `attenGeom` is how much of the star's light reaches this
                // distance, `pulse` is how much the star is putting out right now. Everything
                // that receives light wants the product; the scattering shell wants the pulse on
                // its own PROFILE instead of its colour, so that pixel mode can quantize a
                // corona that grows and shrinks rather than one that brightens off-palette.
                float attenGeom = SunAttenFor(center);
                float pulse = StarLightPulse(center);
                float atten = attenGeom * pulse;
                float3 L = normalize(float3(lightXY, LightElevation()));

                // A star lights ITSELF, so its corona is radial — equally bright all the way
                // round. Running it through the same sunward term as a planet asks "which way is
                // the sun from here?" of the sun itself; the answer is a zero vector, the code
                // falls back to +x, and the corona comes out brighter on one arbitrary side with
                // a visible seam where the term crosses zero.
                float litF = kind == KIND_STAR
                           ? 1.0
                           : pow(saturate(dot(d / max(rlen, 1e-4), lightXY) * 0.5 + 0.5), 1.6);
                float3 atmoCol = AtmoColor(kind, c1, c3);
                // Even bare rock gets a whisper of scatter: a razor edge against black reads as
                // a cut-out sticker, and the eye catches the cut before it catches the rock.
                // The floor is a whisper (0.04), not a glow — and the lit/unlit ratio is steep so
                // the shell is a sunward crescent rather than a ring around the whole body.
                float shellAmt = max(saturate(atmosphere), 0.04) * _AtmoIntensity;
                float3 shellCol = atmoCol * shellAmt * (0.02 + 0.98 * litF) * attenGeom
                                * lerp(1.0, 0.4, saturate(depth));   // distance takes the glow too

                if (r2G > 1.0)
                {
                    // Outside the disc: the shell's outer tail. In pixel mode a planet still
                    // keeps its hard disc — only a star's corona survives out here, posterized;
                    // see the branch below for why the two kinds part ways.
                    //
                    // Two-scale falloff: a bright short lobe against the rim plus a LONG faint
                    // tail — a single exponential reads as a thin ring that stops dead. The
                    // window term forces an exact zero at the far boundary, so the glow never
                    // ends on a visible band; it simply runs out of light.
                    // A star's corona needs far more room than a planet's air shell, and it has
                    // to run out of light rather than stop. The cutoff exists only to bound the
                    // work; if the glow is still worth seeing when it arrives, the boundary is
                    // what you see instead of the glow — which is exactly what a bright corona
                    // squeezed into a planet's extent does.
                    float ext = hw * (kind == KIND_STAR ? 9.0 : 4.0);
                    if (rlenG > 1.0 + ext) return 0.0;
                    // Measured from the hull, so the glow stays welded to the outline it belongs
                    // to. Clamped at zero because a texel can sit just outside the true disc and
                    // just inside the snapped one; that texel gets the brightest ring, which is
                    // exactly what a pixel hugging the limb should have.
                    float x = max(rlenG - 1.0, 0.0);
                    float fall = 0.75 * exp(-x / hw) + 0.25 * exp(-x / (hw * 3.0));
                    // The window starts late and closes at the boundary, so it only ever trims a
                    // tail that has already faded rather than cutting a visible edge.
                    fall *= 1.0 - smoothstep(ext * 0.72, ext, x);

                    if (_PixelArt > 0.5)
                    {
                        // Returning nothing here is the right answer for a PLANET: its shell is a
                        // thread a fraction of a texel wide, and a fraction of a texel of glow is
                        // not a halo, it is the outline this file spends the paragraphs above
                        // forbidding.
                        //
                        // For a STAR it is wrong. The one body in the sky that IS the light source
                        // would sit in pitch black while smooth mode gives it a corona reaching
                        // two radii out — a sun lighting every other planet in frame and throwing
                        // nothing into the space around itself.
                        //
                        // So the star's corona is drawn in pixel mode too, from the SAME profile,
                        // spoken in the pixel-art language: posterized into a handful of
                        // concentric rings on the body's own grid (rlen is already stepped, since
                        // sp arrives snapped), with the dither policy for translucent gas —
                        // DitherFull, not DitherStep — breaking the seams between the rings.
                        if (kind != KIND_STAR) return 0.0;
                        // Half the surface's step budget: the corona is one colour fading out, and
                        // spending eight levels on it makes a smooth gradient wearing a pixel
                        // grid, which is the one thing pixel mode must not look like.
                        //
                        // The pulse goes in BEFORE the quantizer, which is the whole reason it is
                        // carried separately from the shell's colour: the corona then grows and
                        // shrinks by whole rings as the star breathes. Applied after, it would
                        // slide the ring brightness continuously between palette levels — a
                        // smooth gradient wearing a pixel grid, which is the defect this mode
                        // exists to avoid.
                        fall = DitherFull(fall * pulse, max(floor(_PlanetColorSteps * 0.5), 3.0), cell);
                    }
                    else fall *= pulse;
                    return float4(shellCol * fall * 0.45, 0.0);   // emits, occludes nothing
                }

                // Inside the disc: coverage fades over the SAME soft width the shell uses, so
                // the two sides of the rim meet without a seam. Never harder than one pixel.
                float alpha = 1.0;
                if (_PixelArt < 0.5)
                {
                    float aaW = max(pixelWorld / radius, soft);
                    alpha = smoothstep(1.0, 1.0 - aaW, rlenG);
                }

                float z = sqrt(saturate(1.0 - r2));
                float3 n = float3(d, z);                       // view-space normal (for lighting)

                // Undo the axis tilt → planet space (Y = spin axis).
                float ct = cos(tilt), st = sin(tilt);
                float3x3 tiltRot = RotX(ct, st);
                float3 ns = mul(tiltRot, n);

                // ROTATION is done by GENUINELY rotating the vector around the Y axis, not by
                // adding to longitude. The addition trick is incompatible with 3D sampling; it
                // also moves the wrap point, tying the seam's location to time.
                //
                // Through a matrix, never as scalar products — see RotX/RotY for the miscompile
                // that costs every planet its surface when this is written out by hand.
                // Held in whole-texel steps in pixel mode — see SnapSpin. This one line is the
                // difference between a planet that turns and a planet that re-pixelates.
                float a = SnapSpin(Now() * spin, radius * ppu);
                float ca = cos(a), sa = sin(a);
                float3x3 bodyRot = mul(RotY(ca, sa), tiltRot);
                float3 sp3 = mul(bodyRot, n);

                float lon = atan2(sp3.x, sp3.z);
                float lat = asin(clamp(sp3.y, -1.0, 1.0));   // invariant under Y rotation

                // Per-planet structural variation, derived from the seed once per pixel:
                // x feature scale, y banding/dune frequency, z free 0..1.
                float3 knobs = float3(0.75 + frac(seed * 0.731) * 0.85,
                                      9.0 + frac(seed * 1.377) * 9.0,
                                      frac(seed * 2.113));

                float emissive;
                float3 aux;
                float3 surf = PlanetSurface(kind, sp3, lon, lat, seed, fscale, knobs,
                                            c1, c2, c3, emissive, aux);

                // ── RELIEF (smooth mode, solid surfaces) ──────────────────────────────────────
                // The color pass alone shades every pixel with the geometric sphere normal, so
                // craters and mountain ranges read as painted-on stains. Sampling the terrain
                // height at two tangent offsets builds a perturbed normal: bowls catch the sun on
                // one wall and shadow the other, ranges throw their flanks into darkness. This is
                // the single strongest "it is 3D terrain, not a texture" cue.
                float3 nS = n;
                bool solid = kind == KIND_ROCKY || kind == KIND_ICE || kind == KIND_LAVA ||
                             kind == KIND_TERRAN || kind == KIND_DESERT;
                if (_PixelArt < 0.5 && solid)
                {
                    float3 up = abs(n.z) < 0.99 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
                    float3 t1 = normalize(cross(up, n));
                    float3 t2 = cross(n, t1);
                    const float e = 0.035;
                    // h0 is the height at THIS point, and for a rocky body the surface pass has
                    // already computed it and left it in aux.z. Only the two offset samples need
                    // the function.
                    float h0 = kind == KIND_ROCKY ? aux.z * 0.9
                                                  : SurfaceHeight(kind, sp3, lon, lat, seed, fscale, knobs);
                    float3 p1 = mul(bodyRot, normalize(n + t1 * e));
                    float3 p2 = mul(bodyRot, normalize(n + t2 * e));
                    float h1 = SurfaceHeight(kind, p1, atan2(p1.x, p1.z),
                                             asin(clamp(p1.y, -1.0, 1.0)), seed, fscale, knobs);
                    float h2 = SurfaceHeight(kind, p2, atan2(p2.x, p2.z),
                                             asin(clamp(p2.y, -1.0, 1.0)), seed, fscale, knobs);
                    // Small bodies get gentler relief — at their screen size strong bump reads
                    // as noise, not terrain.
                    float bumpAmt = (kind == KIND_TERRAN ? 0.10 : 0.15) * saturate(radius * 0.8);
                    nS = ReliefNormal(n, t1, t2, h0, h1, h2, bumpAmt / e);
                }

                // The cloud layer rotates at a DIFFERENT speed (differential rotation) — one of
                // the strongest 3D cues on its own, because the two layers slide against each
                // other and depth separates.
                if (cloudAmount > 0.001)
                {
                    // The cloud deck steps on its own grid too: left continuous it would boil
                    // over a body that had stopped doing so, which reads worse than both moving.
                    float a2 = SnapSpin(Now() * (spin + cloudSpin), radius * ppu);
                    float ca2 = cos(a2), sa2 = sin(a2);
                    float3 cp = mul(RotY(ca2, sa2), ns);
                    float3 cwarp = 0.0;
                    if (_PixelArt < 0.5)
                        cwarp = (fbm3(cp * 1.2 + 31.0, 3) - 0.5) * 0.7;   // stretched, weather-like fronts
                    float cl = fbm3(cp * 2.4 + seed * 7.3 + cwarp, 4);
                    float cover = smoothstep(0.54, 0.78, cl) * cloudAmount;
                    if (_PixelArt < 0.5)
                    {
                        // Cloud shadows: the same deck sampled slightly toward the sun darkens
                        // the ground beneath — clouds float ABOVE the surface instead of being
                        // painted onto it.
                        float clS = fbm3(cp * 2.4 + seed * 7.3 + cwarp + float3(lightXY, 0.0) * 0.18, 3);
                        surf *= 1.0 - smoothstep(0.54, 0.78, clS) * cloudAmount * 0.35;
                    }
                    surf = lerp(surf, float3(0.95, 0.96, 1.0), cover);
                }

                // ── DISTANCE ──────────────────────────────────────────────────────────────────
                // Detail is the first thing distance takes away. Flattening the surface toward
                // its own average albedo keeps the body's colour while dissolving the features
                // inside it, so a far world reads as a world you cannot quite make out rather
                // than a small one rendered in full clarity. Drawing every body at identical
                // sharpness is what pins them all to the same plane, however different their
                // sizes are — the eye reads detail as proximity.
                float3 avgAlbedo = (c1 + c2 + c3) * 0.33333;
                surf = lerp(surf, avgAlbedo, saturate(depth) * 0.55);

                // ── PIXEL-ART CONTRAST ────────────────────────────────────────────────────────
                // Expand the surface's range about its own average before quantizing it.
                //
                // Quantization does not preserve differences, it preserves DISTANCES BETWEEN
                // LEVELS. Two features five percent apart in albedo land on the same level and
                // become one flat area — so exactly the fine structure that distinguishes a
                // world is what disappears first, while the broad shapes survive. Shrinking the
                // features (which is what the coarse factor does) makes them fit; expanding the
                // contrast is what makes them SURVIVE the palette.
                //
                // This is why pixel art of anything is drawn with more contrast than the thing
                // it depicts: the medium spends its levels on what it can afford to show.
                if (_PixelArt > 0.5)
                {
                    float3 avgSurf = (c1 + c2 + c3) * 0.33333;
                    surf = max(0.0, avgSurf + (surf - avgSurf) * 1.5);
                }

                // A star's own output pulses, and ShadeSphere cannot carry it: an emissive body
                // ignores the incoming-light term by construction (that is what emissive MEANS),
                // so the star would sit at a fixed brightness while its corona and every planet
                // it lights breathed around it.
                //
                // Folded into the surface colour BEFORE the quantizer, for the same reason the
                // corona is: in pixel mode the palette LEVELS shift, so the contour bands migrate
                // across the disc as the star brightens. That is how a pixel artist animates a
                // sun — colour cycling, not a wash — and it is the only form of the effect the
                // mode can actually show.
                if (kind == KIND_STAR) surf *= pulse;

                // Palette restriction is applied BEFORE shading, to the surface color. Applying
                // it after meant re-quantizing the FLAT areas the light steps had produced; being
                // dithered, those flat areas turned into checkerboards.
                surf = QuantizeLuma(surf, max(_PlanetColorSteps, 2.0));

                // Block width in disc-normalized space: the dither gate's unit of measure.
                // Shading uses the RELIEF normal (nS) so terrain actually catches the light;
                // everything about the body's silhouette (fresnel, limb, alpha) keeps the
                // geometric normal (n) — relief must never dent the outline.
                float3 col = ShadeSphere(nS, surf, emissive, _PlanetAmbient, cell, L, atten);

                // The second half of the distance cue: value. Detail loss alone still leaves a
                // far body sitting at the same brightness as a near one, and equal value reads
                // as equal distance no matter what else differs. Emissive bodies are exempt —
                // a star that dimmed with depth would stop looking like a light source.
                col *= lerp(1.0, 0.42, saturate(depth) * (1.0 - saturate(emissive)));

                float ndl = saturate(dot(n, L));

                if (_PixelArt > 0.5)
                {
                    // Pixel mode: the original additive lit rim. POSTERIZE happens in
                    // QuantizeLuma above — the rim is added after so the halo steps too.
                    //
                    // A star lights ITSELF, so its rim is radial, for the same reason its corona
                    // is (see litF): asking "which way is the sun from here?" of the sun returns
                    // a zero vector, the fallback points at +x, and the inner edge of the corona
                    // lit up on one arbitrary side with the rest of the ring dark.
                    float rim = smoothstep(0.70, 1.0, r2) * atmosphere
                              * (kind == KIND_STAR ? 1.0 : ndl);
                    col += c1 * rim;
                }
                else
                {
                    // ── Smooth-mode light package — what makes a disc read as a WORLD ─────────
                    // Limb darkening: light leaves a gas sphere obliquely at the edges, so the
                    // rim is dimmer than the disc center. Strong on gaseous bodies, gentle on
                    // solid ones. This alone removes most of the "flat sticker" look.
                    //
                    // It is deliberately NOT taken all the way down: past a point it stops
                    // reading as curvature and starts reading as a drawn border. The scattering
                    // shell (added after this branch) is what carries the last stretch to the
                    // limb.
                    if (_Cartoon > 0.5 && kind != KIND_STAR)
                    {
                        // A thin ink rim, in the same spirit as the game's toon outlines.
                        float edge = smoothstep(0.62, 0.96, r2);
                        col = lerp(col, col * 0.28, edge);
                    }
                    else
                    {
                        float limb = (kind == KIND_GAS || kind == KIND_TOXIC || kind == KIND_STAR) ? 0.38 : 0.16;
                        col *= lerp(1.0 - limb, 1.0, sqrt(saturate(n.z)));
                    }

                    // Ring shadow: a ringed giant wears its ring's shadow as a soft banded arc —
                    // one of the strongest "this is a real 3D system" cues a sky can have.
                    if (ringOuter > ringInner && ringInner > 0.0)
                    {
                        float3 P = float3(d, z);
                        float ctr = cos(tilt), str = sin(tilt);
                        // The ring is also ROLLED about the view axis before it is drawn, and the
                        // shadow has to be cast by the ring that is actually there. Rotating the
                        // plane normal by the same roll is the whole correction; without it the
                        // shadow band crossed the disc at an angle unrelated to the ring above it,
                        // on every planet whose roll was not near zero — which is most of them.
                        float rollS = frac(seed * 7.919) * 6.2831853;
                        float croS = cos(rollS), sroS = sin(rollS);
                        float3 nr = float3(-ctr * sroS, ctr * croS, -str);
                        float denom = dot(nr, L);
                        if (abs(denom) > 0.05)
                        {
                            float tt = -dot(nr, P) / denom;
                            float rr2 = length(P + L * tt);
                            if (tt > 0.0 && rr2 > ringInner && rr2 < ringOuter)
                                col *= 0.55 + 0.25 * fbm(float2(rr2 * 9.0 + seed, seed * 2.0), 2);
                        }
                    }

                    // Night-side city lights (terran only, via aux.x): civilization glitters
                    // where the sun does not reach.
                    col += float3(1.0, 0.82, 0.5) * aux.x * saturate(1.0 - ndl * 2.5) * 0.85;

                    // ── AURORA ────────────────────────────────────────────────────────────────
                    // A curtain of light around the poles of a body with a magnetic field and an
                    // atmosphere to excite. It earns its place by being the only effect that
                    // lives on the NIGHT side: everything else in the lighting model fades as the
                    // terminator passes, so a planet's dark limb is where the eye has nothing to
                    // hold — an aurora puts something there without pretending the sun reaches it.
                    if (atmosphere > 0.25 && kind != KIND_STAR && kind != KIND_GAS)
                    {
                        // Y is the spin axis in body space, so |ns.y| near 1 is a polar cap.
                        float polar = smoothstep(0.62, 0.95, abs(ns.y));
                        // Curtains, not a solid ring: the band is broken up along longitude and
                        // drifts, which is what makes it read as moving light rather than paint.
                        float curtain = fbm3(float3(sp3.x, sp3.y * 0.4, sp3.z) * 5.0
                                             + float3(Now() * 0.06, 0.0, 0.0) + seed, 3);
                        float lit2 = saturate(1.0 - ndl * 1.8);          // strongest past the terminator
                        float3 auroraCol = kind == KIND_TOXIC ? float3(0.9, 0.5, 1.0)
                                                              : float3(0.35, 1.0, 0.65);
                        col += auroraCol * polar * smoothstep(0.45, 0.85, curtain) * lit2 * 0.55;
                    }

                    // Sun glint on open water (terran only, via aux.y) — relief normal so the
                    // glint rides the waves' parent geometry.
                    float3 refl = 2.0 * dot(nS, L) * nS - L;
                    col += float3(1.0, 0.97, 0.9) * pow(saturate(refl.z), 48.0) * aux.y * ndl * 0.4;

                    // Gentle saturation lift: multi-layer lighting desaturates; pull the hues
                    // back so worlds stay vivid against the void.
                    float lum = dot(col, float3(0.299, 0.587, 0.114));
                    col = max(0.0, lum + (col - lum) * 1.15);

                    // The night limb melts into space: where the rim is BOTH unlit and at the
                    // very edge, coverage fades — a hard dark circle against black reads as a
                    // sticker, a dissolving one reads as a body sitting IN space.
                    float edge = smoothstep(0.72, 1.0, r2G);
                    alpha *= 1.0 - edge * (1.0 - saturate(ndl * 2.2 + emissive)) * 0.45;
                }

                // Inner half of the scattering shell — the same profile the outside tail uses,
                // so the two meet at r=1 with no seam and no gap.
                float3 shell = 0.0;
                if (_PixelArt < 0.5)
                    shell = shellCol * smoothstep(1.0 - soft * 3.5, 1.0, rlenG) * pulse * 0.45;

                // Premultiplied: the body occludes by `alpha`, the shell adds on top of whatever
                // shows through. This is what lets space swallow the edge instead of outlining it.
                return float4(col * alpha + shell, alpha);
            }


            /// A moon: a small rocky body, and deliberately NOT routed through Sphere.
            ///
            /// Sphere carries every kind's surface generator, the relief dispatch, cloud layers,
            /// ring shadows, aurora, city lights and water glint — and HLSL inlines all of it at
            /// every call site — so routing moons through it would compile that entire body into
            /// the fragment program a second time, for nothing: a rock uses almost none of it.
            /// Inlined bodies are most of this shader's compile time.
            ///
            /// This is the same machinery cut down to what a moon actually needs — rocky surface,
            /// crater relief, terminator, distance. Splitting the kinds into their own files is
            /// what made it cheap to write: Surface_Rocky can be called directly, without
            /// dragging the other seven in behind it.
            float4 MoonSphere(float2 lp, float2 center, float radius, float ppu,
                              float seed, float depth, float3 c1, float3 c2, float3 c3,
                              float pixelWorld)
            {
                // A moon snaps to ITS OWN surface grid, from the unsnapped layer position and its
                // own surfacePPU — which the composer computes from the moon's radius and uploads
                // for exactly this. Passing the parent planet's already-snapped position and cell
                // instead draws a moon a tenth of its planet's size in the planet's blocks: a
                // surface ten times too coarse for its size on screen, a handful of giant squares
                // where a small pitted disc belongs.
                float2 cell = floor(lp * ppu);
                float2 sp = _PixelArt > 0.5 ? cell / ppu : lp;

                // Two centres, exactly as Sphere does it — a moon needs the split more than a
                // planet does. It is on a 3D orbit that moves it every frame, and it is the
                // smallest body in the sky: an unsnapped centre re-quantized its whole disc
                // continuously, which on something a handful of texels across is the entire moon
                // flickering. Snapped to the MOON's grid, which is not its planet's.
                float2 dG = (sp - center) / radius;              // silhouette
                float r2G = dot(dG, dG);
                if (r2G > 1.0) return 0.0;

                float2 d = (sp - Snap(center, ppu)) / radius;    // surface and lighting
                d *= rsqrt(max(dot(d, d), 1.0));
                float r2 = dot(d, d);

                float alpha = 1.0;
                if (_PixelArt < 0.5)
                {
                    float aaW = max(pixelWorld / radius, 0.02);
                    alpha = smoothstep(1.0, 1.0 - aaW, sqrt(r2G));
                }

                float z = sqrt(saturate(1.0 - r2));
                float3 n = float3(d, z);

                // The tilt and spin a moon is given at its call site, kept here as constants.
                const float tilt = 0.2;
                float ct = cos(tilt), st = sin(tilt);
                // A moon is small, so its texel radius is small and its steps are coarse — which
                // is correct: a body eight texels across genuinely has only a couple of dozen
                // distinguishable rotation states, and pretending otherwise is the boil.
                float a = SnapSpin(Now() * 0.05, radius * ppu);
                float ca = cos(a), sa = sin(a);
                float3x3 bodyRot = mul(RotY(ca, sa), RotX(ct, st));   // matrix, never scalar: see RotX
                float3 sp3 = mul(bodyRot, n);

                SurfaceCtx s;
                s.sp = sp3;
                s.lon = atan2(sp3.x, sp3.z);
                s.lat = asin(clamp(sp3.y, -1.0, 1.0));
                s.seed = seed;
                s.fscale = 2.0;
                s.hires = _PixelArt < 0.5 && _Cartoon < 0.5;
                s.c1 = c1; s.c2 = c2; s.c3 = c3;

                float coarse = _Cartoon > 0.5 ? 0.38 : (s.hires ? 1.0 : 0.62);
                s.q = sp3 * (3.2 * s.fscale * coarse) + seed;
                s.knobs = float3(0.75 + frac(seed * 0.731) * 0.85,
                                 (9.0 + frac(seed * 1.377) * 9.0) * coarse,
                                 frac(seed * 2.113));

                SurfaceResult sr = Surface_Rocky(s);
                float3 surf = sr.color;

                // Crater relief, read straight from the same field the colour used.
                float3 nS = n;
                if (s.hires)
                {
                    float3 up = abs(n.z) < 0.99 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
                    float3 t1 = normalize(cross(up, n));
                    float3 t2 = cross(n, t1);
                    const float e = 0.035;
                    float cs = 1.1 * s.knobs.x;
                    float3 p1 = mul(bodyRot, normalize(n + t1 * e));
                    float3 p2 = mul(bodyRot, normalize(n + t2 * e));
                    // 0.92, matching CraterField's base octave — the coverage a moon's ALBEDO is
                    // drawn with. These were 0.55, left behind when the field was rewritten, and
                    // because the coverage test is a threshold on the same hash the 0.55 set is a
                    // strict subset: roughly two in five craters on every moon were painted with
                    // no relief under them, which reads as a stain rather than a hole.
                    float h0 = sr.aux.z;     // already computed by Surface_Rocky above
                    float h1 = CraterOctave((p1 * (3.2 * s.fscale) + seed) * cs, 0.0, CRATER_COVERAGE);
                    float h2 = CraterOctave((p2 * (3.2 * s.fscale) + seed) * cs, 0.0, CRATER_COVERAGE);
                    nS = ReliefNormal(n, t1, t2, h0, h1, h2, 0.15 * saturate(radius * 0.8) / e);
                }

                // Distance takes detail first, then value — the same two cues Sphere spends.
                surf = lerp(surf, (c1 + c2 + c3) * 0.33333, saturate(depth) * 0.55);
                surf = QuantizeLuma(surf, max(_PlanetColorSteps, 2.0));

                float3 L = normalize(float3(LightDirFor(center), LightElevation()));
                float3 col = ShadeSphere(nS, surf, 0.0, _PlanetAmbient, cell, L,
                                         SunAttenFor(center) * StarLightPulse(center));
                col *= lerp(1.0, 0.42, saturate(depth));

                if (_PixelArt < 0.5)
                {
                    col *= lerp(0.82, 1.0, n.z);      // gentle limb darkening; rock is not gas
                    float ndl = saturate(dot(n, L));
                    // The unlit limb dissolves into space rather than ending on a hard circle.
                    // Coverage, so it rides the hull with the rest of the silhouette.
                    float edge = smoothstep(0.72, 1.0, r2G);
                    alpha *= 1.0 - edge * (1.0 - saturate(ndl * 2.2)) * 0.45;
                }

                return float4(col * alpha, alpha);
            }

            /// Ring: ray-plane intersection. With an orthographic camera the ray is (x, y, t);
            /// the ring plane's normal rotates with the planet's axis tilt. Because it is a true
            /// 3D intersection the ring passes IN FRONT of and BEHIND the planet — a fake ellipse
            /// cannot do that.
            /// returns: rgb ring color, a coverage; behind: is the ring behind the planet at this pixel.
            float4 Ring(float2 sp, float2 center, float radius, float tilt,
                        float inner, float outer, float seed, float3 c1, out float behind)
            {
                behind = 0.0;
                if (outer <= inner) return 0.0;

                float2 d = (sp - center) / radius;

                // ── ROLL ──────────────────────────────────────────────────────────────────
                // The tilt below rotates the ring plane about the X axis only, which changes how
                // squashed the ellipse is but never which way it points: the major axis came out
                // horizontal every single time, so every ringed planet in the package wore its
                // ring dead level. A roll about the view axis is what lets one lie diagonally and
                // the next stand nearly upright. Derived from the body's own seed, so it needs no
                // extra data and stays deterministic.
                float roll = frac(seed * 7.919) * 6.2831853;
                float cro = cos(roll), sro = sin(roll);
                d = float2(d.x * cro + d.y * sro, -d.x * sro + d.y * cro);

                // Ring plane normal: +y rotated by tilt around the x axis.
                float ct = cos(tilt), st = sin(tilt);
                float3 nr = float3(0.0, ct, -st);
                if (abs(nr.z) < 0.02) return 0.0;                 // exact edge-on view — a line

                float t = -(nr.x * d.x + nr.y * d.y) / nr.z;      // intersection depth with the plane
                float3 p = float3(d.x, d.y, t);
                float r = length(p);
                if (r < inner || r > outer) return 0.0;

                // +z is TOWARD the viewer — Sphere builds its normal as (d, +sqrt(1-r2)), the
                // outward normal of the near hemisphere, and the light's elevation is positive
                // for a sun behind the camera. So a ring point at positive t is in FRONT of the
                // planet's centre plane, and testing it the other way round hands the near half of
                // every ring to the "draw it behind" pass. Since t = d.y / tan(tilt), that is the
                // top half — every ringed planet wearing its near side threaded behind it.
                behind = t < 0.0 ? 1.0 : 0.0;

                // ── Ring structure ────────────────────────────────────────────────────────
                // A ring is a BAND of countless particles, not a hoop. Note the sense of the gap
                // terms below: inverting one zeroes everything except two hairlines at the inner
                // and outer edges, leaving a wire loop — which is exactly what a drawn-on circle
                // looks like.
                float u = (r - inner) / (outer - inner);

                // Density varies with radius and only with radius: a ring is azimuthally uniform
                // because every particle has had billions of orbits to smear out along its path.
                // Two scales of variation give broad regions and fine ringlets within them.
                float bands = fbm(float2(u * 6.0 + seed, seed * 2.0), 3) * 0.72
                            + fbm(float2(u * 14.0 + seed * 3.1, seed), 2) * 0.28;
                // Rings are mostly EMPTY. Driving the density up until the band is solid turns a
                // cloud of ice into a painted disc with stripes on it; the planet has to stay
                // visible through it, and the structure has to be a suggestion rather than a
                // diagram.
                float a = saturate(0.10 + bands * 0.72);

                // Divisions: narrow gaps swept clear by resonances with the moons. Narrow is the
                // whole point — a wide one reads as two rings rather than one ring with a gap.
                float div1 = smoothstep(0.020, 0.075, abs(u - (0.36 + frac(seed * 1.7) * 0.12)));
                float div2 = smoothstep(0.012, 0.050, abs(u - (0.70 + frac(seed * 3.3) * 0.10)));
                a *= lerp(1.0, div1, 0.85) * lerp(1.0, div2, 0.55);

                // Edges fade rather than stop: the outer boundary of a real ring is where the
                // particles thin out, and a hard rim is the other half of the drawn-hoop look.
                a *= smoothstep(0.0, 0.13, u) * (1.0 - smoothstep(0.80, 1.0, u));

                // Lit from the same source as its planet (positional when the sky has a star).
                // Rolled into ring space with the geometry, or the shadow below would fall on the
                // wrong side of the ring.
                float2 lxy = LightDirFor(center);
                lxy = float2(lxy.x * cro + lxy.y * sro, -lxy.x * sro + lxy.y * cro);
                float3 L = normalize(float3(lxy, LightElevation()));
                float lit = (0.55 + 0.45 * saturate(dot(normalize(nr), L)))
                          * SunAttenFor(center) * StarLightPulse(center);

                // ── The planet's shadow falls across the ring ─────────────────────────────
                // The cheapest possible statement that these two things occupy the same space.
                // Without it the ring reads as a decal pasted over the planet — it is drawn in
                // front, it is drawn behind, but nothing about it acknowledges the sphere.
                // Cylinder test: a ring point lies in shadow when it is on the far side of the
                // planet from the light and within a planet-radius of the axis through it.
                float along = dot(p, L);
                float perp = sqrt(max(r * r - along * along, 0.0));
                float shadow = (along < 0.0) ? 1.0 - smoothstep(0.92, 1.12, perp) : 0.0;
                lit *= 1.0 - shadow * 0.82;

                // Backscatter: dust is brightest seen almost edge-on to the light, which is why
                // a ring flares where it passes in front of its planet.
                float3 viewN = normalize(nr);
                lit *= 1.0 + 0.35 * pow(1.0 - abs(viewN.z), 2.0);

                return float4(c1 * lit, saturate(a) * 0.55);
            }

            // ── Galaxy ─────────────────────────────────────────────────────────────────────────
            // A tilted spiral disc, additive like the nebula. Structure comes from a logarithmic
            // spiral: the arm phase advances with BOTH the polar angle and log(r), which is what
            // makes arms wind outward instead of forming concentric rings. fbm turbulence breaks
            // the mathematically clean sinusoid into patchy, believable arms.
            float3 Galaxy(float2 world, float2 cam, float2 cellScreen)
            {
                if (_GalaxyA.z <= 0.0) return 0.0;

                float2 gp = Snap(LayerPos(world, cam, _GalaxyA.w), _PPU);
                float2 p = (gp - _GalaxyA.xy) / _GalaxyA.z;

                // Screen-plane rotation, then inclination: dividing y squashes the disc into an
                // ellipse — the cheap but convincing "seen at an angle" cue.
                float cr = cos(_GalaxyB.x), sr = sin(_GalaxyB.x);
                p = float2(p.x * cr - p.y * sr, p.x * sr + p.y * cr);
                p.y /= max(_GalaxyB.y, 0.15);

                float r = length(p);
                // The disc is bounded only to stop the work, never to draw an edge. At r = 1.7 the
                // exponential still has a couple of percent of light left in it, and a couple of
                // percent against a near-black sky is perfectly visible — so the cutoff was
                // drawing the ellipse itself across the frame as a hard diagonal seam. Extend the
                // bound and fade into it, so the boundary only ever removes light that has
                // already gone.
                if (r > 2.4) return 0.0;
                float edgeFade = 1.0 - smoothstep(1.35, 2.4, r);

                float theta = atan2(p.y, p.x);
                float phase = theta * _GalaxyB.w + log(max(r, 1e-3)) * _GalaxyC.y - Now() * _GalaxyC.w;
                float arm = 0.5 + 0.5 * cos(phase);
                float turb = fbm(p * 3.0 + _GalaxyB.z, 4);
                arm = saturate(arm + (turb - 0.5) * 0.9);

                float falloff = exp(-r * 2.2);
                float armGlow = pow(arm, 2.2) * falloff;
                float core = exp(-r * r / max(_GalaxyC.z * _GalaxyC.z, 1e-4));

                // A sparse sprinkle of individually resolved stars inside the disc — reads as
                // "made of stars" instead of "colored fog".
                float speck = step(0.997, hash21(floor(gp * _PPU) + _GalaxyB.z)) * falloff * 1.5;

                float intensity = saturate(armGlow + core * 1.4 + speck);
                if (_PixelArt > 0.5)
                {
                    // Quantize the additive intensity with a Bayer offset — an un-stepped glow
                    // would be the only smooth gradient left on a posterized screen.
                    float steps = max(_PlanetColorSteps, 2.0);
                    intensity = floor(intensity * steps + bayer4(cellScreen) * _DitherAmount) / steps;
                }

                float coreW = saturate(core * 1.4 / max(intensity, 1e-4));
                float3 gcol = lerp(_GalaxyColB.rgb, _GalaxyColA.rgb, coreW);
                return gcol * intensity * _GalaxyC.x * edgeFade;
            }

            // ── Asteroid belt ──────────────────────────────────────────────────────────────────
            // A band of rubble: space along the band is divided into cells, every cell hashes into
            // "rock or no rock". Rock density falls off with distance from the band's spine, so
            // the belt has a dense core and ragged edges instead of two hard rails.
            // Returns rgb color, a coverage (rocks are opaque).
            float4 Belt(float2 world, float2 cam, float pixelWorld)
            {
                if (_BeltA.w <= 0.0) return 0.0;

                float2 bp = Snap(LayerPos(world, cam, _BeltB.w), _PPU);
                float2 dir = float2(cos(_BeltA.z), sin(_BeltA.z));
                float2 nrm = float2(-dir.y, dir.x);
                float2 rel = bp - _BeltA.xy;

                float across = dot(rel, nrm);
                if (abs(across) > _BeltA.w * 1.6) return 0.0;

                // Drift makes the belt a slow river of rock — dead-static rubble reads as a
                // texture, moving rubble reads as objects in space.
                float along = dot(rel, dir) + Now() * _BeltB.z;

                float cellSize = max(_BeltB.x, 0.05);
                float2 bc = float2(along, across) / cellSize;
                float2 cellId = floor(bc);

                float h = hash21(cellId + _BeltC.x);
                float band = exp(-(across * across) / max(2.0 * _BeltA.w * _BeltA.w * 0.35, 1e-4));
                if (h >= _BeltB.y * band) return 0.0;

                // Rock center jitter and radius are chosen so the rock always stays inside its own
                // cell — that keeps the test single-cell (no neighbor scan).
                float2 rc = cellId + float2(0.35 + 0.30 * hash21(cellId + 7.7),
                                            0.35 + 0.30 * hash21(cellId + 13.1));
                // Size follows a power law in smooth mode — rubble is mostly gravel with the
                // occasional boulder, and drawing sizes evenly gives a belt of suspiciously
                // similar rocks. Pixel mode keeps the even draw: down there the smallest rocks
                // land on one or two pixels, and a field of sub-pixel specks shimmers as the
                // camera pans.
                float sizeT = hash21(cellId + 29.3);
                if (_PixelArt < 0.5) sizeT = pow(sizeT, 2.2);
                float rr = lerp(_BeltC.z, _BeltC.w, sizeT);
                float2 d = bc - rc;

                // Irregular silhouette: the radius wobbles with the polar angle. Perfect circles
                // read as bubbles, not rock.
                float2 dl = d;
                float rScale = rr;

                if (_PixelArt < 0.5)
                {
                    // ── Shape ────────────────────────────────────────────────────────────────
                    // Asteroids are broken fragments, not pebbles: elongated, lumpy, and tumbling
                    // end over end. A near-circle with one gentle wobble — which is all the base
                    // silhouette is — reads as a drawn blob no matter how well it is shaded.
                    //
                    // The rock is rotated into its own frame first, so the elongation axis
                    // tumbles with it instead of being pinned to the screen.
                    float tumble = h * 6.2831853 + Now() * (0.05 + 0.22 * hash21(cellId + 51.7))
                                                 * (hash21(cellId + 63.1) < 0.5 ? -1.0 : 1.0);
                    float ct = cos(tumble), st = sin(tumble);
                    dl = float2(d.x * ct - d.y * st, d.x * st + d.y * ct);
                    dl.x /= 0.55 + 0.5 * hash21(cellId + 71.3);      // elongation

                    // Silhouette noise sampled on the unit DIRECTION rather than on the polar
                    // angle: the angle wraps at ±π and leaves a visible notch, the direction
                    // vector does not. Two octaves give the flat faces and sharp corners of a
                    // collision fragment.
                    float2 dir = normalize(dl + 1e-5);
                    float shape = 1.0
                        + (vnoise(dir * 2.3 + h * 31.0) - 0.5) * 0.55
                        + (vnoise(dir * 5.7 + h * 57.0) - 0.5) * 0.28;
                    rScale = rr * max(shape, 0.25);
                }
                else
                {
                    float ang = atan2(d.y, d.x);
                    float wob = 1.0 + (vnoise(float2(ang * 1.43 + h * 31.0, h * 47.0)) - 0.5) * 0.55;
                    rScale = rr * wob;
                }

                float rd = length(dl) / max(rScale, 1e-4);
                if (rd > 1.0) return 0.0;

                float alpha = 1.0;
                if (_PixelArt < 0.5)
                    alpha = saturate((1.0 - rd) * rr * cellSize / max(pixelWorld, 1e-5));

                // Fake-sphere shading from the sky's sun, plus per-rock tone variation. Each rock
                // resolves its own direction, so a belt sweeping past a star lights up along it.
                float3 n3 = normalize(float3(dl / max(rScale, 1e-4), sqrt(saturate(1.0 - rd * rd))));
                float3 L = normalize(float3(LightDirFor(bp), LightElevation()));
                float tone = 0.65 + 0.6 * hash21(cellId + 3.3);
                float3 albedo = _BeltCol.rgb * tone;

                if (_PixelArt < 0.5)
                {
                    // ── Surface ──────────────────────────────────────────────────────────────
                    // Craters, and the shadows they cast. Without them a rock is a shaded ball;
                    // the pitting is what says "airless body that has been hit for four billion
                    // years". The normal is perturbed by the crater field rather than only the
                    // colour, so the pits catch the light on one wall and shadow the other.
                    float3 sp3 = n3 * 2.6 + h * 40.0;
                    float w1 = worley3(sp3);
                    float pit = 1.0 - smoothstep(0.0, 0.34, w1);
                    float rim = smoothstep(0.30, 0.38, w1) * (1.0 - smoothstep(0.38, 0.5, w1));
                    float grain = fbm3(sp3 * 3.5, 3);

                    albedo *= 0.78 + 0.42 * grain;                 // patchy regolith
                    albedo = lerp(albedo, albedo * 0.6, pit * 0.8);
                    albedo += _BeltCol.rgb * rim * 0.25;

                    // Cheap relief: push the normal away from crater centres.
                    float2 gr = float2(worley3(sp3 + float3(0.16, 0, 0)) - w1,
                                       worley3(sp3 + float3(0, 0.16, 0)) - w1);
                    n3 = normalize(n3 + float3(gr * 2.2, 0.0));
                }

                float ndl = saturate(dot(n3, L));
                // Airless rock: a hard terminator and a night side that is genuinely dark, lifted
                // only by the faintest bounce.
                float shade = (_PixelArt < 0.5 ? lerp(0.06, 1.0, ndl) : lerp(0.22, 1.0, ndl))
                            // Attenuation is per ROCK (it is a distance cue across the belt), but
                            // the pulse is read once at the belt's centre. Per rock it is a
                            // spatially varying quantity that pixel mode then quantizes, and the
                            // belt would shimmer in bands as the states swept along it.
                            * SunAttenFor(bp) * StarLightPulse(_BeltA.xy);
                float3 rock = QuantizeLuma(albedo * shade, max(_PlanetColorSteps, 2.0));
                return float4(rock * _BeltC.y, alpha);
            }

            // ── Shooting stars / comets ────────────────────────────────────────────────────────
            // Time is cut into windows; each window hashes into one streak (spawn point, heading,
            // and a small period jitter so consecutive stars never fall into a metronome rhythm).
            // The streak is a moving head with an exponentially fading tail along its path.
            float3 Comets(float2 world, float2 cam)
            {
                if (_CometA.x <= 0.0) return 0.0;

                float2 p = Snap(LayerPos(world, cam, _CometB.x), _PPU);
                // Where the camera looks, expressed in this layer's space — streaks must spawn
                // around the VISIBLE region, which moves with the camera.
                float2 viewC = cam * _CometB.x;

                float3 acc = 0.0;
                int tracks = (int)clamp(_CometB.w, 1.0, 2.0);
                // [loop], like every other loop here: the trip count comes from a uniform, and a
                // uniform trip count is exactly what the NVIDIA GL compiler mishandles when it
                // decides to unroll and hoist. This was the last loop in the stack still missing
                // it, which means the bug it invites would only ever have shown up on a customer's
                // machine — GLCore drops or freezes the streaks while D3D11 looks fine.
                [loop] for (int tr = 0; tr < tracks; tr++)
                {
                    float period = _CometA.x * (0.75 + 0.5 * hash21(float2(tr * 3.1, _CometB.y)));
                    float t = Now() + _CometB.y * 13.7 + tr * period * 0.5;
                    float win = floor(t / period);
                    float ft = t - win * period;

                    float2 wh = float2(win, tr * 7.13 + _CometB.y);
                    float travel = 26.0;
                    float dur = travel / max(_CometA.y, 1.0);
                    if (ft > dur) continue;

                    // Spawn across the upper part of the view, heading down at a shallow diagonal.
                    float2 start = viewC + float2((hash21(wh + 1.7) - 0.5) * 46.0,
                                                  4.0 + hash21(wh + 5.3) * 10.0);
                    float ang = lerp(3.60, 4.25, hash21(wh + 9.1));           // ~206°..244°
                    float2 dirc = float2(cos(ang), sin(ang));
                    float2 head = start + dirc * _CometA.y * ft;

                    // Distance to the tail segment behind the head.
                    float2 toP = p - head;
                    float back = clamp(dot(toP, -dirc), 0.0, _CometA.z);
                    float2 closest = head - dirc * back;
                    float dist = length(p - closest);

                    float widthW = 1.6 / _PPU;                                 // ~1-2 px wide
                    float lateral = exp(-dist * dist / (widthW * widthW));
                    float alongFade = 1.0 - back / max(_CometA.z, 1e-4);       // tail thins out
                    float life = sin(3.14159 * saturate(ft / dur));            // fade in/out
                    float head2 = exp(-dot(toP, toP) / max(_CometB.z * _CometB.z, 1e-5));

                    float intensity = (lateral * alongFade * alongFade * 0.8 + head2 * 1.2) * life;

                    // Pixel mode: posterize the streak so its glow speaks the same stepped
                    // language as the planet terminators — a smooth gradient would be the one
                    // airbrushed element on an otherwise quantized screen.
                    if (_PixelArt > 0.5)
                    {
                        float steps = max(_PlanetColorSteps, 2.0);
                        intensity = floor(saturate(intensity) * steps + 0.5) / steps;
                    }

                    acc += _CometCol.rgb * intensity;
                }
                return acc * _CometA.w;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                // View-plane coordinates stay full-width at every camera angle.
                // _SkyTravel scrolls that plane as the camera moves.
                OUT.worldPos = pos.positionVS.xy + _SkyTravel.xy;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Dev-only triage view: dumps each noise layer raw to the screen so a driver
                // miscompilation can be located in one render instead of guessed at.
                // Development triage only — flip to 1 to render raw noise fields instead of a
                // sky. Compiled out otherwise: it drags a whole worley3, ridged3, fbm3 and
                // vnoise3 body into the program for a feature that ships disabled.
                #define BIRICH_DEBUG_NOISE 0
                #if BIRICH_DEBUG_NOISE
                if (_DebugNoise > 0.5)
                {
                    float2 wp = IN.worldPos * 0.5;
                    int m = (int)(_DebugNoise + 0.5);
                    float v;
                    if (m == 1)      v = hash31(float3(floor(wp.x * 4.0), floor(wp.y * 4.0), 3.0));
                    else if (m == 2) v = vnoise3(float3(wp, 7.7));
                    else if (m == 3) v = fbm3(float3(wp, 7.7), 4);
                    else if (m == 4) v = worley3(float3(wp, 3.3));
                    else if (m == 5) v = ridged3(float3(wp, 5.5), 4);
                    else if (m == 7)
                        // Uniform-array delivery test: red = _PlanetD[0].z (featureScale, expect
                        // ~0.45-0.65 gray if delivered, BLACK if the array never arrives),
                        // green = _PlanetD[0].x (cloud), blue = _PlanetB[0].w (kind, reference).
                        return half4(_PlanetD[0].z * 0.5, _PlanetD[0].x, _PlanetB[0].w * 0.15, 1.0);
                    else             v = hash21(floor(wp * 4.0));   // known-good 2D reference
                    return half4(v, v, v, 1.0);
                }
                #endif

                float2 world = IN.worldPos;
                // Drift is a virtual camera glide: adding it to the camera position moves every
                // layer at its own parallax rate (near stars fastest, giants slowest), so even a
                // perfectly static camera reads as slowly traveling through space.
                float2 cam = _SkyTravel.xy + _DriftVec.xy * Now();

                // World-space size of one screen pixel — measured here, in uniform control flow,
                // because derivative instructions are illegal inside the dynamic body loops below.
                float pixelWorld = max(length(fwidth(world)) * 0.7071, 1e-5);

                // ── Base gradient
                // Broken up on purpose. A pure linear ramp across the frame is the single most
                // artificial thing a space background can do: nothing in the sky varies smoothly
                // from one screen edge to the other, so the eye reads it instantly as a painted
                // backdrop — and in 8 bits it bands into visible stripes. Warping the ramp with
                // a large-scale field turns it into uneven galactic glow instead.
                float g = dot(normalize(_GradientDir.xy + 1e-5), world) * _GradientScale + 0.5;
                if (_PixelArt < 0.5 && _PhoneQuality < 0.5)
                    g += (fbm(world * 0.012 + 5.3, 3) - 0.5) * 0.55;
                // The backdrop ramp is deliberately NOT quantized in pixel mode. Its two colours
                // are both near black, so the ramp between them spans a couple of values and
                // banding is invisible — but posterizing it turns the empty sky into two flat
                // slabs with a wide dithered seam between them, and that seam is the single most
                // distracting thing on screen because nothing else is competing with it.
                float3 col = lerp(_BaseColor.rgb, _BaseColor2.rgb, saturate(g));

                // ── Nebula. Phones skip the domain warp and the 4–5 octave stacks.
                if (_NebAmount > 0.001 && _PhoneQuality > 0.5)
                {
                    float2 phoneLp = LayerPos(world, cam, _NebParallax);
                    float2 phoneNp = phoneLp + _NebSeed.xy;
                    float phoneNs = _NebScale * _NebSeed.z * 3.2;
                    float phoneN = fbm(phoneNp * phoneNs, 2);
                    float phoneMask = saturate(pow(saturate(phoneN), _NebContrast));
                    phoneMask *= smoothstep(0.32, 0.66, fbm(phoneNp * (_NebScale * _NebSeed.z * 1.8) + 91.7, 2));
                    float3 phoneNeb = lerp(_NebColB.rgb, _NebColA.rgb, saturate(phoneN));
                    phoneNeb = lerp(phoneNeb, _NebColC.rgb, saturate(pow(saturate(phoneN), _NebContrast * 2.2)));
                    col += phoneNeb * (phoneMask * _NebAmount);
                }
                else if (_NebAmount > 0.001)
                {
                    // Offset the sampling domain by the sky's own seed. Without this the noise
                    // field is IDENTICAL in every sky: the same filaments in the same places,
                    // the same voids, and only the palette changing between them. Two seeds in a
                    // row then read as the same nebula recoloured, which is exactly what a
                    // procedural generator must never do.
                    float2 lp = Snap(LayerPos(world, cam, _NebParallax), _PPU);

                    // The dither lattice is taken HERE, from the plain layer position, and never
                    // from the warped one below. Ordered dithering only works on a grid aligned
                    // with the pixels it is dithering; deriving the cell from a domain-warped
                    // coordinate shears and stretches that grid along the warp, so the threshold
                    // pattern smears into the long diagonal corduroy that was showing up across
                    // the gas instead of disappearing into it.
                    float2 ditherCell = floor(lp * _PPU);

                    float2 np = lp + _NebSeed.xy;
                    // The default scale fits roughly ONE noise period across the frame, which
                    // means the whole screen sits inside a single blob and the gas can only ever
                    // be a flat wash. Both modes pack several cloud masses into the view — and at
                    // the SAME scale, deliberately.
                    //
                    // Coarsening the pixel field slightly looks like a kindness to the low
                    // resolution, but scale is not a rendering choice: it is the shape of the
                    // cloud. Change it and the same seed produces a different nebula in each mode,
                    // which is exactly what switching modes must never do — the planets keep their
                    // identity across the switch, and gas that does not is simply a bug wearing
                    // the right palette.
                    float ns = _NebScale * _NebSeed.z * 3.2;

                    // Domain warp. Ridged noise left to itself lays down a suspiciously even
                    // web — filaments of similar length meeting at similar angles, which the eye
                    // spots as a pattern immediately. Dragging the sampling position around with
                    // a second noise field shears that lattice into strands that bunch, stretch
                    // and tear the way real gas does.
                    float2 w = float2(fbm(np * ns * 0.6 + 3.11, 3),
                                      fbm(np * ns * 0.6 + 17.93, 3)) - 0.5;
                    np += w * (1.6 / max(ns, 1e-4));

                    float n1 = _NebStyle > 1.5 ? ridged(np * ns, 5) : fbm(np * ns, 5);
                    float n2 = fbm(np * ns * 2.3 + 19.7, 4);
                    float mask = saturate(pow(saturate(n1), _NebContrast));
                    if (_NebStyle > 0.5 && _NebStyle < 1.5) mask = saturate(mask * 1.4 - 0.1); // billow: more massive

                    // Presence mask, applied in BOTH modes: a nebula is somewhere, not
                    // everywhere. Gas spread over the whole frame reads as a coloured filter over
                    // the lens however good its internal structure is — what makes it a cloud is
                    // having an edge, with dark sky on the other side of that edge. It matters
                    // even more in pixel mode, where a cloud covering the frame means the frame
                    // is covered in dither and nothing is left to contrast against it.
                    // Frequency matters as much as the mask: below about one period per frame the
                    // cloud is simply present or absent for a whole sky and its edge never enters
                    // the shot, which is the one thing that proves it is a cloud and not a filter.
                    // The presence frequency is tied to the SCREEN, not to the mode's cloud
                    // scale: it has to put a cloud edge inside the frame either way. Deriving it
                    // from the pixel-mode scale gives a period wider than the view, so the mask
                    // comes out constant and the whole sky becomes simply gas or no gas.
                    mask *= smoothstep(0.32, 0.66, fbm(np * (_NebScale * _NebSeed.z * 1.8) + 91.7, 3));

                    float3 neb = lerp(_NebColB.rgb, _NebColA.rgb, n2);
                    neb = lerp(neb, _NebColC.rgb, saturate(pow(saturate(n1), _NebContrast * 2.2))); // hot core

                    // ── Structure, in BOTH modes ──────────────────────────────────────────
                    // This was gated to smooth mode, and that was the mistake behind a pixel
                    // nebula that looked nothing like its smooth twin. Filaments, voids and the
                    // contrast that gathers gas into knots are what a nebula IS; they are not a
                    // high-resolution embellishment. Locking them away left pixel mode with the
                    // bare low-frequency base — one soft blob — and no amount of dithering or
                    // palette work downstream can put structure back into a field that has none.
                    //
                    // What should differ between the modes is how the result is DRAWN: same gas,
                    // quantized and dithered. Not a different, poorer gas.
                    float fil = ridged(np * ns * 3.1 + 41.0, 4);
                    mask *= 0.55 + 0.75 * fil;
                    mask *= 0.35 + 0.9 * fbm(np * ns * 6.5 + 13.0, 3);
                    neb *= 0.85 + 0.5 * fil;
                    // Structure costs brightness — every multiplier above averages below one, and
                    // the dust absorbs the cloud on top of that. Put the light back, but put it
                    // back UNEVENLY: a nebula is bright knots inside a great deal of nothing, and
                    // gas spread evenly over the frame is fog, not a nebula.
                    mask = pow(saturate(mask * 2.4), 1.4) * 1.5;

                    float amt = mask * _NebAmount;

                    if (_PixelArt > 0.5)
                    {
                        // ── Pixel-art gas ────────────────────────────────────────────────────
                        // A nebula rendered as a continuous additive wash carries hundreds of
                        // intermediate colours — it is airbrush, and it sits wrong next to
                        // posterized planets. Pixel art states gas the same way it states
                        // everything else: a few flat densities, with ordered dithering doing
                        // the work of the values in between. Both the DENSITY and the gas TONE
                        // are stepped, so the cloud resolves into distinct bands rather than a
                        // smooth ramp that merely happens to be pixellated.
                        // Step the MASK, not the finished density. The density is the mask times
                        // the archetype's amount (often ~0.6), so quantizing it afterwards spends
                        // only the bottom half of the available levels and the cloud collapses
                        // into "gas" and "no gas". Quantizing the mask spreads the full set of
                        // levels across the cloud whatever its overall strength.
                        float2 nc = ditherCell;

                        // COLOUR takes the full dither, not the seam-confined one, and it takes
                        // enough levels to be a blend rather than a switch. Two seam-dithered
                        // levels — which is what this was — collapse three palette colours into
                        // roughly one: the mix lands on an endpoint almost everywhere, so the
                        // second gas tone barely appears and the hot core almost never does.
                        //
                        // The distinction is the same one the surfaces follow. A planet's crust
                        // is opaque, so its bands must stay flat and only their seams break up.
                        // Gas is translucent: a checkerboard of two tones IS the tone between
                        // them, and dithering the whole range is how a limited palette says a
                        // colour it does not contain.
                        neb = lerp(_NebColB.rgb, _NebColA.rgb, DitherFull(n2, 4.0, nc));
                        neb = lerp(neb, _NebColC.rgb,
                                   DitherFull(pow(saturate(n1), _NebContrast * 2.2), 3.0, nc));
                        // Concentrate the gas before stepping it. Straight fbm averages around
                        // its midpoint, so the cloud ends up thinly spread over the entire frame
                        // and the sky reads as tinted glass with a dark hole in it — the figure
                        // and the ground swap places. Pushing the contrast leaves dark sky as the
                        // ground and lets the gas gather into masses that sit on top of it.
                        // Gas is translucent, so it takes the FULL dither (see DitherFull): the
                        // cloud has to thin out into the void gradually, and a dotted blend
                        // between two densities is how pixel art spells a gradient. Confining the
                        // dither to the seams — correct for a planet's surface — gives the cloud
                        // flat interiors and a hard coastline, which is what made it read as a
                        // silhouette pasted over the sky instead of gas hanging in it.
                        // More density levels than the eye can count, so the cloud thins into
                        // the void as a gradient rather than as a staircase. Ordered dither
                        // carries roughly sixteen sub-levels between each pair, which is what
                        // lets a handful of steps read as a smooth fade while every pixel stays
                        // on the palette.
                        // Quantize across the mask's ACTUAL range. The shared structure pass
                        // leaves it reaching about 1.5, so saturating first threw away a third of
                        // the cloud's brightness — which is why the pixel gas came out darker
                        // than the smooth gas built from the very same field.
                        const float maskPeak = 1.5;
                        amt = DitherFull(saturate(mask / maskPeak), 9.0, nc) * _NebAmount * maskPeak;
                    }

                    col += neb * amt;
                }

                // ── Galaxy (between nebula and the far stars — it is the deepest structure)
                float3 deep = Galaxy(world, cam, floor(LayerPos(world, cam, _GalaxyA.w) * _PPU));

                // ── Far stars (BEHIND the planets)
                deep += StarLayer(LayerPos(world, cam, _FarStarParallax),
                                  _FarStarDensity, 3.7, 0.0, _FarStarColor.rgb, 1.0) * _FarStarBright;

                // ── Unresolved haze: stars too distant and too crowded to separate, packed on a
                //    finer lattice at a fraction of the brightness. This is the faint granular
                //    glow behind every deep-sky photograph — the reason the void never looks
                //    like flat paint even where no single star can be picked out.
                if (_PixelArt < 0.5)
                {
                    deep += StarLayer(LayerPos(world, cam, _FarStarParallax * 0.6),
                                      _FarStarDensity * 2.2, 23.1, 0.0, _FarStarColor.rgb, 2.6) * 0.22;

                    // Other galaxies, deepest of everything — behind the dust with the rest.
                    deep += DistantGalaxies(LayerPos(world, cam, _FarStarParallax * 0.4));
                }

                // ── Mid stars: the third rung of the depth ladder. Two layers read as "front
                //    and back"; three read as continuous depth — the parallax gradient between
                //    them is what sells the distance. Slight twinkle, still behind the planets.
                deep += StarLayer(LayerPos(world, cam, _MidStarParallax),
                                  _MidStarDensity, 7.3, _TwinkleAmount * 0.4, _MidStarColor.rgb, 1.0) * _MidStarBright;

                // ── Dust absorption. Everything DEEP (galaxy, far and mid stars) is seen through
                //    the dust; the base glow is dimmed by it too. Near stars sit in front of the
                //    clouds and are left alone, which is exactly what gives the lanes their
                //    depth — you can tell which stars are behind them and which are not.
                float dust = DustTransmission(world, cam);
                col *= lerp(1.0, dust, 0.7);
                col += deep * dust;

                // ── Celestial bodies. C# sorts the arrays far-to-near → later ones cover earlier ones.
                int pc = (int)min(_PlanetCount, (float)MAX_PLANETS);
                int mc = (int)min(_MoonCount, (float)MAX_MOONS);

                // How much of this pixel is solid matter, accumulated as the bodies composite.
                //
                // It exists so that NOTHING in the star field can be painted over a world. The
                // near star layer must not be composited after the bodies on the reasoning that
                // it is the "near" layer: near there means nearer than the OTHER star layers, a
                // parallax cue inside the star field. Stars are the most distant objects a sky
                // contains; the closest one is four light years away and every planet in frame is
                // a few light minutes. A pinprick of light sitting on top of a solid planet is the
                // most obviously wrong thing an otherwise plausible sky can do.
                //
                // Partial coverage carries through rather than being rounded to a mask, which is
                // also right: a thin atmosphere or a star's corona scatters light and should wash
                // stars out in proportion to how thick it is, not switch them off at an edge.
                float bodyCover = 0.0;

                // [loop] on every body loop: the trip count comes from a uniform, so this states
                // outright what the compiler would otherwise have to prove, and keeps six inlined
                // copies of Sphere out of the program. It was originally added against the NVIDIA
                // GL miscompile that flattens every surface; that is now prevented structurally by
                // RotX/RotY instead, since [loop] only ever hid it on some compilers.
                [loop] for (int i = 0; i < pc; i++)
                {
                    float4 A = _PlanetA[i], B = _PlanetB[i], C = _PlanetC[i], D = _PlanetD[i];
                    if (A.z <= 0.0) continue;

                    float ppu = max(C.x, 0.01);
                    // Cell and snapped position in ONE step: re-multiplying sp by ppu to derive
                    // the cell (a divide→multiply round trip) produces 3.9999999 and the dither
                    // pattern slides at block borders.
                    float2 lp = LayerPos(world, cam, A.w);

                    // ── WRAPPING ──────────────────────────────────────────────────────────────
                    // The star fields, gas and dust are evaluated from position, so they are
                    // endless by construction. The bodies are not: there are six of them at fixed
                    // places, so any sustained motion — the drift, or simply a game whose camera
                    // travels — eventually carries every one of them off screen and leaves an
                    // empty sky that never refills.
                    //
                    // Until bodies are generated from position too, they repeat on a tile: each
                    // one is asked "where am I relative to the nearest copy of this body?". The
                    // tile is sized from the placement area, so at most one copy is ever in view
                    // and the repeat takes minutes to come around at drift speeds.
                    if (_BodyTile.z > 0.5)
                    {
                        float2 rel = lp - A.xy;
                        rel -= _BodyTile.xy * round(rel / _BodyTile.xy);
                        lp = A.xy + rel;
                    }

                    float2 cell = floor(lp * ppu);
                    float2 sp = _PixelArt > 0.5 ? cell / ppu : lp;
                    int kind = (int)B.w;

                    // ── ORBIT ─────────────────────────────────────────────────────────────────
                    // A body on an orbit is placed here rather than by the composer, because its
                    // position is a function of time and the composer runs once per rebuild. The
                    // ellipse is the circle foreshortened vertically — an orbital plane seen from
                    // anywhere but directly above. Everything downstream (lighting direction,
                    // rings, moons) reads this animated centre, so the whole body travels as one.
                    float4 E = _PlanetE[i];
                    float2 pc = A.xy;
                    if (E.x > 0.0)
                    {
                        float oa = E.y + Now() * E.z;
                        pc += float2(cos(oa) * E.x, sin(oa) * E.x * E.w);
                    }

                    // ── AND THE DISC'S POSITION, NOT JUST ITS SURFACE ─────────────────────────
                    // The same rule as SnapSpin, on the other half of the problem. `sp` is
                    // snapped to the body's grid but the CENTRE was not, and everything the body
                    // draws is a function of `sp - centre`. A centre sitting a third of a texel
                    // off the lattice re-quantizes every texel of the body, exactly as a
                    // sub-texel rotation does.
                    //
                    // Two separate things were moving it. An orbiting body travels its ellipse
                    // continuously, which on the orbital archetype means every planet in frame
                    // boils at once. And more quietly, LayerPos quantizes the parallax shift to
                    // the SCREEN grid (_PPU) while a body samples on its own coarser grid
                    // (_PPU / block) — so a screen-pixel of drift moves the body's lattice by a
                    // FRACTION of a body texel, and even a fixed planet crawled whenever the sky
                    // drifted or the camera panned.
                    //
                    // Snapping the centre to the same lattice `sp` lives on makes `sp - centre`
                    // land on exact texel multiples always: the body's interior rasterizes
                    // identically wherever it is. Done here, before the ring and the moons read
                    // it, so the whole system holds together as one piece.
                    //
                    // The TRUE centre is kept and handed to Sphere alongside it: the interior
                    // rides the snapped grid, the silhouette rides the real position. Snapping
                    // both turned every planet into a perfect symmetric pixel circle that never
                    // changed at all — see the two-centres note in Sphere.
                    float2 pcTrue = pc;
                    pc = Snap(pc, ppu);

                    // cloudAmount < 0 marks the featured close star: a point of light with
                    // spikes, not a sphere. A sphere at this size reads as a moon.
                    if (kind == KIND_STAR && D.x < -0.5)
                    {
                        float4 ps = PointStar(sp, pcTrue, A.z, _PlanetCol[i].rgb);
                        col = col * (1.0 - ps.a) + ps.rgb;
                        bodyCover = lerp(bodyCover, 1.0, ps.a);
                        continue;
                    }

                    // 1) Ring arc BEHIND the planet
                    float behind;
                    float4 ring = Ring(sp, pc, A.z, B.z, C.z, C.w, B.x, _PlanetCol3[i].rgb, behind);
                    if (ring.a > 0.0 && behind > 0.5)
                    {
                        col = lerp(col, ring.rgb, ring.a);
                        bodyCover = lerp(bodyCover, 1.0, ring.a);
                    }

                    // 2) Moons, both groups, in ONE loop.
                    //
                    // Calling MoonSphere from two places, with the planet drawn between them, is
                    // the obvious shape and an expensive one: HLSL inlines at every call site, so
                    // writing the loop twice puts a third of the program's crater bodies in it for
                    // nothing. Both groups accumulate here as premultiplied colour and composite
                    // at the two points they belong to; within a group the draw order is
                    // unchanged, so the result is identical.
                    float4 behindAcc = 0.0, frontAcc = 0.0;
                    [loop] for (int m = 0; m < mc; m++)
                    {
                        if ((int)_MoonA[m].x != i) continue;
                        float ang = _MoonA[m].z + Now() * _MoonA[m].w;
                        float mi = _MoonB[m].y;
                        float3 mp = float3(cos(ang) * _MoonA[m].y,
                                           sin(ang) * _MoonA[m].y * sin(mi),
                                           sin(ang) * _MoonA[m].y * cos(mi));
                        bool inFront = mp.z >= 0.0;      // +z is toward the viewer

                        // A moon behind the planet is hidden — but only well inside the disc, or
                        // the planet's own soft edge leaves a gap the background shows through.
                        if (!inFront && length((sp - pc) / A.z) < 0.93) continue;

                        float4 mo = MoonSphere(lp, pc + mp.xy, _MoonB[m].x, max(_MoonB[m].w, 0.01),
                                               _MoonB[m].z, D.w,
                                               _MoonCol[m].rgb, _MoonCol[m].rgb * 0.45,
                                               _MoonCol[m].rgb * 1.3, pixelWorld);
                        if (mo.a <= 0.0) continue;

                        // "over" on premultiplied colour: the later moon covers the earlier one,
                        // exactly as the sequential composites did.
                        if (inFront) frontAcc  = float4(mo.rgb + frontAcc.rgb  * (1.0 - mo.a),
                                                        mo.a   + frontAcc.a   * (1.0 - mo.a));
                        else         behindAcc = float4(mo.rgb + behindAcc.rgb * (1.0 - mo.a),
                                                        mo.a   + behindAcc.a  * (1.0 - mo.a));
                    }

                    if (behindAcc.a > 0.0)
                    {
                        col = col * (1.0 - behindAcc.a) + behindAcc.rgb;
                        bodyCover = lerp(bodyCover, 1.0, behindAcc.a);
                    }

                    // 3) The planet itself
                    float4 pl = Sphere(sp, pc, pcTrue, A.z, cell, ppu, kind, B.x, B.y, B.z, D.z,
                                       D.x, D.y, C.y, C.z, C.w, D.w,
                                       _PlanetCol[i].rgb, _PlanetCol2[i].rgb, _PlanetCol3[i].rgb, pixelWorld);
                    col = col * (1.0 - pl.a) + pl.rgb;       // premultiplied (see Sphere)
                    bodyCover = lerp(bodyCover, 1.0, pl.a);

                    // 4) Ring arc IN FRONT of the planet
                    if (ring.a > 0.0 && behind < 0.5)
                    {
                        col = lerp(col, ring.rgb, ring.a);
                        bodyCover = lerp(bodyCover, 1.0, ring.a);
                    }

                    // 5) Moons in front
                    if (frontAcc.a > 0.0)
                    {
                        col = col * (1.0 - frontAcc.a) + frontAcc.rgb;
                        bodyCover = lerp(bodyCover, 1.0, frontAcc.a);
                    }
                }

                // ── Asteroid belt (in front of the planets: rubble drifting between the viewer
                //    and the distant giants is exactly the "flying through a field" feel)
                float4 belt = Belt(world, cam, pixelWorld);
                if (belt.a > 0.0)
                {
                    col = lerp(col, belt.rgb, belt.a);
                    // Rubble is foreground matter too — it occludes the star field like a world.
                    bodyCover = lerp(bodyCover, 1.0, belt.a);
                }

                // ── Near stars. Drawn last because their parallax makes them the liveliest layer
                //    under camera motion, but MASKED by whatever solid matter is in this pixel —
                //    a star behind a planet is behind it, however fast it slides.
                float starMask = saturate(1.0 - bodyCover);

                // Skipped outright where a body covers the pixel, rather than computed and then
                // multiplied by zero. The branch is divergent in principle and extremely coherent
                // in practice — a planet's interior is one large contiguous region — so it saves
                // the star lattice, an atan2, two pows and three exps on every body pixel.
                if (starMask > 0.001)
                {
                col += StarLayer(LayerPos(world, cam, _NearStarParallax),
                                 _NearStarDensity, 11.9, _TwinkleAmount, _NearStarColor.rgb, 1.0)
                     * _NearStarBright * starMask;

                // ── The few bright ones: cores, halos and spikes. Also occluded — a diffraction
                //    spike is made in the telescope, but the light that makes it still has to
                //    reach the telescope, and a planet in the way stops it.
                col += BrightStars(LayerPos(world, cam, _NearStarParallax), _NearStarColor.rgb) * starMask;
                }

                // ── Pixel mode's version of a bright star: a plus. Four neighbours around a
                //    core is the oldest trick in the book for saying "this one is brighter than
                //    one pixel can show" without inventing a glow that the palette cannot hold.
                if (_PixelArt > 0.5 && _BrightStarAmount > 0.001)
                {
                    float2 lp = LayerPos(world, cam, _NearStarParallax);

                    // Two grids, and they must not be confused. WHICH cells hold a bright star is
                    // decided on the same COARSE lattice smooth mode uses — testing the fine
                    // pixel grid instead would make one pixel in twenty a bright star and bury
                    // the sky. The plus is then drawn on the FINE grid, because its arms are
                    // single pixels either side of the core.
                    // Two coordinates, and they must not be confused either. `cell` is a PLACE on
                    // the lattice; `hc` is the same cell shifted into this sky's own patch of the
                    // hash field. Converting back to a position has to use the place. Add the
                    // seed to both and the star's pixel lands roughly _StarSeed/g × _PPU cells
                    // away — about seventy thousand of them — so the plus is never within one
                    // pixel of anything and pixel mode has no bright stars at all. BrightStars
                    // keeps the two apart for the same reason.
                    float g = max(_PPU * 0.055 * _StarGridScale, 0.05);
                    float2 cell = floor(lp * g);
                    float2 hc = cell + _StarSeed.xy;
                    if (hash21(hc + 91.3) < _BrightStarAmount)
                    {
                        float2 jit = 0.3 + 0.4 * float2(hash21(hc + 3.17), hash21(hc + 8.93));
                        float2 starCell = floor((cell + jit) / g * _PPU);
                        float2 delta = abs(floor(lp * _PPU) - starCell);
                        float manhattan = delta.x + delta.y;

                        float arm = manhattan < 0.5 ? 1.0 : (manhattan < 1.5 ? 0.4 : 0.0);
                        col += _NearStarColor.rgb * arm * _NearStarBright * 0.9 * starMask;
                    }
                }

                // ── Shooting stars (the nearest, liveliest layer — drawn over everything)
                col += Comets(world, cam);

                // ── Anti-banding dither (smooth mode) ─────────────────────────────────────────
                // Everything here is built from gradients that stretch across hundreds of
                // pixels, and 8-bit output can only hold 256 steps: without a sub-step of noise
                // those gradients quantize into visible stripes — the flaw that reads as "the
                // colour transition looks wrong" even when nothing about the colours is wrong.
                // One quantization step of noise removes them completely and costs nothing.
                if (_PixelArt < 0.5)
                    col += (hash21(floor(IN.positionHCS.xy)) - 0.5) * (1.0 / 255.0);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
