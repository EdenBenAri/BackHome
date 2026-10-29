using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime vitals. Level 1 HP / oxygen / damage come from <see cref="PlayerStats"/>.
/// <see cref="LevelTable"/> then raises health by 4% per account level and attack by a flat +3.
/// The oxygen tank stays at the authored size. Weapons are copied at spawn (up to <see cref="CombatLoadout.MaxWeapons"/>)
/// so play mode can swap guns without dirtying the shared asset. Each gun fights with base + itself,
/// and that hit damage scales with level. Attack speed, range, move speed, and oxygen stay as authored.
/// Drain rates stay here as survival feel.
/// </summary>
public class PlayerVitals : MonoBehaviour, IVitalsReadable
{
    [Header("Stats")]
    [SerializeField] PlayerStats stats;

    [Header("Drain Rates")]
    [Tooltip("Oxygen lost per second.")]
    [SerializeField] float oxygenDrainPerSecond = 4f;
    [Tooltip("HP lost per second while oxygen is empty.")]
    [SerializeField] float healthDrainPerSecond = 5f;

    readonly List<WeaponDefinition> _loadout = new();
    bool _loadoutReady;
    float _currentHealth;
    float _currentOxygen;
    bool _invulnerable;
    int _recoveryZones;
    PlayerLevel _boundLevel;

    public PlayerStats Stats => stats;
    public string DisplayName => stats != null ? stats.DisplayName : name;
    public float MaxHealth => stats != null ? LevelTable.ScalePower(stats.MaxHealth, AccountLevel) : 0f;
    public float MaxOxygen => stats != null ? stats.OxygenTank : 0f;
    public CombatStats BaseCombat => ScaleCombat(stats != null ? stats.BaseCombat : CombatStats.Zero);
    public float AttackRange => CombatLoadout.MaxRange(BaseCombat, ActiveWeapons);
    public IReadOnlyList<WeaponDefinition> Weapons => ActiveWeapons;
    public WeaponDefinition PrimaryWeapon => CombatLoadout.Primary(ActiveWeapons);

    public CombatStats CombatFor(WeaponDefinition weapon)
    {
        CombatStats raw = stats != null ? stats.BaseCombat : CombatStats.Zero;
        return ScaleCombat(CombatLoadout.ForWeapon(raw, weapon));
    }

    int AccountLevel
    {
        get
        {
            if (!Application.isPlaying)
                return 1;

            PlayerLevel level = PlayerLevel.Instance != null ? PlayerLevel.Instance : PlayerLevel.EnsureExists();
            return level != null ? level.Level : 1;
        }
    }

    public float CurrentHealth => _currentHealth;
    public float CurrentOxygen => _currentOxygen;
    public float HealthNormalized => MaxHealth > 0f ? _currentHealth / MaxHealth : 0f;
    public float OxygenNormalized => MaxOxygen > 0f ? _currentOxygen / MaxOxygen : 0f;
    public bool IsAlive => _currentHealth > 0f;
    public bool HasStats => stats != null;
    public bool HasOxygen => MaxOxygen > 0f;
    public bool IsInvulnerable => _invulnerable;
    public bool IsRecovering => _recoveryZones > 0;

    IReadOnlyList<WeaponDefinition> ActiveWeapons
    {
        get
        {
            if (Application.isPlaying)
            {
                EnsureLoadout();
                return _loadout;
            }

            return stats != null ? stats.Weapons : Array.Empty<WeaponDefinition>();
        }
    }

    public event Action VitalsChanged;
    public event Action<float> Damaged;
    public event Action Died;
    public event Action LoadoutChanged;

    void Awake()
    {
        RebuildLoadout();
        BindLevel();
        ResetVitals();
    }

    void OnEnable()
    {
        BindLevel();
    }

    void OnDestroy()
    {
        UnbindLevel();
    }

    void OnValidate()
    {
        oxygenDrainPerSecond = Mathf.Max(0f, oxygenDrainPerSecond);
        healthDrainPerSecond = Mathf.Max(0f, healthDrainPerSecond);

        if (!Application.isPlaying && stats != null)
        {
            _currentHealth = stats.MaxHealth;
            _currentOxygen = stats.OxygenTank;
        }
    }

    void Start()
    {
        RaiseChanged();
    }

    void Update()
    {
        if (_invulnerable || _recoveryZones > 0)
            return;

        if (_currentOxygen > 0f)
        {
            float next = Mathf.Max(0f, _currentOxygen - oxygenDrainPerSecond * Time.deltaTime);
            if (!Mathf.Approximately(next, _currentOxygen))
            {
                _currentOxygen = next;
                RaiseChanged();
            }
        }
        else if (_currentHealth > 0f && healthDrainPerSecond > 0f)
        {
            float next = Mathf.Max(0f, _currentHealth - healthDrainPerSecond * Time.deltaTime);
            if (!Mathf.Approximately(next, _currentHealth))
            {
                _currentHealth = next;
                RaiseChanged();
                if (!IsAlive)
                    Died?.Invoke();
            }
        }
    }

    public void SetStats(PlayerStats newStats, bool refill = true)
    {
        stats = newStats;
        RebuildLoadout();
        if (refill)
            ResetVitals();
        else
            ApplyLevelCaps();
        LoadoutChanged?.Invoke();
    }

    /// <summary>Replaces the runtime weapon list. Does not mutate the <see cref="PlayerStats"/> asset.</summary>
    public void SetWeapons(IReadOnlyList<WeaponDefinition> weapons)
    {
        _loadoutReady = true;
        CombatLoadout.CopyClamped(weapons, _loadout);
        RaiseLoadoutChanged();
    }

    /// <summary>Sets slot 0 (the first floating weapon). Other equipped guns stay in later slots.</summary>
    public void SetPrimaryWeapon(WeaponDefinition weapon)
    {
        EnsureLoadout();
        if (weapon == null)
        {
            if (_loadout.Count > 0)
                _loadout.RemoveAt(0);
        }
        else if (_loadout.Count == 0)
            _loadout.Add(weapon);
        else
            _loadout[0] = weapon;

        RaiseLoadoutChanged();
    }

    public void ResetVitals()
    {
        _currentHealth = MaxHealth;
        _currentOxygen = MaxOxygen;
        RaiseChanged();
    }

    public void ResetHealth()
    {
        _currentHealth = MaxHealth;
        RaiseChanged();
    }

    public void SetInvulnerable(bool invulnerable)
    {
        _invulnerable = invulnerable;
    }

    public void TakeDamage(float amount)
    {
        if (_invulnerable || !IsAlive || amount <= 0f)
            return;

        _currentHealth = Mathf.Max(0f, _currentHealth - amount);
        RaiseChanged();
        Damaged?.Invoke(amount);

        if (!IsAlive)
            Died?.Invoke();
    }

    public void Heal(float amount)
    {
        if (!IsAlive || amount <= 0f || MaxHealth <= 0f)
            return;

        float next = Mathf.Min(MaxHealth, _currentHealth + amount);
        if (Mathf.Approximately(next, _currentHealth))
            return;

        _currentHealth = next;
        RaiseChanged();
    }

    public void RefillOxygen()
    {
        _currentOxygen = MaxOxygen;
        RaiseChanged();
    }

    /// <summary>Sets health and oxygen to their current maximum in one step.</summary>
    public void Refill()
    {
        if (!IsAlive)
            return;

        float nextHealth = MaxHealth;
        float nextOxygen = MaxOxygen;
        bool changed = false;

        if (nextHealth > 0f && _currentHealth < nextHealth)
        {
            _currentHealth = nextHealth;
            changed = true;
        }

        if (nextOxygen > 0f && _currentOxygen < nextOxygen)
        {
            _currentOxygen = nextOxygen;
            changed = true;
        }

        if (changed)
            RaiseChanged();
    }

    public void AddOxygen(float amount)
    {
        if (amount <= 0f || MaxOxygen <= 0f)
            return;

        _currentOxygen = Mathf.Min(MaxOxygen, _currentOxygen + amount);
        RaiseChanged();
    }

    /// <summary>Called by a recovery trigger. Overlapping zones stack; drain stays off until the last one is left.</summary>
    public void SetInRecoveryZone(bool inside)
    {
        if (inside)
            _recoveryZones++;
        else
            _recoveryZones = Mathf.Max(0, _recoveryZones - 1);
    }

    /// <summary>Adds health and oxygen together, and notifies listeners only when something actually changed.</summary>
    public void Recover(float healthAmount, float oxygenAmount)
    {
        if (!IsAlive)
            return;

        bool changed = false;

        if (healthAmount > 0f && MaxHealth > 0f && _currentHealth < MaxHealth)
        {
            float next = Mathf.Min(MaxHealth, _currentHealth + healthAmount);
            if (!Mathf.Approximately(next, _currentHealth))
            {
                _currentHealth = next;
                changed = true;
            }
        }

        if (oxygenAmount > 0f && MaxOxygen > 0f && _currentOxygen < MaxOxygen)
        {
            float next = Mathf.Min(MaxOxygen, _currentOxygen + oxygenAmount);
            if (!Mathf.Approximately(next, _currentOxygen))
            {
                _currentOxygen = next;
                changed = true;
            }
        }

        if (changed)
            RaiseChanged();
    }

    void BindLevel()
    {
        if (!Application.isPlaying)
            return;

        PlayerLevel level = PlayerLevel.EnsureExists();
        if (level == null || level == _boundLevel)
            return;

        UnbindLevel();
        _boundLevel = level;
        _boundLevel.LevelChanged += OnLevelChanged;
    }

    void UnbindLevel()
    {
        if (_boundLevel == null)
            return;

        _boundLevel.LevelChanged -= OnLevelChanged;
        _boundLevel = null;
    }

    void OnLevelChanged(int _)
    {
        Refill();
    }

    /// <summary>
    /// Keeps the current bars inside the new max when stats change without a refill.
    /// A level-up does not use this. It calls <see cref="Refill"/> so both bars snap to full.
    /// </summary>
    void ApplyLevelCaps()
    {
        float nextHealth = MaxHealth;
        float nextOxygen = MaxOxygen;

        if (nextHealth > 0f)
            _currentHealth = Mathf.Min(_currentHealth, nextHealth);
        if (nextOxygen > 0f)
            _currentOxygen = Mathf.Min(_currentOxygen, nextOxygen);

        RaiseChanged();
    }

    CombatStats ScaleCombat(CombatStats raw)
    {
        return new CombatStats(
            LevelTable.ScaleAttack(raw.AttackDamage, AccountLevel),
            raw.AttackSpeed,
            raw.AttackRange);
    }

    void EnsureLoadout()
    {
        if (_loadoutReady)
            return;
        RebuildLoadout();
    }

    void RebuildLoadout()
    {
        _loadout.Clear();
        _loadoutReady = true;
        if (stats == null)
            return;

        CombatLoadout.CopyClamped(stats.Weapons, _loadout);
    }

    void RaiseLoadoutChanged()
    {
        RaiseChanged();
        LoadoutChanged?.Invoke();
    }

    void RaiseChanged()
    {
        VitalsChanged?.Invoke();
    }
}
