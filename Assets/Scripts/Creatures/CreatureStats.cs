using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Trick a creature plays when it first spots the player.
/// Add a value here, then handle it in <see cref="CreatureAbility"/>.
/// </summary>
public enum CreatureAttackKind
{
    Basic = 0,
    Special = 1,
}

/// <summary>
/// Trick a creature plays when it first spots the player.
/// Add a value here, handle it in <see cref="CreatureAbility"/>,
/// and give it a field group in <see cref="CreatureStatsEditor"/>.
/// </summary>
public enum CreatureSpecialAbility
{
    None = 0,
    [InspectorName("Snarlfang")]
    Stealth = 1,
}

[Serializable]
public struct LootEntry
{
    public ItemDefinition item;
    [Min(1)] public int minAmount;
    [Min(1)] public int maxAmount;
    [Tooltip("0 = never, 1 = always.")]
    [Range(0f, 1f)] public float chance;
}

/// <summary>
/// Shared stat definition for a creature type (HP, attack, range, ability, loot).
/// Create one asset per creature (e.g. GrimlingStats) and reuse it on every instance.
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Creature Stats", fileName = "CreatureStats")]
public class CreatureStats : ScriptableObject
{
    [SerializeField] string displayName = "Creature";

    [Header("Basic Attack")]
    [SerializeField, Min(0f)] float attackDamage = 10f;
    [Tooltip("Basic attacks per second.")]
    [SerializeField, Min(0.01f)] float attackSpeed = 1f;
    [Tooltip("World-space radius of a basic attack.")]
    [SerializeField, Min(0.05f)] float attackRange = 2f;

    [Header("Vitality")]
    [SerializeField, Min(1f)] float maxHealth = 50f;
    [Tooltip("World-space radius in which this creature detects the player and starts chasing.")]
    [SerializeField, Min(0f)] float visionRange = 10f;

    [Header("Movement")]
    [Tooltip("Surface speed while chasing the player or walking home.")]
    [SerializeField, Min(0.1f)] float movementSpeed = 4f;

    [Header("Special Ability")]
    [Tooltip("Chosen trick. Each ability has its own fields.")]
    [SerializeField] CreatureSpecialAbility specialAbility = CreatureSpecialAbility.None;
    [Tooltip("Snarlfang fades for up to this many seconds, and ends sooner on reaching Special Attack Range.")]
    [SerializeField, Min(0f)] float abilityDuration = 2f;
    [Tooltip("Snarlfang only. 0 = invisible, 1 = solid. Low values read as transparent.")]
    [SerializeField, Range(0f, 1f)] float stealthOpacity = 0.22f;
    [Tooltip("Damage of the single Special Attack that plays when Snarlfang ends.")]
    [SerializeField, Min(0f), FormerlySerializedAs("heavyAttackDamage")] float specialAttackDamage = 30f;
    [Tooltip("Special attacks per second for that one Snarlfang exit strike.")]
    [SerializeField, Min(0.01f), FormerlySerializedAs("heavyAttackSpeed")] float specialAttackSpeed = 1f;
    [Tooltip("Snarlfang ends when the player is inside this radius. The Special Attack uses this range.")]
    [SerializeField, Min(0.05f), FormerlySerializedAs("heavyAttackRange")] float specialAttackRange = 2.5f;

    [Header("Loot")]
    [Tooltip("Rolled independently on death. Chance 0 = never, 1 = always.")]
    [SerializeField] LootEntry[] loot = Array.Empty<LootEntry>();

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public float MaxHealth => maxHealth;
    public float VisionRange => visionRange;
    public float MovementSpeed => movementSpeed;
    public float AttackDamage => attackDamage;
    public float AttackSpeed => attackSpeed;
    public float AttackRange => attackRange;
    public float SpecialAttackDamage => specialAttackDamage;
    public float SpecialAttackSpeed => specialAttackSpeed;
    public float SpecialAttackRange => specialAttackRange;
    public CreatureSpecialAbility SpecialAbility => specialAbility;
    public float AbilityDuration => abilityDuration;
    public float StealthOpacity => stealthOpacity;
    public LootEntry[] Loot => loot;

    public void GetAttack(CreatureAttackKind kind, out float damage, out float speed, out float range)
    {
        if (kind == CreatureAttackKind.Special
            && specialAbility == CreatureSpecialAbility.Stealth)
        {
            damage = specialAttackDamage;
            speed = specialAttackSpeed;
            range = specialAttackRange;
            return;
        }

        damage = attackDamage;
        speed = attackSpeed;
        range = attackRange;
    }

    public bool TryRollLoot(int index, out ItemDefinition item, out int amount)
    {
        item = null;
        amount = 0;
        if (loot == null || index < 0 || index >= loot.Length)
            return false;

        LootEntry entry = loot[index];
        if (entry.item == null || entry.chance <= 0f)
            return false;

        if (entry.chance < 1f && UnityEngine.Random.value > entry.chance)
            return false;

        int min = Mathf.Max(1, entry.minAmount);
        int max = Mathf.Max(min, entry.maxAmount);
        amount = min == max ? min : UnityEngine.Random.Range(min, max + 1);
        item = entry.item;
        return true;
    }

    void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        visionRange = Mathf.Max(0f, visionRange);
        movementSpeed = Mathf.Max(0.1f, movementSpeed);
        attackDamage = Mathf.Max(0f, attackDamage);
        attackSpeed = Mathf.Max(0.01f, attackSpeed);
        attackRange = Mathf.Max(0.05f, attackRange);
        specialAttackDamage = Mathf.Max(0f, specialAttackDamage);
        specialAttackSpeed = Mathf.Max(0.01f, specialAttackSpeed);
        specialAttackRange = Mathf.Max(0.05f, specialAttackRange);
        abilityDuration = Mathf.Max(0f, abilityDuration);
        stealthOpacity = Mathf.Clamp01(stealthOpacity);

        if (loot == null)
            return;

        for (int i = 0; i < loot.Length; i++)
        {
            LootEntry entry = loot[i];
            bool newRow = entry.minAmount <= 0 && entry.maxAmount <= 0;
            entry.minAmount = Mathf.Max(1, entry.minAmount);
            entry.maxAmount = Mathf.Max(entry.minAmount, entry.maxAmount);
            entry.chance = newRow ? 1f : Mathf.Clamp01(entry.chance);
            loot[i] = entry;
        }
    }
}
