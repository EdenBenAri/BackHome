using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Shows the Take Off button authored on this ship while the player stands nearby,
/// and turns it to face the camera. The button itself lives on the prefab so its
/// place, size, and label can be edited there. It stays hidden on the first visit
/// until the player walks away and comes back, then slides out from the hull to that
/// place, and slides back in when the player leaves. Pressing it loads the ship scene.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public class ShipTakeoffPrompt : MonoBehaviour
{
    [SerializeField] string shipSceneName = "Ship";
    [Tooltip("Button placed above the hull on this prefab.")]
    [SerializeField] Button button;
    [Tooltip("Distance from the hull, in meters, at which the button appears.")]
    [SerializeField, Min(0.5f)] float approachDistance = 5f;
    [Tooltip("Distance from the hull at which the button hides. Kept larger than approach so it does not flicker on the edge.")]
    [SerializeField, Min(0.5f)] float leaveDistance = 7f;
    [Tooltip("Seconds to travel from inside the hull out to the button's place, and back.")]
    [SerializeField, Min(0.05f)] float moveDuration = 0.45f;

    Collider _hull;
    Transform _player;
    PlayerVitals _vitals;
    RectTransform _buttonRect;
    Vector2 _restAnchored;
    Vector2 _emergeAnchored;
    Vector3 _restScale;
    float _motion;
    bool _armed;
    bool _open;
    bool _loading;

    void Awake()
    {
        _hull = GetComponent<Collider>();
        ResolveButton();
        if (button == null)
            return;

        Vector2 anchored = _buttonRect.anchoredPosition;
        Vector2 fromLocal = new Vector2(_buttonRect.localPosition.x, _buttonRect.localPosition.y);
        _restAnchored = anchored.sqrMagnitude >= fromLocal.sqrMagnitude ? anchored : fromLocal;
        _restScale = _buttonRect.localScale;
        _emergeAnchored = Vector2.Lerp(Vector2.zero, _restAnchored, 0.08f);
        button.onClick.RemoveListener(TakeOff);
        button.onClick.AddListener(TakeOff);
        button.gameObject.SetActive(false);
    }

    void OnValidate()
    {
        approachDistance = Mathf.Max(0.5f, approachDistance);
        leaveDistance = Mathf.Max(approachDistance, leaveDistance);
        moveDuration = Mathf.Max(0.05f, moveDuration);
    }

    void OnDisable()
    {
        _open = false;
        _motion = 0f;
        if (button != null)
            button.gameObject.SetActive(false);
    }

    void Update()
    {
        if (_loading || button == null)
            return;

        if (!TryGetLivingPlayer(out Transform player))
        {
            _open = false;
        }
        else
        {
            float distance = DistanceToHull(player.position);
            if (!_armed)
            {
                if (distance > leaveDistance)
                    _armed = true;
                _open = false;
            }
            else
            {
                bool near = _open ? distance <= leaveDistance : distance <= approachDistance;
                _open = near;
            }
        }

        TickMotion();

        if (_open && _motion > 0.55f && TryGetPress(out Vector2 screen) && HitsButton(screen))
            TakeOff();
    }

    void TickMotion()
    {
        if (_open && !button.gameObject.activeSelf)
        {
            ApplyPose(_motion);
            button.gameObject.SetActive(true);
        }

        float target = _open ? 1f : 0f;
        if (!Mathf.Approximately(_motion, target))
        {
            _motion = Mathf.MoveTowards(_motion, target, Time.deltaTime / moveDuration);
            ApplyPose(_motion);
        }

        if (!_open && _motion <= 0.001f && button.gameObject.activeSelf)
            button.gameObject.SetActive(false);
    }

    void ApplyPose(float motion)
    {
        if (_buttonRect == null)
            return;

        float eased = EaseOutBack(Mathf.Clamp01(motion));
        _buttonRect.anchoredPosition = Vector2.LerpUnclamped(_emergeAnchored, _restAnchored, eased);
        _buttonRect.localScale = _restScale * Mathf.LerpUnclamped(0.2f, 1f, eased);
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    void LateUpdate()
    {
        if (button == null || !button.gameObject.activeSelf || _buttonRect == null)
            return;

        Camera cam = Camera.main;
        if (cam == null)
            return;

        _buttonRect.rotation = cam.transform.rotation;

        Canvas canvas = button.GetComponent<Canvas>();
        if (canvas != null && canvas.worldCamera != cam)
            canvas.worldCamera = cam;
    }

    void ResolveButton()
    {
        if (button == null)
        {
            Transform child = transform.Find("TakeOffButton");
            if (child != null)
                button = child.GetComponent<Button>();
        }

        _buttonRect = button != null ? button.transform as RectTransform : null;
    }

    bool TryGetLivingPlayer(out Transform player)
    {
        player = null;
        if (_player == null)
        {
            GameObject found = GameObject.FindGameObjectWithTag("Player");
            if (found == null)
                return false;

            _player = found.transform;
            _vitals = found.GetComponent<PlayerVitals>();
            if (_vitals == null)
                _vitals = found.GetComponentInChildren<PlayerVitals>();
        }

        if (_vitals != null && !_vitals.IsAlive)
            return false;

        player = _player;
        return true;
    }

    float DistanceToHull(Vector3 worldPosition)
    {
        Vector3 closest = _hull != null && _hull.enabled
            ? _hull.ClosestPoint(worldPosition)
            : transform.position;
        return Vector3.Distance(closest, worldPosition);
    }

    bool HitsButton(Vector2 screen)
    {
        if (_buttonRect == null)
            return false;

        Camera cam = Camera.main;
        return cam != null && RectTransformUtility.RectangleContainsScreenPoint(_buttonRect, screen, cam);
    }

    static bool TryGetPress(out Vector2 screen)
    {
        screen = default;
        Pointer pointer = Pointer.current;
        if (pointer == null || !pointer.press.wasPressedThisFrame)
            return false;

        screen = pointer.position.ReadValue();
        return true;
    }

    void TakeOff()
    {
        if (_loading)
            return;

        string scene = string.IsNullOrWhiteSpace(shipSceneName) ? "Ship" : shipSceneName;
        if (!Application.CanStreamedLevelBeLoaded(scene))
        {
            Debug.LogWarning($"{nameof(ShipTakeoffPrompt)}: scene '{scene}' is not in the build.", this);
            return;
        }

        _loading = true;
        if (button != null)
            button.gameObject.SetActive(false);

        SceneManager.LoadScene(scene);
    }
}
