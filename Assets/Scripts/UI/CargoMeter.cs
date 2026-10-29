using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Follows the player and fills the cargo readout authored on the Cargo prefab.
/// The bar, label, colors, and item template are edited on that prefab.
/// </summary>
[DefaultExecutionOrder(1000)]
public class CargoMeter : MonoBehaviour
{
    const string CargoLayerName = "Cargo";

    [Header("View")]
    [SerializeField] RectTransform view;
    [SerializeField] RectTransform bar;
    [SerializeField] Image fill;
    [SerializeField] TMP_Text label;
    [SerializeField] RectTransform iconRow;
    [SerializeField] CargoChip chipTemplate;

    [Header("Colors")]
    [SerializeField] Color fillingColor = new Color(0.32f, 0.9f, 0.28f, 1f);
    [SerializeField] Color fullColor = new Color(0.98f, 0.52f, 0.1f, 1f);

    [Header("Placement")]
    [Tooltip("Lift above the player's feet, along the planet up axis.")]
    [SerializeField] float feetLift = 0.5f;
    [Tooltip("Shift toward the bottom of the screen so the bar sits under the character.")]
    [SerializeField] float screenDrop = 1.25f;

    [Header("Size")]
    [Tooltip("SceneBootstrap sets this from the planet HP bar scale.")]
    [SerializeField, Min(0.1f)] float worldScale = 1f;
    [Tooltip("World width of the reference pixel width. Matches the HP bar frame.")]
    [SerializeField, Min(0.1f)] float barWorldWidth = 2.8f;
    [Tooltip("Pixel width that maps onto Bar World Width.")]
    [SerializeField, Min(1f)] float referencePixels = 250f;
    [SerializeField, Min(1f)] float minBarWidth = 168f;
    [SerializeField, Min(0f)] float barTextPadding = 36f;
    [SerializeField, Min(0f)] float chipGap = 18f;

    readonly List<CargoChip> _chips = new();

    Canvas _canvas;
    PlayerVitals _vitals;
    PlayerCargo _cargo;
    Camera _overlayCam;
    Camera _overlayBase;
    float _shown;
    float _bump;
    float _authoredBarWidth;
    int _lastUsed;
    int _iconSignature;
    bool _snap = true;

    public void SetWorldScale(float scale)
    {
        worldScale = Mathf.Max(0.1f, scale);
    }

    /// <summary>World point a deposit icon should lift off from. Falls back to the bar.</summary>
    public Vector3 OriginForSlot(int index)
    {
        if (index >= 0 && index < _chips.Count)
        {
            CargoChip chip = _chips[index];
            if (chip != null && chip.gameObject.activeSelf)
                return chip.transform.position;
        }

        if (bar != null)
            return bar.position;

        return transform.position;
    }

    void Awake()
    {
        _vitals = GetComponentInParent<PlayerVitals>();
        _canvas = GetComponent<Canvas>();
        if (view == null)
            view = transform as RectTransform;

        if (chipTemplate != null)
            chipTemplate.gameObject.SetActive(false);
        if (bar != null)
            _authoredBarWidth = bar.sizeDelta.x;

        SetLayer(gameObject, CargoLayer());

        if (fill == null || label == null || iconRow == null || chipTemplate == null)
            Debug.LogWarning("CargoMeter is missing pieces on the Cargo prefab. Assign the bar, fill, label, icon row, and item template.", this);
    }

    void OnEnable()
    {
        _cargo = PlayerCargo.EnsureExists();
        if (_cargo != null)
            _cargo.Rejected += OnRejected;
        _snap = true;
    }

    void OnDisable()
    {
        if (_cargo != null)
            _cargo.Rejected -= OnRejected;
    }

    void OnDestroy()
    {
        Camera overlay = _overlayCam;
        ReleaseOverlay();
        if (overlay != null)
            Destroy(overlay.gameObject);
        _overlayCam = null;
    }

    void OnRejected()
    {
        _bump = 1f;
    }

    void LateUpdate()
    {
        if (view == null)
            return;

        if (_cargo == null)
            _cargo = PlayerCargo.EnsureExists();

        FollowCamera();
        Refresh();
    }

    void FollowCamera()
    {
        Camera cam = Camera.main;
        bool onPlanet = SphericalPlanet.Instance != null;
        bool alive = _vitals == null || _vitals.IsAlive;
        bool show = onPlanet && alive && cam != null;
        if (_canvas != null && _canvas.enabled != show)
            _canvas.enabled = show;
        if (!show || cam == null)
        {
            if (_overlayCam != null)
                _overlayCam.enabled = false;
            return;
        }

        EnsureOverlay(cam);

        Transform host = view.parent != null ? view.parent : view;
        Vector3 up = host.up.sqrMagnitude > 0.0001f ? host.up.normalized : Vector3.up;
        Vector3 screenDown = Vector3.ProjectOnPlane(-cam.transform.up, up);
        if (screenDown.sqrMagnitude < 0.001f)
            screenDown = Vector3.ProjectOnPlane(-cam.transform.forward, up);
        if (screenDown.sqrMagnitude < 0.001f)
            screenDown = Vector3.forward;
        screenDown.Normalize();

        Vector3 anchor = host.position + up * feetLift + screenDown * screenDrop;
        view.SetPositionAndRotation(anchor, cam.transform.rotation);

        float units = (barWorldWidth * worldScale) / referencePixels;
        float bump = 1f;
        if (_bump > 0f)
        {
            _bump = Mathf.MoveTowards(_bump, 0f, Time.deltaTime / 0.16f);
            bump += 0.07f * Mathf.Sin(_bump * Mathf.PI);
        }

        Vector3 lossy = view.parent != null ? view.parent.lossyScale : Vector3.one;
        view.localScale = new Vector3(
            SafeInv(lossy.x) * units * bump,
            SafeInv(lossy.y) * units * bump,
            SafeInv(lossy.z) * units * bump);
    }

    void Refresh()
    {
        if (_cargo == null)
            return;

        int used = _cargo.Used;
        int cap = _cargo.Capacity;
        if (used > _lastUsed)
            _bump = 1f;
        _lastUsed = used;
        RefreshMeter(used, cap);

        if (label != null)
        {
            string text = _cargo.IsFull
                ? "MAX"
                : "CARGO [" + used + "/" + FormatAmount(cap) + "]";
            if (label.text != text)
                label.text = text;

            FitPill(text);
        }

        RefreshIcons();
    }

    void FitPill(string text)
    {
        if (bar == null || label == null)
            return;

        float textWidth = label.font != null ? label.GetPreferredValues(text).x : 90f;
        float width = Mathf.Max(_authoredBarWidth, Mathf.Max(minBarWidth, textWidth + barTextPadding));
        if (!Mathf.Approximately(bar.sizeDelta.x, width))
            bar.sizeDelta = new Vector2(width, bar.sizeDelta.y);
    }

    void RefreshMeter(int used, int cap)
    {
        if (fill == null)
            return;

        float target = cap > 0 ? Mathf.Clamp01((float)used / cap) : 0f;
        if (_snap)
        {
            _shown = target;
            _snap = false;
        }
        else
        {
            _shown = Mathf.MoveTowards(_shown, target, Time.deltaTime * 2.6f);
        }

        bool full = cap > 0 && used >= cap;
        Color color = full ? fullColor : fillingColor;
        if (fill.color != color)
            fill.color = color;

        bool visible = _shown > 0.012f;
        if (fill.enabled != visible)
            fill.enabled = visible;

        // Width follows the anchors so a sliced Layer Lab fill keeps its rounded ends.
        RectTransform fillRect = fill.rectTransform;
        Vector2 anchorMax = fillRect.anchorMax;
        if (!Mathf.Approximately(anchorMax.x, _shown))
        {
            anchorMax.x = _shown;
            fillRect.anchorMax = anchorMax;
        }
    }

    void RefreshIcons()
    {
        if (iconRow == null || chipTemplate == null)
            return;

        IReadOnlyList<PlayerCargo.Slot> slots = _cargo.Slots;
        int signature = 17;
        for (int i = 0; i < slots.Count; i++)
        {
            PlayerCargo.Slot slot = slots[i];
            int itemId = slot.item != null ? slot.item.GetEntityId().GetHashCode() : 0;
            signature = signature * 31 + itemId;
            signature = signature * 31 + slot.count;
        }

        if (signature == _iconSignature && _chips.Count >= slots.Count)
            return;

        _iconSignature = signature;

        while (_chips.Count < slots.Count)
            _chips.Add(SpawnChip());

        int shown = 0;
        for (int i = 0; i < _chips.Count; i++)
        {
            CargoChip chip = _chips[i];
            bool active = i < slots.Count && slots[i].item != null && slots[i].count > 0;
            if (chip.gameObject.activeSelf != active)
                chip.gameObject.SetActive(active);
            if (!active)
                continue;

            PlayerCargo.Slot slot = slots[i];
            if (chip.Icon != null)
            {
                Sprite sprite = slot.item.Icon;
                chip.Icon.sprite = sprite;
                chip.Icon.enabled = sprite != null;
            }

            if (chip.Count != null)
            {
                string count = FormatAmount(slot.count);
                if (chip.Count.text != count)
                    chip.Count.text = count;
            }

            shown++;
        }

        LayoutChips(shown);
    }

    CargoChip SpawnChip()
    {
        CargoChip chip = Instantiate(chipTemplate, iconRow);
        chip.gameObject.SetActive(true);
        chip.name = "Item";
        SetLayer(chip.gameObject, CargoLayer());
        return chip;
    }

    void LayoutChips(int shown)
    {
        float total = 0f;
        int placed = 0;
        var widths = new float[_chips.Count];

        for (int i = 0; i < _chips.Count && placed < shown; i++)
        {
            CargoChip chip = _chips[i];
            if (!chip.gameObject.activeSelf)
                continue;

            float chipWidth = ChipWidth(chip);
            widths[i] = chipWidth;
            if (chip.Rect != null)
                chip.Rect.sizeDelta = new Vector2(chipWidth, chip.Rect.sizeDelta.y);
            total += chipWidth;
            placed++;
            if (placed < shown)
                total += chipGap;
        }

        float cursor = -total * 0.5f;
        placed = 0;
        for (int i = 0; i < _chips.Count && placed < shown; i++)
        {
            CargoChip chip = _chips[i];
            if (!chip.gameObject.activeSelf || chip.Rect == null)
                continue;

            float chipWidth = widths[i];
            chip.Rect.anchoredPosition = new Vector2(cursor + chipWidth * 0.5f, chip.Rect.anchoredPosition.y);
            cursor += chipWidth + chipGap;
            placed++;
        }
    }

    float ChipWidth(CargoChip chip)
    {
        float iconSize = 46f;
        if (chip.Icon != null)
            iconSize = Mathf.Max(1f, chip.Icon.rectTransform.sizeDelta.x);

        float lead = iconSize + 4f;
        if (chip.Count != null)
            lead = chip.Count.rectTransform.anchoredPosition.x;

        float textWidth = 28f;
        if (chip.Count != null && chip.Count.font != null)
            textWidth = Mathf.Max(18f, chip.Count.GetPreferredValues(chip.Count.text).x);

        return Mathf.Max(iconSize, lead + textWidth);
    }

    /// <summary>
    /// Draws the readout after the planet, with a fresh depth buffer, so the ground cannot hide it.
    /// </summary>
    void EnsureOverlay(Camera main)
    {
        int layer = CargoLayer();
        main.cullingMask &= ~(1 << layer);

        if (_overlayCam == null)
        {
            var go = new GameObject("CargoOverlayCamera");
            _overlayCam = go.AddComponent<Camera>();
            _overlayCam.enabled = false;
        }

        _overlayCam.fieldOfView = main.fieldOfView;
        _overlayCam.nearClipPlane = main.nearClipPlane;
        _overlayCam.farClipPlane = main.farClipPlane;
        _overlayCam.enabled = true;
        if (_overlayBase == main)
            return;

        ReleaseOverlay();
        if (_overlayCam == null)
        {
            var go = new GameObject("CargoOverlayCamera");
            _overlayCam = go.AddComponent<Camera>();
        }

        _overlayBase = main;
        _overlayCam.transform.SetParent(main.transform, false);
        _overlayCam.transform.localPosition = Vector3.zero;
        _overlayCam.transform.localRotation = Quaternion.identity;
        _overlayCam.transform.localScale = Vector3.one;
        _overlayCam.CopyFrom(main);
        _overlayCam.cullingMask = 1 << layer;
        _overlayCam.clearFlags = CameraClearFlags.Depth;
        _overlayCam.depth = main.depth + 1;
        _overlayCam.allowHDR = main.allowHDR;
        _overlayCam.allowMSAA = main.allowMSAA;

        UniversalAdditionalCameraData overlayData = _overlayCam.GetUniversalAdditionalCameraData();
        overlayData.renderType = CameraRenderType.Overlay;
        overlayData.renderShadows = false;

        UniversalAdditionalCameraData mainData = main.GetUniversalAdditionalCameraData();
        if (mainData != null && !mainData.cameraStack.Contains(_overlayCam))
            mainData.cameraStack.Add(_overlayCam);

        main.cullingMask &= ~(1 << layer);
        _overlayCam.enabled = true;
    }

    void ReleaseOverlay()
    {
        if (_overlayBase != null && _overlayCam != null)
        {
            UniversalAdditionalCameraData mainData = _overlayBase.GetUniversalAdditionalCameraData();
            if (mainData != null)
                mainData.cameraStack.Remove(_overlayCam);
        }

        _overlayBase = null;
    }

    static int CargoLayer()
    {
        int layer = LayerMask.NameToLayer(CargoLayerName);
        return layer >= 0 ? layer : 7;
    }

    static void SetLayer(GameObject go, int layer)
    {
        if (go == null)
            return;

        go.layer = layer;
        Transform root = go.transform;
        for (int i = 0; i < root.childCount; i++)
            SetLayer(root.GetChild(i).gameObject, layer);
    }

    static float SafeInv(float value)
    {
        return Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;
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
