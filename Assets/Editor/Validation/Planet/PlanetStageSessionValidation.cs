using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Two opt-in Play sessions: three-boss gate, session saves and a fresh second Play.</summary>
[InitializeOnLoad]
public static class PlanetStageSessionValidation
{
    const string Key = "BackHome.StageSessionValidation";
    const string Trigger = "Temp/BackHomeStageSession.trigger";
    const string Report = "Logs/PlanetStageSession-validation.txt";
    static readonly string[] Bosses = { "KingA1", "KingA2", "KingA3" };

    static PlanetStageSessionValidation() => EditorApplication.update += Poll;

    [MenuItem("BackHome/ChapterOne/Validate Stage Play Sessions")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var owner = Object.FindAnyObjectByType<PlanetAuthoring>();
        if (owner == null || owner.gameObject.scene.path != PlanetAuthoringTools.ScenePath)
            throw new InvalidOperationException("Open the Galaxy scene before validating stage sessions.");
        var progress = owner.RuntimePlanet.GetComponent<PlanetStageProgression>();
        Require(new SerializedObject(progress).FindProperty("persistProgress").boolValue,
            "Enable Persist Progress to test the normal session save path.");
        SessionState.SetString(Key + ".saveKey", progress.SaveKey);
        SessionState.SetString(Key + ".save", PlayerPrefs.GetString(progress.SaveKey, ""));
        SessionState.SetBool(Key + ".background", Application.runInBackground);
        SessionState.SetFloat(Key + ".deadline", (float)EditorApplication.timeSinceStartup + 90f);
        SessionState.SetInt(Key + ".cycle", 1);
        Directory.CreateDirectory("Logs");
        File.WriteAllText(Report, "Two fresh Play sessions; persistent save is preserved.\n");
        Application.runInBackground = true;
        EditorApplication.isPlaying = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ProtectPlayer()
    {
        if (SessionState.GetInt(Key + ".cycle", 0) == 0) return;
        var player = Object.FindAnyObjectByType<PlayerVitals>();
        if (player != null)
        {
            player.SetInvulnerable(true);
            var combat = player.GetComponent<PlayerRangeCombat>();
            if (combat != null) combat.enabled = false;
            var input = player.GetComponent<StarterAssets.StarterAssetsInputs>();
            if (input != null) input.MoveInput(Vector2.zero);
        }
        SessionState.SetFloat(Key + ".started", (float)EditorApplication.timeSinceStartup);
    }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(Key + ".restore", false))
        {
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
            SessionState.SetBool(Key + ".restore", false);
        }
        if (File.Exists(Trigger) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(Trigger);
            Run();
        }
        int cycle = SessionState.GetInt(Key + ".cycle", 0);
        if (cycle == 0) return;
        try
        {
            Require(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + ".deadline", 0f), "Session test timed out.");
            if (!EditorApplication.isPlayingOrWillChangePlaymode && cycle == 2)
            {
                SessionState.SetInt(Key + ".cycle", 3);
                EditorApplication.isPlaying = true;
                return;
            }
            if (!EditorApplication.isPlaying || cycle == 2
                || EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + ".started", 0f) < 3f) return;

            var progress = Object.FindAnyObjectByType<PlanetStageProgression>();
            Require(progress != null, "No runtime stage progression.");
            Require(progress.RequiredBossCount(PlanetStageId.A) == 3, "Stage A must register all three production bosses.");
            Require(progress.HighestUnlocked == PlanetStageId.A && progress.DefeatedBossCount(PlanetStageId.A) == 0,
                "A new Play loaded defeated bosses or opened Stage B.");
            RequireLocked(progress);
            if (cycle == 1)
            {
                File.AppendAllText(Report, "PASS: first Play starts at A despite the existing disk save; B content and clouds remain locked.\n");
                ValidateThreeBossGate(progress);
                RequireDiskSaveUnchanged();
                SessionState.SetInt(Key + ".cycle", 2);
                EditorApplication.isPlaying = false;
            }
            else
            {
                RequireDiskSaveUnchanged();
                Finish("PASS: second Play starts at A with zero defeats; persistent save unchanged.");
            }
        }
        catch (Exception error) { Finish("FAIL: " + error); }
    }

    static void ValidateThreeBossGate(PlanetStageProgression progress)
    {
        int unlocks = 0;
        progress.StageUnlocked += _ => unlocks++;
        progress.RecordBossDefeat(PlanetStageId.B, "KingB1");
        progress.RecordBossDefeat(PlanetStageId.A, "missing");
        progress.RecordBossDefeat(PlanetStageId.A, Bosses[0]);
        progress.RecordBossDefeat(PlanetStageId.A, Bosses[0]);
        Require(progress.DefeatedBossCount(PlanetStageId.A) == 1, "Duplicate/unregistered/locked bosses changed the count.");
        RequireLocked(progress);
        ValidateRecreatedProgress(progress, 1, PlanetStageId.A);
        progress.RecordBossDefeat(PlanetStageId.A, Bosses[1]);
        RequireLocked(progress);
        Require(unlocks == 0, "Unlock fired before the third boss.");
        progress.RecordBossDefeat(PlanetStageId.A, Bosses[2]);
        Require(progress.IsUnlocked(PlanetStageId.B) && !progress.IsUnlocked(PlanetStageId.C)
            && progress.DefeatedBossCount(PlanetStageId.A) == 3 && unlocks == 1, "Third boss did not unlock only B once.");
        Transform stageB = progress.transform.Find("Areas/Stage B");
        Require(stageB.gameObject.activeSelf && stageB.GetComponentsInChildren<Creature>(true).Length > 0,
            "B did not activate and spawn its creatures after the third boss.");
        ValidateRecreatedProgress(progress, 3, PlanetStageId.B);
        File.AppendAllText(Report, "PASS: B stays locked after bosses 1 and 2 and duplicate victories; only boss 3 unlocks B and spawns its creatures.\n"
            + "PASS: partial and completed progress survive component recreation within the same Play.\n");
    }

    static void RequireLocked(PlanetStageProgression progress)
    {
        Transform stageB = progress.transform.Find("Areas/Stage B");
        Require(!progress.IsUnlocked(PlanetStageId.B) && !stageB.gameObject.activeSelf
            && stageB.GetComponentsInChildren<Creature>(true).Length == 0, "B opened or spawned before all three bosses died.");
        Require(progress.transform.Find("Locked Stage B Clouds") != null, "B clouds are missing while locked.");
    }

    static void ValidateRecreatedProgress(PlanetStageProgression source, int defeated, PlanetStageId highest)
    {
        var fixture = new GameObject("Temporary session save fixture");
        fixture.SetActive(false);
        try
        {
            var restored = fixture.AddComponent<PlanetStageProgression>();
            var world = typeof(PlanetStageProgression).GetField("worldId", BindingFlags.Instance | BindingFlags.NonPublic);
            world.SetValue(restored, world.GetValue(source));
            restored.Initialize();
            foreach (string boss in Bosses) restored.RegisterBoss(PlanetStageId.A, boss);
            Require(restored.DefeatedBossCount(PlanetStageId.A) == defeated && restored.HighestUnlocked == highest,
                "Scene/component recreation lost progress within this Play.");
        }
        finally { Object.DestroyImmediate(fixture); }
    }

    static void RequireDiskSaveUnchanged() => Require(
        PlayerPrefs.GetString(SessionState.GetString(Key + ".saveKey", ""), "") == SessionState.GetString(Key + ".save", ""),
        "Play changed the persistent stage save.");

    static void Finish(string message)
    {
        File.AppendAllText(Report, message + "\n");
        SessionState.SetInt(Key + ".cycle", 0);
        SessionState.SetBool(Key + ".restore", true);
        EditorApplication.isPlaying = false;
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
