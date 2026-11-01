using System.Collections;
using UnityEngine;

/// <summary>
/// Follow the player around the planet without reacting to facing, input or combat.
/// The initial spawn establishes the compass; only KingCinematicDirector changes
/// it afterwards. Parallel transport keeps that compass continuous around the sphere.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [SerializeField] Transform target;

    [Header("Offset")]
    // XP Hero-style high angle: steep look-down (~70°), not over-the-shoulder.
    [SerializeField] float offsetHeight = 24f;
    [SerializeField] float offsetBack = 8f;
    [SerializeField] float offsetSide = 0f;

    [Header("Follow")]
    [SerializeField] float smoothSpeed = 10f;
    [SerializeField] bool lookAtTarget = true;
    [Tooltip("Tilt camera using surface up. Softly smoothed.")]
    [SerializeField] bool alignToTargetUp = true;
    [Tooltip("On planets, use radial up (center→player) instead of terrain slope normals.")]
    [SerializeField] bool preferPlanetRadialUp = true;
    [SerializeField] float upSmoothSpeed = 5f;
    [Tooltip("How quickly the camera pivot tracks the player along the ground plane.")]
    [SerializeField] float focusSmoothSpeed = 8f;
    [Tooltip("How quickly the camera pivot tracks height/radial bob. Lower = less shake on uneven terrain.")]
    [SerializeField] float heightSmoothSpeed = 2.5f;

    Vector3 _smoothedUp = Vector3.up;
    Vector3 _smoothedBack = Vector3.forward;
    Vector3 _smoothedFocus;
    bool _hasBasis;
    bool _hasFocus;

    Camera _camera;
    float _baseFov;
    bool _hasBaseFov;

    float _currentHeight, _currentBack;
    float _heightVelocity, _backVelocity, _fovVelocity;
    Vector3 _previousTargetPosition;
    bool _hasPreviousTargetPosition;
    bool _hasRig;

    bool _snapToTarget;
    KingCinematicDirector _kingDirector;
    bool _cinematicControl;

    public void BindKingGuidance(CreatureSpawner spawner)
    {
        if (spawner == null) { if (_kingDirector != null) _kingDirector.Bind(null, target); return; }
        if (_kingDirector == null) _kingDirector = GetComponent<KingCinematicDirector>();
        if (_kingDirector == null) _kingDirector = gameObject.AddComponent<KingCinematicDirector>();
        _kingDirector.Bind(spawner, target);
    }

    void OnDisable()
    {
        if (_kingDirector != null) _kingDirector.Cancel();
    }

    public Vector3 CinematicFocus => _hasFocus ? _smoothedFocus : target.position;

    public void BeginCinematic()
    {
        _cinematicControl = true;
        if (_shakeRoutine != null) StopCoroutine(_shakeRoutine);
        _shakeRoutine = null;
        _shakeOffset = Vector3.zero;
    }

    /// <summary>Rebase the follow rig on the returned shot, so releasing it never snaps back.</summary>
    public void EndCinematic(Vector3 heading)
    {
        _cinematicControl = false;
        if (target == null) return;
        _smoothedUp = ResolveDesiredUp(target.position, target.up).normalized;
        _smoothedBack = TangentDirection(heading, _smoothedUp);
        _smoothedFocus = target.position;
        Vector3 offset = transform.position - _smoothedFocus;
        _currentHeight = Vector3.Dot(offset, _smoothedUp);
        _currentBack = Mathf.Max(0f, -Vector3.Dot(offset, _smoothedBack));
        _heightVelocity = _backVelocity = _fovVelocity = 0f;
        _hasBasis = _hasFocus = _hasRig = true;
        _previousTargetPosition = target.position;
        _hasPreviousTargetPosition = true;
    }

    public float OffsetHeight => offsetHeight;
    public float OffsetBack => offsetBack;

    /// <summary>Overrides the follow distance/height (e.g. for a cinematic wide shot). Restore afterwards.</summary>
    public void SetOffsets(float height, float back)
    {
        offsetHeight = height;
        offsetBack = back;
    }

    /// <summary>
    /// While true, the camera tracks the target with zero smoothing and stays dead-centered.
    /// </summary>
    public void SetSnapToTarget(bool snap)
    {
        _snapToTarget = snap;
    }

    Coroutine _shakeRoutine;
    Vector3 _shakeOffset;
    Vector3 _heartbeatOffset;
    Vector3 _oxygenOffset;
    float _oxygenFovDelta;

    /// <summary>Brief positional shake. Decays smoothly to zero over duration.</summary>
    public void Shake(float duration, float magnitude)
    {
        if (_cinematicControl || duration <= 0f || magnitude <= 0f)
            return;
        if (_shakeRoutine != null)
            StopCoroutine(_shakeRoutine);
        _shakeRoutine = StartCoroutine(ShakeRoutine(duration, magnitude));
    }

    /// <summary>
    /// Landing/hit jolt: a short downward kick plus a decaying rumble. Magnitude is camera-space
    /// world units — use more than <see cref="Shake"/> at distant cinematic framing.
    /// </summary>
    public void ImpactShake(float duration, float magnitude)
    {
        if (_cinematicControl || duration <= 0f || magnitude <= 0f)
            return;
        if (_shakeRoutine != null)
            StopCoroutine(_shakeRoutine);
        _shakeRoutine = StartCoroutine(ImpactShakeRoutine(duration, magnitude));
    }

    /// <summary>
    /// Sustained camera-space kick (x/y) added on top of <see cref="Shake"/>.
    /// Used for low-HP heartbeat. Pass <see cref="Vector3.zero"/> to clear.
    /// </summary>
    public void SetHeartbeatOffset(Vector3 offset)
    {
        _heartbeatOffset = offset;
    }

    /// <summary>
    /// Sustained camera-space sway for low oxygen. Pass <see cref="Vector3.zero"/> to clear.
    /// </summary>
    public void SetOxygenOffset(Vector3 offset)
    {
        _oxygenOffset = offset;
    }

    /// <summary>
    /// Extra FOV on top of the base lens (negative = tunnel vision). Pass 0 to clear.
    /// </summary>
    public void SetOxygenFovDelta(float fovDelta)
    {
        _oxygenFovDelta = fovDelta;
    }

    IEnumerator ShakeRoutine(float duration, float magnitude)
    {
        float t = 0f;
        float seedX = Random.value * 100f;
        float seedY = Random.value * 100f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float damper = 1f - Mathf.Clamp01(t / duration);
            float x = (Mathf.PerlinNoise(seedX + Time.time * 28f, 0f) - 0.5f) * 2f;
            float y = (Mathf.PerlinNoise(0f, seedY + Time.time * 28f) - 0.5f) * 2f;
            _shakeOffset = new Vector3(x, y, 0f) * magnitude * damper;
            yield return null;
        }
        _shakeOffset = Vector3.zero;
        _shakeRoutine = null;
    }

    IEnumerator ImpactShakeRoutine(float duration, float magnitude)
    {
        float t = 0f;
        float seedX = Random.value * 100f;
        float seedY = Random.value * 100f;
        Vector3 kick = new Vector3((Random.value - 0.5f) * 0.4f, -1f, 0f).normalized * magnitude;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            float punch = (1f - u) * (1f - u);
            float rumble = 1f - u;
            float x = (Mathf.PerlinNoise(seedX + Time.time * 36f, 0f) - 0.5f) * 2f;
            float y = (Mathf.PerlinNoise(0f, seedY + Time.time * 36f) - 0.5f) * 2f;
            _shakeOffset = kick * punch + new Vector3(x, y, 0f) * (magnitude * 0.7f * rumble);
            yield return null;
        }
        _shakeOffset = Vector3.zero;
        _shakeRoutine = null;
    }

    public void SetTarget(Transform newTarget)
    {
        if (target != newTarget)
        {
            if (_kingDirector != null) _kingDirector.Cancel();
            _hasBasis = false;
        }
        target = newTarget;
        // Rebinding/teleporting the same player keeps the King-selected compass.
        _hasFocus = false;
        _heightVelocity = _backVelocity = _fovVelocity = 0f;
        _hasRig = false;
        _hasPreviousTargetPosition = false;
    }

    void Awake()
    {
        _camera = GetComponent<Camera>();
        if (_camera != null)
        {
            _baseFov = _camera.fieldOfView;
            _hasBaseFov = true;
        }
        SetTarget(target);
    }

    void LateUpdate()
    {
        UpdateCamera(Time.deltaTime);
    }

    void UpdateCamera(float deltaTime)
    {
        if (_cinematicControl || target == null || deltaTime <= 0f)
            return;

        // Respawn / teleport should establish a fresh rig, never sweep through the planet.
        float warpDistance = Mathf.Max(10f, new Vector2(offsetHeight, offsetBack).magnitude * 2f);
        if (_hasPreviousTargetPosition && (target.position - _previousTargetPosition).sqrMagnitude > warpDistance * warpDistance)
            SetTarget(target);
        _previousTargetPosition = target.position;
        _hasPreviousTargetPosition = true;

        Vector3 desiredUp = ResolveDesiredUp(target.position, target.up);
        if (desiredUp.sqrMagnitude < 0.0001f)
            desiredUp = Vector3.up;
        desiredUp.Normalize();

        Vector3 previousUp = _smoothedUp;
        if (_snapToTarget || !_hasBasis || !_hasRig || upSmoothSpeed <= 0f)
            _smoothedUp = desiredUp;
        else
            _smoothedUp = Vector3.Slerp(_smoothedUp, desiredUp, Damp(upSmoothSpeed, deltaTime)).normalized;

        UpdateSmoothedFocus(_smoothedUp, deltaTime);

        Vector3 back = ResolveBackDirection(_smoothedUp, previousUp);
        Vector3 right = Vector3.Cross(_smoothedUp, back).normalized;

        float height = offsetHeight;
        float backDist = offsetBack;

        if (_snapToTarget || !_hasRig || smoothSpeed <= 0f)
        {
            _currentHeight = height;
            _currentBack = backDist;
            _heightVelocity = _backVelocity = 0f;
        }
        else
        {
            float framingTime = Mathf.Max(0.2f, 5f / smoothSpeed);
            _currentHeight = Mathf.SmoothDamp(_currentHeight, height, ref _heightVelocity, framingTime, Mathf.Infinity, deltaTime);
            _currentBack = Mathf.SmoothDamp(_currentBack, backDist, ref _backVelocity, framingTime, Mathf.Infinity, deltaTime);
        }
        Vector3 desired = _smoothedFocus
                          + _smoothedUp * _currentHeight
                          - back * _currentBack
                          + right * offsetSide;

        // Scenery's broad navigation colliders must not yank this high-angle rig
        // into the hero whenever a flower or rock crosses the sightline.
        transform.position = desired;
        _hasRig = true;

        if (lookAtTarget)
        {
            Vector3 toTarget = _smoothedFocus - transform.position;
            if (toTarget.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(toTarget.normalized, _smoothedUp);
        }

        Vector3 shake = _shakeOffset + _heartbeatOffset + _oxygenOffset;
        if (shake.sqrMagnitude > 0.0000001f)
            transform.position += transform.right * shake.x + transform.up * shake.y;

        if (_hasBaseFov && _camera != null)
        {
            float targetFov = _baseFov + _oxygenFovDelta;
            if (Mathf.Abs(_oxygenFovDelta) > 0.001f || Mathf.Abs(_camera.fieldOfView - targetFov) > 0.02f)
                _camera.fieldOfView = Mathf.SmoothDamp(_camera.fieldOfView, targetFov,
                    ref _fovVelocity, 2f / 3.2f, Mathf.Infinity, deltaTime);
        }
    }

    Vector3 ResolveDesiredUp(Vector3 worldPosition, Vector3 fallbackUp)
    {
        if (!alignToTargetUp)
            return Vector3.up;

        if (preferPlanetRadialUp && SphericalPlanet.Instance != null)
            return SphericalPlanet.Instance.GetUpAt(worldPosition);

        return fallbackUp.sqrMagnitude > 0.0001f ? fallbackUp.normalized : Vector3.up;
    }

    void UpdateSmoothedFocus(Vector3 up, float deltaTime)
    {
        if (_snapToTarget)
        {
            _smoothedFocus = target.position;
            _hasFocus = true;
            return;
        }

        Vector3 targetPos = target.position;
        if (!_hasFocus)
        {
            _smoothedFocus = target.position;
            _hasFocus = true;
            return;
        }

        Vector3 delta = targetPos - _smoothedFocus;
        Vector3 alongUp = Vector3.Project(delta, up);
        Vector3 alongPlane = delta - alongUp;

        float planarT = Damp(focusSmoothSpeed, deltaTime);
        float heightT = Damp(heightSmoothSpeed, deltaTime);

        _smoothedFocus += alongPlane * planarT + alongUp * heightT;
    }

    Vector3 ResolveBackDirection(Vector3 up, Vector3 previousUp)
    {
        // This is the view's forward tangent: camera position SUBTRACTS it to sit behind.
        if (!_hasBasis)
        {
            _smoothedBack = TangentDirection(target.forward, up);
            _hasBasis = true;
            return _smoothedBack;
        }

        // Carry the previous tangent around the sphere; a world compass flips at the poles.
        Vector3 transported = _smoothedBack;
        if ((previousUp - up).sqrMagnitude > 0.000001f)
            transported = Quaternion.FromToRotation(previousUp.normalized, up) * _smoothedBack;
        _smoothedBack = TangentDirection(transported, up);

        // Player facing, joystick gestures and combat never steer this compass.
        return _smoothedBack;
    }

    static Vector3 TangentDirection(Vector3 direction, Vector3 up)
    {
        Vector3 tangent = Vector3.ProjectOnPlane(direction, up);
        if (tangent.sqrMagnitude < 0.001f)
            tangent = Vector3.ProjectOnPlane(Mathf.Abs(up.z) < 0.9f ? Vector3.forward : Vector3.right, up);
        return tangent.normalized;
    }

    static float Damp(float speed, float deltaTime) => speed <= 0f ? 1f : 1f - Mathf.Exp(-speed * deltaTime);
}
