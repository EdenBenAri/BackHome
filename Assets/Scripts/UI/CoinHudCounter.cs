using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-row coin chip, XP Hero style: coin icon and the count on a dark pill,
/// sitting just left of the XP bar. Currency stays out of the bag.
/// </summary>
public class CoinHudCounter : MonoBehaviour
{
    const string BarName = "CoinCounter";
    const string CoinResource = "Items/Coin/Coin";

    PlayerInventory _inventory;
    ItemDefinition _coin;
    RectTransform _bar;
    TMP_Text _label;
    Image _icon;
    Canvas _canvas;
    int _screenW = -1;
    int _screenH = -1;

    void OnEnable()
    {
        if (_coin == null)
            _coin = Resources.Load<ItemDefinition>(CoinResource);

        EnsureBar();
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

        if (_bar != null && (_screenW != Screen.width || _screenH != Screen.height))
            PlaceInCorner();
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
        if (_label == null)
            return;

        int amount = _inventory != null && _coin != null ? _inventory.Count(_coin) : 0;
        string text = FormatAmount(amount);
        if (_label.text != text)
            _label.text = text;

        if (_icon != null && _coin != null && _coin.Icon != null && _icon.sprite != _coin.Icon)
        {
            _icon.sprite = _coin.Icon;
            _icon.enabled = true;
        }
    }

    void EnsureBar()
    {
        if (_bar != null)
            return;

        _canvas = GetComponentInChildren<Canvas>(true);
        if (_canvas == null)
            return;

        Transform existing = _canvas.transform.Find(BarName);
        if (existing != null)
        {
            _bar = existing as RectTransform;
            _label = existing.Find("Label")?.GetComponent<TMP_Text>();
            _icon = existing.Find("Icon")?.GetComponent<Image>();
            PlaceInCorner();
            return;
        }

        var root = new GameObject(BarName, typeof(RectTransform));
        root.layer = 5;
        root.transform.SetParent(_canvas.transform, false);
        _bar = root.GetComponent<RectTransform>();

        Image track = HudChrome.CreateImage(root.transform, "Track", HudChrome.Pill(), new Color(0.07f, 0.07f, 0.09f, 0.94f));
        HudChrome.Stretch(track.rectTransform, 0f);

        _icon = CreateIcon(root.transform, _coin != null ? _coin.Icon : null);
        _label = CreateLabel(root.transform, HudChrome.Font(transform));
        PlaceInCorner();
        HudChrome.KeepUnderDeathOverlay(_bar, _canvas);
    }

    void PlaceInCorner()
    {
        if (_bar == null)
            return;

        _screenW = Screen.width;
        _screenH = Screen.height;
        _bar.anchorMin = new Vector2(1f, 1f);
        _bar.anchorMax = new Vector2(1f, 1f);
        _bar.pivot = new Vector2(1f, 1f);
        _bar.sizeDelta = new Vector2(200f, 52f);
        _bar.localScale = Vector3.one;
        _bar.localRotation = Quaternion.identity;

        float scale = _canvas != null && _canvas.scaleFactor > 0.01f ? _canvas.scaleFactor : 1f;
        Rect safe = Screen.safeArea;
        float right = (Screen.width - safe.xMax) / scale;
        float top = (Screen.height - safe.yMax) / scale;
        // Sit just left of the XP bar (280 wide, 12 gap, 28 margin).
        _bar.anchoredPosition = new Vector2(-(28f + 280f + 12f + right), -(28f + top));
    }

    static Image CreateIcon(Transform parent, Sprite sprite)
    {
        Image image = HudChrome.CreateImage(parent, "Icon", sprite, Color.white);
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.enabled = sprite != null;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(6f, 0f);
        rect.sizeDelta = new Vector2(42f, 42f);
        return image;
    }

    static TMP_Text CreateLabel(Transform parent, TMP_FontAsset font)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        if (font != null)
            text.font = font;
        text.text = "0";
        text.fontSize = 28f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.outlineWidth = 0.18f;
        text.outlineColor = new Color32(20, 16, 28, 255);
        RectTransform rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(54f, 0f);
        rect.offsetMax = new Vector2(-12f, 0f);
        return text;
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
