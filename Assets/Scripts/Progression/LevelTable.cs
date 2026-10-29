using UnityEngine;

/// <summary>
/// Account progression. Cap is 30.
/// XP: level 1 takes a full hunt (about 25 slimes), then the cost grows about 18% each level.
/// Health grows 4% per level, compounded, on the player and on creatures.
/// Player attack gains a flat +3 each level. Creature attack still uses the health curve.
/// Oxygen, attack speed, range, and move speed stay as authored.
/// Level 10 health is about 1.42x, level 20 about 2.11x, level 30 about 3.12x.
/// </summary>
public static class LevelTable
{
    public const int MaxLevel = 30;

    const float BaseXp = 200f;
    const float Growth = 1.18f;
    const float PowerPerLevel = 0.04f;
    public const int AttackPerLevel = 3;

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

    /// <summary>Health. Level 1 returns 1.</summary>
    public static float PowerMultiplier(int level)
    {
        return Mathf.Pow(1f + PowerPerLevel, ClampLevel(level) - 1);
    }

    public static float ScalePower(float baseValue, int level)
    {
        return baseValue * PowerMultiplier(level);
    }

    /// <summary>Player attack. Level 1 is the authored value, then +<see cref="AttackPerLevel"/> each level.</summary>
    public static float ScaleAttack(float baseAttack, int level)
    {
        int steps = Mathf.Max(0, ClampLevel(level) - 1);
        return baseAttack + AttackPerLevel * steps;
    }
}
