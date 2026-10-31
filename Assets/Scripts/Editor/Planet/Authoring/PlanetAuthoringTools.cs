using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class PlanetAuthoringTools
{
    public const string ScenePath = "Assets/Scenes/PlanetNyxara.unity";
    public const string PrefabPath = "Assets/Resources/Galaxy/Nyxara/PlanetNyxara.prefab";

    public static void RemoveProp(PlanetAuthoring owner, PlanetPlacement placement)
    {
        // Scene instances cannot delete a child inherited from their prefab. An inactive
        // placement is an explicit removal override and is omitted during projection.
        if (PrefabUtility.IsPartOfPrefabInstance(placement.gameObject) && !PrefabUtility.IsAddedGameObjectOverride(placement.gameObject))
        {
            Undo.RecordObject(placement.gameObject, "Remove planet prop");
            placement.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(placement.gameObject);
            return;
        }
        Undo.RecordObject(owner, "Remove planet prop");
        owner.UnregisterTarget(placement.RuntimeTarget);
        if (placement.RuntimeTarget != null) Undo.DestroyObjectImmediate(placement.RuntimeTarget.gameObject);
        Undo.DestroyObjectImmediate(placement.gameObject);
        PrefabUtility.RecordPrefabInstancePropertyModifications(owner);
        PlanetAuthoringPreview.Queue(owner);
    }

    public static PlanetPlacement CreateHandle(PlanetAuthoring owner, Transform source, Transform parent)
    {
        var go = new GameObject(source.name);
        go.transform.SetParent(parent, false);
        Vector3 spherical = owner.RuntimePlanet.transform.InverseTransformPoint(source.position);
        Vector3 flat = PlanetProjection.ToFlat(spherical, owner.Radius);
        go.transform.SetPositionAndRotation(owner.transform.TransformPoint(flat),
            owner.transform.rotation * Quaternion.Inverse(PlanetProjection.Frame(flat, owner.Radius))
            * Quaternion.Inverse(owner.RuntimePlanet.transform.rotation) * source.rotation);
        go.transform.localScale = source.lossyScale;
        go.SetActive(source.gameObject.activeSelf);
        var handle = go.AddComponent<PlanetPlacement>();
        handle.Bind(source);
        owner.RegisterTarget(source);
        return handle;
    }

    public static PlanetPlacement AddProp(PlanetAuthoring owner, GameObject prefab, Vector3? flatPosition = null,
        Transform authorParent = null, float yaw = 0f, float scale = 1f, float hover = .05f, bool select = true)
    {
        if (!PrefabUtility.IsPartOfPrefabAsset(prefab)) throw new ArgumentException("Choose a prefab asset.");
        Undo.RecordObject(owner, "Add flat prop");
        Transform parent = owner.RuntimePlanet.transform.Find("Environment");
        if (parent == null) parent = owner.RuntimePlanet.transform;
        if (authorParent != null && authorParent != owner.AuthoringRoot)
        {
            Transform group = parent.Find(authorParent.name);
            if (group == null)
            {
                group = new GameObject(authorParent.name).transform;
                group.SetParent(parent, false);
                Undo.RegisterCreatedObjectUndo(group.gameObject, "Create runtime prop category");
            }
            parent = group;
        }
        var source = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(source, "Add flat prop");
        Vector3 flat = flatPosition ?? Vector3.zero;
        flat.y = owner.Tiles.GetWalkSurfaceRadius(PlanetProjection.ToSphere(flat, owner.Radius)) - owner.Radius + hover;
        source.transform.SetPositionAndRotation(owner.RuntimePlanet.transform.TransformPoint(PlanetProjection.ToSphere(flat, owner.Radius)),
            owner.RuntimePlanet.transform.rotation * PlanetProjection.Frame(flat, owner.Radius) * Quaternion.Euler(0, yaw, 0));
        source.transform.localScale *= Mathf.Max(.001f, scale);
        if (source.GetComponent<PlanetEnvironmentObject>() == null) source.AddComponent<PlanetEnvironmentObject>();
        PrefabUtility.RecordPrefabInstancePropertyModifications(source.transform);
        var handle = CreateHandle(owner, source.transform, authorParent != null ? authorParent : owner.AuthoringRoot);
        Undo.RegisterCreatedObjectUndo(handle.gameObject, "Add flat prop");
        PrefabUtility.RecordPrefabInstancePropertyModifications(owner);
        if (select) Selection.activeGameObject = handle.gameObject;
        PlanetAuthoringPreview.Queue(owner);
        return handle;
    }

    public static void Duplicate(PlanetAuthoring owner, PlanetPlacement original)
    {
        if (original.RuntimeTarget == null) return;
        Undo.RecordObject(owner, "Duplicate flat prop");
        var source = Object.Instantiate(original.RuntimeTarget.gameObject, original.RuntimeTarget.parent);
        source.name = original.RuntimeTarget.name;
        Undo.RegisterCreatedObjectUndo(source, "Duplicate flat prop");
        var copy = CreateHandle(owner, source.transform, original.transform.parent);
        copy.transform.SetPositionAndRotation(original.transform.position + owner.transform.right * 3f, original.transform.rotation);
        copy.transform.localScale = original.transform.localScale;
        copy.gameObject.SetActive(original.gameObject.activeSelf);
        Undo.RegisterCreatedObjectUndo(copy.gameObject, "Duplicate flat prop");
        PrefabUtility.RecordPrefabInstancePropertyModifications(owner);
        Selection.activeGameObject = copy.gameObject;
        PlanetAuthoringPreview.Queue(owner);
    }

    [MenuItem("BackHome/Nyxara/Open World")]
    public static void Open()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(ScenePath);
        var owner = Object.FindAnyObjectByType<PlanetAuthoring>();
        if (owner == null) return;
        Selection.activeGameObject = owner.gameObject;
        PlanetAuthoringPreview.Queue(owner);
        PlanetAuthoringEditor.Frame(owner);
    }
}
