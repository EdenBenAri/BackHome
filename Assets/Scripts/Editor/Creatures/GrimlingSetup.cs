using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Builds Grimling the same way as Snarlfang: CasualToon material, creature components,
/// a 50/50 Idle1/Idle2 animator, and a Run state driven by IsMoving.
/// Drops the extra Icosphere mesh that ships in the middle of the Meshy export.
/// </summary>
public static class GrimlingSetup
{
    const string ModelPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Models/Grimling_Animation_Idle1.fbx";
    const string Idle2ModelPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Models/Grimling_Animation_Idle2.fbx";
    const string RunningModelPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Models/Grimling_Animation_Running.fbx";
    const string MeleeModelPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Models/Grimling_Animation_Melee_Attack.fbx";
    const string DeathModelPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Models/Grimling_Animation_Death.fbx";
    const string Idle1ClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Animations/Idle1.anim";
    const string Idle2ClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Animations/Idle2.anim";
    const string RunningClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Animations/Running.anim";
    const string MeleeClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Animations/MeleeAttack.anim";
    const string DeathClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Animations/Death.anim";
    const string ControllerPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Animations/Grimling.controller";
    const string AlbedoPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Textures/Grimling_texture.png";
    const string NormalPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Textures/Grimling_normal.png";
    const string MetallicPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Textures/Grimling_metallic.png";
    const string RoughnessPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Textures/Grimling_roughness.png";
    const string MaterialPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Materials/Grimling.mat";
    const string StatsPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/GrimlingStats.asset";
    const string PrefabPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Grimling.prefab";
    const string PrefabGuid = "999c9df1ae9ff0641a8369f4be6b7ed5";
    const string VitalsPrefabPath = "Assets/Resources/HUD/VitalsBars/CreaturesVitalsBars.prefab";
    const string DamagePopupPath = "Assets/Resources/HUD/DamageNumber.prefab";
    const string HitFlashPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Shaders/HitFlash.mat";
    const float TargetHeight = 1.8f;
    const float DisplayScale = 3f;

    static readonly string ResultPath = Path.Combine(Application.dataPath, "..", "Temp", "GrimlingSetup.result.txt");
    static readonly string TriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeSetupGrimling.trigger");
    static readonly string PatchTriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomePatchGrimling.trigger");

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.update += WatchTrigger;
        EditorApplication.delayCall += WatchTrigger;
    }

    public static void TryRunPendingSetup()
    {
        WatchTrigger();
    }

    static void WatchTrigger()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (File.Exists(PatchTriggerPath))
        {
            try { File.Delete(PatchTriggerPath); } catch { /* ignore */ }
            PatchScaleAndSpawns();
        }

        if (!File.Exists(TriggerPath))
            return;

        try { File.Delete(TriggerPath); } catch { /* ignore */ }
        SetupFromMenu();
    }

    [MenuItem("BackHome/Creatures/Setup Grimling")]
    public static void SetupFromMenu()
    {
        try
        {
            EnsureTextures();
            Material material = CreateMaterial();
            CreatureStats stats = AssetDatabase.LoadAssetAtPath<CreatureStats>(StatsPath);
            if (stats == null)
                throw new System.InvalidOperationException("GrimlingStats was not found.");

            BuildPrefab(material, stats);
            KeepPrefabGuid();

            AnimationClip idle1 = ExtractLoopingClip(ModelPath, "Idle1", Idle1ClipPath);
            AnimationClip idle2 = ExtractLoopingClip(Idle2ModelPath, "Idle2", Idle2ClipPath);
            AnimationClip running = ExtractLoopingClip(RunningModelPath, "Running", RunningClipPath);
            AnimatorController controller = CreateIdleController(idle1, idle2, running);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            WireAnimator(controller, avatar);

            AnimationClip melee = ExtractLoopingClip(MeleeModelPath, "LightAttack", MeleeClipPath, loop: true);
            AddAttackStates(controller, melee);

            AnimationClip death = ExtractLoopingClip(DeathModelPath, "Death", DeathClipPath, loop: false);
            AddDeathState(controller, death);

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            KeepPrefabGuid();
            AnimatorGraphRepair.Repair();
            WriteResult($"ok idle1={idle1.length:0.00}s idle2={idle2.length:0.00}s run={running.length:0.00}s melee={melee.length:0.00}s death={death.length:0.00}s");
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
        Shader shader = Shader.Find("BackHome/CasualToon");
        if (shader == null)
            throw new System.InvalidOperationException("BackHome/CasualToon shader was not found.");

        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        if (albedo == null)
            throw new System.InvalidOperationException("Grimling albedo texture was not found.");

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "Grimling" };
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

    static void BuildPrefab(Material material, CreatureStats stats)
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
            throw new System.InvalidOperationException("Grimling idle FBX was not found.");

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
            throw new System.InvalidOperationException("Could not load Grimling idle model.");

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            instance.name = "Grimling";
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

            float height = MeasureHeight(instance);
            if (height > 0.05f && (height < 0.8f || height > 3.2f))
            {
                float factor = TargetHeight / height;
                instance.transform.localScale = Vector3.one * factor;
                height = MeasureHeight(instance);
            }

            instance.transform.localScale *= DisplayScale;

            float barHeight = height > 0.05f ? height + 0.25f : 2.2f;
            AddCreatureComponents(instance, stats, barHeight);

            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Debug.Log($"[BackHome] Grimling prefab saved. removedIcosphere={removed} height={height:0.00}");
        }
        finally
        {
            Object.DestroyImmediate(instance);
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

    // Restores the in-game scale after an FBX rebuild. Does not touch CreatureSpawner.
    static void PatchScaleAndSpawns()
    {
        try
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                root.transform.localScale = Vector3.one * DisplayScale;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            KeepPrefabGuid();
            WriteResult($"patched scale={DisplayScale:0.##}");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            WriteResult("PATCH FAILED: " + e);
        }
    }

    static void KeepPrefabGuid()
    {
        string metaPath = PrefabPath + ".meta";
        if (!File.Exists(metaPath))
            return;

        string text = File.ReadAllText(metaPath);
        const string marker = "guid: ";
        int start = text.IndexOf(marker, System.StringComparison.Ordinal);
        if (start < 0)
            return;

        int value = start + marker.Length;
        int end = text.IndexOf('\n', value);
        if (end < 0)
            end = text.Length;

        string current = text.Substring(value, end - value).Trim();
        if (current == PrefabGuid)
            return;

        text = text.Remove(value, end - value).Insert(value, PrefabGuid);
        File.WriteAllText(metaPath, text);
        AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
    }

    static AnimationClip ExtractLoopingClip(string modelPath, string clipName, string outputPath, bool loop = true)
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
        ModelImporterClipAnimation[] takes = new[]
        {
            new ModelImporterClipAnimation
            {
                name = clipName,
                takeName = selected.name,
                firstFrame = selected.startTime * selected.sampleRate,
                lastFrame = selected.stopTime * selected.sampleRate,
                loopTime = loop,
                loopPose = false,
                wrapMode = WrapMode.Loop
            }
        };

        importer.clipAnimations = takes;
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
            bool lockHips = path.EndsWith("Hips", System.StringComparison.Ordinal)
                && (binding.propertyName == "m_LocalPosition.x" || binding.propertyName == "m_LocalPosition.z");
            if (lockHips && curve.keys.Length > 0)
            {
                float locked = curve.keys[0].value;
                curve = AnimationCurve.Constant(curve.keys[0].time, curve.keys[curve.keys.Length - 1].time, locked);
            }

            var remapped = binding;
            remapped.path = path;
            AnimationUtility.SetEditorCurve(clip, remapped, curve);
        }

        return SaveClipInPlace(clip, outputPath);
    }

    static AnimationClip SaveClipInPlace(AnimationClip clip, string outputPath)
    {
        EnsureFolder("Assets/Resources/Galaxy/Nyxara/Creatures/Grimling", "Animations");
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

    static AnimatorController CreateIdleController(AnimationClip idle1, AnimationClip idle2, AnimationClip running)
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

        AnimatorState state1 = machine.AddState("Idle1", new Vector3(280, 0, 0));
        state1.motion = idle1;
        AnimatorState state2 = machine.AddState("Idle2", new Vector3(280, 140, 0));
        state2.motion = idle2;
        AnimatorState run = machine.AddState("Run", new Vector3(540, 70, 0));
        run.motion = running;
        machine.defaultState = state1;

        const float idleCrossfade = 0.25f;
        const float moveCrossfade = 0.12f;
        AddMoveTransition(state1, run, moving: true, moveCrossfade);
        AddMoveTransition(state2, run, moving: true, moveCrossfade);
        AddMoveTransition(run, state1, moving: false, moveCrossfade);

        AnimatorStateTransition to2 = state1.AddTransition(state2);
        to2.hasExitTime = true;
        to2.exitTime = 1f;
        to2.hasFixedDuration = true;
        to2.duration = idleCrossfade;
        to2.interruptionSource = TransitionInterruptionSource.Source;
        to2.orderedInterruption = true;
        to2.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");

        AnimatorStateTransition to1 = state2.AddTransition(state1);
        to1.hasExitTime = true;
        to1.exitTime = 1f;
        to1.hasFixedDuration = true;
        to1.duration = idleCrossfade;
        to1.interruptionSource = TransitionInterruptionSource.Source;
        to1.orderedInterruption = true;
        to1.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static void AddMoveTransition(AnimatorState from, AnimatorState to, bool moving, float duration)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.exitTime = 0f;
        transition.hasFixedDuration = true;
        transition.duration = duration;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAttacking");
        transition.AddCondition(moving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "IsMoving");
    }

    static void AddAttackStates(AnimatorController controller, AnimationClip melee)
    {
        EnsureParameter(controller, "IsAttacking", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "AttackKind", AnimatorControllerParameterType.Int);
        EnsureParameter(controller, "AttackAnimSpeed", AnimatorControllerParameterType.Float);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState light = FindState(machine, "LightAttack") ?? machine.AddState("LightAttack", new Vector3(540, 210, 0));
        light.motion = melee;
        light.speedParameterActive = true;
        light.speedParameter = "AttackAnimSpeed";

        AnimatorState idle1 = FindState(machine, "Idle1");
        AnimatorState idle2 = FindState(machine, "Idle2");
        AnimatorState run = FindState(machine, "Run");

        LinkAttack(idle1, light, CreatureAttackKind.Basic);
        LinkAttack(idle2, light, CreatureAttackKind.Basic);
        LinkAttack(run, light, CreatureAttackKind.Basic);
        LinkAttackExit(light, run, moving: true);
        LinkAttackExit(light, idle1, moving: false);

        EditorUtility.SetDirty(controller);
    }

    static void AddDeathState(AnimatorController controller, AnimationClip death)
    {
        EnsureParameter(controller, "Die", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState state = FindState(machine, "Death") ?? machine.AddState("Death", new Vector3(540, 420, 0));
        state.motion = death;
        state.speed = 1f;
        state.speedParameterActive = false;

        AnimatorStateTransition transition = machine.AddAnyStateTransition(state);
        transition.hasExitTime = false;
        transition.exitTime = 0f;
        transition.hasFixedDuration = true;
        transition.duration = 0.05f;
        transition.canTransitionToSelf = false;
        transition.interruptionSource = TransitionInterruptionSource.None;
        transition.AddCondition(AnimatorConditionMode.If, 0, "Die");

        EditorUtility.SetDirty(controller);
    }

    static void LinkAttack(AnimatorState from, AnimatorState to, CreatureAttackKind kind)
    {
        if (from == null || to == null)
            return;

        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.exitTime = 0f;
        transition.hasFixedDuration = true;
        transition.duration = 0.08f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.If, 0, "IsAttacking");
        transition.AddCondition(AnimatorConditionMode.Equals, (int)kind, "AttackKind");
    }

    static void LinkAttackExit(AnimatorState from, AnimatorState to, bool moving)
    {
        if (from == null || to == null)
            return;

        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.exitTime = 0f;
        transition.hasFixedDuration = true;
        transition.duration = 0.1f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAttacking");
        transition.AddCondition(moving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "IsMoving");
    }

    static AnimatorState FindState(AnimatorStateMachine machine, string stateName)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state != null && child.state.name == stateName)
                return child.state;
        }

        return null;
    }

    static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        foreach (AnimatorControllerParameter parameter in controller.parameters)
        {
            if (parameter.name == name && parameter.type == type)
                return;
        }

        controller.AddParameter(name, type);
    }

    static void WireAnimator(AnimatorController controller, Avatar avatar)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Animator animator = root.GetComponent<Animator>();
            if (animator == null)
                animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (root.GetComponent<CreatureAnimator>() == null)
                root.AddComponent<CreatureAnimator>();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
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

        Debug.Log("[BackHome] Grimling setup: " + message);
    }
}

public class GrimlingSetupHook : AssetPostprocessor
{
    static bool _busy;

    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        if (_busy)
            return;

        _busy = true;
        try
        {
            GrimlingSetup.TryRunPendingSetup();
        }
        finally
        {
            _busy = false;
        }
    }
}
