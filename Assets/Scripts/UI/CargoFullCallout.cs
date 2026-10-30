using UnityEngine;

/// <summary>
/// Shows the authored "CARGO FULL" view above the player, then fades it out.
/// The words, colors, and arrow live on the CargoFull prefab.
/// </summary>
[DefaultExecutionOrder(1000)]
public class CargoFullCallout : MonoBehaviour
{
    [SerializeField] CanvasGroup canvasGroup;
    [Tooltip("How long the callout stays fully visible.")]
    [SerializeField, Min(0.1f)] float visibleSeconds = 2f;
    [Tooltip("Fade after the hold. Zero hides it immediately.")]
    [SerializeField, Min(0f)] float fadeSeconds = 0.2f;
    [Tooltip("Height above the player, along the planet up axis.")]
    [SerializeField] float headLift = 3.05f;

    [Header("Size")]
    [Tooltip("SceneBootstrap sets this from the planet HP bar scale.")]
    [SerializeField, Min(0.1f)] float worldScale = 1f;
    [SerializeField, Min(0.1f)] float worldWidth = 2.8f;
    [SerializeField, Min(1f)] float referencePixels = 250f;

    Canvas _canvas;
    PlayerVitals _vitals;
    PlayerCargo _cargo;
    float _age;
    int _lastUsed;
    bool _showing;

    public void SetWorldScale(float scale)
    {
        worldScale = Mathf.Max(0.1f, scale);
    }

    void Awake()
    {
        _canvas = GetComponent<Canvas>();
        _vitals = GetComponentInParent<PlayerVitals>();
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        HideImmediate();
    }

    void OnEnable()
    {
        _cargo = PlayerCargo.EnsureExists();
        _lastUsed = _cargo != null ? _cargo.Used : 0;
        if (_cargo == null)
            return;

        _cargo.Changed += OnChanged;
        _cargo.Rejected += OnRejected;
    }

    void OnDisable()
    {
        if (_cargo == null)
            return;

        _cargo.Changed -= OnChanged;
        _cargo.Rejected -= OnRejected;
    }

    void OnChanged()
    {
        if (_cargo == null)
            return;

        int used = _cargo.Used;
        bool justFilled = _lastUsed < _cargo.Capacity && used >= _cargo.Capacity;
        _lastUsed = used;
        if (!_cargo.IsFull)
        {
            HideImmediate();
            return;
        }

        if (justFilled)
            Show();
    }

    void OnRejected()
    {
        if (_cargo != null && _cargo.IsFull)
            Show();
    }

    void LateUpdate()
    {
        if (!_showing)
            return;

        if (!CanShow())
        {
            HideImmediate();
            return;
        }

        Follow();

        _age += Time.deltaTime;
        if (_age <= visibleSeconds)
        {
            SetAlpha(1f);
            return;
        }

        if (fadeSeconds <= 0.001f)
        {
            HideImmediate();
            return;
        }

        float fade = (_age - visibleSeconds) / fadeSeconds;
        if (fade >= 1f)
        {
            HideImmediate();
            return;
        }

        SetAlpha(1f - fade);
    }

    void Show()
    {
        if (!CanShow())
            return;

        _showing = true;
        _age = 0f;
        SetAlpha(1f);
        if (_canvas != null)
            _canvas.enabled = true;
        Follow();
    }

    bool CanShow()
    {
        if (_cargo == null || !_cargo.IsFull)
            return false;

        if (SphericalPlanet.Instance == null || Camera.main == null)
            return false;

        return _vitals == null || (_vitals.IsAlive && !_vitals.IsRecovering);
    }

    void Follow()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        Transform host = transform.parent != null ? transform.parent : transform;
        Vector3 up = host.up.sqrMagnitude > 0.0001f ? host.up.normalized : Vector3.up;
        transform.SetPositionAndRotation(host.position + up * headLift, cam.transform.rotation);

        float units = (worldWidth * worldScale) / referencePixels;
        Vector3 lossy = host.lossyScale;
        transform.localScale = new Vector3(
            SafeInv(lossy.x) * units,
            SafeInv(lossy.y) * units,
            SafeInv(lossy.z) * units);
    }

    void HideImmediate()
    {
        _showing = false;
        _age = 0f;
        SetAlpha(0f);
        if (_canvas != null)
            _canvas.enabled = false;
    }

    void SetAlpha(float alpha)
    {
        if (canvasGroup != null)
            canvasGroup.alpha = alpha;
    }

    static float SafeInv(float value)
    {
        return Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;
    }
}
