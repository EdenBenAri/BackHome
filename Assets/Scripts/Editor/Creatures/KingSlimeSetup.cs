using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds King Slime: CasualToon material, boss stats, and a prefab twice the mob's size.
/// The hop is the same squash-and-pop as the mob, lifted higher so the bigger body still reads.
/// Does not assign the prefab on CreatureSpawner.
/// </summary>
public static class KingSlimeSetup
{
    const string Root = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/King";
    const string ModelPath = Root + "/Models/King_Slime.fbx";
    const string AlbedoPath = Root + "/Textures/King_Slime_texture.png";
    const string NormalPath = Root + "/Textures/King_Slime_normal.png";
    const string MetallicPath = Root + "/Textures/King_Slime_metallic.png";
    const string RoughnessPath = Root + "/Textures/King_Slime_roughness.png";
    const string MaterialPath = Root + "/Materials/King_Slime.mat";
    const string SourceMaterialPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Mob/Materials/Slime.mat";
    const string LegacyMaterialPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Materials/Slime.mat";
    const string StatsPath = Root + "/King_SlimeStats.asset";
    const string PrefabPath = Root + "/King_Slime.prefab";
    const string IdleClipPath = Root + "/Animations/Idle.anim";
    const string JumpClipPath = Root + "/Animations/Jump.anim";
    const string ControllerPath = Root + "/Animations/King_Slime.controller";
    const string VitalsPrefabPath = "Assets/Resources/HUD/VitalsBars/VitalsBars.prefab";
    const string DamagePopupPath = "Assets/Resources/HUD/DamageNumber.prefab";
    const string HitFlashPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Shaders/HitFlash.mat";
    const float TargetHeight = 1.8f;
    const float DisplayScale = 2f;
    const float KingScale = 2f;

    static readonly string TriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeSetupKingSlime.trigger");
    static readonly string ResultPath = Path.Combine(Application.dataPath, "..", "Temp", "KingSlimeSetup.result.txt");

    static bool _autoStarted;

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.update += WatchTrigger;
        EditorApplication.delayCall += AutoSetup;
        if (!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
            TryRunPendingSetup();
    }

    public static void TryRunPendingSetup()
    {
        if (!File.Exists(TriggerPath))
            return;

        try { File.Delete(TriggerPath); } catch { /* ignore */ }
        Setup(force: true);
    }

    static void WatchTrigger()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        TryRunPendingSetup();
    }

    static void AutoSetup()
    {
        if (_autoStarted || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += AutoSetup;
            return;
        }

        _autoStarted = true;
        Setup(force: false);
    }

    [MenuItem("BackHome/Creatures/Setup King Slime")]
    public static void SetupFromMenu()
    {
        Setup(force: true);
    }

    static void Setup(bool force)
    {
        try
        {
            bool hasPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            bool hasHop = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath) != null;
            if (!force && hasPrefab && hasHop && AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) != null
                && AssetDatabase.LoadAssetAtPath<CreatureStats>(StatsPath) != null)
            {
                WriteResult("already set up");
                return;
            }

            EnsureTextures();
            Material material = CreateMaterial();
            CreatureStats stats = CreateStats(AssetDatabase.LoadAssetAtPath<CreatureStats>(StatsPath) == null);

            bool builtPrefab = false;
            if (force || !hasPrefab)
            {
                BuildPrefab(material, stats);
                builtPrefab = true;
            }

            if (force || builtPrefab || !hasHop)
                SlimeHopSetup.ApplyTo(PrefabPath, IdleClipPath, JumpClipPath, ControllerPath, KingScale);

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
        CreatureAssetUtil.EnsurePbrMaps(NormalPath, MetallicPath, RoughnessPath);
    }

    static Material CreateMaterial()
    {
        CreatureAssetUtil.EnsureFolder(Root, "Materials");
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        if (albedo == null)
            throw new System.InvalidOperationException("King Slime albedo texture was not found.");

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            string source = AssetDatabase.LoadAssetAtPath<Material>(SourceMaterialPath) != null
                ? SourceMaterialPath
                : LegacyMaterialPath;
            if (!AssetDatabase.CopyAsset(source, MaterialPath))
            {
                Shader shader = Shader.Find("BackHome/CasualToon");
                if (shader == null)
                    throw new System.InvalidOperationException("BackHome/CasualToon shader was not found.");

                material = new Material(shader) { name = "King_Slime" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        }

        material.name = "King_Slime";
        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_BumpMap", normal);
        material.SetFloat("_BumpScale", 1f);
        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", albedo);

        var metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(MetallicPath);
        if (metallic != null && material.HasProperty("_MetallicGlossMap"))
            material.SetTexture("_MetallicGlossMap", metallic);

        EditorUtility.SetDirty(material);
        return material;
    }

    static CreatureStats CreateStats(bool applyNumbers)
    {
        var stats = AssetDatabase.LoadAssetAtPath<CreatureStats>(StatsPath);
        if (stats == null)
        {
            stats = ScriptableObject.CreateInstance<CreatureStats>();
            AssetDatabase.CreateAsset(stats, StatsPath);
            applyNumbers = true;
        }

        var so = new SerializedObject(stats);
        so.FindProperty("displayName").stringValue = "King Slime";
        so.FindProperty("level").intValue = 6;
        if (applyNumbers)
        {
            so.FindProperty("attackDamage").floatValue = 26f;
            so.FindProperty("attackSpeed").floatValue = 1.1f;
            so.FindProperty("attackRange").floatValue = 4.5f;
            so.FindProperty("maxHealth").floatValue = 1200f;
            so.FindProperty("visionRange").floatValue = 18f;
            so.FindProperty("movementSpeed").floatValue = 3.2f;
            so.FindProperty("experience").intValue = 160;

            CreatureAssetUtil.WriteKingLoot(so);
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(stats);
        return stats;
    }

    static void BuildPrefab(Material material, CreatureStats stats)
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
            throw new System.InvalidOperationException("King Slime FBX was not found.");

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
            throw new System.InvalidOperationException("Could not load King Slime model.");

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            instance.name = "King_Slime";
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
                throw new System.InvalidOperationException("King Slime mesh has no renderer bounds.");

            float height = bounds.size.y;
            float fit = 1f;
            if (height > 0.0001f && (height < 0.8f || height > 3.2f))
                fit = TargetHeight / height;

            instance.transform.localScale = Vector3.one * (fit * DisplayScale * KingScale);
            if (!TryMeasureBounds(instance, out bounds))
                throw new System.InvalidOperationException("King Slime mesh bounds disappeared after scaling.");

            var root = new GameObject("King_Slime");
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.name = "Body";
            instance.transform.SetParent(root.transform, true);
            instance.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);

            float barHeight = bounds.size.y + 0.45f;
            AddCreatureComponents(root, stats, barHeight, bounds.extents);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[BackHome] King Slime prefab saved. removedIcosphere={removed} height={bounds.size.y:0.00} scale={fit * DisplayScale * KingScale:0.00}");
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

    static void AddCreatureComponents(GameObject instance, CreatureStats stats, float barHeight, Vector3 extents)
    {
        Creature creature = instance.GetComponent<Creature>() ?? instance.AddComponent<Creature>();
        var creatureSo = new SerializedObject(creature);
        creatureSo.FindProperty("stats").objectReferenceValue = stats;
        creatureSo.FindProperty("destroyDelay").floatValue = 0.1f;
        creatureSo.ApplyModifiedPropertiesWithoutUndo();

        if (instance.GetComponent<AreaKing>() == null)
            instance.AddComponent<AreaKing>();

        VitalsBars bars = instance.GetComponent<VitalsBars>() ?? instance.AddComponent<VitalsBars>();
        var barsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VitalsPrefabPath);
        var barsSo = new SerializedObject(bars);
        barsSo.FindProperty("vitalsBarsPrefab").objectReferenceValue =
            barsPrefab != null ? barsPrefab.GetComponent<VitalsBarsView>() : null;
        barsSo.FindProperty("localOffset").vector3Value = new Vector3(0f, barHeight, 0f);
        barsSo.FindProperty("worldScale").floatValue = 2.6f;
        barsSo.FindProperty("hideWhenDead").boolValue = true;
        barsSo.FindProperty("hideUntilDamaged").boolValue = true;
        barsSo.FindProperty("visibleAfterDamage").floatValue = 3.5f;
        barsSo.FindProperty("fadeDuration").floatValue = 0.2f;
        barsSo.ApplyModifiedPropertiesWithoutUndo();

        if (instance.GetComponent<CreatureRangeCombat>() == null)
            instance.AddComponent<CreatureRangeCombat>();

        DamageNumbers numbers = instance.GetComponent<DamageNumbers>() ?? instance.AddComponent<DamageNumbers>();
        var popupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DamagePopupPath);
        var numbersSo = new SerializedObject(numbers);
        numbersSo.FindProperty("popupPrefab").objectReferenceValue =
            popupPrefab != null ? popupPrefab.GetComponent<DamagePopup>() : null;
        numbersSo.FindProperty("localOffset").vector3Value = new Vector3(0f, barHeight + 0.25f, 0f);
        numbersSo.ApplyModifiedPropertiesWithoutUndo();

        CreatureChase chase = instance.GetComponent<CreatureChase>() ?? instance.AddComponent<CreatureChase>();
        var chaseSo = new SerializedObject(chase);
        chaseSo.FindProperty("alignSpeed").floatValue = 8f;
        chaseSo.FindProperty("footOffset").floatValue = 0.05f;
        chaseSo.FindProperty("groundProbeDistance").floatValue = 12f;
        chaseSo.FindProperty("groundLayer").intValue = 1 << 3;
        chaseSo.FindProperty("loseVisionDelay").floatValue = 4f;
        chaseSo.FindProperty("homeArriveDistance").floatValue = 0.6f;
        chaseSo.FindProperty("knockbackDistance").floatValue = 0.12f;
        chaseSo.FindProperty("knockbackDuration").floatValue = 0.14f;
        chaseSo.FindProperty("knockbackCooldown").floatValue = 0.55f;
        chaseSo.ApplyModifiedPropertiesWithoutUndo();

        float radius = Mathf.Clamp(Mathf.Max(extents.x, extents.z), 0.45f, 6f);
        if (instance.GetComponent<SlimeBody>() == null)
            instance.AddComponent<SlimeBody>();

        SphereCollider body = instance.GetComponent<SphereCollider>();
        if (body == null)
            body = instance.AddComponent<SphereCollider>();
        body.isTrigger = true;
        body.center = new Vector3(0f, radius, 0f);
        body.radius = radius;

        CreatureHitFlash flash = instance.GetComponent<CreatureHitFlash>() ?? instance.AddComponent<CreatureHitFlash>();
        var flashSo = new SerializedObject(flash);
        flashSo.FindProperty("overlayMaterial").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Material>(HitFlashPath);
        flashSo.FindProperty("hitColor").colorValue = Color.white;
        flashSo.FindProperty("flashDuration").floatValue = 0.12f;
        flashSo.ApplyModifiedPropertiesWithoutUndo();
    }

    static void WriteResult(string message)
    {
        CreatureAssetUtil.WriteResult(ResultPath, "King Slime setup", message);
    }
}
