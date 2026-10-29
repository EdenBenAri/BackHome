using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Dashed circle on the ground at the camp trigger's radius.
/// Built when the camp moves or resizes. In play it only ticks while the enter flash is running.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
[DefaultExecutionOrder(50)]
[RequireComponent(typeof(SphereCollider))]
public class CampRadiusRing : MonoBehaviour
{
    const int DashCount = 28;
    const int SlicesPerDash = 3;
    const float DashFill = 0.52f;
    const float WidthOverRadius = 0.04f;
    const float Lift = 0.06f;

    static readonly int StraightId = Shader.PropertyToID("_Straight");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly Color RestColor = Color.white;
    static readonly Color EnterColor = new Color(0.25f, 1f, 0.38f, 1f);
    const float EnterFlashSeconds = 0.5f;

    static Material s_material;
    static bool s_loggedMissing;

    readonly List<Vector3> _vertices = new List<Vector3>(28 * 4 * 2);
    readonly List<Vector2> _uvs = new List<Vector2>(28 * 4 * 2);
    readonly List<int> _triangles = new List<int>(28 * 3 * 6);
    readonly Vector3[] _samples = new Vector3[SlicesPerDash + 1];

    GameObject _visual;
    Mesh _mesh;
    Vector3 _stampPosition;
    Quaternion _stampRotation;
    Vector3 _stampScale;
    Vector3 _stampCenter;
    float _stampRadius;
    Material _instance;
    float _flash;
    bool _hasStamp;

    void OnEnable()
    {
        if (!gameObject.scene.IsValid())
            return;

        Rebuild();
    }

    void Start()
    {
        if (!Application.isPlaying)
            return;

        Rebuild();
    }

    void OnDisable()
    {
        DestroyVisual();
        _hasStamp = false;
    }

    /// <summary>Turns the dashes green for half a second, then back to white.</summary>
    public void PulseEnter()
    {
        if (!Application.isPlaying)
            return;

        EnsureVisual();
        _flash = EnterFlashSeconds;
        ApplyColor(EnterColor);
    }

    void Update()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (!isActiveAndEnabled || !gameObject.scene.IsValid())
                return;

            if (Moved())
                Rebuild();
            return;
        }
#endif
        if (_flash <= 0f)
            return;

        _flash -= Time.deltaTime;
        if (_flash > 0f)
            return;

        _flash = 0f;
        ApplyColor(RestColor);
    }

    void Rebuild()
    {
        SphereCollider sphere = GetComponent<SphereCollider>();
        if (sphere == null || sphere.radius <= 0.0001f)
            return;

        EnsureVisual();
        if (_visual == null || _mesh == null)
            return;

        AlignVisual();
        Vector3 center = transform.TransformPoint(sphere.center);
        float worldRadius = sphere.radius * MaxScale(transform.lossyScale);
        if (worldRadius <= 0.0001f)
            return;

        SphericalPlanet planet = GetComponentInParent<SphericalPlanet>();
        PlanetTileMap tiles = planet != null ? planet.GetComponent<PlanetTileMap>() : null;
        BuildMesh(center, worldRadius, planet, tiles);
        Remember(sphere);

        Material material = SharedMaterial();
        if (material != null)
            material.SetFloat(StraightId, StraightUnits());
    }

    void BuildMesh(Vector3 center, float worldRadius, SphericalPlanet planet, PlanetTileMap tiles)
    {
        _vertices.Clear();
        _uvs.Clear();
        _triangles.Clear();

        float width = Mathf.Max(0.12f, worldRadius * WidthOverRadius);
        float half = width * 0.5f;
        float along = StraightUnits() + 1f;
        float slot = Mathf.PI * 2f / DashCount;
        float sweep = slot * DashFill;
        float lead = slot * (1f - DashFill) * 0.5f;
        Vector3 origin = _visual.transform.position;

        for (int dash = 0; dash < DashCount; dash++)
        {
            float start = dash * slot + lead;
            for (int s = 0; s <= SlicesPerDash; s++)
            {
                float t = s / (float)SlicesPerDash;
                float angle = start + sweep * t;
                _samples[s] = PointOnRadius(center, worldRadius, planet, tiles, angle, Lift);
            }

            int first = _vertices.Count;
            for (int s = 0; s <= SlicesPerDash; s++)
            {
                Vector3 forward = SampleForward(s);
                Vector3 radial = Radial(planet, _samples[s], center);
                Vector3 side = Vector3.Cross(radial, forward);
                if (side.sqrMagnitude < 0.000001f)
                    side = Vector3.Cross(radial, Vector3.right);
                side.Normalize();

                float u = Mathf.Lerp(-along, along, s / (float)SlicesPerDash);
                Vector3 left = PlaceEdge(center, worldRadius, planet, tiles, _samples[s], -side * half);
                Vector3 right = PlaceEdge(center, worldRadius, planet, tiles, _samples[s], side * half);
                _vertices.Add(left - origin);
                _vertices.Add(right - origin);
                _uvs.Add(new Vector2(u, -1f));
                _uvs.Add(new Vector2(u, 1f));
            }

            for (int s = 0; s < SlicesPerDash; s++)
            {
                int a = first + s * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;
                _triangles.Add(a);
                _triangles.Add(c);
                _triangles.Add(b);
                _triangles.Add(b);
                _triangles.Add(c);
                _triangles.Add(d);
            }
        }

        _mesh.Clear();
        _mesh.SetVertices(_vertices);
        _mesh.SetUVs(0, _uvs);
        _mesh.SetTriangles(_triangles, 0);
        _mesh.RecalculateBounds();
    }

    Vector3 SampleForward(int index)
    {
        int last = SlicesPerDash;
        if (index <= 0)
            return _samples[1] - _samples[0];
        if (index >= last)
            return _samples[last] - _samples[last - 1];
        return _samples[index + 1] - _samples[index - 1];
    }

    Vector3 PlaceEdge(
        Vector3 center,
        float worldRadius,
        SphericalPlanet planet,
        PlanetTileMap tiles,
        Vector3 sample,
        Vector3 offset)
    {
        if (planet == null)
            return sample + offset;

        Vector3 dir = sample + offset - planet.Center;
        return OnSurface(planet, tiles, dir, Lift);
    }

    Vector3 PointOnRadius(
        Vector3 center,
        float worldRadius,
        SphericalPlanet planet,
        PlanetTileMap tiles,
        float angle,
        float lift)
    {
        if (planet == null)
        {
            Vector3 axis = transform.up.sqrMagnitude > 0.0001f ? transform.up.normalized : Vector3.up;
            Vector3 east = Vector3.Cross(axis, Vector3.forward);
            if (east.sqrMagnitude < 0.000001f)
                east = Vector3.Cross(axis, Vector3.right);
            east.Normalize();
            Vector3 north = Vector3.Cross(east, axis).normalized;
            return center + (east * Mathf.Cos(angle) + north * Mathf.Sin(angle)) * worldRadius + axis * lift;
        }

        Vector3 up = center - planet.Center;
        if (up.sqrMagnitude < 0.000001f)
            up = Vector3.up;
        up.Normalize();

        Vector3 tangent = Tangent(up, angle);
        float alpha = SolveAlpha(center, worldRadius, planet, tiles, up, tangent);
        Vector3 dir = up * Mathf.Cos(alpha) + tangent * Mathf.Sin(alpha);
        return OnSurface(planet, tiles, dir, lift);
    }

    static float SolveAlpha(
        Vector3 center,
        float worldRadius,
        SphericalPlanet planet,
        PlanetTileMap tiles,
        Vector3 up,
        Vector3 tangent)
    {
        float hi = 0.45f;
        float limit = worldRadius * worldRadius;
        for (int n = 0; n < 6; n++)
        {
            if (Separation(center, planet, tiles, up, tangent, hi) >= limit)
                break;
            hi = Mathf.Min(hi * 1.7f, 1.5f);
        }

        float lo = 0f;
        for (int i = 0; i < 14; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (Separation(center, planet, tiles, up, tangent, mid) < limit)
                lo = mid;
            else
                hi = mid;
        }

        return (lo + hi) * 0.5f;
    }

    static float Separation(
        Vector3 center,
        SphericalPlanet planet,
        PlanetTileMap tiles,
        Vector3 up,
        Vector3 tangent,
        float alpha)
    {
        Vector3 dir = up * Mathf.Cos(alpha) + tangent * Mathf.Sin(alpha);
        Vector3 point = OnSurface(planet, tiles, dir, 0f);
        return (point - center).sqrMagnitude;
    }

    static Vector3 OnSurface(SphericalPlanet planet, PlanetTileMap tiles, Vector3 direction, float lift)
    {
        if (tiles != null)
            return tiles.GetWalkSurfacePoint(direction, lift);
        return planet.GetSurfacePoint(direction, lift);
    }

    static Vector3 Tangent(Vector3 up, float angle)
    {
        Vector3 east = Vector3.Cross(up, Vector3.forward);
        if (east.sqrMagnitude < 0.000001f)
            east = Vector3.Cross(up, Vector3.right);
        east.Normalize();
        Vector3 north = Vector3.Cross(east, up).normalized;
        return east * Mathf.Cos(angle) + north * Mathf.Sin(angle);
    }

    static Vector3 Radial(SphericalPlanet planet, Vector3 sample, Vector3 center)
    {
        if (planet == null)
        {
            Vector3 up = sample - center;
            return up.sqrMagnitude > 0.000001f ? up.normalized : Vector3.up;
        }

        Vector3 radial = sample - planet.Center;
        return radial.sqrMagnitude > 0.000001f ? radial.normalized : Vector3.up;
    }

    static float StraightUnits()
    {
        float dashOverWidth = (Mathf.PI * 2f * DashFill) / (DashCount * WidthOverRadius);
        return Mathf.Max(0.05f, dashOverWidth - 1f);
    }

    static float MaxScale(Vector3 scale)
    {
        return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
    }

    void EnsureVisual()
    {
        if (_visual != null)
            return;

        Material material = SharedMaterial();
        if (material == null)
            return;

        _visual = new GameObject("Camp Radius");
        _visual.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
        _visual.layer = 0;

        _mesh = new Mesh { name = "Camp Radius", hideFlags = HideFlags.DontSave };
        MeshFilter filter = _visual.AddComponent<MeshFilter>();
        filter.sharedMesh = _mesh;

        MeshRenderer renderer = _visual.AddComponent<MeshRenderer>();
        _instance = new Material(material)
        {
            name = "Camp Radius",
            hideFlags = HideFlags.HideAndDontSave
        };
        _instance.SetColor(ColorId, _flash > 0f ? EnterColor : RestColor);
        renderer.sharedMaterial = _instance;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        renderer.allowOcclusionWhenDynamic = false;
    }

    void AlignVisual()
    {
        Transform ring = _visual.transform;
        ring.SetParent(transform, false);
        ring.localPosition = Vector3.zero;
        ring.localRotation = Quaternion.Inverse(transform.rotation);
        Vector3 lossy = transform.lossyScale;
        ring.localScale = new Vector3(
            1f / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
    }

    void Remember(SphereCollider sphere)
    {
        _stampPosition = transform.position;
        _stampRotation = transform.rotation;
        _stampScale = transform.lossyScale;
        _stampCenter = sphere.center;
        _stampRadius = sphere.radius;
        _hasStamp = true;
    }

    bool Moved()
    {
        SphereCollider sphere = GetComponent<SphereCollider>();
        if (sphere == null)
            return false;
        if (!_hasStamp || _visual == null)
            return true;

        if ((transform.position - _stampPosition).sqrMagnitude > 0.00001f)
            return true;
        if (Quaternion.Angle(transform.rotation, _stampRotation) > 0.05f)
            return true;
        if ((transform.lossyScale - _stampScale).sqrMagnitude > 0.0000001f)
            return true;
        if ((sphere.center - _stampCenter).sqrMagnitude > 0.0000001f)
            return true;
        return Mathf.Abs(sphere.radius - _stampRadius) > 0.00001f;
    }

    void DestroyVisual()
    {
        if (_mesh != null)
        {
            if (Application.isPlaying)
                Destroy(_mesh);
            else
                DestroyImmediate(_mesh);
            _mesh = null;
        }

        if (_visual != null)
        {
            if (Application.isPlaying)
                Destroy(_visual);
            else
                DestroyImmediate(_visual);
            _visual = null;
        }

        if (_instance != null)
        {
            if (Application.isPlaying)
                Destroy(_instance);
            else
                DestroyImmediate(_instance);
            _instance = null;
        }
    }

    void ApplyColor(Color color)
    {
        if (_instance == null)
            return;

        _instance.SetColor(ColorId, color);
    }

    static Material SharedMaterial()
    {
        if (s_material != null)
            return s_material;

        Shader shader = Shader.Find("BackHome/CampRadiusRing");
        if (shader == null)
        {
            if (!s_loggedMissing)
            {
                s_loggedMissing = true;
                Debug.LogWarning("CampRadiusRing: missing shader BackHome/CampRadiusRing.");
            }

            return null;
        }

        s_material = new Material(shader)
        {
            name = "Camp Radius",
            hideFlags = HideFlags.HideAndDontSave
        };
        s_material.SetColor(ColorId, Color.white);
        s_material.SetFloat(StraightId, StraightUnits());
        return s_material;
    }
}
