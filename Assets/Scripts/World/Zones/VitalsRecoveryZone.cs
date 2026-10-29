using UnityEngine;

/// <summary>
/// Safe volume. While the player stands inside this trigger, oxygen and health drain stop.
/// A normal zone refills both slowly. An instant zone fills both to full the moment they step in.
/// </summary>
[RequireComponent(typeof(Collider))]
public class VitalsRecoveryZone : MonoBehaviour
{
    [Tooltip("Oxygen restored per second while the player is inside. Ignored when Instant is on.")]
    [SerializeField, Min(0f)] float oxygenPerSecond = 5f;
    [Tooltip("Health restored per second while the player is inside. Ignored when Instant is on.")]
    [SerializeField, Min(0f)] float healthPerSecond = 40f;
    [Tooltip("Fill health and oxygen to full the moment the player enters, instead of over time.")]
    [SerializeField] bool instant;

    PlayerVitals _occupant;
    int _overlapCount;
    bool _spawnInside = true;

    void Reset()
    {
        EnsureTrigger();
    }

    void Awake()
    {
        EnsureTrigger();
    }

    void OnDisable()
    {
        Release();
    }

    void Update()
    {
        if (_occupant == null)
            return;

        if (!_occupant.IsAlive)
        {
            Release();
            return;
        }

        if (instant)
            return;

        _occupant.Recover(healthPerSecond * Time.deltaTime, oxygenPerSecond * Time.deltaTime);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!TryResolve(other, out _))
            return;

        _overlapCount++;
        if (_overlapCount == 1)
            Begin(other);
    }

    void OnTriggerStay(Collider other)
    {
        if (_overlapCount > 0 || !TryResolve(other, out PlayerVitals vitals) || !vitals.IsAlive)
            return;

        _overlapCount = 1;
        Begin(other);
    }

    void OnTriggerExit(Collider other)
    {
        if (_overlapCount <= 0 || !TryResolve(other, out PlayerVitals vitals))
            return;

        if (_occupant != null && vitals != _occupant)
            return;

        _overlapCount = Mathf.Max(0, _overlapCount - 1);
        if (_overlapCount == 0)
            Release();
    }

    void Begin(Collider other)
    {
        if (!TryResolve(other, out PlayerVitals vitals) || vitals == null)
            return;

        if (!vitals.IsAlive || _occupant == vitals)
            return;

        if (_occupant != null)
            _occupant.SetInRecoveryZone(false);

        _occupant = vitals;
        _occupant.SetInRecoveryZone(true);
        if (instant)
            _occupant.Refill();

        bool spawnedHere = _spawnInside;
        _spawnInside = false;
        if (spawnedHere)
            return;

        CampRadiusRing ring = GetComponent<CampRadiusRing>();
        if (ring != null)
            ring.PulseEnter();
    }

    void Release()
    {
        if (_occupant != null)
            _occupant.SetInRecoveryZone(false);

        _occupant = null;
        _overlapCount = 0;
    }

    void EnsureTrigger()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;

        Rigidbody body = GetComponent<Rigidbody>();
        if (body == null)
            body = gameObject.AddComponent<Rigidbody>();

        body.isKinematic = true;
        body.useGravity = false;
    }

    static bool TryResolve(Collider other, out PlayerVitals vitals)
    {
        vitals = other != null ? other.GetComponentInParent<PlayerVitals>() : null;
        return vitals != null;
    }
}
