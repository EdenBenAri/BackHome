using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Keep the inactive area snapshot derived from its editable flat placements.</summary>
[InitializeOnLoad]
public static class PlanetAreaSynchronization
{
    static bool queued;

    static PlanetAreaSynchronization()
    {
        Undo.postprocessModifications += OnModified;
        Undo.undoRedoPerformed += Queue;
        EditorApplication.hierarchyChanged += Queue;
        EditorSceneManager.sceneSaving += (scene, path) => SynchronizeScene(scene);
        PrefabStage.prefabSaving += SynchronizeRoot;
        Queue();
    }

    static UndoPropertyModification[] OnModified(UndoPropertyModification[] modifications)
    {
        foreach (var modification in modifications)
        {
            var changed = modification.currentValue?.target;
            var transform = changed is GameObject go ? go.transform : (changed as Component)?.transform;
            if (transform != null && transform.GetComponentInParent<PlanetAuthoring>(true) != null)
            {
                Queue();
                break;
            }
        }
        return modifications;
    }

    static void Queue()
    {
        if (queued || EditorApplication.isPlayingOrWillChangePlaymode) return;
        queued = true;
        EditorApplication.delayCall += () =>
        {
            queued = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null) SynchronizeRoot(stage.prefabContentsRoot);
            else
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    SynchronizeScene(SceneManager.GetSceneAt(i));
        };
    }

    static void SynchronizeScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) return;
        foreach (var root in scene.GetRootGameObjects()) SynchronizeRoot(root);
    }

    static void SynchronizeRoot(GameObject root)
    {
        if (root == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach (var owner in root.GetComponentsInChildren<PlanetAuthoring>(true))
            if (Synchronize(owner) > 0) EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
    }

    /// <summary>Flat handles remain authoritative. Only derived runtime area transforms are written.</summary>
    public static int Synchronize(PlanetAuthoring owner)
    {
        if (owner == null || owner.RuntimePlanet == null || owner.AuthoringRoot == null
            || owner.RuntimePlanet.gameObject.activeSelf || Application.IsPlaying(owner.gameObject)) return 0;
        Transform areas = owner.AuthoringRoot.Find("Areas");
        Transform runtimeAreas = owner.RuntimePlanet.transform.Find("Areas");
        if (areas == null || runtimeAreas == null) return 0;
        int changes = 0;
        foreach (var handle in areas.GetComponentsInChildren<PlanetPlacement>(true))
        {
            Transform target = handle.RuntimeTarget;
            if (target == null || !target.IsChildOf(runtimeAreas)) continue;
            Vector3 flat = owner.transform.InverseTransformPoint(handle.transform.position);
            Vector3 position = owner.RuntimePlanet.transform.TransformPoint(PlanetProjection.ToSphere(flat, owner.Radius));
            Quaternion rotation = owner.RuntimePlanet.transform.rotation * PlanetProjection.Frame(flat, owner.Radius)
                * Quaternion.Inverse(owner.transform.rotation) * handle.transform.rotation;
            Vector3 scale = Vector3.Scale(handle.transform.lossyScale, handle.RuntimeScaleRatio);
            if ((target.position - position).sqrMagnitude < .00000001f
                && Quaternion.Angle(target.rotation, rotation) < .05f
                && (target.localScale - scale).sqrMagnitude < .00000001f
                && target.gameObject.activeSelf == handle.gameObject.activeInHierarchy) continue;

            // Derived state gets recomputed after Undo/Redo; no separate undo step is needed.
            handle.Apply(owner);
            EditorUtility.SetDirty(target);
            EditorUtility.SetDirty(target.gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target.gameObject);
            changes++;
        }
        if (changes > 0) SceneView.RepaintAll();
        return changes;
    }
}
