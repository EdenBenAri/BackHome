using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>A non-blocking movement hint, shown after the chapter introduction.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public sealed class DragToMoveTutorial : MonoBehaviour
{
    [Header("Prefab References")]
    [SerializeField] CanvasGroup visibility;
    [SerializeField] RectTransform arrow;
    [SerializeField] RectTransform hand;
    [SerializeField] CanvasGroup handVisibility;

    [Header("Animation")]
    [SerializeField, Min(.5f)] float gestureDuration = 2.4f;
    [SerializeField, Min(0f)] float repeatDelay = .25f;
    [SerializeField, Min(0f)] float fadeInDuration = .18f;
    [Tooltip("Finger-tip positions in the arrow image, normalized from bottom-left to top-right.")]
    [SerializeField] Vector2[] fingerPath;

    public bool IsVisible => visibility != null && visibility.alpha > 0f;
    public bool IsDismissed { get; private set; }

    KingCinematicDirector _director;
    StarterAssetsInputs _input;
    PlayerVitals _vitals;
    Vector2[] _path;
    float[] _distances;
    float _length, _elapsed;
    bool _started;

    void Awake()
    {
        if (visibility == null) visibility = GetComponent<CanvasGroup>();
        visibility.alpha = 0f;
        visibility.interactable = visibility.blocksRaycasts = false;
        if (arrow == null || hand == null || handVisibility == null || fingerPath == null || fingerPath.Length < 2)
        {
            Debug.LogError("DragToMoveTutorial needs the references authored in Tutorial/DragToMoveTutorial.prefab.", this);
            enabled = false;
            return;
        }
        BuildPath();
    }

    void Start()
    {
        // SceneBootstrap has now bound the player and scheduled the camera introduction.
        _director = FindAnyObjectByType<KingCinematicDirector>();
        _input = FindAnyObjectByType<StarterAssetsInputs>();
        _vitals = _input != null ? _input.GetComponent<PlayerVitals>() : null;
        var progress = FindAnyObjectByType<PlanetStageProgression>();
        if (!SceneRoles.IsPlanetScene() || (progress != null && progress.IsResumingAfterDeath))
            Dismiss();
    }

    void LateUpdate()
    {
        if (IsDismissed) return;
        if (_input == null || (_vitals != null && !_vitals.IsAlive)) { Dismiss(); return; }

        // IntroGrace is also pending camera work. Never flash the hint before the reveal.
        bool cameraBusy = _director != null && _director.isActiveAndEnabled
            && _director.CurrentPhase != KingCinematicDirector.Phase.Idle;
        if (cameraBusy || _input.GameplayBlocked || Time.timeScale <= 0f)
        {
            visibility.alpha = 0f;
            return;
        }

        // Observe the same input without consuming it: the first touch can also start moving.
        // A finger held through the final camera frame counts as well, avoiding a stale hint.
        if (HasPointerContact() || _input.IsMoveGestureActive || _input.move.sqrMagnitude > .001f)
        {
            Dismiss();
            return;
        }

        if (!_started) { _started = true; _elapsed = 0f; }
        _elapsed += Time.unscaledDeltaTime;
        visibility.alpha = fadeInDuration > 0f ? Mathf.Clamp01(_elapsed / fadeInDuration) : 1f;
        AnimateHand(_elapsed);
    }

    static bool HasPointerContact()
    {
        if (Touchscreen.current != null)
            foreach (var touch in Touchscreen.current.touches)
                if (touch.press.isPressed || touch.press.wasPressedThisFrame) return true;
        if (Mouse.current != null && (Mouse.current.leftButton.isPressed || Mouse.current.leftButton.wasPressedThisFrame))
            return true;
        return Pen.current != null && (Pen.current.tip.isPressed || Pen.current.tip.wasPressedThisFrame);
    }

    public void Dismiss()
    {
        IsDismissed = true;
        visibility.alpha = 0f;
        gameObject.SetActive(false);
    }

    void OnDisable()
    {
        if (visibility != null) visibility.alpha = 0f;
    }

    void AnimateHand(float elapsed)
    {
        float duration = Mathf.Max(.5f, gestureDuration);
        float cycle = Mathf.Repeat(elapsed, duration + Mathf.Max(0f, repeatDelay));
        float distance = Mathf.Clamp01(cycle / duration) * _length;
        int end = System.Array.BinarySearch(_distances, distance);
        if (end < 0) end = ~end;
        end = Mathf.Clamp(end, 1, _path.Length - 1);
        float t = Mathf.InverseLerp(_distances[end - 1], _distances[end], distance);
        Vector2 point = Vector2.Lerp(_path[end - 1], _path[end], t);
        hand.anchoredPosition = Vector2.Scale(point - arrow.pivot, arrow.rect.size);
        // Lift the finger at the arrowhead, then reset invisibly to the open tail.
        handVisibility.alpha = Mathf.Clamp01(cycle / .12f) * Mathf.Clamp01((duration - cycle) / .15f);
    }

    void BuildPath()
    {
        const int samplesPerSegment = 20;
        int count = (fingerPath.Length - 1) * samplesPerSegment + 1;
        _path = new Vector2[count];
        _distances = new float[count];
        for (int i = 0; i < count; i++)
        {
            int segment = Mathf.Min(i / samplesPerSegment, fingerPath.Length - 2);
            float t = (i - segment * samplesPerSegment) / (float)samplesPerSegment;
            Vector2 a = fingerPath[Mathf.Max(0, segment - 1)];
            Vector2 b = fingerPath[segment];
            Vector2 c = fingerPath[segment + 1];
            Vector2 d = fingerPath[Mathf.Min(segment + 2, fingerPath.Length - 1)];
            _path[i] = .5f * (2f * b + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t * t
                + (-a + 3f * b - 3f * c + d) * t * t * t);
            if (i > 0)
                _distances[i] = _distances[i - 1] + Vector2.Scale(_path[i] - _path[i - 1], arrow.rect.size).magnitude;
        }
        _length = _distances[count - 1];
        hand.anchoredPosition = Vector2.Scale(_path[0] - arrow.pivot, arrow.rect.size);
    }
}
