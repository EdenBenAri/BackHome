using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Fits the shared Hud prefab to the ship cockpit without changing the planet layout.
/// Menu: BackHome → Ship → Arrange HUD
/// </summary>
public static class ShipHudArrange
{
    const string ScenePath = "Assets/Scenes/Ship.unity";

    static readonly string TriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeShipHud.trigger");
    static readonly string ResultPath = Path.Combine(Application.dataPath, "..", "Temp", "ShipHud.result.txt");

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.update += WatchTrigger;
    }

    static void WatchTrigger()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (!File.Exists(TriggerPath))
            return;

        try { File.Delete(TriggerPath); } catch { /* ignore */ }
        Arrange();
    }

    [MenuItem("BackHome/Ship/Arrange HUD")]
    public static void Arrange()
    {
        var report = new StringBuilder();
        try
        {
            Scene scene = SceneManager.GetActiveScene();
            bool opened = false;
            if (!scene.IsValid() || scene.path != ScenePath)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                opened = true;
            }

            GameObject hud = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Hud")
                {
                    hud = root;
                    break;
                }
            }

            if (hud == null)
            {
                report.AppendLine("missing Hud");
                return;
            }

            int disabledEvents = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == hud)
                    continue;
                if (root.GetComponent<EventSystem>() == null)
                    continue;
                root.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(root);
                disabledEvents++;
            }

            RectTransform warning = FindNamed(hud.transform, "Frame", "LowOxygenWarning");
            if (warning != null)
            {
                warning.anchorMin = new Vector2(0.5f, 1f);
                warning.anchorMax = new Vector2(0.5f, 1f);
                warning.pivot = new Vector2(0.5f, 1f);
                warning.anchoredPosition = new Vector2(0f, -28f);
                warning.sizeDelta = new Vector2(760f, 108f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(warning);
                report.AppendLine("oxygen warning pinned to top of the windshield");
            }
            else
            {
                report.AppendLine("missing oxygen frame");
            }

            RectTransform bar = FindNamed(hud.transform, "BottomBar", "InventoryPanel");
            if (bar != null)
            {
                bar.anchorMin = new Vector2(0f, 0f);
                bar.anchorMax = new Vector2(1f, 0f);
                bar.pivot = new Vector2(0.5f, 0f);
                bar.anchoredPosition = new Vector2(0f, 36f);
                bar.sizeDelta = new Vector2(0f, 168f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(bar);
                report.AppendLine("inventory bar lifted above the bottom edge");
            }
            else
            {
                report.AppendLine("missing bottom bar");
            }

            report.Append("disabled extra event systems ").Append(disabledEvents).AppendLine();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (opened)
                EditorSceneManager.CloseScene(scene, true);
            report.AppendLine("OK");
        }
        catch (System.Exception ex)
        {
            report.AppendLine(ex.ToString());
        }
        finally
        {
            File.WriteAllText(ResultPath, report.ToString());
        }
    }

    static RectTransform FindNamed(Transform root, string name, string parentName)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t.name != name)
                continue;
            if (parentName != null && (t.parent == null || t.parent.name != parentName))
                continue;
            return t as RectTransform;
        }

        return null;
    }
}
