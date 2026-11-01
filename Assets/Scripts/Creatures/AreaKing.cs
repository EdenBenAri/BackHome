using UnityEngine;

/// <summary>
/// Marks a creature as an area king and plays its spawn intro before it can chase or attack.
/// The intro is the Spawn state on <see cref="CreatureAnimator"/>. Regular mobs do not use this.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Creature))]
[DefaultExecutionOrder(80)]
public class AreaKing : MonoBehaviour
{
    Creature _creature;
    CreatureAnimator _anim;
    bool _holding;

    void Awake()
    {
        _creature = GetComponent<Creature>();
        _anim = GetComponent<CreatureAnimator>();
        if (_anim == null)
            _anim = GetComponentInChildren<CreatureAnimator>();

        if (GetComponent<AreaKingGlow>() == null)
            gameObject.AddComponent<AreaKingGlow>();
        if (GetComponent<AreaKingLeashIndicator>() == null)
            gameObject.AddComponent<AreaKingLeashIndicator>();
    }

    void OnEnable()
    {
        // Creature.OnEnable marks the black layer first. This runs later and switches the body to red.
        if (Application.isPlaying)
            ToonOutlineSubjects.MarkKing(this);
    }

    void Start()
    {
        FacePlayer(FindAnyObjectByType<PlayerVitals>()?.transform);

        if (_anim == null)
            return;

        _holding = true;
        _creature.SetFrozen(true);
        _anim.PlaySpawn();
    }

    /// <summary>Face the player immediately while preserving the king's surface alignment.</summary>
    public void FacePlayer(Transform player)
    {
        if (player == null)
            return;

        Vector3 up = transform.up;
        Vector3 forward = Vector3.ProjectOnPlane(player.position - transform.position, up);
        if (forward.sqrMagnitude < 0.001f)
            return;

        transform.rotation = Quaternion.LookRotation(forward.normalized, up);
    }

    void Update()
    {
        if (!_holding)
            return;
        if (_anim != null && _anim.IsSpawning)
            return;

        _holding = false;
        if (_creature != null && _creature.IsAlive)
            _creature.SetFrozen(false);
    }
}
