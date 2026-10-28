using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-right account XP bar. Fills left to right. The level number is only on the world HP bar.
/// </summary>
public class XpHudBar : MonoBehaviour
{
    const string BarName = "XpBar";

    PlayerLevel _level;
    RectTransform _bar;
    RectTransform _fill;
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
        if (_fill == null)
            return;

        float amount = _level != null ? _level.XpNormalized : 0f;

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
            Transform oldLabel = existing.Find("Label");
            if (oldLabel != null)
                Destroy(oldLabel.gameObject);
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
}
