using System;
using UnityEngine;

/// <summary>
/// Account level and XP. Survives scene loads the same way inventory does,
/// because the player object is recreated on planet changes and death.
/// Killing a creature grants its <see cref="CreatureStats.Experience"/>.
/// Reaching a new level does not change stats yet — read <see cref="Level"/> when that lands.
/// </summary>
public class PlayerLevel : MonoBehaviour
{
    static PlayerLevel _instance;

    int _level = 1;
    int _xp;

    public static PlayerLevel Instance => _instance;

    public int Level => _level;
    public int Xp => _xp;
    public int XpToNext => LevelTable.XpToAdvance(_level);
    public bool IsMaxLevel => _level >= LevelTable.MaxLevel;

    /// <summary>0 at the start of a level, 1 when the next level is ready. Stays full at the cap.</summary>
    public float XpNormalized
    {
        get
        {
            if (IsMaxLevel)
                return 1f;

            int need = XpToNext;
            return need <= 0 ? 1f : Mathf.Clamp01((float)_xp / need);
        }
    }

    public event Action<int> LevelChanged;
    public event Action Changed;

    public static PlayerLevel EnsureExists()
    {
        if (_instance != null)
            return _instance;

        if (!Application.isPlaying)
            return null;

        var go = new GameObject("PlayerLevel");
        go.AddComponent<PlayerLevel>();
        return _instance;
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    public void AddExperience(int amount)
    {
        if (amount <= 0 || IsMaxLevel)
            return;

        _xp += amount;
        while (!IsMaxLevel)
        {
            int need = LevelTable.XpToAdvance(_level);
            if (need <= 0 || _xp < need)
                break;

            _xp -= need;
            _level++;
            if (IsMaxLevel)
                _xp = 0;
            LevelChanged?.Invoke(_level);
        }

        Changed?.Invoke();
    }
}
