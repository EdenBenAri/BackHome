using TMPro;
using UnityEngine;

/// <summary>
/// Shows the level-up row already built on this HUD prefab.
/// Current labels open on the stat from before the level, count up to the new stat,
/// and the row hides only after that count finishes. Bonus labels stay on the gain.
/// </summary>
public class LevelUpStatsBanner : MonoBehaviour
{
    [SerializeField] CanvasGroup group;
    [SerializeField] TMP_Text currentAttack;
    [SerializeField] TMP_Text currentHealth;
    [SerializeField] TMP_Text attackBonus;
    [SerializeField] TMP_Text healthBonus;
    [SerializeField] float delay = 0.72f;
    [SerializeField] float pop = 0.16f;
    [Tooltip("How long the pre-level numbers stay readable before they start counting.")]
    [SerializeField] float hold = 0.45f;
    [Tooltip("How long CurrentAttack and CurrentHealth take to roll from the old stat to the new one.")]
    [SerializeField] float countUp = 0.8f;
    [Tooltip("Pause on the new numbers before the row fades.")]
    [SerializeField] float settle = 0.4f;
    [SerializeField] float fade = 0.35f;

    PlayerLevel _level;
    Vector3 _baseScale = Vector3.one;
    Vector3 _attackScale = Vector3.one;
    Vector3 _healthScale = Vector3.one;
    int _attackFrom;
    int _attackTo;
    int _healthFrom;
    int _healthTo;
    float _age = -1f;
    bool _shown;

    void Awake()
    {
        if (group == null)
            group = GetComponent<CanvasGroup>();
        ResolveLabels();
        _baseScale = transform.localScale;
        if (currentAttack != null)
            _attackScale = currentAttack.transform.localScale;
        if (currentHealth != null)
            _healthScale = currentHealth.transform.localScale;
        HideImmediate();
    }

    void OnEnable()
    {
        Bind();
    }

    void OnDisable()
    {
        Unbind();
    }

    void Update()
    {
        if (_level == null)
            Bind();
        Tick();
    }

    void Bind()
    {
        if (_level != null)
            return;

        _level = PlayerLevel.EnsureExists();
        if (_level != null)
            _level.LevelChanged += OnLevelChanged;
    }

    void Unbind()
    {
        if (_level == null)
            return;

        _level.LevelChanged -= OnLevelChanged;
        _level = null;
    }

    void OnLevelChanged(int level)
    {
        WriteGains(level);
        _age = 0f;
        _shown = true;
    }

    [ContextMenu("Preview Level Up")]
    void Preview()
    {
        ResolveLabels();
        int level = 2;
        if (PlayerLevel.Instance != null)
            level = Mathf.Max(2, PlayerLevel.Instance.Level);
        WriteGains(level);
        _age = 0f;
        _shown = true;
    }

    void ResolveLabels()
    {
        currentAttack = currentAttack != null ? currentAttack : FindLabel("CurrentAttack");
        currentHealth = currentHealth != null ? currentHealth : FindLabel("CurrentHealth");
        attackBonus = attackBonus != null ? attackBonus : FindLabel("AttackBonus");
        healthBonus = healthBonus != null ? healthBonus : FindLabel("HealthBonus");
    }

    TMP_Text FindLabel(string objectName)
    {
        TMP_Text[] labels = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] != null && labels[i].name == objectName)
                return labels[i];
        }

        return null;
    }

    void WriteGains(int level)
    {
        ResolveLabels();

        int attackNow = 0;
        int healthNow = 0;
        int attackGain = 0;
        int healthGain = 0;
        PlayerVitals vitals = FindAnyObjectByType<PlayerVitals>();
        if (vitals != null && vitals.Stats != null && level > 0)
        {
            float attackBase = vitals.Stats.BaseCombat.AttackDamage;
            float healthBase = vitals.Stats.MaxHealth;
            attackNow = Mathf.RoundToInt(LevelTable.ScaleAttack(attackBase, level));
            healthNow = Mathf.RoundToInt(LevelTable.ScalePower(healthBase, level));
            if (level > 1)
            {
                int previous = level - 1;
                attackGain = Mathf.Max(0, attackNow - Mathf.RoundToInt(LevelTable.ScaleAttack(attackBase, previous)));
                healthGain = Mathf.Max(0, healthNow - Mathf.RoundToInt(LevelTable.ScalePower(healthBase, previous)));
            }
        }

        _attackTo = attackNow;
        _healthTo = healthNow;
        _attackFrom = attackNow - attackGain;
        _healthFrom = healthNow - healthGain;
        SetCurrent(_attackFrom, _healthFrom);
        if (attackBonus != null)
            attackBonus.text = "+" + attackGain;
        if (healthBonus != null)
            healthBonus.text = "+" + healthGain;
    }

    void SetCurrent(int attack, int health)
    {
        if (currentAttack != null)
            currentAttack.text = attack.ToString();
        if (currentHealth != null)
            currentHealth.text = health.ToString();
    }

    void RollCurrent(float u)
    {
        float eased = 1f - (1f - Mathf.Clamp01(u)) * (1f - Mathf.Clamp01(u));
        SetCurrent(
            Mathf.RoundToInt(Mathf.Lerp(_attackFrom, _attackTo, eased)),
            Mathf.RoundToInt(Mathf.Lerp(_healthFrom, _healthTo, eased)));

        float punch = 1f + 0.08f * Mathf.Sin(eased * Mathf.PI);
        if (currentAttack != null)
            currentAttack.transform.localScale = _attackScale * punch;
        if (currentHealth != null)
            currentHealth.transform.localScale = _healthScale * punch;
    }

    void RestCurrentScale()
    {
        if (currentAttack != null)
            currentAttack.transform.localScale = _attackScale;
        if (currentHealth != null)
            currentHealth.transform.localScale = _healthScale;
    }

    void Tick()
    {
        if (!_shown || group == null)
            return;

        _age += Time.deltaTime;
        float start = delay;
        float popEnd = start + pop;
        float countStart = popEnd + hold;
        float countEnd = countStart + countUp;
        float settleEnd = countEnd + settle;
        float fadeEnd = settleEnd + fade;

        if (_age < start)
        {
            HideImmediate();
            return;
        }

        float alpha;
        float scale;
        if (_age < popEnd)
        {
            float u = pop <= 0f ? 1f : (_age - start) / pop;
            float eased = 1f - (1f - u) * (1f - u);
            alpha = eased;
            scale = Mathf.Lerp(0.86f, 1f, eased);
            SetCurrent(_attackFrom, _healthFrom);
        }
        else if (_age < countStart)
        {
            alpha = 1f;
            scale = 1f;
            SetCurrent(_attackFrom, _healthFrom);
        }
        else if (_age < countEnd)
        {
            alpha = 1f;
            scale = 1f;
            float u = countUp <= 0f ? 1f : (_age - countStart) / countUp;
            RollCurrent(u);
        }
        else if (_age < settleEnd)
        {
            alpha = 1f;
            scale = 1f;
            SetCurrent(_attackTo, _healthTo);
            RestCurrentScale();
        }
        else if (_age < fadeEnd)
        {
            float u = (_age - settleEnd) / Mathf.Max(0.01f, fade);
            alpha = 1f - u;
            scale = 1f;
            SetCurrent(_attackTo, _healthTo);
        }
        else
        {
            _shown = false;
            HideImmediate();
            return;
        }

        group.alpha = alpha;
        group.blocksRaycasts = false;
        group.interactable = false;
        transform.localScale = _baseScale * scale;
    }

    void HideImmediate()
    {
        if (group == null)
            return;

        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        transform.localScale = _baseScale;
        RestCurrentScale();
    }
}
