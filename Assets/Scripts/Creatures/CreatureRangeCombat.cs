using UnityEngine;

/// <summary>
/// Damages the player when they are inside this creature's attack range.
/// Instant hit on enter, then ticks every 1 / attack speed seconds while the player stays inside.
/// </summary>
[DefaultExecutionOrder(40)]
[RequireComponent(typeof(Creature))]
public class CreatureRangeCombat : MonoBehaviour
{
    Creature _creature;
    CreatureAnimator _anim;
    CreatureChase _chase;
    PlayerVitals _player;
    float _tickCooldown;
    bool _playerInside;
    bool _hitPlayerThisFrame;

    void Awake()
    {
        _creature = GetComponent<Creature>();
        _anim = GetComponent<CreatureAnimator>();
        if (_anim == null)
            _anim = GetComponentInChildren<CreatureAnimator>();
        _chase = GetComponent<CreatureChase>();
    }

    void Update()
    {
        _hitPlayerThisFrame = false;

        if (_creature == null || !_creature.IsAlive || _creature.IsFrozen)
        {
            _playerInside = false;
            _anim?.SetAttacking(false);
            return;
        }

        // No damage / attack anim while idle or returning home.
        if (_chase != null && !_chase.IsAggroed)
        {
            _playerInside = false;
            _anim?.SetAttacking(false);
            return;
        }

        // Brief flinch: keep occupancy so we don't re-trigger an enter hit after the shove.
        if (_chase != null && _chase.IsKnockedBack)
        {
            _anim?.SetAttacking(false);
            return;
        }

        float damage = _creature.AttackDamage;
        float attackSpeed = _creature.AttackSpeed;
        float radius = _creature.AttackRange;
        if (attackSpeed <= 0f || damage <= 0f || radius <= 0f)
        {
            _anim?.SetAttacking(false);
            return;
        }

        if (!TryResolvePlayer(out PlayerVitals player) || !player.IsAlive)
        {
            _playerInside = false;
            _anim?.SetAttacking(false);
            return;
        }

        bool inside = IsTargetInRange(player.transform.position, radius);

        if (inside)
        {
            if (!_playerInside)
            {
                player.TakeDamage(damage);
                _hitPlayerThisFrame = true;
                _chase?.ResetLeashTimer();
            }

            _playerInside = true;
        }
        else
        {
            _playerInside = false;
        }

        _anim?.SetAttacking(_playerInside);
        if (_playerInside)
            _anim?.SetAttackRate(attackSpeed);

        float interval = 1f / attackSpeed;
        _tickCooldown -= Time.deltaTime;
        if (_tickCooldown > 0f)
            return;

        _tickCooldown = interval;

        if (_playerInside && !_hitPlayerThisFrame && player.IsAlive)
        {
            player.TakeDamage(damage);
            _chase?.ResetLeashTimer();
        }
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

    bool IsTargetInRange(Vector3 targetPosition, float radius)
    {
        Vector3 origin = transform.position;

        if (SphericalPlanet.Instance != null)
        {
            float distance = PlanetSurfacePose.GetSurfaceDistance(SphericalPlanet.Instance.Center, origin, targetPosition);
            return distance <= radius;
        }

        Vector3 planar = Vector3.ProjectOnPlane(targetPosition - origin, transform.up);
        return planar.sqrMagnitude <= radius * radius;
    }
}
