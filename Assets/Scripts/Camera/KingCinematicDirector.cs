using StarterAssets;
using UnityEngine;
using Unity.Profiling;

/// <summary>
/// Chapter king reveals run on real time. Gameplay freezes, but the camera and the
/// featured king's Spawn state continue; every exit restores the state we borrowed.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CameraFollow), typeof(Camera))]
[DefaultExecutionOrder(100)]
public sealed class KingCinematicDirector : MonoBehaviour
{
    [SerializeField] KingCinematicSettings settings;
    public KingCinematicSettings Settings => settings;
    bool _ownsSettings;
    static readonly ProfilerMarker TickMarker = new("BackHome.KingCinematic.Tick");

    void EnsureSettings()
    {
        if (settings != null) return;
        settings = ScriptableObject.CreateInstance<KingCinematicSettings>();
        settings.hideFlags = HideFlags.HideAndDontSave;
        _ownsSettings = true;
    }

    void OnDestroy()
    {
        if (!_ownsSettings || settings == null) return;
        if (Application.isPlaying) Destroy(settings);
        else DestroyImmediate(settings);
    }

    public enum Phase { Idle, IntroGrace, SlowMotion, Turn, ZoomIn, SpawnHold, ZoomOut }
    public Phase CurrentPhase { get; private set; }
    public bool IsPresenting => CurrentPhase != Phase.Idle && CurrentPhase != Phase.IntroGrace;

    struct Shot
    {
        public Vector3 position;
        public Quaternion rotation;
        public Shot(Vector3 position, Quaternion rotation) { this.position = position; this.rotation = rotation; }
    }

    CreatureSpawner _spawner;
    CameraFollow _follow;
    Camera _camera;
    Transform _player;
    PlayerVitals _vitals;
    StarterAssetsInputs _input;
    SphericalPlanet _planet;
    Creature _featured;
    CreatureAnimator _spawnAnimation;
    GameObject _featuredRoot;
    bool _introScheduled, _introClockStarted, _ownsTime, _ownsCamera, _inputWasBlocked;
    float _elapsed, _holdDuration, _savedTimeScale, _savedFixedDelta, _writtenScale, _writtenFixedDelta;
    float _savedFov, _turnAngle;
    Vector3 _heading, _playerUp;
    Shot _from, _close, _return;

    public void Bind(CreatureSpawner spawner, Transform player)
    {
        if (_spawner == spawner && _player == player) return;
        EnsureSettings();
        Cancel();
        Unsubscribe();
        _spawner = spawner;
        _player = player;
        _follow = GetComponent<CameraFollow>();
        _camera = GetComponent<Camera>();
        _planet = spawner != null ? spawner.GetComponentInParent<SphericalPlanet>() : null;
        if (_planet == null) _planet = SphericalPlanet.Instance;
        _input = player != null ? player.GetComponent<StarterAssetsInputs>() : null;
        _vitals = player != null ? player.GetComponent<PlayerVitals>() : null;
        Subscribe();
        if (!_introScheduled && spawner != null && player != null && settings.playIntroduction)
        {
            _introScheduled = true;
            Enter(Phase.IntroGrace);
        }
    }

    void Subscribe()
    {
        if (_spawner != null)
        {
            _spawner.KingDefeatedFirstTime += OnFirstSavedVictory;
            _spawner.KingDefeatedThisVisit += OnFirstVisitVictory;
        }
        if (_vitals != null) _vitals.Died += Cancel;
    }

    void Unsubscribe()
    {
        if (_spawner != null)
        {
            _spawner.KingDefeatedFirstTime -= OnFirstSavedVictory;
            _spawner.KingDefeatedThisVisit -= OnFirstVisitVictory;
        }
        if (_vitals != null) _vitals.Died -= Cancel;
    }

    void OnEnable() { if (_spawner != null) Subscribe(); }
    void OnDisable() { Unsubscribe(); Cancel(); }

    void OnFirstSavedVictory()
    {
        if (!settings.replayVictoriesOnChapterEntry) OnVictory();
    }

    void OnFirstVisitVictory()
    {
        if (settings.replayVictoriesOnChapterEntry) OnVictory();
    }

    void OnVictory()
    {
        // Died is dispatched synchronously by the lethal hit, not by the death animation.
        // A second kill during this beat is included when we resolve the next living objective.
        if (IsPresenting || !CanRun() || Time.timeScale <= 0f || !settings.playVictoryReveals) return;
        AcquireTimeAndInput();
        SetTimeScale(_savedTimeScale * settings.slowMotionScale);
        Enter(Phase.SlowMotion);
    }

    bool CanRun() => _player != null && _spawner != null && _follow != null && _follow.enabled
        && (_vitals == null || _vitals.IsAlive);

    void Update()
    {
        if (CurrentPhase == Phase.Idle) return;
        using var sample = TickMarker.Auto();
        if (CurrentPhase == Phase.IntroGrace)
        {
            // Scene import/shader warm-up can leave a >1s first delta. It isn't playable time.
            if (!_introClockStarted) { _introClockStarted = true; return; }
            Tick(Mathf.Min(Time.unscaledDeltaTime, settings.maximumIntroFrameStep));
        }
        else Tick(Time.unscaledDeltaTime);
    }

    void Tick(float realDelta)
    {
        if (CurrentPhase == Phase.Idle) return;
        if (!CanRun()) { Cancel(); return; }
        // A death/pause system that takes over time also takes precedence over the reveal.
        if (_ownsTime && !Mathf.Approximately(Time.timeScale, _writtenScale)) { Cancel(); return; }
        if (CurrentPhase == Phase.IntroGrace && Time.timeScale <= 0f) return;
        if (_ownsCamera && (_featured == null || !_featured.IsAlive)) { Cancel(); return; }
        _elapsed += Mathf.Max(0f, realDelta);
        switch (CurrentPhase)
        {
            case Phase.IntroGrace:
                if (_elapsed < settings.initialFreeMovement) break;
                if (_spawner.TryGetKingForPresentation(settings.firstKingArea, out var first))
                {
                    AcquireTimeAndInput();
                    StartPresentation(first, false);
                }
                // Allow delayed spawners to finish without ever freezing on an absent target.
                else if (_elapsed >= settings.initialFreeMovement + settings.spawnAvailabilityTimeout) Cancel();
                break;
            case Phase.SlowMotion:
                if (_elapsed < settings.slowMotionDuration) break;
                if (_spawner.TryGetNextKingForPresentation(out var next, settings.replayVictoriesOnChapterEntry))
                    StartPresentation(next, true);
                else Cancel(); // Last king: finish the victory beat and return control.
                break;
            case Phase.Turn:
                float turn = Ease(_elapsed / Mathf.Max(.01f, settings.turnDuration));
                Quaternion orbit = Quaternion.AngleAxis(_turnAngle * turn, _playerUp);
                transform.SetPositionAndRotation(_player.position + orbit * (_from.position - _player.position),
                    orbit * _from.rotation);
                if (_elapsed >= settings.turnDuration)
                {
                    Apply(_return);
                    _from = _return;
                    Enter(Phase.ZoomIn);
                }
                break;
            case Phase.ZoomIn:
                Apply(BlendShot(_from, _close, Ease(_elapsed / Mathf.Max(.01f, settings.zoomInDuration))));
                if (_elapsed >= settings.zoomInDuration)
                {
                    float clipDuration = _spawnAnimation != null ? _spawnAnimation.BeginCinematicSpawn() : 0f;
                    _holdDuration = Mathf.Max(.5f, Mathf.Max(settings.minimumSpawnHold, clipDuration));
                    Enter(Phase.SpawnHold);
                }
                break;
            case Phase.SpawnHold:
                Apply(_close);
                if (_elapsed >= _holdDuration)
                {
                    _spawnAnimation?.EndCinematicSpawn();
                    Enter(Phase.ZoomOut);
                }
                break;
            case Phase.ZoomOut:
                Apply(BlendShot(_close, _return, Ease(_elapsed / Mathf.Max(.01f, settings.zoomOutDuration))));
                if (_elapsed >= settings.zoomOutDuration) Cancel();
                break;
        }
    }

    void StartPresentation(Creature king, bool turn)
    {
        SetTimeScale(0f);
        _featured = king;
        AreaKing marker = king.GetComponentInParent<AreaKing>();
        _featuredRoot = marker != null ? marker.gameObject : king.gameObject;
        PlanetEnvironmentStreamer.SetCinematicVisible(_featuredRoot, true);
        marker?.FacePlayer(_player);
        _spawnAnimation = _featuredRoot.GetComponentInChildren<CreatureAnimator>(true);
        _follow.BeginCinematic();
        _ownsCamera = true;
        _savedFov = _camera.fieldOfView;
        _from = new Shot(transform.position, transform.rotation);
        _playerUp = UpAt(_player.position, _player.up);
        Vector3 oldHeading = Tangent(transform.forward, _playerUp);
        _heading = Tangent(king.transform.position - _player.position, _playerUp);
        _turnAngle = Vector3.SignedAngle(oldHeading, _heading, _playerUp);

        // Orbit at the gameplay distance, then fly toward the king along the planet's surface.
        float height = Mathf.Max(1f, Vector3.Dot(_from.position - _player.position, _playerUp));
        float back = Mathf.Max(1f, Vector3.ProjectOnPlane(_from.position - _player.position, _playerUp).magnitude);
        _return = LookShot(_player.position + _playerUp * height - _heading * back, _player.position, _playerUp);
        Vector3 kingUp = UpAt(king.transform.position, king.transform.up);
        Vector3 kingHeading = Quaternion.FromToRotation(_playerUp, kingUp) * _heading;
        float size = settings.fitKingToScreen ? MeasureKingRadius(_featuredRoot) : settings.fallbackBodyRadius;
        float halfView = Mathf.Tan(_camera.fieldOfView * Mathf.Deg2Rad * 0.5f) * Mathf.Min(1f, _camera.aspect);
        float distance = settings.fitKingToScreen
            ? Mathf.Max(settings.closeShotBack, size * settings.framingPadding / Mathf.Max(.1f, halfView)) : settings.closeShotBack;
        float closeHeight = Mathf.Max(settings.closeShotHeight, size * settings.heightPerBodyRadius);
        Vector3 focus = king.transform.position + kingUp * Mathf.Max(settings.minimumFocusHeight, size * settings.focusHeightPerBodyRadius);
        _close = LookShot(focus + kingUp * closeHeight - kingHeading * distance, focus, kingUp);
        Enter(turn ? Phase.Turn : Phase.ZoomIn);
    }

    void AcquireTimeAndInput()
    {
        _savedTimeScale = Time.timeScale;
        _savedFixedDelta = Time.fixedDeltaTime;
        _ownsTime = true;
        _writtenScale = _savedTimeScale;
        _writtenFixedDelta = _savedFixedDelta;
        if (_input != null)
        {
            _inputWasBlocked = _input.GameplayBlocked;
            _input.SetGameplayBlocked(true);
        }
    }

    void SetTimeScale(float scale)
    {
        _writtenScale = scale;
        Time.timeScale = scale;
        _writtenFixedDelta = scale > 0f
            ? _savedFixedDelta * scale / Mathf.Max(0.01f, _savedTimeScale) : _savedFixedDelta;
        Time.fixedDeltaTime = _writtenFixedDelta;
        // Unity clamps/quantizes this property; compare against the value it accepted on cleanup.
        _writtenFixedDelta = Time.fixedDeltaTime;
    }

    /// <summary>Safe for death, disable, scene unload, missing targets and normal completion.</summary>
    public void Cancel()
    {
        _spawnAnimation?.EndCinematicSpawn();
        _spawnAnimation = null;
        if (_featuredRoot != null) PlanetEnvironmentStreamer.SetCinematicVisible(_featuredRoot, false);
        _featured = null;
        _featuredRoot = null;
        if (_ownsCamera)
        {
            Apply(_return);
            if (_camera != null) _camera.fieldOfView = _savedFov;
            _follow.EndCinematic(_heading);
            _ownsCamera = false;
        }
        if (_ownsTime)
        {
            if (Mathf.Approximately(Time.timeScale, _writtenScale)) Time.timeScale = _savedTimeScale;
            if (Mathf.Approximately(Time.fixedDeltaTime, _writtenFixedDelta)) Time.fixedDeltaTime = _savedFixedDelta;
            if (_input != null) _input.SetGameplayBlocked(_inputWasBlocked);
            _ownsTime = false;
        }
        Enter(Phase.Idle);
    }

    void Enter(Phase phase) { CurrentPhase = phase; _elapsed = 0f; }
    void Apply(Shot shot) => transform.SetPositionAndRotation(shot.position, shot.rotation);
    Vector3 UpAt(Vector3 point, Vector3 fallback) => _planet != null ? _planet.GetUpAt(point) : fallback.normalized;
    static Vector3 Tangent(Vector3 direction, Vector3 up)
    {
        Vector3 result = Vector3.ProjectOnPlane(direction, up);
        if (result.sqrMagnitude < 0.001f)
            result = Vector3.ProjectOnPlane(Mathf.Abs(up.z) < 0.9f ? Vector3.forward : Vector3.right, up);
        return result.normalized;
    }
    float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return settings.movementEase != null ? Mathf.Clamp01(settings.movementEase.Evaluate(t)) : t * t * (3f - 2f * t);
    }
    static Shot LookShot(Vector3 position, Vector3 focus, Vector3 up) =>
        new Shot(position, Quaternion.LookRotation(focus - position, up));

    Shot BlendShot(Shot from, Shot to, float t)
    {
        Vector3 position;
        if (_planet != null)
        {
            Vector3 a = from.position - _planet.Center, b = to.position - _planet.Center;
            // A chord can pass underground. Interpolate directions and altitude separately.
            position = _planet.Center + Vector3.Slerp(a.normalized, b.normalized, t)
                * Mathf.Lerp(a.magnitude, b.magnitude, t);
        }
        else position = Vector3.Lerp(from.position, to.position, t);
        return new Shot(position, Quaternion.Slerp(from.rotation, to.rotation, t));
    }

    float MeasureKingRadius(GameObject root)
    {
        // Use only body meshes; glows, range rings and particle bounds can be enormous.
        float radius = settings.fallbackBodyRadius;
        foreach (Renderer body in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!ToonOutlineSubjects.IsBody(body, root.transform)) continue;
            Bounds bounds = body is SkinnedMeshRenderer skin && skin.sharedMesh != null
                ? skin.sharedMesh.bounds : body.localBounds;
            Vector3 size = Vector3.Scale(bounds.extents, body.transform.lossyScale);
            radius = Mathf.Max(radius, size.magnitude);
        }
        return radius;
    }
}
