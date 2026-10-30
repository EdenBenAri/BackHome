using UnityEngine;

/// <summary>
/// Gates the DustTrail's emission to only happen while actually running - every other particle
/// setting (lifetime, size, shape, mesh, material, forces, curves, ...) is left alone, authored
/// directly on the DustTrail prefab's ParticleSystem/ParticleSystemRenderer and tunable
/// by hand in the Inspector like any normal particle system.
///
/// If the parent has a <see cref="PlanetWalker"/> or <see cref="TouchController"/>, gating uses
/// their <c>MotionAmount</c> (0..1, clamped to 1 exactly at their own run threshold) - this is
/// exact, not a guess, and works correctly both on flat scenes and the spherical planet. Without
/// either, it falls back to an Animator "MotionSpeed" float if present, or just always emits
/// (whatever's authored on the Emission module runs as-is) so it's still usable on other objects.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
[DefaultExecutionOrder(100)]
public class DustTrailVfx : MonoBehaviour
{
    [Header("Run Gate")]
    [Tooltip("If on, only emits while running (see class summary for how that's detected). If " +
        "off, always emits - whatever's authored on the Emission module runs as-is.")]
    [SerializeField] bool onlyEmitWhileRunning = true;
    [Tooltip("Threshold against PlanetWalker/TouchController's MotionAmount (which clamps to 1 " +
        "exactly at their own run threshold) or, lacking those, the Animator's \"MotionSpeed\".")]
    [SerializeField, Range(0f, 1f)] float runThreshold = 0.9f;

    ParticleSystem _ps;
    PlanetWalker _planetWalker;
    TouchController _touchController;
    Animator _animator;
    DustTrailFootFollower _footFollower;
    Transform _motionRoot;
    Vector3 _lastPosition;
    bool _hasMotionParameter;
    int _animIDMotionSpeed;
    bool _running;

    void OnEnable()
    {
        // OnEnable also runs after a script reload in Play Mode; Awake does not.
        _ps = GetComponent<ParticleSystem>();
        _planetWalker = GetComponentInParent<PlanetWalker>();
        _touchController = GetComponentInParent<TouchController>();
        _animator = GetComponentInParent<Animator>();
        _footFollower = GetComponent<DustTrailFootFollower>();
        _motionRoot = _planetWalker != null ? _planetWalker.transform
            : _touchController != null ? _touchController.transform
            : _animator != null ? _animator.transform : transform;
        _lastPosition = _motionRoot.position;
        _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        _hasMotionParameter = false;
        if (_animator != null && _animator.runtimeAnimatorController != null)
        {
            foreach (AnimatorControllerParameter parameter in _animator.parameters)
                if (parameter.nameHash == _animIDMotionSpeed && parameter.type == AnimatorControllerParameterType.Float)
                    _hasMotionParameter = true;
        }
        SetRunning(!onlyEmitWhileRunning || !HasRunSignal());
    }

    void LateUpdate()
    {
        bool running = !onlyEmitWhileRunning || !HasRunSignal() || GetMotionAmount() >= runThreshold;
        if (onlyEmitWhileRunning && (_planetWalker != null || _touchController != null))
        {
            // Intent alone still says "run" when pushing into a wall or during a locked intro.
            float distance = Vector3.ProjectOnPlane(_motionRoot.position - _lastPosition, _motionRoot.up).magnitude;
            running &= Time.deltaTime > 0f && distance > 0.1f * Time.deltaTime;
            running &= _footFollower == null || _footFollower.IsGrounded;
        }
        _lastPosition = _motionRoot.position;
        if (running != _running)
            SetRunning(running);
    }

    void OnDisable()
    {
        if (_ps != null)
            SetRunning(false);
    }

    bool HasRunSignal() => _planetWalker != null || _touchController != null || _hasMotionParameter;

    float GetMotionAmount()
    {
        // PlanetWalker also remains enabled on the ship, but it does not own locomotion there.
        if (_planetWalker != null && _planetWalker.isActiveAndEnabled && _planetWalker.IsWalkingOnPlanet)
            return _planetWalker.LockLocomotion ? 0f : _planetWalker.MotionAmount;
        if (_touchController != null && _touchController.isActiveAndEnabled)
            return _touchController.LockLocomotion ? 0f : _touchController.MotionAmount;
        if (_planetWalker != null || _touchController != null)
            return 0f;
        if (_hasMotionParameter)
            return _animator.GetFloat(_animIDMotionSpeed);
        return 1f;
    }

    void SetRunning(bool running)
    {
        if (_ps == null)
            return;

        // Module structs are native handles and are not restored by Unity's script reload.
        // Always get a fresh handle from the surviving ParticleSystem before changing it.
        var emission = _ps.emission;
        emission.enabled = running;
        _running = running;
    }
}
