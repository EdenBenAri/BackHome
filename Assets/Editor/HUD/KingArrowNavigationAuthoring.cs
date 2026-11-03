using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Migrates the screen arrow to ArrowMark once; subsequent designer edits are preserved.</summary>
public static class KingArrowNavigationAuthoring
{
    public const string PrefabPath = "Assets/Resources/HUD/KingArrowNavigation.prefab";
    public const string ModelPath = "Assets/Resources/Tutorial/Models/ArrowMark.fbx";
    public const string MaterialPath = "Assets/Resources/Tutorial/YellowMark.mat";

    [InitializeOnLoadMethod]
    static void Schedule() => EditorApplication.delayCall += EnsureAsset;

    [MenuItem("BackHome/HUD/Open King Arrow Navigation")]
    public static void OpenPrefab()
    {
        EnsureAsset();
        AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
    }

    public static void EnsureAsset()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += EnsureAsset;
            return;
        }
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null && existing.GetComponent<Canvas>() == null
            && existing.GetComponentInChildren<MeshRenderer>(true) != null) return;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (model == null || material == null) return;
        var root = new GameObject("KingArrowNavigation");
        try
        {
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, visual.transform);
            instance.name = "ArrowMark";
            // The imported mark faces +X in the XY plane. Lay it flat in XZ,
            // with its tip along local +Z and its thickness along local Y.
            instance.transform.localRotation = Quaternion.Euler(90f, -90f, 0f) * instance.transform.localRotation;
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("ArrowMark has no renderer.");
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
            // Normalize to a unit-long mesh whose underside rests at the origin.
            float scale = 1f / Mathf.Max(.001f, bounds.size.z);
            instance.transform.localScale *= scale;
            instance.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
            var component = root.AddComponent<KingArrowNavigation>();
            var serialized = new SerializedObject(component);
            serialized.FindProperty("visual").objectReferenceValue = visual.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/KingArrowNavigationAsset.txt", $"Source: {ModelPath}\nFlat bounds: {bounds}\nNormalized length: 1\n");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
