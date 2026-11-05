using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Casual equipment + resource HUD. The NavigationBar Equipment button opens EquipmentPanel.
/// </summary>
public class InventoryUI : MonoBehaviour
{
    const int InventoryGridCapacity = 10;
    const string WeaponSlotResource = "HUD/Equipment/Weapons/WeaponSlot";

    [SerializeField] GameObject inventoryPanelPrefab;
    [SerializeField] GameObject inventoryPanel;
    [SerializeField] GameObject contentPanel;
    [SerializeField] GameObject bottomBar;
    [SerializeField] Button toggleButton;
    [SerializeField] Button closeButton;
    [SerializeField] TMP_Text powerText;
    [SerializeField] TMP_Text heroNameText;
    [SerializeField] EquipmentSlotView[] equipmentSlots;
    [SerializeField] Transform inventoryGrid;
    [SerializeField] InventoryGridSlotView inventorySlotPrefab;
    [SerializeField] Sprite fallbackResourceIcon;
    [SerializeField] bool startOpen;

    PlayerInventory _inventory;
    PlayerVitals _vitals;
    EquipmentHeroPreview _heroPreview;
    Transform _navigationBar;
    bool _open;
    bool _boundInventory;
    bool _boundVitals;

    void Start()
    {
        PlayerInventory.EnsureExists();
        _inventory = PlayerInventory.Instance;
        EnsurePanel();
        BindInventory();
        TryBindPlayer();
        SetOpen(startOpen);
        Refresh();
    }

    void Update()
    {
        if (_vitals == null)
        {
            TryBindPlayer();
            if (_vitals != null && _open)
                Refresh();
        }
    }

    void OnDestroy()
    {
        UnbindInventory();
        UnbindPlayer();
    }

    void EnsurePanel()
    {
        if (inventoryPanel == null)
        {
            Transform existing = FindExistingPanel();
            if (existing != null)
                inventoryPanel = existing.gameObject;
        }

        if (inventoryPanel == null)
        {
            if (inventoryPanelPrefab == null)
            {
                Debug.LogWarning($"{nameof(InventoryUI)}: assign InventoryPanel prefab.", this);
                return;
            }

            Canvas canvas = GetComponentInChildren<Canvas>(true);
            Transform parent = canvas != null ? canvas.transform : transform;
            inventoryPanel = Instantiate(inventoryPanelPrefab, parent, false);
            inventoryPanel.name = "InventoryPanel";
        }

        WireReferences(inventoryPanel.transform);
    }

    Transform FindExistingPanel()
    {
        Transform direct = transform.Find("EquipmentPanel");
        if (direct == null)
            direct = transform.Find("InventoryPanel");
        if (direct != null)
            return direct;

        Canvas[] canvases = GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            Transform panel = canvases[i].transform.Find("EquipmentPanel");
            if (panel == null)
                panel = canvases[i].transform.Find("InventoryPanel");
            if (panel != null)
                return panel;
        }

        return null;
    }

    void WireReferences(Transform root)
    {
        if (bottomBar == null)
        {
            Transform bar = root.Find("BottomBar");
            if (bar == null)
            {
                Canvas canvas = GetComponentInChildren<Canvas>(true);
                if (canvas != null)
                    bar = canvas.transform.Find("BottomBar");
            }
            if (bar != null)
                bottomBar = bar.gameObject;
        }

        if (toggleButton == null)
        {
            Transform toggle = bottomBar != null ? bottomBar.transform.Find("ToggleButton") : null;
            if (toggle == null)
                toggle = root.Find("BottomBar/ToggleButton");
            if (toggle == null)
                toggle = root.Find("ToggleButton");
            if (toggle != null)
                toggleButton = toggle.GetComponent<Button>();
        }

        if (toggleButton == null)
            toggleButton = FindEquipmentButton();

        if (contentPanel == null)
        {
            Transform panel = root.Find("ContentPanel");
            if (panel != null)
                contentPanel = panel.gameObject;
        }

        Transform window = root.Find("ContentPanel/Window");
        if (closeButton == null)
        {
            Transform close = window != null ? window.Find("Header/CloseButton") : null;
            if (close != null)
                closeButton = close.GetComponent<Button>();
        }

        if (powerText == null)
        {
            Transform power = window != null ? window.Find("EquipBody/Hero/Power") : null;
            if (power != null)
                powerText = power.GetComponent<TMP_Text>();
        }

        if (heroNameText == null)
        {
            Transform name = window != null ? window.Find("EquipBody/Hero/Name") : null;
            if (name != null)
                heroNameText = name.GetComponent<TMP_Text>();
        }

        if (_heroPreview == null && window != null)
        {
            Transform hero = window.Find("EquipBody/Hero");
            if (hero != null)
            {
                _heroPreview = hero.GetComponent<EquipmentHeroPreview>();
                if (_heroPreview == null)
                    _heroPreview = hero.gameObject.AddComponent<EquipmentHeroPreview>();
            }
        }

        if (equipmentSlots == null || equipmentSlots.Length == 0)
        {
            if (window != null)
                equipmentSlots = window.GetComponentsInChildren<EquipmentSlotView>(true);
        }

        if (inventoryGrid == null && window != null)
        {
            Transform grid = window.Find("InventoryBody/Grid");
            if (grid != null)
                inventoryGrid = grid;
        }

        if (inventorySlotPrefab == null)
            inventorySlotPrefab = Resources.Load<InventoryGridSlotView>(WeaponSlotResource);

        if (inventorySlotPrefab == null && inventoryGrid != null)
        {
            Transform placed = inventoryGrid.Find("WeaponSlot");
            if (placed != null)
                inventorySlotPrefab = placed.GetComponent<InventoryGridSlotView>();
        }

        if (SlotPrefabLivesInGrid())
            inventorySlotPrefab.gameObject.SetActive(false);

        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveListener(ToggleOpen);
            toggleButton.onClick.AddListener(ToggleOpen);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Close);
            closeButton.onClick.AddListener(Close);
        }
    }

    Button FindEquipmentButton()
    {
        Canvas[] canvases = GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            Transform equipment = canvases[i].transform.Find("NavigationBar/Equipment");
            if (equipment == null)
                continue;

            _navigationBar = equipment.parent;
            Button button = equipment.GetComponent<Button>();
            if (button != null)
                return button;
        }

        return null;
    }

    void BindInventory()
    {
        if (_inventory == null || _boundInventory)
            return;

        _inventory.Changed += Refresh;
        _boundInventory = true;
    }

    void UnbindInventory()
    {
        if (_inventory == null || !_boundInventory)
            return;

        _inventory.Changed -= Refresh;
        _boundInventory = false;
    }

    void TryBindPlayer()
    {
        if (_vitals != null)
            return;

        GameObject playerGo = GameObject.FindGameObjectWithTag("Player");
        if (playerGo == null)
            return;

        _vitals = playerGo.GetComponent<PlayerVitals>();
        if (_vitals == null)
            _vitals = playerGo.GetComponentInChildren<PlayerVitals>();
        if (_vitals == null)
            return;

        _vitals.VitalsChanged += Refresh;
        _vitals.LoadoutChanged += Refresh;
        _boundVitals = true;
    }

    void UnbindPlayer()
    {
        if (_vitals == null || !_boundVitals)
            return;

        _vitals.VitalsChanged -= Refresh;
        _vitals.LoadoutChanged -= Refresh;
        _boundVitals = false;
        _vitals = null;
    }

    public void ToggleOpen()
    {
        SetOpen(!_open);
    }

    public void Close()
    {
        SetOpen(false);
    }

    void SetOpen(bool open)
    {
        _open = open;
        if (!open)
            _heroPreview?.SetVisible(false);

        if (inventoryPanel != null)
            inventoryPanel.SetActive(open);
        if (contentPanel != null)
            contentPanel.SetActive(open);
        if (bottomBar != null)
            bottomBar.SetActive(true);

        if (open)
        {
            if (inventoryPanel != null)
                inventoryPanel.transform.SetAsLastSibling();
            Refresh();
            _heroPreview?.SetVisible(true);
        }

        if (bottomBar != null)
            bottomBar.transform.SetAsLastSibling();
        if (_navigationBar != null)
            _navigationBar.gameObject.SetActive(!open);
    }

    void Refresh()
    {
        if (!_open && contentPanel != null && !contentPanel.activeSelf)
            return;

        RefreshHero();
        RefreshEquipment();
        RefreshInventoryGrid();
    }

    void RefreshHero()
    {
        if (heroNameText != null)
            heroNameText.text = _vitals != null ? _vitals.DisplayName : "Hero";
        if (powerText != null)
            powerText.text = FormatPower(ComputePower());
    }

    void RefreshEquipment()
    {
        if (equipmentSlots == null)
            return;

        for (int i = 0; i < equipmentSlots.Length; i++)
        {
            EquipmentSlotView slot = equipmentSlots[i];
            if (slot == null)
                continue;

            switch (slot.Kind)
            {
                case EquipmentSlotKind.OxygenTank:
                    BindOxygen(slot);
                    break;
                case EquipmentSlotKind.Health:
                    BindHealth(slot);
                    break;
                case EquipmentSlotKind.MovementSpeed:
                    BindMoveSpeed(slot);
                    break;
                case EquipmentSlotKind.Weapon:
                    BindWeapon(slot);
                    break;
            }
        }
    }

    void BindOxygen(EquipmentSlotView slot)
    {
        float tank = _vitals != null ? _vitals.MaxOxygen : 0f;
        slot.Bind("O2 TANK", FormatStat(tank), null, new Color(0.35f, 0.85f, 1f, 1f));
    }

    void BindHealth(EquipmentSlotView slot)
    {
        float hp = _vitals != null ? _vitals.MaxHealth : 0f;
        slot.Bind("SUIT", FormatStat(hp), null, new Color(1f, 0.42f, 0.48f, 1f));
    }

    void BindMoveSpeed(EquipmentSlotView slot)
    {
        float speed = ReadMoveSpeed();
        slot.Bind("BOOTS", FormatStat(speed), null, new Color(1f, 0.82f, 0.28f, 1f));
    }

    void BindWeapon(EquipmentSlotView slot)
    {
        WeaponDefinition weapon = ReadWeapon(slot.WeaponIndex);
        Color tint = new Color(0.72f, 0.45f, 1f, 1f);
        if (weapon == null)
        {
            slot.BindEmpty("WEAPON", tint);
            return;
        }

        slot.Bind(weapon.DisplayName, FormatStat(weapon.Combat.AttackDamage), weapon.Icon, tint);
    }

    void RefreshInventoryGrid()
    {
        if (inventoryGrid == null || inventorySlotPrefab == null)
            return;

        if (_inventory == null)
            _inventory = PlayerInventory.Instance;

        int filled = CountBagSlots();
        int shown = Mathf.Max(InventoryGridCapacity, filled);

        while (CountSpawnedSlots() < shown)
        {
            InventoryGridSlotView extra = Instantiate(inventorySlotPrefab, inventoryGrid);
            extra.gameObject.SetActive(true);
            extra.name = "WeaponSlot";
        }

        int slotIndex = 0;
        for (int i = 0; i < inventoryGrid.childCount; i++)
        {
            InventoryGridSlotView view = inventoryGrid.GetChild(i).GetComponent<InventoryGridSlotView>();
            if (view == null || IsSlotTemplate(view))
                continue;

            bool active = slotIndex < shown;
            view.gameObject.SetActive(active);
            if (!active)
            {
                slotIndex++;
                continue;
            }

            if (TryBagSlot(slotIndex, out PlayerInventory.Slot packed))
                view.Bind(packed.item, packed.count, fallbackResourceIcon);
            else
                view.Bind(null, 0, fallbackResourceIcon);

            slotIndex++;
        }
    }

    int CountBagSlots()
    {
        if (_inventory == null)
            return 0;

        int count = 0;
        var slots = _inventory.Slots;
        for (int i = 0; i < slots.Count; i++)
        {
            if (!ShowsInEquipmentBag(slots[i].item))
                continue;
            count++;
        }

        return count;
    }

    bool TryBagSlot(int visibleIndex, out PlayerInventory.Slot slot)
    {
        slot = default;
        if (_inventory == null || visibleIndex < 0)
            return false;

        int seen = 0;
        var slots = _inventory.Slots;
        for (int i = 0; i < slots.Count; i++)
        {
            if (!ShowsInEquipmentBag(slots[i].item))
                continue;

            if (seen == visibleIndex)
            {
                slot = slots[i];
                return true;
            }

            seen++;
        }

        return false;
    }

    bool SlotPrefabLivesInGrid()
    {
        return inventorySlotPrefab != null
            && inventoryGrid != null
            && inventorySlotPrefab.transform.IsChildOf(inventoryGrid);
    }

    bool IsSlotTemplate(InventoryGridSlotView view)
    {
        return SlotPrefabLivesInGrid() && view == inventorySlotPrefab;
    }

    static bool ShowsInEquipmentBag(ItemDefinition item)
    {
        if (item == null)
            return false;
        if (item.Category == ItemCategory.Currency)
            return false;
        return !EnhancementMaterialHudCounter.Owns(item);
    }

    int CountSpawnedSlots()
    {
        int n = 0;
        for (int i = 0; i < inventoryGrid.childCount; i++)
        {
            InventoryGridSlotView view = inventoryGrid.GetChild(i).GetComponent<InventoryGridSlotView>();
            if (view != null && !IsSlotTemplate(view))
                n++;
        }

        return n;
    }

    WeaponDefinition ReadWeapon(int index)
    {
        if (_vitals == null || index < 0)
            return null;

        var weapons = _vitals.Weapons;
        int seen = 0;
        for (int i = 0; i < weapons.Count; i++)
        {
            if (weapons[i] == null)
                continue;
            if (seen == index)
                return weapons[i];
            seen++;
        }

        return null;
    }

    float ReadMoveSpeed()
    {
        if (_vitals == null)
            return 0f;

        PlanetWalker walker = _vitals.GetComponent<PlanetWalker>();
        if (walker != null)
            return walker.WalkSpeed;

        TouchController touch = _vitals.GetComponent<TouchController>();
        return touch != null ? touch.WalkSpeed : 0f;
    }

    float ComputePower()
    {
        if (_vitals == null)
            return 0f;

        float power = _vitals.MaxHealth + _vitals.MaxOxygen * 3f + ReadMoveSpeed() * 20f;
        var weapons = _vitals.Weapons;
        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponDefinition weapon = weapons[i];
            if (weapon == null)
                continue;
            CombatStats combat = _vitals.CombatFor(weapon);
            power += combat.AttackDamage * Mathf.Max(0.5f, combat.AttackSpeed) * 8f;
        }

        return power;
    }

    static string FormatStat(float value)
    {
        if (value >= 1000f)
            return (value / 1000f).ToString("0.#") + "K";
        if (Mathf.Abs(value - Mathf.Round(value)) < 0.05f)
            return Mathf.RoundToInt(value).ToString();
        return value.ToString("0.#");
    }

    static string FormatPower(float value)
    {
        if (value >= 1000000f)
            return (value / 1000000f).ToString("0.0") + "M";
        if (value >= 1000f)
            return (value / 1000f).ToString("0.0") + "K";
        return Mathf.RoundToInt(value).ToString();
    }
}
