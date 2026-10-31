using System.Collections.Generic;
using UnityEngine;

/// <summary>Scene-owned pools are released with the scene, including their inactive text.</summary>
[AddComponentMenu("")]
public sealed class DamagePopupPool : MonoBehaviour
{
    static DamagePopupPool _instance;
    readonly Dictionary<DamagePopup, Bucket> _buckets = new();

    internal static Bucket GetBucket(DamagePopup prefab)
    {
        if (_instance == null)
            _instance = new GameObject("Damage text pool").AddComponent<DamagePopupPool>();
        if (!_instance._buckets.TryGetValue(prefab, out Bucket bucket))
        {
            bucket = new Bucket(prefab, _instance.transform);
            _instance._buckets.Add(prefab, bucket);
            bucket.Warm(32);
        }
        return bucket;
    }

    void OnDestroy() { if (_instance == this) _instance = null; }

    internal sealed class Bucket
    {
        readonly DamagePopup _prefab;
        readonly Transform _root;
        readonly Stack<DamagePopup> _free = new(64);
        internal Bucket(DamagePopup prefab, Transform root) { _prefab = prefab; _root = root; }
        DamagePopup New()
        {
            var popup = Instantiate(_prefab, _root);
            popup.SetPool(this);
            popup.gameObject.SetActive(false);
            return popup;
        }
        internal void Warm(int count) { while (_free.Count < count) _free.Push(New()); }
        internal DamagePopup Take()
        {
            while (_free.Count > 0)
            {
                DamagePopup popup = _free.Pop();
                if (popup != null) return popup;
            }
            return New();
        }
        internal void Return(DamagePopup popup)
        {
            popup.gameObject.SetActive(false);
            if (_free.Count < 128) _free.Push(popup);
            else Destroy(popup.gameObject);
        }
    }
}
