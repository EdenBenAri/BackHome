using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CreatureSpawner))]
public sealed class CreatureSpawnerEditor : Editor
{
    readonly Dictionary<Object, bool> expandedStages = new();

    void OnEnable() => EditorApplication.hierarchyChanged += Repaint;
    void OnDisable() => EditorApplication.hierarchyChanged -= Repaint;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var property = serializedObject.GetIterator();
        bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (property.name == "spawnPoints")
                DrawSpawnPoints(property);
            else
            {
                using (new EditorGUI.DisabledScope(property.name == "m_Script"))
                    EditorGUILayout.PropertyField(property, true);
            }
        }
        serializedObject.ApplyModifiedProperties();
    }

    Transform FindAreasRoot()
    {
        var planet = serializedObject.FindProperty("planet").objectReferenceValue as SphericalPlanet;
        if (planet != null) return planet.transform.Find("Areas");

        // The authoring planet is inactive in Edit mode, so include inactive objects.
        // Restrict discovery to this spawner's scene (also works in Prefab Mode).
        var scene = ((CreatureSpawner)target).gameObject.scene;
        if (!scene.IsValid()) return null;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var candidate in root.GetComponentsInChildren<SphericalPlanet>(true))
            {
                if (planet != null) return null; // An explicit planet is needed when ambiguous.
                planet = candidate;
            }
        }
        return planet != null ? planet.transform.Find("Areas") : null;
    }

    void DrawSpawnPoints(SerializedProperty points)
    {
        Transform areas = FindAreasRoot();
        if (areas == null)
        {
            EditorGUILayout.PropertyField(points, true);
            EditorGUILayout.HelpBox("Assign a Planet to group spawn points by its Areas stages.", MessageType.Info);
            return;
        }

        var stages = new List<PlanetStage>();
        for (int i = 0; i < areas.childCount; i++)
            if (areas.GetChild(i).TryGetComponent<PlanetStage>(out var stage)) stages.Add(stage);
        if (stages.Count == 0)
        {
            EditorGUILayout.PropertyField(points, true);
            return;
        }

        // Group the view only. Serialized indices and king navigation order stay intact.
        var groups = new Dictionary<PlanetStage, List<int>>();
        foreach (var stage in stages) groups.Add(stage, new List<int>());
        var unassigned = new List<int>();
        for (int i = 0; i < points.arraySize; i++)
        {
            string areaId = points.GetArrayElementAtIndex(i).FindPropertyRelative("areaId").stringValue;
            Transform area = PlanetStage.FindArea(areas, areaId);
            var stage = area != null ? area.GetComponentInParent<PlanetStage>(true) : null;
            if (stage != null && groups.TryGetValue(stage, out var indices)) indices.Add(i);
            else unassigned.Add(i);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Spawn Points", points.arraySize.ToString(), EditorStyles.boldLabel);
        foreach (var stage in stages)
            DrawGroup(points, areas, stage, groups[stage]);
        if (unassigned.Count > 0)
            DrawGroup(points, areas, null, unassigned);
    }

    void DrawGroup(SerializedProperty points, Transform areas, PlanetStage stage, List<int> indices)
    {
        Object key = stage != null ? stage : target;
        string title = stage != null ? stage.name : "Unassigned";
        if (!expandedStages.TryGetValue(key, out bool expanded)) expanded = indices.Count > 0;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            expanded = EditorGUILayout.Foldout(expanded, $"{title} ({indices.Count})", true, EditorStyles.foldoutHeader);
            expandedStages[key] = expanded;
            if (!expanded) return;

            using (new EditorGUI.IndentLevelScope())
            {
                if (stage == null)
                    EditorGUILayout.HelpBox("These area IDs are missing or outside the planet's stage groups. Edit Area Id to assign them.", MessageType.Warning);
                else if (indices.Count == 0)
                    EditorGUILayout.LabelField("No spawn points yet.", EditorStyles.miniLabel);

                for (int row = 0; row < indices.Count; row++)
                    DrawPoint(points, indices, row);
            }

            if (stage != null && GUILayout.Button("Add Spawn Point", EditorStyles.miniButton))
                ShowAddMenu(areas, stage);
        }
    }

    void DrawPoint(SerializedProperty points, List<int> indices, int row)
    {
        var point = points.GetArrayElementAtIndex(indices[row]);
        string areaId = point.FindPropertyRelative("areaId").stringValue;
        var label = new GUIContent(string.IsNullOrWhiteSpace(areaId) ? "New Spawn Point" : areaId);
        Rect rect = EditorGUILayout.GetControlRect(false, EditorGUI.GetPropertyHeight(point, label, true));
        Rect menuRect = new(rect.xMax - 24f, rect.y, 24f, EditorGUIUtility.singleLineHeight);
        rect.width -= 28f;
        EditorGUI.PropertyField(rect, point, label, true);
        if (!GUI.Button(menuRect, new GUIContent("⋮", "Move or remove this spawn point"), EditorStyles.miniButton)) return;

        int index = indices[row];
        var menu = new GenericMenu();
        if (row > 0)
        {
            int previous = indices[row - 1];
            menu.AddItem(new GUIContent("Move Up"), false, () => MovePoint(index, previous));
        }
        else menu.AddDisabledItem(new GUIContent("Move Up"));
        if (row + 1 < indices.Count)
        {
            int next = indices[row + 1];
            menu.AddItem(new GUIContent("Move Down"), false, () => MovePoint(index, next));
        }
        else menu.AddDisabledItem(new GUIContent("Move Down"));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Remove"), false, () =>
        {
            serializedObject.Update();
            serializedObject.FindProperty("spawnPoints").DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
        });
        menu.ShowAsContext();
    }

    void MovePoint(int from, int to)
    {
        serializedObject.Update();
        serializedObject.FindProperty("spawnPoints").MoveArrayElement(from, to);
        serializedObject.ApplyModifiedProperties();
    }

    void ShowAddMenu(Transform areas, PlanetStage stage)
    {
        var points = serializedObject.FindProperty("spawnPoints");
        var usedAreas = new HashSet<Transform>();
        for (int i = 0; i < points.arraySize; i++)
            usedAreas.Add(PlanetStage.FindArea(areas, points.GetArrayElementAtIndex(i).FindPropertyRelative("areaId").stringValue));

        var menu = new GenericMenu();
        for (int i = 0; i < stage.transform.childCount; i++)
        {
            Transform area = stage.transform.GetChild(i);
            var label = new GUIContent(area.name);
            if (usedAreas.Contains(area)) menu.AddDisabledItem(label);
            else
            {
                string path = stage.name + "/" + area.name;
                menu.AddItem(label, false, () => AddPoint(path));
            }
        }
        if (stage.transform.childCount == 0)
            menu.AddDisabledItem(new GUIContent("Add an area under this stage on the planet first"));
        menu.ShowAsContext();
    }

    void AddPoint(string areaId)
    {
        serializedObject.Update();
        var points = serializedObject.FindProperty("spawnPoints");
        int index = points.arraySize++;
        var point = points.GetArrayElementAtIndex(index);
        // Unity duplicates the last array element on insertion; start with a clean den.
        point.FindPropertyRelative("areaId").stringValue = areaId;
        point.FindPropertyRelative("radius").floatValue = 12f;
        point.FindPropertyRelative("creatures").ClearArray();
        var king = point.FindPropertyRelative("king");
        king.FindPropertyRelative("prefab").objectReferenceValue = null;
        king.FindPropertyRelative("respawnSeconds").floatValue = 180f;
        point.isExpanded = true;
        serializedObject.ApplyModifiedProperties();
    }
}
