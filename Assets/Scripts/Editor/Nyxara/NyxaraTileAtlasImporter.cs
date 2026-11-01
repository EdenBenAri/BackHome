using UnityEditor;
using UnityEngine;

/// <summary>
/// Wires Nyxara to the Handpainted Grass and Ground textures.
/// Those sheets are seamless fills, so the planet splat-blends them
/// instead of packing wang stamps.
/// </summary>
public static class NyxaraTileAtlasImporter
{
    const string TilesetPath = "Assets/Resources/Galaxy/Nyxara/Tiles/NyxaraTileset.asset";
    const float SplatTiling = 1f;
    const float BlendSharpness = 5f;

    static readonly string[] SplatPaths =
    {
        "Assets/Resources/Galaxy/Nyxara/Tiles/Textures/Grass/Grass_brightened.png",
        "Assets/Resources/Galaxy/Nyxara/Tiles/Textures/Dirt/Dirt_darked.png",
        "Assets/Resources/Galaxy/Nyxara/Tiles/Textures/Dirt/Dirt_brightened.png",
        "Assets/Resources/Galaxy/Nyxara/Tiles/Textures/Grass/Grass_darked.png",
        "Assets/Resources/Galaxy/Nyxara/Tiles/Textures/Void/Void_brightened.png",
        "Assets/Resources/Galaxy/Nyxara/Tiles/Textures/Void/Void_darked.png",
        "Assets/Resources/Galaxy/Nyxara/Tiles/Textures/Crimson/Crimson_brightened.png",
        "Assets/Resources/Galaxy/Nyxara/Tiles/Textures/Crimson/Crimson_darked.png",
        "Assets/Resources/Galaxy/Nyxara/Tiles/Textures/GlowingMushroom/GlowingMushroom_brightened.png",
        "Assets/Resources/Galaxy/Nyxara/Tiles/Textures/GlowingMushroom/GlowingMushroom_darked.png"
    };

    struct TerrainSpec
    {
        public string id;
        public string displayName;
        public string zoneId;
        public Color previewColor;
    }

    static readonly TerrainSpec[] TerrainSpecs =
    {
        new TerrainSpec
        {
            id = "Grass",
            displayName = "Grass_brightened",
            zoneId = "grass",
            previewColor = new Color(0.38f, 0.62f, 0.18f)
        },
        new TerrainSpec
        {
            id = "Dirt",
            displayName = "Dirt_darked",
            zoneId = "dirt",
            previewColor = new Color(0.81f, 0.62f, 0.35f)
        },
        new TerrainSpec
        {
            id = "Clay",
            displayName = "Dirt_brightened",
            zoneId = "clay",
            previewColor = new Color(0.99f, 0.81f, 0.50f)
        },
        new TerrainSpec
        {
            id = "DarkGrass",
            displayName = "Grass_darked",
            zoneId = "dark_grass",
            previewColor = new Color(0.22f, 0.42f, 0.16f)
        },
        new TerrainSpec
        {
            id = "Void",
            displayName = "Void_brightened",
            zoneId = "void",
            previewColor = new Color(0.64f, 0.29f, 0.64f)
        },
        new TerrainSpec
        {
            id = "DarkVoid",
            displayName = "Void_darked",
            zoneId = "dark_void",
            previewColor = new Color(0.45f, 0.20f, 0.45f)
        },
        new TerrainSpec
        {
            id = "Crimson",
            displayName = "Crimson_brightened",
            zoneId = "crimson",
            previewColor = new Color(1f, 0.30f, 0.33f)
        },
        new TerrainSpec
        {
            id = "DarkCrimson",
            displayName = "Crimson_darked",
            zoneId = "dark_crimson",
            previewColor = new Color(0.93f, 0.11f, 0.14f)
        },
        new TerrainSpec
        {
            id = "GlowingMushroom",
            displayName = "GlowingMushroom_brightened",
            zoneId = "glowing_mushroom",
            previewColor = new Color(0f, 0.50f, 0.50f)
        },
        new TerrainSpec
        {
            id = "DarkGlowingMushroom",
            displayName = "GlowingMushroom_darked",
            zoneId = "dark_glowing_mushroom",
            previewColor = new Color(0f, 0.39f, 0.39f)
        }
    };

    [MenuItem("BackHome/Import Nyxara Tileset")]
    public static void Import()
    {
        var splats = new Texture2D[SplatPaths.Length];
        for (int i = 0; i < SplatPaths.Length; i++)
        {
            ConfigureSplatImporter(SplatPaths[i]);
            splats[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(SplatPaths[i]);
            if (splats[i] == null)
            {
                EditorUtility.DisplayDialog(
                    "Nyxara Tileset",
                    "Missing handpainted texture:\n" + SplatPaths[i],
                    "OK");
                return;
            }
        }

        var entries = new PlanetTileset.Entry[TerrainSpecs.Length];
        var terrains = new PlanetTileset.Terrain[TerrainSpecs.Length];
        for (int t = 0; t < TerrainSpecs.Length; t++)
        {
            TerrainSpec spec = TerrainSpecs[t];
            string fillId = spec.displayName;
            entries[t] = new PlanetTileset.Entry
            {
                id = fillId,
                column = 0,
                row = 0,
                cornerNW = t,
                cornerNE = t,
                cornerSW = t,
                cornerSE = t,
                walkable = true,
                zoneId = spec.zoneId
            };

            var mask = new string[PlanetTileset.MaskCount];
            for (int m = 0; m < mask.Length; m++)
                mask[m] = fillId;

            terrains[t] = new PlanetTileset.Terrain
            {
                id = spec.id,
                displayName = spec.displayName,
                previewColor = spec.previewColor,
                walkable = true,
                zoneId = spec.zoneId,
                fillAtlasId = fillId,
                maskToAtlasId = mask
            };
        }

        PlanetTileset tileset = AssetDatabase.LoadAssetAtPath<PlanetTileset>(TilesetPath);
        if (tileset == null)
        {
            tileset = ScriptableObject.CreateInstance<PlanetTileset>();
            AssetDatabase.CreateAsset(tileset, TilesetPath);
        }

        int tileSize = Mathf.Max(32, splats[0].width);
        tileset.Configure(splats[0], tileSize, entries, terrains);
        tileset.ConfigureSplat(splats, SplatTiling, BlendSharpness);
        EditorUtility.SetDirty(tileset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        AssignToOpenPlanet();

        var summary = new System.Text.StringBuilder();
        summary.AppendLine("Nyxara now splat-blends the handpainted ground pack.");
        summary.AppendLine("Brushes: Grass_brightened, Grass_darked, Dirt_brightened, Dirt_darked, Void_brightened, Void_darked, Crimson_brightened, Crimson_darked, GlowingMushroom_brightened, GlowingMushroom_darked.");
        summary.AppendLine("Paint on the 3D authoring plane; the preview refreshes on release.");
        Debug.Log("[BackHome] " + summary.ToString().Replace("\n", " "));
        EditorUtility.DisplayDialog("Nyxara Tileset", summary.ToString(), "OK");
    }

    static void AssignToOpenPlanet()
    {
        PlanetTileset tileset = AssetDatabase.LoadAssetAtPath<PlanetTileset>(TilesetPath);
        PlanetTileMap[] maps = Resources.FindObjectsOfTypeAll<PlanetTileMap>();
        for (int i = 0; i < maps.Length; i++)
        {
            if (maps[i] == null || !maps[i].gameObject.scene.IsValid()
                || maps[i].GetComponentInParent<PlanetAuthoring>() == null)
                continue;
            Undo.RecordObject(maps[i], "Assign Nyxara Tileset");
            maps[i].SetTileset(tileset, refillBase: false);
            PlanetTerrainPresets.MarkDirty(maps[i]);
        }
    }

    static void ConfigureSplatImporter(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.anisoLevel = 8;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.npotScale = TextureImporterNPOTScale.ToNearest;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.maxTextureSize = 2048;
        importer.alphaIsTransparency = false;
        importer.spriteImportMode = SpriteImportMode.None;
        importer.SaveAndReimport();
    }
}
