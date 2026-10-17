using System.Collections.Generic;
using UnityEngine;

namespace BirichStudio.ProceduralSpace
{
    /// <summary>
    /// Procedural space background renderer. Drop it on a quad (or let it build its own mesh),
    /// assign a <see cref="SpaceBackgroundProfile"/> and every seed becomes a different sky:
    /// planets, moons, rings, nebulae and parallax star layers — all generated in one shader,
    /// with zero texture assets.
    ///
    /// The component is deliberately thin: <see cref="SkyComposer"/> decides WHAT is in the sky,
    /// this class only owns the seed, the renderer plumbing and the material upload (via
    /// MaterialPropertyBlock, so any number of backgrounds share one material asset).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    [AddComponentMenu("Birich Studio/Procedural Space/Space Background")]
    public class SpaceBackground : MonoBehaviour
    {
        public const string ShaderName = "Birich Studio/ProceduralSpace";

        [Tooltip("Style preset driving the generation. If empty, built-in defaults are used — " +
                 "assign or create a profile (Create > Birich Studio > Procedural Space) to " +
                 "customize the look.")]
        [SerializeField] private SpaceBackgroundProfile profile;

        [Header("Seed")]
        [Tooltip("The sky's identity: the same seed with the same profile always composes the " +
                 "same sky. Change it (or call RandomizeSeed) for a new one.")]
        [SerializeField] private int seed = 20260813;

        [Tooltip("On: a fresh random seed is drawn every time play mode starts — every run gets " +
                 "its own sky. Off: the seed above is used as-is.")]
        [SerializeField] private bool randomizeSeedOnPlay = false;

        [Header("Archetype")]
        [Tooltip("On: the archetype is drawn from the seed using the profile's weights. " +
                 "Off: the archetype below is forced (useful for testing and art direction).")]
        [SerializeField] private bool randomArchetype = true;
        [SerializeField] private SkyArchetype forcedArchetype = SkyArchetype.PlanetOrbit;

        [Header("Rendering")]
        [Tooltip("Sorting layer the backdrop is drawn on. Leave empty to keep whatever the " +
                 "renderer already uses. Projects that set up their own sorting layers need this: " +
                 "a large negative order cannot put the sky behind anything on a LOWER layer, " +
                 "because layer is compared before order.")]
        [SerializeField] private string sortingLayerName = "";

        [Tooltip("Sorting order of the backdrop. Keep strongly negative so every sprite in the " +
                 "scene draws in front of it (URP 2D Renderer sorts by this, not by queue).")]
        [SerializeField] private int sortingOrder = -1000;

        [Tooltip("Optional explicit material. Leave empty to auto-create one from the " +
                 "Birich Studio/ProceduralSpace shader at runtime.")]
        [SerializeField] private Material material;

        private static readonly int PlanetAID = Shader.PropertyToID("_PlanetA");
        private static readonly int PlanetBID = Shader.PropertyToID("_PlanetB");
        private static readonly int PlanetCID = Shader.PropertyToID("_PlanetC");
        private static readonly int PlanetDID = Shader.PropertyToID("_PlanetD");
        private static readonly int PlanetEID = Shader.PropertyToID("_PlanetE");
        private static readonly int BodyTileID = Shader.PropertyToID("_BodyTile");
        private static readonly int PlanetColID = Shader.PropertyToID("_PlanetCol");
        private static readonly int PlanetCol2ID = Shader.PropertyToID("_PlanetCol2");
        private static readonly int PlanetCol3ID = Shader.PropertyToID("_PlanetCol3");
        private static readonly int PlanetCountID = Shader.PropertyToID("_PlanetCount");
        private static readonly int MoonAID = Shader.PropertyToID("_MoonA");
        private static readonly int MoonBID = Shader.PropertyToID("_MoonB");
        private static readonly int MoonColID = Shader.PropertyToID("_MoonCol");
        private static readonly int MoonCountID = Shader.PropertyToID("_MoonCount");

        private static readonly int PixelArtID = Shader.PropertyToID("_PixelArt");
        private static readonly int CartoonID = Shader.PropertyToID("_Cartoon");
        private static readonly int PPUID = Shader.PropertyToID("_PPU");
        private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseColor2ID = Shader.PropertyToID("_BaseColor2");
        private static readonly int GradientDirID = Shader.PropertyToID("_GradientDir");
        private static readonly int GradientScaleID = Shader.PropertyToID("_GradientScale");
        private static readonly int NebAmountID = Shader.PropertyToID("_NebAmount");
        private static readonly int NebContrastID = Shader.PropertyToID("_NebContrast");
        private static readonly int NebStyleID = Shader.PropertyToID("_NebStyle");
        private static readonly int NebColAID = Shader.PropertyToID("_NebColA");
        private static readonly int NebColBID = Shader.PropertyToID("_NebColB");
        private static readonly int NebColCID = Shader.PropertyToID("_NebColC");
        private static readonly int NebSeedID = Shader.PropertyToID("_NebSeed");
        private static readonly int NebScaleID = Shader.PropertyToID("_NebScale");
        private static readonly int StarGridID = Shader.PropertyToID("_StarGridScale");
        private static readonly int StarSeedID = Shader.PropertyToID("_StarSeed");
        private static readonly int FarDensityID = Shader.PropertyToID("_FarStarDensity");
        private static readonly int MidDensityID = Shader.PropertyToID("_MidStarDensity");
        private static readonly int NearDensityID = Shader.PropertyToID("_NearStarDensity");
        private static readonly int NebParallaxID = Shader.PropertyToID("_NebParallax");
        private static readonly int FarParallaxID = Shader.PropertyToID("_FarStarParallax");
        private static readonly int MidParallaxID = Shader.PropertyToID("_MidStarParallax");
        private static readonly int NearParallaxID = Shader.PropertyToID("_NearStarParallax");
        private static readonly int HueVarietyID = Shader.PropertyToID("_StarHueVariety");
        private static readonly int TwinkleAmountID = Shader.PropertyToID("_TwinkleAmount");
        private static readonly int BlinkFractionID = Shader.PropertyToID("_BlinkFraction");
        private static readonly int LightDirID = Shader.PropertyToID("_PlanetLightDir");
        private static readonly int SunPosID = Shader.PropertyToID("_SunPos");
        private static readonly int AtmoIntensityID = Shader.PropertyToID("_AtmoIntensity");
        private static readonly int StarPulseID = Shader.PropertyToID("_StarPulse");
        private static readonly int PixelHueShiftID = Shader.PropertyToID("_PixelHueShift");
        private static readonly int BrightStarID = Shader.PropertyToID("_BrightStarAmount");
        private static readonly int StarSpikesID = Shader.PropertyToID("_StarSpikes");
        private static readonly int StarSpikeCountID = Shader.PropertyToID("_StarSpikeCount");
        private static readonly int DustAmountID = Shader.PropertyToID("_DustAmount");
        private static readonly int FarGalaxyID = Shader.PropertyToID("_FarGalaxyAmount");
        private static readonly int LightStepsID = Shader.PropertyToID("_PlanetLightSteps");
        private static readonly int ShadowID = Shader.PropertyToID("_PlanetShadow");
        private static readonly int AmbientID = Shader.PropertyToID("_PlanetAmbient");
        private static readonly int ColorStepsID = Shader.PropertyToID("_PlanetColorSteps");
        private static readonly int DitherID = Shader.PropertyToID("_DitherAmount");

        private static readonly int GalaxyAID = Shader.PropertyToID("_GalaxyA");
        private static readonly int GalaxyBID = Shader.PropertyToID("_GalaxyB");
        private static readonly int GalaxyCID = Shader.PropertyToID("_GalaxyC");
        private static readonly int GalaxyColAID = Shader.PropertyToID("_GalaxyColA");
        private static readonly int GalaxyColBID = Shader.PropertyToID("_GalaxyColB");
        private static readonly int BeltAID = Shader.PropertyToID("_BeltA");
        private static readonly int BeltBID = Shader.PropertyToID("_BeltB");
        private static readonly int BeltCID = Shader.PropertyToID("_BeltC");
        private static readonly int BeltColID = Shader.PropertyToID("_BeltCol");
        private static readonly int CometAID = Shader.PropertyToID("_CometA");
        private static readonly int CometBID = Shader.PropertyToID("_CometB");
        private static readonly int CometColID = Shader.PropertyToID("_CometCol");
        private static readonly int DriftID = Shader.PropertyToID("_DriftVec");
        private static readonly int TimeOffsetID = Shader.PropertyToID("_TimeOffset");

        // Seed history, serialized so it survives play-mode and script reloads. Exploring seeds
        // is the main way anyone uses this asset, and a roll past a sky you liked is otherwise
        // unrecoverable — which in practice teaches people to stop rolling.
        [SerializeField, HideInInspector] private List<int> seedHistory = new List<int>();
        [SerializeField, HideInInspector] private int historyIndex = -1;

        private const int MaxSeedHistory = 32;

        private readonly SkyData _sky = new SkyData();
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _mpb;
        private Material _runtimeMaterial;
        private SpaceBackgroundProfile _defaultProfile;

        /// <summary>The profile in use. Assigning a different one rebuilds immediately.</summary>
        public SpaceBackgroundProfile Profile
        {
            get => profile;
            set { profile = value; Rebuild(); }
        }

        /// <summary>Current seed. Use <see cref="Rebuild(int)"/> to change it with a rebuild.</summary>
        public int Seed => seed;

        /// <summary>The archetype chosen for the current sky (for diagnostics / UI).</summary>
        public SkyArchetype Archetype => _sky.Archetype;

#if UNITY_EDITOR
        /// <summary>
        /// Live instances, so a profile can recompose the backgrounds using it when one of its
        /// settings changes. A registry rather than a scene search: this runs on every drag of
        /// every slider, and scanning the whole hierarchy for that is a cost that grows with the
        /// user's scene rather than with the number of backgrounds in it.
        /// </summary>
        internal static readonly List<SpaceBackground> Live = new List<SpaceBackground>();
#endif

        private void OnEnable()
        {
#if UNITY_EDITOR
            if (!Live.Contains(this)) Live.Add(this);
#endif
            EnsureResources();
            if (Application.isPlaying && randomizeSeedOnPlay)
                seed = System.Environment.TickCount ^ System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
            Rebuild();

            // Only a perspective camera derives its view size from the quad's depth, and only a
            // SpaceCameraFit moves the quad after this point — so the second compose is asked for
            // exactly when it can change the answer.
            SpaceCameraFit fit = GetComponent<SpaceCameraFit>();
            Camera cam = fit != null ? fit.TargetCamera : null;
            _fitPending = fit != null && fit.enabled && cam != null && !cam.orthographic;
        }

#if UNITY_EDITOR
        private void OnDisable() => Live.Remove(this);
#endif

        /// <summary>
        /// Recompose once more after the camera fit has had a frame to place the quad.
        ///
        /// <see cref="SpaceCameraFit"/> writes the transform in LateUpdate, which is after
        /// OnEnable has already run the one and only Rebuild. For a perspective camera the view
        /// size is derived from the distance between quad and camera, so that first compose read
        /// the quad's AUTHORED depth — and nothing recomposed afterwards, leaving the sky
        /// permanently scaled for a position it no longer occupies.
        /// </summary>
        private void LateUpdate()
        {
            if (_fitPending)
            {
                _fitPending = false;
                Rebuild();
            }
        }

        private bool _fitPending;
        private Vector2 _viewSize;
        private bool _viewValid;

        private void OnDestroy()
        {
            Discard(_runtimeMaterial);
            // A HideAndDontSave object survives scene changes AND domain reloads — which is the
            // point of the flag and also why forgetting to destroy one leaks for the whole
            // session. Every background that ever ran without a profile assigned was leaving a
            // hidden ScriptableObject behind on each script recompile.
            Discard(_defaultProfile);
            _runtimeMaterial = null;
            _defaultProfile = null;
        }

        private static void Discard(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

#if UNITY_EDITOR
        private void OnValidate() { if (isActiveAndEnabled) Rebuild(); }
#endif

        /// <summary>Recompose the sky with the current seed.</summary>
        public void Rebuild()
        {
            // Always re-ensure: editor lifecycle (domain reload, OnValidate ordering, prefab
            // isolation) can leave the renderer reference alive while the property block is
            // gone — never trust cached plumbing here. EnsureResources is idempotent and cheap
            // at rebuild frequency.
            _viewValid = false;      // the camera may have moved or resized since the last one
            EnsureResources();
            if (_renderer == null) return;

            // The seed in the inspector counts as history too. Recording only the ones that
            // arrive through Rebuild(int) left the back arrow dead on a freshly placed
            // background, and made the sky it shipped with the one sky you could not step
            // back to.
            if (seedHistory == null || seedHistory.Count == 0) RecordSeed(seed);

            SpaceBackgroundProfile p = ActiveProfile();
            SkyComposer.Compose(p, seed, randomArchetype ? (SkyArchetype?)null : forcedArchetype,
                                _sky, ViewScale(p));
            Upload(p);
        }

        /// <summary>Recompose the sky with a specific seed (also stores it).</summary>
        public void Rebuild(int newSeed)
        {
            seed = newSeed;
            RecordSeed(newSeed);
            Rebuild();
        }

        /// <summary>True if there is a previous (-1) or next (+1) seed to step to.</summary>
        public bool CanStepSeed(int direction) => direction < 0
            ? historyIndex > 0
            : historyIndex >= 0 && historyIndex < seedHistory.Count - 1;

        /// <summary>Walk the seed history without adding to it.</summary>
        public void StepSeedHistory(int direction)
        {
            if (!CanStepSeed(direction)) return;
            historyIndex += direction;
            seed = seedHistory[historyIndex];
            Rebuild();
        }

        private void RecordSeed(int value)
        {
            seedHistory ??= new List<int>();
            if (historyIndex >= 0 && historyIndex < seedHistory.Count && seedHistory[historyIndex] == value)
                return;

            // Stepping back and then rolling again drops the abandoned branch, the way undo does.
            if (historyIndex < seedHistory.Count - 1)
                seedHistory.RemoveRange(historyIndex + 1, seedHistory.Count - historyIndex - 1);

            seedHistory.Add(value);
            if (seedHistory.Count > MaxSeedHistory) seedHistory.RemoveAt(0);
            historyIndex = seedHistory.Count - 1;
        }

        /// <summary>Draw a fresh random seed and recompose.</summary>
        public void RandomizeSeed() => Rebuild(Random.Range(int.MinValue, int.MaxValue));

        // ── Snapshots ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Freeze this background's current sky into a snapshot: the seed, the archetype mode, and
        /// a complete copy of the profile's settings.
        /// </summary>
        public void CaptureSnapshot(SkySnapshot snapshot)
        {
            if (snapshot == null) return;
            snapshot.Capture(ActiveProfile(), seed, randomArchetype, forcedArchetype);
        }

        /// <summary>
        /// Restore a snapshot and recompose.
        ///
        /// The settings are written into <paramref name="into"/>, or into this background's own
        /// profile when that is null. Restoring is a write: whatever profile receives it is
        /// changed, and every background sharing that profile changes with it — the same as
        /// editing the settings by hand, which is what this is a shortcut for.
        ///
        /// Returns false if the snapshot is empty or there is no profile to write into.
        /// </summary>
        public bool LoadSnapshot(SkySnapshot snapshot, SpaceBackgroundProfile into = null)
        {
            if (snapshot == null) return false;

            SpaceBackgroundProfile target = into != null ? into : profile;
            if (target == null || !snapshot.ApplyTo(target)) return false;

            seed = snapshot.seed;
            randomArchetype = snapshot.randomArchetype;
            forcedArchetype = snapshot.forcedArchetype;
            RecordSeed(seed);
            Rebuild();
            return true;
        }

        /// <summary>Force a specific archetype (null = draw from the profile's weights) and recompose.</summary>
        public void SetArchetype(SkyArchetype? forced)
        {
            randomArchetype = !forced.HasValue;
            if (forced.HasValue) forcedArchetype = forced.Value;
            Rebuild();
        }

        /// <summary>
        /// How much larger the camera's view is than the profile's reference height.
        ///
        /// The sky is composed in world units, so a camera that sees less than the profile
        /// assumes gets a composition built for a larger frame: planets too big for the view,
        /// bodies placed outside it. Scaling by the ratio is what makes one set of settings look
        /// the same through any camera.
        /// </summary>
        private float ViewScale(SpaceBackgroundProfile p)
        {
            if (!p.autoFitToCamera || p.referenceViewHeight <= 0.01f) return 1f;

            float viewHeight = ViewSize().y;
            if (viewHeight <= 0f) return 1f;

            return Mathf.Clamp(viewHeight / p.referenceViewHeight, 0.05f, 20f);
        }

        /// <summary>
        /// What the camera can actually see, in world units. Zero when there is no camera to ask —
        /// callers treat that as "no information" rather than as an empty view.
        /// </summary>
        private Vector2 ViewSize()
        {
            // Resolved once per rebuild and cached: ViewScale and Upload between them asked for
            // this three times, and each ask was a GetComponent plus a Camera.main lookup.
            if (_viewValid) return _viewSize;

            SpaceCameraFit fit = GetComponent<SpaceCameraFit>();
            Camera cam = fit != null ? fit.TargetCamera : Camera.main;
            if (cam == null) return Vector2.zero;

            float h = cam.orthographic
                ? cam.orthographicSize * 2f
                : 2f * Mathf.Abs(transform.position.z - cam.transform.position.z)
                      * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);

            _viewSize = new Vector2(h * Mathf.Max(0.01f, cam.aspect), h);
            _viewValid = true;
            return _viewSize;
        }

        // ── Plumbing ───────────────────────────────────────────────────────────────────────────

        private SpaceBackgroundProfile ActiveProfile()
        {
            if (profile != null) return profile;
            if (_defaultProfile == null)
            {
                // No profile assigned: fall back to the class defaults so the component works
                // out of the box. Hidden so it never shows up as a savable asset.
                _defaultProfile = ScriptableObject.CreateInstance<SpaceBackgroundProfile>();
                _defaultProfile.hideFlags = HideFlags.HideAndDontSave;
            }
            return _defaultProfile;
        }

        private void EnsureResources()
        {
            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null) return;
            _mpb ??= new MaterialPropertyBlock();

            var filter = GetComponent<MeshFilter>();
            if (filter.sharedMesh == null)
                filter.sharedMesh = SharedQuad();

            if (_renderer.sharedMaterial == null)
            {
                if (material != null)
                {
                    _renderer.sharedMaterial = material;
                }
                else
                {
                    Shader shader = Shader.Find(ShaderName);
                    if (shader == null)
                    {
                        Debug.LogError($"[ProceduralSpace] Shader \"{ShaderName}\" not found. " +
                                       "Assign the ProceduralSpace material manually.", this);
                        return;
                    }
                    if (_runtimeMaterial == null)
                        _runtimeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    _renderer.sharedMaterial = _runtimeMaterial;
                }
            }

            _renderer.sortingOrder = sortingOrder;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        }

        /// <summary>
        /// The unit quad, built once and shared.
        ///
        /// There is nothing per-instance about a unit quad — the transform does all the work —
        /// so one serves the whole session. Building it per instance costs a mesh per background
        /// that no one owns: the shipped prefab stores no mesh, so each object makes its own and
        /// loses the reference on every domain reload.
        /// </summary>
        private static Mesh _sharedQuad;

        private static Mesh SharedQuad()
        {
            if (_sharedQuad == null)
            {
                _sharedQuad = BuildQuad();
                _sharedQuad.hideFlags = HideFlags.HideAndDontSave;
            }
            return _sharedQuad;
        }

        private static Mesh BuildQuad()
        {
            // A unit quad built in code: no dependency on editor-only built-in resources, works
            // in any pipeline, scales via the transform.
            var mesh = new Mesh
            {
                name = "ProceduralSpace Quad",
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f),  new Vector3(0.5f, 0.5f, 0f),
                },
                triangles = new[] { 0, 2, 1, 2, 3, 1 },
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Upload(SpaceBackgroundProfile p)
        {
            _mpb ??= new MaterialPropertyBlock();   // belt-and-braces: see Rebuild
            _renderer.GetPropertyBlock(_mpb);

            _mpb.SetVectorArray(PlanetAID, _sky.PlanetA);
            _mpb.SetVectorArray(PlanetBID, _sky.PlanetB);
            _mpb.SetVectorArray(PlanetCID, _sky.PlanetC);
            _mpb.SetVectorArray(PlanetDID, _sky.PlanetD);
            _mpb.SetVectorArray(PlanetEID, _sky.PlanetE);

            float fit = ViewScale(p);

            // Tile sized from the placement area: bodies are scattered over ±spread, so the
            // repeat has to clear twice that or a body would overlap its own copy. The composer
            // scatters over spread × fit, so the tile has to take the same scaling. Without it,
            // a large view scale leaves the tile narrower than the area it is meant to contain
            // and bodies collide with their own copies.
            float tileX = Mathf.Max(1f, p.spread.x * 2f * fit * p.wrapSpacing);
            float tileY = Mathf.Max(1f, p.spread.y * 2f * fit * p.wrapSpacing);

            // …and never smaller than the frame. Wrapping exists so that at most ONE copy of a
            // body is ever in view; a tile shorter than the camera's own width guarantees the
            // opposite — the same planet three times across the screen, which reads as a broken
            // renderer rather than as a small placement area. The floor costs nothing when the
            // settings are sane and is the difference between a strange-looking sky and a sky
            // that looks wrong.
            Vector2 view = ViewSize();
            if (view.x > 0f)
            {
                tileX = Mathf.Max(tileX, view.x * 1.15f);
                tileY = Mathf.Max(tileY, view.y * 1.15f);
            }

            _mpb.SetVector(BodyTileID, new Vector4(
                tileX, tileY, p.wrapBodies ? 1f : 0f, 0f));
            _mpb.SetVectorArray(PlanetColID, _sky.PlanetCol);
            _mpb.SetVectorArray(PlanetCol2ID, _sky.PlanetCol2);
            _mpb.SetVectorArray(PlanetCol3ID, _sky.PlanetCol3);
            _mpb.SetFloat(PlanetCountID, _sky.PlanetCount);

            _mpb.SetVectorArray(MoonAID, _sky.MoonA);
            _mpb.SetVectorArray(MoonBID, _sky.MoonB);
            _mpb.SetVectorArray(MoonColID, _sky.MoonCol);
            _mpb.SetFloat(MoonCountID, _sky.MoonCount);

            _mpb.SetFloat(NebAmountID, _sky.NebAmount);
            _mpb.SetFloat(NebStyleID, (float)_sky.NebStyle);
            _mpb.SetFloat(NebContrastID, _sky.NebContrast);
            _mpb.SetColor(NebColAID, _sky.NebColA);
            _mpb.SetColor(NebColBID, _sky.NebColB);
            _mpb.SetColor(NebColCID, _sky.NebColC);
            _mpb.SetVector(NebSeedID, _sky.NebSeed);
            _mpb.SetFloat(NebScaleID, 0.035f / fit);
            _mpb.SetFloat(StarGridID, 1f / fit);
            _mpb.SetVector(StarSeedID, _sky.StarSeed);
            _mpb.SetFloat(FarDensityID, _sky.FarStarDensity);
            _mpb.SetFloat(MidDensityID, _sky.MidStarDensity);
            _mpb.SetFloat(NearDensityID, _sky.NearStarDensity);
            // The archetype's hue spread times the profile's master — a star cluster stays more
            // colourful than deep space, and the setting still reaches it. Uploading the
            // archetype's value alone leaves the profile's control wired to nothing: a slider that
            // draws, saves a number, and changes no pixel on screen.
            _mpb.SetFloat(HueVarietyID, Mathf.Clamp01(_sky.HueVariety * p.starHueVariety));

            // The parallax ladder: nebula deepest, then far/mid/near stars. Bodies interpolate
            // their own band (parallaxSmall..parallaxLarge) inside the composer.
            // parallaxStrength scales the background layers here and the body band inside the
            // composer, so one control moves the whole ladder without disturbing its ratios.
            _mpb.SetFloat(NebParallaxID, p.nebulaParallax * p.parallaxStrength);
            _mpb.SetFloat(FarParallaxID, p.farStarParallax * p.parallaxStrength);
            _mpb.SetFloat(MidParallaxID, p.midStarParallax * p.parallaxStrength);
            _mpb.SetFloat(NearParallaxID, p.nearStarParallax * p.parallaxStrength);

            _mpb.SetVector(GalaxyAID, _sky.GalaxyA);
            _mpb.SetVector(GalaxyBID, _sky.GalaxyB);
            _mpb.SetVector(GalaxyCID, _sky.GalaxyC);
            _mpb.SetColor(GalaxyColAID, _sky.GalaxyColA);
            _mpb.SetColor(GalaxyColBID, _sky.GalaxyColB);

            _mpb.SetVector(BeltAID, _sky.BeltA);
            _mpb.SetVector(BeltBID, _sky.BeltB);
            _mpb.SetVector(BeltCID, _sky.BeltC);
            _mpb.SetColor(BeltColID, _sky.BeltCol);

            // Shooting stars are a profile-level feature (not archetype-composed): the seed only
            // desynchronizes their timing between different backgrounds.
            _mpb.SetVector(CometAID, new Vector4(
                p.shootingStars ? Mathf.Max(1f, p.shootingStarPeriod) : 0f,
                Mathf.Max(1f, p.shootingStarSpeed),
                Mathf.Max(0.1f, p.shootingStarTail),
                p.shootingStarBrightness));
            _mpb.SetVector(CometBID, new Vector4(0.3f, Mathf.Abs(seed % 97), 0.22f, p.shootingStarTracks));
            _mpb.SetColor(CometColID, p.shootingStarColor);

            // Per background, so two skies sharing one material do not animate in lockstep.
            // The property existed and was read by the shader's Now(); nothing ever set it, so
            // every background in a scene rotated its planets and twinkled its stars on exactly
            // the same clock.
            _mpb.SetFloat(TimeOffsetID, Mathf.Abs(seed % 9973) * 0.37f);
            _mpb.SetVector(DriftID, p.driftVelocity);
            _mpb.SetFloat(PixelArtID, p.pixelArt ? 1f : 0f);
            _mpb.SetFloat(CartoonID, p.cartoon ? 1f : 0f);
            _mpb.SetFloat(PPUID, Mathf.Max(1f, p.pixelsPerUnit));
            _mpb.SetColor(BaseColorID, p.baseColor);
            _mpb.SetColor(BaseColor2ID, p.baseColor2);
            _mpb.SetVector(GradientDirID, p.gradientDirection);
            _mpb.SetFloat(GradientScaleID, p.gradientScale);
            _mpb.SetFloat(TwinkleAmountID, p.twinkleAmount);
            _mpb.SetFloat(BlinkFractionID, p.blinkFraction);
            _mpb.SetVector(LightDirID, _sky.LightDir);
            _mpb.SetVector(SunPosID, _sky.SunPos);
            _mpb.SetFloat(AtmoIntensityID, p.atmosphereGlow);
            _mpb.SetFloat(StarPulseID, p.starPulse);
            _mpb.SetFloat(PixelHueShiftID, p.pixelHueShift);
            _mpb.SetFloat(BrightStarID, p.brightStarDensity);
            _mpb.SetFloat(StarSpikesID, p.starSpikes);
            _mpb.SetFloat(StarSpikeCountID, p.starSpikeCount);
            _mpb.SetFloat(DustAmountID, p.dustAmount);
            _mpb.SetFloat(FarGalaxyID, p.distantGalaxyDensity);
            _mpb.SetFloat(LightStepsID, p.terminatorSteps);
            _mpb.SetFloat(ShadowID, p.shadowDarkness);
            _mpb.SetFloat(AmbientID, p.planetAmbient);
            _mpb.SetFloat(ColorStepsID, p.surfaceColorSteps);
            _mpb.SetFloat(DitherID, p.ditherAmount);

            _renderer.SetPropertyBlock(_mpb);
            _renderer.sortingOrder = sortingOrder;
            if (!string.IsNullOrEmpty(sortingLayerName))
                _renderer.sortingLayerName = sortingLayerName;
        }
    }
}
