using System.IO;
using UnityEditor;
using UnityEngine;

namespace BirichStudio.ProceduralSpace.Editor
{
    /// <summary>
    /// Inspector for <see cref="SpaceBackground"/>.
    ///
    /// It embeds the profile's settings rather than making the user go and find the asset. The
    /// settings and the sky they produce are one thing to think about, and splitting them across
    /// two selections means every adjustment costs a trip out of the object you are looking at.
    /// </summary>
    [CustomEditor(typeof(SpaceBackground))]
    [CanEditMultipleObjects]
    internal class SpaceBackgroundEditor : UnityEditor.Editor
    {
        private static string PackagePresetFolder => SpaceMenu.Root + "/Presets";
        private const string DefaultProfileFolder = "Assets/Space Backgrounds";

        private SerializedObject _profileSO;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty profileProp = serializedObject.FindProperty("profile");
            EditorGUILayout.PropertyField(profileProp);

            DrawProperties("seed", "randomizeSeedOnPlay", "randomArchetype", "forcedArchetype",
                           "sortingLayerName", "sortingOrder", "material");

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            DrawSceneViewNote();

            EditorGUILayout.Space();
            DrawPixelGridCheck();

            EditorGUILayout.Space();
            DrawSeedControls();

            EditorGUILayout.Space();
            DrawSnapshotSection();

            EditorGUILayout.Space();
            DrawProfileSection(profileProp);

            RunPendingAction();
        }

        // ── Deferred actions ───────────────────────────────────────────────────────────────────
        //
        // A modal file panel, a DisplayDialog, or anything that touches the AssetDatabase cannot
        // run in the middle of a layout scope. Unity may repaint while the dialog is up and the
        // import that follows resets the GUI state, so the scope's EndLayoutGroup comes back to
        // find no BeginLayoutGroup waiting for it — "EndLayoutGroup: BeginLayoutGroup must be
        // called first", thrown from a `using` block that did nothing wrong.
        //
        // So buttons only record what was asked for. It is carried out here, after every scope
        // has closed, and the frame is then abandoned with ExitGUI because the inspector it was
        // drawing may no longer describe anything that exists.

        private enum Pending { None, Save, Load, CreateProfile, DuplicateProfile }

        private Pending _pending;
        private SkySnapshot _pendingSnapshot;
        private SpaceBackgroundProfile _pendingProfile;

        private void RunPendingAction()
        {
            if (_pending == Pending.None) return;

            Pending action = _pending;
            SkySnapshot snap = _pendingSnapshot;
            SpaceBackgroundProfile prof = _pendingProfile;
            _pending = Pending.None;
            _pendingSnapshot = null;
            _pendingProfile = null;

            switch (action)
            {
                case Pending.Save:             SaveSnapshot((SpaceBackground)target); break;
                case Pending.Load:             LoadSnapshotIntoTargets(snap);         break;
                case Pending.CreateProfile:    CreateProfileForTargets();             break;
                case Pending.DuplicateProfile: DuplicateProfileForTargets(prof);      break;
            }

            GUIUtility.ExitGUI();
        }

        /// <summary>
        /// Warns that the Scene view cannot be used to judge framing.
        ///
        /// The sky is anchored to whichever camera renders it — that is what makes a parallax
        /// backdrop a backdrop, and in the Scene view that camera is the Scene camera. So the
        /// composition slides around as you navigate while the quad stays glued to the game
        /// camera, and the bodies look like they are being generated off to one side of the
        /// frame. Everyone hits this, everyone reads it as a bug, and nothing in the inspector
        /// said otherwise.
        /// </summary>
        private void DrawSceneViewNote()
        {
            EditorGUILayout.HelpBox(
                "Judge framing in the GAME view.\n\n" +
                "The sky is anchored to whichever camera renders it, so in the Scene view it " +
                "follows the Scene camera while this quad follows your game camera. The two only " +
                "line up when you are looking from the game camera's position — everywhere else " +
                "the bodies will appear shifted out of the frame.",
                MessageType.Info);
        }

        /// <summary>
        /// Reports whether the shader's pixel grid lands on whole screen pixels.
        ///
        /// This is the defect behind "the pixels shimmer constantly", and no screenshot can show
        /// it — it only exists in motion, which is exactly why it needs saying here.
        ///
        /// <c>pixelsPerUnit</c> is a grid in WORLD space: the shader snaps every sampling
        /// position to 1/PPU world units. Nothing in the package ties that grid to the camera, so
        /// one shader texel comes out at whatever fraction of a screen pixel the camera happens to
        /// give it — 5.36 in the demo scene at 1080p. Two things then go wrong at once. The blocks
        /// are drawn at uneven sizes, some five screen pixels and some six. And the moment
        /// ANYTHING moves — the sky drift, the camera, a planet turning — every block boundary
        /// re-lands on a different screen pixel, so the whole image reshuffles which blocks are
        /// which size, every frame. On a lava world, whose glowing cracks put maximum contrast
        /// across single texels, that reads as the surface boiling.
        ///
        /// A camera sized FROM the grid never shows this, which is why it survives screenshots
        /// and appears the moment the scene is played.
        /// </summary>
        private void DrawPixelGridCheck()
        {
            var bg = (SpaceBackground)target;
            SpaceBackgroundProfile p = bg.Profile;
            if (p == null || !p.pixelArt) return;              // smooth mode has no grid to miss

            SpaceCameraFit fit = bg.GetComponent<SpaceCameraFit>();
            Camera cam = fit != null ? fit.TargetCamera : Camera.main;
            if (cam == null) return;

            int screenPx = cam.pixelHeight;
            if (screenPx <= 0) return;

            float viewH = cam.orthographic
                ? cam.orthographicSize * 2f
                : 2f * Mathf.Abs(bg.transform.position.z - cam.transform.position.z)
                      * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            if (viewH <= 0.0001f) return;

            float ppu = Mathf.Max(1f, p.pixelsPerUnit);
            float texelPx = screenPx / viewH / ppu;            // screen pixels per shader texel
            if (texelPx < 0.5f) return;                        // grid finer than the screen: a different problem

            int n = Mathf.Max(1, Mathf.RoundToInt(texelPx));
            if (Mathf.Abs(texelPx - n) < 0.01f)
            {
                EditorGUILayout.HelpBox(
                    $"Pixel grid is aligned: 1 texel = {n} screen pixel{(n == 1 ? "" : "s")} " +
                    $"at {cam.pixelWidth}x{screenPx}.", MessageType.None);
                return;
            }

            EditorGUILayout.HelpBox(
                $"Pixel grid is NOT aligned to the screen: 1 texel = {texelPx:0.00} screen " +
                $"pixels at {cam.pixelWidth}x{screenPx}.\n\n" +
                "Blocks are drawn at uneven sizes, and every time the sky drifts or a planet " +
                "turns they re-land on different screen pixels — which looks like the whole " +
                "image shimmering. It is invisible in a still and unmissable in motion.\n\n" +
                "Fix it by matching the camera to the grid, or the grid to the camera.",
                MessageType.Warning);

            float fitSize = screenPx / (2f * n * ppu);
            float fitPPU = screenPx / viewH / n;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!cam.orthographic))
                {
                    if (GUILayout.Button($"Camera → {fitSize:0.###} ortho size", GUILayout.Height(22)))
                    {
                        Undo.RecordObject(cam, "Fit camera to pixel grid");
                        cam.orthographicSize = fitSize;
                        EditorUtility.SetDirty(cam);
                    }
                }
                if (GUILayout.Button($"Grid → {fitPPU:0.###} PPU", GUILayout.Height(22)))
                {
                    Undo.RecordObject(p, "Fit pixel grid to camera");
                    p.pixelsPerUnit = fitPPU;
                    EditorUtility.SetDirty(p);
                    bg.Rebuild();
                }
            }

            EditorGUILayout.HelpBox(
                "Either button aligns this window size only — the grid is a ratio, so it comes " +
                "apart again at a different resolution. For a shipping game use URP's Pixel " +
                "Perfect Camera with a fixed reference resolution and set Pixels Per Unit here " +
                "to its Assets PPU (or an exact divisor of it); the sky then rides the same " +
                "locked grid as your sprites at every window size.", MessageType.None);
        }

        // ── Snapshots ──────────────────────────────────────────────────────────────────────────

        private SkySnapshot _snapshot;

        /// <summary>
        /// Save and restore a whole sky.
        ///
        /// The seed alone is not a sky. It is a sky *given a profile*, and the moment the profile
        /// moves — one slider, a week later — the seed still resolves but no longer to the thing
        /// that was worth keeping. There is no way back from that, because the thing that changed
        /// was not the seed. This is the way back.
        /// </summary>
        private void DrawSnapshotSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Snapshot", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(
                    "Stores the seed and every setting together, so this exact sky can be " +
                    "restored later even after the profile has been changed.\n\n" +
                    "Loading WRITES the stored settings into the assigned profile — every " +
                    "background sharing that profile changes with it. Undo reverses it.",
                    EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.Space(2);

                using (new EditorGUILayout.HorizontalScope())
                {
                    _snapshot = (SkySnapshot)EditorGUILayout.ObjectField(
                        _snapshot, typeof(SkySnapshot), false);

                    using (new EditorGUI.DisabledScope(_snapshot == null || !_snapshot.HasSettings))
                        if (GUILayout.Button("Load", GUILayout.Width(60), GUILayout.Height(20)))
                        {
                            _pending = Pending.Load;
                            _pendingSnapshot = _snapshot;
                        }
                }

                if (_snapshot != null && !_snapshot.HasSettings)
                    EditorGUILayout.HelpBox("That snapshot is empty — it was created but never " +
                                            "captured from a sky.", MessageType.Warning);

                if (_snapshot != null && _snapshot.HasSettings)
                    EditorGUILayout.LabelField(
                        $"seed {_snapshot.seed}   ·   from {(_snapshot.capturedFrom == "" ? "?" : _snapshot.capturedFrom)}" +
                        $"   ·   {_snapshot.capturedUtc}",
                        EditorStyles.miniLabel);

                using (new EditorGUI.DisabledScope(targets.Length != 1))
                    if (GUILayout.Button("Save Snapshot…", GUILayout.Height(22)))
                        _pending = Pending.Save;
            }
        }

        private void SaveSnapshot(SpaceBackground bg)
        {
            SpaceBackgroundProfile p = bg.Profile;
            string suggested = $"{(p != null ? p.name : "Sky")} {bg.Seed}";

            Directory.CreateDirectory(DefaultProfileFolder);
            string path = EditorUtility.SaveFilePanelInProject(
                "Save sky snapshot", suggested, "asset",
                "Stores the seed and a full copy of the settings.", DefaultProfileFolder);
            if (string.IsNullOrEmpty(path)) return;

            var snap = ScriptableObject.CreateInstance<SkySnapshot>();
            bg.CaptureSnapshot(snap);
            AssetDatabase.CreateAsset(snap, path);
            AssetDatabase.SaveAssets();

            _snapshot = snap;
            EditorGUIUtility.PingObject(snap);
        }

        private void LoadSnapshotIntoTargets(SkySnapshot snap)
        {
            foreach (Object t in targets)
            {
                var bg = (SpaceBackground)t;
                SpaceBackgroundProfile p = bg.Profile;

                // No profile to write into: make one, so Load works on a background that has
                // never been given a profile rather than silently doing nothing.
                if (p == null)
                {
                    p = ScriptableObject.CreateInstance<SpaceBackgroundProfile>();
                    AssignNewProfile(p, snap.name);
                    p = bg.Profile;
                }

                if (IsBundledPreset(p) &&
                    !EditorUtility.DisplayDialog(
                        "Restore into a bundled preset?",
                        $"\"{p.name}\" ships with the package. Restoring writes the snapshot's " +
                        "settings into it, which changes every scene using it and is lost when " +
                        "the package updates.\n\nDuplicate it first instead?",
                        "Write into it anyway", "Cancel"))
                    continue;

                Undo.RecordObjects(new Object[] { bg, p }, "Load Sky Snapshot");
                if (bg.LoadSnapshot(snap))
                {
                    EditorUtility.SetDirty(bg);
                    EditorUtility.SetDirty(p);
                }
            }
        }

        private void DrawProperties(params string[] names)
        {
            foreach (string n in names)
            {
                SerializedProperty prop = serializedObject.FindProperty(n);
                if (prop != null) EditorGUILayout.PropertyField(prop);
            }
        }

        // ── Seed ───────────────────────────────────────────────────────────────────────────────

        private void DrawSeedControls()
        {
            var bg = (SpaceBackground)target;

            using (new EditorGUILayout.HorizontalScope())
            {
                // Finding a sky you like is the main thing anyone does with this asset, and it is
                // done by rolling the dice repeatedly. Without a way back, rolling one past a sky
                // you liked loses it for good — which quietly teaches people to stop rolling.
                using (new EditorGUI.DisabledScope(!bg.CanStepSeed(-1)))
                    if (GUILayout.Button("◀", GUILayout.Width(28), GUILayout.Height(24)))
                        StepAll(-1);

                if (GUILayout.Button("Randomize Seed", GUILayout.Height(24)))
                    foreach (Object t in targets)
                    {
                        Undo.RecordObject(t, "Randomize Space Background Seed");
                        ((SpaceBackground)t).RandomizeSeed();
                        EditorUtility.SetDirty(t);
                    }

                using (new EditorGUI.DisabledScope(!bg.CanStepSeed(1)))
                    if (GUILayout.Button("▶", GUILayout.Width(28), GUILayout.Height(24)))
                        StepAll(1);

                if (GUILayout.Button("Rebuild", GUILayout.Width(70), GUILayout.Height(24)))
                    foreach (Object t in targets)
                        ((SpaceBackground)t).Rebuild();
            }

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.LabelField("Composed Archetype", bg.Archetype.ToString());
        }

        private void StepAll(int direction)
        {
            foreach (Object t in targets)
            {
                Undo.RecordObject(t, "Step Space Background Seed");
                ((SpaceBackground)t).StepSeedHistory(direction);
                EditorUtility.SetDirty(t);
            }
        }

        // ── Profile ────────────────────────────────────────────────────────────────────────────

        private void DrawProfileSection(SerializedProperty profileProp)
        {
            var profile = profileProp.objectReferenceValue as SpaceBackgroundProfile;

            if (profile == null)
            {
                // Without a profile the component runs on hidden defaults: the inspector shows
                // "None" and not one of the sixty-seven settings that shape the sky. There is no
                // way to discover from here that tuning is even possible, so the empty state has to
                // offer the way in rather than just report the absence.
                EditorGUILayout.HelpBox(
                    "No profile assigned — the sky is using built-in defaults and cannot be tuned. " +
                    "Create one to open up the settings.", MessageType.Info);

                if (GUILayout.Button("Create Profile", GUILayout.Height(26)))
                    _pending = Pending.CreateProfile;
                return;
            }

            if (IsBundledPreset(profile))
            {
                // Editing a shipped preset in place is a trap with a delayed cost: it changes
                // every scene using that preset, and the next package update overwrites the work
                // without warning. Say so where the editing would happen, not in a README.
                EditorGUILayout.HelpBox(
                    "This is a preset that ships with the package. Edits apply to every scene " +
                    "using it and are lost when the package updates — duplicate it first.",
                    MessageType.Warning);

                if (GUILayout.Button("Duplicate & Edit", GUILayout.Height(24)))
                {
                    _pending = Pending.DuplicateProfile;
                    _pendingProfile = profile;
                    return;
                }
                EditorGUILayout.Space(2);
            }

            if (_profileSO == null || _profileSO.targetObject != profile)
                _profileSO = new SerializedObject(profile);

            ProfilePanel.Draw(_profileSO);

            EditorGUILayout.Space(2);
            if (GUILayout.Button("Save As New Preset"))
            {
                _pending = Pending.DuplicateProfile;
                _pendingProfile = profile;
            }
        }

        private static bool IsBundledPreset(SpaceBackgroundProfile profile)
        {
            string path = AssetDatabase.GetAssetPath(profile);
            // Ordinal, not the culture-sensitive default: under some cultures the default
            // overload can compare two byte-identical paths as unequal, and the only symptom is
            // that the warning above silently stops appearing and the user edits a shipped asset.
            return !string.IsNullOrEmpty(path)
                && path.StartsWith(PackagePresetFolder, System.StringComparison.Ordinal);
        }

        private void CreateProfileForTargets()
        {
            var created = ScriptableObject.CreateInstance<SpaceBackgroundProfile>();
            AssignNewProfile(created, "SpaceBackgroundProfile");
        }

        private void DuplicateProfileForTargets(SpaceBackgroundProfile source)
        {
            var copy = Object.Instantiate(source);
            AssignNewProfile(copy, source.name + " Copy");
        }

        private void AssignNewProfile(SpaceBackgroundProfile asset, string baseName)
        {
            Directory.CreateDirectory(DefaultProfileFolder);
            AssetDatabase.Refresh();

            string path = AssetDatabase.GenerateUniqueAssetPath($"{DefaultProfileFolder}/{baseName}.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            foreach (Object t in targets)
            {
                Undo.RecordObject(t, "Assign Space Background Profile");
                ((SpaceBackground)t).Profile = asset;
                EditorUtility.SetDirty(t);
            }

            _profileSO = null;
            EditorGUIUtility.PingObject(asset);
        }
    }
}
