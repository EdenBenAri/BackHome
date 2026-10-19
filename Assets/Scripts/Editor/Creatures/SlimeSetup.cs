using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds Slime the same way as the other creatures: CasualToon material, stats, and a prefab
/// with the shared creature components. Animation clips and the animator controller are left
/// for a later pass. The model is scaled up to the same height band as the other creatures.
/// </summary>

public static class SlimeSetup
{
    const string ModelPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Models/Slime.fbx";
    const string AlbedoPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Textures/Slime_texture.png";
    const string NormalPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Textures/Slime_normal.png";
    const string MetallicPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Textures/Slime_metallic.png";
    const string RoughnessPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Textures/Slime_roughness.png";
    const string MaterialPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Materials/Slime.mat";
    const string StatsPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/SlimeStats.asset";
    const string LootItemPath = "Assets/Resources/Galaxy/Nyxara/Items/Thornhide/Thornhide.asset";
    const string PrefabPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Slime.prefab";
    const string VitalsPrefabPath = "Assets/Resources/HUD/VitalsBars/CreaturesVitalsBars.prefab";
    const string DamagePopupPath = "Assets/Resources/HUD/DamageNumber.prefab";
    const string HitFlashPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Shaders/HitFlash.mat";
    const float TargetHeight = 1.8f;
    const float DisplayScale = 2f;

    static readonly string ResultPath = Path.Combine(Application.dataPath, "..", "Temp", "SlimeSetup.result.txt");
    static readonly string TriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeSetupSlime.trigger");

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

    [MenuItem("BackHome/Creatures/Setup Slime")]
    public static void SetupFromMenu()
    {
        try
        {
            EnsureTextures();
            Material material = CreateMaterial();
            CreatureStats stats = CreateStats();
            BuildPrefab(material, stats);
            SlimeHopSetup.Apply();
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            WriteResult("ok");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            WriteResult("FAILED: " + e);
        }
    }

    static void EnsureTextures()
    {
        EnsureNormalMap(NormalPath);
        EnsureLinearData(MetallicPath);
        EnsureLinearData(RoughnessPath);
    }

    static void EnsureNormalMap(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null || importer.textureType == TextureImporterType.NormalMap)
            return;

        importer.textureType = TextureImporterType.NormalMap;
        importer.SaveAndReimport();
    }

    static void EnsureLinearData(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null || !importer.sRGBTexture)
            return;

        importer.sRGBTexture = false;
        importer.SaveAndReimport();
    }

    static Material CreateMaterial()
    {
        EnsureFolder("Assets/Resources/Galaxy/Nyxara/Creatures/Slime", "Materials");

        Shader shader = Shader.Find("BackHome/CasualToon");
        if (shader == null)
            throw new System.InvalidOperationException("BackHome/CasualToon shader was not found.");

        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        if (albedo == null)
            throw new System.InvalidOperationException("Slime albedo texture was not found.");

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "Slime" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_BumpMap", normal);
        material.SetTexture("_EmissionMap", null);
        material.SetColor("_EmissionColor", Color.black);
        material.SetFloat("_BumpScale", 1f);
        material.SetFloat("_Cull", 2f);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        material.shaderKeywords = System.Array.Empty<string>();
        EditorUtility.SetDirty(material);
        return material;
    }

    static CreatureStats CreateStats()
    {
        var stats = AssetDatabase.LoadAssetAtPath<CreatureStats>(StatsPath);
        if (stats == null)
        {
            stats = ScriptableObject.CreateInstance<CreatureStats>();
            AssetDatabase.CreateAsset(stats, StatsPath);
        }

        var lootItem = AssetDatabase.LoadAssetAtPath<ItemDefinition>(LootItemPath);
        var so = new SerializedObject(stats);
        so.FindProperty("displayName").stringValue = "Slime";
        so.FindProperty("attackDamage").floatValue = 8f;
        so.FindProperty("attackSpeed").floatValue = 1.2f;
        so.FindProperty("attackRange").floatValue = 1.6f;
        so.FindProperty("maxHealth").floatValue = 80f;
        so.FindProperty("visionRange").floatValue = 8f;
        so.FindProperty("movementSpeed").floatValue = 2.5f;

        SerializedProperty loot = so.FindProperty("loot");
        loot.arraySize = 1;
        SerializedProperty entry = loot.GetArrayElementAtIndex(0);
        entry.FindPropertyRelative("item").objectReferenceValue = lootItem;
        entry.FindPropertyRelative("minAmount").intValue = 1;
        entry.FindPropertyRelative("maxAmount").intValue = 1;
        entry.FindPropertyRelative("chance").floatValue = 1f;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(stats);
        return stats;
    }

    static void BuildPrefab(Material material, CreatureStats stats)
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
            throw new System.InvalidOperationException("Slime FBX was not found.");

        bool reimport = false;
        if (importer.animationType != ModelImporterAnimationType.Generic)
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            reimport = true;
        }

        if (!importer.importAnimation)
        {
            importer.importAnimation = true;
            reimport = true;
        }

        if (importer.importCameras || importer.importLights)
        {
            importer.importCameras = false;
            importer.importLights = false;
            reimport = true;
        }

        if (reimport)
            importer.SaveAndReimport();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
            throw new System.InvalidOperationException("Could not load Slime model.");

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            instance.name = "Slime";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            int removed = RemoveIcosphere(instance);

            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<AnimationClip>()
                .Where(c => c != null && !c.name.StartsWith("__preview"))
                .OrderByDescending(c => c.length)
                .FirstOrDefault();

            if (clip != null)
                clip.SampleAnimation(instance, clip.length * 0.5f);

            foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true))
                Object.DestroyImmediate(animator);

            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                    slots[i] = material;
                if (slots.Length == 0)
                    slots = new[] { material };
                renderer.sharedMaterials = slots;
            }

            if (!TryMeasureBounds(instance, out Bounds bounds))
                throw new System.InvalidOperationException("Slime mesh has no renderer bounds.");

            float height = bounds.size.y;
            float fit = 1f;
            if (height > 0.0001f && (height < 0.8f || height > 3.2f))
                fit = TargetHeight / height;

            instance.transform.localScale = Vector3.one * (fit * DisplayScale);
            if (!TryMeasureBounds(instance, out bounds))
                throw new System.InvalidOperationException("Slime mesh bounds disappeared after scaling.");

            // Meshy puts the pivot in the middle of the blob. Keep gameplay on a root
            // whose origin is the feet, and shift the mesh so it sits on the ground.
            var root = new GameObject("Slime");
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.name = "Body";
            instance.transform.SetParent(root.transform, true);
            instance.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);

            float barHeight = bounds.size.y + 0.25f;
            AddCreatureComponents(root, stats, barHeight);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[BackHome] Slime prefab saved. removedIcosphere={removed} height={bounds.size.y:0.00} scale={fit * DisplayScale:0.00}");
        }
        finally
        {
            if (instance != null)
            {
                GameObject root = instance.transform.parent != null ? instance.transform.parent.gameObject : instance;
                Object.DestroyImmediate(root);
            }
        }
    }

    static int RemoveIcosphere(GameObject instance)
    {
        var doomed = new List<GameObject>();
        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
        {
            if (child == null || child == instance.transform)
                continue;
            if (child.name == "Icosphere")
                doomed.Add(child.gameObject);
        }

        foreach (GameObject extra in doomed)
            Object.DestroyImmediate(extra);

        return doomed.Count;
    }

    static bool TryMeasureBounds(GameObject instance, out Bounds bounds)
    {
        bool any = false;
        bounds = new Bounds();
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is not SkinnedMeshRenderer && renderer is not MeshRenderer)
                continue;

            if (!any)
            {
                bounds = renderer.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return any;
    }

    static void AddCreatureComponents(GameObject instance, CreatureStats stats, float barHeight)
    {
        Creature creature = instance.GetComponent<Creature>();
        if (creature == null)
            creature = instance.AddComponent<Creature>();

        var creatureSo = new SerializedObject(creature);
        creatureSo.FindProperty("stats").objectReferenceValue = stats;
        creatureSo.FindProperty("destroyDelay").floatValue = 0.1f;
        creatureSo.ApplyModifiedPropertiesWithoutUndo();

        VitalsBars bars = instance.GetComponent<VitalsBars>();
        if (bars == null)
            bars = instance.AddComponent<VitalsBars>();

        var barsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VitalsPrefabPath);
        var barsSo = new SerializedObject(bars);
        barsSo.FindProperty("vitalsBarsPrefab").objectReferenceValue =
            barsPrefab != null ? barsPrefab.GetComponent<VitalsBarsView>() : null;
        barsSo.FindProperty("localOffset").vector3Value = new Vector3(0f, barHeight, 0f);
        barsSo.FindProperty("worldScale").floatValue = 1.6f;
        barsSo.FindProperty("hideWhenDead").boolValue = true;
        barsSo.FindProperty("hideUntilDamaged").boolValue = true;
        barsSo.FindProperty("visibleAfterDamage").floatValue = 2.5f;
        barsSo.FindProperty("fadeDuration").floatValue = 0.2f;
        barsSo.ApplyModifiedPropertiesWithoutUndo();

        if (instance.GetComponent<CreatureRangeCombat>() == null)
            instance.AddComponent<CreatureRangeCombat>();

        DamageNumbers numbers = instance.GetComponent<DamageNumbers>();
        if (numbers == null)
            numbers = instance.AddComponent<DamageNumbers>();

        var popupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DamagePopupPath);
        var numbersSo = new SerializedObject(numbers);
        numbersSo.FindProperty("popupPrefab").objectReferenceValue =
            popupPrefab != null ? popupPrefab.GetComponent<DamagePopup>() : null;
        numbersSo.FindProperty("localOffset").vector3Value = new Vector3(0f, barHeight + 0.2f, 0f);
        numbersSo.ApplyModifiedPropertiesWithoutUndo();

        CreatureChase chase = instance.GetComponent<CreatureChase>();
        if (chase == null)
            chase = instance.AddComponent<CreatureChase>();

        var chaseSo = new SerializedObject(chase);
        chaseSo.FindProperty("alignSpeed").floatValue = 10f;
        chaseSo.FindProperty("footOffset").floatValue = 0.05f;
        chaseSo.FindProperty("groundProbeDistance").floatValue = 12f;
        chaseSo.FindProperty("groundLayer").intValue = 1 << 3;
        chaseSo.FindProperty("loseVisionDelay").floatValue = 3f;
        chaseSo.FindProperty("homeArriveDistance").floatValue = 0.35f;
        chaseSo.FindProperty("knockbackDistance").floatValue = 0.2f;
        chaseSo.FindProperty("knockbackDuration").floatValue = 0.16f;
        chaseSo.FindProperty("knockbackCooldown").floatValue = 0.5f;
        chaseSo.ApplyModifiedPropertiesWithoutUndo();

        SphereCollider body = instance.GetComponent<SphereCollider>();
        if (body == null)
            body = instance.AddComponent<SphereCollider>();
        body.isTrigger = true;
        body.center = new Vector3(0f, 1.7f, 0f);
        body.radius = 1.7f;

        if (instance.GetComponent<SlimeBody>() == null)
            instance.AddComponent<SlimeBody>();

        CreatureHitFlash flash = instance.GetComponent<CreatureHitFlash>();
        if (flash == null)
            flash = instance.AddComponent<CreatureHitFlash>();

        var flashSo = new SerializedObject(flash);
        flashSo.FindProperty("overlayMaterial").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Material>(HitFlashPath);
        flashSo.FindProperty("hitColor").colorValue = Color.white;
        flashSo.FindProperty("flashDuration").floatValue = 0.1f;
        flashSo.ApplyModifiedPropertiesWithoutUndo();
    }

    static void EnsureFolder(string parent, string child)
    {
        string folder = parent + "/" + child;
        if (AssetDatabase.IsValidFolder(folder))
            return;

        AssetDatabase.CreateFolder(parent, child);
    }

    static void WriteResult(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
            File.WriteAllText(ResultPath, message);
        }
        catch { /* ignore */ }

        Debug.Log("[BackHome] Slime setup: " + message);
    }
}
