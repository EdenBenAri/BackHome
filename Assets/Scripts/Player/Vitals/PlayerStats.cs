using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player kit: vitality, base combat, and up to <see cref="CombatLoadout.MaxWeapons"/> weapons.
/// Each gun fights with <c>base + that weapon</c> and floats on its own.
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Player Stats", fileName = "PlayerStats")]
public class PlayerStats : ScriptableObject
{
    [SerializeField] string displayName = "Player";

    [Header("Combat")]
    [SerializeField, Min(0f)] float attackDamage;
    [Tooltip("Attacks per second. 1 = deal attack damage once every second.")]
    [SerializeField, Min(0f)] float attackSpeed;
    [Tooltip("World-space radius of the attack ring around the player.")]
    [SerializeField, Min(0f)] float attackRange;

    [Header("Weapons")]
    [Tooltip("Up to 3. Each floats beside the player and fights with Combat + this weapon.")]
    [SerializeField] WeaponDefinition[] weapons;

    [Header("Movement")]
    [Tooltip("Multiplies the player's walk/run speed. 1 = normal speed.")]
    [SerializeField, Min(0.1f)] float moveSpeedMultiplier;
    [Tooltip("Multiplies the running animation's playback rate (Animator \"MotionSpeed\"). Tune so the feet don't slide/skate when move speed changes.")]
    [SerializeField, Min(0.1f)] float runningAnimationSpeed;
    [Tooltip("Percent removed from move speed while oxygen is 0. 40 = 40% slower.")]
    [SerializeField, Range(0f, 100f)] float lowOxygenMoveSpeedDebuffPercent;
    [Tooltip("Playback rate of the low-oxygen run while oxygen is 0. Lower this when that run looks too fast.")]
    [SerializeField, Min(0.05f)] float lowOxygenAnimationSpeed;

    [Header("Vitality")]
    [SerializeField, Min(1f)] float maxHealth;
    [Tooltip("Oxygen tank capacity.")]
    [SerializeField, Min(1f)] float oxygenTank;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public float MaxHealth => maxHealth;
    public float OxygenTank => oxygenTank;
    public CombatStats BaseCombat => new CombatStats(attackDamage, attackSpeed, attackRange);
    public IReadOnlyList<WeaponDefinition> Weapons => weapons ?? Array.Empty<WeaponDefinition>();
    public float MaxAttackRange => CombatLoadout.MaxRange(BaseCombat, Weapons);
    public float MoveSpeedMultiplier => moveSpeedMultiplier;
    public float RunningAnimationSpeed => runningAnimationSpeed;
    public float LowOxygenMoveSpeedDebuffPercent => lowOxygenMoveSpeedDebuffPercent;
    /// <summary>Speed factor while oxygen is empty. 40% debuff returns 0.6.</summary>
    public float LowOxygenMoveSpeedMultiplier => 1f - Mathf.Clamp(lowOxygenMoveSpeedDebuffPercent, 0f, 100f) / 100f;
    public float LowOxygenAnimationSpeed => lowOxygenAnimationSpeed;

    public CombatStats CombatFor(WeaponDefinition weapon)
    {
        return CombatLoadout.ForWeapon(BaseCombat, weapon);
    }

    void OnValidate()
    {
        if (weapons != null && weapons.Length > CombatLoadout.MaxWeapons)
            Array.Resize(ref weapons, CombatLoadout.MaxWeapons);
    }
}
