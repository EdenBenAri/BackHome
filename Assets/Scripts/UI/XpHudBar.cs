using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-right account XP bar. Dark pill with "Lv8" on it, filling left to right
/// the way the XP Hero corner bar does. Level raises health, oxygen, and hit damage.
/// </summary>
public class XpHudBar : MonoBehaviour
{
    const string BarName = "XpBar";

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

        Sprite pill = HudChrome.Pill();
        var root = new GameObject(BarName, typeof(RectTransform));
        root.layer = 5;
        root.transform.SetParent(_canvas.transform, false);
        _bar = root.GetComponent<RectTransform>();

        Image track = HudChrome.CreateImage(root.transform, "Track", pill, new Color(0.07f, 0.07f, 0.09f, 0.94f));
        HudChrome.Stretch(track.rectTransform, 0f);

        var maskGo = new GameObject("FillMask", typeof(RectTransform), typeof(RectMask2D));
        maskGo.layer = 5;
        maskGo.transform.SetParent(root.transform, false);
        var mask = maskGo.GetComponent<RectTransform>();
        HudChrome.Stretch(mask, 4f);

        _fillImage = HudChrome.CreateImage(mask, "Fill", pill, new Color(1f, 0.78f, 0.22f, 1f));
        _fill = _fillImage.rectTransform;
        _fill.pivot = new Vector2(0f, 0.5f);

        _label = CreateLabel(root.transform, HudChrome.Font(transform));
        PlaceInCorner();
        HudChrome.KeepUnderDeathOverlay(_bar, _canvas);
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
        HudChrome.Stretch(text.rectTransform, 0f);
        return text;
    }
}
