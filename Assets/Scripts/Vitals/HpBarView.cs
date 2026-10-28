using TMPro;
using UnityEngine;

/// <summary>
/// Billboard HP / oxygen art. Frame, fills, and labels are authored on the prefab;
/// runtime only changes fill width and label text.
/// </summary>
public class HpBarView : MonoBehaviour
{
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    const int LabelSortingOrder = 50;

    struct AuthoredFill
    {
        public bool valid;
        public Vector3 localPosition;
        public Vector3 localScale;
        public Vector2 spriteSize;
        public bool sliced;
    }

    [Header("Bars")]
    [SerializeField] bool showOxygen = true;

    [Header("Value Labels")]
    [SerializeField] bool showValueLabels = true;
    [SerializeField] Color labelColor = new Color(1f, 1f, 1f, 0.95f);

    static readonly Color SliderTrackColor = new Color(0.05f, 0.05f, 0.06f, 1f);

    [Header("Colors")]
    [SerializeField] Color frameColor = SliderTrackColor;
    [SerializeField] Color healthFillColor = new Color(0.45f, 0.92f, 0.22f, 1f);
    [SerializeField] Color healthLowColor = new Color(0.9f, 0.2f, 0.2f, 1f);
    [SerializeField] Color oxygenFillColor = new Color(0.25f, 0.65f, 1f, 1f);

    [Header("Damage Trail")]
    [SerializeField] Color damageTrailColor = Color.white;
    [Tooltip("How long the white bar waits before it starts catching the colored bar.")]
    [SerializeField, Min(0f)] float damageTrailDelay = 0.2f;
    [Tooltip("Seconds the white bar takes to finish catching up after the delay. Scaled a little by how much health was lost.")]
    [SerializeField, Min(0.05f)] float damageTrailDuration = 0.34f;

    [Header("References")]
    [SerializeField] Renderer frameRenderer;
    [SerializeField] Renderer oxygenFrameRenderer;
    [SerializeField] Transform healthFill;
    [SerializeField] Transform oxygenFill;
    [SerializeField] Renderer healthFillRenderer;
    [SerializeField] Renderer oxygenFillRenderer;
    [SerializeField] Transform healthTrailFill;
    [SerializeField] Renderer healthTrailRenderer;
    [SerializeField] Material barMaterial;
    [SerializeField] Material fillMaterial;
    [SerializeField] TMP_Text healthLabel;
    [SerializeField] TMP_Text oxygenLabel;
    [SerializeField] TMP_Text levelLabel;

    MaterialPropertyBlock _block;
    bool _levelLabelReady;
    Color _levelLabelColor = Color.white;
    bool _levelLabelColorSet;
    bool _colorsCaptured;
    bool _healthColorLocked;
    Color _authoredFrameColor = Color.white;
    Color _authoredHealthColor = Color.white;
    Color _authoredOxygenColor = Color.white;
    AuthoredFill _healthAuthored;
    AuthoredFill _oxygenAuthored;
    AuthoredFill _trailAuthored;
    float _previewHealth = 1f;
    float _previewOxygen = 1f;
    float _healthCurrent = 1f;
    float _healthMax = 1f;
    float _oxygenCurrent = 1f;
    float _oxygenMax = 1f;
    float _alpha = 1f;
    float _trailHealth = 1f;
    float _trailDelay;
    float _trailFrom;
    float _trailTween;
    float _trailTweenDuration;
    bool _trailInitialized;
    bool _trailAnimating;

    void Awake()
    {
        CaptureAuthoredColors();
        CaptureAuthoredLayout();
        EnsureTrailFill();
    }

    void Update()
    {
        if (!_trailInitialized || _trailHealth <= _previewHealth + 0.0001f)
            return;

        if (_trailDelay > 0f)
        {
            _trailDelay -= Time.deltaTime;
            return;
        }

        if (!_trailAnimating)
        {
            _trailAnimating = true;
            _trailFrom = _trailHealth;
            _trailTween = 0f;
            float gap = Mathf.Clamp01(_trailFrom - _previewHealth);
            _trailTweenDuration = Mathf.Clamp(damageTrailDuration * Mathf.Lerp(0.55f, 1f, gap), 0.16f, damageTrailDuration);
        }

        _trailTween += Time.deltaTime / Mathf.Max(0.05f, _trailTweenDuration);
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_trailTween));
        _trailHealth = Mathf.Lerp(_trailFrom, _previewHealth, t);
        if (_trailTween >= 1f)
        {
            _trailHealth = _previewHealth;
            _trailAnimating = false;
        }

        ApplyTrail(_trailHealth);
    }

    void OnEnable()
    {
        CaptureAuthoredColors();
        CaptureAuthoredLayout();
        EnsureTrailFill();
        EnsureValueLabels();
        EnsureLevelLabel();
        EnsureMaterials();
        ApplyOxygenVisibility();
        RefreshValueLabels();
    }

    void CaptureAuthoredColors()
    {
        if (_colorsCaptured)
            return;

        _authoredFrameColor = ReadSpriteColor(frameRenderer, frameColor);
        if (!_healthColorLocked)
            _authoredHealthColor = ReadSpriteColor(healthFillRenderer, healthFillColor);
        _authoredOxygenColor = ReadSpriteColor(oxygenFillRenderer, oxygenFillColor);
        _colorsCaptured = true;
    }

    /// <summary>
    /// Replaces the health fill color. Used so creatures and kings stay red while the player stays green.
    /// </summary>
    public void SetHealthFillColor(Color color)
    {
        _authoredHealthColor = color;
        _healthColorLocked = true;
        ApplyHealthFillColor(_previewHealth);
    }

    static Color ReadSpriteColor(Renderer renderer, Color fallback)
    {
        if (renderer is SpriteRenderer spriteRenderer)
            return spriteRenderer.color;
        return fallback;
    }

    public void SetOxygenVisible(bool visible)
    {
        showOxygen = visible;
        ApplyOxygenVisibility();
        RefreshValueLabels();
    }

    public void SetHealth(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);
        EnsureTrailFill();

        if (!_trailInitialized)
        {
            _trailInitialized = true;
            _previewHealth = normalized;
            _trailHealth = normalized;
            _trailDelay = 0f;
            _trailAnimating = false;
            ApplyFill(ref _healthAuthored, healthFill, healthFillRenderer, normalized);
            ApplyTrail(normalized);
            ApplyHealthFillColor(normalized);
            return;
        }

        bool damaged = normalized < _previewHealth - 0.0001f;
        _previewHealth = normalized;
        ApplyFill(ref _healthAuthored, healthFill, healthFillRenderer, normalized);
        ApplyHealthFillColor(normalized);

        if (normalized >= _trailHealth - 0.0001f)
        {
            _trailHealth = normalized;
            _trailDelay = 0f;
            _trailAnimating = false;
            ApplyTrail(normalized);
            return;
        }

        if (damaged)
        {
            _trailDelay = damageTrailDelay;
            _trailAnimating = false;
        }
    }

    public void SetAlpha(float alpha)
    {
        _alpha = Mathf.Clamp01(alpha);
        SetRendererColor(frameRenderer, _authoredFrameColor);
        SetRendererColor(oxygenFrameRenderer, _authoredFrameColor);
        ApplyHealthFillColor(_previewHealth);
        SetRendererColor(healthTrailRenderer, damageTrailColor);
        SetRendererColor(oxygenFillRenderer, _authoredOxygenColor);
        ApplyLabelAlpha();
    }

    void ApplyHealthFillColor(float normalized)
    {
        SetRendererColor(healthFillRenderer, _authoredHealthColor);
    }

    /// <summary>
    /// Level centered above the health bar: "Lv8", with a dark outline.
    /// The player stays the prefab color. Kings pass red. Regular mobs call <see cref="HideLevel"/>.
    /// </summary>
    public void SetLevel(int level)
    {
        SetLevel(level, labelColor);
    }

    public void SetLevel(int level, Color color)
    {
        _levelLabelColor = color;
        _levelLabelColorSet = true;
        EnsureLevelLabel();
        if (levelLabel == null)
            return;

        if (!levelLabel.gameObject.activeSelf)
            levelLabel.gameObject.SetActive(true);

        string text = "Lv" + Mathf.Max(1, level);
        if (levelLabel.text != text)
        {
            levelLabel.text = text;
            levelLabel.ForceMeshUpdate();
        }

        ApplyLevelLabelColor();
    }

    public void HideLevel()
    {
        if (levelLabel == null)
            levelLabel = FindLabel("LevelValue");
        if (levelLabel != null && levelLabel.gameObject.activeSelf)
            levelLabel.gameObject.SetActive(false);
    }

    public void SetHealthValues(float current, float max)
    {
        _healthMax = Mathf.Max(0f, max);
        _healthCurrent = Mathf.Clamp(current, 0f, _healthMax > 0f ? _healthMax : current);
        float normalized = _healthMax > 0f ? _healthCurrent / _healthMax : 0f;
        SetHealth(normalized);
        RefreshHealthLabel();
    }

    public void SetOxygen(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);
        _previewOxygen = normalized;
        if (!showOxygen)
            return;

        ApplyFill(ref _oxygenAuthored, oxygenFill, oxygenFillRenderer, normalized);
        SetRendererColor(oxygenFillRenderer, _authoredOxygenColor);
    }

    public void SetOxygenValues(float current, float max)
    {
        _oxygenMax = Mathf.Max(0f, max);
        _oxygenCurrent = Mathf.Clamp(current, 0f, _oxygenMax > 0f ? _oxygenMax : current);
        float normalized = _oxygenMax > 0f ? _oxygenCurrent / _oxygenMax : 0f;
        SetOxygen(normalized);
        RefreshOxygenLabel();
    }

    void ApplyOxygenVisibility()
    {
        if (oxygenFrameRenderer != null)
            oxygenFrameRenderer.gameObject.SetActive(showOxygen);
        if (oxygenFill != null)
            oxygenFill.gameObject.SetActive(showOxygen);
        if (oxygenLabel != null)
            oxygenLabel.gameObject.SetActive(showValueLabels && showOxygen);
    }

    void CaptureAuthoredLayout()
    {
        CaptureFill(ref _healthAuthored, healthFill, healthFillRenderer);
        CaptureFill(ref _oxygenAuthored, oxygenFill, oxygenFillRenderer);
    }

    static void CaptureFill(ref AuthoredFill authored, Transform fill, Renderer renderer)
    {
        if (authored.valid || fill == null)
            return;

        authored.localPosition = fill.localPosition;
        authored.localScale = fill.localScale;
        authored.spriteSize = Vector2.one;
        authored.sliced = false;

        if (renderer is SpriteRenderer spriteRenderer)
        {
            authored.sliced = spriteRenderer.drawMode == SpriteDrawMode.Sliced
                || spriteRenderer.drawMode == SpriteDrawMode.Tiled;
            if (authored.sliced)
                authored.spriteSize = spriteRenderer.size;
            else if (spriteRenderer.sprite != null)
                authored.spriteSize = spriteRenderer.sprite.bounds.size;
        }

        authored.spriteSize.x = Mathf.Max(0.0001f, authored.spriteSize.x);
        authored.spriteSize.y = Mathf.Max(0.0001f, authored.spriteSize.y);
        authored.valid = true;
    }

    void EnsureValueLabels()
    {
        if (healthLabel == null)
            healthLabel = FindLabel("HealthValue");
        if (oxygenLabel == null)
            oxygenLabel = FindLabel("OxygenValue");

        EnsureWorldSpaceLabel(healthLabel);
        EnsureWorldSpaceLabel(oxygenLabel);
        if (levelLabel != null)
            EnsureWorldSpaceLabel(levelLabel);
    }

    static void EnsureWorldSpaceLabel(TMP_Text label)
    {
        if (label == null)
            return;

        Canvas canvas = label.GetComponent<Canvas>();
        if (canvas == null)
            canvas = label.gameObject.AddComponent<Canvas>();

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = LabelSortingOrder;
        canvas.additionalShaderChannels =
            AdditionalCanvasShaderChannels.TexCoord1
            | AdditionalCanvasShaderChannels.Normal
            | AdditionalCanvasShaderChannels.Tangent;
    }

    TMP_Text FindLabel(string objectName)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && children[i].name == objectName)
                return children[i].GetComponent<TMP_Text>();
        }

        return null;
    }

    void RefreshValueLabels()
    {
        RefreshHealthLabel();
        RefreshOxygenLabel();
    }

    void RefreshHealthLabel()
    {
        if (healthLabel == null)
            return;

        healthLabel.gameObject.SetActive(showValueLabels);
        if (!showValueLabels)
            return;

        healthLabel.text = FormatValue(_healthCurrent, _healthMax);
        ApplyLabelColor(healthLabel);
        healthLabel.ForceMeshUpdate();
    }

    void RefreshOxygenLabel()
    {
        if (oxygenLabel == null)
            return;

        bool visible = showValueLabels && showOxygen;
        oxygenLabel.gameObject.SetActive(visible);
        if (!visible)
            return;

        oxygenLabel.text = FormatValue(_oxygenCurrent, _oxygenMax);
        ApplyLabelColor(oxygenLabel);
        oxygenLabel.ForceMeshUpdate();
    }

    static string FormatValue(float current, float max)
    {
        int cur = Mathf.Max(0, Mathf.RoundToInt(current));
        int mx = Mathf.Max(0, Mathf.RoundToInt(max));
        return $"{cur}/{mx}";
    }

    void ApplyFill(ref AuthoredFill authored, Transform fill, Renderer renderer, float normalized)
    {
        if (fill == null)
            return;

        CaptureFill(ref authored, fill, renderer);
        if (!authored.valid)
            return;

        normalized = Mathf.Clamp01(normalized);
        Vector3 position = authored.localPosition;
        Vector3 scale = authored.localScale;
        float fullWidth = authored.spriteSize.x * Mathf.Abs(scale.x);
        float width = Mathf.Max(0.0001f, fullWidth * normalized);

        fill.localPosition = new Vector3(
            position.x - fullWidth * 0.5f + width * 0.5f,
            position.y,
            position.z);

        if (renderer is SpriteRenderer spriteRenderer && authored.sliced)
        {
            fill.localScale = scale;
            spriteRenderer.size = new Vector2(
                Mathf.Max(0.0001f, authored.spriteSize.x * normalized),
                authored.spriteSize.y);
            return;
        }

        fill.localScale = new Vector3(
            scale.x * Mathf.Max(0.0001f, normalized),
            scale.y,
            scale.z);
    }

    void EnsureTrailFill()
    {
        if (healthTrailFill != null || healthFill == null)
            return;

        GameObject copy = Instantiate(healthFill.gameObject, healthFill.parent);
        copy.name = "HealthTrail";
        copy.transform.SetSiblingIndex(healthFill.GetSiblingIndex());

        Vector3 position = copy.transform.localPosition;
        position.z += 0.01f;
        copy.transform.localPosition = position;

        healthTrailFill = copy.transform;
        healthTrailRenderer = copy.GetComponent<Renderer>();
        if (healthTrailRenderer is SpriteRenderer trailSprite && healthFillRenderer is SpriteRenderer healthSprite)
            trailSprite.sortingOrder = healthSprite.sortingOrder - 1;

        CaptureFill(ref _trailAuthored, healthTrailFill, healthTrailRenderer);
        SetRendererColor(healthTrailRenderer, damageTrailColor);
    }

    void ApplyTrail(float normalized)
    {
        if (healthTrailFill == null)
            return;

        ApplyFill(ref _trailAuthored, healthTrailFill, healthTrailRenderer, normalized);
        SetRendererColor(healthTrailRenderer, damageTrailColor);
    }

    void EnsureMaterials()
    {
        AssignSharedMaterial(frameRenderer, barMaterial);
        AssignSharedMaterial(oxygenFrameRenderer, barMaterial);
        Material fill = fillMaterial != null ? fillMaterial : barMaterial;
        AssignSharedMaterial(healthFillRenderer, fill);
        AssignSharedMaterial(healthTrailRenderer, fill);
        AssignSharedMaterial(oxygenFillRenderer, fill);
    }

    void AssignSharedMaterial(Renderer renderer, Material material)
    {
        if (renderer == null || material == null)
            return;
        if (renderer.sharedMaterial != material)
            renderer.sharedMaterial = material;
        if (renderer is SpriteRenderer spriteRenderer)
            BindSpriteTexture(spriteRenderer);
    }

    Material FillMaterial()
    {
        return fillMaterial != null ? fillMaterial : barMaterial;
    }

    void BindSpriteTexture(SpriteRenderer spriteRenderer)
    {
        if (spriteRenderer.sprite == null)
            return;

        if (_block == null)
            _block = new MaterialPropertyBlock();

        spriteRenderer.GetPropertyBlock(_block);
        _block.SetTexture(MainTexId, spriteRenderer.sprite.texture);
        spriteRenderer.SetPropertyBlock(_block);
    }

    void SetRendererColor(Renderer renderer, Color color)
    {
        if (renderer == null)
            return;

        AssignSharedMaterial(renderer, renderer == frameRenderer ? barMaterial : FillMaterial());
        color.a *= _alpha;

        if (renderer is SpriteRenderer spriteRenderer)
        {
            spriteRenderer.color = color;
            BindSpriteTexture(spriteRenderer);
            return;
        }

        if (_block == null)
            _block = new MaterialPropertyBlock();

        renderer.GetPropertyBlock(_block);
        _block.SetColor(ColorId, color);
        renderer.SetPropertyBlock(_block);
    }

    void EnsureLevelLabel()
    {
        if (_levelLabelReady && levelLabel != null)
            return;

        if (levelLabel == null)
            levelLabel = FindLabel("LevelValue");

        if (levelLabel == null)
            return;

        if (!levelLabel.gameObject.activeSelf)
            levelLabel.gameObject.SetActive(true);

        EnsureWorldSpaceLabel(levelLabel);
        levelLabel.fontStyle = FontStyles.Normal;
        levelLabel.alignment = TextAlignmentOptions.Center;
        levelLabel.textWrappingMode = TextWrappingModes.NoWrap;
        levelLabel.overflowMode = TextOverflowModes.Overflow;
        levelLabel.extraPadding = true;
        levelLabel.raycastTarget = false;
        if (levelLabel.font != null && levelLabel.fontSharedMaterial != null)
        {
            levelLabel.outlineWidth = 0.35f;
            levelLabel.outlineColor = Color.black;
            Material face = levelLabel.fontMaterial;
            if (face != null)
            {
                face.SetFloat("_OutlineSoftness", 0f);
                face.SetFloat("_FaceDilate", 0.12f);
                face.SetFloat("_OutlineWidth", 0.35f);
                face.SetColor("_OutlineColor", Color.black);
            }
        }

        RectTransform rect = levelLabel.rectTransform;
        rect.sizeDelta = new Vector2(220f, 90f);
        if (rect.localScale.y > 0.05f || rect.localScale.sqrMagnitude < 0.0000001f)
            rect.localScale = new Vector3(0.01f, 0.01f, 0.01f);
        PlaceLevelAboveHealth(rect);
        _levelLabelReady = true;
    }

    void PlaceLevelAboveHealth(RectTransform rect)
    {
        CaptureFill(ref _healthAuthored, healthFill, healthFillRenderer);

        float barTop = 0.16f;
        float centerX = 0f;
        if (_healthAuthored.valid && healthFill != null)
        {
            float height = _healthAuthored.spriteSize.y * Mathf.Abs(_healthAuthored.localScale.y);
            barTop = _healthAuthored.localPosition.y + height * 0.5f;
            centerX = healthLabel != null ? healthLabel.rectTransform.localPosition.x : _healthAuthored.localPosition.x;
        }

        float glyph = levelLabel.fontSize * Mathf.Abs(rect.localScale.y);
        Vector3 local = rect.localPosition;
        local.x = centerX;
        local.y = barTop + glyph * 0.5f + 0.06f;
        local.z = -0.05f;
        rect.localPosition = local;
    }

    void ApplyLabelAlpha()
    {
        ApplyLabelColor(healthLabel);
        ApplyLabelColor(oxygenLabel);
        ApplyLevelLabelColor();
    }

    void ApplyLevelLabelColor()
    {
        if (levelLabel == null)
            return;

        Color color = _levelLabelColorSet ? _levelLabelColor : labelColor;
        color.a *= _alpha;
        levelLabel.color = color;
    }

    void ApplyLabelColor(TMP_Text label)
    {
        if (label == null)
            return;

        Color color = labelColor;
        color.a *= _alpha;
        label.color = color;
    }
}
