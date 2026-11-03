using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the oxygen gauge authored on the HUD prefab.
/// Layout, position, and label placement stay as saved on that prefab.
/// </summary>
public class OxygenHudBar : MonoBehaviour
{
    const string BarName = "OxygenBar";

    [SerializeField] RectTransform bar;
    [SerializeField] RectTransform fill;
    [SerializeField] Image fillImage;
    [SerializeField] TMP_Text label;

    PlayerVitals _vitals;

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
        if (_vitals == null)
            Bind();
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
        if (bar == null)
            return;

        bool show = _vitals != null && _vitals.HasOxygen;
        if (bar.gameObject.activeSelf != show)
            bar.gameObject.SetActive(show);
        if (!show)
            return;

        float amount = Mathf.Clamp01(_vitals != null ? _vitals.OxygenNormalized : 0f);
        if (fillImage != null && fillImage.enabled != (amount > 0.001f))
            fillImage.enabled = amount > 0.001f;

        if (fill != null)
            fill.anchorMax = new Vector2(fill.anchorMax.x, amount);

        if (label == null)
            return;

        int current = _vitals != null ? Mathf.Max(0, Mathf.RoundToInt(_vitals.CurrentOxygen)) : 0;
        int max = _vitals != null ? Mathf.Max(0, Mathf.RoundToInt(_vitals.MaxOxygen)) : 0;
        string text = current + "/" + max;
        if (label.text != text)
            label.text = text;
    }
}
