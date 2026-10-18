using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Builds King Snarlfang: CasualToon material, boss stats, and a spawn/idle/run/attack/death animator.
/// Does not assign the prefab on CreatureSpawner.
/// </summary>
public static class KingSnarlfangSetup
{
    const string Root = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/King";
    const string IdleModelPath = Root + "/Models/King_Snarlfang_Animation_Idle1.fbx";
    const string RunningModelPath = Root + "/Models/King_Snarlfang_Animation_Running.fbx";
    const string AttackModelPath = Root + "/Models/King_Snarlfang_Animation_Attack.fbx";
    const string DeathModelPath = Root + "/Models/King_Snarlfang_Animation_Death.fbx";
    const string SpawnModelPath = Root + "/Models/King_Snarlfang_Animation_Spwan.fbx";
    const string IdleClipPath = Root + "/Animations/Idle1.anim";
    const string RunningClipPath = Root + "/Animations/Running.anim";
    const string AttackClipPath = Root + "/Animations/LightAttack.anim";
    const string DeathClipPath = Root + "/Animations/Death.anim";
    const string SpawnClipPath = Root + "/Animations/Spawn.anim";
    const string ControllerPath = Root + "/Animations/King_Snarlfang.controller";
    const string AlbedoPath = Root + "/Textures/King_Snarlfang_texture.png";
    const string NormalPath = Root + "/Textures/King_Snarlfang_normal.png";
    const string MetallicPath = Root + "/Textures/King_Snarlfang_metallic.png";
    const string RoughnessPath = Root + "/Textures/King_Snarlfang_roughness.png";
    const string MaterialPath = Root + "/Materials/King_Snarlfang.mat";
    const string SourceMaterialPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Mob/Materials/Snarlfang.mat";
    const string StatsPath = Root + "/King_SnarlfangStats.asset";
    const string SourceStatsPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/SnarlfangStats.asset";
    const string PrefabPath = Root + "/King_Snarlfang.prefab";
    const string VitalsPrefabPath = "Assets/Resources/HUD/VitalsBars/CreaturesVitalsBars.prefab";
    const string DamagePopupPath = "Assets/Resources/HUD/DamageNumber.prefab";
    const string HitFlashPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Shaders/HitFlash.mat";
    const float TargetHeight = 4.2f;
    const float ModelScale = 2f;

    static readonly string TriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeSetupKingSnarlfang.trigger");
    static readonly string ResultPath = Path.Combine(Application.dataPath, "..", "Temp", "KingSnarlfangSetup.result.txt");

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

    [MenuItem("BackHome/Creatures/Setup King Snarlfang")]
    public static void SetupFromMenu()
    {
        Setup(force: true);
    }

    static void Setup(bool force)
    {
        try
        {
            bool hasPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            bool animatorReady = AnimatorReady();
            if (!force && hasPrefab && animatorReady)
            {
                WriteResult("already set up");
                return;
            }

            EnsureTextures();
            Material material = CreateMaterial();
            CreatureStats stats = CreateStats(AssetDatabase.LoadAssetAtPath<CreatureStats>(StatsPath) == null);

            if (force || !hasPrefab)
                BuildPrefab(material, stats);
            else
                EnsureKingComponent();

            if (force || !AnimatorReady())
                EnsureAnimator(force);

            AssetDatabase.SaveAssets();
            WriteResult("ok");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            WriteResult("FAILED: " + e);
        }
    }

    static bool AnimatorReady()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (controller == null || prefab == null || controller.layers == null || controller.layers.Length == 0)
            return false;

        Animator animator = prefab.GetComponent<Animator>();
        if (animator == null || animator.runtimeAnimatorController != controller)
            return false;
        if (prefab.GetComponent<AreaKing>() == null)
            return false;

        return FindState(controller.layers[0].stateMachine, "Spawn") != null
            && FindState(controller.layers[0].stateMachine, "Death") != null
            && FindState(controller.layers[0].stateMachine, "LightAttack") != null
            && FindState(controller.layers[0].stateMachine, "Run") != null;
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
        EnsureFolder(Root, "Materials");
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        if (albedo == null)
            throw new System.InvalidOperationException("King Snarlfang albedo texture was not found.");

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            if (!AssetDatabase.CopyAsset(SourceMaterialPath, MaterialPath))
            {
                Shader shader = Shader.Find("BackHome/CasualToon");
                if (shader == null)
                    throw new System.InvalidOperationException("BackHome/CasualToon shader was not found.");

                material = new Material(shader) { name = "King_Snarlfang" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        }

        material.name = "King_Snarlfang";
        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_BumpMap", normal);
        material.SetFloat("_BumpScale", 1f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static CreatureStats CreateStats(bool applyNumbers)
    {
        if (AssetDatabase.LoadAssetAtPath<CreatureStats>(StatsPath) == null)
        {
            if (!AssetDatabase.CopyAsset(SourceStatsPath, StatsPath))
                throw new System.InvalidOperationException("Could not copy Snarlfang stats for the king.");
            applyNumbers = true;
        }

        var stats = AssetDatabase.LoadAssetAtPath<CreatureStats>(StatsPath);
        var so = new SerializedObject(stats);
        so.FindProperty("displayName").stringValue = "King Snarlfang";
        if (applyNumbers)
        {
            so.FindProperty("attackDamage").floatValue = 32f;
            so.FindProperty("attackSpeed").floatValue = 0.8f;
            so.FindProperty("attackRange").floatValue = 3.5f;
            so.FindProperty("maxHealth").floatValue = 1600f;
            so.FindProperty("visionRange").floatValue = 18f;
            so.FindProperty("movementSpeed").floatValue = 6f;

            SerializedProperty loot = so.FindProperty("loot");
            if (loot != null && loot.arraySize > 0)
            {
                SerializedProperty entry = loot.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("minAmount").intValue = 3;
                entry.FindPropertyRelative("maxAmount").intValue = 6;
                entry.FindPropertyRelative("chance").floatValue = 1f;
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(stats);
        return stats;
    }

    static void BuildPrefab(Material material, CreatureStats stats)
    {
        var importer = AssetImporter.GetAtPath(IdleModelPath) as ModelImporter;
        if (importer == null)
            throw new System.InvalidOperationException("King Snarlfang idle FBX was not found.");

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

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(IdleModelPath);
        if (model == null)
            throw new System.InvalidOperationException("Could not load King Snarlfang idle model.");

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            instance.name = "King_Snarlfang";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            RemoveIcosphere(instance);

            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(IdleModelPath)
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

            float height = MeasureHeight(instance);
            if (height > 0.05f && Mathf.Abs(height - TargetHeight) > 0.2f)
            {
                instance.transform.localScale = Vector3.one * (TargetHeight / height);
                height = MeasureHeight(instance);
            }

            foreach (Transform child in instance.transform)
            {
                if (child.name == "target_character" || child.name == "Mesh_0")
                    child.localScale *= ModelScale;
            }

            float barHeight = height > 0.05f ? height + 0.45f : 4.6f;
            AddCreatureComponents(instance, stats, barHeight);
            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Debug.Log($"[BackHome] King Snarlfang prefab saved. height={height:0.00}");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    static void EnsureKingComponent()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (root.GetComponent<AreaKing>() == null)
                root.AddComponent<AreaKing>();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static int RemoveIcosphere(GameObject instance)
    {
        var doomed = new List<GameObject>();
        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
        {
            if (child != null && child != instance.transform && child.name == "Icosphere")
                doomed.Add(child.gameObject);
        }

        foreach (GameObject extra in doomed)
            Object.DestroyImmediate(extra);
        return doomed.Count;
    }

    static float MeasureHeight(GameObject instance)
    {
        float top = float.NegativeInfinity;
        float bottom = float.PositiveInfinity;
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is not SkinnedMeshRenderer && renderer is not MeshRenderer)
                continue;

            top = Mathf.Max(top, renderer.bounds.max.y);
            bottom = Mathf.Min(bottom, renderer.bounds.min.y);
        }

        if (float.IsNegativeInfinity(top) || float.IsPositiveInfinity(bottom))
            return 0f;

        return top - bottom;
    }

    static void AddCreatureComponents(GameObject instance, CreatureStats stats, float barHeight)
    {
        Creature creature = instance.GetComponent<Creature>() ?? instance.AddComponent<Creature>();
        var creatureSo = new SerializedObject(creature);
        creatureSo.FindProperty("stats").objectReferenceValue = stats;
        creatureSo.FindProperty("destroyDelay").floatValue = 0.1f;
        creatureSo.ApplyModifiedPropertiesWithoutUndo();

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

        CreatureHitFlash flash = instance.GetComponent<CreatureHitFlash>() ?? instance.AddComponent<CreatureHitFlash>();
        var flashSo = new SerializedObject(flash);
        flashSo.FindProperty("overlayMaterial").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Material>(HitFlashPath);
        flashSo.FindProperty("hitColor").colorValue = Color.white;
        flashSo.FindProperty("flashDuration").floatValue = 0.12f;
        flashSo.ApplyModifiedPropertiesWithoutUndo();

        if (instance.GetComponent<AreaKing>() == null)
            instance.AddComponent<AreaKing>();
    }

    static void EnsureAnimator(bool force)
    {
        AnimationClip idle = LoadOrExtract(force, IdleClipPath, IdleModelPath, "Idle1", loop: true);
        AnimationClip running = LoadOrExtract(force, RunningClipPath, RunningModelPath, "Running", loop: true);
        AnimationClip attack = LoadOrExtract(force, AttackClipPath, AttackModelPath, "LightAttack", loop: true);
        AnimationClip death = LoadOrExtract(force, DeathClipPath, DeathModelPath, "Death", loop: false);
        AnimationClip spawn = LoadOrExtract(force, SpawnClipPath, SpawnModelPath, "Spawn", loop: false);
        AnimatorController controller = CreateController(idle, running, attack, death, spawn);
        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(IdleModelPath).OfType<Avatar>().FirstOrDefault();
        WireAnimator(controller, avatar);
        string paths = string.Join(" | ", SamplePaths(spawn).Take(4));
        Debug.Log($"[BackHome] King animator idle={idle.length:0.00}s run={running.length:0.00}s attack={attack.length:0.00}s death={death.length:0.00}s spawn={spawn.length:0.00}s paths={paths}");
    }

    static AnimationClip LoadOrExtract(bool force, string clipPath, string modelPath, string clipName, bool loop)
    {
        if (!force)
        {
            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (existing != null)
                return existing;
        }

        return ExtractClip(modelPath, clipName, clipPath, loop);
    }

    static AnimatorController CreateController(
        AnimationClip idle,
        AnimationClip running,
        AnimationClip attack,
        AnimationClip death,
        AnimationClip spawn)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
            ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        foreach (AnimatorControllerParameter param in controller.parameters.ToList())
            controller.RemoveParameter(param);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState child in machine.states.ToList())
            machine.RemoveState(child.state);
        foreach (AnimatorStateTransition any in machine.anyStateTransitions.ToList())
            machine.RemoveAnyStateTransition(any);

        controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("IsAttacking", AnimatorControllerParameterType.Bool);
        controller.AddParameter("AttackKind", AnimatorControllerParameterType.Int);
        controller.AddParameter("AttackAnimSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);

        AnimatorState idleState = machine.AddState("Idle1", new Vector3(280, 0, 0));
        idleState.motion = idle;
        AnimatorState run = machine.AddState("Run", new Vector3(540, 0, 0));
        run.motion = running;
        AnimatorState light = machine.AddState("LightAttack", new Vector3(540, 160, 0));
        light.motion = attack;
        light.speedParameterActive = true;
        light.speedParameter = "AttackAnimSpeed";
        AnimatorState deathState = machine.AddState("Death", new Vector3(800, 80, 0));
        deathState.motion = death;
        AnimatorState spawnState = machine.AddState("Spawn", new Vector3(280, 180, 0));
        spawnState.motion = spawn;
        machine.defaultState = idleState;

        LinkMove(idleState, run, moving: true);
        LinkMove(run, idleState, moving: false);
        LinkAttack(idleState, light);
        LinkAttack(run, light);
        LinkAttackExit(light, run, moving: true);
        LinkAttackExit(light, idleState, moving: false);

        AnimatorStateTransition die = machine.AddAnyStateTransition(deathState);
        die.hasExitTime = false;
        die.duration = 0.05f;
        die.hasFixedDuration = true;
        die.canTransitionToSelf = false;
        die.AddCondition(AnimatorConditionMode.If, 0, "Die");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static void LinkMove(AnimatorState from, AnimatorState to, bool moving)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0.12f;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAttacking");
        transition.AddCondition(moving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "IsMoving");
    }

    static void LinkAttack(AnimatorState from, AnimatorState to)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0.08f;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.If, 0, "IsAttacking");
        transition.AddCondition(AnimatorConditionMode.Equals, 0, "AttackKind");
    }

    static void LinkAttackExit(AnimatorState from, AnimatorState to, bool moving)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0.1f;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAttacking");
        transition.AddCondition(moving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "IsMoving");
    }

    static AnimatorState FindState(AnimatorStateMachine machine, string stateName)
    {
        if (machine == null)
            return null;

        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state != null && child.state.name == stateName)
                return child.state;
        }

        return null;
    }

    static void WireAnimator(AnimatorController controller, Avatar avatar)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            // Unity's ?? does not treat a destroyed Animator as null, and assigning the controller
            // through the property then throws. Add it explicitly and write the serialized fields.
            Animator animator = root.GetComponent<Animator>();
            if (animator == null)
                animator = root.AddComponent<Animator>();

            var animatorSo = new SerializedObject(animator);
            animatorSo.FindProperty("m_Controller").objectReferenceValue = controller;
            animatorSo.FindProperty("m_Avatar").objectReferenceValue = avatar;
            animatorSo.FindProperty("m_ApplyRootMotion").boolValue = false;
            animatorSo.FindProperty("m_CullingMode").intValue = (int)AnimatorCullingMode.AlwaysAnimate;
            animatorSo.ApplyModifiedPropertiesWithoutUndo();

            if (root.GetComponent<CreatureAnimator>() == null)
                root.AddComponent<CreatureAnimator>();
            if (root.GetComponent<AreaKing>() == null)
                root.AddComponent<AreaKing>();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static AnimationClip ExtractClip(string modelPath, string clipName, string outputPath, bool loop)
    {
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null)
            throw new System.InvalidOperationException("Missing model at " + modelPath);

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.optimizeGameObjects = false;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.importAnimation = true;
        importer.SaveAndReimport();

        importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer.importedTakeInfos == null || importer.importedTakeInfos.Length == 0)
            throw new System.InvalidOperationException(modelPath + " has no animation take.");

        TakeInfo selected = SelectLongestTake(importer.importedTakeInfos);
        importer.clipAnimations = new[]
        {
            new ModelImporterClipAnimation
            {
                name = clipName,
                takeName = selected.name,
                firstFrame = selected.startTime * selected.sampleRate,
                lastFrame = selected.stopTime * selected.sampleRate,
                loopTime = loop,
                loopPose = false,
                wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever
            }
        };
        importer.SaveAndReimport();

        AnimationClip source = AssetDatabase.LoadAllAssetsAtPath(modelPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == clipName && !c.name.StartsWith("__preview"));
        if (source == null)
            throw new System.InvalidOperationException("Could not read clip " + clipName + " from " + modelPath);

        return BakeInPlaceClip(source, outputPath, loop);
    }

    static AnimationClip BakeInPlaceClip(AnimationClip source, string outputPath, bool loop)
    {
        var clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(outputPath) };
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(source);
        settings.loopTime = loop;
        settings.loopBlend = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
        {
            if (binding.type == typeof(Transform) && binding.propertyName.StartsWith("m_LocalScale."))
                continue;

            string path = RemapBonePath(binding.path);
            if (path == "target_character")
                continue;

            AnimationCurve curve = AnimationUtility.GetEditorCurve(source, binding);
            // target_character is rotated -90° on X, so hips local Y is forward and local Z is up.
            // Root-motion runs slide along Y and then the loop snaps them backward.
            bool lockHips = path.EndsWith("Hips", System.StringComparison.Ordinal)
                && (binding.propertyName == "m_LocalPosition.x" || binding.propertyName == "m_LocalPosition.y");
            if (lockHips && curve.keys.Length > 0)
            {
                float locked = curve.keys[0].value;
                curve = AnimationCurve.Constant(curve.keys[0].time, curve.keys[curve.keys.Length - 1].time, locked);
            }

            var remapped = binding;
            remapped.path = path;
            AnimationUtility.SetEditorCurve(clip, remapped, curve);
        }

        return SaveClip(clip, outputPath);
    }

    static AnimationClip SaveClip(AnimationClip clip, string outputPath)
    {
        EnsureFolder(Root, "Animations");
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(clip, outputPath);
            return clip;
        }

        string name = existing.name;
        EditorUtility.CopySerialized(clip, existing);
        existing.name = name;
        EditorUtility.SetDirty(existing);
        AssetDatabase.SaveAssetIfDirty(existing);
        Object.DestroyImmediate(clip);
        return existing;
    }

    static TakeInfo SelectLongestTake(TakeInfo[] takes)
    {
        TakeInfo best = takes[0];
        float bestLength = best.stopTime - best.startTime;
        foreach (TakeInfo take in takes)
        {
            float length = take.stopTime - take.startTime;
            if (length > bestLength)
            {
                best = take;
                bestLength = length;
            }
        }

        return best;
    }

    static string RemapBonePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        const string liveRoot = "target_character";
        int slash = path.IndexOf('/');
        string head = slash < 0 ? path : path.Substring(0, slash);
        if (head == liveRoot)
            return path;
        if (head.StartsWith("mixamorig:", System.StringComparison.Ordinal))
            return liveRoot + "/" + path;
        if (slash >= 0)
            return liveRoot + path.Substring(slash);
        return path;
    }

    static IEnumerable<string> SamplePaths(AnimationClip clip)
    {
        return AnimationUtility.GetCurveBindings(clip).Select(b => b.path).Distinct();
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

        Debug.Log("[BackHome] King Snarlfang setup: " + message);
    }
}
