using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Opt-in startup smoke test of the open Galaxy scene, preserving saved progression.</summary>
[InitializeOnLoad]
public static class PlanetStartupValidation
{
    const string Key = "BackHome.PlanetStartupValidation";
    const string Trigger = "Temp/BackHomePlanetStartup.trigger";
    const string Report = "Logs/PlanetStartupValidation.txt";

    static PlanetStartupValidation()
    {
        EditorApplication.update += Poll;
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert
                || message.Contains("no spawn pose resolved") || message.Contains("no SphericalPlanet assigned"))
            {
                SessionState.SetBool(Key + ".error", true);
                File.AppendAllText(Report, "ERROR " + message + "\n");
            }
        };
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ProtectPlayer()
    {
        if (!SessionState.GetBool(Key, false)) return;
        var player = Object.FindAnyObjectByType<PlayerVitals>();
        if (player == null) return;
        player.SetInvulnerable(true);
        var combat = player.GetComponent<PlayerRangeCombat>();
        if (combat != null) combat.enabled = false;
        var input = player.GetComponent<StarterAssets.StarterAssetsInputs>();
        if (input != null) input.MoveInput(Vector2.zero);
        SessionState.SetFloat(Key + ".started", (float)EditorApplication.timeSinceStartup);
    }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(Key + ".restore", false))
        {
            var progress = Object.FindAnyObjectByType<PlanetStageProgression>(FindObjectsInactive.Include);
            if (progress != null) SetPersistence(progress, SessionState.GetBool(Key + ".persist", true));
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
            SessionState.SetBool(Key + ".restore", false);
        }
        if (File.Exists(Trigger) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            SessionState.SetBool(Key + ".kingRing", File.ReadAllText(Trigger).Trim() == "king-ring");
            SessionState.SetBool(Key + ".kingObjective", File.ReadAllText(Trigger).Trim() == "king-objective");
            SessionState.SetBool(Key + ".kingRingPrepared", false);
            SessionState.SetBool(Key + ".kingObjectivePrepared", false);
            File.Delete(Trigger);
            var progress = Object.FindAnyObjectByType<PlanetStageProgression>(FindObjectsInactive.Include);
            if (progress == null) throw new InvalidOperationException("Open Galaxy before the startup check.");
            SessionState.SetBool(Key + ".persist", new SerializedObject(progress).FindProperty("persistProgress").boolValue);
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            SessionState.SetBool(Key + ".restore", true);
            SetPersistence(progress, false);
            Application.runInBackground = true;
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Report, "Live Galaxy startup; no combat or saved progression changes.\n");
            SessionState.SetBool(Key + ".error", false);
            SessionState.SetBool(Key, true);
            SessionState.SetFloat(Key + ".started", (float)EditorApplication.timeSinceStartup);
            EditorApplication.isPlaying = true;
        }
        if (!SessionState.GetBool(Key, false)) return;
        float elapsed = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + ".started", 0f);
        if (elapsed > 30f) { Finish("FAIL startup timeout"); return; }
        if (EditorApplication.isPlaying && elapsed >= 3f && SessionState.GetBool(Key + ".kingObjective", false)
            && !SessionState.GetBool(Key + ".kingObjectivePrepared", false))
        {
            KingObjectiveMarkerValidation.Prepare();
            SessionState.SetBool(Key + ".kingObjectivePrepared", true);
            return;
        }
        if (EditorApplication.isPlaying && elapsed >= 3f && SessionState.GetBool(Key + ".kingRing", false)
            && !SessionState.GetBool(Key + ".kingRingPrepared", false))
        {
            KingLeashIndicatorValidation.Prepare();
            SessionState.SetBool(Key + ".kingRingPrepared", true);
            SessionState.SetFloat(Key + ".started", (float)EditorApplication.timeSinceStartup - 3f);
            return;
        }
        if (!EditorApplication.isPlaying || elapsed < 6f) return;
        try
        {
            var owner = Object.FindAnyObjectByType<PlanetAuthoring>();
            Require(owner != null && owner.IsProjected && owner.RuntimePlanet.isActiveAndEnabled, "World did not activate.");
            Require(owner.ValidateConfiguration(out string reason), reason);
            Require(SphericalPlanet.Instance == owner.RuntimePlanet, "No live spherical planet.");
            var player = Object.FindAnyObjectByType<PlayerVitals>();
            Require(player != null && player.IsAlive, "Player did not spawn.");
            Require(Creature.ActiveCreatures.Count > 0, "Creatures did not spawn.");
            if (SessionState.GetBool(Key + ".kingRing", false))
                KingLeashIndicatorValidation.Run();
            if (SessionState.GetBool(Key + ".kingObjective", false))
                KingObjectiveMarkerValidation.Run();
            Require(!SessionState.GetBool(Key + ".error", false), "Startup logged an error or missing-world warning.");
            Finish($"PASS planet active; {owner.ProjectedObjectCount} unique placements; player alive; {Creature.ActiveCreatures.Count} creatures spawned; no startup errors/warnings.");
        }
        catch (Exception error) { Finish("FAIL " + error); }
    }

    static void SetPersistence(PlanetStageProgression progress, bool persist)
    {
        var serialized = new SerializedObject(progress);
        serialized.FindProperty("persistProgress").boolValue = persist;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Finish(string result)
    {
        File.AppendAllText(Report, result + "\n");
        SessionState.SetBool(Key, false);
        EditorApplication.isPlaying = false;
    }

    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
