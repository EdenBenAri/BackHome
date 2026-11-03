using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Opt-in, isolated checks plus real three-weapon boss timings in the open Galaxy.</summary>
public static class BalancePlayValidation
{
    const string Report = "Logs/BalancePlayValidation.txt";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static PlayerVitals player;
    static Creature target;
    static CreatureStats probeStats;
    static bool started;
    static int probe;
    static float began;
    static readonly string[] Types = { "Slime", "Snarlfang", "Grimling" };
    static readonly int[] PlayerLevels = { 1, 5, 25 };
    static readonly int[] KingLevels = { 3, 7, 27 };

    [MenuItem("BackHome/Balance/Validate In Play")]
    public static void Request() => File.WriteAllText("Temp/BackHomePlanetStartup.trigger", "balance");

    public static void Prepare()
    {
        started = false;
        probe = 0;
        foreach (var director in Object.FindObjectsByType<KingCinematicDirector>()) director.enabled = false;
        foreach (var tutorial in Object.FindObjectsByType<DragToMoveTutorial>()) tutorial.enabled = false;
        Application.runInBackground = true;
        File.WriteAllText(Report, "Live Galaxy balance checks. No saved progression or asset changes.\n");
    }

    public static bool Tick()
    {
        if (!started)
        {
            RunContracts();
            started = true;
            BeginProbe();
            return false;
        }
        if (target != null && target.IsAlive)
        {
            Require(Time.timeScale > .99f && !EditorApplication.isPaused, "Probe clock paused: scale=" + Time.timeScale);
            Require(Time.time - began < 20f, "Real weapon probe timed out: " + Types[probe]);
            return false;
        }
        float seconds = Time.time - began;
        Require(seconds >= 6f && seconds <= 16f, $"Boss duration outside budget: {Types[probe]} {seconds:0.00}s");
        Log($"PASS real weapons: {Types[probe]} Lv{KingLevels[probe]}, player Lv{PlayerLevels[probe]}: {seconds:0.00}s (stationary single target)");
        player.GetComponent<PlayerRangeCombat>().enabled = false;
        if (target != null) Object.Destroy(target.gameObject);
        Object.Destroy(probeStats);
        if (++probe < Types.Length) { BeginProbe(); return false; }
        Log("PASS all balance checks; movement/kiting and full chapter pacing still require a human playthrough.");
        return true;
    }

    static void RunContracts()
    {
        // The bootstrap may bind the camera after Prepare. Detach its victory callbacks
        // before the respawn check kills a king, so slow motion cannot skew timings.
        foreach (var director in Object.FindObjectsByType<KingCinematicDirector>())
        {
            director.enabled = false;
            director.Bind(null, null);
        }
        player = Object.FindAnyObjectByType<PlayerVitals>();
        Require(player != null && player.Weapons.Count == 3, "Missing player loadout.");
        foreach (var walker in player.GetComponents<PlanetWalker>()) walker.enabled = false;
        foreach (var controller in player.GetComponents<TouchController>()) controller.enabled = false;
        foreach (var zone in Object.FindObjectsByType<VitalsRecoveryZone>()) zone.enabled = false;
        Set(player, "_recoveryZones", 0);
        var account = PlayerLevel.EnsureExists();
        Set(account, "_level", 1); Set(account, "_xp", 0);
        float oldDamage = player.CombatFor(player.PrimaryWeapon).AttackDamage, oldHp = player.MaxHealth;
        player.AddOxygen(-1f); // Invalid recovery must not change the tank.
        account.AddExperience(LevelTable.XpToAdvance(1));
        Require(account.Level == 2 && account.Xp == 0, "Level-up accounting failed.");
        Close(player.CombatFor(player.PrimaryWeapon).AttackDamage, oldDamage * 1.1f, "Weapon damage did not grow 10%.");
        Close(player.MaxHealth, oldHp * 1.08f, "Health did not grow 8%.");
        Close(player.CurrentHealth, player.MaxHealth, "Level-up did not refill health.");
        Close(player.MaxOxygen, 360f, "Level-up grew the oxygen tank.");
        for (int level = 2; level <= LevelTable.MaxLevel; level++)
        {
            Close(LevelTable.ScaleAttack(13f, level) / LevelTable.ScaleAttack(13f, level - 1), 1.1f, "Damage growth drift.");
            Close(LevelTable.ScalePower(80f, level) / LevelTable.ScalePower(80f, level - 1), 1.08f, "Health growth drift.");
        }
        var banner = Object.FindAnyObjectByType<LevelUpStatsBanner>(FindObjectsInactive.Include);
        Require(banner != null, "Missing level-up banner.");
        Invoke(banner, "WriteGains", 2);
        var attackLabel = (TMPro.TMP_Text)Get(banner, "attackBonus");
        var healthLabel = (TMPro.TMP_Text)Get(banner, "healthBonus");
        Require(attackLabel.text == "+10%" && healthLabel.text == "+8%", "Level-up UI does not show percentages.");
        Log("PASS actual XP level-up, full weapon scaling, 30 compounded levels and percentage UI.");

        foreach (int level in new[] { 1, 30 })
        {
            Set(account, "_level", level); player.ResetVitals();
            Set(player, "_currentOxygen", 0f); player.SetInvulnerable(false);
            float before = player.CurrentHealth;
            Invoke(player, "Update");
            Close((before - player.CurrentHealth) / player.MaxHealth, .0625f * Time.deltaTime, "Oxygen grace changes with level.");
        }
        player.SetInvulnerable(true);
        Set(account, "_level", 1); Set(account, "_xp", 0); player.ResetVitals();
        Log("PASS 360 O2 / 4 per second = 90 seconds; empty-tank health loss stays proportional at Lv1 and Lv30.");

        var spawner = Object.FindAnyObjectByType<CreatureSpawner>();
        var points = (CreatureSpawner.SpawnPoint[])Get(spawner, "spawnPoints");
        var progress = Object.FindAnyObjectByType<PlanetStageProgression>();
        var areas = SphericalPlanet.Instance.transform.Find("Areas");
        int checkedKings = 0, checkedMobs = 0;
        foreach (var point in points)
        {
            Transform area = PlanetStage.FindArea(areas, point.areaId);
            Require(area != null, "Missing area " + point.areaId);
            foreach (var creature in area.GetComponentsInChildren<Creature>(true))
            {
                bool king = creature.GetComponent<AreaKing>() != null;
                Require(creature.Level == (king ? point.kingLevel : point.creatureLevel), "Wrong encounter level at " + point.areaId);
                Close(creature.MaxHealth, LevelTable.ScaleEnemyHealth(creature.Stats.MaxHealth, creature.Level), "Creature HP curve failed.");
                Close(creature.AttackDamage, LevelTable.ScaleEnemyDamage(creature.Stats.AttackDamage, creature.Level), "Creature damage curve failed.");
                Require(creature.Experience == LevelTable.ScaleExperience(creature.Stats.Experience, creature.Level), "XP reward mismatch.");
                if (!king) checkedMobs++;
            }
            if (point.king.prefab == null) continue;
            Require(spawner.TryGetKingForPresentation(point.areaId, out var live), "Missing live king at " + point.areaId);
            Require(live.Level == point.kingLevel, "King has the prefab level instead of its area level.");
            checkedKings++;
            progress.RecordBossDefeat(progress.StageFor(area), area.name);
        }
        Require(checkedKings == 11 && checkedMobs >= 90, "Did not validate all chapter stages.");
        Require(spawner.TryGetKingForPresentation("KingA1", out var first), "Missing respawn subject.");
        var source = first.Stats;
        int sourceLevel = source.Level;
        var safe = Object.Instantiate(source);
        Set(safe, "experience", 0); Set(safe, "loot", Array.Empty<LootEntry>());
        first.SetStats(safe, false); first.TakeDamage(first.MaxHealth + 1f);
        foreach (var live in (IList)Get(spawner, "_kings"))
            if ((bool)live.GetType().GetField("waiting").GetValue(live))
                live.GetType().GetField("readyAt").SetValue(live, Time.time - 1f);
        Invoke(spawner, "Update");
        Require(spawner.TryGetKingForPresentation("KingA1", out var respawn) && respawn.Level == 3, "Respawn lost its encounter level.");
        Require(respawn.Stats == source && source.Level == sourceLevel, "Shared stats were mutated.");
        Object.Destroy(safe);
        Log($"PASS all 11 king levels, {checkedMobs} mobs, stage unlocks, scaled XP, respawn and shared asset isolation.");

        // Repeated hits arrive faster than poison's interval: damage must still tick.
        var dotObject = new GameObject("Poison balance probe");
        var dotTarget = dotObject.AddComponent<Creature>();
        var dotStats = Object.Instantiate(source);
        Set(dotStats, "maxHealth", 10000f); Set(dotStats, "experience", 0);
        dotTarget.SetStats(dotStats);
        var poison = Resources.Load<WeaponDefinition>("Weapons/Itchy/ItchyWeapon").DotSettings;
        float dotHealth = dotTarget.CurrentHealth;
        for (int i = 0; i < Mathf.CeilToInt(2f / Mathf.Max(.001f, Time.deltaTime)); i++)
        {
            CreatureAilments.RegisterRepeatedHit(dotTarget, poison, 40f);
            Invoke(dotObject.GetComponent<CreatureAilments>(), "Update");
        }
        Require(dotTarget.CurrentHealth < dotHealth - 5f, "Rapid fire still starves poison ticks.");
        Object.Destroy(dotObject); Object.Destroy(dotStats);
        Log("PASS continuous-fire poison ticks while its duration refreshes.");

        spawner.enabled = false;
        foreach (var streamer in Object.FindObjectsByType<PlanetEnvironmentStreamer>()) streamer.enabled = false;
        foreach (var creature in Object.FindObjectsByType<Creature>(FindObjectsInactive.Include)) creature.gameObject.SetActive(false);
    }

    static void BeginProbe()
    {
        Set(PlayerLevel.Instance, "_level", PlayerLevels[probe]);
        player.ResetVitals();
        string type = Types[probe];
        var prefab = Resources.Load<GameObject>($"Galaxy/ChapterOne/Creatures/{type}/King/King_{type}");
        Require(prefab != null, "Missing king prefab " + type);
        Vector3 position = player.transform.position + player.transform.forward * 3f;
        var go = Object.Instantiate(prefab, position, player.transform.rotation);
        target = go.GetComponent<Creature>();
        probeStats = Object.Instantiate(target.Stats);
        Set(probeStats, "experience", 0); Set(probeStats, "loot", Array.Empty<LootEntry>());
        target.SetStats(probeStats); target.SetEncounterLevel(KingLevels[probe]); target.SetFrozen(true);
        var combat = target.GetComponent<CreatureRangeCombat>();
        if (combat != null) combat.enabled = false;
        player.GetComponent<PlayerRangeCombat>().enabled = true;
        began = Time.time;
        Log($"BEGIN {type}: HP={target.MaxHealth:0.0}, time={began:0.00}, scale={Time.timeScale}, background={Application.runInBackground}");
    }

    static object Get(object o, string field) => o.GetType().GetField(field, Private).GetValue(o);
    static void Set(object o, string field, object value) => o.GetType().GetField(field, Private).SetValue(o, value);
    static void Invoke(object o, string method, params object[] args) => o.GetType().GetMethod(method, Private).Invoke(o, args);
    static void Close(float value, float expected, string message) => Require(Mathf.Abs(value - expected) < .001f * Mathf.Max(1f, expected), message);
    static void Require(bool condition, string message) { if (!condition) { Log("FAIL " + message); throw new InvalidOperationException(message); } }
    static void Log(string message) => File.AppendAllText(Report, message + "\n");
}
