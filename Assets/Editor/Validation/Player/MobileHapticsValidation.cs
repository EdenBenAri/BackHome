using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Opt-in checks using disposable objects; never changes scenes, assets, or preferences.</summary>
[InitializeOnLoad]
public static class MobileHapticsValidation
{
    const string Trigger = "Temp/BackHomeHapticsValidation.trigger";
    const string Report = "Logs/MobileHapticsValidation.txt";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int _passed;

    static MobileHapticsValidation() => EditorApplication.update += Poll;

    static void Poll()
    {
        if (!File.Exists(Trigger) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Trigger);
        Run();
    }

    [MenuItem("BackHome/Feedback/Validate Mobile Haptics")]
    public static void Run()
    {
        _passed = 0;
        Directory.CreateDirectory("Logs");
        File.WriteAllText(Report, "Mobile haptics validation - Unity " + Application.unityVersion + "\n");
        try
        {
            Check("Multiple weapons share a five-pulse/second budget", AttackBudget);
            Check("Completion has priority and leaves a quiet interval", Priority);
            Check("Rate limiter allocates zero managed bytes after warmup", Allocation);
            Check("Cargo waits for all flyers and ignores duplicate completion", CargoArrival);
            Check("Interrupted cargo cannot complete; old batches cannot affect new batches", CargoInterrupted);
            Check("Native plugins are restricted to their intended platforms", PluginPlatforms);
            File.AppendAllText(Report, $"PASS all {_passed} checks\n");
        }
        catch (Exception error)
        {
            File.AppendAllText(Report, "FAIL " + error + "\n");
            throw;
        }
    }

    static void Check(string label, Action test)
    {
        test();
        _passed++;
        File.AppendAllText(Report, "PASS " + label + "\n");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static void AttackBudget()
    {
        var limiter = new HapticRateLimiter();
        int accepted = 0;
        for (int millisecond = 0; millisecond < 10000; millisecond++)
            for (int weapon = 0; weapon < 8; weapon++)
                if (limiter.TryAccept(MobileHaptics.Cue.Attack, millisecond / 1000d)) accepted++;
        Require(accepted >= 49 && accepted <= 50, "80,000 requests should yield at most 50 pulses: " + accepted);
        Require(!limiter.TryAccept((MobileHaptics.Cue)99, 100), "Unknown cue accepted.");
    }

    static void Priority()
    {
        var limiter = new HapticRateLimiter();
        Require(limiter.TryAccept(MobileHaptics.Cue.Attack, 10), "First shot was suppressed.");
        Require(!limiter.TryAccept(MobileHaptics.Cue.CargoStart, 10.05), "Start overlapped a shot.");
        Require(limiter.TryAccept(MobileHaptics.Cue.CargoComplete, 10.05), "Completion was swallowed by gunfire.");
        Require(!limiter.TryAccept(MobileHaptics.Cue.Attack, 10.3), "Completion quiet interval was lost.");
        Require(limiter.TryAccept(MobileHaptics.Cue.Attack, 10.36), "Feedback did not recover.");
        Require(!limiter.TryAccept(MobileHaptics.Cue.CargoComplete, 10.5), "Duplicate completion was accepted.");
        Require(limiter.TryAccept(MobileHaptics.Cue.CargoComplete, 10.81), "Later deposit was suppressed.");
    }

    static void Allocation()
    {
        var limiter = new HapticRateLimiter();
        limiter.TryAccept(MobileHaptics.Cue.Attack, 0);
        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 1; i <= 100000; i++) limiter.TryAccept(MobileHaptics.Cue.Attack, i * 0.01);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, "Limiter allocated " + allocated + " bytes.");
    }

    static void CargoArrival()
    {
        WithDeposit(deposit =>
        {
            Set(deposit, "_depositId", 1);
            Call(deposit, "RegisterFlyer"); Call(deposit, "RegisterFlyer");
            Call(deposit, "FlyerCommitted", 1, true);
            Require(Get<int>(deposit, "_pendingFlyers") == 1, "First icon completed the batch.");
            Call(deposit, "FlyerCommitted", 1, true);
            Require(Get<int>(deposit, "_pendingFlyers") == 0, "Last icon did not finish the batch.");
            Require(!Get<bool>(deposit, "_depositInterrupted"), "Visible arrival was marked interrupted.");
            Call(deposit, "FlyerCommitted", 1, true);
            Require(Get<int>(deposit, "_pendingFlyers") == 0, "Duplicate commit underflowed the counter.");
        });
    }

    static void CargoInterrupted()
    {
        WithDeposit(deposit =>
        {
            Set(deposit, "_depositId", 2);
            Call(deposit, "RegisterFlyer"); Call(deposit, "RegisterFlyer");
            Call(deposit, "FlyerCommitted", 1, false);
            Require(Get<int>(deposit, "_pendingFlyers") == 2, "Old batch consumed a new flyer.");
            Require(!Get<bool>(deposit, "_depositInterrupted"), "Old batch interrupted a new deposit.");
            Call(deposit, "FlyerCommitted", 2, false);
            Call(deposit, "FlyerCommitted", 2, true);
            Require(Get<int>(deposit, "_pendingFlyers") == 0 && Get<bool>(deposit, "_depositInterrupted"),
                "Interrupted batch may emit success.");
        });
    }

    static void WithDeposit(Action<CargoCampDeposit> test)
    {
        var go = new GameObject("Haptics validation") { hideFlags = HideFlags.HideAndDontSave };
        go.SetActive(false);
        try { test(go.AddComponent<CargoCampDeposit>()); }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    static void PluginPlatforms()
    {
        Plugin("Assets/Plugins/WebGL/BackHomeHaptics.jslib", BuildTarget.WebGL);
        Plugin("Assets/Plugins/Android/BackHomeHaptics.java", BuildTarget.Android);
        Plugin("Assets/Plugins/iOS/BackHomeHaptics.mm", BuildTarget.iOS);
    }

    static void Plugin(string path, BuildTarget target)
    {
        var importer = AssetImporter.GetAtPath(path) as PluginImporter;
        Require(importer != null, "Missing plugin importer: " + path);
        Require(!importer.GetCompatibleWithEditor() && !importer.GetCompatibleWithAnyPlatform(), "Plugin leaks into Editor/other platforms: " + path);
        foreach (var platform in new[] { BuildTarget.WebGL, BuildTarget.Android, BuildTarget.iOS, BuildTarget.StandaloneWindows64 })
            Require(importer.GetCompatibleWithPlatform(platform) == (platform == target), "Incorrect platform: " + path + " / " + platform);
    }

    static void Set(object instance, string field, object value) => instance.GetType().GetField(field, Private).SetValue(instance, value);
    static T Get<T>(object instance, string field) => (T)instance.GetType().GetField(field, Private).GetValue(instance);
    static void Call(object instance, string method, params object[] args) => instance.GetType().GetMethod(method, Private).Invoke(instance, args);
}
