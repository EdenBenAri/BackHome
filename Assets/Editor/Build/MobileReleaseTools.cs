using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>Repeatable asset preparation for the mobile Web release.</summary>
[InitializeOnLoad]
public static class MobileReleaseTools
{
    const string Request = "Temp/prepare-mobile-release.flag";
    static MobileReleaseTools() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try
        {
            PrepareDamageText();
            File.WriteAllText("Logs/mobile-release-prepare.txt", "success");
        }
        catch (Exception e) { File.WriteAllText("Logs/mobile-release-prepare.txt", e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Tools/BackHome/Prepare mobile damage text")]
    public static void PrepareDamageText()
    {
        var mobileRenderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/Mobile_Renderer.asset");
        if (mobileRenderer != null)
        {
            // Both gameplay cameras have post-processing off. Don't construct
            // disabled passes whose resources are correctly stripped on Web.
            mobileRenderer.rendererFeatures.RemoveAll(feature => feature != null
                && !feature.isActive && feature.GetType().Name == "ScreenSpaceAmbientOcclusion");
            var rendererData = new SerializedObject(mobileRenderer);
            var featureMap = rendererData.FindProperty("m_RendererFeatureMap");
            featureMap.arraySize = mobileRenderer.rendererFeatures.Count;
            for (int i = 0; i < mobileRenderer.rendererFeatures.Count; i++)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mobileRenderer.rendererFeatures[i], out string _, out long id);
                featureMap.GetArrayElementAtIndex(i).longValue = id;
            }
            rendererData.ApplyModifiedPropertiesWithoutUndo();
            mobileRenderer.postProcessData = null;
            mobileRenderer.SetDirty();
            EditorUtility.SetDirty(mobileRenderer);
        }
        // SphericalPlanet creates this material by Shader.Find at runtime. A
        // Resources reference keeps its shader in stripped release players.
        const string surfacePath = "Assets/Resources/Galaxy/PlanetSurfaceUnlit.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(surfacePath) == null)
        {
            Shader surfaceShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (surfaceShader == null) throw new InvalidOperationException("Planet surface shader missing.");
            AssetDatabase.CreateAsset(new Material(surfaceShader) { name = "PlanetSurfaceUnlit" }, surfacePath);
        }
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/AssetStore/Layer Lab/GUI Pro-CasualGame/ResourcesData/Fonts/LilitaOne-Regular SDF.asset");
        if (font == null || font.material == null) throw new InvalidOperationException("Layer Lab damage font missing.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/HUD/DamageNumber.prefab");
        var popup = prefab != null ? prefab.GetComponent<DamagePopup>() : null;
        var label = prefab != null ? prefab.GetComponentInChildren<TextMeshPro>(true) : null;
        if (popup == null || label == null) throw new InvalidOperationException("DamageNumber prefab missing TextMeshPro.");
        var labelData = new SerializedObject(label);
        labelData.FindProperty("m_fontAsset").objectReferenceValue = font;
        labelData.FindProperty("m_sharedMaterial").objectReferenceValue = font.material;
        labelData.ApplyModifiedPropertiesWithoutUndo();
        var textRenderer = label.GetComponent<MeshRenderer>();
        if (textRenderer != null)
        {
            textRenderer.sharedMaterial = font.material;
            EditorUtility.SetDirty(textRenderer);
        }
        EditorUtility.SetDirty(label);
        PrefabUtility.SavePrefabAsset(prefab);
        AssetDatabase.SaveAssets();
    }
}
