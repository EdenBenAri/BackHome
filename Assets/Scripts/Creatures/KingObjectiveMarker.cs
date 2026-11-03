using UnityEngine;

/// <summary>One camera-facing exclamation mark for the chapter's current unfinished king.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1100)]
public sealed class KingObjectiveMarker : MonoBehaviour
{
    [SerializeField] Transform visual;
    [Header("Above the King")]
    [SerializeField, Min(.1f)] float worldHeight = 1.8f;
    [SerializeField, Min(0f)] float headClearance = .55f;
    [SerializeField, Min(0f)] float bobHeight = .12f;
    [SerializeField, Min(.1f)] float bobPeriod = 1.8f;
    [Tooltip("Keep the mark readable at normal distance without letting close cinematic shots fill the screen.")]
    [SerializeField] Vector2 screenHeightRange = new(36f, 76f);

    public Creature Target { get; private set; }
    public bool IsVisible => visual != null && visual.gameObject.activeInHierarchy;

    CreatureSpawner _spawner;
    Camera _camera;
    PlayerVitals _player;
    PlanetStageProgression _progress;
    PlanetStageFog _fog;
    Bounds _bodyBounds;
    float _nextResolve;
    bool _dirty = true;

    public static void CreateFor(CreatureSpawner spawner)
    {
        if (spawner.GetComponentInChildren<KingObjectiveMarker>(true) != null) return;
        var prefab = Resources.Load<KingObjectiveMarker>("Tutorial/KingObjectiveMarker");
        if (prefab == null)
        {
            Debug.LogWarning("Missing Tutorial/KingObjectiveMarker.prefab.", spawner);
            return;
        }
        var marker = Instantiate(prefab, spawner.transform, false);
        marker.name = "Current King Objective";
        marker.Bind(spawner);
    }

    public void Bind(CreatureSpawner spawner)
    {
        Unsubscribe();
        _spawner = spawner;
        _progress = FindAnyObjectByType<PlanetStageProgression>();
        _fog = _progress != null ? _progress.GetComponent<PlanetStageFog>() : null;
        _player = FindAnyObjectByType<PlayerVitals>();
        if (isActiveAndEnabled) Subscribe();
        _dirty = true;
    }

    void Awake() => SetVisible(false);
    void OnEnable()
    {
        ToonOutlineSubjects.MarkObjective(this);
        Subscribe();
        _dirty = true;
    }
    void OnDisable()
    {
        Unsubscribe();
        ToonOutlineSubjects.Unmark(this);
        SetVisible(false);
    }
    void Subscribe()
    {
        if (_spawner != null) _spawner.KingDefeatedThisVisit += Invalidate;
        if (_progress != null) _progress.ProgressChanged += Invalidate;
    }
    void Unsubscribe()
    {
        if (_spawner != null) _spawner.KingDefeatedThisVisit -= Invalidate;
        if (_progress != null) _progress.ProgressChanged -= Invalidate;
    }
    void Invalidate() => _dirty = true;

    void LateUpdate()
    {
        if (_spawner == null || !_spawner.isActiveAndEnabled || (_player != null && !_player.IsAlive))
        {
            SetVisible(false);
            return;
        }
        if (_dirty || Time.unscaledTime >= _nextResolve || (Target != null && !Target.IsAlive))
        {
            _dirty = false;
            _nextResolve = Time.unscaledTime + .25f;
            _spawner.TryGetCurrentKingObjective(out var next);
            if (Target != next)
            {
                Target = next;
                if (Target != null) _bodyBounds = MeasureBody(Target.transform);
            }
        }
        if (_camera == null) _camera = Camera.main;
        bool covered = Target != null && _progress != null && _fog != null
            && (_fog.CoveredStagesMask & (1 << (int)_progress.StageFor(Target.transform))) != 0;
        if (Target == null || !Target.IsAlive || !Target.isActiveAndEnabled || covered || _camera == null)
        {
            SetVisible(false);
            return;
        }

        // Project the fitted body onto camera-up, keeping the sign above its silhouette
        // even on the far side of a spherical planet or in a steep overhead shot.
        Vector3 center = Target.transform.TransformPoint(_bodyBounds.center);
        Vector3 up = _camera.transform.up;
        float top = 0f;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = _bodyBounds.center + Vector3.Scale(_bodyBounds.extents, new Vector3(x, y, z));
            top = Mathf.Max(top, Vector3.Dot(Target.transform.TransformPoint(corner) - center, up));
        }
        float bob = Mathf.Sin(Time.unscaledTime * (Mathf.PI * 2f / Mathf.Max(.1f, bobPeriod))) * bobHeight;
        Vector3 anchor = center + up * (top + headClearance + bob);
        float depth = Vector3.Dot(anchor - _camera.transform.position, _camera.transform.forward);
        if (depth <= _camera.nearClipPlane) { SetVisible(false); return; }

        float viewHeight = _camera.orthographic ? 2f * _camera.orthographicSize
            : 2f * depth * Mathf.Tan(_camera.fieldOfView * Mathf.Deg2Rad * .5f);
        float height = Mathf.Clamp(worldHeight, viewHeight * screenHeightRange.x / 1080f,
            viewHeight * Mathf.Max(screenHeightRange.x, screenHeightRange.y) / 1080f);
        transform.SetPositionAndRotation(anchor, _camera.transform.rotation);
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(height / Mathf.Max(.001f, Mathf.Abs(parentScale.x)),
            height / Mathf.Max(.001f, Mathf.Abs(parentScale.y)), height / Mathf.Max(.001f, Mathf.Abs(parentScale.z)));
        SetVisible(true);
    }

    void SetVisible(bool visible)
    {
        if (visual != null && visual.gameObject.activeSelf != visible) visual.gameObject.SetActive(visible);
    }

    static Bounds MeasureBody(Transform root)
    {
        bool found = false;
        Bounds bounds = new(Vector3.up, Vector3.one * 2f);
        Transform hop = root.Find("Hop");
        foreach (Renderer body in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!ToonOutlineSubjects.IsBody(body, root)) continue;
            Bounds local = body is SkinnedMeshRenderer skin && skin.sharedMesh != null
                ? skin.sharedMesh.bounds : body.localBounds;
            Matrix4x4 toRoot = root.worldToLocalMatrix * body.transform.localToWorldMatrix;
            if (hop != null && body.transform.IsChildOf(hop))
                toRoot = root.worldToLocalMatrix * hop.parent.localToWorldMatrix
                    * Matrix4x4.TRS(hop.localPosition, hop.localRotation, Vector3.one)
                    * hop.worldToLocalMatrix * body.transform.localToWorldMatrix;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 point = toRoot.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z)));
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                else bounds.Encapsulate(point);
            }
        }
        return bounds;
    }
}
