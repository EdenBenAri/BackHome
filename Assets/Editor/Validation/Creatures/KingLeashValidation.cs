using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Opt-in regression checks on disposable objects; never saves scenes or assets.</summary>
[InitializeOnLoad]
public static class KingLeashValidation
{
    const string Trigger = "Temp/BackHomeKingLeashValidation.trigger";
    const string Report = "Logs/KingLeashValidation.txt";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<Object> Cleanup = new();
    static readonly Vector3 Center = new Vector3(10000f, 10000f, 10000f);
    const float SurfaceRadius = 75f;

    static KingLeashValidation() => EditorApplication.update += Poll;

    static void Poll()
    {
        if (!File.Exists(Trigger) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Trigger);
        Run();
    }

    [MenuItem("BackHome/Creatures/Validate King Leash")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play before running king leash validation.");
        Directory.CreateDirectory("Logs");
        File.WriteAllText(Report, "King leash regression checks - Unity " + Application.unityVersion + "\n");
        int failures = 0;
        Check("King prefab defaults", Prefabs, ref failures);
        Check("Own spawn radius and spherical surface distance", Radius, ref failures);
        Check("Half-second grace survives incoming and outgoing hits and knockback", Grace, ref failures);
        Check("Update enforces deadline during knockback", KnockbackDeadline, ref failures);
        Check("Return stays locked until home, then heals and permits combat", ReturnHome, ref failures);
        Check("Re-entering radius during grace resets the timer", Reentry, ref failures);
        Check("Combat inside radius and regular creature leash behavior", ExistingCombat, ref failures);
        File.AppendAllText(Report, failures == 0 ? "PASS all 7 checks\n" : $"FAIL {failures} checks\n");
        if (failures > 0) throw new Exception("King leash regression failed. See " + Report);
    }

    static void Check(string name, Action check, ref int failures)
    {
        try { check(); File.AppendAllText(Report, "PASS " + name + "\n"); }
        catch (Exception error) { failures++; File.AppendAllText(Report, "FAIL " + name + ": " + error + "\n"); }
        finally
        {
            for (int i = Cleanup.Count - 1; i >= 0; i--)
                if (Cleanup[i] != null) Object.DestroyImmediate(Cleanup[i]);
            Cleanup.Clear();
        }
    }

    static GameObject Make(string name)
    {
        var go = new GameObject("King leash validation: " + name) { hideFlags = HideFlags.HideAndDontSave };
        go.SetActive(false);
        Cleanup.Add(go);
        return go;
    }

    static CreatureChase Fixture(bool king = true)
    {
        var planet = Make("planet").AddComponent<SphericalPlanet>();
        planet.transform.position = Center;
        Set(planet, "radius", SurfaceRadius);
        var go = Make(king ? "king" : "mob");
        go.transform.position = AtDistance(0f);
        if (king) go.AddComponent<AreaKing>();
        var chase = go.AddComponent<CreatureChase>();
        Call(chase, "Awake");
        Call(chase, "CaptureHome");
        Set(chase, "_planet", planet);
        var stats = ScriptableObject.CreateInstance<CreatureStats>();
        Cleanup.Add(stats);
        var creature = go.GetComponent<Creature>();
        creature.SetStats(stats);
        Set(creature, "_chase", chase);
        Call(chase, "EnterAggro");
        return chase;
    }

    static Vector3 AtDistance(float distance)
    {
        float angle = distance / SurfaceRadius;
        return Center + new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f) * SurfaceRadius;
    }

    static void Hit(CreatureChase chase)
    {
        chase.GetComponent<Creature>().TakeDamage(0.1f, chase.transform.position + Vector3.right);
        chase.ResetLeashTimer(); // A successful hit by the creature also uses this API.
    }

    static void Prefabs()
    {
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("King_ t:Prefab", new[] { "Assets/Resources/Galaxy" }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            var king = prefab.GetComponent<AreaKing>();
            if (king == null) continue;
            var chase = king.GetComponent<CreatureChase>();
            Require(chase != null && Get<float>(chase, "kingLeashRadius") == 30f,
                prefab.name + " did not inherit the 30-unit king radius.");
            count++;
        }
        Require(count >= 3, "Expected at least the Slime, Grimling and Chompy king prefabs.");
        File.AppendAllText(Report, $"Checked {count} king prefabs.\n");
    }

    static void Radius()
    {
        var chase = Fixture();
        var den = Make("unrelated den").transform;
        den.position = AtDistance(60f);
        chase.SetLeash(den, 1f);
        chase.transform.position = AtDistance(29f);
        Call(chase, "TickLeash", 1f);
        Require(chase.IsAggroed, "King used the den instead of its own starting position.");
        chase.transform.position = AtDistance(30.1f); // Chord is <30, surface arc is >30.
        Call(chase, "TickLeash", .5f);
        Require(chase.IsReturning, "King boundary must use spherical surface distance.");
    }

    static void Grace()
    {
        var chase = Fixture(); // No spawner / SetLeash required for kings.
        chase.transform.position = AtDistance(31f);
        for (int i = 0; i < 4; i++)
        {
            Hit(chase);
            Set(chase, "_knockActive", true);
            Call(chase, "TickLeash", .125f);
            Require(i == 3 ? chase.IsReturning : chase.IsAggroed, "Grace period was not 0.5 seconds.");
        }
        Require(!chase.IsKnockedBack, "Outstanding knockback must stop when forced return starts.");
    }

    static void KnockbackDeadline()
    {
        var chase = Fixture();
        chase.transform.position = AtDistance(31f);
        Set(chase, "_outsideLeashTime", .5f);
        Set(chase, "_knockActive", true);
        Require(Time.timeScale > 0f, "Validation requires an unpaused editor clock.");
        Call(chase, "Update");
        Require(chase.IsReturning && !chase.IsKnockedBack, "Knockback bypassed the leash update.");
    }

    static void ReturnHome()
    {
        var chase = Fixture();
        chase.transform.position = AtDistance(31f);
        Call(chase, "TickLeash", .5f);
        foreach (float distance in new[] { 31f, 29f, 10f, 1f })
        {
            chase.transform.position = AtDistance(distance);
            Hit(chase);
            Require(chase.IsReturning && !chase.IsKnockedBack, "Hit interrupted the committed return.");
        }
        var creature = chase.GetComponent<Creature>();
        Require(creature.CurrentHealth < creature.MaxHealth, "Return unexpectedly prevented damage or healed early.");
        chase.transform.position = AtDistance(0f);
        Call(chase, "TickReturning");
        Require(!chase.IsReturning && !chase.IsAggroed && creature.CurrentHealth == creature.MaxHealth,
            "Home arrival did not reset state and health.");
        Hit(chase);
        Require(chase.IsAggroed, "King could not fight again after reaching home.");
    }

    static void Reentry()
    {
        var chase = Fixture();
        chase.transform.position = AtDistance(31f);
        Call(chase, "TickLeash", .375f);
        chase.transform.position = AtDistance(29f);
        Call(chase, "TickLeash", .125f);
        chase.transform.position = AtDistance(31f);
        Call(chase, "TickLeash", .375f);
        Require(chase.IsAggroed, "Re-entry did not reset the outside timer.");
        Call(chase, "TickLeash", .125f);
        Require(chase.IsReturning, "A second exit did not start a fresh half-second grace.");
    }

    static void ExistingCombat()
    {
        var king = Fixture();
        king.transform.position = AtDistance(10f);
        Call(king, "BeginReturnHome");
        Hit(king);
        Require(king.IsAggroed, "Inside-radius hits no longer restore combat.");
        var mob = Fixture(false);
        var den = Make("mob den").transform;
        den.position = AtDistance(0f);
        mob.SetLeash(den, 5f);
        mob.transform.position = AtDistance(10f);
        for (int i = 0; i < 5; i++)
        {
            Hit(mob);
            Set(mob, "_knockActive", false);
            Call(mob, "TickLeash", 1f);
            Require(mob.IsAggroed, "Hits no longer extend the normal mob leash.");
        }
        Call(mob, "TickLeash", 2f);
        Require(mob.IsReturning, "Normal mob did not return after three seconds without hits.");
        Hit(mob);
        Require(mob.IsAggroed, "Hit no longer interrupts a normal mob's return.");
    }

    static T Get<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private).GetValue(obj);
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private).SetValue(obj, value);
    static void Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Private).Invoke(obj, args);
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
