using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-right account XP bar authored on the HUD prefab. Fills left to right and
/// shows the player level as "Lv8". Layout stays as saved on that prefab.
/// </summary>
public class XpHudBar : MonoBehaviour
{
    const string BarName = "XpBar";

    [SerializeField] RectTransform bar;
    [SerializeField] RectTransform fill;
    [SerializeField] Image fillImage;
    [SerializeField] TMP_Text label;

    PlayerLevel _level;

    void OnEnable()
    {
        Resolve();
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
        if (label != null)
        {
            int level = _level != null ? _level.Level : 1;
            string text = "Lv" + Mathf.Max(1, level);
            if (label.text != text)
                label.text = text;
        }

        if (fill == null)
            return;

        float amount = _level != null ? _level.XpNormalized : 0f;
        bool showFill = amount > 0.001f;
        if (fillImage != null && fillImage.enabled != showFill)
            fillImage.enabled = showFill;

        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(Mathf.Clamp01(amount), 1f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
    }

    void Resolve()
    {
        if (bar == null)
        {
            Canvas canvas = GetComponentInChildren<Canvas>(true);
            if (canvas != null)
                bar = canvas.transform.Find(BarName) as RectTransform;
        }

        if (bar == null)
            return;

        if (fill == null)
            fill = bar.Find("FillMask/Fill") as RectTransform;
        if (fillImage == null && fill != null)
            fillImage = fill.GetComponent<Image>();
        if (label == null)
            label = bar.Find("Label")?.GetComponent<TMP_Text>();
    }
}
