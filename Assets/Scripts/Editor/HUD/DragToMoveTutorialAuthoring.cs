using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Creates the editable tutorial once and nests it in the existing HUD.</summary>
public static class DragToMoveTutorialAuthoring
{
    public const string Folder = "Assets/Resources/Tutorial";
    public const string PrefabPath = Folder + "/DragToMoveTutorial.prefab";

    public static bool EnsureOnHud(GameObject hud)
    {
        if (hud.GetComponentInChildren<DragToMoveTutorial>(true) != null) return false;
        Canvas canvas = hud.GetComponentInChildren<Canvas>(true);
        if (canvas == null) return false;
        EnsureAsset();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
        instance.name = "DragToMoveTutorial";
        // Modal inventory/death UI remains above the non-interactive hint.
        Transform inventory = canvas.transform.Find("InventoryPanel");
        if (inventory != null) instance.transform.SetSiblingIndex(inventory.GetSiblingIndex());
        return true;
    }

    [MenuItem("BackHome/HUD/Open Drag To Move Tutorial")]
    public static void OpenPrefab()
    {
        EnsureAsset();
        AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
    }

    public static void EnsureAsset()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
        Directory.CreateDirectory(Folder);
        string arrowPath = Folder + "/Drage_to_move.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(arrowPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();

        var root = new GameObject("DragToMoveTutorial", typeof(RectTransform), typeof(CanvasGroup));
        root.layer = 5;
        try
        {
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .32f);
            rect.sizeDelta = new Vector2(420f, 300f);
            var group = root.GetComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = false;

            var caption = new GameObject("Drag To Move", typeof(RectTransform), typeof(TextMeshProUGUI));
            caption.layer = 5;
            caption.transform.SetParent(root.transform, false);
            var text = caption.GetComponent<TextMeshProUGUI>();
            text.text = "DRAG TO MOVE";
            text.font = CasualHudKit.FontOutline(40);
            text.fontSharedMaterial = text.font.material;
            text.fontSize = 40f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.rectTransform.sizeDelta = new Vector2(420f, 66f);
            text.rectTransform.anchoredPosition = new Vector2(0f, 122f);

            Image arrow = MakeImage(root.transform, "Infinity Arrow", AssetDatabase.LoadAssetAtPath<Sprite>(arrowPath));
            arrow.rectTransform.sizeDelta = new Vector2(360f, 360f * 880f / 1772f);
            Image hand = MakeImage(arrow.transform, "Hand", CasualHudKit.Picto("Tap", 512));
            hand.rectTransform.sizeDelta = new Vector2(132f, 132f);
            // This pointing hand is already upright. Keep its fingertip on the arrow path.
            hand.rectTransform.pivot = new Vector2(.30f, .81f);
            hand.rectTransform.localRotation = Quaternion.identity;
            var handOutline = hand.gameObject.AddComponent<Outline>();
            handOutline.effectColor = Color.black;
            handOutline.effectDistance = new Vector2(2.5f, -2.5f);
            var handGroup = hand.gameObject.AddComponent<CanvasGroup>();
            handGroup.interactable = handGroup.blocksRaycasts = false;

            var component = root.AddComponent<DragToMoveTutorial>();
            var so = new SerializedObject(component);
            so.FindProperty("visibility").objectReferenceValue = group;
            so.FindProperty("arrow").objectReferenceValue = arrow.rectTransform;
            so.FindProperty("hand").objectReferenceValue = hand.rectTransform;
            so.FindProperty("handVisibility").objectReferenceValue = handGroup;
            Vector2[] path = {
                new(.584f, .233f), new(.71f, .20f), new(.83f, .34f), new(.85f, .54f),
                new(.79f, .72f), new(.67f, .78f), new(.56f, .68f), new(.5f, .50f),
                new(.43f, .31f), new(.31f, .22f), new(.19f, .31f), new(.15f, .51f),
                new(.22f, .72f), new(.35f, .76f), new(.39f, .75f)
            };
            var points = so.FindProperty("fingerPath");
            points.arraySize = path.Length;
            for (int i = 0; i < path.Length; i++) points.GetArrayElementAtIndex(i).vector2Value = path[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            hand.rectTransform.anchoredPosition = Vector2.Scale(path[0] - arrow.rectTransform.pivot, arrow.rectTransform.sizeDelta);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    static Image MakeImage(Transform parent, string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }
}
