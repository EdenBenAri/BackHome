using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using StarterAssets;
using Object = UnityEngine.Object;

/// <summary>Opt-in deterministic camera regression checks, using Unity's real math and physics.</summary>
[InitializeOnLoad]
public static class CameraFollowValidation
{
    const string Request = "Temp/BackHomeCameraValidation.trigger";
    const string Report = "Logs/CameraFollowValidation.txt";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<Object> Cleanup = new();
    static readonly Vector3 Origin = new Vector3(10000f, 10000f, 10000f);
    static int passed, failed;

    static CameraFollowValidation() => EditorApplication.update += Poll;

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        File.Delete(Request);
        Run();
    }

    [MenuItem("BackHome/Camera/Validate King Camera Follow")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play before running deterministic camera validation.");
        // These editor-only simulations start from the project's normal gameplay clock.
        Time.timeScale = 1f;
        Time.fixedDeltaTime = .02f;
        Directory.CreateDirectory("Logs");
        File.WriteAllText(Report, "King camera regression checks · Unity " + Application.unityVersion + "\n");
        passed = failed = 0;
        Check("Movement, release, U-turns and teleports preserve the view compass and framing", FollowAndTurn);
        Check("Comparable response at 30/60/120 FPS; pause freezes the rig", FrameRates);
        Check("Two full spherical circuits cross both poles without heading flips", SphereTravel);
        Check("Analog sweeps have no 30-degree remap; axes always match the screen", MovementFrame);
        Check("Surrounding enemies / target churn never change heading, distance or FOV", Combat);
        Check("Real floating joystick owns one pointer and clears cancelled gestures", TouchOwnership);
        Check("Shake does not feed back; oxygen FOV and cinematic snap remain usable", EffectsAndSnap);
        Check("Broad scenery colliders cannot collapse the high gameplay camera", Obstacles);
        Check("Cinematic owns the camera; movement and combat never release the returned heading", KingReveal);
        Check("King objectives use stage order and saved first victories, including reload and final boss", KingProgression);
        Check("Planet attachment preserves the player's initial king-facing heading", SpawnHeading);
        Check("King cinematic intro, lethal-hit timing, freeze, minimum hold and seamless return", KingCinematicTiming);
        Check("Saved victories replay once per chapter visit without resetting saved progress", KingCinematicVisitReplay);
        Check("King reveals advance A3 to B1, B3 to C1 and onward in numeric order", KingCinematicStageOrder);
        Check("Camera waits for fog completion; freeze, optional pause and cancellation restore safely", KingCinematicFogWait);
        Check("Input and combat activity cannot interrupt any King cinematic phase", KingCinematicInputIsolation);
        Check("King cinematic cancellation restores time/input and respects another pause owner", KingCinematicCancellation);
        Check("King cinematic steady ticks allocate zero managed bytes while idle and zooming", KingCinematicAllocations);
        File.AppendAllText(Report, $"\nRESULT: {passed} passed, {failed} failed\n");
        if (failed == 0) Debug.Log($"Camera validation: {passed} groups passed. {Report}");
        else Debug.LogError($"Camera validation: {failed} groups failed. {Report}");
    }

    static void Check(string label, Action test)
    {
        float savedScale = Time.timeScale, savedFixed = Time.fixedDeltaTime;
        try { test(); passed++; File.AppendAllText(Report, "PASS " + label + "\n"); }
        catch (Exception e) { failed++; File.AppendAllText(Report, "FAIL " + label + "\n" + e + "\n"); }
        finally
        {
            for (int i = Cleanup.Count - 1; i >= 0; i--)
                if (Cleanup[i] != null) Object.DestroyImmediate(Cleanup[i]);
            Cleanup.Clear();
            Time.timeScale = savedScale;
            Time.fixedDeltaTime = savedFixed;
        }
    }

    static GameObject Make(string name)
    {
        var go = new GameObject("Camera validation: " + name) { hideFlags = HideFlags.HideAndDontSave };
        go.transform.position = Origin;
        Cleanup.Add(go);
        return go;
    }

    static CameraFollow Rig(out Transform player)
    {
        player = Make("target").transform;
        player.gameObject.AddComponent<StarterAssetsInputs>();
        var go = Make("rig");
        go.AddComponent<Camera>().enabled = false;
        var follow = go.AddComponent<CameraFollow>();
        Call(follow, "Awake");
        Set(follow, "preferPlanetRadialUp", false);
        follow.SetOffsets(17f, 22f);
        follow.SetTarget(player);
        Tick(follow);
        return follow;
    }

    static void FollowAndTurn()
    {
        var rig = Rig(out var player);
        Vector3 heading = Heading(rig);
        Require(Vector3.Dot((rig.transform.position - player.position).normalized, player.forward) < -0.5f,
            "Initial camera is in front of the player.");
        var input = player.GetComponent<StarterAssetsInputs>();
        input.BeginMoveGesture(); input.MoveInput(Vector2.right);
        for (int i = 0; i < 600; i++)
        {
            player.position += Vector3.right * .1f;
            player.rotation = Quaternion.Euler(0f, i * 3f, 0f);
            Tick(rig);
            Require(Vector3.Angle(heading, Heading(rig)) < .1f, "Movement steered the camera compass.");
            Vector3 offset = rig.transform.position - rig.CinematicFocus;
            Require(Mathf.Abs(Vector3.Dot(offset, Vector3.up) - 17f) < .01f
                && Mathf.Abs(Vector3.Dot(offset, -heading) - 22f) < .01f,
                "Walking changed the framing distance.");
        }
        input.MoveInput(Vector2.zero); RunFor(rig, 2f);
        input.EndMoveGesture(); player.rotation = Quaternion.Euler(0, 90, 0); RunFor(rig, 12f);
        Require(Vector3.Angle(heading, Heading(rig)) < .1f, "Release recentered behind the player.");
        player.rotation = Quaternion.Euler(0, 270, 0);
        input.BeginMoveGesture(); RunFor(rig, 2f); input.EndMoveGesture(); RunFor(rig, 12f);
        Require(Vector3.Angle(heading, Heading(rig)) < .1f, "New gesture/U-turn changed the compass.");
        Require(Vector3.Distance(rig.transform.position, player.position + Vector3.up * 17f - heading * 22f) < .1f,
            "Positional follow did not settle on the player.");
        player.position += Vector3.right * 500f; Tick(rig);
        Require(Vector3.Distance(rig.transform.position, player.position) < 30f
            && Vector3.Angle(heading, Heading(rig)) < .1f, "Teleport lost the player or changed the compass.");
    }

    static void FrameRates()
    {
        var rig = Rig(out var player);
        float min = float.MaxValue, max = 0f;
        foreach (int fps in new[] { 30, 60, 120 })
        {
            player.position = Origin; player.rotation = Quaternion.identity;
            rig.SetTarget(player); Tick(rig, 1f / fps);
            player.position += Vector3.right * 5f;
            player.rotation = Quaternion.Euler(0f, 90f, 0f);
            RunFor(rig, .3f, fps);
            float error = Vector3.Distance(rig.CinematicFocus, player.position);
            min = Mathf.Min(min, error); max = Mathf.Max(max, error);
            Require(Vector3.Angle(Heading(rig), Vector3.forward) < .1f, "Frame rate changed the compass.");
        }
        Require(max - min < .01f, $"Positional follow varied by {max - min:F3} units across frame rates.");
        Vector3 frozen = rig.transform.position;
        player.position += Vector3.right * 5f; Tick(rig, 0f);
        Require(rig.transform.position == frozen, "Camera advanced during pause.");
    }

    static void SphereTravel()
    {
        var rig = Rig(out var player);
        player.position = Origin + Vector3.up * 80f;
        rig.SetTarget(player); Tick(rig);
        Vector3 previous = Heading(rig);
        for (int i = 1; i <= 1440; i++)
        {
            Quaternion pose = Quaternion.AngleAxis(i * 0.5f, Vector3.right);
            player.SetPositionAndRotation(Origin + pose * Vector3.up * 80f, pose);
            Tick(rig);
            Require(Vector3.Angle(previous, Heading(rig)) < 3f, "Heading flipped near a pole.");
            Require(Vector3.Distance(rig.transform.position, player.position) > 15f, "Orbit collapsed on the sphere.");
            previous = Heading(rig);
        }
        Require(Vector3.Angle(Heading(rig), player.forward) < 10f, "Heading drifted during sphere transport.");
    }

    static void MovementFrame()
    {
        for (int viewAngle = 0; viewAngle < 360; viewAngle += 45)
        {
            Vector3 view = Quaternion.Euler(0, viewAngle, 0) * Vector3.forward;
            Vector3 previous = CameraRelativeMoveFrame.Resolve(Vector2.up, Vector3.up, view);
            for (int angle = 1; angle <= 360; angle++)
            {
                float radians = angle * Mathf.Deg2Rad;
                Vector2 input = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
                Vector3 direction = CameraRelativeMoveFrame.Resolve(input, Vector3.up, view);
                Require(Vector3.Angle(previous, direction) < 1.1f, "Analog sweep hit an angular rebind discontinuity.");
                Vector3 expected = Quaternion.AngleAxis(angle, Vector3.up) * view;
                Require(Vector3.Angle(direction, expected) < 0.1f, "Movement no longer matches the screen axes.");
                previous = direction;
            }
        }
        for (int i = 0; i <= 720; i++)
        {
            Quaternion pose = Quaternion.AngleAxis(i, Vector3.right);
            Vector3 direction = CameraRelativeMoveFrame.Resolve(Vector2.up, pose * Vector3.up, pose * Vector3.forward);
            Require(Vector3.Angle(direction, pose * Vector3.forward) < 0.1f, "Movement frame flipped at a pole.");
        }
    }

    static void Combat()
    {
        var rig = Rig(out var player);
        var vitals = player.gameObject.AddComponent<PlayerVitals>();
        Set(vitals, "_currentHealth", 100f);
        var combat = player.gameObject.AddComponent<PlayerRangeCombat>();
        Set(combat, "_vitals", vitals); Set(combat, "_firing", true);
        var enemies = new Creature[12];
        for (int i = 0; i < enemies.Length; i++)
        {
            enemies[i] = Make("enemy " + i).AddComponent<Creature>();
            enemies[i].transform.position += Quaternion.Euler(0, i * 30f, 0) * Vector3.forward * 8f;
            Set(enemies[i], "_currentHealth", 100f);
        }
        rig.SetTarget(player); Tick(rig);
        Vector3 heading = Heading(rig), initialPosition = rig.transform.position;
        for (int i = 0; i < 600; i++)
        {
            Set(combat, "_firing", i % 30 < 8);
            Get<Creature[]>(combat, "_aimTarget")[0] = enemies[i % enemies.Length];
            player.rotation = Quaternion.Euler(0, i * 19f, 0);
            Tick(rig);
            Require(Vector3.Angle(heading, Heading(rig)) < 0.1f, "Crowd/target churn changed yaw.");
            Require(Vector3.Distance(rig.transform.position, initialPosition) < 0.01f, "Combat changed framing.");
            Require(Mathf.Abs(rig.GetComponent<Camera>().fieldOfView - 60f) < 0.01f, "Combat pumped FOV.");
        }
        Set(combat, "_firing", false); RunFor(rig, 0.5f);
        Require(Vector3.Distance(rig.transform.position, initialPosition) < .01f,
            "A gap between volleys changed the camera.");
        combat.enabled = false;
        RunFor(rig, 15f);
        Require(Vector3.Distance(rig.transform.position, initialPosition) < .01f
            && Vector3.Angle(heading, Heading(rig)) < .1f, "Leaving combat moved the camera.");
    }

    static void TouchOwnership()
    {
        var root = new GameObject("Camera validation: UI", typeof(RectTransform), typeof(Canvas)); Cleanup.Add(root);
        root.hideFlags = HideFlags.HideAndDontSave;
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var output = root.AddComponent<UICanvasControllerInput>();
        var input = Make("touch input").AddComponent<StarterAssetsInputs>(); output.starterAssetsInputs = input;
        var joystick = new GameObject("UI_Virtual_Joystick_Move", typeof(RectTransform)); joystick.transform.SetParent(root.transform, false);
        var handle = new GameObject("Handle", typeof(RectTransform)); handle.transform.SetParent(joystick.transform, false);
        var pad = new GameObject("FloatingTouchPad", typeof(RectTransform)); pad.transform.SetParent(root.transform, false);
        var floating = pad.AddComponent<DynamicFloatingJoystick>();
        floating.Setup((RectTransform)joystick.transform, output); Call(floating, "Awake");
        Canvas.ForceUpdateCanvases();
        var first = new PointerEventData(null) { pointerId = 11, position = new Vector2(Screen.width / 2f, Screen.height / 2f) };
        var other = new PointerEventData(null) { pointerId = 22, position = first.position + Vector2.left * 200f };
        floating.OnPointerDown(first);
        Require(input.IsMoveGestureActive, "Contact was not exposed to the camera.");
        first.position += Vector2.right * 60f; floating.OnDrag(first);
        Require(input.move.x > 0.1f, "Real pointer drag did not reach movement input.");
        Vector2 held = input.move, center = ((RectTransform)joystick.transform).anchoredPosition;
        floating.OnPointerDown(other); floating.OnDrag(other); floating.OnPointerUp(other);
        Require(input.IsMoveGestureActive && input.move == held && ((RectTransform)joystick.transform).anchoredPosition == center,
            "Second finger stole, moved or released the joystick.");
        floating.OnPointerUp(first);
        Require(!input.IsMoveGestureActive && input.move == Vector2.zero, "Release left stale movement.");
        floating.OnPointerDown(first); floating.OnDrag(first); Call(floating, "OnApplicationFocus", false);
        Require(!input.IsMoveGestureActive && input.move == Vector2.zero, "Focus loss left stale movement.");
        floating.OnPointerDown(first); Call(floating, "OnDisable");
        Require(!input.IsMoveGestureActive && input.move == Vector2.zero, "Disabling joystick left a gesture latched.");
    }

    static void EffectsAndSnap()
    {
        var rig = Rig(out var player);
        Vector3 clean = rig.transform.position;
        rig.SetHeartbeatOffset(Vector3.right * 0.5f);
        RunFor(rig, 5f);
        Require(Mathf.Abs(Vector3.Distance(rig.transform.position, clean) - 0.5f) < 0.01f,
            "Heartbeat accumulated into follow position.");
        rig.SetHeartbeatOffset(Vector3.zero); Tick(rig);
        Require(Vector3.Distance(rig.transform.position, clean) < 0.01f, "Shake left a follow drift.");
        rig.SetOxygenFovDelta(-5f); RunFor(rig, 4f);
        Require(Mathf.Abs(rig.GetComponent<Camera>().fieldOfView - 55f) < 0.1f, "Oxygen FOV was lost.");
        rig.SetOxygenFovDelta(0f); RunFor(rig, 4f);
        Require(Mathf.Abs(rig.GetComponent<Camera>().fieldOfView - 60f) < 0.1f, "FOV failed to restore.");
        rig.SetSnapToTarget(true); player.position += Vector3.right * 5f; player.rotation = Quaternion.Euler(0, 90, 0);
        Tick(rig);
        Vector3 expected = player.position + player.up * rig.OffsetHeight - Vector3.forward * rig.OffsetBack;
        Require(Vector3.Distance(rig.transform.position, expected) < 0.01f, "Cinematic snap no longer centers instantly.");
    }

    static void Obstacles()
    {
        var rig = Rig(out var player);
        Vector3 clean = rig.transform.position;
        var wall = Make("wall");
        wall.layer = LayerMask.NameToLayer("Ground");
        wall.transform.position = player.position + Vector3.back * 8f + Vector3.up * 8f;
        wall.AddComponent<BoxCollider>().size = new Vector3(20f, 20f, 1f);
        Physics.SyncTransforms(); Tick(rig);
        Require(Vector3.Distance(rig.transform.position, clean) < 0.01f, "Scenery proxy moved the camera.");
        wall.GetComponent<Collider>().enabled = false; Physics.SyncTransforms(); Tick(rig);
        Require(Vector3.Distance(rig.transform.position, clean) < 0.01f, "Leaving scenery caused camera pumping.");
        RunFor(rig, 4f);
        Require(Vector3.Distance(clean, rig.transform.position) < 0.1f, "Camera never recovered full boom.");
        wall.GetComponent<Collider>().enabled = true;
        wall.transform.position = player.position + Vector3.up * 1.3f;
        Physics.SyncTransforms(); Tick(rig);
        Require(Vector3.Distance(rig.transform.position, player.position) > 20f,
            "A cast starting inside a scenery proxy collapsed the camera into the player.");
    }

    static void KingReveal()
    {
        var rig = Rig(out var player);
        var input = player.GetComponent<StarterAssetsInputs>();
        var combat = player.gameObject.AddComponent<PlayerRangeCombat>();
        Vector3 initialPlayer = player.position;
        rig.BeginCinematic();
        Vector3 returned = player.position + Vector3.up * 17f - Vector3.right * 22f;
        Quaternion returnedRotation = Quaternion.LookRotation(player.position - returned, Vector3.up);
        rig.transform.SetPositionAndRotation(returned, returnedRotation);
        input.BeginMoveGesture(); input.MoveInput(Vector2.left);
        Set(combat, "_firing", true);
        player.position += Vector3.forward * 5f; player.rotation = Quaternion.Euler(0, 180, 0);
        rig.Shake(1f, 2f); rig.ImpactShake(1f, 2f);
        RunFor(rig, 1f);
        Require(Vector3.Distance(rig.transform.position, returned) < .001f
            && Quaternion.Angle(rig.transform.rotation, returnedRotation) < .01f,
            "Movement/combat overwrote cinematic control.");
        Require(Get<object>(rig, "_shakeRoutine") == null, "Combat started a shake during the reveal.");
        player.position = initialPlayer;
        rig.EndCinematic(Vector3.right);
        RunFor(rig, 5f);
        Require(Vector3.Angle(Heading(rig), Vector3.right) < .1f, "Held movement undid the reveal.");
        input.EndMoveGesture(); Set(combat, "_firing", false); RunFor(rig, 12f);
        input.BeginMoveGesture(); input.MoveInput(Vector2.up); RunFor(rig, 2f);
        input.EndMoveGesture(); RunFor(rig, 12f);
        Require(Vector3.Angle(Heading(rig), Vector3.right) < .1f, "New movement/release undid the King heading.");
        rig.SetTarget(player); Tick(rig);
        Require(Vector3.Angle(Heading(rig), Vector3.right) < .1f, "Rebinding the player undid the King heading.");
    }

    static CreatureSpawner KingFixture(string world, out PlanetStageProgression progress)
    {
        var root = Make("king planet"); root.SetActive(false);
        var planet = root.AddComponent<SphericalPlanet>();
        Set(planet, "radius", 80f);
        progress = root.AddComponent<PlanetStageProgression>(); Set(progress, "worldId", world);
        var areas = new GameObject("Areas").transform; areas.SetParent(root.transform, false);
        var a = new GameObject("Stage A").transform; a.SetParent(areas, false); a.gameObject.AddComponent<PlanetStage>();
        var b = new GameObject("Stage B").transform; b.SetParent(areas, false);
        Set(b.gameObject.AddComponent<PlanetStage>(), "stageId", PlanetStageId.B);
        Transform Area(string name, Transform parent, float longitude)
        {
            var area = new GameObject(name).transform; area.SetParent(parent, false);
            area.position = Origin + Quaternion.AngleAxis(longitude, Vector3.forward) * Vector3.up * 80f;
            return area;
        }
        Area("first", a, -25f); Area("second", a, -50f); Area("last", a, -75f); Area("next-stage", b, -100f);
        progress.Initialize();
        foreach (string boss in new[] { "first", "second", "last" }) progress.RegisterBoss(PlanetStageId.A, boss);
        progress.RegisterBoss(PlanetStageId.B, "next-stage");
        var spawner = root.AddComponent<CreatureSpawner>();
        Set(spawner, "planet", planet); Set(spawner, "_stageProgression", progress);
        var prefab = Make("dummy king prefab");
        var points = new CreatureSpawner.SpawnPoint[4];
        string[] names = { "next-stage", "first", "second", "last" };
        for (int i = 0; i < points.Length; i++) points[i] = new CreatureSpawner.SpawnPoint
            { areaId = names[i], king = new CreatureSpawner.KingSpawn { prefab = prefab, respawnSeconds = 40f } };
        Set(spawner, "spawnPoints", points);
        return spawner;
    }

    static void KingProgression()
    {
        string world = "CameraKingValidation-" + Guid.NewGuid().ToString("N");
        string key = "BackHome.Stages.v1." + world;
        try
        {
            var spawner = KingFixture(world, out var progress);
            Require(spawner.TryGetNextKingFacing(Origin + Vector3.up * 80f, out Vector3 forward, out Vector3 up)
                && Vector3.Angle(forward, Vector3.right) < .1f && Vector3.Angle(up, Vector3.up) < .1f,
                "Initial spawn did not face first accessible king along the spherical tangent.");
            var rig = Rig(out var player); rig.BindKingGuidance(spawner); rig.BindKingGuidance(spawner);
            int reveals = 0; spawner.KingDefeatedFirstTime += () => reveals++;
            Call(spawner, "RecordKingDefeat", 0);
            Require(reveals == 0, "Locked boss advanced the camera.");
            Call(spawner, "RecordKingDefeat", 1);
            Require(reveals == 1 && progress.HasDefeatedBoss(PlanetStageId.A, "first")
                && rig.GetComponent<KingCinematicDirector>().CurrentPhase == KingCinematicDirector.Phase.SlowMotion,
                "First victory did not immediately start the bound cinematic.");
            spawner.TryGetNextKingObjective(out Vector3 next);
            Require(Vector3.Distance(next, spawner.transform.Find("Areas/Stage A/second").position) < .001f,
                "First victory did not select the second king.");
            Call(spawner, "RecordKingDefeat", 1);
            Require(reveals == 1, "Respawned king triggered another reveal.");
            rig.BindKingGuidance(null); Object.DestroyImmediate(spawner.gameObject);
            spawner = KingFixture(world, out progress); rig.BindKingGuidance(spawner);
            spawner.KingDefeatedFirstTime += () => reveals++;
            Call(spawner, "RecordKingDefeat", 1);
            Require(reveals == 1 && progress.HasDefeatedBoss(PlanetStageId.A, "first"), "Saved first victory was lost on reload.");
            spawner.TryGetNextKingObjective(out next);
            Require(Vector3.Distance(next, spawner.transform.Find("Areas/Stage A/second").position) < .001f,
                "Loaded game pointed back to an already defeated boss.");
            Call(spawner, "RecordKingDefeat", 2); Call(spawner, "RecordKingDefeat", 3);
            Require(reveals == 3 && spawner.TryGetNextKingObjective(out next)
                && Vector3.Distance(next, spawner.transform.Find("Areas/Stage B/next-stage").position) < .001f,
                "Next stage was not selected after its unlock.");
            Call(spawner, "RecordKingDefeat", 0);
            Require(reveals == 4 && !spawner.TryGetNextKingObjective(out _),
                "Last king left a stale camera destination.");
        }
        finally { PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); }
    }

    static void SpawnHeading()
    {
        var root = Make("spawn planet"); root.SetActive(false);
        var planet = root.AddComponent<SphericalPlanet>(); Set(planet, "radius", 80f);
        var rig = Rig(out var player);
        player.position = Origin + Vector3.up * 80f;
        player.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
        var walker = player.gameObject.AddComponent<PlanetWalker>(); Call(walker, "Awake");
        Set(walker, "_planet", planet);
        Call(walker, "SnapToSurface", Vector3.up);
        rig.SetTarget(null); // First binding happens after the spawn pose is established.
        rig.SetTarget(player); Tick(rig);
        Require(Vector3.Angle(player.forward, Vector3.right) < .1f && Vector3.Angle(Heading(rig), Vector3.right) < .1f,
            "Ground attachment replaced the spawn heading with a world compass.");
    }

    static Creature AddLiveKing(CreatureSpawner spawner, int index, string area)
    {
        var go = Make("species-independent king");
        go.transform.position = PlanetStage.FindArea(spawner.transform.Find("Areas"), area).position;
        var creature = go.AddComponent<Creature>();
        var stats = ScriptableObject.CreateInstance<CreatureStats>(); Cleanup.Add(stats);
        creature.SetStats(stats);
        var liveType = typeof(CreatureSpawner).GetNestedType("LiveKing", BindingFlags.NonPublic);
        var live = Activator.CreateInstance(liveType, true);
        liveType.GetField("spawnPointIndex").SetValue(live, index);
        liveType.GetField("creature").SetValue(live, creature);
        ((System.Collections.IList)Get<object>(spawner, "_kings")).Add(live);
        return creature;
    }

    static KingCinematicDirector CinematicFixture(out CameraFollow rig, out CreatureSpawner spawner)
    {
        Time.timeScale = 1f;
        spawner = KingFixture("Cinematic-" + Guid.NewGuid().ToString("N"), out var progress);
        Set(progress, "persistProgress", false);
        AddLiveKing(spawner, 1, "first"); AddLiveKing(spawner, 2, "second"); AddLiveKing(spawner, 3, "last");
        rig = Rig(out var player);
        player.position = Origin + Vector3.up * 80f;
        rig.SetTarget(player); Tick(rig);
        rig.BindKingGuidance(spawner);
        var director = rig.GetComponent<KingCinematicDirector>();
        Require(director.Settings.firstKingArea == "KingA1", "Intro does not use KingA1 by default.");
        director.Settings.firstKingArea = "first";
        return director;
    }

    static void KingCinematicTiming()
    {
        var director = CinematicFixture(out var rig, out var spawner);
        var input = Get<Transform>(rig, "target").GetComponent<StarterAssetsInputs>();
        input.BeginMoveGesture(); input.MoveInput(Vector2.right);
        Call(director, "Tick", .29f);
        Require(Time.timeScale == 1f && input.IsMoveGestureActive, "Intro stole the free movement window.");
        Call(director, "Tick", .011f);
        Require(director.CurrentPhase == KingCinematicDirector.Phase.ZoomIn && Time.timeScale == 0f
            && input.GameplayBlocked && !input.IsMoveGestureActive && input.move == Vector2.zero, "Intro did not freeze and clear input.");
        input.MoveInput(Vector2.left); input.BeginMoveGesture();
        Require(input.move == Vector2.zero && !input.IsMoveGestureActive, "Input leaked into a reveal.");
        Call(director, "Tick", .65f);
        Require(director.CurrentPhase == KingCinematicDirector.Phase.SpawnHold, "Zoom-in never reached king.");
        Call(director, "Tick", .49f);
        Require(director.CurrentPhase == KingCinematicDirector.Phase.SpawnHold, "Missing animation did not hold for 0.5s.");
        Call(director, "Tick", .011f); Call(director, "Tick", .65f);
        Require(director.CurrentPhase == KingCinematicDirector.Phase.Idle && Time.timeScale == 1f
            && !input.GameplayBlocked, "Intro did not restore gameplay.");
        Vector3 returned = rig.transform.position; Tick(rig);
        Require(Vector3.Distance(returned, rig.transform.position) < .02f, "Camera snapped at handoff.");

        Call(spawner, "RecordKingDefeat", 1);
        Require(director.CurrentPhase == KingCinematicDirector.Phase.SlowMotion && Mathf.Approximately(Time.timeScale, .04f),
            "Lethal-hit event did not immediately enter 4% speed.");
        Call(director, "Tick", .69f);
        Require(director.CurrentPhase == KingCinematicDirector.Phase.SlowMotion, "Victory beat ended before 0.7s.");
        Call(director, "Tick", .011f);
        Require(director.CurrentPhase == KingCinematicDirector.Phase.Turn && Time.timeScale == 0f, "Next-king orbit did not freeze.");
        Call(director, "Tick", .29f);
        Require(director.CurrentPhase == KingCinematicDirector.Phase.Turn, "Orbit ended before 0.3s.");
        Call(director, "Tick", .011f);
        Require(director.CurrentPhase == KingCinematicDirector.Phase.ZoomIn, "Orbit exceeded 0.3s.");
        Call(director, "Tick", .65f); Call(director, "Tick", .5f); Call(director, "Tick", .65f);
        Require(Time.timeScale == 1f && !input.GameplayBlocked, "Victory reveal did not restore controls.");
        Call(spawner, "RecordKingDefeat", 1);
        Require(director.CurrentPhase == KingCinematicDirector.Phase.Idle, "Repeat king kill replayed chapter progression.");
    }

    static void KingCinematicVisitReplay()
    {
        string world = "CameraVisitValidation-" + Guid.NewGuid().ToString("N");
        string key = "BackHome.Stages.v1." + world;
        try
        {
            var seed = KingFixture(world, out var savedProgress);
            foreach (string area in new[] { "first", "second", "last" })
                savedProgress.RecordBossDefeat(PlanetStageId.A, area);
            savedProgress.RecordBossDefeat(PlanetStageId.B, "next-stage");
            string saved = PlayerPrefs.GetString(key);
            Object.DestroyImmediate(seed.gameObject);

            for (int visit = 0; visit < 2; visit++)
            {
                var spawner = KingFixture(world, out var progress);
                var first = AddLiveKing(spawner, 1, "first");
                var second = AddLiveKing(spawner, 2, "second");
                var last = AddLiveKing(spawner, 3, "last");
                var rig = Rig(out var player);
                player.position = Origin + Vector3.up * 80f; rig.SetTarget(player); Tick(rig);
                rig.BindKingGuidance(spawner);
                var director = rig.GetComponent<KingCinematicDirector>();
                director.Settings.firstKingArea = "first";
                Require(director.Settings.replayVictoriesOnChapterEntry, "Per-visit presentation is not enabled by default.");
                Require(progress.IsComplete(PlanetStageId.A) && progress.IsComplete(PlanetStageId.B),
                    "Visit did not load existing progression.");
                int visitEvents = 0, savedEvents = 0;
                spawner.KingDefeatedThisVisit += () => visitEvents++;
                spawner.KingDefeatedFirstTime += () => savedEvents++;
                void CompleteReveal(bool turn)
                {
                    if (turn) Call(director, "Tick", .3f);
                    Call(director, "Tick", .65f); Call(director, "Tick", .5f); Call(director, "Tick", .65f);
                    Require(director.CurrentPhase == KingCinematicDirector.Phase.Idle && Time.timeScale == 1f,
                        "Repeated-visit reveal did not restore gameplay.");
                }
                Call(director, "Tick", .31f);
                Require(Get<Creature>(director, "_featured") == first, "Saved progress skipped the first-area introduction.");
                CompleteReveal(false);
                Call(spawner, "RecordKingDefeat", 1);
                Require(director.CurrentPhase == KingCinematicDirector.Phase.SlowMotion, "Saved victory suppressed the slow-motion beat.");
                Call(director, "Tick", .7f);
                Require(Get<Creature>(director, "_featured") == second, "Visit skipped the saved second king.");
                CompleteReveal(true);
                Call(spawner, "RecordKingDefeat", 1);
                Require(visitEvents == 1 && director.CurrentPhase == KingCinematicDirector.Phase.Idle,
                    "Respawned King replayed its cinematic in the same visit.");
                Call(spawner, "RecordKingDefeat", 2); Call(director, "Tick", .7f);
                Require(Get<Creature>(director, "_featured") == last, "Visit skipped the saved third king.");
                CompleteReveal(true);
                Call(spawner, "RecordKingDefeat", 3); Call(spawner, "RecordKingDefeat", 0);
                Call(director, "Tick", .7f);
                Require(visitEvents == 4 && savedEvents == 0 && Time.timeScale == 1f
                    && director.CurrentPhase == KingCinematicDirector.Phase.Idle,
                    "Chapter completion leaked time or rewrote saved victories.");
                Require(PlayerPrefs.GetString(key) == saved, "Camera replay changed saved stage progress.");
                Object.DestroyImmediate(rig.gameObject); Object.DestroyImmediate(spawner.gameObject);
            }

            var legacy = KingFixture(world, out _);
            var legacyRig = Rig(out _); legacyRig.BindKingGuidance(legacy);
            var legacyDirector = legacyRig.GetComponent<KingCinematicDirector>();
            legacyDirector.Cancel(); legacyDirector.Settings.replayVictoriesOnChapterEntry = false;
            Call(legacy, "RecordKingDefeat", 1);
            Require(legacyDirector.CurrentPhase == KingCinematicDirector.Phase.Idle,
                "The external replay option did not preserve first-saved-defeat mode.");
        }
        finally { PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); }
    }

    static void KingCinematicStageOrder()
    {
        string[] order = { "KingA1", "KingA2", "KingA3", "KingB1", "KingB2", "KingB3",
            "KingC1", "KingC2", "KingC3", "KingD1", "KingD2", "KingD10" };
        var root = Make("complete chapter"); root.SetActive(false);
        var planet = root.AddComponent<SphericalPlanet>(); Set(planet, "radius", 80f);
        var progress = root.AddComponent<PlanetStageProgression>(); Set(progress, "persistProgress", false);
        var areas = new GameObject("Areas").transform; areas.SetParent(root.transform, false);
        var groups = new Transform[4];
        for (int i = 0; i < groups.Length; i++)
        {
            groups[i] = new GameObject("Stage " + (char)('A' + i)).transform;
            groups[i].SetParent(areas, false);
            Set(groups[i].gameObject.AddComponent<PlanetStage>(), "stageId", (PlanetStageId)(i + 1));
        }
        for (int i = 0; i < order.Length; i++)
        {
            var area = new GameObject(order[i]).transform; area.SetParent(groups[i / 3], false);
            area.position = Origin + Quaternion.AngleAxis(-(i + 1) * 15f, Vector3.forward) * Vector3.up * 80f;
        }
        progress.Initialize();
        var spawner = root.AddComponent<CreatureSpawner>();
        Set(spawner, "planet", planet); Set(spawner, "_stageProgression", progress);
        var prefab = Make("chapter king prefab");
        var points = new CreatureSpawner.SpawnPoint[order.Length];
        // Intentionally reverse every row: presentation must not depend on Inspector row order.
        for (int i = 0; i < order.Length; i++)
        {
            progress.RegisterBoss((PlanetStageId)(i / 3 + 1), order[i]);
            points[order.Length - 1 - i] = new CreatureSpawner.SpawnPoint
                { areaId = order[i], king = new CreatureSpawner.KingSpawn { prefab = prefab } };
        }
        Set(spawner, "spawnPoints", points);
        var kings = new Creature[order.Length];
        for (int i = 0; i < order.Length; i++) kings[i] = AddLiveKing(spawner, order.Length - 1 - i, order[i]);
        // The player resumes at A3; A1/A2 are saved victories, not kills in this visit.
        progress.RecordBossDefeat(PlanetStageId.A, "KingA1");
        progress.RecordBossDefeat(PlanetStageId.A, "KingA2");
        Require(!progress.IsUnlocked(PlanetStageId.B), "Stage B unlocked before A3.");
        var rig = Rig(out var player); player.position = Origin + Vector3.up * 80f;
        rig.SetTarget(player); Tick(rig); rig.BindKingGuidance(spawner);
        var director = rig.GetComponent<KingCinematicDirector>(); director.Cancel();
        for (int i = 2; i < order.Length; i++)
        {
            Call(spawner, "RecordKingDefeat", order.Length - 1 - i);
            Call(director, "Tick", .7f);
            if (i == order.Length - 1)
            {
                Require(director.CurrentPhase == KingCinematicDirector.Phase.Idle && Time.timeScale == 1f,
                    "Final chapter king did not restore gameplay.");
                break;
            }
            Require(director.CurrentPhase == KingCinematicDirector.Phase.Turn
                && Get<Creature>(director, "_featured") == kings[i + 1],
                order[i] + " did not present " + order[i + 1]);
            Require(progress.IsUnlocked((PlanetStageId)((i + 1) / 3 + 1)), "Next stage did not unlock before its reveal.");
            Call(director, "Tick", .3f); Call(director, "Tick", .65f);
            Call(director, "Tick", .5f); Call(director, "Tick", .65f);
            Call(spawner, "RecordKingDefeat", order.Length - 1 - i);
            Require(director.CurrentPhase == KingCinematicDirector.Phase.Idle, "Respawn replayed a completed transition.");
        }
    }

    static void KingCinematicFogWait()
    {
        var director = CinematicFixture(out var rig, out var spawner);
        var fog = spawner.gameObject.AddComponent<PlanetStageFog>();
        Set(director, "_stageFog", fog);
        var input = Get<Transform>(rig, "target").GetComponent<StarterAssetsInputs>();
        director.Cancel();
        director.Settings.stageRevealSettleTime = .2f;
        Call(spawner, "RecordKingDefeat", 1); Call(director, "Tick", .3f);
        // A projectile finishes the stage after the victory beat already began.
        Set(fog, "_revealingStagesMask", 1 << (int)PlanetStageId.B);
        Call(director, "Tick", .4f);
        Require(director.CurrentPhase == KingCinematicDirector.Phase.StageReveal && Time.timeScale == 0f
            && input.GameplayBlocked, "Fog wait did not freeze after the victory beat.");
        Vector3 shot = rig.transform.position;
        Call(director, "Tick", 3f); Tick(rig, 0f);
        Require(director.CurrentPhase == KingCinematicDirector.Phase.StageReveal
            && Vector3.Distance(shot, rig.transform.position) < .001f, "Camera moved before fog completion.");
        Set(fog, "_revealingStagesMask", 0);
        Call(director, "Tick", .19f);
        Require(director.CurrentPhase == KingCinematicDirector.Phase.StageReveal, "Configured post-fog pause was skipped.");
        Call(director, "Tick", .011f);
        Require(director.CurrentPhase == KingCinematicDirector.Phase.Turn, "Fog completion did not release the camera.");
        director.Cancel();
        Require(Time.timeScale == 1f && !input.GameplayBlocked, "Fog wait leaked time or input ownership.");

        // Disabling/removing fog while waiting cannot strand the cinematic in a freeze.
        Set(fog, "_revealingStagesMask", 1 << (int)PlanetStageId.C);
        director.Settings.stageRevealSettleTime = 0f;
        Call(spawner, "RecordKingDefeat", 2); Call(director, "Tick", .7f);
        Require(director.CurrentPhase == KingCinematicDirector.Phase.StageReveal, "Second reveal did not wait.");
        Call(fog, "OnDisable"); Call(director, "Tick", .01f);
        Require(!fog.IsRevealing && director.CurrentPhase == KingCinematicDirector.Phase.Turn,
            "Disabled fog left stale reveal state.");
        director.Cancel();
        Set(fog, "_revealingStagesMask", 1 << (int)PlanetStageId.D);
        Call(spawner, "RecordKingDefeat", 3); Call(director, "Tick", .7f); director.Cancel();
        Require(Time.timeScale == 1f && !input.GameplayBlocked
            && director.CurrentPhase == KingCinematicDirector.Phase.Idle, "Cancelling fog wait leaked a freeze.");
    }

    static void KingCinematicInputIsolation()
    {
        var director = CinematicFixture(out var rig, out var spawner);
        var player = Get<Transform>(rig, "target");
        var input = player.GetComponent<StarterAssetsInputs>();
        var combat = player.gameObject.AddComponent<PlayerRangeCombat>();
        var visited = new HashSet<KingCinematicDirector.Phase>();
        void FinishUnderPressure()
        {
            int frames = 0;
            while (director.IsPresenting && frames++ < 600)
            {
                visited.Add(director.CurrentPhase);
                player.rotation = Quaternion.LookRotation(
                    Quaternion.AngleAxis(frames * 31f, player.up) * player.forward, player.up);
                input.BeginMoveGesture(); input.MoveInput(Vector2.right);
                Set(combat, "_firing", frames % 3 != 0);
                Require(input.GameplayBlocked && !input.IsMoveGestureActive && input.move == Vector2.zero,
                    "New input escaped the cinematic lock.");
                Call(director, "Tick", 1f / 60f);
                Vector3 shotPosition = rig.transform.position;
                Quaternion shotRotation = rig.transform.rotation;
                bool ownsCamera = Get<bool>(rig, "_cinematicControl");
                Tick(rig);
                if (ownsCamera)
                    Require(Vector3.Distance(shotPosition, rig.transform.position) < .001f
                        && Quaternion.Angle(shotRotation, rig.transform.rotation) < .01f,
                        "Gameplay follow replaced the King shot.");
            }
            Require(frames < 600 && director.CurrentPhase == KingCinematicDirector.Phase.Idle,
                "Cinematic did not finish under input/combat activity.");
        }
        Call(director, "Tick", .31f); FinishUnderPressure();
        Call(spawner, "RecordKingDefeat", 1); FinishUnderPressure();
        foreach (var phase in new[] { KingCinematicDirector.Phase.SlowMotion, KingCinematicDirector.Phase.Turn,
            KingCinematicDirector.Phase.ZoomIn, KingCinematicDirector.Phase.SpawnHold, KingCinematicDirector.Phase.ZoomOut })
            Require(visited.Contains(phase), "Activity skipped cinematic phase " + phase);
        Vector3 heading = Heading(rig);
        input.BeginMoveGesture(); input.MoveInput(Vector2.left); RunFor(rig, 3f);
        input.EndMoveGesture(); Set(combat, "_firing", false); RunFor(rig, 12f);
        Require(Vector3.Angle(heading, Heading(rig)) < .1f, "Gameplay changed the returned King heading.");
    }

    static void KingCinematicCancellation()
    {
        var director = CinematicFixture(out var rig, out var spawner);
        var input = Get<Transform>(rig, "target").GetComponent<StarterAssetsInputs>();
        float originalFixed = Time.fixedDeltaTime;
        Call(director, "Tick", .31f); director.Cancel();
        Require(Time.timeScale == 1f && Time.fixedDeltaTime == originalFixed && !input.GameplayBlocked,
            "Cancelled intro leaked a freeze/input lock.");
        Call(spawner, "RecordKingDefeat", 1);
        Time.timeScale = 0f; // Another owner pauses during slow motion.
        Call(director, "Tick", .01f);
        Require(Time.timeScale == 0f && Mathf.Approximately(Time.fixedDeltaTime, originalFixed) && !input.GameplayBlocked,
            $"Cancellation overwrote another system's pause: scale={Time.timeScale}, fixed={Time.fixedDeltaTime}/{originalFixed}, blocked={input.GameplayBlocked}, phase={director.CurrentPhase}.");
        Time.timeScale = 1f;
        Call(spawner, "RecordKingDefeat", 2); Call(spawner, "RecordKingDefeat", 3); Call(spawner, "RecordKingDefeat", 0);
        Call(director, "Tick", .71f);
        Require(director.CurrentPhase == KingCinematicDirector.Phase.Idle && Time.timeScale == 1f,
            "Chapter completion left the game frozen or tried to show a missing king.");
    }

    static void KingCinematicAllocations()
    {
        var director = CinematicFixture(out _, out _);
        var tick = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), director,
            typeof(KingCinematicDirector).GetMethod("Tick", Private));
        void Measure(string label)
        {
            for (int i = 0; i < 100; i++) tick(0f);
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < 10000; i++) tick(0f);
            long end = System.Diagnostics.Stopwatch.GetTimestamp();
            bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
            double micros = (end - start) * 1000000.0 / System.Diagnostics.Stopwatch.Frequency / 10000;
            Require(bytes == 0, label + " allocated " + bytes + " bytes.");
            File.AppendAllText(Report, $"PERF {label}: {micros:F3} microseconds/tick; {bytes} managed bytes over 10000 ticks\n");
        }
        tick(.31f); Measure("zoom");
        director.Cancel(); Measure("idle");
    }

    static void RunFor(CameraFollow rig, float seconds, int fps = 60)
    { for (int i = 0; i < Mathf.RoundToInt(seconds * fps); i++) Tick(rig, 1f / fps); }
    static void Tick(CameraFollow rig, float dt = 1f / 60f) => Call(rig, "UpdateCamera", dt);
    static Vector3 Heading(CameraFollow rig) => Get<Vector3>(rig, "_smoothedBack");
    static T Get<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private).GetValue(obj);
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private).SetValue(obj, value);
    static void Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Private).Invoke(obj, args);
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
