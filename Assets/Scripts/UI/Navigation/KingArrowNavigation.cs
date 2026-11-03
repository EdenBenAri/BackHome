using UnityEngine;

/// <summary>A ground-aligned ArrowMark beside the player, pointing to the current unfinished king.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1100)]
public sealed class KingArrowNavigation : MonoBehaviour
{
    [SerializeField] Transform visual;
    [Header("Placement")]
    [Tooltip("Distance along the ground from the player to the arrow, in world units.")]
    [SerializeField, Min(.1f)] float orbitDistance = 4.2f;
    [SerializeField, Min(.1f)] float worldLength = 1.9f;
    [SerializeField, Min(0f)] float groundClearance = .22f;
    [SerializeField, Min(.01f)] float directionSmoothTime = .1f;

    public Creature Target { get; private set; }
    public bool IsVisible => visual != null && visual.gameObject.activeInHierarchy;

    CreatureSpawner _spawner;
    PlanetStageProgression _progress;
    PlayerVitals _player;
    Camera _camera;
    KingCinematicDirector _cinematic;
    SphericalPlanet _planet;
    PlanetTileMap _tiles;
    Vector3 _destination, _lastDirection;
    float _nextResolve, _nextPlayerSearch, _turnVelocity;
    bool _dirty = true, _hasDestination, _hasPlacement;

    public static void CreateFor(CreatureSpawner spawner)
    {
        if (spawner.GetComponentInChildren<KingArrowNavigation>(true) != null) return;
        var prefab = Resources.Load<KingArrowNavigation>("HUD/KingArrowNavigation");
        if (prefab == null)
        {
            Debug.LogWarning("Missing HUD/KingArrowNavigation.prefab.", spawner);
            return;
        }
        var instance = Instantiate(prefab, spawner.transform, false);
        instance.name = "Current King Arrow";
        instance.Bind(spawner);
    }

    public void Bind(CreatureSpawner spawner)
    {
        Unsubscribe();
        _spawner = spawner;
        _progress = FindAnyObjectByType<PlanetStageProgression>();
        if (isActiveAndEnabled) Subscribe();
        _dirty = true;
    }

    void Awake() => Hide();

    void OnEnable() { ToonOutlineSubjects.MarkObjective(this); Subscribe(); _dirty = true; }
    void OnDisable() { Unsubscribe(); ToonOutlineSubjects.Unmark(this); Hide(); }

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
        if (_spawner == null || !_spawner.isActiveAndEnabled || visual == null)
        {
            Hide();
            return;
        }
        if (_dirty || Time.unscaledTime >= _nextResolve || (Target != null && !Target.IsAlive))
        {
            _dirty = false;
            _nextResolve = Time.unscaledTime + .25f;
            _hasDestination = _spawner.TryGetNextKingObjective(out _destination);
            _spawner.TryGetCurrentKingObjective(out var next);
            Target = next;
        }

        // A streamed-out king is still a valid destination. If it hasn't spawned
        // yet, navigate to its den until the living instance becomes available.
        if (Target != null && Target.IsAlive) _destination = Target.transform.position;
        if (_player == null && Time.unscaledTime >= _nextPlayerSearch)
        {
            _nextPlayerSearch = Time.unscaledTime + .5f;
            _player = FindAnyObjectByType<PlayerVitals>();
        }
        Camera camera = Camera.main;
        if (_camera != camera)
        {
            _camera = camera;
            _cinematic = camera != null ? camera.GetComponent<KingCinematicDirector>() : null;
        }
        var planet = SphericalPlanet.Instance;
        if (_planet != planet)
        {
            _planet = planet;
            _tiles = planet != null ? planet.GetComponent<PlanetTileMap>() : null;
        }
        if (!_hasDestination || _player == null || !_player.isActiveAndEnabled || !_player.IsAlive
            || planet == null || _camera == null || (_cinematic != null && _cinematic.IsPresenting)
            || !Place(planet))
        {
            Hide();
            return;
        }
        if (!visual.gameObject.activeSelf) visual.gameObject.SetActive(true);
    }

    bool Place(SphericalPlanet planet)
    {
        Vector3 playerPosition = _player.transform.position;
        Vector3 up = planet.GetUpAt(playerPosition);
        Vector3 delta = _destination - playerPosition;
        if (delta.sqrMagnitude < .16f) return false;

        // Follow the shortest surface route, including destinations beyond the horizon.
        Vector3 direction = Vector3.ProjectOnPlane(delta, up);
        if (direction.sqrMagnitude < .0001f)
        {
            direction = Vector3.ProjectOnPlane(_lastDirection, up);
            if (direction.sqrMagnitude < .0001f)
                direction = Vector3.ProjectOnPlane(_player.transform.forward, up);
        }
        direction.Normalize();
        if (!_hasPlacement)
        {
            _turnVelocity = 0f;
            _hasPlacement = true;
        }
        else
        {
            Vector3 previous = Vector3.ProjectOnPlane(_lastDirection, up).normalized;
            if (previous.sqrMagnitude > .0001f)
            {
                float turn = Time.deltaTime > 0f
                    ? Mathf.SmoothDampAngle(0f, Vector3.SignedAngle(previous, direction, up), ref _turnVelocity,
                        directionSmoothTime, Mathf.Infinity, Time.deltaTime)
                    : 0f;
                direction = Quaternion.AngleAxis(turn, up) * previous;
            }
        }
        _lastDirection = direction;

        float radius = (playerPosition - planet.Center).magnitude;
        // Pull inward when approaching the target so the arrow never passes it.
        float distance = Mathf.Min(orbitDistance,
            PlanetSurfacePose.GetSurfaceDistance(planet.Center, playerPosition, _destination) * .45f);
        float angle = distance / Mathf.Max(.001f, radius);
        Vector3 radial = (up * Mathf.Cos(angle) + direction * Mathf.Sin(angle)).normalized;
        bool tiled = _tiles != null && _tiles.ProvidesWalkSurface;
        Vector3 anchor = tiled ? _tiles.GetWalkSurfacePoint(radial) : planet.GetSurfacePoint(radial);
        Vector3 normal = tiled ? _tiles.GetWalkSurfaceNormal(radial) : planet.GetTerrainNormal(radial);
        Vector3 forward = Vector3.ProjectOnPlane(direction, normal).normalized;
        if (forward.sqrMagnitude < .0001f) return false;
        transform.SetPositionAndRotation(anchor + normal * groundClearance, Quaternion.LookRotation(forward, normal));
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(worldLength / Mathf.Max(.001f, Mathf.Abs(parentScale.x)),
            worldLength / Mathf.Max(.001f, Mathf.Abs(parentScale.y)), worldLength / Mathf.Max(.001f, Mathf.Abs(parentScale.z)));
        return true;
    }

    void Hide()
    {
        _hasPlacement = false;
        if (visual != null && visual.gameObject.activeSelf) visual.gameObject.SetActive(false);
    }
}
