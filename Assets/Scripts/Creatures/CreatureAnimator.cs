using UnityEngine;

/// <summary>
/// Drives creature Animator bools shared by locomotion and combat.
/// Expects controller params: IsMoving, IsAttacking, AttackKind, AttackAnimSpeed.
/// States: Idle / Run, plus LightAttack (or a shared Attack state).
/// </summary>
[RequireComponent(typeof(Animator))]
public class CreatureAnimator : MonoBehaviour
{
    static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    static readonly int IsAttackingHash = Animator.StringToHash("IsAttacking");
    static readonly int AttackAnimSpeedHash = Animator.StringToHash("AttackAnimSpeed");
    static readonly int AttackKindHash = Animator.StringToHash("AttackKind");
    static readonly int Idle1Hash = Animator.StringToHash("Idle1");
    static readonly int Idle2Hash = Animator.StringToHash("Idle2");
    static readonly int RunHash = Animator.StringToHash("Run");
    static readonly int AttackHash = Animator.StringToHash("Attack");
    static readonly int LightAttackHash = Animator.StringToHash("LightAttack");
    static readonly int DeathHash = Animator.StringToHash("Death");
    static readonly int DieHash = Animator.StringToHash("Die");
    static readonly int SpawnHash = Animator.StringToHash("Spawn");

    // First frame of the king slime Spawn clip (SlimeHopSetup.CreateSpawnClip).
    // Applied before the Animator is ready so the body is already a puddle on the first draw.
    const float SpawnFlatY = 0.045f;
    const float SpawnFlatXZ = 2.3f;

    [SerializeField] string attackClipName = "Attack";

    Animator _animator;
    bool _moving;
    bool _attacking;
    bool _spawning;
    bool _spawnPending;
    float _spawnEnteredAt = -1f;
    float _spawnClipLength = -1f;
    int _hasMovingParam = -1;
    float _attackClipLength = -1f;
    float _lightClipLength = -1f;
    float _deathClipLength = -1f;
    bool _dying;
    bool _groundDeath;
    float _deathStartTime = -1f;
    Transform _bodyRoot;
    Vector3 _bodyBindLocalPosition;
    Transform[] _bones;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();

        CacheBody();
        CacheAttackClipLength();
        FlattenSpawnPose();
        if (_animator != null && HasParameter(AttackAnimSpeedHash))
            _animator.SetFloat(AttackAnimSpeedHash, 1f);

        // Controllers with both idle takes (Snarlfang) start on one of them at random,
        // then the controller alternates every loop so each pose gets half the time.
        if (_animator != null && _animator.HasState(0, Idle1Hash) && _animator.HasState(0, Idle2Hash))
        {
            bool idle2 = Random.value < 0.5f;
            _animator.Play(idle2 ? Idle2Hash : Idle1Hash, 0, Random.value);
        }
    }

    /// <summary>True while the one-shot Spawn intro is playing or still waiting for the Animator to wake up.</summary>
    public bool IsSpawning => _spawning || _spawnPending;

    public void SetMoving(bool moving)
    {
        if (_dying || _spawning)
            return;
        if (_moving == moving)
            return;

        _moving = moving;
        PushMoving();
        if (_moving)
            TryEnterRun();
    }

    void LateUpdate()
    {
        if (_dying)
        {
            TryEnterDeath();
            if (_groundDeath)
                StickDeathToGround();
            return;
        }

        if (_spawnPending)
            TryBeginPendingSpawn();

        if (_spawning)
        {
            HoldSpawn();
            return;
        }

        // The idle states crossfade into each other on exit time. Push the bool again after
        // that transition is chosen, and enter Run or the attack immediately instead of waiting it out.
        PushMoving();
        if (_attacking)
        {
            TryEnterAttack();
            return;
        }

        if (_moving && !IsAttackPose())
            TryEnterRun();
    }

    void PushMoving()
    {
        if (!CanDriveMoving())
            return;

        _animator.SetBool(IsMovingHash, _moving);
    }

    void TryEnterRun()
    {
        if (!CanDriveMoving() || !_animator.isInitialized || !_animator.HasState(0, RunHash))
            return;
        if (IsInOrEntering(RunHash))
            return;

        _animator.CrossFadeInFixedTime(RunHash, 0.12f, 0, 0f);
    }

    bool IsAttackPose()
    {
        if (!_attacking || _animator == null)
            return false;

        return IsInOrEntering(AttackHash)
            || IsInOrEntering(LightAttackHash);
    }

    bool IsInOrEntering(int stateHash)
    {
        if (_animator.GetCurrentAnimatorStateInfo(0).shortNameHash == stateHash)
            return true;
        if (!_animator.IsInTransition(0))
            return false;

        return _animator.GetNextAnimatorStateInfo(0).shortNameHash == stateHash;
    }

    bool CanDriveMoving()
    {
        if (_animator == null || _animator.runtimeAnimatorController == null)
            return false;
        if (_hasMovingParam >= 0)
            return _hasMovingParam == 1;
        if (_animator.parameterCount == 0)
            return false;

        _hasMovingParam = HasParameter(IsMovingHash) ? 1 : 0;
        return _hasMovingParam == 1;
    }

    public void SetAttacking(bool attacking)
    {
        if (_dying || _spawning)
            return;

        if (_animator != null && HasParameter(AttackKindHash))
            _animator.SetInteger(AttackKindHash, (int)CreatureAttackKind.Basic);

        if (_attacking == attacking)
            return;

        _attacking = attacking;
        if (_animator != null && HasParameter(IsAttackingHash))
            _animator.SetBool(IsAttackingHash, attacking);

        if (_attacking)
            TryEnterAttack();
    }

    void TryEnterAttack()
    {
        if (_animator == null || !_animator.isInitialized)
            return;

        // Controllers without a LightAttack state transition on IsAttacking alone.
        if (!_animator.HasState(0, LightAttackHash))
            return;

        if (IsInOrEntering(LightAttackHash))
            return;

        _animator.CrossFadeInFixedTime(LightAttackHash, 0.08f, 0, 0f);
    }

    /// <summary>
    /// Scales the active attack clip so one cycle roughly matches 1 / attacksPerSecond.
    /// </summary>
    public void SetAttackRate(float attacksPerSecond)
    {
        if (_animator == null || !HasParameter(AttackAnimSpeedHash))
            return;

        // Snarlfang's light clip stays at authored speed so each swing is readable.
        // Hit rate still comes from CreatureStats. Grimling keeps the scaled Attack clip.
        if (_animator.HasState(0, LightAttackHash))
        {
            _animator.SetFloat(AttackAnimSpeedHash, 1f);
            return;
        }

        float clipLength = ClipLength();
        if (clipLength <= 0.01f || attacksPerSecond <= 0.01f)
        {
            _animator.SetFloat(AttackAnimSpeedHash, 1f);
            return;
        }

        float interval = 1f / attacksPerSecond;
        _animator.SetFloat(AttackAnimSpeedHash, clipLength / interval);
    }

    public void ResetToIdle()
    {
        if (_dying || _spawning)
            return;

        SetMoving(false);
        SetAttacking(false);
    }

    /// <summary>
    /// Plays the Spawn clip once, then returns to idle.
    /// Safe to call on the first frame — the Animator often is not initialized yet, so the crossfade waits.
    /// </summary>
    public void PlaySpawn()
    {
        if (_dying || _animator == null)
            return;

        _spawnPending = true;
        _spawnEnteredAt = -1f;
        TryBeginPendingSpawn();
    }

    void TryBeginPendingSpawn()
    {
        if (!_spawnPending || _dying || _animator == null || !_animator.isInitialized)
            return;

        _spawnPending = false;
        if (!_animator.HasState(0, SpawnHash))
            return;

        float length = CacheClip(ref _spawnClipLength, "Spawn");
        if (length <= 0.05f)
            return;

        _spawning = true;
        _moving = false;
        _attacking = false;
        if (HasParameter(IsMovingHash))
            _animator.SetBool(IsMovingHash, false);
        if (HasParameter(IsAttackingHash))
            _animator.SetBool(IsAttackingHash, false);
        TryEnterSpawn();
    }

    void HoldSpawn()
    {
        TryEnterSpawn();
        float length = CacheClip(ref _spawnClipLength, "Spawn");
        if (_spawnEnteredAt < 0f || length <= 0.05f)
            return;
        if (Time.time < _spawnEnteredAt + length - 0.05f)
            return;

        _spawning = false;
        if (_animator != null && _animator.HasState(0, Idle1Hash))
            _animator.CrossFadeInFixedTime(Idle1Hash, 0.15f, 0, 0f);
        else if (_animator != null && _animator.HasState(0, Animator.StringToHash("Idle")))
            _animator.CrossFadeInFixedTime("Idle", 0.15f, 0, 0f);
    }

    void TryEnterSpawn()
    {
        if (_animator == null || !_animator.isInitialized || !_animator.HasState(0, SpawnHash))
            return;
        if (_spawnEnteredAt >= 0f)
            return;

        // Spawn is the default state, so the puddle is already playing. Don't restart it.
        // Play (not a crossfade) if we are still on another pose, so the full body never blends in.
        if (!IsInOrEntering(SpawnHash))
            _animator.Play(SpawnHash, 0, 0f);

        _spawnEnteredAt = Time.time;
    }

    void FlattenSpawnPose()
    {
        if (!HasClipNamed("Spawn"))
            return;

        Transform hop = transform.Find("Hop");
        if (hop == null)
            return;

        hop.localScale = new Vector3(SpawnFlatXZ, SpawnFlatY, SpawnFlatXZ);
    }

    bool HasClipNamed(string clipName)
    {
        if (_animator == null || _animator.runtimeAnimatorController == null)
            return false;

        AnimationClip[] clips = _animator.runtimeAnimatorController.animationClips;
        if (clips == null)
            return false;

        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip != null && string.Equals(clip.name, clipName, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Plays the Death clip once. Returns its length in seconds, or 0 when this controller has no Death state.
    /// </summary>
    public float PlayDeath()
    {
        _dying = true;
        _spawning = false;
        _spawnPending = false;
        _moving = false;
        _attacking = false;

        // Mob death is particles. Kings keep a Death clip and do not carry this component.
        // Stop the Animator before a leftover Death state can sample.
        if (GetComponent<SlimeDeathSplash>() != null || GetComponent<CreatureDeathVfx>() != null)
        {
            if (_animator != null)
                _animator.enabled = false;
            ResetHopPose();
            return 0f;
        }

        if (_animator == null || !_animator.HasState(0, DeathHash))
            return 0f;

        _groundDeath = true;
        _deathStartTime = -1f;
        if (HasParameter(DieHash))
            _animator.ResetTrigger(DieHash);
        TryEnterDeath();

        float length = CacheClip(ref _deathClipLength, "Death");
        return length > 0.05f ? length : 0f;
    }

    void ResetHopPose()
    {
        Transform hop = transform.Find("Hop");
        if (hop == null)
            return;

        hop.localPosition = Vector3.zero;
        hop.localRotation = Quaternion.identity;
        hop.localScale = Vector3.one;
    }

    void TryEnterDeath()
    {
        if (_animator == null || !_animator.isInitialized || !_animator.HasState(0, DeathHash))
            return;
        if (IsInOrEntering(DeathHash))
            return;
        // A second crossfade would restart the clip from the first frame.
        if (_deathStartTime >= 0f)
            return;

        _deathStartTime = Time.time;
        _animator.CrossFadeInFixedTime(DeathHash, 0.05f, 0, 0f);
    }

    void CacheBody()
    {
        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name != "target_character")
                continue;

            _bodyRoot = transforms[i];
            _bodyBindLocalPosition = _bodyRoot.localPosition;
            break;
        }

        if (_bodyRoot == null)
            return;

        Transform[] bones = _bodyRoot.GetComponentsInChildren<Transform>(true);
        int count = 0;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] != _bodyRoot)
                count++;
        }

        _bones = new Transform[count];
        int write = 0;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == _bodyRoot)
                continue;

            _bones[write] = bones[i];
            write++;
        }
    }

    /// <summary>
    /// The death clip keeps the hips near standing height, so the fallen body floats.
    /// Drop the skeleton until its lowest bone sits on the ground.
    /// </summary>
    void StickDeathToGround()
    {
        if (_bodyRoot == null || _bones == null || _bones.Length == 0)
            return;

        _bodyRoot.localPosition = _bodyBindLocalPosition;

        Vector3 up = transform.up;
        Vector3 ground = transform.position;
        float lowest = float.PositiveInfinity;
        for (int i = 0; i < _bones.Length; i++)
        {
            Transform bone = _bones[i];
            if (bone == null)
                continue;

            float height = Vector3.Dot(bone.position - ground, up);
            if (height < lowest)
                lowest = height;
        }

        // A wild sample would throw the body off the planet. Standing hip height is about 1.5m.
        if (float.IsPositiveInfinity(lowest) || lowest < -2f || lowest > 4f)
            return;

        _bodyRoot.position -= up * lowest;
    }

    bool HasParameter(int hash)
    {
        if (_animator == null)
            return false;

        AnimatorControllerParameter[] parameters = _animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].nameHash == hash)
                return true;
        }

        return false;
    }

    void CacheAttackClipLength()
    {
        ClipLength();
    }

    float ClipLength()
    {
        float light = CacheClip(ref _lightClipLength, "LightAttack");
        if (light > 0.01f)
            return light;

        return CacheClip(ref _attackClipLength, attackClipName);
    }

    float CacheClip(ref float cached, string clipName)
    {
        if (cached > 0f || _animator == null || _animator.runtimeAnimatorController == null)
            return Mathf.Max(0f, cached);

        AnimationClip[] clips = _animator.runtimeAnimatorController.animationClips;
        if (clips == null)
            return 0f;

        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip == null)
                continue;

            if (string.Equals(clip.name, clipName, System.StringComparison.OrdinalIgnoreCase))
            {
                cached = clip.length;
                return cached;
            }
        }

        cached = 0f;
        return 0f;
    }
}
