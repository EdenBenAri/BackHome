using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// When the player steps into camp, empties <see cref="PlayerCargo"/> and flies each
/// icon from the middle of the screen into the coin counter. The bank updates as each icon lands.
/// </summary>
[DefaultExecutionOrder(1200)]
public class CargoCampDeposit : MonoBehaviour
{
    PlayerVitals _vitals;

    void LateUpdate()
    {
        PlayerCargo cargo = PlayerCargo.Instance;
        if (cargo == null || cargo.Used <= 0)
            return;

        if (!ResolvePlayer())
            return;

        if (_vitals == null || !_vitals.IsAlive || !_vitals.IsRecovering)
            return;

        Deposit(cargo);
    }

    bool ResolvePlayer()
    {
        if (_vitals != null)
            return true;

        _vitals = FindAnyObjectByType<PlayerVitals>();
        return _vitals != null;
    }

    void Deposit(PlayerCargo cargo)
    {
        PlayerCargo.Slot[] taken = cargo.TakeAll();
        for (int i = 0; i < taken.Length; i++)
        {
            if (!CargoDepositFlyer.TryLaunch(taken[i].item, taken[i].count, i))
                Bank(taken[i].item, taken[i].count);
        }
    }

    public static void Bank(ItemDefinition item, int amount)
    {
        if (item == null || amount <= 0)
            return;

        PlayerInventory.EnsureExists().Add(item, amount);
        if (CoinHudCounter.Active != null)
            CoinHudCounter.Active.PlayReceive();
    }
}

/// <summary>
/// One flying icon. Banks its share of the stack when it reaches the coin counter,
/// and also if the scene unloads mid-flight.
/// </summary>
public class CargoDepositFlyer : MonoBehaviour
{
    const float Duration = 0.48f;
    const float Arc = 72f;

    RectTransform _rect;
    RectTransform _canvas;
    RectTransform _target;
    Camera _eventCamera;
    ItemDefinition _item;
    int _amount;
    Vector2 _start;
    float _bend;
    float _delay;
    float _age;
    bool _committed;

    public static bool TryLaunch(ItemDefinition item, int amount, int stackIndex)
    {
        if (item == null || amount <= 0)
            return true;

        if (item.Icon == null || !TryResolveHud(out RectTransform canvas, out Camera eventCamera, out RectTransform target))
            return false;

        // Cargo sits in the middle of the view. Lift the icons from there, not from a world point.
        Vector2 screen = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas, screen, eventCamera, out Vector2 start))
            return false;

        start = ToAnchored(canvas, start);

        int visuals = Mathf.Clamp(amount, 1, 6);
        int baseShare = amount / visuals;
        int extra = amount % visuals;
        for (int i = 0; i < visuals; i++)
        {
            int share = baseShare + (i < extra ? 1 : 0);
            if (share <= 0)
                continue;

            var go = new GameObject("CargoFlyer", typeof(RectTransform));
            go.layer = canvas.gameObject.layer;
            go.transform.SetParent(canvas, false);
            var flyer = go.AddComponent<CargoDepositFlyer>();
            Vector2 jitter = new Vector2(Random.Range(-22f, 22f), Random.Range(-8f, 14f));
            float bend = (i % 2 == 0 ? 1f : -1f) * Random.Range(110f, 190f);
            float delay = stackIndex * 0.028f + i * 0.03f;
            flyer.Setup(item, share, start + jitter, bend, canvas, target, eventCamera, delay);
        }

        return true;
    }

    static bool TryResolveHud(out RectTransform canvas, out Camera eventCamera, out RectTransform target)
    {
        canvas = null;
        eventCamera = null;
        target = null;

        CoinHudCounter counter = CoinHudCounter.Active;
        if (counter == null || counter.Target == null)
            return false;

        Canvas hud = counter.Target.GetComponentInParent<Canvas>();
        if (hud == null)
            return false;

        canvas = hud.transform as RectTransform;
        target = counter.Target;
        eventCamera = hud.renderMode == RenderMode.ScreenSpaceOverlay ? null : hud.worldCamera;
        if (eventCamera == null && hud.renderMode != RenderMode.ScreenSpaceOverlay)
            eventCamera = Camera.main;
        return canvas != null;
    }

    void Setup(
        ItemDefinition item,
        int amount,
        Vector2 start,
        float bend,
        RectTransform canvas,
        RectTransform target,
        Camera eventCamera,
        float delay)
    {
        _item = item;
        _amount = amount;
        _start = start;
        _bend = bend;
        _canvas = canvas;
        _target = target;
        _eventCamera = eventCamera;
        _delay = delay;
        _rect = (RectTransform)transform;
        _rect.anchorMin = new Vector2(0.5f, 0.5f);
        _rect.anchorMax = new Vector2(0.5f, 0.5f);
        _rect.pivot = new Vector2(0.5f, 0.5f);
        _rect.sizeDelta = new Vector2(68f, 68f);
        _rect.anchoredPosition = start;
        _rect.localScale = Vector3.zero;

        Image image = gameObject.AddComponent<Image>();
        image.sprite = item.Icon;
        image.preserveAspect = true;
        image.raycastTarget = false;
        _rect.SetAsLastSibling();
    }

    void Update()
    {
        if (_committed || _rect == null)
            return;

        _age += Time.deltaTime;
        if (_age < _delay)
            return;

        float t = Mathf.Clamp01((_age - _delay) / Duration);
        Vector2 end = CurrentTarget();
        float travel = t * t * (3f - 2f * t);
        Vector2 position = Curve(_start, end, _bend, travel);
        _rect.anchoredPosition = position;

        float pop = t < 0.18f ? Mathf.Lerp(0.4f, 1.12f, t / 0.18f) : Mathf.Lerp(1.12f, 0.42f, (t - 0.18f) / 0.82f);
        _rect.localScale = Vector3.one * pop;

        if (t >= 1f)
            Finish();
    }

    static Vector2 Curve(Vector2 start, Vector2 end, float bend, float t)
    {
        Vector2 delta = end - start;
        Vector2 side = delta.sqrMagnitude > 1f
            ? new Vector2(-delta.y, delta.x).normalized
            : Vector2.right;
        Vector2 control = (start + end) * 0.5f + side * bend + Vector2.up * Arc;
        float k = 1f - t;
        return k * k * start + 2f * k * t * control + t * t * end;
    }

    Vector2 CurrentTarget()
    {
        if (_canvas == null || _target == null)
            return _start;

        Vector3 screen = RectTransformUtility.WorldToScreenPoint(_eventCamera, _target.position);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvas, screen, _eventCamera, out Vector2 local))
            return ToAnchored(_canvas, local);

        return ToAnchored(_canvas, _canvas.InverseTransformPoint(_target.position));
    }

    /// <summary>
    /// ScreenPointToLocalPointInRectangle is relative to the canvas pivot.
    /// Flyers are anchored to the center, so shift into that space.
    /// </summary>
    static Vector2 ToAnchored(RectTransform canvas, Vector2 pivotLocal)
    {
        Rect rect = canvas.rect;
        Vector2 anchor = new Vector2(
            (0.5f - canvas.pivot.x) * rect.width,
            (0.5f - canvas.pivot.y) * rect.height);
        return pivotLocal - anchor;
    }

    void Finish()
    {
        Commit();
        if (this != null)
            Destroy(gameObject);
    }

    void OnDestroy()
    {
        Commit();
    }

    void Commit()
    {
        if (_committed || !Application.isPlaying)
            return;

        _committed = true;
        CargoCampDeposit.Bank(_item, _amount);
    }
}
