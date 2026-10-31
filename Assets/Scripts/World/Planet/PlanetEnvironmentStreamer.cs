using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Play-mode only. Hides props under this Environment object, and any creature
/// registered with <see cref="Track"/>, then turns each one on when the player
/// or the camera comes within range. Edit mode never touches active state, so
/// the full set stays visible while you place and arrange props.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
[AddComponentMenu("BackHome/Planet Environment Streamer")]
public class PlanetEnvironmentStreamer : MonoBehaviour
{
    [Tooltip("A prop turns on when the player or the camera is at least this close (meters).")]
    [SerializeField, Min(1f)] float showRadius = 46f;
    [Tooltip("A prop turns back off only after both the player and the camera are farther than this. Keep it larger than Show Radius so props don't flicker on the edge.")]
    [SerializeField, Min(1f)] float hideRadius = 54f;

    struct Entry
    {
        public GameObject Instance;
        public Vector3 Position;
        public bool Visible;
        public bool Dynamic;
    }

    static PlanetEnvironmentStreamer _instance;

    readonly List<Entry> _entries = new List<Entry>(256);
    Transform _player;
    Transform _camera;
    int _cameraTimer;
    int _cursor;
    Vector3 _lastFocus;
    bool _hasFocus;
    float _showSqr;
    float _hideSqr;

    void OnValidate()
    {
        showRadius = Mathf.Max(1f, showRadius);
        hideRadius = Mathf.Max(showRadius + 1f, hideRadius);
        _showSqr = showRadius * showRadius;
        _hideSqr = hideRadius * hideRadius;
    }

    /// <summary>
    /// Same distance rule as a placed prop. A creature far from the player and the
    /// camera is deactivated; it wakes again when either comes inside the show radius.
    /// </summary>
    public static void Track(GameObject instance)
    {
        if (instance == null || !Application.isPlaying)
            return;

        PlanetEnvironmentStreamer streamer = _instance;
        if (streamer == null)
            return;

        streamer.TrackInstance(instance);
    }

    void Awake()
    {
        // Edit mode must keep every prop visible for arranging.
        if (!Application.isPlaying)
            return;

        _instance = this;

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            showRadius = Mathf.Min(showRadius, 24f);
            hideRadius = Mathf.Max(showRadius + 4f, Mathf.Min(hideRadius, 32f));
        }

        _showSqr = showRadius * showRadius;
        _hideSqr = hideRadius * hideRadius;

        var found = new List<GameObject>(256);
        SphericalPlanet planet = GetComponentInParent<SphericalPlanet>();
        Collect(transform, planet, found);

        for (int i = 0; i < found.Count; i++)
        {
            if (IsFoliage(found[i].transform))
                StripFoliage(found[i]);
            found[i].SetActive(false);
            _entries.Add(new Entry
            {
                Instance = found[i],
                Position = found[i].transform.position,
                Visible = false
            });
        }
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    void TrackInstance(GameObject instance)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Instance == instance)
                return;
        }

        ResolveTargets();
        Vector3 position = instance.transform.position;
        bool show = BestDistanceSqr(position) <= _showSqr;
        if (instance.activeSelf != show)
            instance.SetActive(show);

        _entries.Add(new Entry
        {
            Instance = instance,
            Position = position,
            Visible = show,
            Dynamic = true
        });
    }

    void LateUpdate()
    {
        if (!Application.isPlaying || _entries.Count == 0)
            return;

        ResolveTargets();
        bool hasPlayer = _player != null;
        bool hasCamera = _camera != null;
        if (!hasPlayer && !hasCamera)
            return;

        Vector3 playerPos = hasPlayer ? _player.position : default;
        Vector3 cameraPos = hasCamera ? _camera.position : default;
        Vector3 focus = hasPlayer ? playerPos : cameraPos;
        bool teleported = _hasFocus && (focus - _lastFocus).sqrMagnitude > 25f * 25f;
        bool full = !_hasFocus || teleported;
        _lastFocus = focus;
        _hasFocus = true;

        int total = _entries.Count;
        int steps = full ? total : Mathf.Min(48, total);
        for (int s = 0; s < steps; s++)
        {
            if (_entries.Count == 0)
                break;

            int i = _cursor;
            if (i >= _entries.Count)
                i = 0;
            _cursor = i + 1;
            if (_cursor >= _entries.Count)
                _cursor = 0;

            Entry entry = _entries[i];
            GameObject instance = entry.Instance;
            if (instance == null)
            {
                _entries.RemoveAt(i);
                if (_cursor > 0)
                    _cursor--;
                continue;
            }

            Vector3 position = entry.Position;
            if (entry.Dynamic && entry.Visible)
            {
                position = instance.transform.position;
                entry.Position = position;
            }

            float best = BestDistanceSqr(position, hasPlayer, playerPos, hasCamera, cameraPos);
            bool shouldShow = entry.Visible ? best <= _hideSqr : best <= _showSqr;
            if (entry.Dynamic && !shouldShow)
                entry.Position = position;

            if (shouldShow == entry.Visible)
            {
                _entries[i] = entry;
                continue;
            }

            entry.Visible = shouldShow;
            _entries[i] = entry;
            instance.SetActive(shouldShow);
        }
    }

    float BestDistanceSqr(Vector3 position)
    {
        bool hasPlayer = _player != null;
        bool hasCamera = _camera != null;
        return BestDistanceSqr(
            position,
            hasPlayer,
            hasPlayer ? _player.position : default,
            hasCamera,
            hasCamera ? _camera.position : default);
    }

    static float BestDistanceSqr(Vector3 position, bool hasPlayer, Vector3 playerPos, bool hasCamera, Vector3 cameraPos)
    {
        float best = float.PositiveInfinity;
        if (hasPlayer)
            best = (position - playerPos).sqrMagnitude;

        if (hasCamera)
        {
            float d = (position - cameraPos).sqrMagnitude;
            if (d < best)
                best = d;
        }

        return best;
    }

    void ResolveTargets()
    {
        if (_player == null)
        {
            PlanetWalker walker = FindAnyObjectByType<PlanetWalker>();
            if (walker != null)
                _player = walker.transform;
        }

        _cameraTimer--;
        if (_camera == null || !_camera.gameObject.activeInHierarchy || _cameraTimer <= 0)
        {
            _cameraTimer = 20;
            Camera cam = Camera.main;
            _camera = cam != null ? cam.transform : null;
        }
    }

    static bool IsFoliage(Transform t)
    {
        while (t != null)
        {
            string name = t.name;
            if (name.IndexOf("Grass", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Toadstool", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            t = t.parent;
        }

        return false;
    }

    static void StripFoliage(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }

    static void Collect(Transform container, SphericalPlanet planet, List<GameObject> into)
    {
        float centerLimit = planet != null ? planet.Radius * 0.35f : 0f;
        float centerLimitSqr = centerLimit * centerLimit;
        Vector3 center = planet != null ? planet.Center : container.position;

        for (int i = 0; i < container.childCount; i++)
        {
            Transform child = container.GetChild(i);
            if (!child.gameObject.activeSelf)
                continue;

            if (IsContainer(child))
            {
                Collect(child, planet, into);
                continue;
            }

            if (child.GetComponentInChildren<Renderer>(true) == null
                && child.GetComponentInChildren<Collider>(true) == null)
                continue;

            // Props live on the surface. Objects near the planet center (a whole-planet model
            // sitting on the origin) stay active so they are not culled by a pivot at the core.
            if (planet != null && (child.position - center).sqrMagnitude < centerLimitSqr)
                continue;

            into.Add(child.gameObject);
        }
    }

    static bool IsContainer(Transform t)
    {
        if (t.GetComponent<PlanetEnvironmentObject>() != null)
            return false;
        if (t.GetComponent<Renderer>() != null
            || t.GetComponent<MeshFilter>() != null
            || t.GetComponent<Collider>() != null)
            return false;
        if (t.childCount == 0)
            return false;

        for (int i = 0; i < t.childCount; i++)
        {
            if (t.GetChild(i).GetComponent<PlanetEnvironmentObject>() != null)
                return true;
        }

        // Category folders (Grass / Rocks / Trees) sit at the environment origin.
        // A placed prefab sits on the surface, so it is one prop even if its mesh is on a child.
        return t.localPosition.sqrMagnitude < 0.0001f
               && t.GetComponent<Animator>() == null
               && t.GetComponentInChildren<Renderer>(true) != null;
    }
}
