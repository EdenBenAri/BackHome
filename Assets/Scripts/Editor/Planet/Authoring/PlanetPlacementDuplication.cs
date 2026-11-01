using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Keep ordinary Unity duplication of flat handles independent of its source.</summary>
[InitializeOnLoad]
public static class PlanetPlacementDuplication
{
    const string UndoName = "Duplicate planet placement";
    static bool queued;

    static PlanetPlacementDuplication()
    {
        EditorApplication.hierarchyChanged += Queue;
        EditorApplication.playModeStateChanged += state =>
        {
            // Also cover Ctrl+D followed immediately by Play, before the delayed editor pass.
            if (state == PlayModeStateChange.ExitingEditMode) RepairLoadedWorlds();
        };
        Queue();
    }

    static void Queue()
    {
        if (queued || EditorApplication.isPlayingOrWillChangePlaymode) return;
        queued = true;
        EditorApplication.delayCall += () =>
        {
            queued = false;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) RepairLoadedWorlds();
        };
    }

    public static void RepairLoadedWorlds()
    {
        if (EditorApplication.isPlaying) return;
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null)
        {
            var owner = stage.prefabContentsRoot.GetComponent<PlanetAuthoring>();
            if (owner != null && Repair(owner) > 0) PlanetAuthoringPreview.Queue(owner);
            return;
        }
        foreach (var owner in Resources.FindObjectsOfTypeAll<PlanetAuthoring>())
        {
            if (EditorUtility.IsPersistent(owner) || !owner.gameObject.scene.IsValid()
                || EditorSceneManager.IsPreviewScene(owner.gameObject.scene)) continue;
            if (Repair(owner) > 0) PlanetAuthoringPreview.Queue(owner);
        }
    }

    /// <summary>Repair duplicate references without changing any handle's pose or scale ratio.</summary>
    public static int Repair(PlanetAuthoring owner, bool recordUndo = true)
    {
        if (owner == null || owner.RuntimePlanet == null || owner.AuthoringRoot == null
            || owner.RuntimePlanet.gameObject.activeSelf) return 0;
        var placements = owner.AuthoringRoot.GetComponentsInChildren<PlanetPlacement>(true);
        var keepers = new Dictionary<Transform, PlanetPlacement>();
        // Prefer the original name and group even when a copy is reordered before the original.
        foreach (var placement in placements)
        {
            Transform target = placement.RuntimeTarget;
            if (target == null || !target.IsChildOf(owner.RuntimePlanet.transform)) continue;
            if (!keepers.TryGetValue(target, out var keeper)
                || MatchScore(owner, placement) > MatchScore(owner, keeper)) keepers[target] = placement;
        }

        int changes = 0;
        int undoGroup = Undo.GetCurrentGroup();
        foreach (var placement in placements)
        {
            Transform target = placement.RuntimeTarget;
            if (target == null || !keepers.TryGetValue(target, out var keeper)) continue;
            if (placement != keeper)
            {
                // Ctrl+D can also copy the disposable preview child. It is not in the
                // preview cache, so discard it before the cache creates the copy's own view.
                for (int i = placement.transform.childCount - 1; i >= 0; i--)
                {
                    var child = placement.transform.GetChild(i).gameObject;
                    if (child.name == "Prop preview" && (child.hideFlags & HideFlags.DontSaveInEditor) != 0)
                        Object.DestroyImmediate(child);
                }
                Transform parent = MatchingParent(owner, placement) ?? target.parent;
                var copy = Object.Instantiate(target.gameObject, parent);
                copy.name = placement.name;
                if (recordUndo)
                {
                    Undo.RegisterCreatedObjectUndo(copy, UndoName);
                    Undo.RecordObject(owner, UndoName);
                }
                // Bind() recalculates the ratio using the edited handle scale and would
                // shrink a resized duplicate. Change only the reference inherited by Ctrl+D.
                var serialized = new SerializedObject(placement);
                serialized.FindProperty("runtimeTarget").objectReferenceValue = copy.transform;
                if (recordUndo) serialized.ApplyModifiedProperties();
                else serialized.ApplyModifiedPropertiesWithoutUndo();
                owner.RegisterTarget(copy.transform);
                EditorUtility.SetDirty(owner);
                PrefabUtility.RecordPrefabInstancePropertyModifications(owner);
                PrefabUtility.RecordPrefabInstancePropertyModifications(placement);
                target = copy.transform;
                changes++;
            }

            // Area names and stage membership drive spawner lookup. A copied A1 renamed
            // B1 and moved under Stage B must also be B1 under Stage B in the runtime world.
            var authorStage = placement.GetComponentInParent<PlanetStage>();
            Transform runtimeParent = MatchingParent(owner, placement);
            if (authorStage == null || runtimeParent == null) continue;
            if (target.parent != runtimeParent)
            {
                if (recordUndo) Undo.SetTransformParent(target, runtimeParent, UndoName);
                else target.SetParent(runtimeParent, true);
                EditorUtility.SetDirty(target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                changes++;
            }
            if (target.name != placement.name)
            {
                if (recordUndo) Undo.RecordObject(target.gameObject, UndoName);
                target.name = placement.name;
                EditorUtility.SetDirty(target.gameObject);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target.gameObject);
                changes++;
            }
        }
        if (changes > 0 && recordUndo)
        {
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
        }
        return changes;
    }

    static int MatchScore(PlanetAuthoring owner, PlanetPlacement placement)
    {
        int score = placement.name == placement.RuntimeTarget.name ? 2 : 0;
        if (MatchingParent(owner, placement) == placement.RuntimeTarget.parent) score++;
        return score;
    }

    static Transform MatchingParent(PlanetAuthoring owner, PlanetPlacement placement)
    {
        if (placement.transform.parent == owner.AuthoringRoot) return owner.RuntimePlanet.transform;
        string path = AnimationUtility.CalculateTransformPath(placement.transform.parent, owner.AuthoringRoot);
        return owner.RuntimePlanet.transform.Find(path);
    }
}
