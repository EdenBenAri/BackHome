using UnityEngine;

/// <summary>
/// Compounded account growth and encounter budgets, capped at level 30.
/// Enemy health follows player damage; enemy damage follows player health.
/// Equal-level encounters keep their intended duration and danger. Returning to an
/// earlier area becomes easier because enemies keep their authored encounter level.
/// Oxygen capacity, attack speed, range and movement do not grow with level.
/// </summary>
public static class LevelTable
{
    public const int MaxLevel = 30;

    public const float BaseXp = 80f;
    public const float XpGrowth = 1.15f;
    public const float RewardGrowth = 1.14f;
    public const float AttackGrowthPercent = 10f;
    public const float HealthGrowthPercent = 8f;

    /// <summary>XP required to advance from <paramref name="level"/> to the next. 0 at the cap.</summary>
    public static int XpToAdvance(int level)
    {
        if (level < 1 || level >= MaxLevel)
            return 0;

        return Mathf.Max(1, Mathf.RoundToInt(BaseXp * Mathf.Pow(XpGrowth, level - 1)));
    }

    public static int ClampLevel(int level)
    {
        return Mathf.Clamp(level, 1, MaxLevel);
    }

    /// <summary>Health. Level 1 returns 1.</summary>
    public static float PowerMultiplier(int level)
    {
        return Mathf.Pow(1f + HealthGrowthPercent / 100f, ClampLevel(level) - 1);
    }

    public static float ScalePower(float baseValue, int level)
    {
        return baseValue * PowerMultiplier(level);
    }

    public static float AttackMultiplier(int level) =>
        Mathf.Pow(1f + AttackGrowthPercent / 100f, ClampLevel(level) - 1);

    /// <summary>Multiply the complete base + weapon hit once, without rounding between levels.</summary>
    public static float ScaleAttack(float baseAttack, int level)
    {
        return baseAttack * AttackMultiplier(level);
    }

    public static float ScaleEnemyHealth(float baseHealth, int level) => baseHealth * AttackMultiplier(level);
    public static float ScaleEnemyDamage(float baseDamage, int level) => baseDamage * PowerMultiplier(level);

    /// <summary>Harder areas pay more; farming an old area never increases its XP with player level.</summary>
    public static int ScaleExperience(int baseExperience, int level) => baseExperience <= 0 ? 0
        : Mathf.Max(1, Mathf.RoundToInt(baseExperience * Mathf.Pow(RewardGrowth, ClampLevel(level) - 1)));
}
