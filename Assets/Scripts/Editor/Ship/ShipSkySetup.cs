using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Places the Nyxara procedural sky in the Ship scene, without planets.
/// Menu: BackHome → Ship → Add Space Sky
/// </summary>
public static class ShipSkySetup
{
    const string ScenePath = "Assets/Scenes/Ship.unity";
    const string PrefabPath = "Assets/AssetStore/Birich Studio/ProceduralSpace/SpaceBackground.prefab";
    const string ProfilePath = "Assets/Resources/Galaxy/Skybox/SpaceSkyBox.asset";
    const string MaterialPath = "Assets/AssetStore/Birich Studio/ProceduralSpace/Shaders/ProceduralSpace.mat";

    static readonly string ResultPath = Path.Combine(Application.dataPath, "..", "Temp", "ShipSky.result.txt");
    static readonly string TriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeShipSky.trigger");

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.update += WatchTrigger;
        EditorApplication.delayCall += WatchTrigger;
    }

    static void WatchTrigger()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (!File.Exists(TriggerPath))
            return;

        try { File.Delete(TriggerPath); } catch { /* ignore */ }
        AddFromMenu();
    }

    [MenuItem("BackHome/Ship/Add Space Sky")]
    public static void AddFromMenu()
    {
        try
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (scene.isDirty)
                    throw new System.InvalidOperationException("Ship scene is not active, and the open scene has unsaved changes.");

                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                throw new System.InvalidOperationException("Missing Space Background prefab.");

            var profile = AssetDatabase.LoadAssetAtPath<BirichStudio.ProceduralSpace.SpaceBackgroundProfile>(ProfilePath);
            if (profile == null)
                throw new System.InvalidOperationException("Missing SpaceSkyBox profile.");

            Camera camera = FindCamera(scene);
            if (camera == null)
                throw new System.InvalidOperationException("No camera in the Ship scene.");

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.1f, 0.16f, 0.42f, 1f);
            if (camera.farClipPlane < 1500f)
                camera.farClipPlane = 1500f;

            GameObject sky = FindSky(scene);
            if (sky == null)
            {
                sky = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                sky.name = "Space Background";
            }

            var background = sky.GetComponent<BirichStudio.ProceduralSpace.SpaceBackground>();
            if (background == null)
                throw new System.InvalidOperationException("Space Background component missing.");

            var backgroundData = new SerializedObject(background);
            backgroundData.FindProperty("profile").objectReferenceValue = profile;
            backgroundData.FindProperty("randomizeSeedOnPlay").boolValue = false;
            backgroundData.ApplyModifiedPropertiesWithoutUndo();

            var fit = sky.GetComponent<BirichStudio.ProceduralSpace.SpaceCameraFit>();
            if (fit != null)
            {
                var fitData = new SerializedObject(fit);
                fitData.FindProperty("targetCamera").objectReferenceValue = camera;
                fitData.FindProperty("distance").floatValue = 200f;
                fitData.ApplyModifiedPropertiesWithoutUndo();
            }

            if (sky.GetComponent<SpaceSkyWithoutPlanets>() == null)
                sky.AddComponent<SpaceSkyWithoutPlanets>();

            var skyMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var renderer = sky.GetComponent<MeshRenderer>();
            if (renderer != null && skyMaterial != null)
                renderer.sharedMaterial = skyMaterial;

            PlaceInFrontOf(sky.transform, camera, 200f);
            background.Rebuild();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            File.WriteAllText(ResultPath, "ok " + sky.name);
            Debug.Log("[BackHome] Space sky is in the Ship scene, without planets.");
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            try { File.WriteAllText(ResultPath, "FAILED: " + ex); } catch { /* ignore */ }
        }
    }

    static Camera FindCamera(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            var camera = roots[i].GetComponentInChildren<Camera>(true);
            if (camera != null)
                return camera;
        }

        return null;
    }

    static void PlaceInFrontOf(Transform sky, Camera camera, float distance)
    {
        float d = Mathf.Clamp(distance, camera.nearClipPlane * 2f, camera.farClipPlane * 0.9f);
        sky.SetPositionAndRotation(
            camera.transform.position + camera.transform.forward * d,
            camera.transform.rotation);

        float height = 2f * d * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float width = height * Mathf.Max(0.01f, camera.aspect);
        const float padding = 1.15f;
        sky.localScale = new Vector3(width * padding, height * padding, 1f);
    }

    static GameObject FindSky(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].GetComponent<BirichStudio.ProceduralSpace.SpaceBackground>() != null)
                return roots[i];
        }

        return null;
    }
}
