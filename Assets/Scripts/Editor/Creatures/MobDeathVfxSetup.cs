using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Creates GrimlingDeathVFX and SnarlfangDeathVFX once, and points the mob prefabs at them
/// only while the reference is empty. Does not rebuild or overwrite a prefab that already exists,
/// so later particle edits stay put. Death clips stay on the kings.
/// </summary>
public static class MobDeathVfxSetup
{
    const string GrimlingPrefab = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Grimling.prefab";
    const string SnarlfangPrefab = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Snarlfang.prefab";
    const string GrimlingVfx = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Mob/VFX/GrimlingDeathVFX.prefab";
    const string SnarlfangVfx = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Mob/VFX/SnarlfangDeathVFX.prefab";
    const string GrimlingController = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Mob/Animations/Grimling.controller";
    const string SnarlfangController = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Mob/Animations/Snarlfang.controller";

    static readonly string TriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeWireMobDeathVfx.trigger");
    static readonly string ResultPath = Path.Combine(Application.dataPath, "..", "Temp", "MobDeathVfx.result.txt");

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.delayCall += Watch;
    }

    static void Watch()
    {
        if (!File.Exists(TriggerPath))
            return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += Watch;
            return;
        }

        try { File.Delete(TriggerPath); } catch { /* ignore */ }

        try
        {
            DeleteIfExists("Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Mob/Animations/Death.anim");
            DeleteIfExists("Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Mob/Animations/Death.anim");
            DeleteIfExists("Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/Mob/Models/Grimling_Animation_Death.fbx");
            DeleteIfExists("Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Mob/Models/Snarlfang_Animation_Death.fbx");

            StripDeath(GrimlingController);
            StripDeath(SnarlfangController);

            GameObject grimlingVfx = CreateVfxIfMissing(GrimlingVfx, "GrimlingDeathVFX");
            GameObject snarlfangVfx = CreateVfxIfMissing(SnarlfangVfx, "SnarlfangDeathVFX");
            bool grimlingAssigned = AssignIfEmpty(GrimlingPrefab, grimlingVfx);
            bool snarlfangAssigned = AssignIfEmpty(SnarlfangPrefab, snarlfangVfx);

            AssetDatabase.SaveAssets();
            File.WriteAllText(ResultPath,
                $"grimlingVfx={(grimlingVfx != null)} snarlfangVfx={(snarlfangVfx != null)} " +
                $"grimlingAssigned={grimlingAssigned} snarlfangAssigned={snarlfangAssigned}");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            try { File.WriteAllText(ResultPath, "FAILED: " + e); } catch { /* ignore */ }
        }
    }

    static void DeleteIfExists(string path)
    {
        if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            AssetDatabase.DeleteAsset(path);
    }

    static GameObject CreateVfxIfMissing(string path, string objectName)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
            return existing;

        EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));

        var go = new GameObject(objectName);
        ParticleSystem particles = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = true;
        main.duration = 1.5f;
        main.stopAction = ParticleSystemStopAction.Destroy;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    static bool AssignIfEmpty(string creaturePath, GameObject vfx)
    {
        if (vfx == null || AssetDatabase.LoadAssetAtPath<GameObject>(creaturePath) == null)
            return false;

        GameObject root = PrefabUtility.LoadPrefabContents(creaturePath);
        try
        {
            CreatureDeathVfx splash = root.GetComponent<CreatureDeathVfx>();
            if (splash == null)
                splash = root.AddComponent<CreatureDeathVfx>();

            var so = new SerializedObject(splash);
            SerializedProperty prop = so.FindProperty("deathVfxPrefab");
            if (prop == null)
                return false;
            if (prop.objectReferenceValue != null)
                return true;

            prop.objectReferenceValue = vfx;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, creaturePath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void StripDeath(string controllerPath)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null || controller.layers == null || controller.layers.Length == 0)
            return;

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        if (machine == null)
            return;

        AnimatorStateTransition[] transitions = machine.anyStateTransitions;
        for (int i = transitions.Length - 1; i >= 0; i--)
        {
            AnimatorStateTransition transition = transitions[i];
            if (transition != null && transition.destinationState != null && transition.destinationState.name == "Death")
                machine.RemoveAnyStateTransition(transition);
        }

        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state != null && child.state.name == "Death")
            {
                machine.RemoveState(child.state);
                break;
            }
        }

        for (int i = controller.parameters.Length - 1; i >= 0; i--)
        {
            if (controller.parameters[i].name == "Die")
                controller.RemoveParameter(i);
        }

        EditorUtility.SetDirty(controller);
    }

    static void EnsureFolder(string assetFolder)
    {
        if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder))
            return;

        string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
        string leaf = Path.GetFileName(assetFolder);
        EnsureFolder(parent);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(assetFolder))
            AssetDatabase.CreateFolder(parent, leaf);
    }
}
