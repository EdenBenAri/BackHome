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

    PlayerInventory _inventory;
    ItemDefinition _coin;

    void OnEnable()
    {
        if (_coin == null)
            _coin = Resources.Load<ItemDefinition>(CoinResource);

        Resolve();
        Bind();
        Refresh();
    }

    void OnDisable()
    {
        Unbind();
    }

    void Update()
    {
        if (_inventory == null)
            Bind();
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
