using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Hand-authored hop for Slime. While moving, the body squashes flat, pops into a
/// round ball in the air, then splats on landing. Matches the slime hop reference.
/// </summary>
public static class SlimeHopSetup
{
    const string PrefabPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Slime.prefab";
    const string IdleClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Animations/Idle.anim";
    const string JumpClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Animations/Jump.anim";
    const string ControllerPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Slime/Animations/Slime.controller";

    static readonly string ResultPath = Path.Combine(Application.dataPath, "..", "Temp", "SlimeHopSetup.result.txt");
    static readonly string TriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeSetupSlimeHop.trigger");

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
    }

    /// <summary>
    /// Same squash-and-pop hop, aimed at another slime prefab. <paramref name="hopHeight"/>
    /// scales the jump lift (1 = the regular slime).
    /// </summary>
    public static void ApplyTo(string prefabPath, string idleClipPath, string jumpClipPath, string controllerPath, float hopHeight)
    {
        string folder = Path.GetDirectoryName(idleClipPath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
        {
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string child = Path.GetFileName(folder);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(child))
                EnsureFolder(parent, child);
        }

        AnimationClip idle = SaveClip(CreateIdleClip(), idleClipPath);
        AnimationClip jump = SaveClip(CreateJumpClip(Mathf.Max(0.01f, hopHeight)), jumpClipPath);
        AnimatorController controller = CreateController(idle, jump, controllerPath);
        WirePrefab(controller, prefabPath);
        AssetDatabase.SaveAssets();
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

    static void WriteHopCurves(AnimationClip clip, float[] times, float[] posY, float[] scaleY, float[] scaleXZ)
    {
        SetCurve(clip, "m_LocalPosition.x", times, Zeros(times.Length));
        SetCurve(clip, "m_LocalPosition.y", times, posY);
        SetCurve(clip, "m_LocalPosition.z", times, Zeros(times.Length));
        SetCurve(clip, "m_LocalScale.x", times, scaleXZ);
        SetCurve(clip, "m_LocalScale.y", times, scaleY);
        SetCurve(clip, "m_LocalScale.z", times, scaleXZ);
    }

    static float[] Zeros(int count)
    {
        return new float[count];
    }

    static void SetCurve(AnimationClip clip, string property, float[] times, float[] values)
    {
        var curve = new AnimationCurve();
        for (int i = 0; i < times.Length; i++)
            curve.AddKey(new Keyframe(times[i], values[i]));

        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
        }

        AnimationUtility.SetEditorCurve(
            clip,
            EditorCurveBinding.FloatCurve("Hop", typeof(Transform), property),
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

    static AnimatorController CreateController(AnimationClip idle, AnimationClip jump, string controllerPath)
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

        AnimatorState idleState = machine.AddState("Idle1", new Vector3(280, 0, 0));
        idleState.motion = idle;
        AnimatorState runState = machine.AddState("Run", new Vector3(540, 70, 0));
        runState.motion = jump;
        machine.defaultState = idleState;

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

        EditorUtility.SetDirty(controller);
        return controller;
    }

    static void WirePrefab(AnimatorController controller, string prefabPath)
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

            Animator animator = root.GetComponent<Animator>();
            if (animator == null)
                animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.avatar = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (root.GetComponent<CreatureAnimator>() == null)
                root.AddComponent<CreatureAnimator>();

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
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

        Debug.Log("[BackHome] Slime hop: " + message);
    }
}
