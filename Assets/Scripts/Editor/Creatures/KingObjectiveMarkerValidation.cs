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
        var spawner = Object.FindAnyObjectByType<CreatureSpawner>();
        if (spawner.TryGetCurrentKingObjective(out var target)) Frame(target);
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
            Kill(first, safeStats);
            Tick(marker);
            Require(marker.Target != null && marker.Target.name.StartsWith("KingA2_"), "Marker did not advance to KingA2.");
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
            Capture("Logs/KingObjectiveMarker-Next.png");
            Log("PASS stage unlock: B1 stays hidden while covered and appears after fog clears");

            string[] remaining = { "KingB1", "KingB2", "KingB3", "KingC1", "KingC2", "KingC3", "KingD1", "KingD2" };
            foreach (string area in remaining)
            {
                Require(spawner.TryGetCurrentKingObjective(out var king) && king.name.StartsWith(area + "_"), "Wrong objective: " + area);
                PlanetEnvironmentStreamer.SetCinematicVisible(king.gameObject, true);
                Kill(king, safeStats);
                fog.Rebuild();
                Tick(marker);
            }
            Require(marker.Target == null && !marker.IsVisible, "Final victory left a marker.");
            var liveKings = (IList)typeof(CreatureSpawner).GetField("_kings", Private).GetValue(spawner);
            foreach (object live in liveKings)
                live.GetType().GetField("readyAt").SetValue(live, Time.time - 1f);
            typeof(CreatureSpawner).GetMethod("Update", Private).Invoke(spawner, null);
            Require(spawner.TryGetKingForPresentation("KingA1", out var respawn) && respawn.IsAlive, "Respawn probe did not spawn a king.");
            marker.Bind(spawner);
            Tick(marker);
            Require(marker.Target == null && !marker.IsVisible, "A defeated king's respawn regained the marker.");
            Require(Object.FindObjectsByType<KingObjectiveMarker>().Length == 1, "Transitions duplicated the marker.");
            Log("PASS all stages completed; respawned defeated kings stay unmarked; no duplicate markers");
            Log("PASS all objective marker checks");
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
