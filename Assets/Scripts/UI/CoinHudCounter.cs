using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-row coin chip authored on the HUD prefab, just left of the XP bar.
/// Layout stays as saved on that prefab. This only updates the count.
/// </summary>
public class CoinHudCounter : MonoBehaviour
{
    const string BarName = "CoinCounter";
    const string CoinResource = "Items/Coin/Coin";

    [SerializeField] RectTransform bar;
    [SerializeField] Image icon;
    [SerializeField] TMP_Text label;

    public static CoinHudCounter Active { get; private set; }

    /// <summary>Screen chip cargo icons fly toward when camp banks the pocket.</summary>
    public RectTransform Target => bar;

    PlayerInventory _inventory;
    ItemDefinition _coin;
    float _receivePulse;

    void OnEnable()
    {
        if (_coin == null)
            _coin = Resources.Load<ItemDefinition>(CoinResource);

        Resolve();
        Active = this;
        Bind();
        Refresh();
    }

    void OnDisable()
    {
        if (Active == this)
            Active = null;
        Unbind();
        if (bar != null)
            bar.localScale = Vector3.one;
    }

    public void PlayReceive()
    {
        _receivePulse = 1f;
    }

    void Update()
    {
        if (_inventory == null)
            Bind();
        TickReceive();
    }

    void TickReceive()
    {
        if (bar == null || _receivePulse <= 0f)
            return;

        _receivePulse = Mathf.MoveTowards(_receivePulse, 0f, Time.deltaTime / 0.18f);
        if (_receivePulse <= 0f)
        {
            bar.localScale = Vector3.one;
            return;
        }

        float kick = Mathf.Sin(_receivePulse * Mathf.PI);
        bar.localScale = Vector3.one * (1f + 0.14f * kick);
    }

    void Bind()
    {
        Unbind();
        _inventory = PlayerInventory.EnsureExists();
        if (_inventory == null)
            return;

        _inventory.Changed += Refresh;
        Refresh();
    }

    void Unbind()
    {
        if (_inventory == null)
            return;

        _inventory.Changed -= Refresh;
        _inventory = null;
    }

    void Refresh()
    {
        if (label != null)
        {
            int amount = _inventory != null && _coin != null ? _inventory.Count(_coin) : 0;
            string text = FormatAmount(amount);
            if (label.text != text)
                label.text = text;
        }

        if (icon != null && _coin != null && _coin.Icon != null && icon.sprite != _coin.Icon)
        {
            icon.sprite = _coin.Icon;
            icon.enabled = true;
        }
    }

    void Resolve()
    {
        if (bar == null)
        {
            Canvas canvas = GetComponentInChildren<Canvas>(true);
            if (canvas != null)
            {
                bar = canvas.transform.Find("XpBar/" + BarName) as RectTransform;
                if (bar == null)
                    bar = canvas.transform.Find(BarName) as RectTransform;
            }
        }

        if (bar == null)
            return;

        if (icon == null)
            icon = bar.Find("Icon")?.GetComponent<Image>();
        if (label == null)
            label = bar.Find("Label")?.GetComponent<TMP_Text>();
    }

    static string FormatAmount(int amount)
    {
        amount = Mathf.Max(0, amount);
        if (amount < 10000)
            return amount.ToString();
        if (amount < 1000000)
            return (amount / 1000f).ToString("0.#") + "K";
        return (amount / 1000000f).ToString("0.##") + "M";
    }
}
