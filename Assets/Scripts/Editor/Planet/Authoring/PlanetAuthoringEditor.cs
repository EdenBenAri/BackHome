using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlanetAuthoring))]
public sealed class PlanetAuthoringEditor : Editor
{
    bool paint;
    int paintTool;
    bool sculpt => paintTool != 0;
    bool eyedropper;
    bool flood;
    bool showGrid;
    int terrain;
    int brush;
    float height = 1f;
    bool stroke;
    bool changed;
    int lastRow = -1, lastColumn = -1;
    GameObject propPrefab;
    bool placeProp;

    public override void OnInspectorGUI()
    {
        var owner = (PlanetAuthoring)target;
        EditorGUILayout.HelpBox("3D Plane edit / spherical Play\n" +
            "X = east, Z = north, Y = elevation. Left and right edges join. " +
            "Click a model to select it, then use the normal 3D transform tools.\n" +
            "The private Runtime Planet holds gameplay components. Keep it inactive while editing.", MessageType.Info);
        if (!owner.ValidateConfiguration(out string reason))
        {
            EditorGUILayout.HelpBox(reason, MessageType.Error);
            return;
        }
        EditorGUILayout.LabelField("Grid", $"{owner.Tiles.LongitudeBands} × {owner.Tiles.LatitudeBands}");
        EditorGUILayout.LabelField("Size", $"{2f * Mathf.PI * owner.Radius:F1} × {Mathf.PI * owner.Radius:F1} m");
        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField("Projection", $"{owner.ProjectedObjectCount} objects · {owner.ProjectionMilliseconds:F2} ms");
            EditorGUILayout.LabelField("Total world activation", $"{owner.RuntimeStartupMilliseconds:F2} ms");
            return;
        }
        if (EditorSettings.enterPlayModeOptionsEnabled
            && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0)
            EditorGUILayout.HelpBox("Planet authoring needs Reload Scene enabled in Enter Play Mode Settings.", MessageType.Error);
        if (GUILayout.Button("Frame 3D Plane")) Frame(owner);
        if (GUILayout.Button("Top View")) Frame(owner, true);
        if (GUILayout.Button("Show Whole Scene")) SceneVisibilityManager.instance.ExitIsolation();
        if (GUILayout.Button("Refresh Editor Preview")) PlanetAuthoringPreview.Queue(owner);
        if (GUILayout.Button("Environment Palette / Scatter"))
        {
            var palette = owner.AuthoringRoot.GetComponentInChildren<PlanetEnvironmentBrush>(true);
            if (palette != null) Selection.activeGameObject = palette.gameObject;
        }
        if (GUILayout.Button("Terrain Settings")) Selection.activeObject = owner.Tiles;
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Load Terrain Preset")) PlanetTerrainPresets.ShowPresetMenu(owner.Tiles, owner.Tiles.LastLoadedPreset);
        if (GUILayout.Button("Save Terrain Preset")) PlanetTerrainPresets.SavePreset(owner.Tiles, owner.Tiles.LastLoadedPreset, true);
        EditorGUILayout.EndHorizontal();
        paint = EditorGUILayout.Toggle("Paint in Scene View", paint);
        if (paint)
        {
            placeProp = false;
            paintTool = EditorGUILayout.Popup("Tool", paintTool, new[] { "Terrain", "Raise Height", "Lower Height", "Set Height" });
            brush = EditorGUILayout.IntSlider("Brush radius (cells)", brush, 0, 10);
            showGrid = EditorGUILayout.Toggle("Show Cell Grid", showGrid);
            if (paintTool == 3) height = EditorGUILayout.Slider("Height (steps)", height, PlanetTileMap.MinHeight, PlanetTileMap.MaxHeight);
            else if (!sculpt)
            {
                var labels = new string[owner.Tiles.Tileset.TerrainCount];
                for (int i = 0; i < labels.Length; i++) labels[i] = owner.Tiles.Tileset.GetTerrain(i).displayName;
                terrain = EditorGUILayout.Popup("Terrain", terrain, labels);
            }
            eyedropper = EditorGUILayout.Toggle("Pick Terrain / Height", eyedropper);
            flood = EditorGUILayout.Toggle("Flood On Next Click", flood);
            if (GUILayout.Button(sculpt ? "Fill Set Height" : "Fill Selected Terrain"))
            {
                Undo.RegisterCompleteObjectUndo(owner.Tiles, "Fill planet terrain");
                if (sculpt) owner.Tiles.FillHeight(height, false);
                else owner.Tiles.FillTerrain(terrain);
                PlanetTerrainPresets.MarkDirty(owner.Tiles);
            }
            EditorGUILayout.HelpBox("Paint directly on the 3D terrain from any angle. Alt navigates. Undo restores a whole stroke. Preview refreshes on release.", MessageType.None);
        }
        else FinishStroke();
        EditorGUILayout.Space();
        propPrefab = (GameObject)EditorGUILayout.ObjectField("New prop prefab", propPrefab, typeof(GameObject), false);
        using (new EditorGUI.DisabledScope(propPrefab == null))
        {
            if (GUILayout.Button(placeProp ? "Cancel Placement (Esc)" : "Place New Prop On Plane"))
            {
                placeProp = !placeProp;
                paint = false;
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("Add Prop At Map Centre")) PlanetAuthoringTools.AddProp(owner, propPrefab);
        }
        if (placeProp) EditorGUILayout.HelpBox("Click the ground in Scene View to place the prefab. Alt navigates; Esc cancels.", MessageType.None);
        EditorGUILayout.HelpBox("Disable or delete a flat handle to remove it from Play. Use Duplicate Selected Prop in a handle's inspector to make an independent copy. Terrain heights preserve the existing spherical sampling; flat preview poles are approximate.", MessageType.None);
    }

    public static void Frame(PlanetAuthoring owner, bool topView = false)
    {
        var view = SceneView.lastActiveSceneView;
        if (view == null) return;
        // World-space HUD and the space backdrop otherwise intercept clicks on the plane.
        // Scene isolation is editor visibility only; the Game camera and Play are unaffected.
        SceneVisibilityManager.instance.Isolate(owner.gameObject, true);
        view.in2DMode = false;
        view.sceneLighting = false;
        view.LookAt(owner.transform.position, owner.transform.rotation * Quaternion.Euler(topView ? 90 : 50, 0, 0),
            Mathf.PI * owner.Radius * (topView ? 1.3f : 1.05f), topView);
    }

    void OnDisable() { FinishStroke(); }

    void FinishStroke()
    {
        if (!stroke) return;
        stroke = false;
        if (target == null || !changed) return;
        var owner = (PlanetAuthoring)target;
        var map = owner.Tiles;
        if (!map.Tileset.UsesSplatBlending)
        {
            int fallback = map.Tileset.DefaultVisualIndex();
            for (int row = 0; row < map.LatitudeBands; row++)
            for (int column = 0; column < map.LongitudeBands; column++)
                map.SetVisualSilent(row, column, PlanetBlobAutotile.ResolveVisualIndex(map, row, column, fallback));
        }
        EditorUtility.SetDirty(map);
        PrefabUtility.RecordPrefabInstancePropertyModifications(map);
        PlanetAuthoringPreview.QueueTerrain(owner);
        changed = false;
    }

    void OnSceneGUI()
    {
        if (Application.isPlaying || (!paint && !placeProp)) return;
        var owner = (PlanetAuthoring)target;
        if (owner.Tiles == null) return;
        if (showGrid && paint && Event.current.type == EventType.Repaint) PlanetAuthoringPreview.DrawGrid(owner);
        var e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            FinishStroke(); GUIUtility.hotControl = 0; placeProp = paint = false; Repaint(); e.Use(); return;
        }
        int control = GUIUtility.GetControlID("PlanetTerrainPaint".GetHashCode(), FocusType.Passive);
        if (e.type == EventType.Layout && !e.alt) HandleUtility.AddDefaultControl(control);
        if (e.type == EventType.MouseUp && stroke)
        {
            FinishStroke(); GUIUtility.hotControl = 0; e.Use(); return;
        }
        Ray worldRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        // The same mesh is rendered and hit-tested. A perspective ray must not use Y=0,
        // which selects a different cell behind raised terrain. Keep this mesh stable for a stroke.
        if (!PlanetAuthoringPreview.Raycast(owner, worldRay, out RaycastHit hit)) return;
        Vector3 point = owner.transform.InverseTransformPoint(hit.point);
        if (placeProp)
        {
            Handles.color = Color.cyan;
            Handles.DrawWireDisc(hit.point, owner.transform.up, 1.5f);
            if (!e.alt && e.button == 0 && e.type == EventType.MouseDown && propPrefab != null)
            {
                placeProp = false;
                PlanetAuthoringTools.AddProp(owner, propPrefab, point);
                e.Use();
            }
            if (e.type == EventType.MouseMove) SceneView.RepaintAll();
            return;
        }
        var map = owner.Tiles;
        if (!PlanetProjection.TryCell(point, owner.Radius, map.LatitudeBands, map.LongitudeBands, out int row, out int column)) return;
        float cell = 2f * Mathf.PI * owner.Radius / map.LongitudeBands;
        Handles.color = Color.yellow;
        Vector3 center = new Vector3((column + .5f) * cell - Mathf.PI * owner.Radius, 0f,
            (row + .5f) * cell - Mathf.PI * owner.Radius * .5f);
        center.y = map.GetWalkSurfaceRadius(PlanetProjection.ToSphere(center, owner.Radius)) - owner.Radius + .2f;
        Handles.DrawWireDisc(owner.transform.TransformPoint(center), owner.transform.up, cell * (brush + .5f));
        Handles.Label(owner.transform.TransformPoint(center), $"{column}, {row} · h {map.GetHeight(row,column):0.##}");
        if (e.type == EventType.MouseMove) SceneView.RepaintAll();
        if (e.alt || e.button != 0) return;
        if (eyedropper && e.type == EventType.MouseDown)
        {
            if (sculpt) { height = map.GetHeight(row, column); paintTool = 3; }
            else terrain = map.GetTerrain(row, column);
            eyedropper = false; Repaint(); e.Use(); return;
        }
        if (e.type == EventType.MouseDown)
        {
            Undo.RegisterCompleteObjectUndo(map, sculpt ? "Sculpt flat Nyxara" : "Paint flat Nyxara");
            stroke = true; changed = false; lastRow = lastColumn = -1;
            GUIUtility.hotControl = control;
        }
        if (stroke && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
        {
            if (row != lastRow || column != lastColumn)
            {
                if (flood)
                {
                    if (sculpt) PlanetBlobAutotile.FloodFillHeight(map, row, column, height);
                    else PlanetBlobAutotile.FloodFill(map, row, column, terrain);
                    flood = false; changed = true; FinishStroke(); GUIUtility.hotControl = 0;
                    Repaint(); e.Use(); return;
                }
                if (paintTool == 1 || paintTool == 2)
                    changed |= map.PaintHeightDeltaBrush(row, column, (paintTool == 1 ? 1 : -1) * (e.shift ? -1 : 1), brush, false);
                else changed |= sculpt ? map.PaintHeightBrush(row, column, e.shift ? PlanetTileMap.DefaultGroundHeight : height, brush, false)
                    : map.PaintTerrainBrush(row, column, e.shift ? map.Tileset.BaseTerrainIndex : terrain, brush, false);
                lastRow = row; lastColumn = column;
            }
            e.Use();
        }
    }
}

[CustomEditor(typeof(PlanetPlacement))]
public sealed class PlanetPlacementEditor : Editor
{
    bool place;

    public override void OnInspectorGUI()
    {
        var handle = (PlanetPlacement)target;
        var owner = handle.GetComponentInParent<PlanetAuthoring>();
        EditorGUILayout.HelpBox("Use W / E / R to move, rotate or scale this model in 3D. Place On Plane moves it to the clicked ground position. Its pose is projected to the sphere once before Play.", MessageType.Info);
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField("Runtime object", handle.RuntimeTarget, typeof(Transform), true);
        if (owner == null || Application.isPlaying) return;
        if (GUILayout.Button(place ? "Cancel Placement (Esc)" : "Place On Plane")) place = !place;
        if (GUILayout.Button("Snap To Ground"))
        {
            Undo.RecordObject(handle.transform, "Snap flat prop");
            Vector3 flat = owner.transform.InverseTransformPoint(handle.transform.position);
            flat.y = owner.Tiles.GetWalkSurfaceRadius(PlanetProjection.ToSphere(flat, owner.Radius)) - owner.Radius + .05f;
            handle.transform.position = owner.transform.TransformPoint(flat);
            PrefabUtility.RecordPrefabInstancePropertyModifications(handle.transform);
        }
        if (GUILayout.Button("Duplicate Selected Prop")) PlanetAuthoringTools.Duplicate(owner, handle);
        if (GUILayout.Button("Remove Prop")) PlanetAuthoringTools.RemoveProp(owner, handle);
        if (handle != null && GUILayout.Button("Edit Gameplay Settings")) Selection.activeGameObject = handle.RuntimeTarget.gameObject;
        if (GUILayout.Button("Select 3D Authoring Plane")) Selection.activeGameObject = owner.gameObject;
    }

    void OnSceneGUI()
    {
        if (!place || Application.isPlaying) return;
        var handle = (PlanetPlacement)target;
        var owner = handle.GetComponentInParent<PlanetAuthoring>();
        if (owner == null) return;
        var e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            place = false; Repaint(); e.Use(); return;
        }
        if (e.type == EventType.Layout && !e.alt)
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        if (!PlanetAuthoringPreview.Raycast(owner, HandleUtility.GUIPointToWorldRay(e.mousePosition), out var hit)) return;
        Handles.color = Color.cyan;
        Handles.DrawWireDisc(hit.point, owner.transform.up, 1.5f);
        Handles.DrawDottedLine(handle.transform.position, hit.point, 4f);
        if (e.type == EventType.MouseMove) SceneView.RepaintAll();
        if (e.alt || e.button != 0 || e.type != EventType.MouseDown) return;
        Undo.RecordObject(handle.transform, "Place prop on 3D plane");
        Vector3 flat = owner.transform.InverseTransformPoint(hit.point);
        flat.y = owner.Tiles.GetWalkSurfaceRadius(PlanetProjection.ToSphere(flat, owner.Radius)) - owner.Radius + .05f;
        handle.transform.position = owner.transform.TransformPoint(flat);
        PrefabUtility.RecordPrefabInstancePropertyModifications(handle.transform);
        place = false; Repaint(); e.Use();
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
    static void DrawHandle(PlanetPlacement handle, GizmoType type)
    {
        if (Application.isPlaying) return;
        Gizmos.color = (type & GizmoType.Selected) != 0 ? Color.yellow : new Color(.2f, .9f, 1f, .5f);
        Gizmos.DrawWireSphere(handle.transform.position, .7f);
    }
}
