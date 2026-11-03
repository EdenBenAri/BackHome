using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared pieces of the screen HUD: the dark pill sprite, sliced images, and font lookup.
/// The oxygen gauge, XP bar, and coin chip are authored on the HUD prefab.
/// </summary>
public static class HudChrome
{
    static Sprite _pill;

    public static Sprite Pill()
    {
        if (_pill != null)
            return _pill;

        const int size = 32;
        const int radius = 14;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var pixels = new Color32[size * size];
        float r = radius;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = 0f;
                if (x < r)
                    dx = r - x;
                else if (x > size - 1 - r)
                    dx = x - (size - 1 - r);

                float dy = 0f;
                if (y < r)
                    dy = r - y;
                else if (y > size - 1 - r)
                    dy = y - (size - 1 - r);

                float alpha = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.75f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        _pill = Sprite.Create(
            tex,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));
        _pill.name = "HudPill";
        return _pill;
    }

    public static Image CreateImage(Transform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        image.pixelsPerUnitMultiplier = 1f;
        return image;
    }

    public static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    public static TMP_FontAsset Font(Transform searchRoot)
    {
        if (searchRoot != null)
        {
            TMP_Text sample = searchRoot.GetComponentInChildren<TMP_Text>(true);
            if (sample != null && sample.font != null)
                return sample.font;
        }

        return TMP_Settings.defaultFontAsset;
    }

    public static void KeepUnderDeathOverlay(RectTransform bar, Canvas canvas)
    {
        if (bar == null || canvas == null)
            return;

        Transform overlay = canvas.transform.Find("DeathOverlay");
        if (overlay != null)
            bar.SetSiblingIndex(overlay.GetSiblingIndex());
        else
            bar.SetAsLastSibling();
    }
}
