using System;
using System.IO;
using System.Reflection;
using StarterAssets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Opt-in real Play validation of the released world, restart, ship and hologram return.</summary>
[InitializeOnLoad]
public static class PlanetPlayValidation
{
    const string StateKey = "BackHome.PlanetPlayValidation.Phase";
    public const string Report = "Logs/PlanetPlayValidation.txt";
    static double started;
    static int errors, surfaceVersion, frameStart;
    static bool sampled;
    static Vector3 initialPosition, lastPosition;
    static float distance;
    static PlanetWalker walker;
    static PlanetTileMap tiles;

    static PlanetPlayValidation()
    {
        EditorApplication.playModeStateChanged += StateChanged;
        EditorApplication.update += Tick;
        SceneManager.sceneLoaded += (scene, mode) => { if (SessionState.GetInt(StateKey, -1) >= 0) ResetSample(); };
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (SessionState.GetInt(StateKey, -1) < 0 || !Application.isPlaying) return;
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            errors++;
            File.AppendAllText(Report, "ERROR: " + message + "\n");
        };
    }

    [MenuItem("BackHome/Nyxara/Validate Play And Travel")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your open scene before Play validation.");
        if (EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0)
            throw new InvalidOperationException("Enable Reload Scene for planet authoring.");
        Directory.CreateDirectory("Logs");
        File.WriteAllText(Report, "Unity " + Application.unityVersion + " · real Editor Play validation, not a device benchmark\n");
        SessionState.SetInt(StateKey, 0);
        EditorSceneManager.OpenScene(PlanetAuthoringTools.ScenePath);
        EditorApplication.isPlaying = true;
    }

    static void ResetSample()
    {
        started = EditorApplication.timeSinceStartup;
        sampled = false; distance = 0; walker = null; tiles = null;
    }

    static void StateChanged(PlayModeStateChange state)
    {
        if (SessionState.GetInt(StateKey, -1) < 0) return;
        if (state == PlayModeStateChange.ExitingEditMode) errors = 0;
        if (state == PlayModeStateChange.EnteredPlayMode) ResetSample();
        if (state != PlayModeStateChange.EnteredEditMode) return;
        SessionState.EraseInt(StateKey);
        EditorApplication.delayCall += () =>
        {
            var owner = Object.FindAnyObjectByType<PlanetAuthoring>();
            if (owner != null)
            {
                Selection.activeGameObject = owner.gameObject;
                PlanetAuthoringPreview.Queue(owner);
                PlanetAuthoringEditor.Frame(owner);
            }
            Debug.Log(File.ReadAllText(Report));
        };
    }

    static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    static void Tick()
    {
        int phase = SessionState.GetInt(StateKey, -1);
        if (phase < 0 || !EditorApplication.isPlaying || !Application.isPlaying || started == 0) return;
        double elapsed = EditorApplication.timeSinceStartup - started;
        if (elapsed < 2) return;
        try
        {
            Check(errors == 0, "Runtime errors: " + errors);
            if (phase == 2)
            {
                Check(SceneManager.GetActiveScene().name == "Ship", "Takeoff did not load Ship");
                Check(SphericalPlanet.Instance == null, "Ship hologram registered as gameplay planet");
                var hologram = Object.FindAnyObjectByType<PlanetHologram>();
                Check(hologram != null, "Ship lost its planet hologram");
                var shell = hologram.transform.Find("VisualShell");
                Check(shell != null && shell.GetComponent<MeshFilter>().sharedMesh != null, "Hologram shell missing");
                Check(hologram.GetComponent<SphereCollider>().isTrigger, "Hologram click collider missing");
                Check(Camera.main != null && Camera.main.GetComponent<ShipPilotHands>() != null, "Ship cockpit camera/pilot missing");
                Check(Object.FindAnyObjectByType<TouchController>() == null, "Planet player leaked into cockpit scene");
                var beam = Object.FindAnyObjectByType<HologramBeam>();
                Check(beam != null && new SerializedObject(beam).FindProperty("hologram").objectReferenceValue != null,
                    "Hologram beam lost its prefab transform reference");
                var ray = new Ray(Camera.main.transform.position, (hologram.transform.position - Camera.main.transform.position).normalized);
                Check(hologram.GetComponent<SphereCollider>().Raycast(ray, out _, 80), "Hologram is not clickable from cockpit camera");
                File.AppendAllText(Report, "PASS Ship: real takeoff handler, cockpit camera/pilot, independent hologram mesh/click collider/beam, no gameplay planet.\n");
                SessionState.SetInt(StateKey, 3); ResetSample(); hologram.Travel(); return;
            }
            if (!sampled)
            {
                Check(SceneManager.GetActiveScene().name == "PlanetNyxara", "Wrong world after travel/restart");
                var owner = Object.FindAnyObjectByType<PlanetAuthoring>();
                var planet = SphericalPlanet.Instance;
                Check(owner != null && owner.IsProjected && planet == owner.RuntimePlanet, "Plane-to-sphere activation failed");
                Check(Object.FindObjectsByType<SphericalPlanet>().Length == 1, "Multiple active planets");
                Check(!owner.AuthoringRoot.gameObject.activeInHierarchy, "Authoring plane is active in Play");
                Check(owner.AuthoringRoot.GetComponentsInChildren<Renderer>(true).Length == 0, "Editor preview leaked into Play");
                Check(owner.AuthoringRoot.GetComponentsInChildren<Collider>(true).Length == 0, "Duplicate plane physics");
                foreach (var collider in Resources.FindObjectsOfTypeAll<MeshCollider>())
                    Check(collider.name != "3D prop picking (editor only)", "Editor picking collider leaked into Play");
                tiles = planet.GetComponent<PlanetTileMap>();
                Check(tiles != null && tiles.WalkMeshCollider != null && tiles.WalkMeshCollider.enabled, "No terrain collider");
                Check(tiles.WalkMeshCollider.sharedMesh == tiles.WalkMeshCollider.GetComponent<MeshFilter>().sharedMesh, "Render/collider mismatch");
                var baseCollider = planet.GetComponent<SphereCollider>();
                Check(baseCollider == null || !baseCollider.enabled, "Duplicate spherical floor");
                walker = Object.FindAnyObjectByType<PlanetWalker>();
                Check(walker != null && walker.IsWalkingOnPlanet && !walker.LockLocomotion, "Planet locomotion failed");
                Check(Object.FindObjectsByType<TouchController>().Length == 1, "Duplicate player");
                Check(Object.FindObjectsByType<PlayerDeathUI>().Length == 1, "Duplicate/missing HUD");
                Check(Object.FindAnyObjectByType<CreatureSpawner>() != null && Object.FindObjectsByType<Creature>().Length > 0, "Creature spawning failed");
                Check(Camera.main != null && Camera.main.GetComponent<CameraFollow>() != null, "Follow camera missing");
                Physics.SyncTransforms();
                for (int i = 0; i < 64; i++)
                {
                    Vector3 dir = PlanetTileMap.StudyLonLatToDirection(i * 137.508f, -75f + i % 16 * 10f);
                    Check(tiles.WalkMeshCollider.Raycast(new Ray(planet.Center + dir * 160, -dir), out _, 200), "Terrain ray miss " + i);
                }
                initialPosition = lastPosition = walker.transform.position;
                frameStart = Time.frameCount; surfaceVersion = tiles.SurfaceVersion; sampled = true;
                File.AppendAllText(Report, $"World phase {phase}: {owner.ProjectedObjectCount} poses; projection {owner.ProjectionMilliseconds:F3} ms; activation {owner.RuntimeStartupMilliseconds:F3} ms; 64/64 probes.\n");
            }
            distance += Vector3.Distance(lastPosition, walker.transform.position); lastPosition = walker.transform.position;
            walker.GetComponent<StarterAssetsInputs>().MoveInput(elapsed < 6 ? Vector2.up : Vector2.zero);
            if (elapsed < 8) return;
            Check(distance > 3 && Vector3.Distance(initialPosition, walker.transform.position) > 2, "Player movement failed");
            Check(tiles.SurfaceVersion == surfaceVersion, "Terrain rebuilt during steady Play");
            Check(Time.frameCount - frameStart >= 60, "Insufficient gameplay frames");
            Check(Vector3.Dot(walker.transform.up, SphericalPlanet.Instance.GetUpAt(walker.transform.position)) > .98f, "Radial alignment failed");
            File.AppendAllText(Report, $"PASS world phase {phase}: walked {distance:F2} m, stable terrain, radial alignment, zero errors.\n");
            if (phase == 0)
            {
                SessionState.SetInt(StateKey, 1); ResetSample();
                Object.FindAnyObjectByType<PlayerDeathUI>().OnContinueClicked();
            }
            else if (phase == 1)
            {
                var prompt = Object.FindAnyObjectByType<ShipTakeoffPrompt>();
                Check(prompt != null, "Takeoff prompt missing");
                SessionState.SetInt(StateKey, 2); ResetSample();
                typeof(ShipTakeoffPrompt).GetMethod("TakeOff", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(prompt, null);
            }
            else
            {
                File.AppendAllText(Report, "PASS: world → restart → Ship → hologram → world; all checks completed.\n");
                started = 0; EditorApplication.isPlaying = false;
            }
        }
        catch (Exception error)
        {
            File.AppendAllText(Report, "FAIL: " + error + "\n");
            if (walker != null) walker.GetComponent<StarterAssetsInputs>().MoveInput(Vector2.zero);
            started = 0; EditorApplication.isPlaying = false;
        }
    }
}
