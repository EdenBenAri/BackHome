using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One flat circle under a creature. The quad is parented, so it rides the
/// body with no per-frame work. Diameter and the sideways offset come from
/// the mesh's local bounds, which do not reskin the character.
/// </summary>
[DisallowMultipleComponent]
public class CreatureBlobShadow : MonoBehaviour
{
    const float WidthFraction = 0.8f;
    const float SlimeScale = 1.25f;
    const float KingScale = 1.45f;
    const float MinDiameter = 0.45f;
    const float MaxDiameter = 6f;
    const float Lift = 0.04f;
    const float Opacity = 0.5f;

    static Mesh s_quad;
    static Material s_material;
    static bool s_loggedMissing;

    void Start()
    {
        Material material = SharedMaterial();
        Mesh quad = SharedQuad();
        if (material == null || quad == null)
            return;

        Renderer[] bodies = CollectBodies();
        float diameter = FitDiameter(bodies);
        Vector3 lossy = transform.lossyScale;
        float sx = Mathf.Max(0.0001f, Mathf.Abs(lossy.x));
        float sy = Mathf.Max(0.0001f, Mathf.Abs(lossy.y));
        float sz = Mathf.Max(0.0001f, Mathf.Abs(lossy.z));

        var shadow = new GameObject("Shadow");
        Transform shadowTransform = shadow.transform;
        shadowTransform.SetParent(transform, false);
        shadowTransform.localRotation = Quaternion.identity;
        shadowTransform.localScale = new Vector3(diameter / sx, 1f, diameter / sz);
        Vector3 local = LocalCenter(bodies);
        local.y = Lift / sy;
        shadowTransform.localPosition = local;

        MeshFilter filter = shadow.AddComponent<MeshFilter>();
        filter.sharedMesh = quad;

        MeshRenderer renderer = shadow.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        renderer.renderingLayerMask = 1;
    }

    Vector3 LocalCenter(Renderer[] bodies)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < bodies.Length; i++)
        {
            Renderer body = bodies[i];
            if (body == null)
                continue;

            Vector3 world = body.transform.TransformPoint(body.localBounds.center);
            sum += transform.InverseTransformPoint(world);
            count++;
        }

        if (count == 0)
            return Vector3.zero;

        Vector3 local = sum / count;
        local.y = 0f;
        return local;
    }

    Renderer[] CollectBodies()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        int count = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (IsBody(renderers[i], transform))
                count++;
        }

        var bodies = new Renderer[count];
        int write = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!IsBody(renderers[i], transform))
                continue;
            bodies[write] = renderers[i];
            write++;
        }

        return bodies;
    }

    float FitDiameter(Renderer[] bodies)
    {
        float width = 0f;
        for (int i = 0; i < bodies.Length; i++)
        {
            Renderer renderer = bodies[i];
            if (renderer == null)
                continue;

            Vector3 extents = Vector3.Scale(renderer.localBounds.extents, Abs(renderer.transform.lossyScale));
            width = Mathf.Max(width, HorizontalWidth(extents));
        }

        if (width < 0.05f)
            width = 0.9f;
        return Mathf.Clamp(width * WidthFraction * KindScale(), MinDiameter, MaxDiameter);
    }

    float KindScale()
    {
        float scale = 1f;
        if (GetComponent<SlimeBody>() != null)
            scale *= SlimeScale;
        if (GetComponent<AreaKing>() != null)
            scale *= KingScale;
        return scale;
    }

    static bool IsBody(Renderer renderer, Transform root)
    {
        if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
            return false;
        if (renderer.GetComponentInParent<VitalsBarsView>(true) != null)
            return false;
        if (renderer.GetComponentInParent<Canvas>(true) != null)
            return false;

        Transform cursor = renderer.transform;
        while (cursor != null && cursor != root)
        {
            if (NameBlocked(cursor.name))
                return false;
            cursor = cursor.parent;
        }

        return true;
    }

    static bool NameBlocked(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return name.IndexOf("vfx", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("slash", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("trail", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("vitals", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("popup", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("shadow", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static float HorizontalWidth(Vector3 extents)
    {
        float x = extents.x;
        float y = extents.y;
        float z = extents.z;
        if (y > x && y > z)
            return Mathf.Max(x, z) * 2f;
        if (x > y && x > z)
            return Mathf.Max(y, z) * 2f;
        if (z > x && z > y)
            return Mathf.Max(x, y) * 2f;
        return Mathf.Max(x, Mathf.Max(y, z)) * 2f;
    }

    static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    static Mesh SharedQuad()
    {
        if (s_quad != null)
            return s_quad;

        s_quad = new Mesh { name = "Creature Blob Shadow" };
        s_quad.hideFlags = HideFlags.HideAndDontSave;
        s_quad.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, 0.5f),
            new Vector3(-0.5f, 0f, 0.5f)
        };
        s_quad.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        // Faces local up, which is the planet up on a creature.
        s_quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        s_quad.RecalculateBounds();
        return s_quad;
    }

    static Material SharedMaterial()
    {
        if (s_material != null)
            return s_material;

        Shader shader = Shader.Find("BackHome/CreatureBlobShadow");
        if (shader == null)
        {
            if (!s_loggedMissing)
            {
                s_loggedMissing = true;
                Debug.LogWarning("CreatureBlobShadow: missing shader.");
            }

            return null;
        }

        s_material = new Material(shader)
        {
            name = "Creature Blob Shadow",
            hideFlags = HideFlags.HideAndDontSave
        };
        s_material.SetColor("_Color", Color.black);
        s_material.SetFloat("_Opacity", Opacity);
        return s_material;
    }
}
