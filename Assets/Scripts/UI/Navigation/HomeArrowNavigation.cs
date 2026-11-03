using UnityEngine;

/// <summary>
/// A translucent home badge beside the player. The arrow follows the shortest
/// direction along the planet surface; the house stays upright in the camera view.
/// The view and its Layer Lab sprites are authored in HomeArrowNavigation.prefab.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1100)]
public class HomeArrowNavigation : MonoBehaviour
{
    [Header("View")]
    [SerializeField] RectTransform badge;
    [SerializeField] RectTransform arrow;
    [SerializeField] CanvasGroup canvasGroup;

    [Header("Placement")]
    [Tooltip("Distance from the player to the home badge, in canvas pixels.")]
    [SerializeField, Min(0f)] float orbitRadius = 320f;
    [Tooltip("Distance from the badge center to the arrow, in canvas pixels.")]
    [SerializeField, Min(0f)] float arrowOffset = 34f;
    [Tooltip("World height of the navigation anchor above the player's feet.")]
    [SerializeField] float feetLift = 1.5f;
    [Tooltip("Hide just before reaching the camp trigger's boundary.")]
    [SerializeField, Min(0f)] float arrivalPadding = 1f;

    [Header("Smoothing")]
    [Tooltip("Smooth the player's screen anchor to filter walking and terrain jitter.")]
    [SerializeField, Min(0.01f)] float positionSmoothTime = 0.1f;
    [Tooltip("Smooth the heading while keeping the badge and arrow on the same bearing.")]
    [SerializeField, Min(0.01f)] float directionSmoothTime = 0.14f;

    [Header("Appearance")]
    [SerializeField, Range(0f, 1f)] float opacity = 0.55f;
    [SerializeField, Min(0.01f)] float fadeSeconds = 0.2f;

    Canvas _canvas;
    RectTransform _canvasRect;
    PlayerVitals _player;
    CampRadiusRing _camp;
    SphereCollider _campCollider;
    float _nextCampSearch;
    Vector3 _lastDirection;
    Vector2 _smoothedAnchor;
    Vector2 _anchorVelocity;
    float _smoothedAngle;
    float _angleVelocity;
    bool _hasPlacement;

    void Awake()
    {
        _canvas = GetComponent<Canvas>();
        _canvasRect = transform as RectTransform;
        _player = GetComponentInParent<PlayerVitals>();
        // Keep the authored nested prefab under Player. Placement uses overlay
        // pixels rather than the parent's moving world transform.
        HideImmediate();
    }

    void OnDisable()
    {
        HideImmediate();
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        SphericalPlanet planet = SphericalPlanet.Instance;
        if (_player == null || !_player.isActiveAndEnabled || !_player.IsAlive || planet == null || cam == null
            || badge == null || arrow == null || canvasGroup == null || _canvas == null)
        {
            HideImmediate();
            return;
        }

        if (!ResolveCamp(planet))
        {
            HideImmediate();
            return;
        }

        Vector3 playerPosition = _player.transform.position;
        bool arrived = _player.IsRecovering
            || (_campCollider.ClosestPoint(playerPosition) - playerPosition).sqrMagnitude
                <= arrivalPadding * arrivalPadding;

        bool placed = Place(cam, planet, playerPosition);
        float target = !arrived && placed ? opacity : 0f;
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, target,
            Time.deltaTime / Mathf.Max(0.01f, fadeSeconds));
        _canvas.enabled = canvasGroup.alpha > 0.001f;
        if (!_canvas.enabled)
            _hasPlacement = false;
    }

    bool ResolveCamp(SphericalPlanet planet)
    {
        if (_camp != null && _camp.isActiveAndEnabled && _campCollider != null
            && _campCollider.enabled && _camp.GetComponentInParent<SphericalPlanet>() == planet)
            return true;

        if (Time.unscaledTime < _nextCampSearch)
            return false;

        _nextCampSearch = Time.unscaledTime + 1f;
        _camp = null;
        _campCollider = null;
        float nearest = float.PositiveInfinity;
        foreach (CampRadiusRing candidate in FindObjectsByType<CampRadiusRing>(FindObjectsInactive.Exclude))
        {
            if (!candidate.isActiveAndEnabled || candidate.GetComponentInParent<SphericalPlanet>() != planet)
                continue;

            SphereCollider volume = candidate.GetComponent<SphereCollider>();
            if (volume == null || !volume.enabled)
                continue;

            float distance = (volume.bounds.center - _player.transform.position).sqrMagnitude;
            if (distance >= nearest)
                continue;

            nearest = distance;
            _camp = candidate;
            _campCollider = volume;
        }

        return _camp != null;
    }

    bool Place(Camera cam, SphericalPlanet planet, Vector3 playerPosition)
    {
        Vector3 up = planet.GetUpAt(playerPosition);
        // Project the destination onto the local tangent, not directly onto the
        // screen: camp can be on the other side of the spherical horizon.
        Vector3 direction = Vector3.ProjectOnPlane(_campCollider.bounds.center - playerPosition, up);
        if (direction.sqrMagnitude < 0.0001f)
        {
            // At the exact antipode every heading is equally short. Keep the
            // previous heading instead of losing or abruptly flipping the arrow.
            direction = Vector3.ProjectOnPlane(_lastDirection, up);
            if (direction.sqrMagnitude < 0.0001f)
                direction = Vector3.ProjectOnPlane(cam.transform.right, up);
        }
        direction.Normalize();
        _lastDirection = direction;

        Vector3 anchor = playerPosition + up * feetLift;
        Vector3 screen = cam.WorldToScreenPoint(anchor);
        if (screen.z <= 0f)
            return false;

        Vector2 screenDirection = (Vector2)(cam.WorldToScreenPoint(anchor + direction) - screen);
        if (screenDirection.sqrMagnitude < 0.0001f)
            return false;
        screenDirection.Normalize();

        Rect pixels = _canvas.pixelRect;
        if (pixels.width <= 0f || pixels.height <= 0f)
            return false;

        // Map pixels directly into the centered overlay. This does not depend on
        // when Unity updates the canvas's world transform during the render loop.
        Vector2 targetAnchor = new Vector2(
            ((screen.x - pixels.xMin) / pixels.width - 0.5f) * _canvasRect.rect.width,
            ((screen.y - pixels.yMin) / pixels.height - 0.5f) * _canvasRect.rect.height);
        float targetAngle = Mathf.Atan2(screenDirection.y, screenDirection.x) * Mathf.Rad2Deg;

        if (!_hasPlacement)
        {
            _smoothedAnchor = targetAnchor;
            _smoothedAngle = targetAngle;
            _anchorVelocity = Vector2.zero;
            _angleVelocity = 0f;
            _hasPlacement = true;
        }
        else if (Time.deltaTime > 0f)
        {
            _smoothedAnchor = Vector2.SmoothDamp(_smoothedAnchor, targetAnchor,
                ref _anchorVelocity, positionSmoothTime, Mathf.Infinity, Time.deltaTime);
            _smoothedAngle = Mathf.SmoothDampAngle(_smoothedAngle, targetAngle,
                ref _angleVelocity, directionSmoothTime, Mathf.Infinity, Time.deltaTime);
        }

        float radians = _smoothedAngle * Mathf.Deg2Rad;
        Vector2 bearing = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        // Smooth the anchor and bearing separately: the orbit never cuts through
        // the player on a turn, and the triangle always points out from the badge.
        badge.anchoredPosition = _smoothedAnchor + bearing * orbitRadius;
        arrow.anchoredPosition = bearing * arrowOffset;
        arrow.localRotation = Quaternion.Euler(0f, 0f, _smoothedAngle);
        return true;
    }

    void HideImmediate()
    {
        _hasPlacement = false;
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
        if (_canvas != null)
            _canvas.enabled = false;
    }
}
