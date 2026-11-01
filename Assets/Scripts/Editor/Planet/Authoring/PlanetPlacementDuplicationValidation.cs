using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Opt-in duplication regression and saved-world repair, without discarding authored poses.</summary>
[InitializeOnLoad]
public static class PlanetPlacementDuplicationValidation
{
    const string Trigger = "Temp/BackHomePlacementDuplication.trigger";
    public const string Report = "Logs/PlanetPlacementDuplication.txt";

    static PlanetPlacementDuplicationValidation() => EditorApplication.update += Poll;

    static void Poll()
    {
        if (!File.Exists(Trigger) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string request = File.ReadAllText(Trigger).Trim();
        File.Delete(Trigger);
        try
        {
            if (request == "repair") RepairSavedWorld();
            Run();
        }
        catch (Exception error)
        {
            File.AppendAllText(Report, "FAIL " + error + "\n");
            Debug.LogException(error);
        }
    }

    static void RepairSavedWorld()
    {
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        bool useStage = stage != null && stage.assetPath == PlanetAuthoringTools.PrefabPath;
        GameObject root = useStage ? stage.prefabContentsRoot
            : PrefabUtility.LoadPrefabContents(PlanetAuthoringTools.PrefabPath);
        try
        {
            var owner = root.GetComponent<PlanetAuthoring>();
            PlanetPlacementDuplication.Repair(owner, useStage);
            Require(owner.ValidateConfiguration(out string reason), reason);
            PrefabUtility.SaveAsPrefabAsset(root, PlanetAuthoringTools.PrefabPath);
        }
        finally { if (!useStage) PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("BackHome/ChapterOne/Validate Placement Duplication")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play first.");
        Directory.CreateDirectory("Logs");
        File.WriteAllText(Report, "Placement duplication regression - Unity " + Application.unityVersion + "\n");
        string prefabBefore = File.ReadAllText(PlanetAuthoringTools.PrefabPath);
        string sceneBefore = File.ReadAllText(PlanetAuthoringTools.ScenePath);
        string temporaryPath = "Assets/PlacementDuplication-" + Guid.NewGuid().ToString("N") + ".prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(PlanetAuthoringTools.PrefabPath);
        try
        {
            var owner = root.GetComponent<PlanetAuthoring>();
            Require(owner.ValidateConfiguration(out string reason), "Saved world: " + reason);
            Require(PlanetPlacementDuplication.Repair(owner, false) == 0, "Saved world still needs repair.");
            CheckStages(owner);
            File.AppendAllText(Report, "PASS saved world has independent A/B area and king markers in matching stages.\n");

            var original = owner.AuthoringRoot.Find("Areas/Stage A/A1").GetComponent<PlanetPlacement>();
            Transform originalTarget = original.RuntimeTarget;
            PlanetAuthoringPreview.Rebuild(owner);
            var copy = Object.Instantiate(original.gameObject, owner.AuthoringRoot.Find("Areas/Stage B"))
                .GetComponent<PlanetPlacement>();
            copy.name = "B_Duplicate_Test";
            copy.transform.SetPositionAndRotation(copy.transform.position + new Vector3(3, 1, 7), Quaternion.Euler(7, 83, 2));
            copy.transform.localScale = new Vector3(2, 3, 4);
            copy.gameObject.SetActive(false);
            Vector3 position = copy.transform.position, scale = copy.transform.localScale;
            Quaternion rotation = copy.transform.rotation;
            Vector3 ratio = copy.RuntimeScaleRatio;
            Require(!owner.ValidateConfiguration(out _), "Duplicate fixture was not ambiguous.");
            Require(PlanetPlacementDuplication.Repair(owner, false) > 0, "Duplicate was not repaired.");
            Require(original.RuntimeTarget == originalTarget && copy.RuntimeTarget != originalTarget, "Original binding changed.");
            Require(copy.transform.Find("Prop preview") == null && original.transform.Find("Prop preview") != null,
                "Copied disposable preview leaked or original preview was removed.");
            Require(copy.RuntimeTarget.parent.name == "Stage B" && copy.RuntimeTarget.name == copy.name, "Wrong runtime stage/name.");
            Require(copy.transform.position == position && copy.transform.localScale == scale
                && Quaternion.Angle(copy.transform.rotation, rotation) < .01f && copy.RuntimeScaleRatio == ratio,
                "Repair changed the authored pose or scale ratio.");
            copy.Apply(owner);
            Require(copy.RuntimeTarget.localScale == Vector3.Scale(copy.transform.lossyScale, ratio)
                && !copy.RuntimeTarget.gameObject.activeSelf, "Scale or disabled state was lost on projection.");
            File.AppendAllText(Report, "PASS Ctrl+D creates a separate target; edited pose, nonuniform scale, ratio and disabled state survive.\n");

            copy.name = "C_Duplicate_Test";
            copy.transform.SetParent(owner.AuthoringRoot.Find("Areas/Stage C"), true);
            PlanetPlacementDuplication.Repair(owner, false);
            Require(copy.RuntimeTarget.parent.name == "Stage C" && copy.RuntimeTarget.name == copy.name,
                "Rename/reparent after duplication left a stale runtime area.");
            Require(PlanetPlacementDuplication.Repair(owner, false) == 0, "Repair is not idempotent.");
            Require(owner.ValidateConfiguration(out reason), reason);
            File.AppendAllText(Report, "PASS later rename/stage move synchronizes lookup and membership; repeated repair creates nothing.\n");

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            var undoCopy = Object.Instantiate(original.gameObject, original.transform.parent).GetComponent<PlanetPlacement>();
            Undo.RegisterCreatedObjectUndo(undoCopy.gameObject, "Test placement duplicate");
            undoCopy.name = "Undo_Duplicate_Test";
            PlanetPlacementDuplication.Repair(owner);
            Transform undoTarget = undoCopy.RuntimeTarget;
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            Undo.PerformUndo();
            Require(undoCopy == null && undoTarget == null && original.RuntimeTarget == originalTarget,
                "Undo did not remove both the new handle and its runtime copy.");
            Undo.PerformRedo();
            var redone = original.transform.parent.Find("Undo_Duplicate_Test").GetComponent<PlanetPlacement>();
            Require(redone.RuntimeTarget != null && redone.RuntimeTarget != originalTarget
                && owner.ValidateConfiguration(out reason), "Redo restored an ambiguous binding.");
            File.AppendAllText(Report, "PASS Undo/Redo preserves a single independent binding.\n");

            PlanetAuthoringPreview.ClearAll();
            PrefabUtility.SaveAsPrefabAsset(root, temporaryPath);
            var restoredRoot = PrefabUtility.LoadPrefabContents(temporaryPath);
            try
            {
                var restored = restoredRoot.GetComponent<PlanetAuthoring>();
                Require(restored.ValidateConfiguration(out reason), "Reload: " + reason);
                var restoredCopy = restored.AuthoringRoot.Find("Areas/Stage C/C_Duplicate_Test").GetComponent<PlanetPlacement>();
                Require(restoredCopy.RuntimeTarget.parent.name == "Stage C" && restoredCopy.RuntimeScaleRatio == ratio,
                    "Prefab reload lost binding or scale ratio.");
                Require(PlanetPlacementDuplication.Repair(restored, false) == 0, "Reload needs another repair.");
            }
            finally { PrefabUtility.UnloadPrefabContents(restoredRoot); }
            File.AppendAllText(Report, "PASS repaired references survive prefab save/reload.\n");
        }
        finally
        {
            PlanetAuthoringPreview.ClearAll();
            PrefabUtility.UnloadPrefabContents(root);
            if (File.Exists(temporaryPath)) AssetDatabase.DeleteAsset(temporaryPath);
            PlanetAuthoringPreview.RefreshAll();
        }
        Require(File.ReadAllText(PlanetAuthoringTools.PrefabPath) == prefabBefore
            && File.ReadAllText(PlanetAuthoringTools.ScenePath) == sceneBefore, "Regression modified production assets.");
        File.AppendAllText(Report, "PASS all duplication checks; regression left production scene/prefab unchanged.\n");
    }

    static void CheckStages(PlanetAuthoring owner)
    {
        var targets = new HashSet<Transform>();
        foreach (var handle in owner.AuthoringRoot.GetComponentsInChildren<PlanetPlacement>(true))
            Require(targets.Add(handle.RuntimeTarget), "Repeated runtime target: " + handle.name);
        foreach (string stage in new[] { "A", "B" })
        foreach (string prefix in new[] { "", "King" })
        for (int i = 1; i <= 3; i++)
        {
            string name = prefix + stage + i;
            var handle = owner.AuthoringRoot.Find("Areas/Stage " + stage + "/" + name).GetComponent<PlanetPlacement>();
            Require(handle.RuntimeTarget.name == name && handle.RuntimeTarget.parent.name == "Stage " + stage,
                "Wrong binding for " + name);
        }
    }

    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
