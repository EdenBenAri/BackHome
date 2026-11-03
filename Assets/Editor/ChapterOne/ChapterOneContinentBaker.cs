using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click ChapterOne continent bake for the open scene.
/// </summary>
public static class ChapterOneContinentBaker
{
    [MenuItem("BackHome/Generate ChapterOne Continents")]
    public static void GenerateInOpenScene()
    {
        PlanetTileMap map = FindAuthoringMap();
        if (map == null)
        {
            EditorUtility.DisplayDialog(
                "ChapterOne Continents",
                "Open a scene with a PlanetTileMap first.",
                "OK");
            return;
        }

        if (map.Tileset == null)
        {
            EditorUtility.DisplayDialog(
                "ChapterOne Continents",
                "Run BackHome → Import ChapterOne Tileset first.",
                "OK");
            return;
        }

        Undo.RecordObject(map, "Generate ChapterOne Continents");
        if (!map.HasValidMap())
            map.SetTilesAroundEquator(map.TilesAroundEquator, refillWithBase: true);

        PlanetBlobAutotile.GenerateContinents(map, seed: 11);
        EditorUtility.SetDirty(map);
        PrefabUtility.RecordPrefabInstancePropertyModifications(map);
        if (map.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        map.RebuildVisuals();
        PlanetTerrainPresets.MarkDirty(map);
        Debug.Log("[BackHome] Generated ChapterOne continents.");
    }

    [MenuItem("BackHome/Resolve ChapterOne Autotile")]
    public static void ResolveInOpenScene()
    {
        PlanetTileMap map = FindAuthoringMap();
        if (map == null || map.Tileset == null)
        {
            EditorUtility.DisplayDialog("ChapterOne Autotile", "Need PlanetTileMap with a tileset.", "OK");
            return;
        }

        Undo.RecordObject(map, "Resolve ChapterOne Autotile");
        PlanetBlobAutotile.ResolveAll(map);
        PlanetTerrainPresets.MarkDirty(map);
        EditorUtility.SetDirty(map);
        PrefabUtility.RecordPrefabInstancePropertyModifications(map);
        if (map.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
    }

    static PlanetTileMap FindAuthoringMap()
    {
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        var owner = stage != null ? stage.prefabContentsRoot.GetComponent<PlanetAuthoring>()
            : Object.FindAnyObjectByType<PlanetAuthoring>();
        return owner != null ? owner.Tiles : null;
    }
}
