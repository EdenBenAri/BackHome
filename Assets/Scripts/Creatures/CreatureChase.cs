using UnityEngine;

/// <summary>
/// Chases the player when they enter this creature's vision range.
/// After <see cref="loseVisionDelay"/> seconds outside vision, loses aggro and returns home.
/// Stops inside attack range so <see cref="CreatureRangeCombat"/> can deal damage.
/// While idle, regular mobs pace a few steps out and back. Kings stay put.
/// Walks on the spherical planet surface (same stick model as spawn / PlanetWalker).
/// </summary>
[RequireComponent(typeof(Creature))]
public class CreatureChase : MonoBehaviour
{
    enum State
    {
        Idle,
        Aggroed,
        Returning
    }

    enum PacePhase
    {
        None,
        WalkOut,
        WaitOut,
        WalkBack,
        WaitHome
    }

    [Header("Movement")]
    [SerializeField, Min(0.1f)] float alignSpeed = 10f;
    [SerializeField, Min(0.001f)] float footOffset = 0.05f;
    [SerializeField, Min(1f)] float groundProbeDistance = 12f;
    [SerializeField] LayerMask groundLayer;

    [Header("Vision")]
    [Tooltip("Seconds the player must stay outside vision range before this creature loses aggro and returns home.")]
    [SerializeField, Min(0f)] float loseVisionDelay = 3f;
    [SerializeField, Min(0.05f)] float homeArriveDistance = 0.35f;

    [Header("Hit reaction")]
    [Tooltip("Surface distance shoved away from the attacker. Keep small — a punch, not a launch.")]
    [SerializeField, Min(0f)] float knockbackDistance = 0.2f;
    [Tooltip("Seconds for the shove to ease out. Short = snappy.")]
    [SerializeField, Min(0.05f)] float knockbackDuration = 0.16f;
    [Tooltip("Minimum seconds between knockbacks on this creature.")]
    [SerializeField, Min(0f)] float knockbackCooldown = 0.5f;

    [Header("Idle pace")]
    [Tooltip("Regular mobs walk a few steps along a random direction, wait, then walk back. Kings stay still.")]
    [SerializeField] bool paceWhileIdle = true;
    [SerializeField, Min(1)] int paceMinSteps = 3;
    [SerializeField, Min(1)] int paceMaxSteps = 5;
    [Tooltip("World distance of one pace step.")]
    [SerializeField, Min(0.05f)] float paceStepDistance = 0.95f;
    [Tooltip("Each stop rolls a new pause in this range. Values under 5 seconds are treated as 5–10.")]
    [SerializeField, Min(0f)] float paceWaitMin = 5f;
    [SerializeField, Min(0f)] float paceWaitMax = 10f;
    [SerializeField, Min(0.05f)] float paceArriveDistance = 0.22f;

    // Fast creatures cover a few steps in a blink. Stretch those walks so they still read as a stroll.
    const float PaceMinWalkSeconds = 0.9f;
    const float PaceMinSpeedScale = 0.4f;
    const float PaceStuckSeconds = 0.45f;

    Creature _creature;
    CreatureAnimator _anim;
    Animator _animator;
    PlayerVitals _player;
    SphericalPlanet _planet;
    PlanetTileMap _tiles;

    State _state = State.Idle;
    Vector3 _homePosition;
    Quaternion _homeRotation;
    float _outOfVisionTime;
    bool _homeCaptured;

    bool _knockActive;
    float _knockElapsed;
    float _knockDuration;
    float _knockDistance;
    Vector3 _knockDir;
    Vector3 _knockFaceTarget;
    float _nextKnockbackTime;
    Vector3 _velocity;

    bool _isKing;
    bool _paceReady;
    PacePhase _pacePhase;
    Vector3 _paceDir;
    Vector3 _paceTarget;
    float _pauseRemaining;
    float _paceSpeedScale = 1f;
    float _paceBestDist;
    float _paceStuck;
    int _paceSteps;

    /// <summary>True while actively chasing / fighting the player.</summary>
    public bool IsAggroed => _state == State.Aggroed;

    /// <summary>True during the short hit-shove after taking damage.</summary>
    public bool IsKnockedBack => _knockActive;

    /// <summary>Current surface movement (direction * speed), zero while stopped/attacking/knocked back — used to lead moving targets for slow or telegraphed attacks.</summary>
    public Vector3 Velocity => _velocity;

    void Awake()
    {
        _creature = GetComponent<Creature>();
        _anim = GetComponent<CreatureAnimator>();
        if (_anim == null)
            _anim = GetComponentInChildren<CreatureAnimator>();
        _animator = GetComponentInChildren<Animator>();
        _isKing = GetComponent<AreaKing>() != null;

        if (groundLayer.value == 0)
            groundLayer = LayerMask.GetMask("Ground");
    }

    void OnDisable()
    {
        SetPacePlayback(1f);
    }

    void Start()
    {
        CaptureHome();
    }

    void Update()
    {
        if (_creature == null)
        {
            _velocity = Vector3.zero;
            SetPacePlayback(1f);
            _anim?.ResetToIdle();
            return;
        }

        if (!_creature.IsAlive)
        {
            _velocity = Vector3.zero;
            SetPacePlayback(1f);
            return;
        }

        if (_creature.IsFrozen)
        {
            _velocity = Vector3.zero;
            SetPacePlayback(1f);
            _anim?.ResetToIdle();
            return;
        }

        if (!_homeCaptured)
            CaptureHome();

        if (!TryResolvePlanet())
        {
            _velocity = Vector3.zero;
            SetPacePlayback(1f);
            _anim?.SetMoving(false);
            return;
        }

        if (_knockActive)
        {
            _velocity = Vector3.zero;
            SetPacePlayback(1f);
            _anim?.SetMoving(false);
            return;
        }

        // Default to stationary; MoveToward() re-sets this below on the frames it actually runs.
        _velocity = Vector3.zero;

        switch (_state)
        {
            case State.Idle:
                TickIdle();
                break;
            case State.Aggroed:
                TickAggroed();
                break;
            case State.Returning:
                TickReturning();
                break;
        }
    }

    void LateUpdate()
    {
        if (_creature != null && !_creature.IsAlive)
        {
            _knockActive = false;
            SetPacePlayback(1f);
            return;
        }

        if (_knockActive)
            TickKnockback();
    }

    /// <summary>
    /// Short surface shove away from <paramref name="fromWorldPosition"/>.
    /// Ignored while this creature's knockback cooldown is active.
    /// </summary>
    public void ApplyKnockback(Vector3 fromWorldPosition)
    {
        if (_state != State.Aggroed)
            EnterAggro();

        if (knockbackDistance <= 0f || knockbackDuration <= 0f)
            return;

        if (Time.time < _nextKnockbackTime)
            return;

        if (!TryResolvePlanet())
            return;

        Vector3 up = _planet.GetUpAt(transform.position);
        Vector3 away = Vector3.ProjectOnPlane(transform.position - fromWorldPosition, up);
        if (away.sqrMagnitude < 0.0001f)
            away = Vector3.ProjectOnPlane(-transform.forward, up);
        if (away.sqrMagnitude < 0.0001f)
            return;

        _knockDir = away.normalized;
        _knockFaceTarget = fromWorldPosition;
        _knockDistance = knockbackDistance;
        _knockDuration = knockbackDuration;
        _knockElapsed = 0f;
        _knockActive = true;
        _nextKnockbackTime = Time.time + knockbackCooldown;
    }

    void TickIdle()
    {
        if (TryGetLivingPlayer(out PlayerVitals player))
        {
            float vision = _creature.VisionRange;
            if (vision > 0f && GetSurfaceDistanceTo(player.transform.position) <= vision)
            {
                SetPacePlayback(1f);
                SpotPlayer();
                return;
            }
        }

        if (_isKing || !paceWhileIdle)
        {
            _velocity = Vector3.zero;
            SetPacePlayback(1f);
            _anim?.SetMoving(false);
            return;
        }

        TickPace();
    }

    void TickAggroed()
    {
        float vision = _creature.VisionRange;

        bool playerVisible = false;
        PlayerVitals player = null;

        if (TryGetLivingPlayer(out player) && vision > 0f)
            playerVisible = GetSurfaceDistanceTo(player.transform.position) <= vision;

        if (playerVisible)
        {
            _outOfVisionTime = 0f;
        }
        else
        {
            _outOfVisionTime += Time.deltaTime;
            if (_outOfVisionTime >= loseVisionDelay)
            {
                BeginReturnHome();
                return;
            }
        }

        // Still aggroed: chase / attack even during the lose-vision grace period.
        if (player == null || !player.IsAlive)
        {
            _anim?.SetMoving(false);
            return;
        }

        float distToPlayer = GetSurfaceDistanceTo(player.transform.position);

        // Stop at attack range and face the player.
        float attack = _creature.AttackRange;
        if (attack > 0f && distToPlayer <= attack)
        {
            FaceToward(player.transform.position);
            _anim?.SetMoving(false);
            return;
        }

        MoveToward(player.transform.position);
        _anim?.SetMoving(true);
    }

    void TickReturning()
    {
        // Re-acquire if the player walks back into vision while heading home.
        if (TryGetLivingPlayer(out PlayerVitals player))
        {
            float vision = _creature.VisionRange;
            if (vision > 0f && GetSurfaceDistanceTo(player.transform.position) <= vision)
            {
                SpotPlayer();
                return;
            }
        }

        float homeDist = GetSurfaceDistanceTo(_homePosition);
        if (homeDist <= homeArriveDistance)
        {
            ArriveHome();
            return;
        }

        MoveToward(_homePosition);
        _anim?.SetMoving(true);
    }

    void EnterAggro()
    {
        _state = State.Aggroed;
        _outOfVisionTime = 0f;
        SetPacePlayback(1f);
    }

    void SpotPlayer()
    {
        EnterAggro();
    }

    void BeginReturnHome()
    {
        _state = State.Returning;
        _outOfVisionTime = 0f;
        SetPacePlayback(1f);
        _anim?.SetAttacking(false);
    }

    void ArriveHome()
    {
        _state = State.Idle;
        _outOfVisionTime = 0f;
        _anim?.SetMoving(false);

        Vector3 up = _planet != null
            ? _planet.GetUpAt(_homePosition)
            : transform.up;

        Vector3 faceDir = Vector3.ProjectOnPlane(_homeRotation * Vector3.forward, up);
        if (faceDir.sqrMagnitude < 0.001f)
            faceDir = Vector3.ProjectOnPlane(transform.forward, up);

        if (faceDir.sqrMagnitude > 0.001f)
            transform.SetPositionAndRotation(_homePosition, Quaternion.LookRotation(faceDir.normalized, up));
        else
            transform.position = _homePosition;

        _pacePhase = PacePhase.None;
        SetPacePlayback(1f);
    }

    void CaptureHome()
    {
        _homePosition = transform.position;
        _homeRotation = transform.rotation;
        _homeCaptured = true;
    }

    bool TryGetLivingPlayer(out PlayerVitals player)
    {
        if (!TryResolvePlayer(out player) || player == null || !player.IsAlive)
        {
            player = null;
            return false;
        }

        return true;
    }

    void TickKnockback()
    {
        if (!TryResolvePlanet())
        {
            _knockActive = false;
            return;
        }

        float duration = Mathf.Max(0.01f, _knockDuration);
        float t0 = Mathf.Clamp01(_knockElapsed / duration);
        _knockElapsed += Time.deltaTime;
        float t1 = Mathf.Clamp01(_knockElapsed / duration);

        Vector3 up = _planet.GetUpAt(transform.position);
        Vector3 dir = Vector3.ProjectOnPlane(_knockDir, up);
        Vector3 next = transform.position;

        if (dir.sqrMagnitude > 0.0001f)
        {
            dir.Normalize();
            _knockDir = dir;

            float step = (EaseOutCubic(t1) - EaseOutCubic(t0)) * _knockDistance;
            if (step > 0.00001f)
                StepOnSurface(dir, step, out next, out up);
        }

        Vector3 faceDir = Vector3.ProjectOnPlane(_knockFaceTarget - next, up);
        if (faceDir.sqrMagnitude < 0.001f)
            faceDir = Vector3.ProjectOnPlane(transform.forward, up);
        if (faceDir.sqrMagnitude < 0.001f)
            faceDir = Vector3.ProjectOnPlane(Vector3.forward, up);

        if (faceDir.sqrMagnitude > 0.001f)
            ApplyPose(next, faceDir.normalized, up);
        else
            transform.position = next;

        if (t1 >= 1f)
            _knockActive = false;
    }

    void TickPace()
    {
        if (!_homeCaptured)
            CaptureHome();

        EnsurePaceProfile();

        if (_pacePhase == PacePhase.None)
            BeginPaceOut();

        switch (_pacePhase)
        {
            case PacePhase.WalkOut:
                TickPaceWalk(_paceTarget, false);
                break;
            case PacePhase.WalkBack:
                TickPaceWalk(_homePosition, true);
                break;
            case PacePhase.WaitOut:
                TickPaceWait(false);
                break;
            case PacePhase.WaitHome:
                TickPaceWait(true);
                break;
        }
    }

    void EnsurePaceProfile()
    {
        if (_paceReady)
            return;

        int minSteps = Mathf.Max(1, Mathf.Min(paceMinSteps, paceMaxSteps));
        int maxSteps = Mathf.Max(minSteps, Mathf.Max(paceMinSteps, paceMaxSteps));
        _paceSteps = Random.Range(minSteps, maxSteps + 1);
        _paceDir = Vector3.zero;
        _paceReady = true;
    }

    void BeginPaceOut()
    {
        // Same random line for this creature. Each outing flips, so it paces side to side
        // through home: out, wait, back, wait, then out the other way.
        bool pickNew = _paceDir.sqrMagnitude <= 0.25f;
        EnsurePaceDirection();
        if (!pickNew)
            _paceDir = -_paceDir;

        float distance = _paceSteps * paceStepDistance;
        _paceSpeedScale = PaceScaleFor(distance);
        _paceTarget = OffsetOnSurface(_homePosition, _paceDir, distance);
        _pacePhase = PacePhase.WalkOut;
        _paceBestDist = float.MaxValue;
        _paceStuck = 0f;
    }

    void BeginPaceBack()
    {
        _pacePhase = PacePhase.WalkBack;
        _paceBestDist = float.MaxValue;
        _paceStuck = 0f;
    }

    void BeginPaceWait(PacePhase phase)
    {
        _pacePhase = phase;
        _pauseRemaining = RollPaceWait();
        _velocity = Vector3.zero;
        SetPacePlayback(1f);
        _anim?.SetMoving(false);
    }

    float RollPaceWait()
    {
        // A saved 0–1.6s range from the first version never actually reads as a stop.
        float min = paceWaitMin;
        float max = paceWaitMax;
        if (max < 5f)
        {
            min = 5f;
            max = 10f;
        }

        min = Mathf.Max(0f, min);
        max = Mathf.Max(min, max);
        return Mathf.Lerp(min, max, Random.value);
    }

    void TickPaceWait(bool atHome)
    {
        _velocity = Vector3.zero;
        SetPacePlayback(1f);
        _anim?.SetMoving(false);
        _pauseRemaining -= Time.deltaTime;
        if (_pauseRemaining > 0f)
            return;

        if (atHome)
            BeginPaceOut();
        else
            BeginPaceBack();
    }

    void TickPaceWalk(Vector3 target, bool returning)
    {
        float dist = GetSurfaceDistanceTo(target);
        if (dist <= paceArriveDistance)
        {
            if (returning)
                ArrivePaceHome();
            else
                BeginPaceWait(PacePhase.WaitOut);
            return;
        }

        if (dist < _paceBestDist - 0.03f)
        {
            _paceBestDist = dist;
            _paceStuck = 0f;
        }
        else
        {
            _paceStuck += Time.deltaTime;
            float giveUp = returning ? 1.25f : PaceStuckSeconds;
            if (_paceStuck >= giveUp)
            {
                if (!returning)
                {
                    // A wall on this side: pick a new line after the return home.
                    if (GetSurfaceDistanceTo(_homePosition) < paceStepDistance)
                        _paceDir = Vector3.zero;
                    BeginPaceWait(PacePhase.WaitOut);
                    return;
                }

                if (dist <= 0.75f)
                {
                    ArrivePaceHome();
                    return;
                }

                _paceStuck = 0f;
                _paceBestDist = dist;
            }
        }

        float scale = Mathf.Clamp(_paceSpeedScale, PaceMinSpeedScale, 1f);
        SetPacePlayback(scale);
        MoveToward(target, scale, dist);
        _anim?.SetMoving(true);
    }

    void ArrivePaceHome()
    {
        SettleAt(_homePosition);
        BeginPaceWait(PacePhase.WaitHome);
    }

    void EnsurePaceDirection()
    {
        if (_paceDir.sqrMagnitude > 0.25f && _planet != null)
        {
            Vector3 up = _planet.GetUpAt(_homePosition);
            _paceDir = Vector3.ProjectOnPlane(_paceDir, up);
        }

        if (_paceDir.sqrMagnitude > 0.25f)
        {
            _paceDir.Normalize();
            return;
        }

        _paceDir = RandomTangent(_homePosition);
    }

    Vector3 RandomTangent(Vector3 surfacePoint)
    {
        Vector3 up = _planet != null ? _planet.GetUpAt(surfacePoint) : transform.up;
        Vector3 tangent = Vector3.ProjectOnPlane(Random.onUnitSphere, up);
        if (tangent.sqrMagnitude < 0.0001f)
            tangent = Vector3.ProjectOnPlane(transform.forward, up);
        if (tangent.sqrMagnitude < 0.0001f)
            tangent = Vector3.Cross(up, Vector3.right);
        if (tangent.sqrMagnitude < 0.0001f)
            tangent = Vector3.Cross(up, Vector3.forward);
        return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : transform.forward;
    }

    Vector3 OffsetOnSurface(Vector3 origin, Vector3 direction, float distance)
    {
        if (_planet == null || distance <= 0f)
            return origin;

        Vector3 radial = origin - _planet.Center;
        float radius = radial.magnitude;
        if (radius < 0.01f)
            return origin;

        radial /= radius;
        Vector3 axis = Vector3.Cross(radial, direction);
        if (axis.sqrMagnitude < 0.0001f)
            return origin;

        float angle = distance / radius * Mathf.Rad2Deg;
        Vector3 turned = Quaternion.AngleAxis(angle, axis.normalized) * radial;
        return _planet.Center + turned * radius;
    }

    float PaceScaleFor(float distance)
    {
        float speed = CurrentMoveSpeed();
        if (speed <= 0.01f || distance <= 0.01f)
            return 1f;

        float duration = distance / speed;
        if (duration >= PaceMinWalkSeconds)
            return 1f;

        return Mathf.Clamp(duration / PaceMinWalkSeconds, PaceMinSpeedScale, 1f);
    }

    float CurrentMoveSpeed()
    {
        return _creature != null && _creature.HasStats ? _creature.MovementSpeed : 4f;
    }

    void SetPacePlayback(float scale)
    {
        if (_animator == null)
            return;

        scale = scale >= 0.999f ? 1f : Mathf.Clamp(scale, PaceMinSpeedScale, 1f);
        if (Mathf.Abs(_animator.speed - scale) < 0.001f)
            return;

        _animator.speed = scale;
    }

    void SettleAt(Vector3 position)
    {
        if (_planet == null)
        {
            transform.position = position;
            return;
        }

        Vector3 up = _planet.GetUpAt(position);
        Vector3 face = Vector3.ProjectOnPlane(transform.forward, up);
        if (face.sqrMagnitude < 0.001f)
            face = Vector3.ProjectOnPlane(_paceDir, up);
        if (face.sqrMagnitude < 0.001f)
            face = Vector3.ProjectOnPlane(Vector3.forward, up);

        if (face.sqrMagnitude > 0.001f)
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(face.normalized, up));
        else
            transform.position = position;
    }

    void MoveToward(Vector3 targetPosition)
    {
        MoveToward(targetPosition, 1f, float.PositiveInfinity);
    }

    void MoveToward(Vector3 targetPosition, float speedScale, float maxDistance)
    {
        Vector3 up = _planet.GetUpAt(transform.position);
        Vector3 toTarget = Vector3.ProjectOnPlane(targetPosition - transform.position, up);
        if (toTarget.sqrMagnitude < 0.0001f)
            return;

        Vector3 moveDir = toTarget.normalized;
        float speed = CurrentMoveSpeed() * Mathf.Max(0f, speedScale);
        float step = speed * Time.deltaTime;
        if (maxDistance < float.PositiveInfinity)
            step = Mathf.Min(step, Mathf.Max(0f, maxDistance));
        _velocity = moveDir * speed;
        StepOnSurface(moveDir, step, out Vector3 next, out up);

        Vector3 faceDir = Vector3.ProjectOnPlane(moveDir, up);
        if (faceDir.sqrMagnitude < 0.001f)
            faceDir = Vector3.ProjectOnPlane(transform.forward, up);
        if (faceDir.sqrMagnitude < 0.001f)
            faceDir = Vector3.ProjectOnPlane(Vector3.forward, up);

        ApplyPose(next, faceDir.normalized, up);
    }

    void StepOnSurface(Vector3 moveDir, float distance, out Vector3 next, out Vector3 up)
    {
        Vector3 probeOrigin = transform.position + moveDir * distance;
        Vector3 radial = (probeOrigin - _planet.Center).normalized;

        if (TryStickToCollider(radial, out next, out Vector3 surfaceUp))
        {
            up = surfaceUp;
            ClampToWalkBand(ref next, ref up);
            return;
        }

        next = probeOrigin;
        float minDist = GetFallbackSurfaceRadius(radial) + footOffset;
        Vector3 fromCenter = next - _planet.Center;
        if (fromCenter.magnitude < minDist)
            next = _planet.Center + fromCenter.normalized * minDist;
        up = _planet.GetUpAt(next);
        ClampToWalkBand(ref next, ref up);
    }

    void ClampToWalkBand(ref Vector3 next, ref Vector3 up)
    {
        if (_tiles == null || _planet == null)
            return;
        next = PlanetWalkBand.ClampPosition(_planet, _tiles, next, footOffset);
        if (PlanetWalkBand.IsOffRoute(_planet, next) &&
            PlanetWalkBand.TrySnapToCenter(_planet, _tiles, next, footOffset, out Vector3 kept))
            next = kept;
        up = _planet.GetUpAt(next);
    }

    static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    void FaceToward(Vector3 targetPosition)
    {
        Vector3 up = _planet != null
            ? _planet.GetUpAt(transform.position)
            : transform.up;

        Vector3 faceDir = Vector3.ProjectOnPlane(targetPosition - transform.position, up);
        if (faceDir.sqrMagnitude < 0.001f)
            return;

        ApplyPose(transform.position, faceDir.normalized, up);
    }

    void ApplyPose(Vector3 next, Vector3 faceDir, Vector3 up)
    {
        Quaternion targetRot = Quaternion.LookRotation(faceDir, up);
        Quaternion nextRot = Quaternion.Slerp(
            transform.rotation,
            targetRot,
            1f - Mathf.Exp(-alignSpeed * Time.deltaTime));

        transform.SetPositionAndRotation(next, nextRot);
    }

    float GetSurfaceDistanceTo(Vector3 targetPosition)
    {
        Vector3 origin = transform.position;

        if (_planet != null)
            return PlanetSurfacePose.GetSurfaceDistance(_planet.Center, origin, targetPosition);

        return Vector3.ProjectOnPlane(targetPosition - origin, transform.up).magnitude;
    }

    bool TryResolvePlayer(out PlayerVitals player)
    {
        if (_player != null)
        {
            player = _player;
            return true;
        }

        _player = FindAnyObjectByType<PlayerVitals>();
        player = _player;
        return player != null;
    }

    bool TryResolvePlanet()
    {
        if (_planet != null)
            return true;

        _planet = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance
            : FindAnyObjectByType<SphericalPlanet>();

        if (_planet == null)
            return false;

        _tiles = _planet.GetComponent<PlanetTileMap>();
        return true;
    }

    bool TryStickToCollider(Vector3 radial, out Vector3 feetPosition, out Vector3 normal)
    {
        feetPosition = default;
        normal = radial;

        float castStart = GetFallbackSurfaceRadius(radial) + Mathf.Max(4f, groundProbeDistance * 0.5f);
        Vector3 origin = _planet.Center + radial * castStart;
        float maxDist = castStart + 2f;

        int count = PlanetSurfacePose.RaycastGround(
            origin,
            -radial,
            maxDist,
            groundLayer,
            out RaycastHit[] hits);

        if (_tiles != null && count > 0 && _tiles.TryPickWalkSurfaceHit(hits, _planet.Center, -radial, count, out RaycastHit best))
        {
            normal = best.normal.sqrMagnitude > 0.001f ? best.normal.normalized : radial;
            if (Vector3.Dot(normal, radial) < 0f)
                normal = -normal;
            feetPosition = best.point + normal * footOffset;
            return true;
        }

        return false;
    }

    float GetFallbackSurfaceRadius(Vector3 radial)
    {
        if (_tiles != null && _tiles.ProvidesWalkSurface)
            return _tiles.GetWalkSurfaceRadius(radial);
        return _planet.GetTerrainRadius(radial);
    }
}
