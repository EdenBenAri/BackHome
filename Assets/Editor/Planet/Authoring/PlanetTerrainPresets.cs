using UnityEditor;
using UnityEngine;

/// <summary>Shared preset operations for the plane authoring inspector and terrain data inspector.</summary>
public static class PlanetTerrainPresets
{
    public static void MarkDirty(PlanetTileMap map)
    {
        EditorUtility.SetDirty(map);
        PrefabUtility.RecordPrefabInstancePropertyModifications(map);
        var owner = map.GetComponentInParent<PlanetAuthoring>();
        if (owner != null) PlanetAuthoringPreview.QueueTerrain(owner);
    }
    public static void ShowPresetMenu(PlanetTileMap map, PlanetTileMapPreset current)
    {
        var menu = new GenericMenu();
        PlanetTileMapPreset[] presets = FindPresets();
        if (presets.Length == 0)
        {
            menu.AddDisabledItem(new GUIContent("No presets yet — paint, then Save Preset"));
            menu.ShowAsContext();
            return;
        }

        for (int i = 0; i < presets.Length; i++)
        {
            PlanetTileMapPreset preset = presets[i];
            string label = PresetMenuPath(preset);
            menu.AddItem(new GUIContent(label), preset == current, () => ApplyPreset(map, preset));
        }

        menu.ShowAsContext();
    }

    static void ApplyPreset(PlanetTileMap map, PlanetTileMapPreset preset)
    {
        if (map == null || preset == null)
            return;

        if (!preset.HasValidData())
        {
            EditorUtility.DisplayDialog(
                "Tile Map Preset",
                $"\"{preset.DisplayName}\" has no map data. Paint a planet and Save Preset to fill it.",
                "OK");
            return;
        }

        if (preset.Tileset != null && map.Tileset != null && preset.Tileset != map.Tileset)
        {
            if (!EditorUtility.DisplayDialog(
                    "Different Tileset",
                    "This preset was saved with a different tileset. Terrain colors may not match.\n\nApply anyway?",
                    "Apply",
                    "Cancel"))
                return;
        }

        Undo.RecordObject(map, "Load Tile Map Preset");
        if (!map.ApplyPreset(preset))
        {
            EditorUtility.DisplayDialog("Tile Map Preset", "Could not apply this preset.", "OK");
            return;
        }

        MarkDirty(map);
        SceneView.RepaintAll();
    }

    public static void SavePreset(PlanetTileMap map, PlanetTileMapPreset bound, bool dirty)
    {
        if (map == null || !map.HasValidMap())
        {
            EditorUtility.DisplayDialog(
                "Save Preset",
                "The map is empty. Paint some terrain first.",
                "OK");
            return;
        }

        if (bound != null)
        {
            var menu = new GenericMenu();
            string overwrite = dirty
                ? $"Overwrite \"{bound.DisplayName}\""
                : $"Overwrite \"{bound.DisplayName}\" (no changes)";
            menu.AddItem(new GUIContent(overwrite), false, () => OverwritePreset(map, bound));
            menu.AddItem(new GUIContent("Save As New…"), false, () => SavePresetAs(map, bound));
            menu.ShowAsContext();
            return;
        }

        SavePresetAs(map, null);
    }

    static void OverwritePreset(PlanetTileMap map, PlanetTileMapPreset preset)
    {
        if (map == null || preset == null)
            return;

        if (!EditorUtility.DisplayDialog(
                "Overwrite Preset",
                $"Replace \"{preset.DisplayName}\" with the current painted map?",
                "Overwrite",
                "Cancel"))
            return;

        Undo.RecordObject(preset, "Update Tile Map Preset");
        Undo.RecordObject(map, "Bind Tile Map Preset");
        map.CaptureToPreset(preset);
        EditorUtility.SetDirty(preset);
        MarkDirty(map);
        AssetDatabase.SaveAssets();
    }

    static void SavePresetAs(PlanetTileMap map, PlanetTileMapPreset bound)
    {
        if (map == null)
            return;

        EnsurePresetFolder();

        string folder = PlanetTileMapPresetEditor.DefaultFolder;
        string fileName = "New Tile Preset";
        if (bound != null)
        {
            string existing = AssetDatabase.GetAssetPath(bound);
            if (!string.IsNullOrEmpty(existing))
            {
                folder = System.IO.Path.GetDirectoryName(existing).Replace('\\', '/');
                fileName = bound.DisplayName + " Copy";
            }
        }

        string path = EditorUtility.SaveFilePanelInProject(
            "Save Tile Map Preset",
            fileName,
            "asset",
            "Save the current painted terrain and ground heights as a reusable preset.",
            folder);
        if (string.IsNullOrEmpty(path))
            return;

        var existingPreset = AssetDatabase.LoadAssetAtPath<PlanetTileMapPreset>(path);
        if (existingPreset != null)
        {
            OverwritePreset(map, existingPreset);
            return;
        }

        var preset = ScriptableObject.CreateInstance<PlanetTileMapPreset>();
        AssetDatabase.CreateAsset(preset, path);
        Undo.RecordObject(map, "Save Tile Map Preset");
        map.CaptureToPreset(preset);
        preset.SetDisplayName(System.IO.Path.GetFileNameWithoutExtension(path));
        EditorUtility.SetDirty(preset);
        MarkDirty(map);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(preset);
    }

    static PlanetTileMapPreset[] FindPresets()
    {
        string[] guids = AssetDatabase.FindAssets("t:PlanetTileMapPreset");
        var list = new System.Collections.Generic.List<PlanetTileMapPreset>(guids.Length);
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var preset = AssetDatabase.LoadAssetAtPath<PlanetTileMapPreset>(path);
            if (preset != null)
                list.Add(preset);
        }

        list.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase));
        return list.ToArray();
    }

    static string PresetMenuPath(PlanetTileMapPreset preset)
    {
        string path = AssetDatabase.GetAssetPath(preset);
        if (!string.IsNullOrEmpty(path) && path.StartsWith(PlanetTileMapPresetEditor.DefaultFolder + "/", System.StringComparison.Ordinal))
            return preset.DisplayName;

        if (string.IsNullOrEmpty(path))
            return preset.DisplayName;

        string folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        if (folder.StartsWith("Assets/", System.StringComparison.Ordinal))
            folder = folder.Substring("Assets/".Length);
        return folder + "/" + preset.DisplayName;
    }

    static void EnsurePresetFolder()
    {
        PlanetTileMapPresetEditor.EnsureDefaultFolder();
    }


}
