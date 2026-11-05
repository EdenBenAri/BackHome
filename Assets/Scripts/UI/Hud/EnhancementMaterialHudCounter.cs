using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-row enhancement material chip authored on the HUD prefab, beside the coin counter.
/// Layout stays as saved on that prefab. This only updates the count.
/// </summary>
public class EnhancementMaterialHudCounter : MonoBehaviour
{
    const string ItemResource = "Items/EnhancementMaterial/EnhancementMaterial";
    const string BarName = "EnhancementMaterial";

    [SerializeField] RectTransform bar;
    [SerializeField] Image icon;
    [SerializeField] TMP_Text label;

    public static EnhancementMaterialHudCounter Active { get; private set; }

    /// <summary>Screen chip these icons fly toward when camp banks the pocket.</summary>
    public RectTransform Target => bar;

    PlayerInventory _inventory;
    ItemDefinition _item;
    float _receivePulse;

    public static bool Owns(ItemDefinition item)
    {
        if (item == null)
            return false;

        ItemDefinition tracked = Active != null ? Active._item : null;
        if (tracked == null)
            tracked = Resources.Load<ItemDefinition>(ItemResource);
        return tracked != null && tracked == item;
    }

    void OnEnable()
    {
        if (_item == null)
            _item = Resources.Load<ItemDefinition>(ItemResource);

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
            int amount = _inventory != null && _item != null ? _inventory.Count(_item) : 0;
            string text = FormatAmount(amount);
            if (label.text != text)
                label.text = text;
        }

        if (icon != null && _item != null && _item.Icon != null && icon.sprite != _item.Icon)
        {
            icon.sprite = _item.Icon;
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
                bar = canvas.transform.Find("CounterBars/" + BarName) as RectTransform;
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
