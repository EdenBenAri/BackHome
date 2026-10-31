using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Environment palette/scatter/erase tools for the 3D authoring plane.</summary>
[CustomEditor(typeof(PlanetEnvironmentBrush))]
public sealed class PlanetEnvironmentBrushEditor : Editor
{
    int categoryIndex;
    int prefabIndex;
    bool paint;
    bool stroke;
    int undoGroup;
    Vector3 lastPoint;
    readonly List<Vector3> occupied = new List<Vector3>();

    public override void OnInspectorGUI()
    {
        var brush = (PlanetEnvironmentBrush)target;
        var owner = brush.GetComponentInParent<PlanetAuthoring>();
        if (owner == null) { DrawDefaultInspector(); return; }
        EditorGUILayout.HelpBox("Paint on the 3D plane. Shift erases the selected category. Alt navigates. Each stroke is one Undo operation.", MessageType.Info);
        DrawDefaultInspector();
        if (Application.isPlaying || brush.CategoryCount == 0) return;
        var labels = new string[brush.CategoryCount];
        for (int i = 0; i < labels.Length; i++) labels[i] = brush.GetCategory(i).displayName;
        categoryIndex = Mathf.Clamp(EditorGUILayout.Popup("Category", categoryIndex, labels), 0, labels.Length - 1);
        var category = brush.GetCategory(categoryIndex);
        var prefabs = category.prefabs ?? System.Array.Empty<GameObject>();
        var choices = new string[prefabs.Length + 1];
        choices[0] = "Random from palette";
        for (int i = 0; i < prefabs.Length; i++) choices[i + 1] = prefabs[i] != null ? prefabs[i].name : "(empty)";
        prefabIndex = Mathf.Clamp(EditorGUILayout.Popup("Prefab", prefabIndex, choices), 0, prefabs.Length);
        paint = EditorGUILayout.Toggle("Paint Environment", paint);
        if (!paint) EndStroke();
        if (GUILayout.Button("Select 3D Authoring Plane")) Selection.activeGameObject = owner.gameObject;
    }

    void OnDisable() { EndStroke(); }
    void EndStroke()
    {
        if (!stroke) return;
        stroke = false;
        Undo.CollapseUndoOperations(undoGroup);
        GUIUtility.hotControl = 0;
    }

    void OnSceneGUI()
    {
        if (!paint || Application.isPlaying) return;
        var brush = (PlanetEnvironmentBrush)target;
        var owner = brush.GetComponentInParent<PlanetAuthoring>();
        var category = brush.GetCategory(categoryIndex);
        if (owner == null || category == null) return;
        var e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        { EndStroke(); paint = false; Repaint(); e.Use(); return; }
        int control = GUIUtility.GetControlID("PlaneEnvironmentBrush".GetHashCode(), FocusType.Passive);
        if (e.type == EventType.Layout && !e.alt) HandleUtility.AddDefaultControl(control);
        if (e.type == EventType.MouseUp && stroke) { EndStroke(); e.Use(); return; }
        if (!PlanetAuthoringPreview.Raycast(owner, HandleUtility.GUIPointToWorldRay(e.mousePosition), out var hit)) return;
        Handles.color = e.shift ? Color.red : category.previewColor;
        Handles.DrawWireDisc(hit.point, owner.transform.up, Mathf.Max(.5f, category.brushRadius));
        if (e.type == EventType.MouseMove) SceneView.RepaintAll();
        if (e.alt || e.button != 0) return;
        Vector3 point = owner.transform.InverseTransformPoint(hit.point);
        if (e.type == EventType.MouseDown)
        {
            Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(e.shift ? "Erase planet environment" : "Paint planet environment");
            stroke = true; GUIUtility.hotControl = control;
            lastPoint = Vector3.positiveInfinity;
            occupied.Clear();
            var folder = brush.transform.Find(FolderName(category));
            if (folder != null)
                foreach (var placement in folder.GetComponentsInChildren<PlanetPlacement>(true))
                    if (placement.gameObject.activeSelf) occupied.Add(owner.transform.InverseTransformPoint(placement.transform.position));
        }
        if (!stroke || (e.type != EventType.MouseDown && e.type != EventType.MouseDrag)) return;
        if ((lastPoint - point).sqrMagnitude >= Mathf.Pow(Mathf.Max(.2f, category.spacing) * .45f, 2))
        {
            if (e.shift) Erase(owner, brush, category, point);
            else Scatter(owner, brush, category, point);
            lastPoint = point;
        }
        e.Use();
    }

    static string FolderName(PlanetEnvironmentBrush.Category category)
        => string.IsNullOrWhiteSpace(category.displayName) ? "Props" : category.displayName.Trim();

    void Scatter(PlanetAuthoring owner, PlanetEnvironmentBrush brush, PlanetEnvironmentBrush.Category category, Vector3 point)
    {
        Transform folder = brush.transform.Find(FolderName(category));
        if (folder == null)
        {
            folder = new GameObject(FolderName(category)).transform;
            folder.SetParent(brush.transform, false);
            Undo.RegisterCreatedObjectUndo(folder.gameObject, "Create authoring category");
        }
        int amount = category.brushRadius < .05f ? 1 : Mathf.Clamp(category.amount, 1, 12);
        for (int i = 0; i < amount; i++)
        {
            var prefab = prefabIndex == 0 ? brush.PickRandomPrefab(categoryIndex) : brush.GetPrefab(categoryIndex, prefabIndex - 1);
            if (prefab == null) continue;
            Vector2 offset = Random.insideUnitCircle * category.brushRadius;
            Vector3 sample = point + new Vector3(offset.x, 0, offset.y);
            if (!PlanetProjection.TryCell(sample, owner.Radius, owner.Tiles.LatitudeBands, owner.Tiles.LongitudeBands, out _, out _)) continue;
            bool crowded = false;
            foreach (var position in occupied)
            {
                Vector3 delta = position - sample; delta.y = 0;
                if (delta.sqrMagnitude < category.spacing * category.spacing) { crowded = true; break; }
            }
            if (crowded) continue;
            PlanetAuthoringTools.AddProp(owner, prefab, sample, folder,
                brush.RandomYaw ? Random.Range(0f, 360f) : brush.Yaw,
                Random.Range(category.scaleMin, Mathf.Max(category.scaleMin, category.scaleMax)), brush.Hover, false);
            occupied.Add(sample);
        }
    }

    void Erase(PlanetAuthoring owner, PlanetEnvironmentBrush brush, PlanetEnvironmentBrush.Category category, Vector3 point)
    {
        Transform folder = brush.transform.Find(FolderName(category));
        if (folder == null) return;
        float radius = Mathf.Max(category.brushRadius, Mathf.Max(.75f, category.spacing * .65f));
        foreach (var placement in folder.GetComponentsInChildren<PlanetPlacement>(true))
        {
            Vector3 position = owner.transform.InverseTransformPoint(placement.transform.position);
            Vector3 delta = position - point; delta.y = 0;
            if (delta.sqrMagnitude > radius * radius || !placement.gameObject.activeSelf) continue;
            PlanetAuthoringTools.RemoveProp(owner, placement);
            occupied.RemoveAll(p => (p - position).sqrMagnitude < .01f);
        }
    }
}
