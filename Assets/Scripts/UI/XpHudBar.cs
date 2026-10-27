using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-right account XP bar. Dark pill with "Lv8" on it, filling left to right
/// the way the XP Hero corner bar does. Level still grants no stats.
/// </summary>
public class XpHudBar : MonoBehaviour
{
    const string BarName = "XpBar";

    static Sprite _pill;

    PlayerLevel _level;
    RectTransform _bar;
    RectTransform _fill;
    TMP_Text _label;
    Image _fillImage;
    Canvas _canvas;
    int _screenW = -1;
    int _screenH = -1;

    void OnEnable()
    {
        EnsureBar();
        Bind();
        Refresh();
    }

    void OnDisable()
    {
        Unbind();
    }

    void Update()
    {
        if (_level == null)
            Bind();

        if (_bar != null && (_screenW != Screen.width || _screenH != Screen.height))
            PlaceInCorner();
    }

    void Bind()
    {
        Unbind();
        _level = PlayerLevel.EnsureExists();
        if (_level == null)
            return;

        _level.Changed += Refresh;
        Refresh();
    }

    void Unbind()
    {
        if (_level == null)
            return;

        _level.Changed -= Refresh;
        _level = null;
    }

    void Refresh()
    {
        if (_label == null || _fill == null)
            return;

        int level = _level != null ? _level.Level : 1;
        float amount = _level != null ? _level.XpNormalized : 0f;
        string text = "Lv" + Mathf.Max(1, level);
        if (_label.text != text)
            _label.text = text;

        bool showFill = amount > 0.001f;
        if (_fillImage != null && _fillImage.enabled != showFill)
            _fillImage.enabled = showFill;

        _fill.anchorMin = Vector2.zero;
        _fill.anchorMax = new Vector2(Mathf.Clamp01(amount), 1f);
        _fill.offsetMin = Vector2.zero;
        _fill.offsetMax = Vector2.zero;
    }

    void EnsureBar()
    {
        if (_bar != null)
            return;

        _canvas = GetComponentInChildren<Canvas>(true);
        if (_canvas == null)
            return;

        Transform existing = _canvas.transform.Find(BarName);
        if (existing != null)
        {
            _bar = existing as RectTransform;
            _fill = existing.Find("FillMask/Fill") as RectTransform;
            _fillImage = _fill != null ? _fill.GetComponent<Image>() : null;
            _label = existing.Find("Label")?.GetComponent<TMP_Text>();
            PlaceInCorner();
            return;
        }

        Sprite pill = Pill();
        var root = new GameObject(BarName, typeof(RectTransform));
        root.layer = 5;
        root.transform.SetParent(_canvas.transform, false);
        _bar = root.GetComponent<RectTransform>();

        Image track = CreateImage(root.transform, "Track", pill, new Color(0.07f, 0.07f, 0.09f, 0.94f));
        Stretch(track.rectTransform, 0f);

        var maskGo = new GameObject("FillMask", typeof(RectTransform), typeof(RectMask2D));
        maskGo.layer = 5;
        maskGo.transform.SetParent(root.transform, false);
        var mask = maskGo.GetComponent<RectTransform>();
        Stretch(mask, 4f);

        _fillImage = CreateImage(mask, "Fill", pill, new Color(1f, 0.78f, 0.22f, 1f));
        _fill = _fillImage.rectTransform;
        _fill.pivot = new Vector2(0f, 0.5f);

        _label = CreateLabel(root.transform, Font(transform));
        PlaceInCorner();
        KeepUnderOverlays();
    }

    void PlaceInCorner()
    {
        if (_bar == null)
            return;

        _screenW = Screen.width;
        _screenH = Screen.height;
        _bar.anchorMin = new Vector2(1f, 1f);
        _bar.anchorMax = new Vector2(1f, 1f);
        _bar.pivot = new Vector2(1f, 1f);
        _bar.sizeDelta = new Vector2(280f, 52f);
        _bar.localScale = Vector3.one;
        _bar.localRotation = Quaternion.identity;

        float scale = _canvas != null && _canvas.scaleFactor > 0.01f ? _canvas.scaleFactor : 1f;
        Rect safe = Screen.safeArea;
        float right = (Screen.width - safe.xMax) / scale;
        float top = (Screen.height - safe.yMax) / scale;
        _bar.anchoredPosition = new Vector2(-(28f + right), -(28f + top));
    }

    void KeepUnderOverlays()
    {
        if (_bar == null || _canvas == null)
            return;

        Transform overlay = _canvas.transform.Find("DeathOverlay");
        if (overlay != null)
            _bar.SetSiblingIndex(overlay.GetSiblingIndex());
        else
            _bar.SetAsLastSibling();
    }

    static Image CreateImage(Transform parent, string name, Sprite sprite, Color color)
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

    static TMP_Text CreateLabel(Transform parent, TMP_FontAsset font)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        if (font != null)
            text.font = font;
        text.text = "Lv1";
        text.fontSize = 30f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.outlineWidth = 0.18f;
        text.outlineColor = new Color32(20, 16, 28, 255);
        Stretch(text.rectTransform, 0f);
        return text;
    }

    static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    static TMP_FontAsset Font(Transform searchRoot)
    {
        if (searchRoot != null)
        {
            TMP_Text sample = searchRoot.GetComponentInChildren<TMP_Text>(true);
            if (sample != null && sample.font != null)
                return sample.font;
        }

        return TMP_Settings.defaultFontAsset;
    }

    static Sprite Pill()
    {
        if (_pill != null)
            return _pill;

        const int size = 32;
        const int radius = 14;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
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
        _pill.name = "XpBarPill";
        return _pill;
    }
}
