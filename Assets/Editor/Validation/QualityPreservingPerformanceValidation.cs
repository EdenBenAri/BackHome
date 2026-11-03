using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Opt-in runtime regression checks, invoked by the performance audit in Play mode.</summary>
public static class QualityPreservingPerformanceValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Report = "Logs/quality-preserving-2026-11-03/regression.txt";
    static bool _threadCounterAvailable;

    [UnityEditor.MenuItem("BackHome/Performance/Validate Quality-Preserving Changes In Play")]
    public static void Run()
    {
        Require(Application.isPlaying, "Run this validation in Play mode in Galaxy.");
        Directory.CreateDirectory(Path.GetDirectoryName(Report));
        File.WriteAllText(Report, "Quality-preserving regression checks\n");
        long calibration = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(new byte[1024]);
        _threadCounterAvailable = GC.GetAllocatedBytesForCurrentThread() > calibration;
        Log("Managed thread allocation counter available: " + _threadCounterAvailable);
        CheckOutlineSnapshots();
        CheckParticles();
        CheckHiddenBars();
        Log("PASS all regression checks");
    }

    static void CheckOutlineSnapshots()
    {
        Type pass = typeof(ToonOutlineRendererFeature).GetNestedType("ToonOutlinePass", BindingFlags.NonPublic);
        Type data = pass.GetNestedType("PassData", BindingFlags.NonPublic);
        object first = Activator.CreateInstance(data, true), second = Activator.CreateInstance(data, true);
        var copy = (Action<List<Renderer>, List<Renderer>>)Delegate.CreateDelegate(
            typeof(Action<List<Renderer>, List<Renderer>>),
            pass.GetMethod("CopySnapshot", BindingFlags.Static | BindingFlags.NonPublic));
        var renderer = Object.FindAnyObjectByType<MeshRenderer>();
        Require(renderer != null, "A live renderer is required.");
        var source = new List<Renderer> { renderer };
        foreach (string name in new[] { "mobs", "kings", "players", "objectives" })
        {
            var a = (List<Renderer>)data.GetField(name).GetValue(first);
            var b = (List<Renderer>)data.GetField(name).GetValue(second);
            Require(!ReferenceEquals(a, b), "Camera passes share a renderer snapshot.");
            copy(source, a);
            source.Clear();
            copy(source, b);
            Require(a.Count == 1 && a[0] == renderer && b.Count == 0, "Later recording changed an earlier camera snapshot.");
            source.Add(renderer);
            copy(source, b);
            copy(source, a); // warm capacity and delegate
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++) copy(source, a);
            if (_threadCounterAvailable)
                Require(GC.GetAllocatedBytesForCurrentThread() == start, "Steady-state outline snapshots allocate.");
        }
        Log("PASS four independent camera snapshots preserve renderer order across 256 warmed copies per category");
        if (_threadCounterAvailable) Log("PASS warmed outline copies allocate 0 managed thread bytes");
    }

    static void CheckParticles()
    {
        var typed = Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude);
        var raw = Object.FindObjectsByType(typeof(ParticleSystem), FindObjectsInactive.Exclude);
        var expected = new HashSet<Object>(typed);
        Require(typed.Length == raw.Length && expected.SetEquals(raw), "Particle queries differ.");
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++) Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude);
        long typedBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++) Object.FindObjectsByType(typeof(ParticleSystem), FindObjectsInactive.Exclude);
        long rawBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Log($"Particle query thread-counter: before={typedBytes}, after={rawBytes} bytes / 128 queries");
        // Some Unity native allocations are visible to Profiler GC.Alloc but
        // not this managed thread counter. The audit captures those separately.
        if (typedBytes > 0) Require(rawBytes < typedBytes, "Particle query allocation did not improve.");
        Log("PASS identical active particle discovery results");

        var drivers = Object.FindObjectsByType<PlanetParticleGravity>();
        var enabled = new List<PlanetParticleGravity>();
        foreach (var driver in drivers) if (driver.enabled) { enabled.Add(driver); driver.enabled = false; }
        GameObject owner = null, active = null, inactive = null, noGravity = null;
        try
        {
            owner = new GameObject("Particle gravity regression driver");
            var gravity = owner.AddComponent<PlanetParticleGravity>();
            active = new GameObject("Active particle regression");
            var ps = active.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.gravityModifier = 1f;
            inactive = new GameObject("Inactive particle regression");
            inactive.SetActive(false);
            var dormant = inactive.AddComponent<ParticleSystem>();
            var dormantMain = dormant.main; dormantMain.gravityModifier = 2f;
            noGravity = new GameObject("No gravity regression");
            var zero = noGravity.AddComponent<ParticleSystem>();
            var zeroMain = zero.main; zeroMain.gravityModifier = 0f;
            Call(gravity, "Discover");
            Require(main.gravityModifier.constant == 0f && dormantMain.gravityModifier.constant == 2f,
                "Active/inactive particle discovery changed.");
            inactive.SetActive(true);
            Call(gravity, "Discover");
            Require(dormantMain.gravityModifier.constant == 0f, "Reactivated particles missed same-frame discovery.");
            Object.DestroyImmediate(noGravity); noGravity = null;
            typeof(PlanetParticleGravity).GetField("_discoveryTicks", Private).SetValue(gravity, 63);
            Call(gravity, "Discover");
            var seen = (HashSet<ParticleSystem>)typeof(PlanetParticleGravity).GetField("_seen", Private).GetValue(gravity);
            Require(!seen.Contains(zero), "Destroyed zero-gravity systems are retained.");
            gravity.enabled = false;
            Require(main.gravityModifier.constant == 1f && dormantMain.gravityModifier.constant == 2f,
                "Authored particle gravity was not restored on disable.");
            Log("PASS particle activation, same-frame discovery, destroyed-system cleanup and authored gravity restoration");
        }
        finally
        {
            if (owner != null) Object.DestroyImmediate(owner);
            if (active != null) Object.DestroyImmediate(active);
            if (inactive != null) Object.DestroyImmediate(inactive);
            if (noGravity != null) Object.DestroyImmediate(noGravity);
            foreach (var driver in enabled) if (driver != null) driver.enabled = true;
        }
    }

    static void CheckHiddenBars()
    {
        HpBar target = null;
        foreach (var bar in Object.FindObjectsByType<HpBar>())
            if (bar.TryGetComponent<Creature>(out var creature) && creature.IsAlive && !bar.TryGetComponent<AreaKing>(out _))
            { target = bar; break; }
        Require(target != null, "An ordinary live creature is required.");
        var view = (HpBarView)typeof(HpBar).GetField("bars", Private).GetValue(target);
        var offset = (Vector3)typeof(HpBar).GetField("localOffset", Private).GetValue(target);
        Call(target, "HideBarsImmediate");
        Require(!view.gameObject.activeSelf, "Creature bar did not hide.");
        var sentinel = offset + Vector3.one * 10f;
        view.transform.localPosition = sentinel;
        Call(target, "LateUpdate");
        Require(view.transform.localPosition == sentinel, "Invisible bar still updates its transform.");
        Call(target, "OnDamaged", 1f);
        Call(target, "LateUpdate");
        Require(view.gameObject.activeSelf && Vector3.Distance(view.transform.localPosition, offset) < .001f,
            "Revealed bar did not recover its position in the same frame.");
        var camera = (Camera)typeof(HpBar).GetField("_camera", Private).GetValue(target);
        Require(camera != null && Quaternion.Angle(view.transform.rotation, camera.transform.rotation) < .01f,
            "Revealed bar no longer faces its camera.");
        typeof(HpBar).GetField("_revealTimer", Private).SetValue(target, 0f);
        for (int i = 0; i < 1000 && view.gameObject.activeSelf; i++) Call(target, "LateUpdate");
        Require(!view.gameObject.activeSelf, "Damage visibility timer/fade stopped progressing.");
        Call(target, "OnDamaged", 1f);
        Call(target, "LateUpdate");
        Require(view.gameObject.activeSelf, "Second hit failed to reveal the bar.");
        Log("PASS hidden bars skip transforms; first and repeated damage restore position/billboard immediately; fade still completes");
    }

    static void Call(object instance, string name, params object[] args) =>
        instance.GetType().GetMethod(name, Private).Invoke(instance, args);
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Log(string message) => File.AppendAllText(Report, message + "\n");
}
