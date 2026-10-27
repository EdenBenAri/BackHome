using TMPro;
using UnityEngine;

/// <summary>
/// World-space "NEXT KING" clock shown where an area king will return.
/// Matches the XP Hero field-boss countdown: a title over mm : ss, facing the camera,
/// only while that area's king is dead.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(900)]
public class AreaKingCountdown : MonoBehaviour
{
    const float TitleSize = 4.2f;
    const float TimerSize = 8f;
    const float Lift = 3.8f;
    const float OpaqueDistance = 32f;
    const float HiddenDistance = 68f;

    Camera _camera;
    TMP_FontAsset _font;
    TMP_Text _title;
    TMP_Text _timer;
    Vector3 _up = Vector3.up;
    float _readyAt;
    bool _shown;
    bool _fontWarned;

    public void UseFont(TMP_FontAsset font)
    {
        _font = font;
    }

    public void Show(Vector3 groundPosition, Vector3 up, float readyAt)
    {
        EnsureTexts();
        _up = up.sqrMagnitude > 0.001f ? up.normalized : Vector3.up;
        _readyAt = readyAt;
        transform.position = groundPosition + _up * Lift;
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
        _shown = true;
        Refresh();
    }

    public void Hide()
    {
        _shown = false;
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (!_shown)
            return;

        if (_camera == null)
            _camera = Camera.main;
        if (_camera == null)
            return;

        Vector3 toCamera = _camera.transform.position - transform.position;
        if (toCamera.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(-toCamera, _camera.transform.up);

        float distance = toCamera.magnitude;
        float alpha = Mathf.Clamp01(Mathf.InverseLerp(HiddenDistance, OpaqueDistance, distance));
        ApplyAlpha(alpha);
        Refresh();
    }

    void Refresh()
    {
        if (_timer == null)
            return;

        int total = Mathf.Max(0, Mathf.CeilToInt(_readyAt - Time.time));
        int minutes = total / 60;
        int seconds = total % 60;
        _timer.text = $"{minutes:00} : {seconds:00}";
    }

    void EnsureTexts()
    {
        if (_title != null && _timer != null)
            return;

        TMP_FontAsset font = _font != null ? _font : TMP_Settings.defaultFontAsset;
        if (font == null)
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font == null && !_fontWarned)
        {
            _fontWarned = true;
            Debug.LogWarning("AreaKingCountdown: no TextMeshPro font found, so the next-king timer has no label.", this);
            return;
        }

        _title = CreateLine("Title", "NEXT KING", TitleSize, 3.4f, font, 4f);
        _timer = CreateLine("Time", "00 : 00", TimerSize, 0f, font, 2f);
    }

    TMP_Text CreateLine(string objectName, string value, float size, float localY, TMP_FontAsset font, float tracking)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, localY, 0f);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var text = go.AddComponent<TextMeshPro>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = FontStyles.Normal;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.characterSpacing = tracking;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.outlineWidth = 0.18f;
        text.outlineColor = new Color32(28, 22, 36, 210);
        text.rectTransform.sizeDelta = new Vector2(30f, size + 1.5f);

        Renderer renderer = text.renderer;
        if (renderer != null)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        return text;
    }

    void ApplyAlpha(float alpha)
    {
        Apply(alpha, _title);
        Apply(alpha, _timer);
    }

    static void Apply(float alpha, TMP_Text text)
    {
        if (text == null)
            return;

        Color color = text.color;
        color.a = alpha;
        text.color = color;
    }
}
