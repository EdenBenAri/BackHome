using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlanetTileMap))]
public sealed class PlanetTileMapEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var map = (PlanetTileMap)target;
        var owner = map.GetComponentInParent<PlanetAuthoring>();
        if (owner != null)
        {
            EditorGUILayout.HelpBox("Terrain data for the 3D authoring plane. Paint and sculpt from the world inspector.", MessageType.Info);
            if (GUILayout.Button("Select 3D Authoring Plane")) Selection.activeGameObject = owner.gameObject;
        }
        serializedObject.Update();
        EditorGUI.BeginChangeCheck();
        DrawPropertiesExcluding(serializedObject, "m_Script", "tileIndices", "terrainIds", "waterMask", "integerHeights", "groundHeights", "heights", "lastLoadedPreset");
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            PlanetTerrainPresets.MarkDirty(map);
        }
        if (GUILayout.Button("Apply Grid Size (resets terrain)"))
        {
            Undo.RecordObject(map, "Resize planet terrain");
            map.SetTilesAroundEquator(map.TilesAroundEquator, refillWithBase: true);
            PlanetTerrainPresets.MarkDirty(map);
        }
        if (GUILayout.Button("Load Terrain Preset")) PlanetTerrainPresets.ShowPresetMenu(map, map.LastLoadedPreset);
        if (GUILayout.Button("Save Terrain Preset")) PlanetTerrainPresets.SavePreset(map, map.LastLoadedPreset, true);
    }
}
