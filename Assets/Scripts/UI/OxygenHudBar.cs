using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Player oxygen, a vertical gauge at the left-center of the screen.
/// Black rim, blue fill rising from the bottom, current/max beside it.
/// </summary>
public class OxygenHudBar : MonoBehaviour
{
    const string BarName = "OxygenBar";

    PlayerVitals _vitals;
    RectTransform _bar;
    RectTransform _fill;
    Image _fillImage;
    TMP_Text _label;
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
        if (_vitals == null)
            Bind();

        if (_bar != null && (_screenW != Screen.width || _screenH != Screen.height))
            Place();
    }

    void Bind()
    {
        Unbind();
        _vitals = FindAnyObjectByType<PlayerVitals>();
        if (_vitals == null)
            return;

        _vitals.VitalsChanged += Refresh;
        Refresh();
    }

    void Unbind()
    {
        if (_vitals == null)
            return;

        _vitals.VitalsChanged -= Refresh;
        _vitals = null;
    }

    void Refresh()
    {
        if (_bar == null)
            return;

        bool show = _vitals != null && _vitals.HasOxygen;
        if (_bar.gameObject.activeSelf != show)
            _bar.gameObject.SetActive(show);
        if (!show)
            return;

        float amount = _vitals.OxygenNormalized;
        bool showFill = amount > 0.001f;
        if (_fillImage != null && _fillImage.enabled != showFill)
            _fillImage.enabled = showFill;

        if (_fill != null)
        {
            _fill.pivot = new Vector2(0.5f, 0f);
            _fill.anchorMin = Vector2.zero;
            _fill.anchorMax = new Vector2(1f, Mathf.Clamp01(amount));
            _fill.offsetMin = Vector2.zero;
            _fill.offsetMax = Vector2.zero;
        }

        if (_label != null)
        {
            int current = Mathf.Max(0, Mathf.RoundToInt(_vitals.CurrentOxygen));
            int max = Mathf.Max(0, Mathf.RoundToInt(_vitals.MaxOxygen));
            string text = current + "/" + max;
            if (_label.text != text)
                _label.text = text;
        }
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
            Place();
            return;
        }

        Sprite pill = HudChrome.Pill();
        var root = new GameObject(BarName, typeof(RectTransform));
        root.layer = 5;
        root.transform.SetParent(_canvas.transform, false);
        _bar = root.GetComponent<RectTransform>();

        Image track = HudChrome.CreateImage(root.transform, "Track", pill, new Color(0.05f, 0.05f, 0.05f, 0.95f));
        HudChrome.Stretch(track.rectTransform, 0f);

        var maskGo = new GameObject("FillMask", typeof(RectTransform), typeof(RectMask2D));
        maskGo.layer = 5;
        maskGo.transform.SetParent(root.transform, false);
        HudChrome.Stretch(maskGo.GetComponent<RectTransform>(), 4f);

        _fillImage = HudChrome.CreateImage(maskGo.transform, "Fill", pill, new Color(0.25f, 0.72f, 1f, 1f));
        _fill = _fillImage.rectTransform;
        _fill.pivot = new Vector2(0.5f, 0f);

        _label = CreateLabel(root.transform, HudChrome.Font(transform));
        Place();
        HudChrome.KeepUnderDeathOverlay(_bar, _canvas);
    }

    void Place()
    {
        if (_bar == null)
            return;

        _screenW = Screen.width;
        _screenH = Screen.height;
        _bar.anchorMin = new Vector2(0f, 0.5f);
        _bar.anchorMax = new Vector2(0f, 0.5f);
        _bar.pivot = new Vector2(0f, 0.5f);
        _bar.sizeDelta = new Vector2(48f, 250f);
        _bar.localScale = Vector3.one;
        _bar.localRotation = Quaternion.identity;

        float scale = _canvas != null && _canvas.scaleFactor > 0.01f ? _canvas.scaleFactor : 1f;
        Rect safe = Screen.safeArea;
        float left = safe.xMin / scale;
        _bar.anchoredPosition = new Vector2(28f + left, 0f);
        LayoutLabel();
    }

    void LayoutLabel()
    {
        if (_label == null)
            return;

        RectTransform rect = _label.rectTransform;
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(12f, 0f);
        rect.sizeDelta = new Vector2(160f, 48f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    static TMP_Text CreateLabel(Transform parent, TMP_FontAsset font)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        if (font != null)
            text.font = font;
        text.text = "0/0";
        text.fontSize = 26f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.outlineWidth = 0.2f;
        text.outlineColor = Color.black;
        return text;
    }
}
