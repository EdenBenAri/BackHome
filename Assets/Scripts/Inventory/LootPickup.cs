using UnityEngine;

/// <summary>
/// World-space loot pickup. Pops up along planet-up, lands, then idles.
/// Item identity is applied at spawn via <see cref="Configure"/> so one prefab can serve many items.
/// Items bob along their local Y axis while idle; sprites keep facing the camera.
/// Nearby drops arc toward the player and shrink into the inventory on arrival.
/// Uncollected drops return to the pool after <see cref="lifetimeSeconds"/>.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class LootPickup : MonoBehaviour
{
    enum Phase
    {
        Dropping,
        Idle,
        Attracting
    }

    [SerializeField] ItemDefinition item;
    [SerializeField, Min(1)] int amount = 1;
    [SerializeField, Min(0.05f)] float groundOffset = 0.35f;
    [Header("Drop")]
    [SerializeField, Min(0.05f)] float jumpHeight = 1.15f;
    [SerializeField, Min(0.05f)] float dropDuration = 0.55f;
    [SerializeField, Range(0f, 0.4f)] float bounceHeightRatio = 0.18f;
    [Header("Idle")]
    [SerializeField, Min(0f)] float idleBobHeight = 0.2f;
    [SerializeField, Min(0f)] float idleBobFrequency = 1f;
    [SerializeField] string dropState = "Drop";
    [SerializeField] string idleState = "Idle";
    [Header("Magnet")]
    [Tooltip("Distance from the player that starts attraction. Zero disables the magnet.")]
    [SerializeField, Min(0f)] float magnetRadius = 5f;
    [SerializeField, Min(0.05f)] float magnetDuration = 0.4f;
    [SerializeField, Min(0f)] float magnetArcHeight = 0.65f;
    [Tooltip("Collection height above the player's feet, along planet-up.")]
    [SerializeField, Min(0f)] float magnetTargetHeight = 0.9f;
    [Header("Lifetime")]
    [SerializeField, Min(0f)] float lifetimeSeconds = 7f;

    LootDropPool _pool;
    GameObject _prefabKey;
    SpriteRenderer _sprite;
    Transform _visual;
    Animator _animator;
    Quaternion _visualRestRotation;
    Vector3 _visualRestScale;
    Vector3 _groundPosition;
    Vector3 _up;
    Phase _phase;
    float _dropElapsed;
    float _aliveElapsed;
    float _idleElapsed;
    Transform _magnetTarget;
    PlayerVitals _magnetTargetVitals;
    Vector3 _magnetStart;
    float _magnetElapsed;
    bool _collected;
    bool _idleStarted;

    static Material _fallbackSpriteMaterial;
    static Transform _cachedPlayer;
    static PlayerVitals _cachedPlayerVitals;
    static float _nextPlayerSearchTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPlayerCache()
    {
        _cachedPlayer = null;
        _cachedPlayerVitals = null;
        _nextPlayerSearchTime = 0f;
    }

    void Awake()
    {
        _sprite = GetComponentInChildren<SpriteRenderer>(true);
        EnsureSpriteMaterial();
        _visual = _sprite != null
            ? _sprite.transform
            : (transform.childCount > 0 ? transform.GetChild(0) : transform);
        _visualRestRotation = _visual.localRotation;
        _visualRestScale = _visual.localScale;
        _animator = GetComponent<Animator>();

        SphereCollider col = GetComponent<SphereCollider>();
        col.isTrigger = true;
        if (col.radius < 0.2f)
            col.radius = 0.6f;
    }

    public void Configure(ItemDefinition definition, int dropAmount)
    {
        item = definition;
        amount = Mathf.Max(1, dropAmount);
    }

    public void ActivateFromPool(LootDropPool pool, GameObject prefabKey, Vector3 worldPosition)
    {
        _pool = pool;
        _prefabKey = prefabKey;
        _collected = false;
        _idleStarted = false;
        _dropElapsed = 0f;
        _aliveElapsed = 0f;
        _idleElapsed = 0f;
        _magnetElapsed = 0f;
        _magnetTarget = null;
        _magnetTargetVitals = null;
        _phase = Phase.Dropping;

        _up = ResolveUp(worldPosition);
        _groundPosition = worldPosition + _up * groundOffset;
        transform.SetPositionAndRotation(
            _groundPosition,
            Quaternion.FromToRotation(Vector3.up, _up));

        if (_visual != null)
        {
            _visual.localRotation = _visualRestRotation;
            _visual.localScale = _visualRestScale;
        }

        ApplyItemVisual();
        gameObject.SetActive(true);

        if (_animator != null && _sprite != null && !string.IsNullOrWhiteSpace(dropState))
        {
            _animator.Rebind();
            _animator.Play(dropState, 0, 0f);
            _animator.Update(0f);
        }
    }

    public void ReturnToPool()
    {
        if (_pool != null)
            _pool.Release(this, _prefabKey);
        else
            gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (_collected)
            return;

        if (_phase != Phase.Attracting && CanCollect())
            TryBeginAttraction();

        // Once a drop is on its way, let the collection animation finish.
        if (lifetimeSeconds > 0f && _phase != Phase.Attracting)
        {
            _aliveElapsed += Time.deltaTime;
            if (_aliveElapsed >= lifetimeSeconds)
            {
                _collected = true;
                ReturnToPool();
                return;
            }
        }

        if (_phase == Phase.Attracting)
            TickAttraction();
        else if (_phase == Phase.Dropping)
            TickDrop();
        else
            TickIdle();

        if (!_collected && _sprite != null)
            BillboardSprite();
    }

    void TickDrop()
    {
        _dropElapsed += Time.deltaTime;
        float t = dropDuration > 0.0001f ? Mathf.Clamp01(_dropElapsed / dropDuration) : 1f;
        float height = EvaluateDropHeight(t);
        transform.position = _groundPosition + _up * height;

        if (t < 1f)
            return;

        transform.position = _groundPosition;
        _phase = Phase.Idle;
        BeginIdle();
    }

    float EvaluateDropHeight(float t)
    {
        float main = 4f * t * (1f - t);
        float bounce = 0f;
        if (t > 0.72f)
        {
            float u = (t - 0.72f) / 0.28f;
            bounce = bounceHeightRatio * 4f * u * (1f - u);
        }

        return jumpHeight * (main + bounce);
    }

    void TickIdle()
    {
        _idleElapsed += Time.deltaTime;
        float height = idleBobHeight * 0.5f
            * (1f - Mathf.Cos(_idleElapsed * idleBobFrequency * 2f * Mathf.PI));
        // The pickup's local Y axis follows planet-up, just like the initial drop.
        transform.position = _groundPosition + _up * height;
    }

    void BeginIdle()
    {
        if (_idleStarted)
            return;

        _idleStarted = true;
        if (_animator != null && _sprite != null && !string.IsNullOrWhiteSpace(idleState))
            _animator.Play(idleState, 0, 0f);
    }

    bool CanCollect()
    {
        return _phase != Phase.Dropping || _dropElapsed >= dropDuration * 0.65f;
    }

    void TryBeginAttraction()
    {
        if (magnetRadius <= 0f)
            return;

        Transform player = ResolvePlayer();
        if (player == null || (player.position - transform.position).sqrMagnitude > magnetRadius * magnetRadius)
            return;

        _magnetTarget = player;
        _magnetTargetVitals = _cachedPlayerVitals;
        _magnetStart = transform.position;
        _magnetElapsed = 0f;
        _phase = Phase.Attracting;
        BeginIdle();
    }

    void TickAttraction()
    {
        if (_magnetTarget == null || !_magnetTarget.gameObject.activeInHierarchy
            || (_magnetTargetVitals != null && !_magnetTargetVitals.IsAlive))
        {
            // Keep the current position if the player dies or leaves the scene.
            _groundPosition = transform.position;
            _up = ResolveUp(_groundPosition);
            _idleElapsed = 0f;
            _phase = Phase.Idle;
            _magnetTarget = null;
            _magnetTargetVitals = null;
            _visual.localScale = _visualRestScale;
            return;
        }

        _magnetElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_magnetElapsed / Mathf.Max(0.05f, magnetDuration));
        float travel = t * t;
        Vector3 target = _magnetTarget.position + ResolveUp(_magnetTarget.position) * magnetTargetHeight;
        Vector3 position = Vector3.Lerp(_magnetStart, target, travel);

        SphericalPlanet planet = SphericalPlanet.Instance;
        if (planet != null)
        {
            // Follow the planet's curvature instead of taking a chord through its surface.
            Vector3 startOffset = _magnetStart - planet.Center;
            Vector3 targetOffset = target - planet.Center;
            Vector3 direction = Vector3.Slerp(startOffset.normalized, targetOffset.normalized, travel);
            float radius = Mathf.Lerp(startOffset.magnitude, targetOffset.magnitude, travel);
            position = planet.Center + direction * radius;
        }

        float lift = Mathf.Sin(t * Mathf.PI) * magnetArcHeight;
        transform.position = position + ResolveUp(position) * lift;

        float pulse = 1f + 0.12f * Mathf.Sin(Mathf.Clamp01(t / 0.4f) * Mathf.PI);
        float shrink = Mathf.SmoothStep(1f, 0.15f, Mathf.InverseLerp(0.65f, 1f, t));
        _visual.localScale = _visualRestScale * (pulse * shrink);

        if (t >= 1f)
            Collect();
    }

    static Transform ResolvePlayer()
    {
        if (_cachedPlayer == null || !_cachedPlayer.gameObject.activeInHierarchy)
        {
            // Share the lookup across all drops, including while waiting for a respawn.
            if (Time.time < _nextPlayerSearchTime)
                return null;

            _nextPlayerSearchTime = Time.time + 0.25f;
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            _cachedPlayer = player != null ? player.transform : null;
            _cachedPlayerVitals = player != null ? player.GetComponentInChildren<PlayerVitals>() : null;
        }

        if (_cachedPlayerVitals != null && !_cachedPlayerVitals.IsAlive)
            return null;

        return _cachedPlayer;
    }

    void EnsureSpriteMaterial()
    {
        if (_sprite == null)
            return;

        Material current = _sprite.sharedMaterial;
        if (current != null && current.shader != null && current.shader.isSupported
            && current.shader.name != "Hidden/InternalErrorShader")
            return;

        if (_fallbackSpriteMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return;

            _fallbackSpriteMaterial = new Material(shader) { name = "WorldSprite (Runtime)" };
        }

        _sprite.sharedMaterial = _fallbackSpriteMaterial;
    }

    void ApplyItemVisual()
    {
        if (_sprite != null && item != null && item.Icon != null)
            _sprite.sprite = item.Icon;
    }

    void BillboardSprite()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        _visual.rotation = cam.transform.rotation * _visualRestRotation;
    }

    void OnTriggerEnter(Collider other)
    {
        TryCollectOnContact(other);
    }

    void OnTriggerStay(Collider other)
    {
        TryCollectOnContact(other);
    }

    void TryCollectOnContact(Collider other)
    {
        // With the magnet enabled, distance detection owns collection so a trigger
        // cannot cut the animation short. Stay also handles overlapping fresh drops.
        if (_collected || _phase == Phase.Attracting || magnetRadius > 0f || !CanCollect() || !IsPlayer(other))
            return;

        PlayerVitals vitals = other.GetComponentInParent<PlayerVitals>();
        if (vitals != null && !vitals.IsAlive)
            return;

        Collect();
    }

    void Collect()
    {
        if (_collected)
            return;

        _collected = true;

        if (item != null)
        {
            PlayerInventory inventory = PlayerInventory.EnsureExists();
            inventory.Add(item, amount);
        }

        ReturnToPool();
    }

    void OnDrawGizmosSelected()
    {
        if (magnetRadius <= 0f)
            return;

        Gizmos.color = new Color(0.25f, 0.85f, 1f, 0.45f);
        Gizmos.DrawWireSphere(transform.position, magnetRadius);
    }

    static Vector3 ResolveUp(Vector3 worldPosition)
    {
        if (SphericalPlanet.Instance != null)
            return SphericalPlanet.Instance.GetUpAt(worldPosition);
        return Vector3.up;
    }

    static bool IsPlayer(Collider other)
    {
        if (other == null)
            return false;

        if (other.CompareTag("Player"))
            return true;

        Transform root = other.attachedRigidbody != null
            ? other.attachedRigidbody.transform
            : other.transform.root;

        return root != null && root.CompareTag("Player");
    }
}
