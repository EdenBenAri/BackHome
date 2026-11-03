using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One-time migration to a nested, editable prefab. Existing authored widgets are never rebuilt.</summary>
public static class MiniMapPrefabAuthoring
{
    public const string Folder = "Assets/Resources/HUD/MiniMap";
    public const string PrefabPath = Folder + "/MiniMap.prefab";

    public static bool EnsureOnHud(GameObject hud)
    {
        Canvas canvas = hud.GetComponentInChildren<Canvas>(true);
        if (canvas == null) return false;
        EnsureAsset();
        bool changed = false;
        MiniMap legacy = hud.GetComponent<MiniMap>();
        if (legacy != null) { Object.DestroyImmediate(legacy); changed = true; }
        if (canvas.GetComponentInChildren<MiniMap>(true) != null) return changed;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) return changed;
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
        instance.name = "MiniMap";
        // Under modal overlays, alongside the authored top-row chips.
        Transform xp = canvas.transform.Find("XpBar");
        if (xp != null) instance.transform.SetSiblingIndex(xp.GetSiblingIndex() + 1);
        return true;
    }

    [MenuItem("BackHome/HUD/Open MiniMap Prefab")]
    public static void OpenPrefab()
    {
        EnsureAsset();
        AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
    }

    public static void EnsureAsset()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
        Directory.CreateDirectory(Folder);
        Sprite circle = SaveSprite(CircleSprite(128, false), "Circle");
        Sprite arrow = SaveSprite(ArrowSprite(64), "PlayerArrow");
        Sprite home = SaveSprite(HouseSprite(64), "Home");
        Sprite enemy = SaveSprite(DotSprite(32), "Enemy");
        Texture2D preview = CreatePreview();
        var root = new GameObject("MiniMap", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        try
        {
            root.layer = 5;
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.sizeDelta = new Vector2(208f, 208f);
            rect.anchoredPosition = new Vector2(-28f, -108f);
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            group.blocksRaycasts = group.interactable = false;
            Image shadow = Image(rect, "Shadow", circle, new Color(0.06f, .13f, .22f, .25f), 210f);
            shadow.rectTransform.anchoredPosition = new Vector2(0f, -5f);
            Image(rect, "Outline", circle, new Color32(22, 42, 65, 255), 208f);
            Image(rect, "Blue Rim", circle, new Color32(83, 132, 170, 255), 198f);
            Image lip = Image(rect, "Rim Highlight", circle, new Color32(188, 224, 237, 255), 190f);
            lip.rectTransform.anchoredPosition = new Vector2(0f, 1f);
            Image view = Image(rect, "Viewport", circle, Color.white, 182f);
            view.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var terrain = new GameObject("Terrain", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            terrain.layer = 5;
            terrain.transform.SetParent(view.transform, false);
            RawImage raw = terrain.GetComponent<RawImage>();
            raw.texture = preview;
            raw.raycastTarget = false;
            Stretch(raw.rectTransform);
            Image enemyImage = Image(view.transform, "Enemy Template", enemy, Color.white, 10f);
            enemyImage.gameObject.SetActive(false);
            Image homeImage = Image(view.transform, "Home", home, Color.white, 24f);
            homeImage.rectTransform.anchoredPosition = new Vector2(-38f, -35f);
            Image playerImage = Image(view.transform, "Player", arrow, Color.white, 26f);
            var component = root.AddComponent<MiniMap>();
            var so = new SerializedObject(component);
            so.FindProperty("viewport").objectReferenceValue = view.rectTransform;
            so.FindProperty("terrainImage").objectReferenceValue = raw;
            so.FindProperty("visibility").objectReferenceValue = group;
            so.FindProperty("playerMarker").objectReferenceValue = playerImage.rectTransform;
            so.FindProperty("homeMarker").objectReferenceValue = homeImage;
            so.FindProperty("enemyTemplate").objectReferenceValue = enemyImage;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    static Image Image(Transform parent, string name, Sprite sprite, Color color, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        image.preserveAspect = true;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = Vector2.one * size;
        return image;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    static Sprite SaveSprite(Sprite generated, string name)
    {
        string path = Folder + "/" + name + ".png";
        // The generated sprites are uploaded as readable until saved here, only in the editor.
        File.WriteAllBytes(path, generated.texture.EncodeToPNG());
        Object.DestroyImmediate(generated.texture);
        Object.DestroyImmediate(generated);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Texture2D CreatePreview()
    {
        // An illustrative editor-only preview; gameplay always replaces it with live terrain.
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        var palette = new MiniMapPalette();
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float coast = 26f + Mathf.Sin(y * .04f) * 9f;
            Color color = x < coast ? palette.water : x < coast + 4 ? palette.sand : palette.grass;
            if (Vector2.Distance(new Vector2(x, y), new Vector2(106, 22)) < 23f) color = palette.forest;
            float cloud = Mathf.Min(Vector2.Distance(new Vector2(x, y), new Vector2(100, 119)) - 38f,
                Vector2.Distance(new Vector2(x, y), new Vector2(134, 92)) - 36f);
            if (cloud < 0) color = cloud < -2f ? palette.fog : palette.fogEdge;
            pixels[y * size + x] = color;
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        string path = Folder + "/EditorPreview.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static Sprite CircleSprite(int size, bool soft)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = soft ? "MiniMapShadow" : "MiniMapCircle"
        };
        var pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        float radius = center;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alpha = soft
                    ? 1f - Mathf.SmoothStep(radius * 0.62f, radius, d)
                    : Mathf.Clamp01(radius - d + 0.85f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    static Sprite DotSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "MiniMapDot"
        };
        var pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        float radius = center - 0.5f;
        var clear = new Color32(0, 0, 0, 0);
        var ink = new Color32(255, 255, 255, 255);
        var fill = new Color32(242, 91, 112, 255);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                if (d > radius + 0.6f)
                    pixels[y * size + x] = clear;
                else if (d > radius - 1.35f)
                    pixels[y * size + x] = ink;
                else
                    pixels[y * size + x] = fill;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    static Sprite ArrowSprite(int size)
    {
        var pixels = new Color32[size * size];
        Clear(pixels);
        var ink = new Color32(22, 42, 65, 255);
        var fill = new Color32(255, 224, 101, 255);
        // Texture row 0 is the bottom, so the tip sits on the high row and points up.
        FillTriangle(pixels, size, new Vector2(size * 0.5f, size - 5f), new Vector2(7f, 7f), new Vector2(size - 7f, 7f), ink);
        FillTriangle(pixels, size, new Vector2(size * 0.5f, size - 11f), new Vector2(14f, 12f), new Vector2(size - 14f, 12f), fill);
        return FinishSprite(pixels, size, "MiniMapArrow");
    }

    static Sprite HouseSprite(int size)
    {
        var pixels = new Color32[size * size];
        Clear(pixels);
        var ink = new Color32(16, 26, 46, 255);
        var paper = new Color32(255, 255, 255, 255);
        float s = size;
        FillRect(pixels, size, s * 0.18f, s * 0.08f, s * 0.82f, s * 0.52f, ink);
        FillTriangle(pixels, size, new Vector2(s * 0.5f, s * 0.94f), new Vector2(s * 0.08f, s * 0.50f), new Vector2(s * 0.92f, s * 0.50f), ink);
        FillRect(pixels, size, s * 0.26f, s * 0.14f, s * 0.74f, s * 0.50f, paper);
        FillTriangle(pixels, size, new Vector2(s * 0.5f, s * 0.86f), new Vector2(s * 0.16f, s * 0.52f), new Vector2(s * 0.84f, s * 0.52f), paper);
        FillRect(pixels, size, s * 0.42f, s * 0.14f, s * 0.58f, s * 0.38f, ink);
        return FinishSprite(pixels, size, "MiniMapHouse");
    }

    static Sprite FinishSprite(Color32[] pixels, int size, string spriteName)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = spriteName
        };
        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    static void Clear(Color32[] pixels)
    {
        var clear = new Color32(0, 0, 0, 0);
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = clear;
    }

    static void FillRect(Color32[] pixels, int size, float x0, float y0, float x1, float y1, Color32 color)
    {
        int left = Mathf.Clamp(Mathf.FloorToInt(x0), 0, size - 1);
        int right = Mathf.Clamp(Mathf.CeilToInt(x1), 0, size);
        int bottom = Mathf.Clamp(Mathf.FloorToInt(y0), 0, size - 1);
        int top = Mathf.Clamp(Mathf.CeilToInt(y1), 0, size);
        for (int y = bottom; y < top; y++)
        {
            for (int x = left; x < right; x++)
                pixels[y * size + x] = color;
        }
    }

    static void FillTriangle(Color32[] pixels, int size, Vector2 a, Vector2 b, Vector2 c, Color32 color)
    {
        int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), 0, size - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))), 0, size - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), 0, size - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))), 0, size - 1);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                if (!PointInTriangle(p, a, b, c))
                    continue;
                pixels[y * size + x] = color;
            }
        }
    }

    static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Edge(p, a, b);
        float d2 = Edge(p, b, c);
        float d3 = Edge(p, c, a);
        bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(neg && pos);
    }

    static float Edge(Vector2 p, Vector2 a, Vector2 b)
    {
        return (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    }
}

