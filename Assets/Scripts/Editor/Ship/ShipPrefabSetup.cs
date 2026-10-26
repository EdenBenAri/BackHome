using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a CasualToon material and a prefab for each Ship part
/// (body, chair, controller, glass). Albedo and normal maps are wired in.
/// Metallic and roughness stay imported as linear data maps; CasualToon does not sample them.
/// Menu: BackHome → Ship → Setup Ship Prefabs
/// </summary>
public static class ShipPrefabSetup
{
    const string Root = "Assets/Resources/Ship";
    const string MaterialsFolder = Root + "/Materials";
    const string ScenePath = "Assets/Scenes/Ship.unity";
    // Meshy/Blender writes these FBX files in centimeters. Unity's file scale
    // turns the authored ~2m meshes into ~2cm. 100 restores the meter size.
    const float FileScaleCompensation = 100f;

    static readonly string[] Parts = { "ShipBody", "ShipChair", "ShipController", "ShipGlass" };

    static readonly string ResultPath = Path.Combine(Application.dataPath, "..", "Temp", "ShipPrefabSetup.result.txt");
    static readonly string TriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeSetupShip.trigger");

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.update += WatchTrigger;
        EditorApplication.delayCall += WatchTrigger;
    }

    static void WatchTrigger()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (!File.Exists(TriggerPath))
            return;

        try { File.Delete(TriggerPath); } catch { /* ignore */ }
        SetupFromMenu();
    }

    /// <summary>Unity batchmode: -executeMethod ShipPrefabSetup.BuildBatch</summary>
    public static void BuildBatch()
    {
        try
        {
            SetupFromMenu();
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(0);
    }

    [MenuItem("BackHome/Ship/Setup Ship Prefabs")]
    public static void SetupFromMenu()
    {
        var report = new StringBuilder();
        try
        {
            EnsureFolder(MaterialsFolder);
            Shader shader = Shader.Find("BackHome/CasualToon");
            if (shader == null)
                throw new System.InvalidOperationException("BackHome/CasualToon shader was not found.");

            for (int i = 0; i < Parts.Length; i++)
            {
                string part = Parts[i];
                ConfigureTextures(part);
                Material material = BuildMaterial(part, shader);
                string bounds = BuildPrefab(part, material);
                report.AppendLine(part + " ok " + bounds);
            }

            PlaceInShipScene(report);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            report.Insert(0, "ok\n");
            WriteResult(report.ToString());
            Debug.Log("[BackHome] Ship prefabs and materials ready under " + Root + "\n" + report);
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            WriteResult("FAILED: " + ex);
        }
    }

    static void ConfigureTextures(string part)
    {
        SetTextureImport(Root + "/" + part + "_texture.png", TextureImporterType.Default, sRGB: true);
        SetTextureImport(Root + "/" + part + "_normal.png", TextureImporterType.NormalMap, sRGB: false);
        SetTextureImport(Root + "/" + part + "_metallic.png", TextureImporterType.Default, sRGB: false);
        SetTextureImport(Root + "/" + part + "_roughness.png", TextureImporterType.Default, sRGB: false);
    }

    static void SetTextureImport(string path, TextureImporterType type, bool sRGB)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            throw new System.InvalidOperationException("Missing texture: " + path);

        bool dirty = false;
        if (importer.textureType != type)
        {
            importer.textureType = type;
            dirty = true;
        }

        if (importer.sRGBTexture != sRGB)
        {
            importer.sRGBTexture = sRGB;
            dirty = true;
        }

        if (importer.mipmapEnabled != true)
        {
            importer.mipmapEnabled = true;
            dirty = true;
        }

        if (importer.isReadable)
        {
            importer.isReadable = false;
            dirty = true;
        }

        if (dirty)
            importer.SaveAndReimport();
    }

    static Material BuildMaterial(string part, Shader shader)
    {
        string matPath = MaterialsFolder + "/" + part + ".mat";
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/" + part + "_texture.png");
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/" + part + "_normal.png");
        if (albedo == null)
            throw new System.InvalidOperationException("Missing albedo for " + part);

        Material material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (material == null)
        {
            material = new Material(shader) { name = part };
            AssetDatabase.CreateAsset(material, matPath);
        }
        else
        {
            material.shader = shader;
            material.name = part;
        }

        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_BumpMap", normal);
        material.SetTexture("_EmissionMap", null);
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_EmissionColor", Color.black);
        material.SetFloat("_BumpScale", 1f);
        material.SetFloat("_Brightness", 1.04f);
        material.SetFloat("_ShadeSteps", 2f);
        material.SetFloat("_ShadeSoftness", 0.85f);
        material.SetFloat("_ShadeFloor", 0.94f);
        material.SetFloat("_Saturation", 1.04f);
        material.SetFloat("_Contrast", 1f);
        material.SetColor("_ShadowTint", new Color(0.98f, 0.98f, 0.97f, 1f));
        material.SetColor("_KeyTint", Color.white);
        material.SetColor("_SpecularColor", new Color(1f, 1f, 1f, 0f));
        material.SetFloat("_SpecularSize", 0.08f);
        material.SetFloat("_SpecularSoftness", 0.04f);
        material.SetColor("_RimColor", new Color(1f, 1f, 1f, 0f));
        material.SetFloat("_RimPower", 4f);
        material.SetFloat("_RimLightMask", 0.5f);
        material.SetFloat("_AmbientStrength", 0.2f);
        material.SetColor("_AmbientTint", Color.white);
        // Glass is a thin shell; render both sides. The other parts are solid.
        material.SetFloat("_Cull", part == "ShipGlass" ? 0f : 2f);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    static string BuildPrefab(string part, Material material)
    {
        string fbxPath = Root + "/" + part + ".fbx";
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
            throw new System.InvalidOperationException("Missing FBX: " + fbxPath);

        bool reimport = false;
        if (importer.importCameras || importer.importLights)
        {
            importer.importCameras = false;
            importer.importLights = false;
            reimport = true;
        }

        if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
        {
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            reimport = true;
        }

        if (!Mathf.Approximately(importer.globalScale, FileScaleCompensation))
        {
            importer.globalScale = FileScaleCompensation;
            reimport = true;
        }

        if (reimport)
            importer.SaveAndReimport();

        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (source == null)
            throw new System.InvalidOperationException("Could not load " + fbxPath);

        string prefabPath = Root + "/" + part + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            AssetDatabase.DeleteAsset(prefabPath);

        var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
        var root = new GameObject(part);
        model.transform.SetParent(root.transform, false);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var slots = renderers[i].sharedMaterials;
            for (int s = 0; s < slots.Length; s++)
                slots[s] = material;
            renderers[i].sharedMaterials = slots;
            renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderers[i].receiveShadows = true;
        }

        var filters = root.GetComponentsInChildren<MeshFilter>(true);
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;
        for (int i = 0; i < filters.Length; i++)
        {
            Mesh mesh = filters[i].sharedMesh;
            if (mesh == null)
                continue;

            var collider = filters[i].gameObject.GetComponent<MeshCollider>();
            if (collider == null)
                collider = filters[i].gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = false;

            if (!hasBounds)
            {
                bounds = rendererBounds(filters[i], mesh);
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(rendererBounds(filters[i], mesh));
            }
        }

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        if (!hasBounds)
            return "no-mesh";

        return "bounds " + bounds.size.ToString("F2");
    }

    static Bounds rendererBounds(MeshFilter filter, Mesh mesh)
    {
        Bounds local = mesh.bounds;
        Vector3 center = filter.transform.TransformPoint(local.center);
        Vector3 scale = filter.transform.lossyScale;
        return new Bounds(center, new Vector3(
            local.size.x * Mathf.Abs(scale.x),
            local.size.y * Mathf.Abs(scale.y),
            local.size.z * Mathf.Abs(scale.z)));
    }

    static void PlaceInShipScene(StringBuilder report)
    {
        Scene active = EditorSceneManager.GetActiveScene();
        if (active.path != ScenePath)
        {
            if (active.isDirty)
            {
                report.AppendLine("scene skipped (another scene is open and unsaved)");
                return;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        Scene scene = EditorSceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (System.Array.IndexOf(Parts, roots[i].name) >= 0)
                Object.DestroyImmediate(roots[i]);
        }

        float cursor = 0f;
        Bounds all = new Bounds(Vector3.zero, Vector3.zero);
        bool hasAll = false;
        for (int i = 0; i < Parts.Length; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/" + Parts[i] + ".prefab");
            if (prefab == null)
                continue;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                continue;

            Bounds bounds = renderers[0].bounds;
            for (int r = 1; r < renderers.Length; r++)
                bounds.Encapsulate(renderers[r].bounds);

            float width = Mathf.Max(bounds.size.x, 0.2f);
            Vector3 shift = new Vector3(cursor + width * 0.5f - bounds.center.x, -bounds.min.y, -bounds.center.z);
            instance.transform.position += shift;

            Bounds placed = new Bounds(bounds.center + shift, bounds.size);
            if (!hasAll)
            {
                all = placed;
                hasAll = true;
            }
            else
            {
                all.Encapsulate(placed);
            }

            cursor += width + 0.75f;
        }

        if (hasAll)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 look = all.center;
                float distance = Mathf.Max(all.size.magnitude * 1.15f, 4f);
                cam.transform.position = look + new Vector3(0f, all.extents.y * 0.35f, -distance);
                cam.transform.LookAt(look);
            }

            report.AppendLine("scene lineup size " + all.size.ToString("F2"));
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static void EnsureFolder(string assetFolder)
    {
        if (AssetDatabase.IsValidFolder(assetFolder))
            return;

        string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
        string name = Path.GetFileName(assetFolder);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            return;
        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    static void WriteResult(string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath)!);
            File.WriteAllText(ResultPath, text);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[BackHome] Could not write ship setup result: " + ex.Message);
        }
    }
}
