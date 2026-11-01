using UnityEngine;
using UnityEngine.Rendering;

/// <summary>A soft red ground ring at a king's fixed chase boundary, using the player's range material.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AreaKing), typeof(CreatureChase))]
[DefaultExecutionOrder(100)]
public sealed class AreaKingLeashIndicator : MonoBehaviour
{
    const int Segments = 256;
    const string MaterialPath = "Player/VFX/Shaders/PlayerRangeIndicator";
    static readonly int ColorId = Shader.PropertyToID("_Color");

    [SerializeField] Color color = new Color(1f, .12f, .12f, .48f);
    [Tooltip("Slightly wider than the player's 0.22-unit attack range ring.")]
    [SerializeField, Min(.005f)] float lineWidth = .32f;
    [SerializeField, Min(0f)] float surfaceLift = .08f;

    readonly Vector3[] _vertices = new Vector3[(Segments + 1) * 2];
    readonly Vector2[] _directions = new Vector2[Segments + 1];
    Creature _creature;
    CreatureChase _chase;
    SphericalPlanet _planet;
    PlanetTileMap _tiles;
    MeshRenderer _renderer;
    Mesh _mesh;
    MaterialPropertyBlock _properties;
    Vector3 _lastCenter;
    Matrix4x4 _lastWorldToLocal;
    float _lastRadius, _lastWidth, _lastLift;
    int _lastSurfaceVersion;
    bool _geometryValid;

    void Awake()
    {
        _creature = GetComponent<Creature>();
        _chase = GetComponent<CreatureChase>();
    }

    void LateUpdate()
    {
        if (_creature == null || !_creature.IsAlive || _chase == null
            || !_chase.TryGetKingLeash(out Vector3 center, out float radius))
        {
            Hide();
            return;
        }

        var planet = SphericalPlanet.Instance;
        if (_planet != planet)
        {
            _planet = planet;
            _tiles = planet != null ? planet.GetComponent<PlanetTileMap>() : null;
            _geometryValid = false;
        }
        if (!EnsureVisual(center)) return;
        BuildRing(center, radius);
        _properties.SetColor(ColorId, color);
        _renderer.SetPropertyBlock(_properties);
        _renderer.enabled = true;
    }

    bool EnsureVisual(Vector3 center)
    {
        if (_renderer != null) return true;
        Material material = Resources.Load<Material>(MaterialPath);
        if (material == null) return false;

        // Keep the renderer outside the moving/scaling king hierarchy. It must neither
        // move with the chase nor expand body bounds used by outlines, shadows and cinematics.
        var ring = new GameObject("King Leash Ring VFX") { layer = gameObject.layer };
        ring.transform.SetParent(_planet != null ? _planet.transform : transform.parent, false);
        ring.transform.SetPositionAndRotation(center, Quaternion.identity);
        _mesh = new Mesh { name = "King Leash Boundary", hideFlags = HideFlags.DontSave };
        ring.AddComponent<MeshFilter>().sharedMesh = _mesh;
        _renderer = ring.AddComponent<MeshRenderer>();
        _renderer.sharedMaterial = material;
        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.lightProbeUsage = LightProbeUsage.Off;
        _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        _properties = new MaterialPropertyBlock();

        var uv = new Vector2[_vertices.Length];
        var triangles = new int[Segments * 6];
        for (int i = 0; i <= Segments; i++)
        {
            float angle = i * (Mathf.PI * 2f / Segments);
            _directions[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            uv[i * 2] = new Vector2(0f, -1f);
            uv[i * 2 + 1] = new Vector2(0f, 1f);
            if (i == Segments) continue;
            int vertex = i * 2, index = i * 6;
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
        return true;
    }

    void BuildRing(Vector3 center, float radius)
    {
        Matrix4x4 worldToLocal = _renderer.transform.worldToLocalMatrix;
        int version = _tiles != null ? _tiles.SurfaceVersion : 0;
        if (_geometryValid && center == _lastCenter && radius == _lastRadius
            && lineWidth == _lastWidth && surfaceLift == _lastLift
            && worldToLocal == _lastWorldToLocal && version == _lastSurfaceVersion) return;

        Vector3 up = _planet != null ? (center - _planet.Center).normalized : Vector3.up;
        Vector3 right = Vector3.Cross(up, Vector3.forward);
        if (right.sqrMagnitude < .001f) right = Vector3.Cross(up, Vector3.right);
        right.Normalize();
        Vector3 forward = Vector3.Cross(right, up);
        float originRadius = _planet != null ? Vector3.Distance(center, _planet.Center) : 0f;
        float halfWidth = Mathf.Min(lineWidth * .5f, radius * .5f);
        for (int i = 0; i <= Segments; i++)
        {
            Vector3 tangent = right * _directions[i].x + forward * _directions[i].y;
            _vertices[i * 2] = worldToLocal.MultiplyPoint3x4(
                SurfacePoint(center, up, tangent, originRadius, radius - halfWidth));
            _vertices[i * 2 + 1] = worldToLocal.MultiplyPoint3x4(
                SurfacePoint(center, up, tangent, originRadius, radius + halfWidth));
        }
        _mesh.vertices = _vertices;
        _mesh.RecalculateBounds();
        _lastCenter = center;
        _lastRadius = radius;
        _lastWidth = lineWidth;
        _lastLift = surfaceLift;
        _lastWorldToLocal = worldToLocal;
        _lastSurfaceVersion = version;
        _geometryValid = true;
    }

    Vector3 SurfacePoint(Vector3 center, Vector3 up, Vector3 tangent, float originRadius, float range)
    {
        if (_planet == null) return center + tangent * range + up * surfaceLift;
        // Match the pursuit's great-circle distance, including varying terrain height,
        // rather than projecting a flat circle/chord onto the spherical world.
        float angle = range / Mathf.Max(.001f, originRadius);
        Vector3 direction = up;
        float surfaceRadius = originRadius;
        for (int i = 0; i < 3; i++)
        {
            direction = up * Mathf.Cos(angle) + tangent * Mathf.Sin(angle);
            surfaceRadius = _tiles != null && _tiles.ProvidesWalkSurface
                ? _tiles.GetWalkSurfaceRadius(direction) : _planet.GetTerrainRadius(direction);
            angle = range / Mathf.Max(.001f, (originRadius + surfaceRadius) * .5f);
        }
        return _planet.Center + direction * (surfaceRadius + surfaceLift);
    }

    void Hide()
    {
        if (_renderer != null) _renderer.enabled = false;
    }

    void OnDisable() => Hide();

    void OnDestroy()
    {
        if (_renderer != null) Release(_renderer.gameObject);
        if (_mesh != null) Release(_mesh);
    }

    void Release(Object value)
    {
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }
}
