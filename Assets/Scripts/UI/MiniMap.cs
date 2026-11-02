using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Authored HUD widget. Flat terrain colors and opaque stage fog share a small CPU texture;
/// the player and pooled markers remain smooth without a second camera or render texture.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(1200)]
public sealed class MiniMap : MonoBehaviour
{
    [Header("Prefab references")]
    [SerializeField] RectTransform viewport;
    [SerializeField] RawImage terrainImage;
    [SerializeField] CanvasGroup visibility;
    [SerializeField] RectTransform playerMarker;
    [SerializeField] Image homeMarker;
    [Tooltip("Inactive template; copies are pooled once, underneath the home and player icons.")]
    [SerializeField] Image enemyTemplate;

    [Header("Map")]
    [SerializeField, Range(16f, 80f)] float viewRadius = 42f;
    [SerializeField, Range(64, 192)] int textureResolution = 128;
    [Tooltip("Maximum terrain uploads per second. Markers still move every frame.")]
    [SerializeField, Range(4f, 15f)] float terrainUpdatesPerSecond = 10f;
    [SerializeField] bool respectSafeArea = true;
    [SerializeField] MiniMapPalette palette = new MiniMapPalette();

    const int DotCapacity = 36;
    const float ArriveHide = 2.4f;
    const float MoveRebuild = .3f;
    const float TurnRebuild = .9995f;
    RectTransform _root;
    Canvas _canvas;
    Vector2 _authoredPosition;
    Image[] _dots;
    PlayerVitals _player;
    SphericalPlanet _planet;
    PlanetTileMap _tiles;
    PlanetStageProgression _progress;
    PlanetStageFog _fog;
    CampRadiusRing _camp;
    SphereCollider _campVolume;
    float _nextCampSearch, _nextPlayerSearch, _nextBake;
    Vector3 _up, _right, _forward, _playerDir, _center;
    Vector3 _bakedPos, _bakedUp, _bakedRight, _bakedForward;
    float _planetRadius, _inner;
    int _bakedVersion = -1, _bakedFogMask = -1;
    bool _hasBake, _hasArrow;
    float _arrowAngle, _arrowVelocity;
    MiniMapRaster _raster;
    readonly Creature[] _picked = new Creature[DotCapacity];
    readonly float[] _pickedArc = new float[DotCapacity];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureInScene()
    {
        if (FindAnyObjectByType<MiniMap>(FindObjectsInactive.Include) != null) return;
        var hud = FindAnyObjectByType<PlayerDeathUI>(FindObjectsInactive.Include);
        Canvas canvas = hud != null ? hud.GetComponentInChildren<Canvas>(true) : null;
        var prefab = Resources.Load<GameObject>("HUD/MiniMap/MiniMap");
        if (canvas != null && prefab != null) Instantiate(prefab, canvas.transform, false);
    }

    void Awake()
    {
        _root = transform as RectTransform;
        _canvas = GetComponentInParent<Canvas>();
        if (_root == null || viewport == null || terrainImage == null || visibility == null
            || playerMarker == null || homeMarker == null || enemyTemplate == null)
        {
            Debug.LogError("MiniMap needs the references authored in HUD/MiniMap/MiniMap.prefab.", this);
            enabled = false;
            return;
        }
        _authoredPosition = _root.anchoredPosition;
        visibility.alpha = 0f;
        visibility.blocksRaycasts = visibility.interactable = false;
        _raster = new MiniMapRaster(Mathf.Clamp(textureResolution, 64, 192));
        terrainImage.texture = _raster.Texture;
        _dots = new Image[DotCapacity];
        for (int i = 0; i < _dots.Length; i++)
        {
            Image dot = Instantiate(enemyTemplate, enemyTemplate.transform.parent, false);
            dot.name = "Enemy " + (i + 1);
            dot.transform.SetSiblingIndex(enemyTemplate.transform.GetSiblingIndex());
            dot.enabled = false;
            dot.gameObject.SetActive(true);
            _dots[i] = dot;
        }
        enemyTemplate.gameObject.SetActive(false);
    }

    void OnEnable() { _hasBake = _hasArrow = false; }
    void OnDisable()
    {
        if (visibility != null) visibility.alpha = 0f;
        if (_root != null) _root.anchoredPosition = _authoredPosition;
    }
    void OnDestroy() { _raster?.Dispose(); }
    void OnValidate() { _hasBake = false; _raster?.Invalidate(); }

    void LateUpdate()
    {
        bool show = TryBindFrame();
        visibility.alpha = show ? 1f : 0f;
        if (!show) { _hasArrow = _hasBake = false; return; }
        LayoutCorner();
        BakeIfNeeded();
        PlaceMarkers();
    }

    bool TryBindFrame()
    {
        if (_player == null && Time.unscaledTime >= _nextPlayerSearch)
        {
            _player = FindAnyObjectByType<PlayerVitals>();
            _nextPlayerSearch = Time.unscaledTime + 1f;
        }
        SphericalPlanet planet = SphericalPlanet.Instance;
        Camera camera = Camera.main;
        if (_player == null || !_player.isActiveAndEnabled || !_player.IsAlive || planet == null || camera == null)
            return false;
        if (_planet != planet)
        {
            _planet = planet;
            _tiles = planet.GetComponent<PlanetTileMap>();
            _progress = planet.GetComponent<PlanetStageProgression>();
            _fog = planet.GetComponent<PlanetStageFog>();
            _hasBake = false;
            _raster.Invalidate();
        }
        _center = planet.Center;
        _planetRadius = Mathf.Max(1f, planet.Radius);
        _up = _playerDir = planet.GetUpAt(_player.transform.position);
        _forward = Vector3.ProjectOnPlane(camera.transform.forward, _up);
        if (_forward.sqrMagnitude < .0001f) _forward = Vector3.ProjectOnPlane(camera.transform.up, _up);
        if (_forward.sqrMagnitude < .0001f) return false;
        _forward.Normalize();
        _right = Vector3.Cross(_up, _forward).normalized;
        _inner = Mathf.Min(viewport.rect.width, viewport.rect.height);
        return _tiles != null && _tiles.HasValidMap();
    }

    void LayoutCorner()
    {
        // Keep the prefab's placement editable; only apply the device's right/top cutout.
        Vector2 inset = Vector2.zero;
        if (respectSafeArea && _canvas != null && Screen.width > 0 && Screen.height > 0)
        {
            var canvasRect = _canvas.rootCanvas.transform as RectTransform;
            Rect safe = Screen.safeArea;
            inset = new Vector2((Screen.width - safe.xMax) / Screen.width * canvasRect.rect.width,
                (Screen.height - safe.yMax) / Screen.height * canvasRect.rect.height);
        }
        Vector2 position = _authoredPosition - inset;
        if (_root.anchoredPosition != position) _root.anchoredPosition = position;
    }

    int FogMask()
    {
        int mask = _fog != null ? _fog.CoveredStagesMask : 0;
        if (_progress != null)
            for (int stage = 2; stage <= 4; stage++)
                if (!_progress.IsUnlocked((PlanetStageId)stage)) mask |= 1 << stage;
        return mask;
    }

    void BakeIfNeeded()
    {
        int mask = FogMask();
        bool worldChanged = _tiles.SurfaceVersion != _bakedVersion || mask != _bakedFogMask;
        Vector3 position = _player.transform.position;
        bool moved = (position - _bakedPos).sqrMagnitude > MoveRebuild * MoveRebuild;
        bool turned = Vector3.Dot(_forward, _bakedForward) < TurnRebuild;
        if (_hasBake && !worldChanged && ((!moved && !turned) || Time.unscaledTime < _nextBake)) return;
        _raster.Bake(_tiles, _progress, mask, palette, _center, _playerDir, _right, _forward, _planetRadius, viewRadius);
        _bakedPos = position;
        _bakedUp = _playerDir;
        _bakedRight = _right;
        _bakedForward = _forward;
        _bakedVersion = _tiles.SurfaceVersion;
        _bakedFogMask = mask;
        _hasBake = true;
        _nextBake = Time.unscaledTime + 1f / Mathf.Max(1f, terrainUpdatesPerSecond);
    }

    bool IsMarkerVisible(Vector3 position)
    {
        // Stage ownership also covers ocean; hidden encounters must never leak through fog.
        if (_progress != null && (_bakedFogMask & (1 << (int)_progress.StageAt(position))) != 0) return false;
        return !TrySurfacePoint(position, out Vector2 point, out _) || !IsFogAtPoint(point);
    }

    bool IsFogAtPoint(Vector2 point) => _inner > 0f && _raster.IsFogAt(point / (_inner * .5f));
    void PlaceMarkers()
    {
        Vector3 face = Vector3.ProjectOnPlane(_player.transform.forward, _up);
        if (face.sqrMagnitude < 0.0001f)
            face = _forward;
        face.Normalize();
        float x = Vector3.Dot(face, _bakedRight);
        float y = Vector3.Dot(face, _bakedForward);
        float target = -Mathf.Atan2(x, y) * Mathf.Rad2Deg;
        if (!_hasArrow)
        {
            _arrowAngle = target;
            _arrowVelocity = 0f;
            _hasArrow = true;
        }
        else if (Time.unscaledDeltaTime > 0f)
        {
            _arrowAngle = Mathf.SmoothDampAngle(_arrowAngle, target, ref _arrowVelocity, 0.07f, Mathf.Infinity, Time.unscaledDeltaTime);
        }

        TrySurfacePoint(_player.transform.position, out Vector2 playerPoint, out _);
        playerMarker.anchoredPosition = playerPoint;
        playerMarker.localRotation = Quaternion.Euler(0f, 0f, _arrowAngle);

        PlaceFoes();
        PlaceHome();
    }

    void PlaceFoes()
    {
        var creatures = Creature.ActiveCreatures;
        int shown = 0;
        var picked = _picked;
        var arcs = _pickedArc;
        for (int i = 0; i < creatures.Count; i++)
        {
            Creature creature = creatures[i];
            if (creature == null || !creature.IsAlive || !IsMarkerVisible(creature.transform.position))
                continue;
            if (!TrySurfacePoint(creature.transform.position, out Vector2 point, out float arc))
                continue;
            if (arc > viewRadius)
                continue;

            if (shown < picked.Length)
            {
                picked[shown] = creature;
                arcs[shown] = arc;
                shown++;
                continue;
            }

            int farthest = 0;
            for (int s = 1; s < shown; s++)
            {
                if (arcs[s] > arcs[farthest])
                    farthest = s;
            }

            if (arc >= arcs[farthest])
                continue;

            picked[farthest] = creature;
            arcs[farthest] = arc;
        }

        float limit = _inner * 0.5f;
        for (int i = 0; i < _dots.Length; i++)
        {
            bool on = i < shown;
            if (_dots[i].enabled != on)
                _dots[i].enabled = on;
            if (!on)
                continue;

            if (!TrySurfacePoint(picked[i].transform.position, out Vector2 point, out _)
                || point.sqrMagnitude > limit * limit)
            {
                _dots[i].enabled = false;
                continue;
            }

            _dots[i].rectTransform.anchoredPosition = point;
        }
    }

    void PlaceHome()
    {
        Image house = homeMarker;
        if (!ResolveCamp() || !IsMarkerVisible(_campVolume.bounds.center) || !TrySurfacePoint(_campVolume.bounds.center, out Vector2 point, out float arc))
        {
            house.enabled = false;
            return;
        }

        if (arc <= ArriveHide)
        {
            house.enabled = false;
            return;
        }

        float reach = _inner * 0.5f - homeMarker.rectTransform.rect.width * 0.55f;
        if (point.sqrMagnitude > reach * reach)
            point = point.normalized * reach;

        house.enabled = !IsFogAtPoint(point);
        homeMarker.rectTransform.anchoredPosition = point;
        homeMarker.rectTransform.localRotation = Quaternion.identity;
    }

    bool TrySurfacePoint(Vector3 world, out Vector2 ui, out float arc)
    {
        ui = Vector2.zero;
        arc = 0f;
        Vector3 from = world - _center;
        if (from.sqrMagnitude < 0.0001f)
            return false;

        Vector3 dir = from.normalized;
        float angle = Vector3.Angle(_bakedUp, dir) * Mathf.Deg2Rad;
        arc = angle * _planetRadius;
        Vector3 bearing = Vector3.ProjectOnPlane(dir, _bakedUp);
        if (bearing.sqrMagnitude < 0.000001f)
            return false;

        bearing.Normalize();
        float scale = (_inner * 0.5f) / viewRadius;
        ui = new Vector2(Vector3.Dot(bearing, _bakedRight), Vector3.Dot(bearing, _bakedForward)) * (arc * scale);
        return true;
    }

    bool ResolveCamp()
    {
        SphericalPlanet planet = SphericalPlanet.Instance;
        if (_camp != null && _camp.isActiveAndEnabled && _campVolume != null && _campVolume.enabled
            && planet != null && _camp.GetComponentInParent<SphericalPlanet>() == planet)
            return true;

        if (Time.unscaledTime < _nextCampSearch)
            return false;

        _nextCampSearch = Time.unscaledTime + 1f;
        _camp = null;
        _campVolume = null;
        if (_player == null || planet == null)
            return false;

        float nearest = float.PositiveInfinity;
        foreach (CampRadiusRing candidate in FindObjectsByType<CampRadiusRing>(FindObjectsInactive.Exclude))
        {
            if (!candidate.isActiveAndEnabled || candidate.GetComponentInParent<SphericalPlanet>() != planet)
                continue;

            SphereCollider volume = candidate.GetComponent<SphereCollider>();
            if (volume == null || !volume.enabled)
                continue;

            float distance = (volume.bounds.center - _player.transform.position).sqrMagnitude;
            if (distance >= nearest)
                continue;

            nearest = distance;
            _camp = candidate;
            _campVolume = volume;
        }

        return _camp != null;
    }

}
