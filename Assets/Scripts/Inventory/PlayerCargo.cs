using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pocket the player carries while out on the planet. Each unit takes one space.
/// Capacity starts small (<see cref="DefaultCapacity"/>); raise it later with <see cref="SetCapacity"/>.
/// Returning to camp moves everything into <see cref="PlayerInventory"/>.
/// </summary>
public class PlayerCargo : MonoBehaviour
{
    public const int DefaultCapacity = 20;

    [Serializable]
    public struct Slot
    {
        public ItemDefinition item;
        public int count;
    }

    static PlayerCargo _instance;

    [SerializeField, Min(1)] int capacity = DefaultCapacity;

    readonly List<Slot> _slots = new();

    public static PlayerCargo Instance => _instance;

    public IReadOnlyList<Slot> Slots => _slots;

    public int Capacity => Mathf.Max(1, capacity);

    public int Used
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _slots.Count; i++)
                total += _slots[i].count;
            return total;
        }
    }

    public int Free => Mathf.Max(0, Capacity - Used);

    public bool IsFull => Free <= 0;

    public event Action Changed;

    /// <summary>Fired when a pickup is refused because nothing fits.</summary>
    public event Action Rejected;

    public int Count(ItemDefinition item)
    {
        if (item == null)
            return 0;

        int total = 0;
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].item == item)
                total += _slots[i].count;
        }

        return total;
    }

    /// <summary>How many of this item can still fit, counting both free space and the stack cap.</summary>
    public int SpaceFor(ItemDefinition item)
    {
        if (item == null)
            return 0;

        int stackRoom = item.MaxStack - Count(item);
        if (stackRoom <= 0)
            return 0;

        return Mathf.Min(Free, stackRoom);
    }

    public static PlayerCargo EnsureExists()
    {
        if (_instance != null)
            return _instance;

        var go = new GameObject("PlayerCargo");
        return go.AddComponent<PlayerCargo>();
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        capacity = Mathf.Max(1, capacity);
        DontDestroyOnLoad(gameObject);
        if (GetComponent<CargoCampDeposit>() == null)
            gameObject.AddComponent<CargoCampDeposit>();
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    void OnValidate()
    {
        capacity = Mathf.Max(1, capacity);
    }

    public void SetCapacity(int value)
    {
        int next = Mathf.Max(1, value);
        if (next == capacity)
            return;

        capacity = next;
        Changed?.Invoke();
    }

    /// <summary>Adds as many as fit. Returns how many were stored.</summary>
    public int TryAdd(ItemDefinition item, int amount)
    {
        if (item == null || amount <= 0)
            return 0;

        int add = Mathf.Min(amount, SpaceFor(item));
        if (add <= 0)
            return 0;

        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].item != item)
                continue;

            Slot slot = _slots[i];
            slot.count += add;
            _slots[i] = slot;
            Changed?.Invoke();
            return add;
        }

        _slots.Add(new Slot { item = item, count = add });
        Changed?.Invoke();
        return add;
    }

    public void NotifyRejected()
    {
        Rejected?.Invoke();
    }

    /// <summary>Empties the pocket and returns what was inside.</summary>
    public Slot[] TakeAll()
    {
        if (_slots.Count == 0)
            return Array.Empty<Slot>();

        Slot[] copy = _slots.ToArray();
        _slots.Clear();
        Changed?.Invoke();
        return copy;
    }
}
