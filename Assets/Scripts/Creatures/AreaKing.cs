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
    }

    void OnEnable()
    {
        // Creature.OnEnable marks the black layer first. This runs later and switches the body to red.
        if (Application.isPlaying)
            ToonOutlineSubjects.MarkKing(this);
    }

    void Start()
    {
        if (_anim == null)
            return;

        _holding = true;
        _creature.SetFrozen(true);
        _anim.PlaySpawn();
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
