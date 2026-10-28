using UnityEngine;

/// <summary>
/// Billboard HP/oxygen bars for any <see cref="IVitalsReadable"/> on the same GameObject
/// (player, creature, …).
/// </summary>
[DefaultExecutionOrder(1000)]
public class HpBar : MonoBehaviour
{
    [SerializeField] HpBarView hpBarPrefab;
    [SerializeField] HpBarView bars;
    [SerializeField] Vector3 localOffset = new Vector3(0f, 2.15f, 0f);
    [Tooltip("Extra world size multiplier. SceneBootstrap can override this per scene.")]
    [SerializeField, Min(0.1f)] float worldScale = 1f;
    [SerializeField] bool hideWhenDead;
    [Tooltip("Health fill for creatures and kings. The player keeps the green color authored on the bar.")]
    [SerializeField] Color hostileHealthColor = new Color(0.92f, 0.18f, 0.16f, 1f);
    [Tooltip("Keep bars hidden until the host takes a hit. Always on for creatures.")]
    [SerializeField] bool hideUntilDamaged;
    [SerializeField, Min(0.05f)] float visibleAfterDamage = 2.5f;
    [SerializeField, Min(0f)] float fadeDuration = 0.2f;

    IVitalsReadable _source;
    PlayerLevel _level;
    Camera _camera;
    float _revealTimer;
    float _alpha = 1f;
    bool _forcedHidden;

    public void SetWorldScale(float scale)
    {
        worldScale = Mathf.Max(0.1f, scale);
    }

    public void SetLocalOffset(Vector3 offset)
    {
        localOffset = offset;
    }

    /// <summary>Force the bars off (e.g. teleport cinematic). They stay hidden until cleared.</summary>
    public void SetHidden(bool hidden)
    {
        _forcedHidden = hidden;
        if (hidden)
            HideBarsImmediate();
        else
            RefreshBars();
    }

    bool HideUntilDamaged => hideUntilDamaged || _source is Creature;

    void Awake()
    {
        _source = GetComponent<IVitalsReadable>();
        EnsureBars();
        ApplyOxygenVisibility();
        if (HideUntilDamaged)
            HideBarsImmediate();
    }

    void OnEnable()
    {
        if (_source == null)
            _source = GetComponent<IVitalsReadable>();

        if (_source != null)
        {
            _source.VitalsChanged += RefreshBars;
            _source.Damaged += OnDamaged;
            _source.Died += RefreshBars;
        }

        BindLevel();
        RefreshBars();
    }

    void Start()
    {
        RefreshBars();
    }

    void OnDisable()
    {
        if (_source != null)
        {
            _source.VitalsChanged -= RefreshBars;
            _source.Damaged -= OnDamaged;
            _source.Died -= RefreshBars;
        }

        UnbindLevel();
    }

    void BindLevel()
    {
        UnbindLevel();
        if (GetComponent<PlayerVitals>() == null)
            return;

        _level = PlayerLevel.EnsureExists();
        if (_level == null)
            return;

        _level.LevelChanged += OnLevelChanged;
    }

    void UnbindLevel()
    {
        if (_level == null)
            return;

        _level.LevelChanged -= OnLevelChanged;
        _level = null;
    }

    void OnLevelChanged(int _)
    {
        RefreshLevelLabel();
    }

    void LateUpdate()
    {
        if (bars == null)
            return;

        if (_forcedHidden)
        {
            HideBarsImmediate();
            return;
        }

        Transform root = bars.transform;
        Vector3 lossy = transform.lossyScale;
        float s = worldScale;
        root.localScale = new Vector3(
            ApproxInverse(lossy.x) * s,
            ApproxInverse(lossy.y) * s,
            ApproxInverse(lossy.z) * s);
        root.localPosition = localOffset;

        Camera cam = ResolveCamera();
        if (cam != null)
            root.rotation = cam.transform.rotation;

        UpdateDamageReveal();
    }

    void EnsureBars()
    {
        if (bars == null)
            bars = GetComponentInChildren<HpBarView>(true);

        if (bars == null)
        {
            if (hpBarPrefab == null)
            {
                Debug.LogWarning($"{name}: assign HpBar prefab on HpBar.", this);
                return;
            }

            bars = Instantiate(hpBarPrefab, transform);
            bars.name = hpBarPrefab.name;
            bars.transform.localPosition = localOffset;
            bars.transform.localRotation = Quaternion.identity;
            bars.transform.localScale = Vector3.one;
        }

        ApplyHealthColor();
    }

    void ApplyHealthColor()
    {
        if (bars == null || _source is not Creature)
            return;

        bars.SetHealthFillColor(hostileHealthColor);
    }

    void ApplyOxygenVisibility()
    {
        if (bars == null)
            return;

        // Player oxygen lives on the screen HUD, not above the character.
        bool aboveCharacter = _source != null && _source.HasOxygen && GetComponent<PlayerVitals>() == null;
        bars.SetOxygenVisible(aboveCharacter);
    }

    void RefreshBars()
    {
        if (bars == null || _source == null)
            return;

        if (_forcedHidden)
        {
            HideBarsImmediate();
            return;
        }

        ApplyOxygenVisibility();
        ApplyHealthColor();
        bars.SetHealthValues(_source.CurrentHealth, _source.MaxHealth);
        RefreshLevelLabel();

        if (_source.HasOxygen)
            bars.SetOxygenValues(_source.CurrentOxygen, _source.MaxOxygen);

        bool alive = !hideWhenDead || _source.IsAlive;
        if (!alive)
        {
            HideBarsImmediate();
            return;
        }

        if (HideUntilDamaged)
            return;

        ShowBarsImmediate();
    }

    void RefreshLevelLabel()
    {
        if (bars == null)
            return;

        if (_level != null)
        {
            bars.SetLevel(_level.Level);
            return;
        }

        if (_source is Creature creature && GetComponent<AreaKing>() != null)
        {
            bars.SetLevel(creature.Level);
            return;
        }

        bars.HideLevel();
    }

    void OnDamaged(float _)
    {
        if (bars == null)
            return;

        if (HideUntilDamaged)
            RevealBars();
    }

    void UpdateDamageReveal()
    {
        if (!HideUntilDamaged || bars == null)
            return;

        bool alive = _source == null || !hideWhenDead || _source.IsAlive;
        if (!alive)
        {
            HideBarsImmediate();
            return;
        }

        if (_revealTimer > 0f)
            _revealTimer -= Time.deltaTime;

        float target = _revealTimer > 0f ? 1f : 0f;
        if (fadeDuration <= 0f)
            _alpha = target;
        else
            _alpha = Mathf.MoveTowards(_alpha, target, Time.deltaTime / fadeDuration);

        bool show = _alpha > 0.001f || target > 0f;
        if (bars.gameObject.activeSelf != show)
            bars.gameObject.SetActive(show);

        if (show)
            bars.SetAlpha(_alpha);
    }

    void RevealBars()
    {
        _revealTimer = visibleAfterDamage > 0.05f ? visibleAfterDamage : 2.5f;
        _alpha = 1f;
        if (!bars.gameObject.activeSelf)
            bars.gameObject.SetActive(true);
        bars.SetAlpha(1f);
    }

    void ShowBarsImmediate()
    {
        _revealTimer = 0f;
        _alpha = 1f;
        if (bars == null)
            return;

        if (!bars.gameObject.activeSelf)
            bars.gameObject.SetActive(true);
        // HideBarsImmediate zeros alpha. Player bars do not use the damage-fade path, so
        // activating the object alone would leave them fully transparent.
        bars.SetAlpha(1f);
        EnsureBarRenderersEnabled();
    }

    void HideBarsImmediate()
    {
        _revealTimer = 0f;
        _alpha = 0f;
        if (bars == null)
            return;

        bars.SetAlpha(0f);
        if (bars.gameObject.activeSelf)
            bars.gameObject.SetActive(false);
    }

    void EnsureBarRenderersEnabled()
    {
        Renderer[] renderers = bars.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = true;
        }
    }

    static float ApproxInverse(float value)
    {
        return Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;
    }

    Camera ResolveCamera()
    {
        if (_camera != null)
            return _camera;

        _camera = Camera.main;
        if (_camera == null)
            _camera = FindAnyObjectByType<Camera>();

        return _camera;
    }
}
