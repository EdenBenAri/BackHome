using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Hand-authored hop and attack for Slime. While moving, the body squashes flat, pops into a
/// round ball in the air, then splats on landing. The attack coils, then launches the whole
/// body forward in about 0.3s and fires a crescent wave ahead of the leap.
/// The king also gets a one-shot Spawn: a puddle on the floor that stretches up into the rest pose.
/// </summary>
public static class SlimeHopSetup
{
    const string PrefabPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Slime.prefab";
    const string IdleClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Mob/Animations/Idle.anim";
    const string JumpClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Mob/Animations/Jump.anim";
    const string ControllerPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Mob/Animations/Slime.controller";

    const string KingPrefabPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/King/King_Slime.prefab";
    const string LegacyKingPrefabPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/King_Slime.prefab";
    const string KingIdleClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/King/Animations/Idle.anim";
    const string KingJumpClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/King/Animations/Jump.anim";
    const string KingControllerPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/King/Animations/King_Slime.controller";
    const string KingSpawnClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/King/Animations/Spawn.anim";

    // Opening puddle. CreatureAnimator applies the same scale before the Animator wakes up.
    const float SpawnFlatY = 0.045f;
    const float SpawnFlatXZ = 2.3f;
    const float SpawnLength = 0.64f;

    const string SlashShaderPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Shaders/SlimeSlash.shader";
    const string SlashMaterialPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Mob/Materials/SlimeSlash.mat";
    const string SlashMeshPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Mob/Models/SlashCrescent.asset";

    const float AttackClipLength = 0.3f;

    const string DeathClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Mob/Animations/Death.anim";

    static readonly string ResultPath = Path.Combine(Application.dataPath, "..", "Temp", "SlimeHopSetup.result.txt");
    static readonly string TriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeSetupSlimeHop.trigger");
    static bool _kingSpawnEnsured;

    struct BodyFit
    {
        public float CenterY;
        public float Radius;
        public float AttackRange;
    }

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.update += WatchTrigger;
        EditorApplication.delayCall += WatchTrigger;
        EditorApplication.delayCall += EnsureKingSpawn;
        EditorApplication.delayCall += StripDeathSmear;
    }

    /// <summary>
    /// The floor-smear Death clip used to be rebuilt whenever this setup ran.
    /// Delete that clip and pull it off the mob controller if it shows up again.
    /// Particles stay on <see cref="SlimeDeathSplash"/>.
    /// </summary>
    static void StripDeathSmear()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += StripDeathSmear;
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(DeathClipPath) != null)
            AssetDatabase.DeleteAsset(DeathClipPath);

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            return;

        bool changed = false;
        for (int i = controller.parameters.Length - 1; i >= 0; i--)
        {
            if (controller.parameters[i].name != "Die")
                continue;

            controller.RemoveParameter(i);
            changed = true;
        }

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorStateTransition[] any = machine.anyStateTransitions;
        for (int i = any.Length - 1; i >= 0; i--)
        {
            AnimatorStateTransition transition = any[i];
            if (transition == null)
                continue;

            bool toDeath = transition.destinationState != null && transition.destinationState.name == "Death";
            if (!toDeath)
            {
                foreach (AnimatorCondition condition in transition.conditions)
                {
                    if (condition.parameter == "Die")
                    {
                        toDeath = true;
                        break;
                    }
                }
            }

            if (!toDeath)
                continue;

            machine.RemoveAnyStateTransition(transition);
            changed = true;
        }

        ChildAnimatorState[] states = machine.states;
        for (int i = states.Length - 1; i >= 0; i--)
        {
            if (states[i].state == null || states[i].state.name != "Death")
                continue;

            machine.RemoveState(states[i].state);
            changed = true;
        }

        if (!changed)
            return;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    static void EnsureKingSpawn()
    {
        if (_kingSpawnEnsured)
            return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += EnsureKingSpawn;
            return;
        }

        _kingSpawnEnsured = true;
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(KingSpawnClipPath) != null
            && ControllerHasState(KingControllerPath, "Spawn"))
            return;

        ApplyTo(KingPrefabPath, KingIdleClipPath, KingJumpClipPath, KingControllerPath, 2f);
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

    [MenuItem("BackHome/Creatures/Setup Slime Hop")]
    public static void SetupFromMenu()
    {
        try
        {
            Apply();
            WriteResult("ok");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            WriteResult("FAILED: " + e);
        }
    }

    public static void Apply()
    {
        ApplyTo(PrefabPath, IdleClipPath, JumpClipPath, ControllerPath, 1f);
        ApplyTo(KingPrefabPath, KingIdleClipPath, KingJumpClipPath, KingControllerPath, 2f);
        StripDeathSmear();
    }

    /// <summary>
    /// Same squash-and-pop hop, aimed at another slime prefab. <paramref name="hopHeight"/>
    /// scales the jump lift (1 = the regular slime). Also builds the crescent attack.
    /// </summary>
    public static void ApplyTo(string prefabPath, string idleClipPath, string jumpClipPath, string controllerPath, float hopHeight)
    {
        string folder = Path.GetDirectoryName(idleClipPath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
        {
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string child = Path.GetFileName(folder);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(child))
                CreatureAssetUtil.EnsureFolder(parent, child);
        }

        BodyFit fit = Measure(prefabPath, hopHeight);
        float attackCycle = AttackCycle(prefabPath);
        string attackClipPath = folder + "/LightAttack.anim";

        AnimationClip idle = CreateIdleClip();
        AnimationClip jump = CreateJumpClip(Mathf.Max(0.01f, hopHeight));
        StampHiddenSlash(idle, fit);
        StampHiddenSlash(jump, fit);
        idle = SaveClip(idle, idleClipPath);
        jump = SaveClip(jump, jumpClipPath);
        AnimationClip attack = SaveClip(CreateAttackClip(fit), attackClipPath);
        AnimationClip spawn = null;
        if (prefabPath == KingPrefabPath)
            spawn = SaveClip(CreateSpawnClip(fit), KingSpawnClipPath);
        Mesh slashMesh = GetSlashMesh();
        Material slashMaterial = GetSlashMaterial();
        AnimatorController controller = CreateController(idle, jump, attack, spawn, controllerPath, attackCycle);
        WirePrefab(controller, prefabPath, slashMesh, slashMaterial, fit);

        if (prefabPath == KingPrefabPath && AssetDatabase.LoadAssetAtPath<GameObject>(LegacyKingPrefabPath) != null)
            WirePrefab(controller, LegacyKingPrefabPath, slashMesh, slashMaterial, fit);

        AssetDatabase.SaveAssets();
        Debug.Log($"[BackHome] Slime attack on {prefabPath} radius={fit.Radius:0.00} range={fit.AttackRange:0.00} cycle={attackCycle:0.00}");
    }

    static float AttackCycle(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Creature creature = prefab != null ? prefab.GetComponent<Creature>() : null;
        float speed = creature != null ? creature.AttackSpeed : 0f;
        if (speed < 0.05f)
            return AttackClipLength;

        return 1f / speed;
    }

    static BodyFit Measure(string prefabPath, float hopHeight)
    {
        var fit = new BodyFit
        {
            CenterY = 1.4f * hopHeight,
            Radius = 1.6f * hopHeight,
            AttackRange = 2f * hopHeight
        };

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Creature creature = prefab != null ? prefab.GetComponent<Creature>() : null;
        if (creature != null && creature.AttackRange > 0.05f)
            fit.AttackRange = creature.AttackRange;

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            bool any = false;
            Bounds bounds = new Bounds(root.transform.position, Vector3.zero);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.transform.name == "Slash")
                    continue;
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
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

            if (!any)
                return fit;

            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            if (radius < 0.2f || radius > 12f)
                return fit;

            fit.CenterY = bounds.center.y;
            fit.Radius = radius;
            return fit;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static AnimationClip CreateIdleClip()
    {
        var clip = new AnimationClip { name = "Idle", frameRate = 30 };
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        // Slow settle and rise, no hop. Keeps the blob alive while it stands.
        float[] times = { 0f, 0.8f, 1.6f };
        float[] posY = { 0f, 0f, 0f };
        float[] scaleY = { 1f, 0.96f, 1f };
        float[] scaleXZ = { 1f, 1.035f, 1f };
        WriteHopCurves(clip, times, posY, scaleY, scaleXZ);
        return clip;
    }

    static AnimationClip CreateJumpClip(float hopHeight)
    {
        var clip = new AnimationClip { name = "Jump", frameRate = 60 };
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        // Flat dome on the ground, round ball in the air, splat on the landing.
        float[] times = { 0f, 0.06f, 0.14f, 0.26f, 0.36f, 0.44f, 0.54f };
        float[] posY = { 0f, 0f, 0.35f * hopHeight, 2.05f * hopHeight, 0.7f * hopHeight, 0f, 0f };
        float[] scaleY = { 0.48f, 0.4f, 1.12f, 1.04f, 1.08f, 0.36f, 0.48f };
        float[] scaleXZ = { 1.45f, 1.58f, 0.94f, 0.98f, 0.96f, 1.66f, 1.45f };
        WriteHopCurves(clip, times, posY, scaleY, scaleXZ);
        return clip;
    }

    static AnimationClip CreateSpawnClip(BodyFit fit)
    {
        var clip = new AnimationClip { name = "Spawn", frameRate = 60 };
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        // Smeared flat on the floor, then a fast stretch up into the model's rest shape.
        // Hop's pivot is at the feet, so scale Y lifts the body off the puddle.
        float[] times = { 0f, 0.12f, 0.2f, 0.3f, 0.4f, 0.52f, SpawnLength };
        float[] posY = Zeros(times.Length);
        float[] posZ = Zeros(times.Length);
        float[] scaleY = { SpawnFlatY, 0.03f, 0.85f, 1.18f, 0.97f, 1f, 1f };
        float[] scaleXZ = { SpawnFlatXZ, 2.48f, 1.7f, 0.9f, 1.05f, 1f, 1f };
        WriteAttackBody(clip, times, posY, posZ, scaleXZ, scaleY, scaleXZ);
        StampHiddenSlash(clip, fit);
        return clip;
    }

    static AnimationClip CreateAttackClip(BodyFit fit)
    {
        var clip = new AnimationClip { name = "LightAttack", frameRate = 60 };
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        float pull = fit.Radius * 0.28f;
        float lunge = Mathf.Max(fit.Radius * 1.35f, fit.AttackRange * 0.82f);
        float hop = fit.Radius * 0.62f;
        float startZ = fit.Radius * 0.12f;
        float travel = Mathf.Max(lunge + fit.Radius * 0.55f, fit.AttackRange * 1.05f);
        float peak = fit.Radius * 0.9f;

        // Coil, then shoot the whole body forward and spring home. Plays in 0.3s at speed 1.
        float[] times = { 0f, 0.04f, 0.08f, 0.14f, 0.20f, 0.26f, AttackClipLength };
        float[] posY = { 0f, 0f, 0f, hop * 0.85f, hop, hop * 0.22f, 0f };
        float[] posZ = { 0f, -pull * 0.55f, -pull, lunge * 0.72f, lunge, lunge * 0.18f, 0f };
        float[] scaleX = { 1f, 1.18f, 1.34f, 0.86f, 0.78f, 1.22f, 1f };
        float[] scaleY = { 1f, 0.78f, 0.58f, 1.22f, 1.08f, 0.72f, 1f };
        float[] scaleZ = { 1f, 0.96f, 0.88f, 1.55f, 1.72f, 1.12f, 1f };
        WriteAttackBody(clip, times, posY, posZ, scaleX, scaleY, scaleZ);

        float[] slashTimes = { 0f, 0.07f, 0.11f, 0.16f, 0.23f, AttackClipLength };
        float[] slashX = Zeros(slashTimes.Length);
        float[] slashY = Filled(slashTimes.Length, fit.CenterY);
        float[] slashZ =
        {
            startZ,
            startZ,
            startZ + travel * 0.18f,
            startZ + travel * 0.62f,
            startZ + travel * 0.92f,
            startZ + travel
        };
        float[] slashScale =
        {
            0f,
            0f,
            peak * 0.8f,
            peak,
            peak * 0.55f,
            0f
        };
        WriteSlash(clip, slashTimes, slashX, slashY, slashZ, slashScale);
        return clip;
    }

    static void WriteHopCurves(AnimationClip clip, float[] times, float[] posY, float[] scaleY, float[] scaleXZ)
    {
        SetCurve(clip, "Hop", "m_LocalPosition.x", times, Zeros(times.Length), linear: false);
        SetCurve(clip, "Hop", "m_LocalPosition.y", times, posY, linear: false);
        SetCurve(clip, "Hop", "m_LocalPosition.z", times, Zeros(times.Length), linear: false);
        SetCurve(clip, "Hop", "m_LocalScale.x", times, scaleXZ, linear: false);
        SetCurve(clip, "Hop", "m_LocalScale.y", times, scaleY, linear: false);
        SetCurve(clip, "Hop", "m_LocalScale.z", times, scaleXZ, linear: false);
    }

    static void WriteAttackBody(AnimationClip clip, float[] times, float[] posY, float[] posZ, float[] scaleX, float[] scaleY, float[] scaleZ)
    {
        SetCurve(clip, "Hop", "m_LocalPosition.x", times, Zeros(times.Length), linear: true);
        SetCurve(clip, "Hop", "m_LocalPosition.y", times, posY, linear: true);
        SetCurve(clip, "Hop", "m_LocalPosition.z", times, posZ, linear: true);
        SetCurve(clip, "Hop", "m_LocalScale.x", times, scaleX, linear: true);
        SetCurve(clip, "Hop", "m_LocalScale.y", times, scaleY, linear: true);
        SetCurve(clip, "Hop", "m_LocalScale.z", times, scaleZ, linear: true);
    }

    static void StampHiddenSlash(AnimationClip clip, BodyFit fit)
    {
        float end = Mathf.Max(clip.length, 0.05f);
        float[] times = { 0f, end };
        float startZ = fit.Radius * 0.15f;
        WriteSlash(clip, times, Zeros(2), Filled(2, fit.CenterY), Filled(2, startZ), Zeros(2));
    }

    static void WriteSlash(AnimationClip clip, float[] times, float[] posX, float[] posY, float[] posZ, float[] scale)
    {
        SetCurve(clip, "Slash", "m_LocalPosition.x", times, posX, linear: true);
        SetCurve(clip, "Slash", "m_LocalPosition.y", times, posY, linear: true);
        SetCurve(clip, "Slash", "m_LocalPosition.z", times, posZ, linear: true);
        SetCurve(clip, "Slash", "m_LocalScale.x", times, scale, linear: true);
        SetCurve(clip, "Slash", "m_LocalScale.y", times, scale, linear: true);
        SetCurve(clip, "Slash", "m_LocalScale.z", times, scale, linear: true);
    }

    static float[] Zeros(int count)
    {
        return new float[count];
    }

    static float[] Filled(int count, float value)
    {
        var values = new float[count];
        for (int i = 0; i < count; i++)
            values[i] = value;
        return values;
    }

    static void SetCurve(AnimationClip clip, string path, string property, float[] times, float[] values, bool linear)
    {
        var curve = new AnimationCurve();
        for (int i = 0; i < times.Length; i++)
            curve.AddKey(new Keyframe(times[i], values[i]));

        for (int i = 0; i < curve.length; i++)
        {
            float inSlope = 0f;
            float outSlope = 0f;
            if (linear && i > 0)
                inSlope = (values[i] - values[i - 1]) / Mathf.Max(0.0001f, times[i] - times[i - 1]);
            if (linear && i + 1 < times.Length)
                outSlope = (values[i + 1] - values[i]) / Mathf.Max(0.0001f, times[i + 1] - times[i]);

            var mode = linear ? AnimationUtility.TangentMode.Free : AnimationUtility.TangentMode.ClampedAuto;
            AnimationUtility.SetKeyBroken(curve, i, linear);
            AnimationUtility.SetKeyLeftTangentMode(curve, i, mode);
            AnimationUtility.SetKeyRightTangentMode(curve, i, mode);

            if (!linear)
                continue;

            Keyframe key = curve[i];
            key.inTangent = inSlope;
            key.outTangent = outSlope;
            curve.MoveKey(i, key);
        }

        AnimationUtility.SetEditorCurve(
            clip,
            EditorCurveBinding.FloatCurve(path, typeof(Transform), property),
            curve);
    }

    static AnimationClip SaveClip(AnimationClip clip, string path)
    {
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        string name = existing.name;
        EditorUtility.CopySerialized(clip, existing);
        existing.name = name;
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(clip);
        return existing;
    }

    static AnimatorController CreateController(AnimationClip idle, AnimationClip jump, AnimationClip attack, AnimationClip spawn, string controllerPath, float attackCycle)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

        while (controller.parameters.Length > 0)
            controller.RemoveParameter(0);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        while (machine.states.Length > 0)
            machine.RemoveState(machine.states[0].state);
        while (machine.anyStateTransitions.Length > 0)
            machine.RemoveAnyStateTransition(machine.anyStateTransitions[0]);

        controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("IsAttacking", AnimatorControllerParameterType.Bool);
        controller.AddParameter("AttackKind", AnimatorControllerParameterType.Int);

        AnimatorState idleState = machine.AddState("Idle1", new Vector3(280, 0, 0));
        idleState.motion = idle;
        AnimatorState runState = machine.AddState("Run", new Vector3(540, 70, 0));
        runState.motion = jump;
        AnimatorState attackState = machine.AddState("LightAttack", new Vector3(540, 210, 0));
        attackState.motion = attack;
        attackState.speedParameterActive = false;
        attackState.speed = 1f;

        if (spawn != null)
        {
            AnimatorState spawnState = machine.AddState("Spawn", new Vector3(280, -110, 0));
            spawnState.motion = spawn;
            machine.defaultState = spawnState;
        }
        else
        {
            machine.defaultState = idleState;
        }

        AnimatorStateTransition toRun = idleState.AddTransition(runState);
        toRun.hasExitTime = false;
        toRun.exitTime = 0f;
        toRun.hasFixedDuration = true;
        toRun.duration = 0.08f;
        toRun.canTransitionToSelf = false;
        toRun.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAttacking");
        toRun.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");

        AnimatorStateTransition toIdle = runState.AddTransition(idleState);
        toIdle.hasExitTime = false;
        toIdle.exitTime = 0f;
        toIdle.hasFixedDuration = true;
        toIdle.duration = 0.1f;
        toIdle.canTransitionToSelf = false;
        toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAttacking");
        toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");

        LinkAttack(idleState, attackState);
        LinkAttack(runState, attackState);
        LinkAttackExit(attackState, runState, moving: true);
        LinkAttackExit(attackState, idleState, moving: false);

        EditorUtility.SetDirty(controller);
        return controller;
    }

    static void LinkAttack(AnimatorState from, AnimatorState to)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.exitTime = 0f;
        transition.hasFixedDuration = true;
        transition.duration = 0.06f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.If, 0, "IsAttacking");
        transition.AddCondition(AnimatorConditionMode.Equals, (int)CreatureAttackKind.Basic, "AttackKind");
    }

    static void LinkAttackExit(AnimatorState from, AnimatorState to, bool moving)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.exitTime = 0f;
        transition.hasFixedDuration = true;
        transition.duration = 0.08f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAttacking");
        transition.AddCondition(moving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "IsMoving");
    }

    static Mesh GetSlashMesh()
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(SlashMeshPath);
        bool created = mesh == null;
        if (created)
            mesh = new Mesh { name = "SlashCrescent" };
        else
            mesh.Clear();

        const int segments = 22;
        const int rings = 3;
        var verts = new List<Vector3>((segments + 1) * rings);
        var colors = new List<Color>((segments + 1) * rings);
        float[] radii = { 0.48f, 0.78f, 1.08f };
        float[] edge = { 0f, 1f, 0f };

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float ang = Mathf.Lerp(-60f, 60f, t) * Mathf.Deg2Rad;
            float tip = Mathf.Sin(t * Mathf.PI);
            float front = Mathf.Cos(ang);
            float yLift = (front - 0.42f) * 0.7f;
            float x = Mathf.Sin(ang);
            float z = front;

            for (int r = 0; r < rings; r++)
            {
                verts.Add(new Vector3(x * radii[r], yLift, z * radii[r]));
                var color = Color.Lerp(new Color(1f, 0.62f, 0.18f, 0f), new Color(1f, 0.98f, 0.9f, 1f), edge[r]);
                color.a *= tip;
                colors.Add(color);
            }
        }

        var tris = new List<int>(segments * (rings - 1) * 6);
        for (int i = 0; i < segments; i++)
        {
            for (int r = 0; r < rings - 1; r++)
            {
                int a = i * rings + r;
                int b = a + rings;
                tris.Add(a);
                tris.Add(a + 1);
                tris.Add(b);
                tris.Add(b);
                tris.Add(a + 1);
                tris.Add(b + 1);
            }
        }

        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (created)
            AssetDatabase.CreateAsset(mesh, SlashMeshPath);
        else
            EditorUtility.SetDirty(mesh);

        return mesh;
    }

    static Material GetSlashMaterial()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(SlashShaderPath);
        if (shader == null)
        {
            AssetDatabase.ImportAsset(SlashShaderPath, ImportAssetOptions.ForceUpdate);
            shader = AssetDatabase.LoadAssetAtPath<Shader>(SlashShaderPath);
        }

        if (shader == null)
            throw new System.InvalidOperationException("Slime slash shader was not found at " + SlashShaderPath);

        Material material = AssetDatabase.LoadAssetAtPath<Material>(SlashMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "SlimeSlash" };
            AssetDatabase.CreateAsset(material, SlashMaterialPath);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", new Color(3.4f, 2.7f, 1.55f, 1f));

        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    static void WirePrefab(AnimatorController controller, string prefabPath, Mesh slashMesh, Material slashMaterial, BodyFit fit)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Transform body = FindNamed(root.transform, "Body");
            if (body == null)
                throw new System.InvalidOperationException(prefabPath + " has no Body.");

            Transform hop = body.parent != null && body.parent.name == "Hop"
                ? body.parent
                : CreateHop(root.transform, body);

            hop.localPosition = Vector3.zero;
            hop.localRotation = Quaternion.identity;
            hop.localScale = Vector3.one;

            Transform slash = FindNamed(root.transform, "Slash");
            if (slash == null)
            {
                var slashObject = new GameObject("Slash");
                slash = slashObject.transform;
                slash.SetParent(root.transform, false);
            }
            else if (slash.parent != root.transform)
            {
                slash.SetParent(root.transform, false);
            }

            float startZ = fit.Radius * 0.15f;
            slash.localPosition = new Vector3(0f, fit.CenterY, startZ);
            slash.localRotation = Quaternion.identity;
            slash.localScale = Vector3.zero;

            MeshFilter filter = slash.GetComponent<MeshFilter>();
            if (filter == null)
                filter = slash.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = slashMesh;

            MeshRenderer renderer = slash.GetComponent<MeshRenderer>();
            if (renderer == null)
                renderer = slash.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = slashMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            Animator animator = root.GetComponent<Animator>();
            if (animator == null)
                animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.avatar = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (root.GetComponent<CreatureAnimator>() == null)
                root.AddComponent<CreatureAnimator>();

            if (prefabPath == PrefabPath || prefabPath == KingPrefabPath || prefabPath == LegacyKingPrefabPath)
                EnsureDeathSplash(root, fit);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void EnsureDeathSplash(GameObject root, BodyFit fit)
    {
        SlimeDeathSplash splash = root.GetComponent<SlimeDeathSplash>();
        if (splash == null)
            splash = root.AddComponent<SlimeDeathSplash>();

        SerializedObject so = new SerializedObject(splash);
        so.FindProperty("bodyRadius").floatValue = fit.Radius;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static Transform CreateHop(Transform root, Transform body)
    {
        var hopObject = new GameObject("Hop");
        Transform hop = hopObject.transform;
        hop.SetParent(root, false);
        body.SetParent(hop, false);
        return hop;
    }

    static Transform FindNamed(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName)
                return child;
        }

        return null;
    }

    static bool ControllerHasState(string controllerPath, string stateName)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null || controller.layers == null || controller.layers.Length == 0)
            return false;

        foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
        {
            if (child.state != null && child.state.name == stateName)
                return true;
        }

        return false;
    }

    static void WriteResult(string message)
    {
        CreatureAssetUtil.WriteResult(ResultPath, "Slime hop", message);
    }
}
