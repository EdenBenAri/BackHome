using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Opt-in live reveal/lifecycle regression. Restores scene settings and never saves progress.</summary>
[InitializeOnLoad]
public static class PlanetStageFogPlayValidation
{
    const string Key = "BackHome.StageFogProbe";
    const string Trigger = "Temp/BackHomeStageFogPlay.trigger";
    public const string Report = "Logs/StageFog/play-results.txt";
    static PlanetStageFogPlayValidation() => EditorApplication.update += Poll;

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(Key + ".restore", false))
        {
            var progress = Object.FindAnyObjectByType<PlanetStageProgression>(FindObjectsInactive.Include);
            if (progress != null)
            {
                var serialized = new SerializedObject(progress);
                serialized.FindProperty("persistProgress").boolValue = SessionState.GetBool(Key + ".persist", true);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            SessionState.SetBool(Key + ".restore", false);
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
        }
        if (File.Exists(Trigger) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(Trigger);
            Run();
        }
        if (!SessionState.GetBool(Key, false)) return;
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + ".deadline", 0f)) Finish("FAIL: timeout.");
        else if (EditorApplication.isPlaying && Object.FindAnyObjectByType<StageFogProbeRunner>() == null)
        {
            Application.runInBackground = true;
            new GameObject("Temporary fog play probe").AddComponent<StageFogProbeRunner>();
        }
    }

    [MenuItem("BackHome/ChapterOne/Validate Stage Fog In Play")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var progress = Object.FindAnyObjectByType<PlanetStageProgression>(FindObjectsInactive.Include);
        if (progress == null) throw new InvalidOperationException("Open Galaxy first.");
        var serialized = new SerializedObject(progress);
        SessionState.SetBool(Key + ".persist", serialized.FindProperty("persistProgress").boolValue);
        SessionState.SetBool(Key + ".background", Application.runInBackground);
        serialized.FindProperty("persistProgress").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        SessionState.SetBool(Key + ".restore", true);
        SessionState.SetBool(Key, true);
        SessionState.SetFloat(Key + ".deadline", (float)EditorApplication.timeSinceStartup + 80f);
        Directory.CreateDirectory("Logs/StageFog");
        File.WriteAllText(Report, "Live stage fog reveal/lifecycle check; isolated unsaved progress.\n");
        EditorApplication.isPlaying = true;
    }

    public static void Finish(string result)
    {
        File.AppendAllText(Report, result + "\n");
        SessionState.SetBool(Key, false);
        EditorApplication.isPlaying = false;
    }
}

public sealed class StageFogProbeRunner : MonoBehaviour
{
    PlanetStageFog _fog;
    PlanetStageProgression _progress;
    GameObject _capture;
    Material _oldMaterial;
    Mesh _oldMesh;
    Vector3 _bankProbe;
    float _elapsed;
    int _phase;
    string _save;

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    void Update()
    {
        try { Step(); }
        catch (Exception error) { PlanetStageFogPlayValidation.Finish("FAIL: " + error); }
    }

    void Step()
    {
        // Cloud removal keeps real-time timing through victory slow motion and freezes.
        _elapsed += Time.unscaledDeltaTime;
        if (_phase == 0 && _elapsed > 1.5f)
        {
            _fog = Object.FindAnyObjectByType<PlanetStageFog>();
            Require(_fog != null && _fog.BuiltChunkCount > 0, "Runtime prefab did not initialize clouds.");
            _progress = _fog.GetComponent<PlanetStageProgression>();
            _save = PlayerPrefs.GetString(_progress.SaveKey, "");
            Require(_progress.HighestUnlocked == PlanetStageId.A, "Probe progress is not isolated.");
            Transform bank = Bank(PlanetStageId.B);
            Require(bank != null && Bank(PlanetStageId.C) != null && Bank(PlanetStageId.D) != null, "Missing locked bank.");
            _oldMaterial = bank.GetComponentInChildren<MeshRenderer>().sharedMaterial;
            _oldMesh = bank.GetComponentInChildren<MeshFilter>().sharedMesh;
            _bankProbe = FindBankInterior();
            Require(_fog.GetMovementClearance(_bankProbe, .6f) < 0f, "Closed bank has no movement boundary.");
            CaptureBoundary();
            File.AppendAllText(PlanetStageFogPlayValidation.Report,
                $"PASS: live prefab built {_fog.BuiltChunkCount} chunks, {_fog.BuiltVertexCount} vertices; captured locked boundary.\n");
            var required = (Dictionary<PlanetStageId, HashSet<string>>)typeof(PlanetStageProgression)
                .GetField("_requiredBosses", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_progress);
            Require(required.TryGetValue(PlanetStageId.A, out var bosses) && bosses.Count > 0, "No configured stage A bosses.");
            foreach (string boss in bosses) _progress.RecordBossDefeat(PlanetStageId.A, boss.Substring(boss.IndexOf(':') + 1));
            Require(_progress.IsUnlocked(PlanetStageId.B), "Production boss progression did not unlock B.");
            Require(Bank(PlanetStageId.B) != null, "Cloud reveal skipped animation.");
            Require(_fog.GetMovementClearance(_bankProbe, .6f) < 0f, "Movement boundary disappeared before the cloud reveal.");
            _elapsed = 0; _phase = 1;
        }
        else if (_phase == 1 && _elapsed > .35f)
        {
            float reveal = _oldMaterial.GetFloat("_Reveal");
            Require(reveal > 0f && reveal < 1f, "Reveal did not animate the material.");
            SaveCapture("boundary-revealing");
            _phase = 2;
        }
        else if (_phase == 2 && _elapsed > 1.3f)
        {
            Require(Bank(PlanetStageId.B) == null && _oldMaterial == null && _oldMesh == null,
                "Reveal left cloud objects, meshes or material allocated.");
            Require(Bank(PlanetStageId.C) != null && Bank(PlanetStageId.D) != null, "Unlock affected a later stage.");
            Require(_fog.GetMovementClearance(_bankProbe, .6f) >= 0f, "Revealed bank kept an invisible movement boundary.");
            SaveCapture("boundary-open");
            _fog.enabled = false;
            _elapsed = 0; _phase = 3;
        }
        else if (_phase == 3 && _elapsed > .15f)
        {
            Require(Bank(PlanetStageId.C) == null && Bank(PlanetStageId.D) == null, "Disable did not clean up clouds.");
            _fog.enabled = true;
            Require(Bank(PlanetStageId.B) == null && Bank(PlanetStageId.C) != null && Bank(PlanetStageId.D) != null,
                "Re-enable ignored current progress.");
            _progress.RegisterBoss(PlanetStageId.B, "fog-probe");
            _progress.RecordBossDefeat(PlanetStageId.B, "fog-probe");
            _elapsed = 0; _phase = 4;
        }
        else if (_phase == 4 && _elapsed > .2f)
        {
            _fog.enabled = false; // Interrupt an active reveal, including its coroutine.
            _elapsed = 0; _phase = 5;
        }
        else if (_phase == 5 && _elapsed > .15f)
        {
            _fog.enabled = true;
            Require(Bank(PlanetStageId.C) == null && Bank(PlanetStageId.D) != null, "Interrupted reveal restored a completed stage.");
            Require(PlayerPrefs.GetString(_progress.SaveKey, "") == _save, "Probe changed the player save.");
            PlanetStageFogPlayValidation.Finish("PASS: animated boss unlock, fog boundary retained during reveal and released afterwards, GPU captures, mesh/material destruction, disable/re-enable, interrupted reveal, save unchanged.");
        }
    }

    Transform Bank(PlanetStageId stage) => _fog.transform.Find("Locked Stage " + stage + " Clouds");

    Vector3 FindBankInterior()
    {
        var map = _fog.GetComponent<PlanetTileMap>();
        for (int lat = 2; lat < map.LatitudeBands - 2; lat++)
        for (int lon = 0; lon < map.LongitudeBands; lon++)
        {
            if (map.GetHeight(lat, lon) <= 0f) continue;
            bool interior = true;
            for (int y = -2; y <= 2 && interior; y++)
            for (int x = -2; x <= 2; x++)
                if (_progress.StageAtCell(lat + y, (lon + x + map.LongitudeBands) % map.LongitudeBands) != PlanetStageId.B)
                { interior = false; break; }
            if (interior && map.TryGetCellCenter(lat, lon, out Vector3 point)) return point;
        }
        throw new InvalidOperationException("No stage B interior for the reveal movement check.");
    }

    void CaptureBoundary()
    {
        var planet = _fog.GetComponent<SphericalPlanet>();
        var map = _fog.GetComponent<PlanetTileMap>();
        Vector3 open = default, locked = default;
        bool found = false;
        // Find an A/B boundary well inside the land, independent of scene camera framing.
        for (int lat = 1; lat < map.LatitudeBands - 1 && !found; lat++)
        for (int lon = 0; lon < map.LongitudeBands && !found; lon++)
        {
            if (_progress.StageAtCell(lat, lon) != PlanetStageId.A || map.GetHeight(lat, lon) <= 0) continue;
            for (int axis = 0; axis < 2 && !found; axis++)
            {
                int nextLat = lat + axis, nextLon = (lon + 1 - axis) % map.LongitudeBands;
                if (_progress.StageAtCell(nextLat, nextLon) != PlanetStageId.B || map.GetHeight(nextLat, nextLon) <= 0) continue;
                map.TryGetCellCenter(lat, lon, out open);
                map.TryGetCellCenter(nextLat, nextLon, out locked);
                found = true;
            }
        }
        Require(found, "No A/B land boundary found.");
        _capture = new GameObject("Temporary cloud boundary camera", typeof(Camera));
        Camera camera = _capture.GetComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.enabled = false;
        camera.fieldOfView = 48;
        Vector3 up = (open - planet.Center).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(locked - open, up).normalized;
        camera.transform.position = open + up * 19f - forward * 19f;
        camera.transform.rotation = Quaternion.LookRotation(open + forward * 5f - camera.transform.position, up);
        var data = camera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false;
        data.volumeLayerMask = 0;
        SaveCapture("boundary-locked");
    }

    void SaveCapture(string name)
    {
        var target = RenderTexture.GetTemporary(720, 1280, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var image = new Texture2D(720, 1280, TextureFormat.RGB24, false);
        try
        {
            RenderPipeline.SubmitRenderRequest(_capture.GetComponent<Camera>(),
                new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 720, 1280), 0, 0);
            image.Apply();
            File.WriteAllBytes("Logs/StageFog/" + name + ".png", image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.Destroy(image); }
    }

    void OnDestroy() { if (_capture != null) Object.Destroy(_capture); }
}
