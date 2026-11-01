using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Opt-in isolated coverage, unlock, cleanup and real GPU rendering checks.</summary>
[InitializeOnLoad]
public static class PlanetStageFogValidation
{
    const string Output = "Logs/StageFog";
    const string Trigger = "Temp/BackHomeStageFogValidation.trigger";
    const string PrefabPath = "Assets/Resources/Galaxy/ChapterOne/PlanetChapterOne.prefab";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static PlanetStageFogValidation() => EditorApplication.update += Poll;

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Trigger)) return;
        File.Delete(Trigger);
        Run();
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [MenuItem("BackHome/ChapterOne/Validate Stage Fog")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play before validating fog.");
        Directory.CreateDirectory(Output);
        var report = new StringBuilder();
        string before = File.ReadAllText(PrefabPath);
        string sceneBefore = File.ReadAllText("Assets/Scenes/Galaxy.unity");
        string saveBefore = PlayerPrefs.GetString("BackHome.Stages.v1.ChapterOne", "");
        GameObject fixture = null, cameraObject = null;
        try
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Resources/Galaxy/Shaders/StageCloud.shader");
            Require(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Cloud shader compilation failed.");
            var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
            foreach (var platform in new[] { ShaderCompilerPlatform.GLES3x, ShaderCompilerPlatform.Vulkan, ShaderCompilerPlatform.Metal })
            {
                BuildTarget target = platform == ShaderCompilerPlatform.Metal ? BuildTarget.iOS : BuildTarget.Android;
                var variant = pass.CompileVariant(ShaderType.Vertex, Array.Empty<string>(), platform, target);
                Require(variant.Success && variant.ShaderData.Length > 0, "Mobile shader compilation failed: " + platform);
                if (platform == ShaderCompilerPlatform.Metal)
                {
                    var fragment = pass.CompileVariant(ShaderType.Fragment, Array.Empty<string>(), platform, target);
                    Require(fragment.Success && fragment.ShaderData.Length > 0, "Metal fragment compilation failed.");
                }
            }
            report.AppendLine("PASS: GLES3, Vulkan and Metal shader compilation (Android/iOS targets).");
            fixture = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            fixture.name = "Temporary stage fog validation";
            fixture.hideFlags = HideFlags.HideAndDontSave;
            fixture.transform.position = new Vector3(10000, 10000, 10000);
            var authoring = fixture.GetComponent<PlanetAuthoring>();
            var planet = authoring.RuntimePlanet;
            var map = planet.GetComponent<PlanetTileMap>();
            var progress = planet.GetComponent<PlanetStageProgression>();
            var fog = planet.GetComponent<PlanetStageFog>();
            Require(fog != null, "Production prefab has no fog component.");
            typeof(PlanetStageProgression).GetField("persistProgress", Private).SetValue(progress, false);
            foreach (var placement in authoring.AuthoringRoot.GetComponentsInChildren<PlanetPlacement>(true)) placement.Apply(authoring);
            authoring.AuthoringRoot.gameObject.SetActive(false);
            progress.Initialize();
            planet.gameObject.SetActive(true);
            foreach (Transform child in fixture.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
            // Edit-mode captures need the same stage visibility as the runtime streamer.
            foreach (var prop in planet.GetComponentsInChildren<PlanetEnvironmentObject>(true))
                if (!progress.IsUnlocked(progress.StageFor(prop.transform))) prop.gameObject.SetActive(false);

            var randomBefore = UnityEngine.Random.state;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            fog.Rebuild();
            watch.Stop();
            Require(UnityEngine.Random.state.Equals(randomBefore), "Fog changed gameplay random state.");
            int expected = 0;
            var perStage = new int[5];
            var stageCenter = new Vector3[5];
            for (int lat = 0; lat < map.LatitudeBands; lat++)
            for (int lon = 0; lon < map.LongitudeBands; lon++)
            {
                map.TryGetCellCenter(lat, lon, out Vector3 point);
                Require(progress.StageAt(point) == progress.StageAtCell(lat, lon), "Fog/gameplay ownership diverged.");
                if (map.GetHeight(lat, lon) <= PlanetTileMap.WaterHeight) continue;
                int stage = (int)progress.StageAtCell(lat, lon);
                perStage[stage]++;
                stageCenter[stage] += (point - planet.Center).normalized;
                if (!progress.IsUnlocked((PlanetStageId)stage)) expected++;
            }
            Require(fog.CoveredCellCount == expected && expected > 0, "Locked land coverage mismatch.");
            Require(fog.BuiltChunkCount < 64 && fog.BuiltVertexCount < 200000, "Production fog exceeds geometry budget.");
            int clouds = 0;
            long bytes = 0;
            foreach (Transform bank in planet.transform)
            {
                if (!bank.name.StartsWith("Locked Stage ")) continue;
                Require(bank.GetComponentsInChildren<Collider>().Length == 0, "Fog added colliders.");
                foreach (var renderer in bank.GetComponentsInChildren<MeshRenderer>())
                {
                    clouds++;
                    Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mesh);
                    Require(!mesh.isReadable && mesh.vertexCount < 65536, "Mesh CPU copy/index budget mismatch.");
                    Require(renderer.shadowCastingMode == ShadowCastingMode.Off && !renderer.receiveShadows
                        && renderer.sharedMaterial.passCount == 1 && renderer.sharedMaterial.renderQueue < 2500
                        && !renderer.HasPropertyBlock(), "Fog rendering contract changed.");
                }
            }
            report.AppendLine($"PASS: {expected} locked land cells (B {perStage[2]}, C {perStage[3]}, D {perStage[4]}); A and open ocean uncovered.");
            report.AppendLine($"PASS: {clouds} spatial chunks, {fog.BuiltVertexCount:N0} vertices, mesh memory {bytes / 1024f:N1} KiB; build {watch.Elapsed.TotalMilliseconds:F2} ms in Editor.");
            report.AppendLine("PASS: opaque single-pass, no colliders/shadows/property blocks, no readable CPU mesh copies; gameplay random state preserved.");

            cameraObject = new GameObject("Temporary fog camera", typeof(Camera)) { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f, .18f, .3f);
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 1000f;
            camera.fieldOfView = 43;
            camera.allowHDR = false;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.volumeLayerMask = 0;
            Vector3 direction = (stageCenter[1] + stageCenter[2] + stageCenter[3] + stageCenter[4]).normalized;
            Frame(camera, planet, direction, planet.Radius * 3.15f);
            Color32[] locked = Render(camera, "locked-overview", 1024, 1024);
            foreach (Transform bank in planet.transform)
                if (bank.name.StartsWith("Locked Stage ")) bank.gameObject.SetActive(false);
            Color32[] clear = Render(camera, "clear-overview", 1024, 1024);
            foreach (Transform bank in planet.transform)
                if (bank.name.StartsWith("Locked Stage ")) bank.gameObject.SetActive(true);
            int changed = 0;
            for (int i = 0; i < locked.Length; i++)
                if (Mathf.Abs(locked[i].r - clear[i].r) + Mathf.Abs(locked[i].g - clear[i].g)
                    + Mathf.Abs(locked[i].b - clear[i].b) > 35) changed++;
            Require(changed > 10000, "Fog did not cover terrain in GPU capture (check winding/material).");
            Vector3 close = stageCenter[2].normalized;
            Vector3 tangent = Vector3.ProjectOnPlane(Vector3.up, close).normalized;
            Vector3 lookAt = planet.Center + close * (planet.Radius + 1f);
            camera.transform.position = lookAt + close * 30f - tangent * 24f;
            camera.transform.rotation = Quaternion.LookRotation(lookAt - camera.transform.position, close);
            Render(camera, "clouds-close", 720, 1280);
            ValidateCrimsonBoundary(camera, planet, map, progress, report);
            Frame(camera, planet, direction, planet.Radius * 3.15f);

            int originalVertices = fog.BuiltVertexCount;
            fog.Rebuild();
            Require(fog.BuiltVertexCount == originalVertices, "Rebuild is not deterministic.");
            for (int stage = 1; stage <= 3; stage++)
            {
                progress.RegisterBoss((PlanetStageId)stage, "fog-test");
                progress.RecordBossDefeat((PlanetStageId)stage, "fog-test");
                Require(planet.transform.Find("Locked Stage " + (PlanetStageId)(stage + 1) + " Clouds") == null,
                    "Unlocked cloud resources not released.");
                if (stage == 1) Render(camera, "stage-b-open", 1024, 1024);
            }
            fog.Rebuild();
            Require(fog.BuiltChunkCount == 0 && fog.CoveredCellCount == 0, "A fully unlocked world recreated fog.");
            report.AppendLine($"PASS: GPU coverage ({changed:N0} changed pixels), portrait/overview captures, deterministic rebuild, ordered unlock cleanup and all-open reload.");
            ValidateFogMovement(planet, map, progress, fog, report);
            Require(!ShaderUtil.ShaderHasError(shader), "GPU shader compilation failed after rendering.");
            Require(File.ReadAllText(PrefabPath) == before && File.ReadAllText("Assets/Scenes/Galaxy.unity") == sceneBefore
                && PlayerPrefs.GetString("BackHome.Stages.v1.ChapterOne", "") == saveBefore, "Production assets or player save changed.");
            report.AppendLine("PASS: production files and player save unchanged. Device GPU timing still requires a target-device build.");
        }
        catch (Exception error) { report.AppendLine("FAIL: " + error); Debug.LogException(error); }
        finally
        {
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            if (fixture != null) Object.DestroyImmediate(fixture);
            File.WriteAllText(Output + "/results.txt", report.ToString());
            Debug.Log(report.ToString());
        }
    }

    static void Frame(Camera camera, SphericalPlanet planet, Vector3 direction, float distance)
    {
        camera.transform.position = planet.Center + direction * distance;
        camera.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
    }

    static void ValidateFogMovement(SphericalPlanet planet, PlanetTileMap map, PlanetStageProgression progress,
        PlanetStageFog fog, StringBuilder report)
    {
        // Reuse only the disposable fixture after the production-layout captures. Flat,
        // walkable grass isolates the fog boundary from dirt, props and unrelated walls.
        int middle = map.LatitudeBands / 2, columns = map.LongitudeBands;
        for (int row = 0; row < map.LatitudeBands; row++)
        for (int col = 0; col < columns; col++)
        {
            bool locked = Mathf.Abs(row - middle) <= 3 && (col >= 10 && col <= 20 || col == 0 || col == columns - 1);
            map.SetHeightSilent(row, col, PlanetTileMap.DefaultGroundHeight);
            map.SetTerrainSilent(row, col, locked ? PlanetTileset.TerrainGlowingMushroom : PlanetTileset.TerrainGrass);
        }
        typeof(PlanetStageProgression).GetMethod("BuildStageMap", Private).Invoke(progress, null);
        typeof(PlanetStageProgression).GetField("_completedMask", Private).SetValue(progress, 0);
        fog.Rebuild();
        Vector3 Cell(int row, int col) { map.TryGetCellCenter(row, col, out Vector3 point); return point; }
        const float body = .6f;
        Vector3 from = Cell(middle, 6), edge = Cell(middle, 9);
        Require(progress.IsPositionUnlocked(edge) && fog.GetMovementClearance(edge, body) < 0f,
            "Fixture does not reproduce fog extending into an unlocked cell.");
        Vector3 Stop(Vector3 start, Vector3 end)
        {
            Require(fog.GetMovementClearance(start, body) > 0f, "Movement fixture starts inside fog.");
            Vector3 stopped = progress.ConstrainMove(start, end, body);
            Require(Vector3.Distance(stopped, end) > .5f && Vector3.Distance(stopped, start) > .5f
                && fog.GetMovementClearance(stopped, body) >= -.0001f && progress.IsPositionUnlocked(stopped),
                "Movement entered the expanded fog or failed to approach its edge.");
            return stopped;
        }
        Vector3 stop = Stop(from, edge);
        Stop(Cell(middle, 24), Cell(middle, 21));
        Stop(Cell(middle - 7, 15), Cell(middle - 4, 15));
        Stop(Cell(middle + 7, 15), Cell(middle + 4, 15));
        Stop(Cell(middle - 7, 6), Cell(middle - 4, 9));
        Stop(from, Cell(middle, 25)); // Long step with accessible endpoints.
        Stop(Cell(middle, columns - 4), Cell(middle, 3)); // Longitude seam.

        Vector3 smallBody = progress.ConstrainMove(from, edge, .2f);
        Vector3 largeBody = progress.ConstrainMove(from, edge, 1.2f);
        Require(Vector3.Distance(from, largeBody) + .5f < Vector3.Distance(from, smallBody),
            "Cloud boundary ignored the player's body radius.");
        Vector3 escaped = progress.ConstrainMove(edge, from, body);
        Require(Vector3.Distance(escaped, from) < .01f, "A player already in the expanded edge cannot walk out.");
        Vector3 deeper = progress.ConstrainMove(edge, Cell(middle, 10), body);
        Require(Vector3.Distance(deeper, edge) < .01f, "An embedded player moved deeper into the fog.");
        Vector3 incremental = from;
        for (int i = 0; i < 250; i++)
        {
            Vector3 direction = Vector3.Slerp((incremental - planet.Center).normalized, (edge - planet.Center).normalized, .04f);
            Vector3 next = planet.Center + direction * (incremental - planet.Center).magnitude;
            incremental = progress.ConstrainMove(incremental, next, body);
        }
        Require(fog.GetMovementClearance(incremental, body) >= -.0001f && Vector3.Distance(incremental, stop) < .5f,
            "Repeated small moves crept into the cloud bank.");

        var padding = typeof(PlanetStageFog).GetField("edgePadding", Private);
        float originalPadding = (float)padding.GetValue(fog);
        padding.SetValue(fog, 1f);
        fog.Rebuild();
        Vector3 widerStop = progress.ConstrainMove(from, edge, body);
        Require(Vector3.Distance(from, widerStop) + .5f < Vector3.Distance(from, stop),
            "Rebuilding wider clouds did not move their movement boundary.");
        padding.SetValue(fog, originalPadding);
        fog.Rebuild();

        Vector3 shortMove = Vector3.Lerp(from, stop, .01f);
        for (int i = 0; i < 16; i++) progress.ConstrainMove(from, shortMove, body);
        var watch = new System.Diagnostics.Stopwatch();
        long before = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        for (int i = 0; i < 1000; i++) progress.ConstrainMove(from, shortMove, body);
        watch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, "Steady movement allocated managed memory: " + allocated);

        fog.enabled = false;
        Require(progress.ConstrainMove(from, edge, body) == edge, "Disabled fog retained its expanded boundary.");
        fog.enabled = true;
        fog.Rebuild();
        Stop(from, edge);
        progress.RegisterBoss(PlanetStageId.A, "fog-movement");
        progress.RecordBossDefeat(PlanetStageId.A, "fog-movement");
        Require(progress.IsUnlocked(PlanetStageId.B) && float.IsPositiveInfinity(fog.GetMovementClearance(edge, body))
            && progress.ConstrainMove(from, Cell(middle, 25), body) == Cell(middle, 25),
            "Opening a stage left an invisible fog wall behind.");
        report.AppendLine($"PASS: expanded fog blocks 7 approaches (diagonal, long step and seam), body clearance, small-step creep, escape, width rebuild, disable/re-enable and unlock; zero GC across 1,000 moves ({watch.Elapsed.TotalMilliseconds:F2} ms total in Editor).");
    }

    static void ValidateCrimsonBoundary(Camera camera, SphericalPlanet planet, PlanetTileMap map,
        PlanetStageProgression progress, StringBuilder report)
    {
        // The reported leak was at A/D, not the A/B boundary used by the lifecycle check.
        // Render terrain and fog alone so props/VFX cannot masquerade as exposed red ground.
        var renderers = planet.GetComponentsInChildren<Renderer>(true);
        var layers = new int[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            layers[i] = renderers[i].gameObject.layer;
            var filter = renderers[i].GetComponent<MeshFilter>();
            string mesh = filter != null && filter.sharedMesh != null ? filter.sharedMesh.name : "";
            if (mesh != "PlanetTiles_Heightfield" && mesh != "Stage cloud bank") renderers[i].gameObject.layer = 0;
        }
        int views = 0, worst = -1;
        Vector3 worstPosition = default;
        Quaternion worstRotation = default;
        try
        {
            for (int lat = 1; lat < map.LatitudeBands - 1; lat++)
            for (int lon = 0; lon < map.LongitudeBands; lon++)
            {
                if (progress.StageAtCell(lat, lon) != PlanetStageId.A || map.GetHeight(lat, lon) <= 0) continue;
                for (int side = 0; side < 4; side++)
                {
                    int nextLat = lat + (side == 0 ? -1 : side == 1 ? 1 : 0);
                    int nextLon = (lon + (side == 2 ? -1 : side == 3 ? 1 : 0) + map.LongitudeBands) % map.LongitudeBands;
                    if (progress.StageAtCell(nextLat, nextLon) != PlanetStageId.D || map.GetHeight(nextLat, nextLon) <= 0) continue;
                    map.TryGetCellCenter(lat, lon, out Vector3 open);
                    map.TryGetCellCenter(nextLat, nextLon, out Vector3 closed);
                    Vector3 up = (open - planet.Center).normalized;
                    Vector3 forward = Vector3.ProjectOnPlane(closed - open, up).normalized;
                    for (int angle = 0; angle < 2; angle++)
                    {
                        camera.transform.position = open + up * (angle == 0 ? 24f : 12f) - forward * (angle == 0 ? 8f : 16f);
                        camera.transform.rotation = Quaternion.LookRotation(open + forward * 4f - camera.transform.position, up);
                        int exposed = CountRed(Render(camera, null, 360, 640));
                        views++;
                        if (exposed <= worst) continue;
                        worst = exposed;
                        worstPosition = camera.transform.position;
                        worstRotation = camera.transform.rotation;
                    }
                }
            }
            Require(views > 0, "No A/D land boundary found for the red-edge regression.");
            camera.transform.SetPositionAndRotation(worstPosition, worstRotation);
            Render(camera, "crimson-boundary-locked", 720, 1280);
            Transform crimson = planet.transform.Find("Locked Stage D Clouds");
            crimson.gameObject.SetActive(false);
            int visibleWithoutFog = CountRed(Render(camera, "crimson-boundary-clear", 360, 640));
            crimson.gameObject.SetActive(true);
            Require(visibleWithoutFog > 200, "Crimson boundary capture did not exercise visible red terrain.");
            Require(worst <= 8, $"Crimson terrain still exposed: {worst} red pixels in the worst of {views} boundary views.");
            report.AppendLine($"PASS: {views} A/D boundary views at gameplay/low angles; worst {worst} exposed red pixels ({visibleWithoutFog} without fog).");
        }
        finally
        {
            for (int i = 0; i < renderers.Length; i++) renderers[i].gameObject.layer = layers[i];
        }
    }

    static int CountRed(Color32[] pixels)
    {
        int count = 0;
        foreach (Color32 pixel in pixels)
            // Sand is warm yellow too; require enough red/green separation to exclude it.
            if (pixel.r > 140 && pixel.r > pixel.g + 55 && pixel.r > pixel.b + 55) count++;
        return count;
    }

    static Color32[] Render(Camera camera, string name, int width, int height)
    {
        var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            if (name != null) File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
            return image.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(image);
        }
    }
}
