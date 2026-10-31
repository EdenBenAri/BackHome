using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Soft red volume shared by every area king. Fits the body once and follows
/// the creature, including its radial up on spherical planets.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AreaKing))]
[DefaultExecutionOrder(100)]
public class AreaKingGlow : MonoBehaviour
{
    const string MaterialPath = "Galaxy/Nyxara/Creatures/VFX/AreaKingGlow";
    const float RadiusMultiplier = 1.35f;

    Creature _creature;
    CreatureAnimator _anim;
    MeshRenderer _renderer;
    Mesh _mesh;

    void Awake()
    {
        _creature = GetComponent<Creature>();
        _anim = GetComponent<CreatureAnimator>();
        if (_anim == null)
            _anim = GetComponentInChildren<CreatureAnimator>();
    }

    void Start()
    {
        Material material = Resources.Load<Material>(MaterialPath);
        if (material == null)
        {
            Debug.LogWarning("AreaKingGlow: missing glow material.", this);
            return;
        }

        if (!TryGetBodyBounds(out Bounds bounds))
            return;

        // Work in the root's local space so model import rotations and nested
        // scales do not turn a tall king into an oversized or off-centre halo.
        float radiusX = Mathf.Max(0.25f, bounds.extents.x) * RadiusMultiplier;
        float radiusZ = Mathf.Max(0.25f, bounds.extents.z) * RadiusMultiplier;
        float height = Mathf.Max(0.5f, bounds.size.y);
        Vector3 center = new Vector3(bounds.center.x, bounds.min.y + height * 0.42f, bounds.center.z);
        // A closed box bounds the shader's ellipsoid. The shader integrates the
        // visible volume along each camera ray instead of drawing a flat disc.
        _mesh = new Mesh { name = "Area King Glow Volume", hideFlags = HideFlags.DontSave };
        _mesh.vertices = new[]
        {
            new Vector3(-1, -1, -1), new Vector3(1, -1, -1),
            new Vector3(1, 1, -1), new Vector3(-1, 1, -1),
            new Vector3(-1, -1, 1), new Vector3(1, -1, 1),
            new Vector3(1, 1, 1), new Vector3(-1, 1, 1)
        };
        _mesh.triangles = new[]
        {
            0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
            0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5,
            0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2
        };
        _mesh.RecalculateBounds();

        // VFX naming keeps the volume out of body outlines and shadow fitting.
        var glow = new GameObject("King Glow VFX") { layer = gameObject.layer };
        glow.transform.SetParent(transform, false);
        glow.transform.localPosition = center;
        glow.transform.localScale = new Vector3(radiusX, height * 0.6f, radiusZ);
        glow.AddComponent<MeshFilter>().sharedMesh = _mesh;
        _renderer = glow.AddComponent<MeshRenderer>();
        _renderer.sharedMaterial = material;
        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.lightProbeUsage = LightProbeUsage.Off;
        _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        UpdateVisibility();
    }

    void OnEnable() => UpdateVisibility();

    void LateUpdate() => UpdateVisibility();

    void UpdateVisibility()
    {
        if (_renderer != null)
            _renderer.enabled = isActiveAndEnabled && _creature != null && _creature.IsAlive
                && (_anim == null || !_anim.IsSpawnAnimationPlaying);
    }

    void OnDisable()
    {
        if (_renderer != null)
            _renderer.enabled = false;
    }

    void OnDestroy()
    {
        if (_renderer != null)
            Destroy(_renderer.gameObject);
        if (_mesh != null)
            Destroy(_mesh);
    }

    bool TryGetBodyBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        // Slime's Hop pivot is stretched to a wide puddle by its spawn animation
        // before Start. Fit in its unscaled space, so that pose is not baked into
        // the permanent halo size. Imported model scale below Hop is preserved.
        Transform hop = transform.Find("Hop");
        foreach (Renderer body in GetComponentsInChildren<Renderer>(true))
        {
            if (!ToonOutlineSubjects.IsBody(body, transform))
                continue;

            Bounds local = body.localBounds;
            // Skinned renderer bounds include the entire animation envelope.
            // Bake once to fit the visible body, rather than that oversized box.
            if (body is SkinnedMeshRenderer skin && skin.sharedMesh != null)
            {
                var baked = new Mesh();
                skin.BakeMesh(baked, true);
                local = baked.bounds;
                if (Application.isPlaying)
                    Destroy(baked);
                else
                    DestroyImmediate(baked);
            }
            Matrix4x4 toRoot = transform.worldToLocalMatrix * body.transform.localToWorldMatrix;
            if (hop != null && body.transform.IsChildOf(hop))
            {
                Matrix4x4 restHop = Matrix4x4.TRS(hop.localPosition, hop.localRotation, Vector3.one);
                toRoot = transform.worldToLocalMatrix * hop.parent.localToWorldMatrix * restHop
                    * hop.worldToLocalMatrix * body.transform.localToWorldMatrix;
            }
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = toRoot.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z)));
                if (!found)
                {
                    bounds = new Bounds(corner, Vector3.zero);
                    found = true;
                }
                else
                    bounds.Encapsulate(corner);
            }
        }
        return found;
    }
}
