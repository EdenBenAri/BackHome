using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared asset helpers for the creature setup menus. Each creature still owns its paths and prefab.
/// </summary>
static class CreatureAssetUtil
{
    public static void EnsurePbrMaps(string normalPath, string metallicPath, string roughnessPath)
    {
        EnsureNormalMap(normalPath);
        EnsureLinearData(metallicPath);
        EnsureLinearData(roughnessPath);
    }

    public static void EnsureNormalMap(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null || importer.textureType == TextureImporterType.NormalMap)
            return;

        importer.textureType = TextureImporterType.NormalMap;
        importer.SaveAndReimport();
    }

    public static void EnsureLinearData(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null || !importer.sRGBTexture)
            return;

        importer.sRGBTexture = false;
        importer.SaveAndReimport();
    }

    public static void EnsureFolder(string parent, string child)
    {
        string folder = parent + "/" + child;
        if (AssetDatabase.IsValidFolder(folder))
            return;

        AssetDatabase.CreateFolder(parent, child);
    }

    public static void WriteKingLoot(SerializedObject stats)
    {
        ItemDefinition coin = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
            "Assets/Resources/Items/Coin/Coin.asset");
        ItemDefinition gem = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
            "Assets/Resources/Items/BlueGem/BlueGem.asset");

        SerializedProperty loot = stats.FindProperty("loot");
        loot.arraySize = 2;

        SerializedProperty coins = loot.GetArrayElementAtIndex(0);
        coins.FindPropertyRelative("item").objectReferenceValue = coin;
        coins.FindPropertyRelative("minAmount").intValue = 5;
        coins.FindPropertyRelative("maxAmount").intValue = 5;
        coins.FindPropertyRelative("chance").floatValue = 1f;

        SerializedProperty gemDrop = loot.GetArrayElementAtIndex(1);
        gemDrop.FindPropertyRelative("item").objectReferenceValue = gem;
        gemDrop.FindPropertyRelative("minAmount").intValue = 1;
        gemDrop.FindPropertyRelative("maxAmount").intValue = 1;
        gemDrop.FindPropertyRelative("chance").floatValue = 1f;
    }

    public static void WriteResult(string resultPath, string label, string message)
    {
        try
        {
            string directory = Path.GetDirectoryName(resultPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(resultPath, message);
        }
        catch
        {
            // The menu still finishes if Temp cannot be written.
        }

        Debug.Log("[BackHome] " + label + ": " + message);
    }
}
