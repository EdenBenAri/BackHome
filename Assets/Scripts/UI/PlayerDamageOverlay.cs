using UnityEngine;

/// <summary>
/// Fullscreen red vignette when the player is hit. Uses
/// <c>BackHome/Hud/PlayerDamageOverlay</c>: dissolves in, holds, dissolves out.
/// Lives on the Hud and binds to the player at runtime.
/// </summary>
public class PlayerDamageOverlay : MonoBehaviour
{
    const string ResourcesPath = "Player/Combat/PlayerDamageOverlay";

    static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

    enum Phase
    {
        Hidden,
        In,
        Hold,
        Out
    }

    [SerializeField] Material overlayMaterial;
    [SerializeField, Min(0.05f)] float dissolveIn = 0.16f;
    [SerializeField, Min(0.05f)] float hold = 0.5f;
    [SerializeField, Min(0.05f)] float dissolveOut = 0.22f;

    [Header("Camera Shake")]
    [SerializeField, Min(0f)] float shakeDuration = 0.12f;
    [SerializeField, Min(0f)] float shakeMagnitude = 0.12f;

    PlayerVitals _vitals;
    CameraFollow _follow;
    Material _runtime;
    Transform _overlay;
    MeshRenderer _renderer;
    Camera _camera;
    float _lastFov = -1f;
    float _lastAspect = -1f;
    float _phaseTime;
    float _dissolve;
    Phase _phase = Phase.Hidden;

    void Awake()
    {
        EnsureMaterial();
    }

    void Start()
    {
        TryBindPlayer();
    }

    void Update()
    {
        if (_vitals == null)
            TryBindPlayer();
    }

    void OnDisable()
    {
        UnbindPlayer();
        HideImmediate();
    }

    void OnDestroy()
    {
        UnbindPlayer();
        if (_overlay != null)
        {
            Destroy(_overlay.gameObject);
            _overlay = null;
            _renderer = null;
        }

        if (_runtime != null)
        {
            Destroy(_runtime);
            _runtime = null;
        }
    }

    void LateUpdate()
    {
        if (_phase == Phase.Hidden)
            return;

        StepPhase(Time.deltaTime);
        FitToCamera();
        ApplyDissolve();
    }

    void TryBindPlayer()
    {
        if (_vitals != null)
            return;

        GameObject playerGo = GameObject.FindGameObjectWithTag("Player");
        if (playerGo == null)
            return;

        _vitals = playerGo.GetComponent<PlayerVitals>();
        if (_vitals == null)
            _vitals = playerGo.GetComponentInChildren<PlayerVitals>();
        if (_vitals == null)
            return;

        _vitals.Damaged += OnDamaged;
        _vitals.Died += HideImmediate;
    }

    void UnbindPlayer()
    {
        if (_vitals == null)
            return;

        _vitals.Damaged -= OnDamaged;
        _vitals.Died -= HideImmediate;
        _vitals = null;
    }

    void OnDamaged(float _)
    {
        ShakeCamera();

        if (_runtime == null && !EnsureMaterial())
            return;

        EnsureOverlay();
        if (_overlay != null)
            _overlay.gameObject.SetActive(true);
        if (_renderer != null)
            _renderer.enabled = true;

        if (_phase == Phase.Hold)
        {
            _phaseTime = 0f;
            return;
        }

        _phaseTime = _dissolve * dissolveIn;
        _phase = Phase.In;
    }

    void ShakeCamera()
    {
        if (shakeDuration <= 0f || shakeMagnitude <= 0f)
            return;

        if (_follow == null)
            _follow = FindAnyObjectByType<CameraFollow>();
        if (_follow == null && Camera.main != null)
            _follow = Camera.main.GetComponent<CameraFollow>();
        if (_follow == null || !_follow.isActiveAndEnabled)
            return;

        _follow.Shake(shakeDuration, shakeMagnitude);
    }

    void StepPhase(float dt)
    {
        _phaseTime += dt;
        if (_phase == Phase.In)
        {
            float t = Mathf.Clamp01(_phaseTime / dissolveIn);
            _dissolve = Smooth(t);
            if (t >= 1f)
            {
                _dissolve = 1f;
                _phase = Phase.Hold;
                _phaseTime = 0f;
            }
        }
        else if (_phase == Phase.Hold)
        {
            _dissolve = 1f;
            if (_phaseTime >= hold)
            {
                _phase = Phase.Out;
                _phaseTime = 0f;
            }
        }
        else if (_phase == Phase.Out)
        {
            float t = Mathf.Clamp01(_phaseTime / dissolveOut);
            _dissolve = 1f - Smooth(t);
            if (t >= 1f)
                HideImmediate();
        }
    }

    void HideImmediate()
    {
        _phase = Phase.Hidden;
        _dissolve = 0f;
        _phaseTime = 0f;
        ApplyDissolve();
        if (_renderer != null)
            _renderer.enabled = false;
        if (_overlay != null && _overlay.gameObject != null)
            _overlay.gameObject.SetActive(false);
    }

    void ApplyDissolve()
    {
        if (_runtime != null)
            _runtime.SetFloat(DissolveId, _dissolve);
    }

    bool EnsureMaterial()
    {
        if (_runtime != null)
            return true;

        Material source = overlayMaterial;
        if (source == null)
            source = Resources.Load<Material>(ResourcesPath);
        if (source == null)
            source = Resources.Load<Material>("Player/Combat/Shaders/PlayerDamageOverlay");
        if (source == null)
        {
            Debug.LogWarning($"{name}: PlayerDamageOverlay material missing at Resources/{ResourcesPath}.", this);
            return false;
        }

        _runtime = new Material(source);
        _runtime.name = source.name + " (Runtime)";
        overlayMaterial = source;
        return true;
    }

    void EnsureOverlay()
    {
        Camera cam = ResolveCamera();
        if (cam == null)
            return;

        if (_overlay == null)
        {
            var go = new GameObject("PlayerDamageOverlay");
            _overlay = go.transform;
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = CameraOverlayQuad.Create("PlayerDamageOverlayQuad");
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.allowOcclusionWhenDynamic = false;
            _renderer.sharedMaterial = _runtime;
        }

        if (_overlay != null && cam != null && _overlay.parent != cam.transform)
            _overlay.SetParent(cam.transform, false);

        _overlay.localRotation = Quaternion.identity;
        FitToCamera(force: true);
    }

    void FitToCamera(bool force = false)
    {
        CameraOverlayQuad.Fit(_overlay, ResolveCamera(), ref _lastFov, ref _lastAspect, 0.02f, force);
    }

    Camera ResolveCamera()
    {
        return CameraOverlayQuad.Resolve(ref _camera);
    }

    static float Smooth(float t)
    {
        return t * t * (3f - 2f * t);
    }
}
