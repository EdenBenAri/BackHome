using UnityEngine;

/// <summary>
/// XP cost to leave each account level. Cap is 30 for now.
/// Level 1 takes a full hunt (about 25 slimes), then the cost grows about 18%
/// each level. Levels do not grant stats yet.
/// </summary>
public static class LevelTable
{
    public const int MaxLevel = 30;

    const float BaseXp = 200f;
    const float Growth = 1.18f;

    /// <summary>XP required to advance from <paramref name="level"/> to the next. 0 at the cap.</summary>
    public static int XpToAdvance(int level)
    {
        if (level < 1 || level >= MaxLevel)
            return 0;

        return Mathf.Max(1, Mathf.RoundToInt(BaseXp * Mathf.Pow(Growth, level - 1)));
    }
}
