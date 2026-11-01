using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using StarterAssets;
using UnityEditor;
using UnityEngine;
using Unity.Profiling;
using Object = UnityEngine.Object;

/// <summary>Opt-in integration run. Never saves scene changes, XP or real chapter progress.</summary>
[InitializeOnLoad]
public static class KingCinematicPlayValidation
{
    const string Key = "BackHome.KingCinematicValidation";
    const string Trigger = "Temp/BackHomeKingCinematicValidation.trigger";
    public const string Report = "Logs/KingCinematicPlayValidation.txt";
    static KingCinematicPlayValidation() => EditorApplication.update += Poll;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AttachBeforeFirstFrame()
    {
        if (!SessionState.GetBool(Key, false)) return;
        Application.runInBackground = true;
        if (Object.FindAnyObjectByType<KingCinematicValidationRunner>() == null)
            new GameObject("Temporary king cinematic validation").AddComponent<KingCinematicValidationRunner>();
    }

    [MenuItem("BackHome/Camera/Validate King Cinematic in Play")]
    public static void Request() => File.WriteAllText(Trigger, "run");

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(Key + ".restore", false))
        {
            var progress = Object.FindAnyObjectByType<PlanetStageProgression>(FindObjectsInactive.Include);
            if (progress != null) SetPersistence(progress, SessionState.GetBool(Key + ".persist", true));
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
            Time.timeScale = SessionState.GetFloat(Key + ".scale", 1f);
            Time.fixedDeltaTime = SessionState.GetFloat(Key + ".fixed", .02f);
            SessionState.SetBool(Key + ".restore", false);
            SessionState.SetBool(Key, false);
        }
        if (File.Exists(Trigger) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(Trigger);
            var progress = Object.FindAnyObjectByType<PlanetStageProgression>(FindObjectsInactive.Include);
            if (progress == null) throw new InvalidOperationException("Open the chapter scene first.");
            var serialized = new SerializedObject(progress);
            SessionState.SetBool(Key + ".persist", serialized.FindProperty("persistProgress").boolValue);
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            SessionState.SetFloat(Key + ".scale", Time.timeScale);
            SessionState.SetFloat(Key + ".fixed", Time.fixedDeltaTime);
            SetPersistence(progress, false);
            SessionState.SetBool(Key + ".restore", true);
            SessionState.SetBool(Key, true);
            SessionState.SetFloat(Key + ".deadline", (float)EditorApplication.timeSinceStartup + 180f);
            File.WriteAllText(Report, "King cinematic live integration; isolated progression and zero-XP lethal hits\n");
            Time.timeScale = 1f;
            EditorApplication.isPlaying = true;
        }
        if (!SessionState.GetBool(Key, false)) return;
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + ".deadline", 0f)) Finish("FAIL timeout");
        else if (EditorApplication.isPlaying && Object.FindAnyObjectByType<KingCinematicValidationRunner>() == null)
        {
            Application.runInBackground = true;
            new GameObject("Temporary king cinematic validation").AddComponent<KingCinematicValidationRunner>();
        }
    }

    static void SetPersistence(PlanetStageProgression progress, bool value)
    {
        var serialized = new SerializedObject(progress);
        serialized.FindProperty("persistProgress").boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void Finish(string result)
    {
        File.AppendAllText(Report, result + "\n");
        SessionState.SetBool(Key, false);
        EditorApplication.isPlaying = false;
    }
}

[DefaultExecutionOrder(30000)]
public class KingCinematicValidationRunner : MonoBehaviour
{
    static readonly string[] ExpectedKings =
    {
        "KingA1", "KingA2", "KingA3", "KingB1", "KingB2", "KingB3",
        "KingC1", "KingC2", "KingC3", "KingD1", "KingD2"
    };
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    KingCinematicDirector director;
    CreatureSpawner spawner;
    PlanetStageFog fog;
    Transform player;
    StarterAssetsInputs input;
    KingCinematicDirector.Phase previous = KingCinematicDirector.Phase.Idle;
    float phaseStart, lastKillAt, holdLength, spawnStart;
    int completed;
    int fogWaits;
    bool initialized, sawIntro, frozen, captured;
    Vector3 playerAtFreeze;
    Animator kingAnimator;
    readonly Dictionary<Transform, Vector3> frozenCreatures = new();
    readonly List<CreatureStats> transientStats = new();
    ProfilerRecorder tickRecorder;

    static object Get(object obj, string field) => obj.GetType().GetField(field, Private).GetValue(obj);
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private).SetValue(obj, value);
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }

    void LateUpdate()
    {
        try { Step(); }
        catch (Exception error) { KingCinematicPlayValidation.Finish("FAIL " + error); enabled = false; }
    }

    void Step()
    {
        if (!initialized)
        {
            director = Object.FindAnyObjectByType<KingCinematicDirector>();
            spawner = Object.FindAnyObjectByType<CreatureSpawner>();
            var walker = Object.FindAnyObjectByType<PlanetWalker>();
            if (director == null || spawner == null || walker == null) return;
            player = walker.transform;
            input = player.GetComponent<StarterAssetsInputs>();
            player.GetComponent<PlayerVitals>().SetInvulnerable(true);
            player.GetComponent<PlayerRangeCombat>().enabled = false;
            Require(director.CurrentPhase == KingCinematicDirector.Phase.IntroGrace,
                $"Missed scene introduction: phase={director.CurrentPhase}, elapsed={Get(director, "_elapsed")}, playerAlive={player.GetComponent<PlayerVitals>().IsAlive}, follow={GetComponent<CameraFollow>()}, dt={Time.unscaledDeltaTime}, scale={Time.timeScale}.");
            initialized = true;
            tickRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "BackHome.KingCinematic.Tick", 4096);
            Require(director.Settings != null && AssetDatabase.GetAssetPath(director.Settings) == "Assets/Settings/KingCinematicSettings.asset",
                "Scene camera is not using the external settings asset.");
            Require(director.Settings.replayVictoriesOnChapterEntry, "Scene settings do not replay per chapter visit.");
            // Simulate the reported returning-player case without touching real PlayerPrefs.
            var progress = Object.FindAnyObjectByType<PlanetStageProgression>();
            Require(progress != null && !(bool)Get(progress, "persistProgress"), "Live test progression was not isolated.");
            fog = progress.GetComponent<PlanetStageFog>();
            Require(fog != null && fog.enabled && director.Settings.waitForStageFog, "Stage-fog sequencing is not enabled.");
            progress.RecordBossDefeat(PlanetStageId.A, director.Settings.firstKingArea);
            Require(progress.HasDefeatedBoss(PlanetStageId.A, director.Settings.firstKingArea),
                "Could not seed the previous first-king victory.");
            File.AppendAllText(KingCinematicPlayValidation.Report,
                "Existing KingA1 victory simulated in isolated progression; replay must still complete.\n");
        }
        var phase = director.CurrentPhase;
        float now = Time.unscaledTime;
        if (phase != previous)
        {
            File.AppendAllText(KingCinematicPlayValidation.Report, $"{previous} -> {phase}, elapsed={now-phaseStart:F3}s, scale={Time.timeScale:F3}\n");
            if (previous == KingCinematicDirector.Phase.SlowMotion)
                Require(now - lastKillAt >= .69f && now - lastKillAt < .85f, "Slow motion duration was not 0.7 real seconds.");
            if (previous == KingCinematicDirector.Phase.Turn)
                Require(now - phaseStart >= .29f && now - phaseStart < .45f, "Orbit duration was not 0.3 real seconds.");
            if (phase == KingCinematicDirector.Phase.StageReveal)
            {
                fogWaits++;
                File.AppendAllText(KingCinematicPlayValidation.Report, "Waiting for stage clouds before " + ExpectedKings[completed] + "\n");
            }
            if (phase == KingCinematicDirector.Phase.Turn)
            {
                Require(!fog.IsRevealing, "Camera turned while the next stage's clouds were still revealing.");
                char stage = ExpectedKings[completed][4];
                Transform bank = fog.transform.Find("Locked Stage " + stage + " Clouds");
                Require(bank == null || !bank.gameObject.activeInHierarchy, "Next stage's fog was still visible at camera handoff.");
            }
            if (previous == KingCinematicDirector.Phase.SpawnHold)
            {
                Require(now - phaseStart >= holdLength - .03f, "Spawn hold ended before configured animation duration.");
                if (kingAnimator != null)
                    Require(kingAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime > spawnStart + .2f
                        || !kingAnimator.GetCurrentAnimatorStateInfo(0).IsName("Spawn"), "Spawn did not animate during freeze.");
            }
            if (phase == KingCinematicDirector.Phase.ZoomIn || phase == KingCinematicDirector.Phase.Turn)
            {
                sawIntro = true;
                var featured = (Creature)Get(director, "_featured");
                Require(completed < ExpectedKings.Length, "Cinematic continued beyond the final configured King.");
                string expected = ExpectedKings[completed];
                Require(featured != null && featured.name.StartsWith(expected + "_"), "Wrong featured area: " + featured?.name);
            }
            if (phase == KingCinematicDirector.Phase.SpawnHold)
            {
                captured = false;
                holdLength = (float)Get(director, "_holdDuration");
                var featured = (Creature)Get(director, "_featured");
                kingAnimator = featured.GetComponentInChildren<Animator>();
                spawnStart = kingAnimator != null ? kingAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime : 0f;
                Require(holdLength >= .5f, "Spawn hold shorter than minimum.");
                File.AppendAllText(KingCinematicPlayValidation.Report, $"Spawn duration={holdLength:F3}s; king={featured.name}\n");
            }
            phaseStart = now; previous = phase;
        }
        if (phase == KingCinematicDirector.Phase.SpawnHold && !captured && now - phaseStart > .25f)
        {
            captured = true;
            ScreenCapture.CaptureScreenshot($"Logs/KingCinematic-{ExpectedKings[completed]}.png");
        }
        if (Time.timeScale == 0f)
        {
            if (!frozen)
            {
                frozen = true; playerAtFreeze = player.position; frozenCreatures.Clear();
                var featured = (Creature)Get(director, "_featured");
                foreach (var creature in Creature.ActiveCreatures)
                    if (creature != featured && creature.IsAlive) frozenCreatures[creature.transform] = creature.transform.position;
            }
            Require(input.GameplayBlocked && input.move == Vector2.zero, "Frozen cinematic accepted movement.");
            Require(Vector3.Distance(playerAtFreeze, player.position) < .002f, "Player moved while frozen.");
            var presentingKing = (Creature)Get(director, "_featured");
            foreach (var item in frozenCreatures)
                if (item.Key != null && item.Key.gameObject.activeInHierarchy
                    && (presentingKing == null || item.Key != presentingKing.transform))
                    Require(Vector3.Distance(item.Key.position, item.Value) < .002f, "A mob moved while frozen.");
        }
        if (phase != KingCinematicDirector.Phase.Idle || !sawIntro) return;
        Require(Time.timeScale == 1f && !input.GameplayBlocked, "End of reveal leaked time or input ownership.");
        frozen = false;
        if (completed == ExpectedKings.Length)
        {
            Require(fogWaits == 3, "Expected fog-first handoffs into B, C and D; observed " + fogWaits);
            Require(!spawner.TryGetNextKingObjective(out _), "Final king left an unfinished objective.");
            var samples = tickRecorder.ToArray();
            long total = 0, peak = 0;
            foreach (var sample in samples) { total += sample.Value; peak = Math.Max(peak, sample.Value); }
            File.AppendAllText(KingCinematicPlayValidation.Report,
                $"PERF cinematic Update: {samples.Length} frames, mean={total / Math.Max(1, samples.Length) / 1000000.0:F4}ms, peak={peak / 1000000.0:F4}ms (Unity Editor)\n");
            KingCinematicPlayValidation.Finish("PASS " + string.Join(" -> ", ExpectedKings)
                + "; fog fully clears before B1/C1/D1, actual lethal hits, spawn/freeze and final restoration");
            enabled = false;
            return;
        }
        Require(spawner.TryGetKingForPresentation(ExpectedKings[completed], out var king),
            "Missing king to defeat: " + ExpectedKings[completed]);
        var safeStats = Object.Instantiate(king.Stats); transientStats.Add(safeStats);
        Set(safeStats, "experience", 0); Set(safeStats, "loot", Array.Empty<LootEntry>());
        king.SetStats(safeStats, false);
        lastKillAt = now;
        king.TakeDamage(king.MaxHealth + 1f);
        Require(director.CurrentPhase == KingCinematicDirector.Phase.SlowMotion && Mathf.Approximately(Time.timeScale, .04f),
            "Real lethal hit did not synchronously start slow motion.");
        completed++;
    }

    void OnDestroy()
    {
        tickRecorder.Dispose();
        foreach (var stats in transientStats) if (stats != null) Destroy(stats);
    }
}
