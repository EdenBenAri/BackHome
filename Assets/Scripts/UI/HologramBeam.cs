using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Light beams from the cockpit screen up to a planet hologram.
/// The hologram itself is not modified; this only reads its position and size.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class HologramBeam : MonoBehaviour
{
    [SerializeField] Transform hologram;
    [SerializeField] Material beamMaterial;
    [SerializeField] Vector3 baseCenter;
    [SerializeField] Vector3 baseAxisX = Vector3.right;
    [SerializeField] Vector3 baseAxisY = Vector3.up;
    [SerializeField] float baseHalfWidth = 0.12f;
    [SerializeField] float baseHalfHeight = 0.07f;
    [SerializeField] float topRadiusScale = 1.3f;
    [SerializeField] float topAlong;
    [SerializeField] int segments = 32;

    Mesh _mesh;
    MeshFilter _filter;
    MeshRenderer _renderer;

    public void Configure(
        Transform target,
        Material material,
        Vector3 center,
        Vector3 axisX,
        Vector3 axisY,
        float halfWidth,
        float halfHeight)
    {
        hologram = target;
        beamMaterial = material;
        baseCenter = center;
        baseAxisX = axisX;
        baseAxisY = axisY;
        baseHalfWidth = halfWidth;
        baseHalfHeight = halfHeight;
        Rebuild();
    }

    void OnEnable()
    {
        Rebuild();
    }

    void OnDestroy()
    {
        if (_mesh == null)
            return;
        if (Application.isPlaying)
            Destroy(_mesh);
        else
            DestroyImmediate(_mesh);
        _mesh = null;
    }

    public void Rebuild()
    {
        if (hologram == null || beamMaterial == null)
            return;
        if (baseHalfWidth < 0.001f || baseHalfHeight < 0.001f)
            return;

        Vector3 axisX = baseAxisX.sqrMagnitude > 0.0001f ? baseAxisX.normalized : Vector3.right;
        Vector3 axisY = baseAxisY.sqrMagnitude > 0.0001f ? baseAxisY.normalized : Vector3.up;
        Vector3 hologramLocal = transform.InverseTransformPoint(hologram.position);
        Vector3 rise = hologramLocal - baseCenter;
        float distance = rise.magnitude;
        if (distance < 0.05f)
            return;

        Vector3 dir = rise / distance;
        float radius = HologramRadius();
        Vector3 topCenter = hologramLocal - dir * (radius * topAlong);
        float topRadius = Mathf.Max(0.08f, radius * topRadiusScale);
        float apex = Mathf.Clamp(Mathf.Min(baseHalfWidth, baseHalfHeight) * 0.07f, 0.03f, 0.055f);

        Vector3 coneX = Vector3.Cross(dir, axisY);
        if (coneX.sqrMagnitude < 0.0001f)
            coneX = Vector3.Cross(dir, axisX);
        coneX.Normalize();
        Vector3 coneY = Vector3.Cross(dir, coneX).normalized;

        int count = Mathf.Clamp(segments, 12, 64);
        var vertices = new List<Vector3>((count + 1) * 2 + 4);
        var uvs = new List<Vector2>(vertices.Capacity);
        var colors = new List<Color>(vertices.Capacity);
        var triangles = new List<int>(count * 6 + 6);

        for (int i = 0; i <= count; i++)
        {
            float t = i / (float)count;
            float angle = t * Mathf.PI * 2f;
            float c = Mathf.Cos(angle);
            float s = Mathf.Sin(angle);

            vertices.Add(baseCenter + coneX * (c * apex) + coneY * (s * apex));
            vertices.Add(topCenter + coneX * (c * topRadius) + coneY * (s * topRadius));
            uvs.Add(new Vector2(t, 0f));
            uvs.Add(new Vector2(t, 1f));
            colors.Add(new Color(0f, 0f, 0f, 1f));
            colors.Add(new Color(0f, 0f, 0f, 1f));
        }

        for (int i = 0; i < count; i++)
        {
            int a = i * 2;
            triangles.Add(a);
            triangles.Add(a + 1);
            triangles.Add(a + 2);
            triangles.Add(a + 2);
            triangles.Add(a + 1);
            triangles.Add(a + 3);
        }

        int plate = vertices.Count;
        float glow = apex * 2.4f;
        vertices.Add(baseCenter - coneX * glow - coneY * glow);
        vertices.Add(baseCenter + coneX * glow - coneY * glow);
        vertices.Add(baseCenter - coneX * glow + coneY * glow);
        vertices.Add(baseCenter + coneX * glow + coneY * glow);
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(1f, 0f));
        uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(1f, 1f));
        for (int i = 0; i < 4; i++)
            colors.Add(new Color(1f, 0f, 0f, 1f));
        triangles.Add(plate);
        triangles.Add(plate + 2);
        triangles.Add(plate + 1);
        triangles.Add(plate + 1);
        triangles.Add(plate + 2);
        triangles.Add(plate + 3);

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "HologramBeam" };
            _mesh.hideFlags = HideFlags.DontSave;
            _mesh.MarkDynamic();
        }

        _mesh.Clear();
        _mesh.SetVertices(vertices);
        _mesh.SetUVs(0, uvs);
        _mesh.SetColors(colors);
        _mesh.SetTriangles(triangles, 0);
        _mesh.RecalculateBounds();

        if (_filter == null)
            _filter = GetComponent<MeshFilter>();
        if (_filter == null)
            _filter = gameObject.AddComponent<MeshFilter>();
        _filter.sharedMesh = _mesh;

        if (_renderer == null)
            _renderer = GetComponent<MeshRenderer>();
        if (_renderer == null)
            _renderer = gameObject.AddComponent<MeshRenderer>();

        _renderer.sharedMaterial = beamMaterial;
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    float HologramRadius()
    {
        var sphere = hologram.GetComponent<SphereCollider>();
        float localRadius = sphere != null ? sphere.radius : 0.5f;
        Vector3 scale = hologram.lossyScale;
        return localRadius * Mathf.Max(scale.x, scale.y, scale.z);
    }

}
