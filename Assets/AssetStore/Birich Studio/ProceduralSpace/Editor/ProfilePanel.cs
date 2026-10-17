using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BirichStudio.ProceduralSpace.Editor
{
    /// <summary>
    /// Draws a <see cref="SpaceBackgroundProfile"/> as a tabbed panel, used both by the profile's
    /// own inspector and inline inside the background component's.
    ///
    /// The profile has sixty-seven fields. Shown as one flat list — which is what Unity does by
    /// default — the first thing a new user meets is something like "surface block range", and the
    /// honest reaction to that is to close the asset and go back to picking presets.
    ///
    /// <b>The layout is a table, not code.</b> Every field appears exactly once in <see cref="Rows"/>
    /// with its tab, its group, its label and when it applies. Drawing, searching and per-tab reset
    /// are then three readings of that one description instead of three hand-written listings that
    /// have to be kept in agreement — which they would not be. It also makes the panel checkable:
    /// anything serialized and missing from the table is reported rather than silently invisible.
    /// </summary>
    internal static class ProfilePanel
    {
        private const string TabKey = "BirichStudio.ProceduralSpace.Tab";
        private const string FoldKey = "BirichStudio.ProceduralSpace.Fold.";

        private enum Tab { Style, Sky, Motion, DeepSky, Light }

        private static readonly string[] TabNames = { "Style", "Sky", "Motion", "Deep Sky", "Light" };

        private static readonly string[] ArchetypeNames = System.Enum.GetNames(typeof(SkyArchetype));
        private static readonly string[] KindNames = System.Enum.GetNames(typeof(PlanetKind));

        // Cleared when the panel is pointed at a different profile. Left standing, a search term
        // typed into one asset's inspector followed the user to the next one and made its tabs
        // look empty, and a weight stashed under an instance ID was restored onto whichever
        // object Unity handed that recycled ID to after a domain reload.
        private static string _search = "";
        private static Object _lastTarget;
        private static int _nextTargetKey = 1;
        private static readonly Dictionary<Object, int> TargetKeys = new Dictionary<Object, int>();

        /// <summary>Condition under which a row applies at all. Null means always.</summary>
        private delegate bool When(SerializedObject o);

        private sealed class Row
        {
            public Tab tab;
            public string group;
            public string field;
            public string label;      // null → nicified field name
            public bool more;         // lives under the tab's collapsed "More"
            public When when;
            public string[] weights;  // non-null → draw as a named weight array
            public int inert = -1;    // index of a weight the generator ignores

            // Precomputed once. OnGUI runs several times per frame for as long as the inspector
            // is on screen, so a label nicified, a group uppercased or a GUIContent built inside
            // it is not a cost paid once — it is a steady stream of garbage for the collector,
            // for values that never change.
            public string text;       // resolved label
            public string haystack;   // lowercased field + label + group, for the search
            public GUIContent content;
        }

        // ── The layout ─────────────────────────────────────────────────────────────────────────
        //
        // Tabs are by SUBJECT, and each tab keeps its own rarely-touched controls under "More".
        // The previous Basic/Advanced split cut across the subjects instead: the parallax master
        // was in Basic and the six per-layer values in Advanced, the colour step count in Basic and
        // the texel target in Advanced. Tuning one thing meant working in two places, which is the
        // one thing a settings panel must not ask for.

        private static readonly List<Row> Rows = Build();

        private static List<Row> Build()
        {
            var rows = new List<Row>();

            When Pixel = o => o.FindProperty("pixelArt").boolValue;
            When Smooth = o => !o.FindProperty("pixelArt").boolValue;
            When AutoFit = o => o.FindProperty("autoFitToCamera").boolValue;
            When Wrapping = o => o.FindProperty("wrapBodies").boolValue;
            When FixedLight = o => !o.FindProperty("proceduralLight").boolValue;
            When DrawnLight = o => o.FindProperty("proceduralLight").boolValue;
            When Streaks = o => o.FindProperty("shootingStars").boolValue;

            void F(Tab t, string g, string field, string label = null,
                   bool more = false, When when = null)
                => rows.Add(new Row { tab = t, group = g, field = field, label = label,
                                      more = more, when = when });

            void W(Tab t, string g, string field, string title, string[] labels, int inert = -1)
                => rows.Add(new Row { tab = t, group = g, field = field, label = title,
                                      weights = labels, inert = inert });

            // ── Style ──────────────────────────────────────────────────────────────────────────
            F(Tab.Style, "About", "description");
            F(Tab.Style, "Rendering", "pixelArt");
            F(Tab.Style, "Rendering", "cartoon", "Cartoon");
            F(Tab.Style, "Pixel art", "pixelsPerUnit", null, false, Pixel);
            F(Tab.Style, "Pixel art", "surfaceColorSteps", "Color Levels", false, Pixel);
            F(Tab.Style, "Pixel art", "ditherAmount", "Dither", false, Pixel);
            F(Tab.Style, "Pixel art", "pixelHueShift", "Hue Shift", false, Pixel);
            F(Tab.Style, "Pixel art", "terminatorSteps", "Terminator Steps", true, Pixel);
            F(Tab.Style, "Pixel art", "targetSurfaceTexels", "Surface Texels", true, Pixel);
            F(Tab.Style, "Pixel art", "surfaceBlockRange", "Surface Block Size", true, Pixel);

            // ── Sky ────────────────────────────────────────────────────────────────────────────
            W(Tab.Sky, "Contents", "archetypeWeights", "Sky Types", ArchetypeNames);
            W(Tab.Sky, "Contents", "planetKindWeights", "Planet Kinds", KindNames, (int)PlanetKind.Star);
            F(Tab.Sky, "Contents", "bodyCountScale", "Planet Count");
            F(Tab.Sky, "Contents", "planetScale", "Planet Size");
            F(Tab.Sky, "Contents", "featuredCloseStar", "Close Star");
            F(Tab.Sky, "Contents", "featuredSun", "Sun");
            F(Tab.Sky, "Placement", "spread", "Placement Area");
            F(Tab.Sky, "Placement", "separationFactor", "Spacing");
            F(Tab.Sky, "Placement", "skyOffset", "Offset");
            F(Tab.Sky, "Framing", "autoFitToCamera", "Fit To Camera");
            F(Tab.Sky, "Framing", "referenceViewHeight", "Designed For View Height", false, AutoFit);
            F(Tab.Sky, "Rings & moons", "ringChance", "Rings");
            F(Tab.Sky, "Rings & moons", "moonChance", "Moons");
            F(Tab.Sky, "Rings & moons", "maxMoonsPerBody", "Max Moons Per Body", true);
            F(Tab.Sky, "Scale classes", "heroRadius", null, true);
            F(Tab.Sky, "Scale classes", "midRadius", null, true);
            F(Tab.Sky, "Scale classes", "smallRadius", null, true);
            F(Tab.Sky, "Scale classes", "heroEdgePush", null, true);
            F(Tab.Sky, "Scale classes", "tiltRange", null, true);

            // ── Motion ─────────────────────────────────────────────────────────────────────────
            F(Tab.Motion, "Drift & spin", "driftVelocity", "Sky Drift");
            F(Tab.Motion, "Drift & spin", "spinPeriod", "Rotation (seconds)");
            F(Tab.Motion, "Parallax", "parallaxStrength", "Parallax");
            F(Tab.Motion, "Parallax", "parallaxSmall", "Bodies (near)", true);
            F(Tab.Motion, "Parallax", "parallaxLarge", "Bodies (far)", true);
            F(Tab.Motion, "Parallax", "nebulaParallax", "Nebula", true);
            F(Tab.Motion, "Parallax", "farStarParallax", "Stars (far)", true);
            F(Tab.Motion, "Parallax", "midStarParallax", "Stars (mid)", true);
            F(Tab.Motion, "Parallax", "nearStarParallax", "Stars (near)", true);
            F(Tab.Motion, "Repeat", "wrapBodies", "Repeat Bodies");
            F(Tab.Motion, "Repeat", "wrapSpacing", "Repeat Spacing", false, Wrapping);

            // ── Deep Sky ───────────────────────────────────────────────────────────────────────
            F(Tab.DeepSky, "Stars", "starDensityScale", "Star Density");
            F(Tab.DeepSky, "Stars", "brightStarDensity", "Bright Stars");
            F(Tab.DeepSky, "Stars", "twinkleAmount", "Twinkle");
            F(Tab.DeepSky, "Stars", "blinkFraction", "Blinking");
            F(Tab.DeepSky, "Stars", "starHueVariety", "Hue Variety", true);
            F(Tab.DeepSky, "Stars", "starSpikes", "Diffraction Spikes", true, Smooth);
            F(Tab.DeepSky, "Stars", "starSpikeCount", "Spike Count", true, Smooth);
            F(Tab.DeepSky, "Gas & dust", "nebulaIntensity", "Nebula");
            F(Tab.DeepSky, "Gas & dust", "nebulaPalette", "Nebula Palette");
            F(Tab.DeepSky, "Gas & dust", "dustAmount", "Dust");
            F(Tab.DeepSky, "Gas & dust", "distantGalaxyDensity", "Distant Galaxies", true, Smooth);
            F(Tab.DeepSky, "Shooting stars", "shootingStars", "Enabled");
            F(Tab.DeepSky, "Shooting stars", "shootingStarPeriod", "Seconds Between", false, Streaks);
            F(Tab.DeepSky, "Shooting stars", "shootingStarBrightness", "Brightness", false, Streaks);
            F(Tab.DeepSky, "Shooting stars", "shootingStarSpeed", "Speed", true, Streaks);
            F(Tab.DeepSky, "Shooting stars", "shootingStarTail", "Tail Length", true, Streaks);
            F(Tab.DeepSky, "Shooting stars", "shootingStarTracks", "Simultaneous", true, Streaks);
            F(Tab.DeepSky, "Shooting stars", "shootingStarColor", "Color", true, Streaks);

            // ── Light ──────────────────────────────────────────────────────────────────────────
            F(Tab.Light, "Sun", "proceduralLight", "Light From Seed");
            F(Tab.Light, "Sun", "lightDirection", "Direction", false, FixedLight);
            F(Tab.Light, "Sun", "lightElevation", "Elevation", false, FixedLight);
            F(Tab.Light, "Sun", "lightElevationRange", "Elevation Range", true, DrawnLight);
            F(Tab.Light, "Sun", "starPulse", "Pulse");
            F(Tab.Light, "Shading", "shadowDarkness", "Night Side");
            F(Tab.Light, "Shading", "atmosphereGlow", "Atmosphere");
            F(Tab.Light, "Shading", "planetAmbient", "Ambient Fill", true);
            F(Tab.Light, "Background", "baseColor", "Background");
            F(Tab.Light, "Background", "baseColor2", "Background (far)");
            F(Tab.Light, "Background", "gradientDirection", "Gradient Direction", true);
            F(Tab.Light, "Background", "gradientScale", "Gradient Scale", true);

            foreach (Row r in rows)
            {
                r.text = r.label ?? ObjectNames.NicifyVariableName(r.field);
                r.haystack = (r.field + " " + r.text + " " + r.group).ToLowerInvariant();
            }
            return rows;
        }

        // ── Entry point ────────────────────────────────────────────────────────────────────────

        public static void Draw(SerializedObject profile)
        {
            profile.Update();

            Object target = profile.targetObject;
            if (target != _lastTarget)
            {
                _lastTarget = target;
                _search = "";
                Stashed.Clear();
            }

            var tab = (Tab)Mathf.Clamp(EditorPrefs.GetInt(TabKey, 0), 0, TabNames.Length - 1);
            tab = (Tab)GUILayout.Toolbar((int)tab, TabNames, EditorStyles.toolbarButton,
                                         GUILayout.Height(22f));
            EditorPrefs.SetInt(TabKey, (int)tab);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawToolbar(profile, tab);
                EditorGUILayout.Space(2);

                if (string.IsNullOrEmpty(_search)) DrawTab(profile, tab);
                else DrawSearchResults(profile);

                DrawUnlisted(profile);
            }

            profile.ApplyModifiedProperties();
        }

        private static void DrawToolbar(SerializedObject profile, Tab tab)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);

                // Reset is per TAB rather than for the whole profile, because that is the unit
                // people actually work in: you push the motion settings around, dislike them, and
                // want the motion settings back — not your carefully chosen palette back too.
                bool searching = !string.IsNullOrEmpty(_search);
                using (new EditorGUI.DisabledScope(searching))
                {
                    var reset = new GUIContent("Reset",
                        searching
                            ? "Clear the search box to reset a tab."
                            : $"Restore every setting on the {TabNames[(int)tab]} tab to its default.");
                    if (GUILayout.Button(reset, EditorStyles.toolbarButton, GUILayout.Width(52f)))
                        ResetTab(profile, tab);
                }
            }
        }

        // ── Drawing ────────────────────────────────────────────────────────────────────────────

        private static void DrawTab(SerializedObject o, Tab tab)
        {
            string group = null;
            bool any = false;

            // One pass over the table. "Does this tab have anything under More" is answered while
            // drawing rather than by a second sweep of all sixty-seven rows.
            bool hasMore = false;
            foreach (Row row in Rows)
            {
                if (row.tab != tab || !Applies(o, row, out SerializedProperty prop)) continue;
                if (row.more) { hasMore = true; continue; }
                if (row.group != group) { GroupHeader(row.group); group = row.group; }
                DrawRow(o, row, prop);
                any = true;
            }

            if (!any)
                EditorGUILayout.LabelField("Nothing to configure here in this mode.",
                                           EditorStyles.centeredGreyMiniLabel);

            // Only offer "More" when this tab actually has advanced rows that apply — an empty
            // foldout is a promise of settings that are not there.
            if (!hasMore) return;

            EditorGUILayout.Space(4);
            if (!Foldout($"more.{tab}", "More", false)) return;

            using (new EditorGUI.IndentLevelScope())
            {
                group = null;
                foreach (Row row in Rows)
                {
                    if (row.tab != tab || !row.more) continue;
                    if (!Applies(o, row, out SerializedProperty prop)) continue;
                    if (row.group != group) { GroupHeader(row.group); group = row.group; }
                    DrawRow(o, row, prop);
                }
            }
        }

        /// <summary>
        /// Search ignores the tabs entirely.
        ///
        /// Tabs answer "show me everything about motion"; they are the wrong tool for "where is
        /// the dither slider", because answering that requires already knowing which tab it is on.
        /// Every row is labelled with its tab and group so a match is still locatable afterwards.
        /// </summary>
        private static void DrawSearchResults(SerializedObject o)
        {
            string needle = _search.ToLowerInvariant();
            int matches = 0, total = 0;
            string heading = null;

            foreach (Row row in Rows)
            {
                total++;
                if (!Matches(row, needle)) continue;
                if (!Applies(o, row, out SerializedProperty prop)) continue;

                string where = $"{TabNames[(int)row.tab]}  ›  {row.group}";
                if (where != heading) { GroupHeader(where); heading = where; }
                DrawRow(o, row, prop);
                matches++;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(
                matches == 0
                    ? $"No setting matches “{_search}”."
                    : $"{matches} of {total} settings match.",
                EditorStyles.centeredGreyMiniLabel);
        }

        private static bool Matches(Row row, string needle) => row.haystack.Contains(needle);

        private static string Label(Row row) => row.text;

        /// <summary>
        /// Whether this row applies, handing back the property it had to look up anyway.
        ///
        /// Resolving the field in Applies and again in DrawRow, plus a second sweep of all
        /// sixty-seven rows to ask whether the tab has anything under More, comes to roughly two
        /// hundred string-keyed native lookups per OnGUI — several times a frame, while a slider
        /// is being dragged.
        /// </summary>
        private static bool Applies(SerializedObject o, Row row, out SerializedProperty prop)
        {
            prop = o.FindProperty(row.field);
            return prop != null && (row.when == null || row.when(o));
        }

        private static bool Applies(SerializedObject o, Row row) => Applies(o, row, out _);

        private static void DrawRow(SerializedObject o, Row row, SerializedProperty prop)
        {
            if (row.weights != null) { WeightArray(o, row, prop); return; }
            if (prop == null) return;   // survives a renamed field rather than throwing

            // The tooltip is carried across explicitly. Supplying a GUIContent replaces the one
            // Unity would have built from the [Tooltip] attribute, so without this a renamed label
            // costs the field its explanation — on a panel whose tooltips carry most of the
            // guidance.
            row.content ??= new GUIContent(row.text, prop.tooltip);
            EditorGUILayout.PropertyField(prop, row.content, true);
        }

        // ── Widgets ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A group heading, drawn as a banded bar with an accent edge.
        ///
        /// The first attempt was a five-percent tint behind a mini bold label, which is what a
        /// heading looks like when you are afraid of decorating an inspector. It disappeared. A
        /// heading is not there to be tasteful, it is there to be the thing your eye lands on
        /// while scrolling past forty controls — so it gets a band you can see, a coloured edge to
        /// catch the eye at the left margin where scanning starts, a rule underneath to separate
        /// it from its own contents, and upright capitals at a size that is not an apology.
        /// </summary>
        private static void GroupHeader(string label)
        {
            EditorGUILayout.Space(10f);

            Rect r = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
            var bar = new Rect(r.x - 4f, r.y, r.width + 8f, r.height);   // bleed to the panel edges
            bool pro = EditorGUIUtility.isProSkin;

            EditorGUI.DrawRect(bar, pro ? new Color(1f, 1f, 1f, 0.09f)
                                        : new Color(0f, 0f, 0f, 0.075f));
            EditorGUI.DrawRect(new Rect(bar.x, bar.yMax - 1f, bar.width, 1f),
                               pro ? new Color(0f, 0f, 0f, 0.35f) : new Color(0f, 0f, 0f, 0.14f));
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, 3f, bar.height),
                               pro ? new Color(0.35f, 0.62f, 0.92f) : new Color(0.18f, 0.42f, 0.76f));

            // ToUpperInvariant, never ToUpper: on a Turkish system the culture-aware version
            // turns "i" into "İ", so the panel would shout "PİXEL ART" at its own author.
            EditorGUI.LabelField(new Rect(bar.x + 11f, r.y, bar.width - 13f, r.height),
                                 label.ToUpperInvariant(), HeaderLabel);

            EditorGUILayout.Space(3f);
        }

        // Styles are built once and rebuilt only when the editor theme changes. OnGUI runs several
        // times per frame, so a style allocated inline is garbage generated continuously for as
        // long as the inspector is on screen — but a style cached forever keeps the colours of the
        // theme it was born under, and the panel would come out unreadable after a theme switch.
        private static bool _stylesForPro;
        private static GUIStyle _rightMini, _headerLabel;

        private static void EnsureStyles()
        {
            if (_rightMini != null && _stylesForPro == EditorGUIUtility.isProSkin) return;
            _stylesForPro = EditorGUIUtility.isProSkin;

            _rightMini = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            _headerLabel = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal =
                {
                    textColor = _stylesForPro
                        ? new Color(0.87f, 0.89f, 0.92f)
                        : new Color(0.13f, 0.14f, 0.16f),
                },
            };
        }

        private static GUIStyle RightMini { get { EnsureStyles(); return _rightMini; } }
        private static GUIStyle HeaderLabel { get { EnsureStyles(); return _headerLabel; } }

        private static bool Foldout(string key, string label, bool defaultOpen)
        {
            bool open = EditorPrefs.GetBool(FoldKey + key, defaultOpen);
            bool now = EditorGUILayout.Foldout(open, label, true, EditorStyles.foldoutHeader);
            if (now != open) EditorPrefs.SetBool(FoldKey + key, now);
            return now;
        }

        /// Remembers what a weight was before its checkbox turned it off, so switching a kind back
        /// on restores the value you tuned instead of a bare 1. Keyed by asset AND property, or
        /// two profiles open in turn would hand each other their stashed values.
        private static readonly Dictionary<string, float> Stashed = new Dictionary<string, float>();

        /// <summary>
        /// Session-local id for an inspected asset. Unity 6 no longer allows casting an entity id
        /// to int, and this dictionary only has to stay unique until the next domain reload.
        /// </summary>
        private static int TargetKey(Object target)
        {
            if (target == null) return 0;
            if (!TargetKeys.TryGetValue(target, out int key))
            {
                key = _nextTargetKey++;
                TargetKeys[target] = key;
            }
            return key;
        }

        /// <summary>
        /// A weight array drawn as a collapsed summary that opens into one checkbox + slider row
        /// per entry.
        ///
        /// Two of these account for sixteen of the panel's rows while being among the least
        /// touched controls in it, so they are closed by default and say what they contain from
        /// the outside — "6 of 8 enabled" is the whole answer most of the time.
        ///
        /// The checkbox exists because turning a kind OFF is the operation people actually want,
        /// and expressing it as "drag this slider all the way to zero" makes a common intent into
        /// a fiddly gesture with no obvious end point.
        /// </summary>
        private static void WeightArray(SerializedObject o, Row row, SerializedProperty arr)
        {
            if (arr == null || !arr.isArray) return;

            string[] labels = row.weights;

            // Resized only on an explicit click. Writing arraySize during DRAWING meant that
            // merely selecting a profile whose array predates a new enum entry modified the
            // asset, marked it dirty and pushed an undo step — with no user action at all. A
            // panel must be able to look at something without changing it.
            if (arr.arraySize != labels.Length)
            {
                EditorGUILayout.HelpBox(
                    $"This profile has {arr.arraySize} weights; there are now {labels.Length}. " +
                    "It was saved by an older version.", MessageType.Warning);
                if (GUILayout.Button($"Update to {labels.Length} weights"))
                {
                    arr.arraySize = labels.Length;
                    o.ApplyModifiedProperties();
                }
                return;
            }

            int enabled = 0;
            for (int i = 0; i < labels.Length; i++)
                if (i != row.inert && arr.GetArrayElementAtIndex(i).floatValue > 0f) enabled++;
            int available = row.inert >= 0 ? labels.Length - 1 : labels.Length;

            Rect head = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
            const float summaryW = 112f;

            string key = FoldKey + "w." + row.field;
            bool open = EditorPrefs.GetBool(key, false);
            bool now = EditorGUI.Foldout(new Rect(head.x, head.y, head.width - summaryW, head.height),
                                         open, Label(row), true);
            if (now != open) EditorPrefs.SetBool(key, now);

            EditorGUI.LabelField(new Rect(head.xMax - summaryW, head.y, summaryW, head.height),
                                 $"{enabled} of {available} enabled", RightMini);
            if (!now) return;

            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < labels.Length; i++)
                {
                    SerializedProperty e = arr.GetArrayElementAtIndex(i);
                    using (new EditorGUI.DisabledScope(i == row.inert))
                        WeightRow(e, Nicify(labels[i]), stashKey: $"{TargetKey(o.targetObject)}/{e.propertyPath}");
                }
            }
        }

        /// <summary>
        /// One weight: a checkbox, a name, and a slider, laid out by hand.
        ///
        /// The indent level is zeroed for the duration. Every <c>EditorGUI</c> control given an
        /// explicit rect runs it through <c>IndentedRect</c> itself, which does not merely move the
        /// rect — it takes the indent off the WIDTH. Under one level of indent a 16-pixel checkbox
        /// came out one pixel wide: drawn, apparently normal, and impossible to hit. With the
        /// slider disabled at zero, that checkbox was the only way back, so a weight dragged to
        /// zero could not be turned on again.
        /// </summary>
        private static void WeightRow(SerializedProperty e, string label, string stashKey)
        {
            Rect r = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            try
            {
                var toggleRect = new Rect(r.x, r.y, 16f, r.height);
                float labelX = r.x + 20f;
                float labelW = Mathf.Max(60f, EditorGUIUtility.labelWidth - (labelX - r.x));
                var labelRect = new Rect(labelX, r.y, labelW, r.height);
                var sliderRect = new Rect(labelX + labelW, r.y,
                                          Mathf.Max(50f, r.xMax - labelX - labelW), r.height);

                bool on = e.floatValue > 0f;
                bool nowOn = EditorGUI.Toggle(toggleRect, on);
                if (nowOn != on)
                {
                    if (nowOn)
                        e.floatValue = Stashed.TryGetValue(stashKey, out float v) && v > 0f ? v : 1f;
                    else
                    {
                        Stashed[stashKey] = e.floatValue;
                        e.floatValue = 0f;
                    }
                }

                EditorGUI.LabelField(labelRect, label);

                // The slider stays live even at zero. Disabling it there sounds tidy and creates a
                // trap: drag a weight down, and the instant it touches zero the control you are
                // holding goes dead under the cursor — you have to let go and find the checkbox to
                // come back. The checkbox is a shortcut for "off", not a gate on the slider.
                e.floatValue = EditorGUI.Slider(sliderRect, e.floatValue, 0f, 3f);
            }
            finally
            {
                EditorGUI.indentLevel = indent;
            }
        }

        /// <summary>Star is placed by archetypes, never drawn as a planet — say so in the label.</summary>
        private static string Nicify(string enumName) =>
            enumName == "Star" ? "Star (by sky type)" : ObjectNames.NicifyVariableName(enumName);

        // ── Safety net ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Anything serialized on the profile that the table above does not place.
        ///
        /// A hand-authored layout buys a sensible order at the cost of a silent failure mode: a
        /// field added to the profile and not added to the table simply does not exist as far as
        /// the user is concerned. That is a worse bug than an ugly inspector, because nothing
        /// about it looks wrong — so the panel reports what it missed instead of pretending it
        /// covered everything.
        /// </summary>
        /// The set of fields the table places. Built once; the answer cannot change per repaint.
        private static HashSet<string> _placed;

        private static void DrawUnlisted(SerializedObject o)
        {
            if (_placed == null)
            {
                _placed = new HashSet<string>();
                foreach (Row row in Rows) _placed.Add(row.field);
            }
            HashSet<string> placed = _placed;

            List<SerializedProperty> missing = null;
            SerializedProperty it = o.GetIterator();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (it.name == "m_Script" || placed.Contains(it.name)) continue;
                (missing ??= new List<SerializedProperty>()).Add(it.Copy());
            }
            if (missing == null) return;

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                $"{missing.Count} setting(s) are not placed on any tab. They are shown here so " +
                "they can still be edited — add them to ProfilePanel.Build().",
                MessageType.Warning);
            foreach (SerializedProperty prop in missing)
                EditorGUILayout.PropertyField(prop, true);
        }

        // ── Reset ──────────────────────────────────────────────────────────────────────────────

        private static void ResetTab(SerializedObject target, Tab tab)
        {
            var defaults = ScriptableObject.CreateInstance<SpaceBackgroundProfile>();
            try
            {
                var source = new SerializedObject(defaults);
                foreach (Row row in Rows)
                {
                    if (row.tab != tab) continue;      // deliberately ignores `when`: a hidden
                                                       // field is still part of the tab, and
                                                       // leaving it behind would make Reset depend
                                                       // on which mode happened to be selected
                    SerializedProperty prop = source.FindProperty(row.field);
                    if (prop != null) target.CopyFromSerializedProperty(prop);
                }
                target.ApplyModifiedProperties();
            }
            finally
            {
                Object.DestroyImmediate(defaults);
            }
        }
    }
}
