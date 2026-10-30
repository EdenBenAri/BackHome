using UnityEngine;

/// <summary>A faint ground ring at the equipped weapons' maximum attack range.</summary>
[DisallowMultipleComponent]
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[DefaultExecutionOrder(100)]
public class PlayerRangeIndicator : MonoBehaviour
{
    const int Segments = 128;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("Visibility")]
    [SerializeField, Min(0.01f)] float combatTimeout = 1f;
    [Tooltip("Fade during the end of the timeout, fully hidden after one second by default.")]
    [SerializeField, Min(0f)] float fadeOutDuration = 0.15f;

    [Header("Appearance")]
    [SerializeField] Color color = new Color(1f, 1f, 1f, 0.32f);
    [SerializeField, Min(0.005f)] float lineWidth = 0.045f;
    [SerializeField, Min(0f)] float surfaceLift = 0.06f;
    [Tooltip("Show the equipped weapons' range while editing the Player prefab or scene.")]
    [SerializeField] bool previewInEditor = true;

    readonly Vector3[] _vertices = new Vector3[(Segments + 1) * 2];
    readonly Vector2[] _directions = new Vector2[Segments + 1];
    PlayerVitals _vitals;
    PlayerRangeCombat _combat;
    SphericalPlanet _planet;
    PlanetTileMap _tiles;
    MeshFilter _filter;
    Mesh _mesh;
    MeshRenderer _renderer;
    MaterialPropertyBlock _properties;
    float _lastCombatTime = float.NegativeInfinity;

    void OnEnable()
    {
        _vitals = GetComponentInParent<PlayerVitals>();
        _combat = GetComponentInParent<PlayerRangeCombat>();
        _filter = GetComponent<MeshFilter>();
        _renderer = GetComponent<MeshRenderer>();
        Hide();
        if (Application.IsPlaying(gameObject) && _vitals != null)
        {
            _vitals.Damaged += OnDamaged;
            _vitals.Died += Hide;
        }
    }

    void OnDisable()
    {
        if (_vitals != null)
        {
            _vitals.Damaged -= OnDamaged;
            _vitals.Died -= Hide;
        }
        Hide();
        if (_filter != null && _filter.sharedMesh == _mesh)
            _filter.sharedMesh = null;
        if (_renderer != null)
            _renderer.SetPropertyBlock(null);
        Release(_mesh);
        _mesh = null;
    }

    void OnDamaged(float _)
    {
        // Oxygen drain does not raise Damaged, so only combat keeps the ring alive.
        if (_vitals.IsAlive && _combat != null && _combat.isActiveAndEnabled)
            _lastCombatTime = Time.time;
    }

    void LateUpdate()
    {
        if (_vitals == null || _combat == null)
        {
            Hide();
            return;
        }

        float radius = _vitals.AttackRange;
        // Prefab Mode also needs a preview when the main scene is in Play Mode.
        if (!Application.IsPlaying(gameObject))
        {
            if (previewInEditor && radius > 0f)
                ShowRing(radius, 1f);
            else
                Hide();
            return;
        }

        if (!_vitals.IsAlive || !_combat.isActiveAndEnabled || radius <= 0f || _vitals.Weapons.Count == 0)
        {
            Hide();
            return;
        }

        // Keep showing between shots, including weapons with a long firing cooldown.
        if (_combat.IsFiringAtCreatures)
            _lastCombatTime = Time.time;

        float remaining = combatTimeout - (Time.time - _lastCombatTime);
        if (remaining <= 0f)
        {
            Hide();
            return;
        }

        float fade = Mathf.Min(fadeOutDuration, combatTimeout);
        ShowRing(radius, fade > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(remaining / fade)) : 1f);
    }

    void ShowRing(float radius, float opacity)
    {
        EnsureVisual();
        BuildRing(radius);
        Color tint = color;
        tint.a *= opacity;
        if (_properties == null)
            _properties = new MaterialPropertyBlock();
        _renderer.GetPropertyBlock(_properties);
        _properties.SetColor(ColorId, tint);
        _renderer.SetPropertyBlock(_properties);
        _renderer.enabled = true;
    }

    void Hide()
    {
        _lastCombatTime = float.NegativeInfinity;
        if (_renderer != null)
            _renderer.enabled = false;
    }

    void EnsureVisual()
    {
        if (_mesh != null)
            return;

        // The object and renderer are authored in the prefab; only the terrain-following
        // mesh is transient, so changing the appearance never creates duplicate children.
        _mesh = new Mesh { name = "Player Attack Range", hideFlags = HideFlags.HideAndDontSave };
        _mesh.MarkDynamic();
        _filter.sharedMesh = _mesh;

        var uv = new Vector2[_vertices.Length];
        var triangles = new int[Segments * 6];
        for (int i = 0; i <= Segments; i++)
        {
            float angle = i * (Mathf.PI * 2f / Segments);
            _directions[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            uv[i * 2] = new Vector2(0f, -1f);
            uv[i * 2 + 1] = new Vector2(0f, 1f);
            if (i == Segments)
                continue;

            int vertex = i * 2;
            int index = i * 6;
            triangles[index] = vertex;
            triangles[index + 1] = vertex + 2;
            triangles[index + 2] = vertex + 1;
            triangles[index + 3] = vertex + 1;
            triangles[index + 4] = vertex + 2;
            triangles[index + 5] = vertex + 3;
        }
        _directions[Segments] = _directions[0];
        _mesh.vertices = _vertices;
        _mesh.uv = uv;
        _mesh.triangles = triangles;
    }

    void BuildRing(float radius)
    {
        SphericalPlanet planet = SphericalPlanet.Instance;
        // A prefab preview must not project onto a planet in the open gameplay scene.
        if (!Application.IsPlaying(gameObject) && planet != null && planet.gameObject.scene != gameObject.scene)
            planet = null;
        if (_planet != planet)
        {
            _planet = planet;
            _tiles = _planet != null ? _planet.GetComponent<PlanetTileMap>() : null;
        }

        Transform player = _vitals.transform;
        Vector3 origin = player.position;
        Vector3 up = _planet != null ? (origin - _planet.Center).normalized : player.up;
        Vector3 right = Vector3.Cross(up, Vector3.forward);
        if (right.sqrMagnitude < 0.001f)
            right = Vector3.Cross(up, Vector3.right);
        right.Normalize();
        Vector3 forward = Vector3.Cross(right, up);
        float halfWidth = Mathf.Min(lineWidth * 0.5f, radius * 0.5f);
        float originRadius = _planet != null ? Vector3.Distance(origin, _planet.Center) : 0f;
        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;

        for (int i = 0; i <= Segments; i++)
        {
            Vector3 tangent = right * _directions[i].x + forward * _directions[i].y;
            _vertices[i * 2] = worldToLocal.MultiplyPoint3x4(
                SurfacePoint(origin, up, tangent, originRadius, radius - halfWidth));
            _vertices[i * 2 + 1] = worldToLocal.MultiplyPoint3x4(
                SurfacePoint(origin, up, tangent, originRadius, radius + halfWidth));
        }

        _mesh.vertices = _vertices;
        _mesh.RecalculateBounds();
    }

    Vector3 SurfacePoint(Vector3 origin, Vector3 up, Vector3 tangent, float originRadius, float range)
    {
        if (_planet == null)
            return origin + tangent * range + up * surfaceLift;

        // Combat uses arc length times the average of the two surface radii.
        // Refine the angle for terrain height instead of drawing a flat chord circle.
        float angle = range / Mathf.Max(0.001f, originRadius);
        Vector3 direction = up;
        float surfaceRadius = originRadius;
        for (int i = 0; i < 3; i++)
        {
            direction = up * Mathf.Cos(angle) + tangent * Mathf.Sin(angle);
            surfaceRadius = _tiles != null && _tiles.ProvidesWalkSurface
                ? _tiles.GetWalkSurfaceRadius(direction)
                : _planet.GetTerrainRadius(direction);
            angle = range / Mathf.Max(0.001f, (originRadius + surfaceRadius) * 0.5f);
        }

        return _planet.Center + direction * (surfaceRadius + surfaceLift);
    }

    void Release(Object value)
    {
        if (value == null)
            return;
        if (Application.IsPlaying(gameObject))
            Destroy(value);
        else
            DestroyImmediate(value);
    }
}
