using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using StarterAssets;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Opt-in real death/Continue/reload checks; never saves scene or persistent progress.</summary>
[InitializeOnLoad]
public static class ChapterRespawnPlayValidation
{
    const string Key = "BackHome.ChapterRespawnValidation";
    const string Trigger = "Temp/BackHomeChapterRespawnValidation.trigger";
    public const string Report = "Logs/ChapterRespawnPlayValidation.txt";

    static ChapterRespawnPlayValidation() => EditorApplication.update += Poll;

    [MenuItem("BackHome/ChapterOne/Validate Death Respawn")]
    public static void Request() => File.WriteAllText(Trigger, "run");

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(Key + ".restore", false))
        {
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
            Time.timeScale = SessionState.GetFloat(Key + ".scale", 1f);
            Time.fixedDeltaTime = SessionState.GetFloat(Key + ".fixed", .02f);
            SessionState.SetBool(Key + ".restore", false);
            SessionState.SetBool(Key, false);
        }
        if (File.Exists(Trigger) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(Trigger);
            if (SceneManager.GetActiveScene().path != PlanetAuthoringTools.ScenePath)
                throw new InvalidOperationException("Open Galaxy before validating death respawn.");
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            SessionState.SetFloat(Key + ".scale", Time.timeScale);
            SessionState.SetFloat(Key + ".fixed", Time.fixedDeltaTime);
            SessionState.SetBool(Key, true);
            SessionState.SetFloat(Key + ".deadline", (float)EditorApplication.timeSinceStartup + 180f);
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Report, "Actual lethal hits, death panel and Continue across three chapter reloads.\n");
            Time.timeScale = 1f;
            EditorApplication.isPlaying = true;
        }
        if (!SessionState.GetBool(Key, false)) return;
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + ".deadline", 0f)) Finish("FAIL timeout");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        if (!SessionState.GetBool(Key, false)) return;
        Application.runInBackground = true;
        new GameObject("Temporary chapter respawn validation").AddComponent<ChapterRespawnValidationRunner>();
    }

    public static void Finish(string result)
    {
        File.AppendAllText(Report, result + "\n");
        SessionState.SetBool(Key, false);
        SessionState.SetBool(Key + ".restore", true);
        EditorApplication.isPlaying = false;
    }
}

[DefaultExecutionOrder(30000)]
public sealed class ChapterRespawnValidationRunner : MonoBehaviour
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly List<CreatureStats> _stats = new();
    PlanetStageProgression _progress;
    CreatureSpawner _spawner;
    KingCinematicDirector _director;
    PlayerVitals _player;
    PlayerDeathUI _death;
    string _scenePath, _saveKey, _diskSave;
    int _checkpoint;
    bool _started, _waitingForDeath, _waitingForReload, _sawZoom, _sawHold;
    float _fixedDelta;

    static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    void Awake() => DontDestroyOnLoad(gameObject);

    void LateUpdate()
    {
        try { Step(); }
        catch (Exception error) { ChapterRespawnPlayValidation.Finish("FAIL " + error); enabled = false; }
    }

    bool Bind()
    {
        _progress = Object.FindAnyObjectByType<PlanetStageProgression>();
        _spawner = Object.FindAnyObjectByType<CreatureSpawner>();
        _director = Object.FindAnyObjectByType<KingCinematicDirector>();
        _player = Object.FindAnyObjectByType<PlayerVitals>();
        _death = Object.FindAnyObjectByType<PlayerDeathUI>();
        if (_progress == null || _spawner == null || _director == null || _player == null || _death == null) return false;
        _player.SetInvulnerable(true);
        _player.GetComponent<PlayerRangeCombat>().enabled = false;
        _player.GetComponent<StarterAssetsInputs>().MoveInput(Vector2.zero);
        return _progress.RequiredBossCount(PlanetStageId.A) == 3;
    }

    void Step()
    {
        if (!_started)
        {
            if (!Bind()) return;
            _scenePath = SceneManager.GetActiveScene().path;
            _saveKey = _progress.SaveKey;
            _diskSave = PlayerPrefs.GetString(_saveKey, "");
            _fixedDelta = Time.fixedDeltaTime;
            Require(_progress.HighestUnlocked == PlanetStageId.A && _progress.DefeatedBossCount(PlanetStageId.A) == 0,
                "Validation must start in a fresh editor Play session.");
            _started = true;
            BeginDeath();
            return;
        }
        if (_waitingForDeath)
        {
            var panel = (GameObject)Get(_death, "_deathPanelInstance");
            if (panel == null || !panel.activeSelf || Time.timeScale != 0f) return;
            _waitingForDeath = false;
            _waitingForReload = true;
            // A stale shared-HUD fallback must not send a planet respawn to a different scene.
            Set(_death, "restartSceneName", "Ship");
            _death.OnContinueClicked();
            return;
        }
        if (_waitingForReload)
        {
            if (_progress != null) return;
            if (!Bind()) return;
            _waitingForReload = false;
            _sawZoom = _sawHold = false;
            Require(SceneManager.GetActiveScene().path == _scenePath && _progress.IsResumingAfterDeath,
                "Continue did not resume the same chapter.");
            Require(_progress.DefeatedBossCount(PlanetStageId.A) == (_checkpoint == 0 ? 1 : 3)
                && _progress.DefeatedBossCount(PlanetStageId.B) == (_checkpoint == 0 ? 0 : 1), "Boss victories were lost on reload.");
            Require(_progress.HighestUnlocked == (_checkpoint == 0 ? PlanetStageId.A : PlanetStageId.B),
                "Unlocked stage changed on death.");
            Require(ScenePlayerSpawnPoint.TryGetPose(out var camp, out _) && Vector3.Distance(_player.transform.position, camp) < 2f,
                "Player did not respawn at the camp.");
            if (_checkpoint > 0)
            {
                Require(_progress.transform.Find("Areas/Stage B").gameObject.activeSelf, "Stage B content was locked again.");
                var clouds = _progress.transform.Find("Locked Stage B Clouds");
                Require(clouds == null || !clouds.gameObject.activeInHierarchy, "Unlocked Stage B regained fog.");
            }
        }
        string expected = _checkpoint == 0 ? "KingA2" : "KingB2";
        if (_director.IsPresenting)
        {
            var featured = (Creature)Get(_director, "_featured");
            Require(featured != null && featured.name.StartsWith(expected + "_"), "Respawn zoom selected " + featured?.name);
            Require(Time.timeScale == 0f && _player.GetComponent<StarterAssetsInputs>().GameplayBlocked,
                "Respawn zoom did not protect gameplay.");
            _sawZoom |= _director.CurrentPhase == KingCinematicDirector.Phase.ZoomIn;
            _sawHold |= _director.CurrentPhase == KingCinematicDirector.Phase.SpawnHold;
        }
        if (_director.CurrentPhase != KingCinematicDirector.Phase.Idle) return;
        Require(_sawZoom && _sawHold && Time.timeScale == 1f && Mathf.Approximately(Time.fixedDeltaTime, _fixedDelta)
            && !_player.GetComponent<StarterAssetsInputs>().GameplayBlocked, "Respawn reveal failed to finish and restore controls.");
        Kill("KingA1");
        Require(_director.CurrentPhase == KingCinematicDirector.Phase.Idle, "An old boss replayed a victory after death.");
        Require(PlayerPrefs.GetString(_saveKey, "") == _diskSave, "Validation changed the persistent stage save.");
        File.AppendAllText(ChapterRespawnPlayValidation.Report,
            $"PASS reload {_checkpoint + 1}: camp spawn, stage {_progress.HighestUnlocked}, zoom/hold {expected}, old victories ignored, controls restored.\n");
        if (++_checkpoint == 3)
        {
            ChapterRespawnPlayValidation.Finish("PASS partial progress, Stage 2 unlock, repeated death and persistent-save isolation.");
            enabled = false;
        }
        else BeginDeath();
    }

    void BeginDeath()
    {
        _director.Cancel();
        if (_checkpoint == 0) Kill("KingA1");
        else if (_checkpoint == 1)
        {
            Kill("KingA2"); Kill("KingA3"); Kill("KingB1");
        }
        _director.Cancel();
        _player.SetInvulnerable(false);
        _player.TakeDamage(_player.MaxHealth + 1f);
        Require(!_player.IsAlive, "Lethal damage did not kill the player.");
        _waitingForDeath = true;
    }

    void Kill(string area)
    {
        Require(_spawner.TryGetKingForPresentation(area, out var king), "Missing living king " + area);
        var stats = Object.Instantiate(king.Stats);
        _stats.Add(stats);
        Set(stats, "experience", 0);
        Set(stats, "loot", Array.Empty<LootEntry>());
        king.SetStats(stats, false);
        king.TakeDamage(king.MaxHealth + 1f);
    }

    void OnDestroy()
    {
        foreach (var stats in _stats) if (stats != null) Destroy(stats);
    }
}
