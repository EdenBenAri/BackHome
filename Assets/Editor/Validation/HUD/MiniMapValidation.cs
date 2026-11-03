using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Opt-in integration checks using disposable prefab contents; no scene or save edits.</summary>
[InitializeOnLoad]
public static class MiniMapValidation
{
    const string Trigger = "Temp/BackHomeMiniMapValidation.trigger";
    const string Output = "Logs/MiniMap";
    const string PlanetPath = "Assets/Resources/Galaxy/ChapterOne/PlanetChapterOne.prefab";
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static MiniMapValidation() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode
            || !File.Exists(Trigger)) return;
        File.Delete(Trigger);
        Run();
    }

    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    static void Set(object target, string field, object value) { target.GetType().GetField(field, Private).SetValue(target, value); }
    static object Call(object target, string method, params object[] args) { return target.GetType().GetMethod(method, Private).Invoke(target, args); }

    [MenuItem("BackHome/HUD/Validate MiniMap")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(Output);
        var report = new StringBuilder();
        GameObject planetRoot = null, hud = null, cameraObject = null, fakePlayer = null;
        MiniMapRaster raster = null;
        string saveBefore = PlayerPrefs.GetString("BackHome.Stages.v1.ChapterOne", "");
        string sceneBefore = File.ReadAllText("Assets/Scenes/Galaxy.unity");
        string planetBefore = File.ReadAllText(PlanetPath);
        try
        {
            HudScenePresence.BuildBatch();
            planetRoot = PrefabUtility.LoadPrefabContents(PlanetPath);
            var planet = planetRoot.GetComponent<PlanetAuthoring>().RuntimePlanet;
            var map = planet.GetComponent<PlanetTileMap>();
            var progress = planet.GetComponent<PlanetStageProgression>();
            var fog = planet.GetComponent<PlanetStageFog>();
            Set(progress, "persistProgress", false);
            progress.Initialize();
            fog.Rebuild();
            Require(fog.CoveredStagesMask == 28, "World fog must cover B/C/D initially.");

            var centers = new Vector3[5];
            var counts = new int[5];
            Vector3 waterPoint = Vector3.zero;
            for (int lat = 0; lat < map.LatitudeBands; lat++)
            for (int lon = 0; lon < map.LongitudeBands; lon++)
            {
                map.TryGetCellCenter(lat, lon, out Vector3 point);
                if (map.GetHeight(lat, lon) <= PlanetTileMap.WaterHeight) { waterPoint = point; continue; }
                int stage = (int)progress.StageAtCell(lat, lon);
                centers[stage] += (point - planet.Center).normalized;
                counts[stage]++;
            }
            // Find actual land nearest each stage centroid, rather than averaging into ocean.
            var positions = new Vector3[5];
            var scores = new float[] { -2, -2, -2, -2, -2 };
            for (int lat = 0; lat < map.LatitudeBands; lat++)
            for (int lon = 0; lon < map.LongitudeBands; lon++)
            {
                if (map.GetHeight(lat, lon) <= PlanetTileMap.WaterHeight) continue;
                int stage = (int)progress.StageAtCell(lat, lon);
                map.TryGetCellCenter(lat, lon, out Vector3 point);
                float score = Vector3.Dot((point - planet.Center).normalized, centers[stage].normalized);
                if (score > scores[stage]) { scores[stage] = score; positions[stage] = point; }
            }
            var palette = new MiniMapPalette();
            raster = new MiniMapRaster(128);
            void BakeAt(Vector3 point, int mask)
            {
                Vector3 up = (point - planet.Center).normalized;
                Vector3 forward = Vector3.ProjectOnPlane(Vector3.up, up).normalized;
                if (forward.sqrMagnitude < .001f) forward = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
                raster.Bake(map, progress, mask, palette, planet.Center, up, Vector3.Cross(up, forward), forward, planet.Radius, 42f);
            }
            bool FogPixel(Color32 c) => c.Equals((Color32)palette.fog) || c.Equals((Color32)palette.fogEdge);
            for (int stage = 1; stage <= 4; stage++)
            {
                Require(counts[stage] > 0, "Missing stage land.");
                BakeAt(positions[stage], 28);
                Color32 pixel = raster.Texture.GetPixels32()[64 * 128 + 64];
                Require(FogPixel(pixel) == (stage > 1), "Incorrect fog at stage " + stage);
                File.WriteAllBytes(Output + "/stage-" + stage + "-locked.png", raster.Texture.EncodeToPNG());
            }
            BakeAt(waterPoint, 28);
            Require(raster.Texture.GetPixels32()[64 * 128 + 64].Equals((Color32)palette.water), "Ocean is covered by stage fog.");
            report.AppendLine("PASS: all four production stages sampled; locked B/C/D are opaque, A and ocean remain visible.");

            // Frame the actual A/B boundary to inspect both terrain and fog together.
            Vector3 boundary = (centers[1].normalized + centers[2].normalized).normalized;
            BakeAt(planet.Center + boundary * planet.Radius, 28);
            File.WriteAllBytes(Output + "/boundary-locked.png", raster.Texture.EncodeToPNG());

            hud = PrefabUtility.LoadPrefabContents("Assets/Resources/HUD/Hud.prefab");
            var widget = hud.GetComponentInChildren<MiniMap>(true);
            Require(widget != null && hud.GetComponent<MiniMap>() == null, "HUD must use the nested widget, not the legacy root component.");
            Require(PrefabUtility.GetCorrespondingObjectFromSource(widget) != null, "MiniMap is not a linked prefab instance.");
            var rect = (RectTransform)widget.transform;
            Require(rect.anchorMin == Vector2.one && rect.anchorMax == Vector2.one && rect.pivot == Vector2.one
                && rect.anchoredPosition.x < 0 && rect.anchoredPosition.y <= -100, "MiniMap is not on the right below the account row.");
            foreach (Graphic graphic in widget.GetComponentsInChildren<Graphic>(true)) Require(!graphic.raycastTarget, "MiniMap blocks touch input.");
            Require(widget.GetComponentsInChildren<Camera>().Length == 0, "MiniMap adds a camera.");
            var so = new SerializedObject(widget);
            foreach (string field in new[] { "viewport", "terrainImage", "visibility", "playerMarker", "homeMarker", "enemyTemplate" })
                Require(so.FindProperty(field).objectReferenceValue != null, "Missing prefab reference: " + field);
            var raw = (RawImage)so.FindProperty("terrainImage").objectReferenceValue;
            raw.texture = raster.Texture;
            report.AppendLine("PASS: linked prefab, editable references, right anchor, no touch interception or extra camera.");

            // Render the authored HUD with its real canvas scaler and account-row positions.
            cameraObject = new GameObject("MiniMap validation camera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, hud.scene);
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(56, 91, 116, 255);
            camera.cullingMask = 1 << 30;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(hud.scene);
            camera.nearClipPlane = .1f; camera.farClipPlane = 100f;
            camera.transform.position = new Vector3(0, 0, -10);
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false; data.volumeLayerMask = 0;
            Canvas canvas = hud.GetComponentInChildren<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 5f;
            foreach (Transform child in hud.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
            Capture(camera, "hud-portrait", 1080, 1920);
            Capture(camera, "hud-tall", 1080, 2400);
            Capture(camera, "hud-landscape", 1920, 1080);
            report.AppendLine("PASS: GPU HUD captures at 1080x1920, 1080x2400 and 1920x1080.");

            Call(widget, "Awake");
            fakePlayer = new GameObject("MiniMap test player");
            fakePlayer.SetActive(false);
            var player = fakePlayer.AddComponent<PlayerVitals>();
            player.transform.position = positions[1];
            Set(widget, "_player", player); Set(widget, "_planet", planet); Set(widget, "_tiles", map);
            Set(widget, "_progress", progress); Set(widget, "_fog", fog); Set(widget, "_center", planet.Center);
            Vector3 testUp = (positions[1] - planet.Center).normalized;
            Vector3 testForward = Vector3.ProjectOnPlane(Vector3.up, testUp).normalized;
            Set(widget, "_playerDir", testUp); Set(widget, "_up", testUp);
            Set(widget, "_forward", testForward); Set(widget, "_right", Vector3.Cross(testUp, testForward));
            Set(widget, "_planetRadius", planet.Radius);
            Call(widget, "BakeIfNeeded");
            Require(!(bool)Call(widget, "IsMarkerVisible", positions[2]), "Locked stage enemy marker leaked.");
            var texture = (Texture2D)raw.texture;
            uint uploads = texture.updateCount;
            for (int i = 0; i < 20; i++) Call(widget, "BakeIfNeeded");
            Require(texture.updateCount == uploads, "Idle terrain keeps uploading.");
            player.transform.position += testForward;
            Call(widget, "BakeIfNeeded");
            Require(texture.updateCount == uploads, "Movement bypassed the upload rate limit.");
            Set(widget, "_nextBake", -1f);
            Call(widget, "BakeIfNeeded");
            Require(texture.updateCount > uploads, "Movement did not update terrain when due.");
            uploads = texture.updateCount;
            // Keep an already unlocked bank present: the minimap must wait for the world reveal.
            Set(progress, "_completedMask", 1);
            Call(widget, "BakeIfNeeded");
            Require(!(bool)Call(widget, "IsMarkerVisible", positions[2]), "A dissolving bank exposed markers early.");
            Call(fog, "ReleaseBank", 2);
            Call(widget, "BakeIfNeeded");
            Require(texture.updateCount > uploads && (bool)Call(widget, "IsMarkerVisible", positions[2]), "Unlock failed to refresh immediately.");
            BakeAt(positions[2], (int)Call(widget, "FogMask"));
            Require(!FogPixel(raster.Texture.GetPixels32()[64 * 128 + 64]), "Opened B still has fog.");
            BakeAt(planet.Center + boundary * planet.Radius, (int)Call(widget, "FogMask"));
            File.WriteAllBytes(Output + "/boundary-open.png", raster.Texture.EncodeToPNG());
            Require(widget.GetComponentsInChildren<Image>(true).Length == 44, "Enemy pool was not created exactly once.");
            report.AppendLine("PASS: hidden enemy markers, reveal completion, immediate unlock refresh, idle skip, movement throttle, 36 pooled enemies.");

            BakeAt(positions[1], 28); // Warm caches before allocation measurement.
            var watch = new System.Diagnostics.Stopwatch();
            long before = GC.GetAllocatedBytesForCurrentThread();
            watch.Start();
            for (int i = 0; i < 30; i++) BakeAt(positions[1], 28);
            watch.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(allocated == 0, "Terrain bake allocated " + allocated + " bytes.");
            report.AppendLine($"PASS: 128x128 raster, {watch.Elapsed.TotalMilliseconds / 30:F2} ms/bake in Editor, {allocated} managed bytes over 30 warm bakes; capped at 10 Hz while moving.");
            Require(saveBefore == PlayerPrefs.GetString("BackHome.Stages.v1.ChapterOne", "")
                && sceneBefore == File.ReadAllText("Assets/Scenes/Galaxy.unity") && planetBefore == File.ReadAllText(PlanetPath), "Scene, planet or player save changed.");
            report.AppendLine("PASS: scene, production planet and player save unchanged.");
        }
        catch (Exception error) { report.AppendLine("FAIL: " + error); Debug.LogException(error); }
        finally
        {
            if (fakePlayer != null) Object.DestroyImmediate(fakePlayer);
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            if (hud != null) PrefabUtility.UnloadPrefabContents(hud);
            if (planetRoot != null) PrefabUtility.UnloadPrefabContents(planetRoot);
            raster?.Dispose();
            File.WriteAllText(Output + "/validation.txt", report.ToString());
            Debug.Log(report.ToString());
        }
    }

    static void Capture(Camera camera, string name, int width, int height)
    {
        var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            Color32[] pixels = texture.GetPixels32();
            int painted = 0;
            Color32 background = pixels[0];
            foreach (Color32 pixel in pixels) if (!pixel.Equals(background)) painted++;
            Require(painted > 1000, "HUD capture is blank: " + name);
            File.WriteAllBytes(Output + "/" + name + ".png", texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(texture);
        }
    }
}
