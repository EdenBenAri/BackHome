using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Circular local map in the top-right corner, in the Skull Hero style.
/// Thick navy bezel, soft painted ground, a green facing arrow at the center,
/// red dots for living creatures, and a white house for camp.
/// The level and coin chips drop to sit just under the disc.
/// The player stays in the middle. Up on the disc is up on the screen.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1200)]
public class MiniMap : MonoBehaviour
{
    const float Diameter = 208f;
    const float Border = 16f;
    const float ScreenMargin = 28f;
    const float XpGap = 12f;
    const float ViewRadius = 42f;
    const float ArriveHide = 2.4f;
    const int Resolution = 128;
    const int DotCapacity = 36;
    const float DotSize = 8f;
    const float HouseSize = 20f;
    const float ArrowSize = 22f;
    const float WaterLine = 0.02f;

    const float MoveRebuild = 0.12f;
    const float TurnRebuild = 0.9995f;

    static readonly Color BezelColor = new Color(0.055f, 0.078f, 0.137f, 1f);
    static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.42f);
    static readonly Color DotColor = new Color(0.90f, 0.16f, 0.14f, 1f);
    static readonly Color GrassDark = new Color(0.30f, 0.58f, 0.20f, 1f);
    static readonly Color GrassLight = new Color(0.56f, 0.84f, 0.33f, 1f);
    static readonly Color Canopy = new Color(0.20f, 0.46f, 0.16f, 1f);
    static readonly Color CanopyLight = new Color(0.34f, 0.60f, 0.22f, 1f);
    static readonly Color DirtDark = new Color(0.62f, 0.44f, 0.22f, 1f);
    static readonly Color DirtLight = new Color(0.81f, 0.62f, 0.35f, 1f);
    static readonly Color SandDark = new Color(0.81f, 0.62f, 0.35f, 1f);
    static readonly Color SandLight = new Color(0.99f, 0.81f, 0.50f, 1f);
    static readonly Color VoidDark = new Color(0.45f, 0.20f, 0.45f, 1f);
    static readonly Color VoidLight = new Color(0.64f, 0.29f, 0.64f, 1f);
    static readonly Color CrimsonDark = new Color(0.55f, 0.06f, 0.08f, 1f);
    static readonly Color CrimsonLight = new Color(1f, 0.30f, 0.33f, 1f);
    static readonly Color MushroomDark = new Color(0f, 0.22f, 0.22f, 1f);
    static readonly Color MushroomMid = new Color(0f, 0.39f, 0.39f, 1f);
    static readonly Color MushroomLight = new Color(0f, 0.50f, 0.50f, 1f);
    static readonly Color ShoreColor = new Color(0.91f, 0.80f, 0.55f, 1f);
    static readonly Color ShallowColor = new Color(0.36f, 0.70f, 0.88f, 1f);
    static readonly Color DeepColor = new Color(0.16f, 0.46f, 0.78f, 1f);

    RectTransform _root;
    RectTransform _arrow;
    RectTransform _house;
    RawImage _map;
    Image[] _dots;
    Canvas _canvas;
    CanvasGroup _group;
    RectTransform _xp;
    RectTransform _coin;
    Vector2 _xpHome;
    Vector2 _coinHome;
    bool _rowStored;
    bool _rowShifted;

    Texture2D _texture;
    Color32[] _pixels;
    Color32[] _swap;
    Sprite _circle;
    Sprite _softCircle;
    Sprite _arrowSprite;
    Sprite _houseSprite;
    Sprite _dotSprite;

    PlayerVitals _player;
    CampRadiusRing _camp;
    SphereCollider _campVolume;
    float _nextCampSearch;

    Vector3 _up;
    Vector3 _right;
    Vector3 _forward;
    Vector3 _playerDir;
    Vector3 _center;
    float _planetRadius;
    float _inner;

    Vector3 _bakedPos;
    Vector3 _bakedForward;
    int _bakedVersion = -1;
    bool _hasBake;

    float _arrowAngle;
    float _arrowVelocity;
    bool _hasArrow;

    readonly Creature[] _picked = new Creature[DotCapacity];
    readonly float[] _pickedArc = new float[DotCapacity];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureInScene()
    {
        if (FindAnyObjectByType<MiniMap>(FindObjectsInactive.Include) != null)
            return;

        PlayerDeathUI hud = FindAnyObjectByType<PlayerDeathUI>(FindObjectsInactive.Include);
        if (hud == null)
            return;

        hud.gameObject.AddComponent<MiniMap>();
    }

    void Awake()
    {
        Build();
    }

    void OnDestroy()
    {
        RestoreRow();
        DestroySprite(_circle);
        DestroySprite(_softCircle);
        DestroySprite(_arrowSprite);
        DestroySprite(_houseSprite);
        DestroySprite(_dotSprite);
        DestroyAsset(_texture);
    }

    void LateUpdate()
    {
        if (_root == null && !Build())
            return;

        bool show = TryBindFrame();
        _group.alpha = show ? 1f : 0f;
        if (!show)
        {
            _hasArrow = false;
            _hasBake = false;
            RestoreRow();
            return;
        }

        LayoutCorner();
        BakeIfNeeded();
        PlaceMarkers();
    }

    bool Build()
    {
        if (_root != null)
            return true;

        _canvas = GetComponentInChildren<Canvas>(true);
        if (_canvas == null)
            return false;

        _circle = CircleSprite(128, false);
        _softCircle = CircleSprite(128, true);
        _arrowSprite = ArrowSprite(64);
        _houseSprite = HouseSprite(64);
        _dotSprite = DotSprite(32);

        Transform parent = _canvas.transform;
        Transform xp = FindNamed(parent, "XpBar");
        if (xp != null)
            parent = xp.parent;
        _xp = xp as RectTransform;

        var rootGo = new GameObject("MiniMap", typeof(RectTransform), typeof(CanvasGroup));
        rootGo.layer = 5;
        rootGo.transform.SetParent(parent, false);
        _root = rootGo.GetComponent<RectTransform>();
        _root.anchorMin = _root.anchorMax = new Vector2(1f, 1f);
        _root.pivot = new Vector2(1f, 1f);
        _root.sizeDelta = new Vector2(Diameter, Diameter);
        _root.anchoredPosition = CornerPosition();
        _group = rootGo.GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        if (xp != null)
            _root.SetSiblingIndex(xp.GetSiblingIndex() + 1);

        Image shadow = CreateImage(_root, "Shadow", _softCircle, ShadowColor);
        RectTransform shadowRect = shadow.rectTransform;
        shadowRect.anchorMin = shadowRect.anchorMax = new Vector2(0.5f, 0.5f);
        shadowRect.pivot = new Vector2(0.5f, 0.5f);
        shadowRect.sizeDelta = new Vector2(Diameter + 18f, Diameter + 18f);
        shadowRect.anchoredPosition = new Vector2(0f, -5f);

        Image bezel = CreateImage(_root, "Bezel", _circle, BezelColor);
        HudChrome.Stretch(bezel.rectTransform, 0f);

        _inner = Diameter - Border * 2f;
        var viewGo = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
        viewGo.layer = 5;
        viewGo.transform.SetParent(_root, false);
        RectTransform view = viewGo.GetComponent<RectTransform>();
        view.anchorMin = view.anchorMax = new Vector2(0.5f, 0.5f);
        view.pivot = new Vector2(0.5f, 0.5f);
        view.sizeDelta = new Vector2(_inner, _inner);
        view.anchoredPosition = Vector2.zero;
        Image viewImage = viewGo.GetComponent<Image>();
        viewImage.sprite = _circle;
        viewImage.color = Color.white;
        viewImage.raycastTarget = false;
        viewGo.GetComponent<Mask>().showMaskGraphic = false;

        _texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "MiniMapTerrain"
        };
        _pixels = new Color32[Resolution * Resolution];
        _swap = new Color32[Resolution * Resolution];
        Fill(GrassLight);
        _texture.SetPixels32(_pixels);
        _texture.Apply(false, false);

        var mapGo = new GameObject("Terrain", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        mapGo.layer = 5;
        mapGo.transform.SetParent(view, false);
        _map = mapGo.GetComponent<RawImage>();
        _map.texture = _texture;
        _map.raycastTarget = false;
        _map.color = Color.white;
        HudChrome.Stretch(_map.rectTransform, 0f);

        _dots = new Image[DotCapacity];
        for (int i = 0; i < _dots.Length; i++)
        {
            _dots[i] = CreateImage(view, "Foe", _dotSprite, DotColor);
            SetSize(_dots[i].rectTransform, DotSize);
            _dots[i].enabled = false;
        }

        _house = CreateImage(view, "Home", _houseSprite, Color.white).rectTransform;
        SetSize(_house, HouseSize);
        _house.GetComponent<Image>().enabled = false;

        _arrow = CreateImage(view, "Player", _arrowSprite, Color.white).rectTransform;
        SetSize(_arrow, ArrowSize);
        return true;
    }

    bool TryBindFrame()
    {
        if (_player == null)
            _player = FindAnyObjectByType<PlayerVitals>();

        SphericalPlanet planet = SphericalPlanet.Instance;
        Camera cam = Camera.main;
        if (_player == null || !_player.isActiveAndEnabled || !_player.IsAlive || planet == null || cam == null)
            return false;

        Vector3 playerPos = _player.transform.position;
        _center = planet.Center;
        _planetRadius = Mathf.Max(1f, planet.Radius);
        _up = planet.GetUpAt(playerPos);
        _playerDir = _up;

        _forward = Vector3.ProjectOnPlane(cam.transform.forward, _up);
        if (_forward.sqrMagnitude < 0.0001f)
            _forward = Vector3.ProjectOnPlane(cam.transform.up, _up);
        if (_forward.sqrMagnitude < 0.0001f)
            _forward = Vector3.ProjectOnPlane(Vector3.forward, _up);
        if (_forward.sqrMagnitude < 0.0001f)
            return false;

        _forward.Normalize();
        _right = Vector3.Cross(_up, _forward);
        if (_right.sqrMagnitude < 0.0001f)
            return false;
        _right.Normalize();
        return true;
    }

    void LayoutCorner()
    {
        RememberRow();
        _root.anchoredPosition = CornerPosition();

        if (!_rowStored)
            return;

        float top = -_root.anchoredPosition.y;
        float rowTop = -top - Diameter - XpGap;
        if (_xp != null)
            _xp.anchoredPosition = new Vector2(_xpHome.x, rowTop);
        if (_coin != null)
            _coin.anchoredPosition = _coinHome + new Vector2(0f, rowTop - _xpHome.y);
        _rowShifted = true;
    }

    void RememberRow()
    {
        if (_canvas == null)
            return;
        if (_xp == null)
            _xp = FindNamed(_canvas.transform, "XpBar") as RectTransform;
        if (_coin == null)
            _coin = FindNamed(_canvas.transform, "CoinCounter") as RectTransform;
        if (_rowStored)
            return;

        if (_xp != null)
            _xpHome = _xp.anchoredPosition;
        if (_coin != null)
            _coinHome = _coin.anchoredPosition;
        _rowStored = _xp != null || _coin != null;
    }

    void RestoreRow()
    {
        if (!_rowShifted)
            return;

        if (_xp != null)
            _xp.anchoredPosition = _xpHome;
        if (_coin != null)
            _coin.anchoredPosition = _coinHome;
        _rowShifted = false;
    }

    Vector2 CornerPosition()
    {
        Vector2 inset = SafeInset();
        float right = ScreenMargin + inset.x;
        float top = ScreenMargin + inset.y;
        if (_rowStored && _xp != null)
        {
            right = Mathf.Max(right, -_xpHome.x + inset.x);
            top = Mathf.Max(top, -_xpHome.y + inset.y);
        }

        return new Vector2(-right, -top);
    }

    void BakeIfNeeded()
    {
        PlanetTileMap map = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetComponent<PlanetTileMap>()
            : null;
        int version = map != null ? map.SurfaceVersion : 0;
        Vector3 pos = _player.transform.position;
        bool moved = (pos - _bakedPos).sqrMagnitude > MoveRebuild * MoveRebuild;
        bool turned = Vector3.Dot(_forward, _bakedForward) < TurnRebuild;
        if (_hasBake && !moved && !turned && version == _bakedVersion)
            return;

        PlanetTileset tileset = map != null ? map.Tileset : null;
        float span = ViewRadius * 2f;
        for (int y = 0; y < Resolution; y++)
        {
            float ny = ((y + 0.5f) / Resolution - 0.5f) * span;
            for (int x = 0; x < Resolution; x++)
            {
                float nx = ((x + 0.5f) / Resolution - 0.5f) * span;
                _pixels[y * Resolution + x] = Sample(map, tileset, nx, ny);
            }
        }

        // Two wide box blurs. Cell tiles become the soft painted regions of the reference.
        Blur(_pixels, _swap, 3, true);
        Blur(_swap, _pixels, 3, false);
        Blur(_pixels, _swap, 2, true);
        Blur(_swap, _pixels, 2, false);
        Vignette(_pixels);

        _texture.SetPixels32(_pixels);
        _texture.Apply(false, false);
        _bakedPos = pos;
        _bakedForward = _forward;
        _bakedVersion = version;
        _hasBake = true;
    }

    Color32 Sample(PlanetTileMap map, PlanetTileset tileset, float arcX, float arcY)
    {
        if (map == null || !map.HasValidMap())
            return To32(Color.Lerp(GrassDark, GrassLight, 0.5f));

        Vector3 dir = DirectionOnSurface(arcX, arcY);
        if (!map.WorldToCell(_center + dir, out int lat, out int lon))
            return To32(GrassDark);

        float height = map.GetHeight(lat, lon);
        float n = Hash01(lat, lon);
        if (height <= WaterLine)
        {
            float depth = Mathf.InverseLerp(WaterLine, -1.15f, height);
            Color water = Color.Lerp(ShallowColor, DeepColor, Mathf.Clamp01(depth));
            water = Color.Lerp(water, Color.white, (n - 0.5f) * 0.1f);
            return To32(water);
        }

        int terrain = map.GetTerrain(lat, lon);
        Color ground = GroundColor(tileset, terrain, n);
        if (height < 1.2f && NearWater(map, lat, lon))
            ground = Color.Lerp(ground, ShoreColor, 0.82f);
        return To32(ground);
    }

    Color GroundColor(PlanetTileset tileset, int terrain, float n)
    {
        switch (terrain)
        {
            case PlanetTileset.TerrainLightGrass:
                return Color.Lerp(GrassDark, GrassLight, n);
            case PlanetTileset.TerrainDirt:
                return Color.Lerp(DirtDark, DirtLight, n);
            case PlanetTileset.TerrainSand:
                return Color.Lerp(SandDark, SandLight, n);
            case PlanetTileset.TerrainDarkGrass:
                return Color.Lerp(Canopy, CanopyLight, n);
            case PlanetTileset.TerrainVoid:
                return Color.Lerp(VoidDark, VoidLight, n);
            case PlanetTileset.TerrainDarkVoid:
                return Color.Lerp(VoidDark * 0.62f, VoidDark, n);
            case PlanetTileset.TerrainCrimson:
                return Color.Lerp(CrimsonDark, CrimsonLight, n);
            case PlanetTileset.TerrainDarkCrimson:
                return Color.Lerp(CrimsonDark, new Color(0.93f, 0.11f, 0.14f, 1f), n);
            case PlanetTileset.TerrainGlowingMushroom:
                return Color.Lerp(MushroomMid, MushroomLight, n);
            case PlanetTileset.TerrainDarkGlowingMushroom:
                return Color.Lerp(MushroomDark, MushroomMid, n);
            default:
                PlanetTileset.Terrain entry = tileset != null ? tileset.GetTerrain(terrain) : null;
                Color fallback = entry != null ? entry.previewColor : GrassLight;
                return Color.Lerp(fallback, Color.white, (n - 0.5f) * 0.18f);
        }
    }

    static bool NearWater(PlanetTileMap map, int lat, int lon)
    {
        return map.GetHeight(lat + 1, lon) <= WaterLine
            || map.GetHeight(lat - 1, lon) <= WaterLine
            || map.GetHeight(lat, lon + 1) <= WaterLine
            || map.GetHeight(lat, lon - 1) <= WaterLine;
    }

    Vector3 DirectionOnSurface(float arcX, float arcY)
    {
        float dist = Mathf.Sqrt(arcX * arcX + arcY * arcY);
        if (dist < 0.0001f)
            return _playerDir;

        float angle = dist / _planetRadius;
        Vector3 axis = _right * (arcX / dist) + _forward * (arcY / dist);
        return (_playerDir * Mathf.Cos(angle) + axis * Mathf.Sin(angle)).normalized;
    }

    void PlaceMarkers()
    {
        Vector3 face = Vector3.ProjectOnPlane(_player.transform.forward, _up);
        if (face.sqrMagnitude < 0.0001f)
            face = _forward;
        face.Normalize();
        float x = Vector3.Dot(face, _right);
        float y = Vector3.Dot(face, _forward);
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

        _arrow.anchoredPosition = Vector2.zero;
        _arrow.localRotation = Quaternion.Euler(0f, 0f, _arrowAngle);

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
            if (creature == null || !creature.IsAlive)
                continue;
            if (!TrySurfacePoint(creature.transform.position, out Vector2 point, out float arc))
                continue;
            if (arc > ViewRadius)
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
        Image house = _house.GetComponent<Image>();
        if (!ResolveCamp() || !TrySurfacePoint(_campVolume.bounds.center, out Vector2 point, out float arc))
        {
            house.enabled = false;
            return;
        }

        if (arc <= ArriveHide)
        {
            house.enabled = false;
            return;
        }

        float reach = _inner * 0.5f - HouseSize * 0.55f;
        if (point.sqrMagnitude > reach * reach)
            point = point.normalized * reach;

        house.enabled = true;
        _house.anchoredPosition = point;
        _house.localRotation = Quaternion.identity;
    }

    bool TrySurfacePoint(Vector3 world, out Vector2 ui, out float arc)
    {
        ui = Vector2.zero;
        arc = 0f;
        Vector3 from = world - _center;
        if (from.sqrMagnitude < 0.0001f)
            return false;

        Vector3 dir = from.normalized;
        float angle = Vector3.Angle(_playerDir, dir) * Mathf.Deg2Rad;
        arc = angle * _planetRadius;
        Vector3 bearing = Vector3.ProjectOnPlane(dir, _up);
        if (bearing.sqrMagnitude < 0.000001f)
            return false;

        bearing.Normalize();
        float scale = (_inner * 0.5f) / ViewRadius;
        ui = new Vector2(Vector3.Dot(bearing, _right), Vector3.Dot(bearing, _forward)) * (arc * scale);
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

    Vector2 SafeInset()
    {
        if (_canvas == null || Screen.width <= 0 || Screen.height <= 0)
            return Vector2.zero;

        RectTransform canvasRect = _canvas.transform as RectTransform;
        if (canvasRect == null)
            return Vector2.zero;

        Rect safe = Screen.safeArea;
        Vector2 size = canvasRect.rect.size;
        float right = (Screen.width - safe.xMax) / Screen.width * size.x;
        float top = (Screen.height - safe.yMax) / Screen.height * size.y;
        return new Vector2(Mathf.Max(0f, right), Mathf.Max(0f, top));
    }

    void Fill(Color color)
    {
        Color32 px = To32(color);
        for (int i = 0; i < _pixels.Length; i++)
            _pixels[i] = px;
    }

    static void Vignette(Color32[] pixels)
    {
        float half = (Resolution - 1) * 0.5f;
        for (int y = 0; y < Resolution; y++)
        {
            float dy = (y - half) / half;
            for (int x = 0; x < Resolution; x++)
            {
                float dx = (x - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float shade = 1f - 0.32f * Mathf.SmoothStep(0.42f, 1.05f, d);
                int i = y * Resolution + x;
                Color32 c = pixels[i];
                pixels[i] = new Color32(
                    (byte)(c.r * shade),
                    (byte)(c.g * shade),
                    (byte)(c.b * shade),
                    255);
            }
        }
    }

    static void Blur(Color32[] src, Color32[] dst, int radius, bool horizontal)
    {
        int window = radius * 2 + 1;
        for (int y = 0; y < Resolution; y++)
        {
            for (int x = 0; x < Resolution; x++)
            {
                int r = 0;
                int g = 0;
                int b = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int xx = horizontal ? Mathf.Clamp(x + k, 0, Resolution - 1) : x;
                    int yy = horizontal ? y : Mathf.Clamp(y + k, 0, Resolution - 1);
                    Color32 c = src[yy * Resolution + xx];
                    r += c.r;
                    g += c.g;
                    b += c.b;
                }

                dst[y * Resolution + x] = new Color32(
                    (byte)(r / window),
                    (byte)(g / window),
                    (byte)(b / window),
                    255);
            }
        }
    }

    static float Hash01(int lat, int lon)
    {
        unchecked
        {
            uint h = (uint)(lat * 374761393 + lon * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h & 1023u) / 1023f;
        }
    }

    static Color32 To32(Color c)
    {
        return new Color32(
            (byte)(Mathf.Clamp01(c.r) * 255f),
            (byte)(Mathf.Clamp01(c.g) * 255f),
            (byte)(Mathf.Clamp01(c.b) * 255f),
            255);
    }

    static Image CreateImage(Transform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        image.preserveAspect = true;
        return image;
    }

    static void SetSize(RectTransform rect, float size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    static Transform FindNamed(Transform root, string childName)
    {
        if (root == null)
            return null;
        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamed(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }

    static void DestroySprite(Sprite sprite)
    {
        if (sprite == null)
            return;

        Texture texture = sprite.texture;
        DestroyAsset(sprite);
        if (texture != null)
            DestroyAsset(texture);
    }

    static void DestroyAsset(Object asset)
    {
        if (asset == null)
            return;
        if (Application.isPlaying)
            UnityEngine.Object.Destroy(asset);
        else
            UnityEngine.Object.DestroyImmediate(asset);
    }

    static Sprite CircleSprite(int size, bool soft)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = soft ? "MiniMapShadow" : "MiniMapCircle"
        };
        var pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        float radius = center;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alpha = soft
                    ? 1f - Mathf.SmoothStep(radius * 0.62f, radius, d)
                    : Mathf.Clamp01(radius - d + 0.85f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    static Sprite DotSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "MiniMapDot"
        };
        var pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        float radius = center - 0.5f;
        var clear = new Color32(0, 0, 0, 0);
        var ink = new Color32(92, 16, 14, 255);
        var fill = new Color32(255, 255, 255, 255);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                if (d > radius + 0.6f)
                    pixels[y * size + x] = clear;
                else if (d > radius - 1.35f)
                    pixels[y * size + x] = ink;
                else
                    pixels[y * size + x] = fill;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    static Sprite ArrowSprite(int size)
    {
        var pixels = new Color32[size * size];
        Clear(pixels);
        var ink = new Color32(16, 62, 22, 255);
        var fill = new Color32(58, 214, 72, 255);
        // Texture row 0 is the bottom, so the tip sits on the high row and points up.
        FillTriangle(pixels, size, new Vector2(size * 0.5f, size - 5f), new Vector2(7f, 7f), new Vector2(size - 7f, 7f), ink);
        FillTriangle(pixels, size, new Vector2(size * 0.5f, size - 11f), new Vector2(14f, 12f), new Vector2(size - 14f, 12f), fill);
        return FinishSprite(pixels, size, "MiniMapArrow");
    }

    static Sprite HouseSprite(int size)
    {
        var pixels = new Color32[size * size];
        Clear(pixels);
        var ink = new Color32(16, 26, 46, 255);
        var paper = new Color32(255, 255, 255, 255);
        float s = size;
        FillRect(pixels, size, s * 0.18f, s * 0.08f, s * 0.82f, s * 0.52f, ink);
        FillTriangle(pixels, size, new Vector2(s * 0.5f, s * 0.94f), new Vector2(s * 0.08f, s * 0.50f), new Vector2(s * 0.92f, s * 0.50f), ink);
        FillRect(pixels, size, s * 0.26f, s * 0.14f, s * 0.74f, s * 0.50f, paper);
        FillTriangle(pixels, size, new Vector2(s * 0.5f, s * 0.86f), new Vector2(s * 0.16f, s * 0.52f), new Vector2(s * 0.84f, s * 0.52f), paper);
        FillRect(pixels, size, s * 0.42f, s * 0.14f, s * 0.58f, s * 0.38f, ink);
        return FinishSprite(pixels, size, "MiniMapHouse");
    }

    static Sprite FinishSprite(Color32[] pixels, int size, string spriteName)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = spriteName
        };
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    static void Clear(Color32[] pixels)
    {
        var clear = new Color32(0, 0, 0, 0);
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = clear;
    }

    static void FillRect(Color32[] pixels, int size, float x0, float y0, float x1, float y1, Color32 color)
    {
        int left = Mathf.Clamp(Mathf.FloorToInt(x0), 0, size - 1);
        int right = Mathf.Clamp(Mathf.CeilToInt(x1), 0, size);
        int bottom = Mathf.Clamp(Mathf.FloorToInt(y0), 0, size - 1);
        int top = Mathf.Clamp(Mathf.CeilToInt(y1), 0, size);
        for (int y = bottom; y < top; y++)
        {
            for (int x = left; x < right; x++)
                pixels[y * size + x] = color;
        }
    }

    static void FillTriangle(Color32[] pixels, int size, Vector2 a, Vector2 b, Vector2 c, Color32 color)
    {
        int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), 0, size - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))), 0, size - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), 0, size - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))), 0, size - 1);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                if (!PointInTriangle(p, a, b, c))
                    continue;
                pixels[y * size + x] = color;
            }
        }
    }

    static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Edge(p, a, b);
        float d2 = Edge(p, b, c);
        float d3 = Edge(p, c, a);
        bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(neg && pos);
    }

    static float Edge(Vector2 p, Vector2 a, Vector2 b)
    {
        return (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    }
}
