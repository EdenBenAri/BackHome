using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Author once; subsequent imports preserve designer changes to the prefab.</summary>
public static class KingObjectiveMarkerAuthoring
{
    public const string Folder = "Assets/Resources/Tutorial";
    public const string PrefabPath = Folder + "/KingObjectiveMarker.prefab";

    [InitializeOnLoadMethod]
    static void Schedule() => EditorApplication.delayCall += EnsureAsset;

    [MenuItem("BackHome/Tutorial/Open King Objective Marker")]
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
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/ExclamationMark.fbx");
        if (model == null) return;
        var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/ExclamationMark.mat");
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "ExclamationMark" };
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/ExclamationMark_texture.png"));
            material.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(material, Folder + "/ExclamationMark.mat");
        }
        var root = new GameObject("KingObjectiveMarker");
        try
        {
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, visual.transform);
            instance.name = "ExclamationMark";
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("ExclamationMark model has no renderer.");
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
            // Unit-height geometry with the bottom of the dot at the marker's origin.
            float scale = 1f / Mathf.Max(.001f, bounds.size.y);
            instance.transform.localScale *= scale;
            instance.transform.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
            var marker = root.AddComponent<KingObjectiveMarker>();
            var so = new SerializedObject(marker);
            so.FindProperty("visual").objectReferenceValue = visual.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            File.WriteAllText("Logs/KingObjectiveMarkerAsset.txt", $"Source bounds: {bounds}\nNormalized height: 1\nRenderers: {renderers.Length}\n");
        }
        finally { Object.DestroyImmediate(root); }
    }
}
