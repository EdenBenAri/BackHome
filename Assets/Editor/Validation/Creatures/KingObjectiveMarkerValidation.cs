using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Runs inside PlanetStartupValidation's isolated play session, with zero XP/loot.</summary>
public static class KingObjectiveMarkerValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Report = "Logs/KingObjectiveMarkerValidation.txt";

    [MenuItem("BackHome/Tutorial/Validate King Objective Marker In Play")]
    public static void Request() => File.WriteAllText("Temp/BackHomePlanetStartup.trigger", "king-objective");

    public static void Prepare()
    {
        foreach (var director in Object.FindObjectsByType<KingCinematicDirector>()) director.enabled = false;
        foreach (var follow in Object.FindObjectsByType<CameraFollow>()) follow.enabled = false;
        foreach (var king in Object.FindObjectsByType<AreaKing>(FindObjectsInactive.Include))
            PlanetEnvironmentStreamer.SetCinematicVisible(king.gameObject, true);
        var player = Object.FindAnyObjectByType<PlayerVitals>();
        var camera = Camera.main;
        Vector3 up = SphericalPlanet.Instance.GetUpAt(player.transform.position);
        Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, up).normalized;
        if (forward.sqrMagnitude < .01f) forward = player.transform.forward;
        camera.transform.SetPositionAndRotation(player.transform.position + up * 24f - forward * 8f,
            Quaternion.LookRotation(forward * 8f - up * 24f, up));
        ScreenCapture.CaptureScreenshot("Logs/KingArrowNavigation-Gameplay.png");
    }

    public static void Run()
    {
        File.WriteAllText(Report, "King objective marker: live chapter validation\n");
        var spawner = Object.FindAnyObjectByType<CreatureSpawner>();
        var marker = Object.FindAnyObjectByType<KingObjectiveMarker>();
        var fog = Object.FindAnyObjectByType<PlanetStageFog>();
        var safeStats = new List<CreatureStats>();
        try
        {
            Require(Object.FindObjectsByType<KingObjectiveMarker>().Length == 1, "Expected exactly one marker.");
            Require(spawner.TryGetCurrentKingObjective(out var first), "Missing initial objective.");
            CheckArrow(first);
            CheckArrowDirections(first);
            Frame(first);
            Tick(marker);
            Require(marker.Target == first && first.name.StartsWith("KingA1_") && marker.IsVisible, "First objective is not marked.");
            Require(Quaternion.Angle(marker.transform.rotation, Camera.main.transform.rotation) < .1f, "Marker is not camera-facing.");
            var mobs = new List<Renderer>(); var kings = new List<Renderer>(); var players = new List<Renderer>();
            var objectives = new List<Renderer>();
            ToonOutlineSubjects.Copy(mobs, kings, players, objectives);
            foreach (var renderer in marker.GetComponentsInChildren<Renderer>())
                Require(objectives.Contains(renderer) && !mobs.Contains(renderer) && !kings.Contains(renderer),
                    "Marker did not receive its independent black outline.");
            Capture("Logs/KingObjectiveMarker-First.png");
            Log("PASS one marker on KingA1; imported model/texture, black outline and camera-facing pose");

            first.gameObject.SetActive(false);
            CheckArrow(first);
            Tick(marker);
            Require(!marker.IsVisible && marker.Target == first, "Streaming selected another king or left the mark visible.");
            first.gameObject.SetActive(true);
            Tick(marker);
            Require(marker.IsVisible, "Marker did not recover after streaming.");
            marker.enabled = false;
            Require(!marker.IsVisible, "Disabled marker remained visible.");
            marker.enabled = true;
            Tick(marker);
            Require(marker.IsVisible, "Re-enabled marker was lost.");
            Log("PASS streaming and component disable/re-enable visibility");

            Require(spawner.TryGetKingForPresentation("KingA3", out var third), "Missing out-of-order king.");
            Kill(third, safeStats);
            Tick(marker);
            Require(marker.Target == first, "Out-of-order victory skipped the current objective.");
            CheckArrow(first);
            Kill(first, safeStats);
            Tick(marker);
            Require(marker.Target != null && marker.Target.name.StartsWith("KingA2_"), "Marker did not advance to KingA2.");
            CheckArrow(marker.Target);
            Log("PASS real deaths: out-of-order KingA3 leaves A1 selected; A1 advances to A2");
            Kill(marker.Target, safeStats);
            Require(spawner.TryGetCurrentKingObjective(out var next), "Stage B did not produce an objective.");
            PlanetEnvironmentStreamer.SetCinematicVisible(next.gameObject, true);
            Frame(next);
            Tick(marker);
            Require(next.name.StartsWith("KingB1_") && marker.Target == next && !marker.IsVisible,
                "Next objective must wait under its dissolving fog bank.");
            fog.Rebuild();
            Tick(marker);
            Require(marker.IsVisible, "Cleared fog did not reveal the marker.");
            CheckArrow(next);
            Capture("Logs/KingObjectiveMarker-Next.png");
            Log("PASS stage unlock: B1 stays hidden while covered and appears after fog clears");

            string[] remaining = { "KingB1", "KingB2", "KingB3", "KingC1", "KingC2", "KingC3", "KingD1", "KingD2" };
            foreach (string area in remaining)
            {
                Require(spawner.TryGetCurrentKingObjective(out var king) && king.name.StartsWith(area + "_"), "Wrong objective: " + area);
                CheckArrow(king);
                PlanetEnvironmentStreamer.SetCinematicVisible(king.gameObject, true);
                Kill(king, safeStats);
                fog.Rebuild();
                Tick(marker);
            }
            Require(marker.Target == null && !marker.IsVisible, "Final victory left a marker.");
            CheckArrow(null);
            var liveKings = (IList)typeof(CreatureSpawner).GetField("_kings", Private).GetValue(spawner);
            foreach (object live in liveKings)
                live.GetType().GetField("readyAt").SetValue(live, Time.time - 1f);
            typeof(CreatureSpawner).GetMethod("Update", Private).Invoke(spawner, null);
            Require(spawner.TryGetKingForPresentation("KingA1", out var respawn) && respawn.IsAlive, "Respawn probe did not spawn a king.");
            marker.Bind(spawner);
            Tick(marker);
            Require(marker.Target == null && !marker.IsVisible, "A defeated king's respawn regained the marker.");
            CheckArrow(null);
            Require(Object.FindObjectsByType<KingObjectiveMarker>().Length == 1, "Transitions duplicated the marker.");
            Log("PASS all stages completed; respawned defeated kings stay unmarked; no duplicate markers");
            Log("PASS all objective marker checks");
            Log("PASS 3D king arrow: streamed targets, ordered progression, stage unlocks, completion and defeated respawns");
        }
        catch (Exception ex) { Log("FAIL " + ex); throw; }
        finally { foreach (var stats in safeStats) Object.Destroy(stats); }
    }

    static void Kill(Creature king, List<CreatureStats> copies)
    {
        var stats = Object.Instantiate(king.Stats);
        copies.Add(stats);
        typeof(CreatureStats).GetField("experience", Private).SetValue(stats, 0);
        typeof(CreatureStats).GetField("loot", Private).SetValue(stats, Array.Empty<LootEntry>());
        king.SetStats(stats, false);
        king.TakeDamage(king.MaxHealth + 1f);
    }

    static void CheckArrow(Creature expected)
    {
        var arrows = Object.FindObjectsByType<KingArrowNavigation>();
        Require(arrows.Length == 1, "Expected exactly one king navigation arrow.");
        var arrow = arrows[0];
        var camera = Camera.main;
        Vector3 position = camera.transform.position;
        Quaternion rotation = camera.transform.rotation;
        try
        {
            FrameArrowPlayer();
            // Resolve through the same death/progression events as ordinary gameplay.
            typeof(KingArrowNavigation).GetMethod("LateUpdate", Private).Invoke(arrow, null);
            Require(arrow.Target == expected, "Arrow and current king objective disagree.");
            Require(arrow.IsVisible == (expected != null), "Wrong arrow visibility for current objective.");
        }
        finally { camera.transform.SetPositionAndRotation(position, rotation); }
    }

    static void FrameArrowPlayer()
    {
        Transform player = Object.FindAnyObjectByType<PlayerVitals>().transform;
        Vector3 up = SphericalPlanet.Instance.GetUpAt(player.position);
        Camera.main.transform.SetPositionAndRotation(player.position + up * 24f,
            Quaternion.LookRotation(-up, player.forward));
    }

    static void CheckArrowDirections(Creature king)
    {
        var arrow = Object.FindAnyObjectByType<KingArrowNavigation>();
        var mesh = arrow.GetComponentInChildren<MeshFilter>();
        Require(mesh != null && AssetDatabase.GetAssetPath(mesh.sharedMesh) == KingArrowNavigationAuthoring.ModelPath,
            "Arrow must use the original ArrowMark model.");
        var renderer = mesh.GetComponent<Renderer>();
        Require(AssetDatabase.GetAssetPath(renderer.sharedMaterial) == KingArrowNavigationAuthoring.MaterialPath,
            "Arrow must use YellowMark.");
        Require(arrow.GetComponentInChildren<Canvas>(true) == null && arrow.GetComponentInChildren<Collider>(true) == null,
            "The 3D arrow should have no screen overlay or gameplay collider.");
        var mobs = new List<Renderer>(); var kings = new List<Renderer>(); var players = new List<Renderer>();
        var objectives = new List<Renderer>();
        ToonOutlineSubjects.Copy(mobs, kings, players, objectives);
        Require(objectives.Contains(renderer), "The 3D arrow did not receive its black objective outline.");
        Bounds local = mesh.sharedMesh.bounds;
        Bounds fitted = new(arrow.transform.InverseTransformPoint(mesh.transform.TransformPoint(local.center)), Vector3.zero);
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
            fitted.Encapsulate(arrow.transform.InverseTransformPoint(mesh.transform.TransformPoint(
                local.center + Vector3.Scale(local.extents, new Vector3(x, y, z)))));
        Require(Mathf.Abs(fitted.size.z - 1f) < .01f && fitted.size.y < .2f && Mathf.Abs(fitted.min.y) < .01f,
            "ArrowMark is not normalized flat in XZ with its underside at the origin: " + fitted);
        var player = Object.FindAnyObjectByType<PlayerVitals>();
        var camera = Camera.main;
        Vector3 cameraPosition = camera.transform.position, kingPosition = king.transform.position;
        Quaternion cameraRotation = camera.transform.rotation;
        Vector3 playerPosition = player.transform.position;
        Quaternion playerRotation = player.transform.rotation;
        float health = player.CurrentHealth;
        try
        {
            FrameArrowPlayer();
            Vector2[] bearings = { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
            foreach (Vector2 expected in bearings)
            {
                king.transform.position = player.transform.position
                    + (camera.transform.right * expected.x + camera.transform.up * expected.y) * 20f;
                arrow.enabled = false;
                Require(!arrow.IsVisible, "Disabled arrow remained visible.");
                arrow.enabled = true;
                typeof(KingArrowNavigation).GetMethod("LateUpdate", Private).Invoke(arrow, null);
                Vector3 desired = camera.transform.right * expected.x + camera.transform.up * expected.y;
                Require(arrow.IsVisible && Vector3.Dot(arrow.transform.forward, desired) > .995f,
                    "Arrow points the wrong way after a turn.");
                CheckArrowGroundPose(arrow);
            }
            // On the opposite hemisphere the surface route still starts to the right.
            Vector3 center = SphericalPlanet.Instance.transform.position;
            king.transform.position = center - (player.transform.position - center) + camera.transform.right * 10f;
            arrow.enabled = false;
            arrow.enabled = true;
            typeof(KingArrowNavigation).GetMethod("LateUpdate", Private).Invoke(arrow, null);
            Require(arrow.IsVisible && Vector3.Dot(arrow.transform.forward, camera.transform.right) > .995f,
                "Far-hemisphere objective reversed the arrow.");
            Vector3 arrowPosition = arrow.transform.position;
            Quaternion arrowRotation = arrow.transform.rotation;
            camera.transform.Rotate(0f, 60f, 0f);
            typeof(KingArrowNavigation).GetMethod("LateUpdate", Private).Invoke(arrow, null);
            Require(Vector3.Distance(arrow.transform.position, arrowPosition) < .001f
                && Quaternion.Angle(arrow.transform.rotation, arrowRotation) < .01f,
                "The 3D arrow follows the camera instead of the ground.");
            player.transform.position = center - (playerPosition - center);
            Vector3 newUp = SphericalPlanet.Instance.GetUpAt(player.transform.position);
            player.transform.rotation = Quaternion.LookRotation(player.transform.forward, newUp);
            FrameArrowPlayer();
            king.transform.position = player.transform.position + camera.transform.right * 20f;
            arrow.enabled = false;
            arrow.enabled = true;
            typeof(KingArrowNavigation).GetMethod("LateUpdate", Private).Invoke(arrow, null);
            CheckArrowGroundPose(arrow);
            Require(Vector3.Dot(arrow.transform.up, SphericalPlanet.Instance.GetUpAt(playerPosition)) < -.99f,
                "Arrow stayed aligned to world-up on the opposite side of the planet.");
            typeof(PlayerVitals).GetField("_currentHealth", Private).SetValue(player, 0f);
            typeof(KingArrowNavigation).GetMethod("LateUpdate", Private).Invoke(arrow, null);
            Require(!arrow.IsVisible, "Dead player retained the navigation arrow.");
            Log("PASS ArrowMark mesh and YellowMark material; flat ground pose, four bearings, curved planet, camera independence, outline and player death");
        }
        finally
        {
            typeof(PlayerVitals).GetField("_currentHealth", Private).SetValue(player, health);
            king.transform.position = kingPosition;
            player.transform.SetPositionAndRotation(playerPosition, playerRotation);
            camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
        }
    }

    static void CheckArrowGroundPose(KingArrowNavigation arrow)
    {
        var planet = SphericalPlanet.Instance;
        var tiles = planet.GetComponent<PlanetTileMap>();
        Vector3 radial = planet.GetUpAt(arrow.transform.position);
        Vector3 surface = tiles != null && tiles.ProvidesWalkSurface
            ? tiles.GetWalkSurfacePoint(radial) : planet.GetSurfacePoint(radial);
        Vector3 normal = tiles != null && tiles.ProvidesWalkSurface
            ? tiles.GetWalkSurfaceNormal(radial) : planet.GetTerrainNormal(radial);
        Require(Vector3.Dot(arrow.transform.up, normal) > .999f, "Arrow is not parallel to the ground.");
        Require(Vector3.Dot(arrow.transform.position - surface, normal) > .1f,
            "Arrow is buried in the terrain.");
    }

    static void Frame(Creature king)
    {
        var camera = Camera.main;
        Vector3 up = king.transform.up;
        Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, up).normalized;
        if (forward.sqrMagnitude < .01f) forward = king.transform.forward;
        Vector3 focus = king.transform.position + up * 2f;
        camera.transform.SetPositionAndRotation(focus + up * 15f - forward * 13f,
            Quaternion.LookRotation(forward * 13f - up * 15f, up));
    }

    static void Capture(string path)
    {
        var go = new GameObject("Temporary objective preview");
        var camera = go.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.enabled = false;
        camera.transform.SetPositionAndRotation(Camera.main.transform.position, Camera.main.transform.rotation);
        camera.cullingMask &= ~LayerMask.GetMask("UI");
        camera.aspect = 1f;
        var target = RenderTexture.GetTemporary(1024, 1024, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var image = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
        try
        {
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(go);
        }
    }

    static void Tick(KingObjectiveMarker marker) => typeof(KingObjectiveMarker).GetMethod("LateUpdate", Private).Invoke(marker, null);
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Log(string message) => File.AppendAllText(Report, message + "\n");
}
