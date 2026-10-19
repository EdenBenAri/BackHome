using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Body volume for a slime. Chase movement writes the transform directly, so a collider
/// alone never stops two slimes from sharing a point. This sphere is the personal space:
/// after everyone has moved, overlapping slimes are pushed apart along the ground.
/// The collider is a trigger so it does not block the player or the ground probe.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SphereCollider))]
[DefaultExecutionOrder(120)]
public class SlimeBody : MonoBehaviour
{
    const float Skin = 0.04f;
    const float FootOffset = 0.05f;
    const int ResolvePasses = 6;

    [Tooltip("Size the sphere from the slime mesh the first time it wakes up.")]
    [SerializeField] bool fitToMesh = true;

    static readonly List<SlimeBody> All = new();
    static readonly RaycastHit[] StickHits = new RaycastHit[24];
    static SphericalPlanet _planet;
    static PlayerVitals _player;
    static int _resolvedFrame = -1;
    static int _nextBodyId;

    SphereCollider _collider;
    Creature _creature;
    Vector3 _push;
    int _bodyId;
    bool _moved;

    bool Occupies =>
        isActiveAndEnabled
        && _collider != null
        && _collider.enabled
        && (_creature == null || _creature.IsAlive);

    bool CanBePushed => Occupies && (_creature == null || !_creature.IsFrozen);

    float WorldRadius
    {
        get
        {
            Vector3 scale = transform.lossyScale;
            float axis = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            return _collider.radius * axis;
        }
    }

    void Reset()
    {
        EnsureTrigger();
    }

    void OnValidate()
    {
        EnsureTrigger();
    }

    void Awake()
    {
        _collider = GetComponent<SphereCollider>();
        _creature = GetComponent<Creature>();
        _bodyId = ++_nextBodyId;
        EnsureTrigger();
        if (fitToMesh)
            FitToMesh();
    }

    void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    void LateUpdate()
    {
        if (_resolvedFrame == Time.frameCount)
            return;

        _resolvedFrame = Time.frameCount;
        ResolveAll();
    }

    void EnsureTrigger()
    {
        SphereCollider col = _collider != null ? _collider : GetComponent<SphereCollider>();
        if (col != null && !col.isTrigger)
            col.isTrigger = true;
    }

    void FitToMesh()
    {
        if (_collider == null || !TryGetMeshBounds(out Bounds bounds))
            return;

        float horizontal = Mathf.Max(bounds.extents.x, bounds.extents.z);
        float radius = Mathf.Clamp(horizontal, 0.45f, 2.6f);
        float y = Mathf.Clamp(bounds.center.y, radius * 0.35f, Mathf.Max(radius, bounds.max.y));
        _collider.center = new Vector3(bounds.center.x, y, bounds.center.z);
        _collider.radius = radius;
    }

    bool TryGetMeshBounds(out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            Mesh mesh = null;
            if (renderer is SkinnedMeshRenderer skin)
                mesh = skin.sharedMesh;
            else
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null)
                    mesh = filter.sharedMesh;
            }

            if (mesh == null)
                continue;

            Bounds meshBounds = mesh.bounds;
            Vector3 center = meshBounds.center;
            Vector3 extents = meshBounds.extents;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        Vector3 local = transform.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                        if (!any)
                        {
                            bounds = new Bounds(local, Vector3.zero);
                            any = true;
                        }
                        else
                        {
                            bounds.Encapsulate(local);
                        }
                    }
                }
            }
        }

        return any;
    }

    static void ResolveAll()
    {
        if (_planet == null)
        {
            _planet = SphericalPlanet.Instance != null
                ? SphericalPlanet.Instance
                : FindAnyObjectByType<SphericalPlanet>();
        }

        if (_player == null)
            _player = FindAnyObjectByType<PlayerVitals>();

        SphericalPlanet planet = _planet;
        PlayerVitals player = _player;
        bool hasPlayer = player != null;
        Vector3 playerPos = hasPlayer ? player.transform.position : default;

        for (int pass = 0; pass < ResolvePasses; pass++)
        {
            for (int i = 0; i < All.Count; i++)
                All[i]._push = Vector3.zero;

            bool any = false;
            for (int i = 0; i < All.Count; i++)
            {
                SlimeBody a = All[i];
                if (!a.Occupies)
                    continue;

                for (int j = i + 1; j < All.Count; j++)
                {
                    SlimeBody b = All[j];
                    if (b.Occupies && Separate(a, b, planet, hasPlayer, playerPos))
                        any = true;
                }
            }

            if (!any)
                break;

            for (int i = 0; i < All.Count; i++)
                All[i].ApplyPush(planet, hasPlayer, playerPos);
        }

        for (int i = 0; i < All.Count; i++)
            All[i].StickIfMoved(planet);
    }

    static bool Separate(SlimeBody a, SlimeBody b, SphericalPlanet planet, bool hasPlayer, Vector3 playerPos)
    {
        float reach = a.WorldRadius + b.WorldRadius;
        if ((a.transform.position - b.transform.position).sqrMagnitude > (reach + 0.5f) * (reach + 0.5f))
            return false;

        Vector3 up = PairUp(a, b, planet);
        Vector3 aCenter = a.PlanarCenter(up);
        Vector3 bCenter = b.PlanarCenter(up);
        Vector3 delta = aCenter - bCenter;
        float dist = delta.magnitude;
        float neededGap = reach;
        if (dist >= neededGap - Skin)
            return false;

        Vector3 awayA;
        float needed;
        if (dist < 0.05f)
        {
            awayA = PairSide(up, a, b, hasPlayer, playerPos);
            needed = neededGap;
        }
        else
        {
            awayA = delta / dist;
            needed = neededGap - dist;
        }

        bool aMoves = a.CanBePushed;
        bool bMoves = b.CanBePushed;
        if (!aMoves && !bMoves)
            return false;

        float shareA = aMoves && bMoves ? 0.5f : (aMoves ? 1f : 0f);
        float shareB = 1f - shareA;

        if (hasPlayer && aMoves && bMoves)
        {
            float da = PlanarDistance(a.transform.position, playerPos, up);
            float db = PlanarDistance(b.transform.position, playerPos, up);
            if (da + 0.2f < db)
            {
                shareA = 0f;
                shareB = 1f;
            }
            else if (db + 0.2f < da)
            {
                shareA = 1f;
                shareB = 0f;
            }
        }

        if (hasPlayer)
        {
            Vector3 mid = (a.transform.position + b.transform.position) * 0.5f;
            Vector3 toPlayer = Vector3.ProjectOnPlane(playerPos - mid, up);
            if (toPlayer.sqrMagnitude > 0.04f && Mathf.Abs(Vector3.Dot(awayA, toPlayer.normalized)) > 0.6f)
            {
                Vector3 side = Vector3.Cross(up, toPlayer.normalized);
                if (side.sqrMagnitude > 0.0001f)
                {
                    side.Normalize();
                    float sign = Vector3.Dot(awayA, side);
                    if (Mathf.Abs(sign) < 0.15f)
                        sign = a._bodyId < b._bodyId ? -1f : 1f;
                    else
                        sign = Mathf.Sign(sign);

                    awayA = side * sign;
                    float gap = neededGap * neededGap - dist * dist;
                    needed = gap > 0.0001f ? Mathf.Sqrt(gap) : needed;
                }
            }
        }

        if (shareA > 0f)
            a._push += awayA * (needed * shareA);
        if (shareB > 0f)
            b._push -= awayA * (needed * shareB);
        return true;
    }

    void ApplyPush(SphericalPlanet planet, bool hasPlayer, Vector3 playerPos)
    {
        if (!CanBePushed || _push.sqrMagnitude < 0.000001f)
        {
            _push = Vector3.zero;
            return;
        }

        Vector3 up = Up(planet);
        Vector3 push = Vector3.ProjectOnPlane(_push, up);
        _push = Vector3.zero;

        if (hasPlayer)
        {
            Vector3 toPlayer = Vector3.ProjectOnPlane(playerPos - transform.position, up);
            if (toPlayer.sqrMagnitude > 0.04f)
            {
                Vector3 forward = toPlayer.normalized;
                float inward = Vector3.Dot(push, forward);
                if (inward > 0f)
                    push -= forward * inward;
            }
        }

        float maxStep = WorldRadius * 2f;
        if (push.magnitude > maxStep)
            push = push.normalized * maxStep;
        if (push.sqrMagnitude < 0.000001f)
            return;

        Vector3 next = transform.position + push;
        if (planet != null)
        {
            float keep = (transform.position - planet.Center).magnitude;
            Vector3 fromCenter = next - planet.Center;
            if (fromCenter.sqrMagnitude > 0.0001f && keep > 0.01f)
                next = planet.Center + fromCenter.normalized * keep;
        }

        transform.position = next;
        _moved = true;
    }

    void StickIfMoved(SphericalPlanet planet)
    {
        if (!_moved)
            return;

        _moved = false;
        if (planet == null)
            return;

        PlanetTileMap tiles = planet.GetComponent<PlanetTileMap>();
        Vector3 next = PlanetWalkBand.ClampPosition(planet, tiles, transform.position, FootOffset);
        Vector3 fromCenter = next - planet.Center;
        if (fromCenter.sqrMagnitude < 0.0001f)
        {
            transform.position = next;
            return;
        }

        Vector3 radial = fromCenter.normalized;
        float cast = 6f;
        System.Array.Clear(StickHits, 0, StickHits.Length);
        int count = Physics.RaycastNonAlloc(
            next + radial * cast,
            -radial,
            StickHits,
            cast + 8f,
            1 << 3,
            QueryTriggerInteraction.Ignore);

        if (count > 0 && tiles != null && tiles.TryPickWalkSurfaceHit(StickHits, planet.Center, -radial, out RaycastHit best))
        {
            Vector3 normal = best.normal.sqrMagnitude > 0.001f ? best.normal.normalized : radial;
            if (Vector3.Dot(normal, radial) < 0f)
                normal = -normal;
            next = best.point + normal * FootOffset;
        }

        transform.position = next;
    }

    Vector3 PlanarCenter(Vector3 up)
    {
        Vector3 center = transform.TransformPoint(_collider.center);
        return center - up * Vector3.Dot(center - transform.position, up);
    }

    Vector3 Up(SphericalPlanet planet)
    {
        if (planet != null)
            return planet.GetUpAt(transform.position);
        return transform.up.sqrMagnitude > 0.0001f ? transform.up : Vector3.up;
    }

    static Vector3 PairUp(SlimeBody a, SlimeBody b, SphericalPlanet planet)
    {
        Vector3 up = a.Up(planet) + b.Up(planet);
        return up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
    }

    static float PlanarDistance(Vector3 from, Vector3 to, Vector3 up)
    {
        return Vector3.ProjectOnPlane(to - from, up).magnitude;
    }

    static Vector3 PairSide(Vector3 up, SlimeBody a, SlimeBody b, bool hasPlayer, Vector3 playerPos)
    {
        Vector3 side;
        if (hasPlayer)
        {
            Vector3 toPlayer = Vector3.ProjectOnPlane(playerPos - a.transform.position, up);
            side = Vector3.Cross(up, toPlayer);
        }
        else
        {
            side = Vector3.zero;
        }

        if (side.sqrMagnitude < 0.0001f)
            side = Vector3.Cross(up, Vector3.forward);
        if (side.sqrMagnitude < 0.0001f)
            side = Vector3.Cross(up, Vector3.right);

        side.Normalize();
        if (a._bodyId > b._bodyId)
            side = -side;
        return side;
    }
}
