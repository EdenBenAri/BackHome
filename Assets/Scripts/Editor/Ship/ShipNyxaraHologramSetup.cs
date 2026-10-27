using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a cheap hologram variant of PlanetNyxara and places it at the
/// center of the Ship camera view.
/// Menu: BackHome → Ship → Add Nyxara Hologram
/// </summary>
public static class ShipNyxaraHologramSetup
{
    const string SourcePath = "Assets/Resources/Galaxy/Nyxara/PlanetNyxara.prefab";
    const string VariantPath = "Assets/Resources/Galaxy/Nyxara/NyxaraHologram.prefab";
    const string MapPath = "Assets/Resources/Galaxy/Nyxara/NyxaraHologramMap.png";
    const string MaterialPath = "Assets/Resources/Galaxy/Shaders/NyxaraHologram.mat";
    const string ScenePath = "Assets/Scenes/Ship.unity";
    const string ShaderName = "BackHome/PlanetHologram";

    const int MapWidth = 256;
    const int MapHeight = 128;
    const float ViewDistance = 2.45f;
    const float ViewScale = 0.82f;

    static readonly string[] HeavyChildNames = { "Environment", "Areas", "Borders", "Sea", "Tiles" };

    static readonly string TriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeNyxaraHologram.trigger");
    static readonly string PreviewTriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeNyxaraHologramPreview.trigger");
    static readonly string ResultPath = Path.Combine(Application.dataPath, "..", "Temp", "NyxaraHologram.result.txt");
    static readonly string PreviewPath = Path.Combine(Application.dataPath, "..", "Temp", "NyxaraHologram.preview.png");

    static int _shaderWaits;

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.update += WatchTrigger;
    }

    static void WatchTrigger()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (File.Exists(PreviewTriggerPath))
        {
            if (Shader.Find(ShaderName) == null && _shaderWaits < 120)
            {
                _shaderWaits++;
                return;
            }

            _shaderWaits = 0;
            try { File.Delete(PreviewTriggerPath); } catch { /* ignore */ }
            Preview();
            return;
        }

        if (!File.Exists(TriggerPath))
            return;

        if (Shader.Find(ShaderName) == null && _shaderWaits < 120)
        {
            _shaderWaits++;
            return;
        }

        _shaderWaits = 0;
        try { File.Delete(TriggerPath); } catch { /* ignore */ }
        Build();
    }

    [MenuItem("BackHome/Ship/Add Nyxara Hologram")]
    public static void Build()
    {
        var report = new StringBuilder();
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Exit play mode before building the hologram.");

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
                throw new System.InvalidOperationException("Shader BackHome/PlanetHologram was not found.");

            Texture2D map = BakeMap(report);
            Material material = BuildMaterial(shader, map);
            StripVariant(material, map, report);
            PlaceInShip(report);
            AssetDatabase.SaveAssets();
            report.AppendLine("OK");
        }
        catch (System.Exception ex)
        {
            report.AppendLine("FAIL");
            report.AppendLine(ex.ToString());
            Debug.LogException(ex);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
        File.WriteAllText(ResultPath, report.ToString());
        Debug.Log("[BackHome] Nyxara hologram\n" + report);
    }

    static Texture2D BakeMap(StringBuilder report)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (source == null)
            throw new System.InvalidOperationException("Missing PlanetNyxara prefab.");

        var tiles = source.GetComponent<PlanetTileMap>();
        if (tiles == null)
            throw new System.InvalidOperationException("PlanetNyxara has no tile map.");

        var data = new SerializedObject(tiles);
        int latBands = data.FindProperty("latitudeBands").intValue;
        int lonBands = data.FindProperty("longitudeBands").intValue;
        var heights = data.FindProperty("groundHeights");
        var terrains = data.FindProperty("terrainIds");
        var tileset = data.FindProperty("tileset").objectReferenceValue as PlanetTileset;
        int cells = latBands * lonBands;
        if (latBands < 4 || lonBands < 8 || heights == null || heights.arraySize < cells || terrains == null || terrains.arraySize < cells)
            throw new System.InvalidOperationException("Planet tile map data is incomplete.");

        var landColors = new Color[4];
        landColors[0] = new Color(0.38f, 0.62f, 0.18f, 1f);
        landColors[1] = new Color(0.55f, 0.38f, 0.22f, 1f);
        landColors[2] = new Color(0.72f, 0.48f, 0.28f, 1f);
        landColors[3] = new Color(0.22f, 0.42f, 0.16f, 1f);
        if (tileset != null)
        {
            int count = Mathf.Min(4, tileset.TerrainCount);
            for (int i = 0; i < count; i++)
            {
                PlanetTileset.Terrain terrain = tileset.GetTerrain(i);
                if (terrain != null)
                    landColors[i] = terrain.previewColor;
            }
        }

        var grid = new Color[cells];
        int land = 0;
        for (int i = 0; i < cells; i++)
        {
            float height = heights.GetArrayElementAtIndex(i).floatValue;
            if (height <= 0.05f)
            {
                float depth = Mathf.InverseLerp(0.05f, -2f, height);
                grid[i] = Color.Lerp(new Color(0.22f, 0.58f, 0.86f, 1f), new Color(0.05f, 0.22f, 0.55f, 1f), depth);
                continue;
            }

            land++;
            int terrain = terrains.GetArrayElementAtIndex(i).intValue;
            Color color = landColors[Mathf.Clamp(terrain, 0, landColors.Length - 1)];
            color = Color.Lerp(color, new Color(0.93f, 0.91f, 0.78f, 1f), Mathf.Clamp01(height / 4f) * 0.28f);
            grid[i] = color;
        }

        var tex = new Texture2D(MapWidth, MapHeight, TextureFormat.RGBA32, false, false);
        var pixels = new Color32[MapWidth * MapHeight];
        for (int y = 0; y < MapHeight; y++)
        {
            float v = (y + 0.5f) / MapHeight * latBands - 0.5f;
            int lat0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, latBands - 1);
            int lat1 = Mathf.Min(lat0 + 1, latBands - 1);
            float ty = Mathf.Clamp01(v - lat0);
            for (int x = 0; x < MapWidth; x++)
            {
                float u = (x + 0.5f) / MapWidth * lonBands - 0.5f;
                int lon0 = Mathf.FloorToInt(u);
                float tx = u - lon0;
                lon0 = Mod(lon0, lonBands);
                int lon1 = Mod(lon0 + 1, lonBands);
                Color sample = Color.Lerp(
                    Color.Lerp(grid[lat0 * lonBands + lon0], grid[lat0 * lonBands + lon1], tx),
                    Color.Lerp(grid[lat1 * lonBands + lon0], grid[lat1 * lonBands + lon1], tx),
                    ty);
                pixels[y * MapWidth + x] = sample;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        File.WriteAllBytes(MapPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(MapPath, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(MapPath) as TextureImporter;
        if (importer != null)
        {
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 2;
            importer.maxTextureSize = 256;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }

        var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(MapPath);
        if (imported == null)
            throw new System.InvalidOperationException("Hologram map failed to import.");

        report.AppendLine("map " + MapWidth + "x" + MapHeight + " land " + land + "/" + cells);
        return imported;
    }

    static Material BuildMaterial(Shader shader, Texture2D map)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "NyxaraHologram" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        material.SetTexture("_BaseMap", map);
        material.SetColor("_Tint", new Color(0.82f, 1f, 1f, 1f));
        material.SetColor("_RimColor", new Color(0.55f, 0.95f, 1f, 1f));
        material.SetFloat("_CoreAlpha", 0.62f);
        material.SetFloat("_RimPower", 2.2f);
        material.SetFloat("_RimStrength", 1.05f);
        material.SetFloat("_ScanFrequency", 36f);
        material.SetFloat("_ScanSpeed", 0.28f);
        material.SetFloat("_ScanStrength", 0.16f);
        material.SetFloat("_Brightness", 1.15f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static void StripVariant(Material material, Texture2D map, StringBuilder report)
    {
        var root = PrefabUtility.LoadPrefabContents(VariantPath);
        try
        {
            if (root == null)
                throw new System.InvalidOperationException("Hologram variant failed to load.");

            int removedObjects = 0;
            for (int i = 0; i < HeavyChildNames.Length; i++)
            {
                Transform child = root.transform.Find(HeavyChildNames[i]);
                if (child == null)
                    continue;
                Object.DestroyImmediate(child.gameObject);
                removedObjects++;
            }

            var tiles = root.GetComponent<PlanetTileMap>();
            if (tiles != null)
                Object.DestroyImmediate(tiles);

            var planet = root.GetComponent<SphericalPlanet>();
            if (planet == null)
                throw new System.InvalidOperationException("Variant is missing SphericalPlanet.");

            var planetData = new SerializedObject(planet);
            planetData.FindProperty("registerInstance").boolValue = false;
            planetData.FindProperty("radius").floatValue = 0.5f;
            planetData.FindProperty("latitudeSegments").intValue = 16;
            planetData.FindProperty("longitudeSegments").intValue = 24;
            planetData.FindProperty("shellLatitudeSegments").intValue = 16;
            planetData.FindProperty("shellLongitudeSegments").intValue = 24;
            planetData.FindProperty("shellHeightAmplitude").floatValue = 0f;
            planetData.FindProperty("textureTiling").floatValue = 1f;
            planetData.FindProperty("castShellShadows").boolValue = false;
            planetData.FindProperty("groundLayerName").stringValue = "Default";
            planetData.FindProperty("shellColorMap").objectReferenceValue = map;
            planetData.ApplyModifiedPropertiesWithoutUndo();

            var renderer = root.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            var sphere = root.GetComponent<SphereCollider>();
            if (sphere != null)
            {
                sphere.enabled = true;
                sphere.isTrigger = true;
                sphere.radius = 0.5f;
            }

            root.layer = 0;
            var hologram = root.GetComponent<PlanetHologram>();
            if (hologram == null)
                hologram = root.AddComponent<PlanetHologram>();

            var hologramData = new SerializedObject(hologram);
            hologramData.FindProperty("planetSceneName").stringValue = "PlanetNyxara";
            hologramData.FindProperty("hologramMaterial").objectReferenceValue = material;
            hologramData.FindProperty("spinDegreesPerSecond").floatValue = 12f;
            hologramData.ApplyModifiedPropertiesWithoutUndo();

            root.name = "NyxaraHologram";
            PrefabUtility.SaveAsPrefabAsset(root, VariantPath);
            report.AppendLine("variant removedChildren " + removedObjects);
        }
        finally
        {
            if (root != null)
                PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void PlaceInShip(StringBuilder report)
    {
        var variant = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
        if (variant == null)
            throw new System.InvalidOperationException("Hologram variant was not saved.");

        var ship = EditorSceneManager.GetSceneByPath(ScenePath);
        bool opened = !ship.IsValid() || !ship.isLoaded;
        if (opened)
            ship = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        try
        {
            Camera camera = null;
            GameObject button = null;
            foreach (GameObject rootObject in ship.GetRootGameObjects())
            {
                if (camera == null)
                    camera = rootObject.GetComponentInChildren<Camera>(true);
                if (button == null)
                {
                    var transforms = rootObject.GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < transforms.Length; i++)
                    {
                        if (transforms[i].name == "NyxaraButton")
                        {
                            button = transforms[i].gameObject;
                            break;
                        }
                    }
                }
            }

            if (camera == null)
                throw new System.InvalidOperationException("No camera in the Ship scene.");

            Transform existing = null;
            foreach (GameObject rootObject in ship.GetRootGameObjects())
            {
                var transforms = rootObject.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    if (transforms[i].name != "NyxaraHologram")
                        continue;
                    existing = transforms[i];
                    break;
                }
                if (existing != null)
                    break;
            }

            GameObject hologram;
            if (existing != null)
            {
                hologram = existing.gameObject;
            }
            else
            {
                hologram = (GameObject)PrefabUtility.InstantiatePrefab(variant, ship);
                hologram.name = "NyxaraHologram";
            }

            hologram.transform.SetParent(camera.transform, false);
            hologram.transform.localPosition = new Vector3(0f, 0f, ViewDistance);
            hologram.transform.localRotation = Quaternion.identity;
            hologram.transform.localScale = Vector3.one * ViewScale;
            report.AppendLine("placed at camera local z " + ViewDistance + " scale " + ViewScale);

            if (button != null)
            {
                Transform canvas = button.transform.parent;
                Object.DestroyImmediate(button);
                if (canvas != null && canvas.childCount == 0 && canvas.name == "Canvas")
                    Object.DestroyImmediate(canvas.gameObject);
                report.AppendLine("removed NyxaraButton");
            }

            EditorSceneManager.MarkSceneDirty(ship);
            EditorSceneManager.SaveScene(ship);
        }
        finally
        {
            if (opened)
                EditorSceneManager.CloseScene(ship, true);
        }
    }

    [MenuItem("BackHome/Ship/Preview Nyxara Hologram")]
    public static void Preview()
    {
        var ship = EditorSceneManager.GetSceneByPath(ScenePath);
        bool opened = !ship.IsValid() || !ship.isLoaded;
        if (opened)
            ship = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        Camera camera = null;
        try
        {
            foreach (GameObject rootObject in ship.GetRootGameObjects())
            {
                camera = rootObject.GetComponentInChildren<Camera>(true);
                if (camera != null)
                    break;
            }

            if (camera == null)
                throw new System.InvalidOperationException("No camera in the Ship scene.");

            var hologram = camera.GetComponentInChildren<PlanetHologram>(true);
            if (hologram != null)
            {
                var shell = hologram.transform.Find("VisualShell");
                var shellRenderer = shell != null ? shell.GetComponent<MeshRenderer>() : null;
                var data = new SerializedObject(hologram);
                var material = data.FindProperty("hologramMaterial").objectReferenceValue as Material;
                if (shellRenderer != null && material != null)
                    shellRenderer.sharedMaterial = material;
            }

            var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            var previous = camera.targetTexture;
            var previousActive = RenderTexture.active;
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            image.Apply(false, false);
            File.WriteAllBytes(PreviewPath, image.EncodeToPNG());
            camera.targetTexture = previous;
            RenderTexture.active = previousActive;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(rt);
            Debug.Log("[BackHome] Hologram preview " + PreviewPath);
        }
        finally
        {
            if (camera != null)
                camera.targetTexture = null;
            if (opened)
                EditorSceneManager.CloseScene(ship, true);
        }
    }

    static int Mod(int value, int length)
    {
        int m = value % length;
        return m < 0 ? m + length : m;
    }
}
