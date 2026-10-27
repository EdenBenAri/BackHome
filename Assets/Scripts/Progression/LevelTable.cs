using UnityEngine;

/// <summary>
/// Account progression. Cap is 30.
/// XP: level 1 takes a full hunt (about 25 slimes), then the cost grows about 18% each level.
/// Power: health and attack damage grow 4% per level, compounded, on the player and on creatures.
/// Oxygen grows 2% per level. Attack speed, range, and move speed are feel, so they stay put.
/// Level 10 is about 1.42x power, level 20 about 2.11x, level 30 about 3.12x.
/// A player whose level matches a creature's level fights that creature's authored body.
/// </summary>
public static class LevelTable
{
    public const int MaxLevel = 30;

    const float BaseXp = 200f;
    const float Growth = 1.18f;
    const float PowerPerLevel = 0.04f;
    const float OxygenPerLevel = 0.02f;

    /// <summary>XP required to advance from <paramref name="level"/> to the next. 0 at the cap.</summary>
    public static int XpToAdvance(int level)
    {
        if (level < 1 || level >= MaxLevel)
            return 0;

        return Mathf.Max(1, Mathf.RoundToInt(BaseXp * Mathf.Pow(Growth, level - 1)));
    }

    public static int ClampLevel(int level)
    {
        return Mathf.Clamp(level, 1, MaxLevel);
    }

    /// <summary>Health and attack damage. Level 1 returns 1.</summary>
    public static float PowerMultiplier(int level)
    {
        return Mathf.Pow(1f + PowerPerLevel, ClampLevel(level) - 1);
    }

    /// <summary>Oxygen tank. Level 1 returns 1. Slower than <see cref="PowerMultiplier"/>.</summary>
    public static float OxygenMultiplier(int level)
    {
        return Mathf.Pow(1f + OxygenPerLevel, ClampLevel(level) - 1);
    }

    public static float ScalePower(float baseValue, int level)
    {
        return baseValue * PowerMultiplier(level);
    }

    public static float ScaleOxygen(float baseValue, int level)
    {
        return baseValue * OxygenMultiplier(level);
    }
}
