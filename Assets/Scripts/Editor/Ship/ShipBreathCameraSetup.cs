using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Wires the sitting astronaut onto the Ship scene camera.
/// Menu: BackHome → Ship → Add Sitting Pilot Hands
/// </summary>
public static class ShipBreathCameraSetup
{
    const string ScenePath = "Assets/Scenes/Ship.unity";

    static readonly string ProbeTriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeProbeSitting.trigger");
    static readonly string ProbeResultPath = Path.Combine(Application.dataPath, "..", "Temp", "ShipSittingProbe.result.txt");
    static readonly string PilotTriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeShipPilotHands.trigger");
    static readonly string PilotResultPath = Path.Combine(Application.dataPath, "..", "Temp", "ShipPilotHands.result.txt");
    const string SittingFbxPath = "Assets/Resources/Player/Models/Starbot_Animation_Sitting.fbx";

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

        if (File.Exists(ProbeTriggerPath))
        {
            try { File.Delete(ProbeTriggerPath); } catch { /* ignore */ }
            ProbeSitting();
            return;
        }

        if (File.Exists(PilotTriggerPath))
        {
            try { File.Delete(PilotTriggerPath); } catch { /* ignore */ }
            AttachPilot();
        }
    }

    // Wires the sitting astronaut onto the Ship camera without moving it.
    [MenuItem("BackHome/Ship/Add Sitting Pilot Hands")]
    public static void AttachPilot()
    {
        try
        {
            var importer = AssetImporter.GetAtPath(SittingFbxPath) as ModelImporter;
            if (importer == null)
                throw new System.InvalidOperationException("Missing sitting FBX.");
            if (!importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (scene.isDirty)
                    throw new System.InvalidOperationException("Ship scene is not active, and the open scene has unsaved changes.");
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            Camera camera = null;
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                camera = roots[i].GetComponentInChildren<Camera>(true);
                if (camera != null)
                    break;
            }

            if (camera == null)
                throw new System.InvalidOperationException("No camera in the Ship scene.");

            if (camera.GetComponent<ShipPilotHands>() == null)
                camera.gameObject.AddComponent<ShipPilotHands>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            File.WriteAllText(PilotResultPath, "ok " + camera.transform.position.ToString("F3"));
            Debug.Log("[BackHome] Sitting pilot hands attached. Camera stays at " + camera.transform.position.ToString("F3"));
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            try { File.WriteAllText(PilotResultPath, "FAILED: " + ex.Message); } catch { /* ignore */ }
        }
    }

    public static void ProbeSitting()
    {
        var report = new System.Text.StringBuilder();
        GameObject instance = null;
        try
        {
            const string path = "Assets/Resources/Player/Models/Starbot_Animation_Sitting.fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
            else
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new System.InvalidOperationException("Missing sitting FBX");

            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            AnimationClip clip = null;
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < assets.Length; i++)
            {
                var candidate = assets[i] as AnimationClip;
                if (candidate == null || candidate.name.Contains("__preview__"))
                    continue;
                report.AppendLine("found clip " + candidate.name + " length " + candidate.length.ToString("F2"));
                if (clip == null || candidate.length > clip.length)
                    clip = candidate;
            }

            if (clip == null)
                throw new System.InvalidOperationException("Missing sitting clip");

            report.AppendLine("using clip " + clip.name + " length " + clip.length.ToString("F2"));

            Transform head = null, left = null, right = null, hips = null;
            var bones = instance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < bones.Length; i++)
            {
                string n = bones[i].name;
                if (n.EndsWith("Head") && head == null)
                    head = bones[i];
                else if (n.EndsWith("LeftHand") && left == null)
                    left = bones[i];
                else if (n.EndsWith("RightHand") && right == null)
                    right = bones[i];
                else if (n.EndsWith("Hips") && hips == null)
                    hips = bones[i];
            }

            float[] samples = { 0f, 0.35f, 0.7f };
            for (int s = 0; s < samples.Length; s++)
            {
                float t = clip.length * samples[s];
                clip.SampleAnimation(instance, t);
                Vector3 hands = left != null && right != null ? (left.position + right.position) * 0.5f : head.position;
                string hipsFwd = hips != null ? hips.forward.ToString("F2") : "none";
                report.AppendLine("t " + t.ToString("F2")
                    + " head " + head.position.ToString("F2")
                    + " hands " + hands.ToString("F2")
                    + " look " + (hands - head.position).ToString("F2")
                    + " hipsFwd " + hipsFwd
                    + " left " + (left != null ? left.position.ToString("F2") : "-")
                    + " right " + (right != null ? right.position.ToString("F2") : "-"));
            }

            clip.SampleAnimation(instance, clip.length * 0.35f);

            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            report.AppendLine("renderers " + renderers.Length);
            for (int i = 0; i < renderers.Length; i++)
            {
                var skin = renderers[i] as SkinnedMeshRenderer;
                int verts = 0;
                int weights = 0;
                bool readable = false;
                if (skin != null && skin.sharedMesh != null)
                {
                    verts = skin.sharedMesh.vertexCount;
                    readable = skin.sharedMesh.isReadable;
                    if (readable)
                        weights = skin.sharedMesh.boneWeights.Length;
                }
                report.AppendLine(renderers[i].GetType().Name + " " + renderers[i].name
                    + " verts " + verts + " readable " + readable + " weights " + weights
                    + " bones " + (skin != null ? skin.bones.Length : 0));
            }

            if (head != null)
            {
                Vector3 hands = left != null && right != null
                    ? (left.position + right.position) * 0.5f
                    : head.position;
                report.AppendLine("head " + head.position.ToString("F3") + " fwd " + head.forward.ToString("F2") + " up " + head.up.ToString("F2"));
                report.AppendLine("hands " + hands.ToString("F3") + " reach " + Vector3.Distance(head.position, hands).ToString("F3"));
                if (left != null)
                    report.AppendLine("left " + left.position.ToString("F3"));
                if (right != null)
                    report.AppendLine("right " + right.position.ToString("F3"));
                Vector3 look = hands - head.position;
                report.AppendLine("look " + look.ToString("F3"));
            }

            var filters = instance.GetComponentsInChildren<MeshFilter>(true);
            Bounds bounds = new Bounds(instance.transform.position, Vector3.zero);
            bool has = false;
            var skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skins.Length; i++)
            {
                if (!has)
                {
                    bounds = skins[i].bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(skins[i].bounds);
                }
            }
            report.AppendLine("bounds " + bounds.size.ToString("F3") + " center " + bounds.center.ToString("F3"));
            report.Insert(0, "ok\n");
        }
        catch (System.Exception ex)
        {
            report.AppendLine("FAILED " + ex);
        }
        finally
        {
            if (instance != null)
                Object.DestroyImmediate(instance);
        }

        File.WriteAllText(ProbeResultPath, report.ToString());
        Debug.Log("[BackHome] Sitting probe\n" + report);
    }
}
