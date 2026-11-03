using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Isolated stage regressions; never writes production assets or the player's save.</summary>
public static class PlanetStageValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Report = "Logs/PlanetStage-validation.txt";

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);

    [MenuItem("BackHome/ChapterOne/Validate Stages")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play before validating stages.");
        string prefabBefore = File.ReadAllText(PlanetAuthoringTools.PrefabPath);
        string sceneBefore = File.ReadAllText(PlanetAuthoringTools.ScenePath);
        var report = new StringBuilder();
        try
        {
            WithWorld(ValidateHierarchyAndProgression);
            report.AppendLine("PASS: both prefab hierarchies, stable bindings, biome identities, ordered unlocks, duplicate victories and empty stages.");
            ValidatePersistence();
            report.AppendLine("PASS: partial boss progress and completed stages survive recreation; isolated save removed.");
            WithWorld(ValidateMovementAndStreaming);
            report.AppendLine("PASS: locked terrain blocks long moves; locked props stay hidden near focus; unlocked props retain distance culling.");
            WithWorld(ValidateRoads);
            report.AppendLine("PASS: dirt roads inherit surrounding stages; a large step cannot skip a locked strip between two accessible endpoints.");
            WithWorld(ValidateUnwalkableTerrain);
            report.AppendLine("PASS: Dirt_brightened blocks entry and crossing in both directions, even after all stages unlock; Dirt_darked remains walkable.");
            WithWorld(ValidateSpawner);
            report.AppendLine("PASS: locked mobs/kings are not instantiated; boss defeat spawns the next stage once and preserves existing mobs.");
            Require(File.ReadAllText(PlanetAuthoringTools.PrefabPath) == prefabBefore
                && File.ReadAllText(PlanetAuthoringTools.ScenePath) == sceneBefore, "Production assets changed during validation.");
            report.AppendLine("PASS: production scene and prefab unchanged.");
            Debug.Log(report.ToString());
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + error);
            Debug.LogException(error);
        }
        finally
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Report, report.ToString());
        }
    }

    [MenuItem("BackHome/ChapterOne/Validate Tile Movement")]
    public static void RunTileMovement()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play before validating tile movement.");
        WithWorld(ValidateRoads);
        WithWorld(ValidateUnwalkableTerrain);
        const string result = "PASS: locked roads block crossing and open with their stage; Dirt_brightened blocks entry and crossing in both directions before and after all stages complete; Dirt_darked remains walkable.";
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/PlanetTileMovement-validation.txt", result + Environment.NewLine);
        Debug.Log(result);
    }

    static void WithWorld(Action<PlanetAuthoring, PlanetStageProgression> test)
    {
        var root = PrefabUtility.LoadPrefabContents(PlanetAuthoringTools.PrefabPath);
        try
        {
            var owner = root.GetComponent<PlanetAuthoring>();
            var progress = owner.RuntimePlanet.GetComponent<PlanetStageProgression>();
            Require(progress != null, "Production prefab has no stage progression.");
            Set(progress, "persistProgress", false);
            foreach (var placement in owner.AuthoringRoot.GetComponentsInChildren<PlanetPlacement>(true)) placement.Apply(owner);
            progress.Initialize();
            test(owner, progress);
        }
        finally
        {
            PlanetAuthoringPreview.ClearAll();
            PrefabUtility.UnloadPrefabContents(root);
            PlanetAuthoringPreview.RefreshAll();
        }
    }

    static void ValidateHierarchyAndProgression(PlanetAuthoring owner, PlanetStageProgression progress)
    {
        Require(owner.ValidateConfiguration(out _), "Invalid placement bindings.");
        foreach (Transform parent in new[] { owner.AuthoringRoot, owner.RuntimePlanet.transform })
        {
            Transform areas = parent.Find("Areas");
            for (int i = 1; i <= 4; i++)
            {
                string name = "Stage " + (PlanetStageId)i;
                Transform stage = areas.Find(name);
                Require(stage != null && stage.GetComponent<PlanetStage>().Id == (PlanetStageId)i, "Missing " + name);
                Require(stage.localPosition == Vector3.zero && stage.localScale == Vector3.one
                    && Quaternion.Angle(stage.localRotation, Quaternion.identity) < .001f, "Nonidentity stage transform.");
                Require(stage.childCount == (i == 1 ? 7 : 6), "Incorrect stage population: " + name);
            }
            foreach (string area in new[] { "Camp", "A1", "KingA1", "A2", "KingA2", "A3", "KingA3" })
            {
                Transform target = areas.Find("Stage A/" + area);
                Require(target != null && PlanetStage.FindArea(areas, " " + area + " ") == target
                    && PlanetStage.FindArea(areas, "Stage A/" + area) == target, "Area lookup failed: " + area);
                if (parent == owner.AuthoringRoot)
                    Require(target.GetComponent<PlanetPlacement>().RuntimeTarget.parent.name == "Stage A", "Stage binding mismatch.");
                else
                    Require(progress.StageAt(target.position) == PlanetStageId.A, "Stage A area lies on locked terrain: " + area);
            }
            Require(PlanetStage.FindArea(areas, "missing") == null && PlanetStage.FindArea(areas, null) == null, "Invalid IDs should not resolve.");
        }
        PlanetStageId[] expected = { PlanetStageId.A, PlanetStageId.A, PlanetStageId.A, PlanetStageId.A,
            PlanetStageId.C, PlanetStageId.C, PlanetStageId.D, PlanetStageId.D, PlanetStageId.B, PlanetStageId.B };
        for (int i = 0; i < expected.Length; i++) Require(PlanetStage.FromTerrain(i) == expected[i], "Wrong terrain mapping.");
        Require(progress.HighestUnlocked == PlanetStageId.A, "New progress must start at A.");
        foreach (string boss in new[] { "A2", "A4", "A6" }) progress.RegisterBoss(PlanetStageId.A, boss);
        progress.RegisterBoss(PlanetStageId.A, "A2");
        progress.RegisterBoss(PlanetStageId.C, "C1");
        progress.RecordBossDefeat(PlanetStageId.C, "C1");
        Require(!progress.IsComplete(PlanetStageId.C), "A locked boss advanced progress.");
        progress.RecordBossDefeat(PlanetStageId.A, "A2");
        progress.RecordBossDefeat(PlanetStageId.A, "A2");
        progress.RecordBossDefeat(PlanetStageId.A, "missing");
        Require(progress.RequiredBossCount(PlanetStageId.A) == 3 && progress.DefeatedBossCount(PlanetStageId.A) == 1, "Bosses must count once.");
        progress.RecordBossDefeat(PlanetStageId.A, "A4");
        Require(!progress.IsUnlocked(PlanetStageId.B), "B opened before the last boss.");
        progress.RecordBossDefeat(PlanetStageId.A, "A6");
        Require(progress.HighestUnlocked == PlanetStageId.B && !progress.IsComplete(PlanetStageId.B), "Empty B completed itself.");
        Require(owner.RuntimePlanet.transform.Find("Areas/Stage B").gameObject.activeSelf
            && !owner.RuntimePlanet.transform.Find("Areas/Stage C").gameObject.activeSelf, "Stage root activation mismatch.");
        progress.RegisterBoss(PlanetStageId.B, "B1");
        progress.RecordBossDefeat(PlanetStageId.B, "B1");
        Require(progress.HighestUnlocked == PlanetStageId.C, "B did not open C.");
        progress.RecordBossDefeat(PlanetStageId.C, "C1");
        Require(progress.HighestUnlocked == PlanetStageId.D && !progress.IsComplete(PlanetStageId.D), "C did not open D.");
    }

    static void ValidatePersistence()
    {
        string world = "StageValidation-" + Guid.NewGuid().ToString("N");
        string key = "BackHome.Stages.v1." + world;
        GameObject fixture = null;
        PlanetStageProgression Load()
        {
            if (fixture != null) Object.DestroyImmediate(fixture);
            fixture = new GameObject("Stage save fixture");
            fixture.SetActive(false);
            var progress = fixture.AddComponent<PlanetStageProgression>();
            Set(progress, "worldId", world);
            progress.Initialize();
            progress.RegisterBoss(PlanetStageId.A, "first");
            progress.RegisterBoss(PlanetStageId.A, "last");
            return progress;
        }
        try
        {
            var progress = Load();
            progress.RecordBossDefeat(PlanetStageId.A, "first");
            progress = Load();
            Require(progress.DefeatedBossCount(PlanetStageId.A) == 1 && !progress.IsUnlocked(PlanetStageId.B), "Partial save was lost.");
            progress.RecordBossDefeat(PlanetStageId.A, "last");
            progress = Load();
            Require(progress.IsComplete(PlanetStageId.A) && progress.HighestUnlocked == PlanetStageId.B, "Unlock save was lost.");
        }
        finally
        {
            if (fixture != null) Object.DestroyImmediate(fixture);
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
    }

    static Vector3 StagePosition(PlanetTileMap map, PlanetStageId stage)
    {
        for (int lat = 0; lat < map.LatitudeBands; lat++)
            for (int lon = 0; lon < map.LongitudeBands; lon++)
                if (PlanetStage.FromTerrain(map.GetTerrain(lat, lon)) == stage
                    && map.GetHeight(lat, lon) > PlanetTileMap.WaterHeight
                    && map.TryGetCellCenter(lat, lon, out Vector3 position)) return position;
        throw new InvalidOperationException("No terrain found for " + stage);
    }

    static void ValidateMovementAndStreaming(PlanetAuthoring owner, PlanetStageProgression progress)
    {
        Vector3 start = owner.RuntimePlanet.transform.Find("Areas/Stage A/Camp").position;
        Vector3 locked = StagePosition(owner.Tiles, PlanetStageId.B);
        Vector3 constrained = progress.ConstrainMove(start, locked);
        Require(progress.IsPositionUnlocked(constrained) && Vector3.Distance(constrained, locked) > .1f, "Long movement crossed locked terrain.");
        GameObject fixture = new GameObject("Stage streaming fixture");
        try
        {
            var focus = new GameObject("Focus"); focus.transform.SetParent(fixture.transform);
            focus.transform.position = locked;
            var near = new GameObject("Locked prop"); near.transform.SetParent(fixture.transform); near.transform.position = locked;
            var far = new GameObject("Distant prop"); far.transform.SetParent(fixture.transform); far.transform.position = locked + Vector3.one * 1000;
            var streamer = fixture.AddComponent<PlanetEnvironmentStreamer>();
            Set(streamer, "_stageProgression", progress);
            Set(streamer, "_player", focus.transform);
            Set(streamer, "_showSqr", 100f);
            Set(streamer, "_hideSqr", 144f);
            Call(streamer, "TrackInstance", near);
            Call(streamer, "TrackInstance", far);
            Require(!near.activeSelf && !far.activeSelf, "Proximity activated locked content.");
            progress.RegisterBoss(PlanetStageId.A, "last");
            progress.RecordBossDefeat(PlanetStageId.A, "last");
            Call(streamer, "RefreshStageAccess");
            Call(streamer, "UpdateVisibility");
            Require(near.activeSelf && !far.activeSelf, "Unlock did not preserve distance culling.");
            Require(Vector3.Distance(progress.ConstrainMove(locked, locked + Vector3.up * .01f), locked + Vector3.up * .01f) < .001f,
                "Unlocked terrain still blocks movement.");
        }
        finally { Object.DestroyImmediate(fixture); }
    }

    static void ValidateSpawner(PlanetAuthoring owner, PlanetStageProgression progress)
    {
        GameObject fixture = new GameObject("Stage spawn fixture");
        fixture.SetActive(false);
        CreatureSpawner spawner = null;
        try
        {
            var template = new GameObject("Creature fixture"); template.transform.SetParent(fixture.transform);
            template.AddComponent<Creature>();
            var b1 = new GameObject("ValidationB1");
            b1.transform.SetParent(owner.RuntimePlanet.transform.Find("Areas/Stage B"), false);
            b1.transform.position = StagePosition(owner.Tiles, PlanetStageId.B);
            spawner = fixture.AddComponent<CreatureSpawner>();
            Set(spawner, "planet", owner.RuntimePlanet);
            CreatureSpawner.SpawnPoint Point(string area) => new CreatureSpawner.SpawnPoint
            {
                areaId = area, radius = .1f,
                creatures = new[] { new CreatureSpawner.SpawnEntry { prefab = template, count = 1, respawnTime = 4 } },
                king = new CreatureSpawner.KingSpawn { prefab = template, respawnSeconds = 40 }
            };
            Set(spawner, "spawnPoints", new[] { Point("A2"), Point("ValidationB1") });
            spawner.SpawnAll();
            Transform a2 = owner.RuntimePlanet.transform.Find("Areas/Stage A/A2");
            Require(a2.childCount == 2 && b1.transform.childCount == 0, "Locked stage instantiated creatures.");
            Transform existingMob = a2.GetChild(0);
            Creature king = a2.GetChild(1).GetComponent<Creature>();
            var died = (Action<Creature>)typeof(Creature).GetField("Died", Private).GetValue(king);
            Require(died != null, "King death is not subscribed.");
            died(king);
            Require(progress.IsUnlocked(PlanetStageId.B) && b1.transform.childCount == 2, "King defeat did not spawn the next stage.");
            Call(spawner, "OnStageUnlocked", PlanetStageId.B);
            Require(b1.transform.childCount == 2 && a2.GetChild(0) == existingMob, "Unlock duplicated or replaced mobs.");
        }
        finally
        {
            if (spawner != null) spawner.ClearSpawned();
            Object.DestroyImmediate(fixture);
        }
    }

    static void ValidateUnwalkableTerrain(PlanetAuthoring owner, PlanetStageProgression progress)
    {
        PlanetTileMap map = owner.Tiles;
        int middle = map.LatitudeBands / 2;
        for (int row = 0; row < map.LatitudeBands; row++)
        {
            for (int col = 0; col < map.LongitudeBands; col++)
            {
                map.SetHeightSilent(row, col, PlanetTileMap.DefaultGroundHeight);
                map.SetTerrainSilent(row, col, col == 15 ? PlanetTileset.TerrainClay : PlanetTileset.TerrainGrass);
            }
        }
        Call(progress, "BuildStageMap");
        map.TryGetCellCenter(middle, 5, out Vector3 from);
        map.TryGetCellCenter(middle, 15, out Vector3 dirt);
        map.TryGetCellCenter(middle, 25, out Vector3 to);
        Require(map.TryGetTile(dirt, out var sample) && !sample.walkable,
            "Dirt_brightened must be configured as unwalkable.");
        Require(progress.IsPositionUnlocked(dirt), "The dirt fixture must be in an unlocked stage.");

        void RequireBlocked(Vector3 start, Vector3 destination)
        {
            Vector3 stopped = PlanetStageProgression.ConstrainMove(owner.RuntimePlanet, start, destination);
            Require(Vector3.Distance(stopped, destination) > .1f
                && map.TryGetTile(stopped, out var safeTile) && safeTile.walkable,
                "Movement entered or crossed Dirt_brightened.");
        }
        RequireBlocked(from, dirt);
        RequireBlocked(from, to);
        RequireBlocked(to, from);
        for (int i = 1; i <= 4; i++)
        {
            var stage = (PlanetStageId)i;
            progress.RegisterBoss(stage, "terrain-check");
            progress.RecordBossDefeat(stage, "terrain-check");
        }
        Require(progress.IsComplete(PlanetStageId.D), "The fixture must complete all stages.");
        RequireBlocked(from, dirt);
        RequireBlocked(from, to);
        RequireBlocked(to, from);

        for (int row = 0; row < map.LatitudeBands; row++)
            map.SetTerrainSilent(row, 15, PlanetTileset.TerrainDirt);
        Require(map.TryGetTile(dirt, out sample) && sample.walkable, "Dirt_darked must remain walkable.");
        Require(progress.ConstrainMove(from, to) == to && progress.ConstrainMove(to, from) == from,
            "Walkable terrain remained blocked after replacing Dirt_brightened.");
    }

    static void ValidateRoads(PlanetAuthoring owner, PlanetStageProgression progress)
    {
        PlanetTileMap map = owner.Tiles;
        int middle = map.LatitudeBands / 2;
        for (int row = 0; row < map.LatitudeBands; row++)
        {
            for (int col = 0; col < map.LongitudeBands; col++)
            {
                map.SetHeightSilent(row, col, PlanetTileMap.DefaultGroundHeight);
                map.SetTerrainSilent(row, col, row == middle ? PlanetTileset.TerrainDirt
                    : col >= 10 && col <= 20 ? PlanetTileset.TerrainGlowingMushroom : PlanetTileset.TerrainGrass);
            }
        }
        Call(progress, "BuildStageMap");
        map.TryGetCellCenter(middle, 5, out Vector3 from);
        map.TryGetCellCenter(middle, 15, out Vector3 road);
        map.TryGetCellCenter(middle, 25, out Vector3 to);
        Require(progress.StageAt(road) == PlanetStageId.B, "A road bypassed its surrounding biome.");
        Require(progress.IsPositionUnlocked(from) && progress.IsPositionUnlocked(to), "Fixture endpoints should be accessible.");
        Vector3 blocked = progress.ConstrainMove(from, to);
        Require(Vector3.Distance(blocked, to) > 1f && progress.IsPositionUnlocked(blocked), "A large move skipped the locked road strip.");
        progress.RegisterBoss(PlanetStageId.A, "last");
        progress.RecordBossDefeat(PlanetStageId.A, "last");
        Require(progress.ConstrainMove(from, to) == to, "The road did not open after completing A.");
    }
}
