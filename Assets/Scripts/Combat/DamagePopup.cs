using TMPro;
using UnityEngine;

/// <summary>Floating damage/heal text rendered with TextMesh Pro.</summary>
public class DamagePopup : MonoBehaviour
{
    [SerializeField] TextMeshPro label;
    [SerializeField] float lifetime = 0.85f;
    [SerializeField] float floatSpeed = 1.6f;
    [SerializeField] float fadeDelay = 0.25f;
    [SerializeField] float startScale = 0.55f;
    [SerializeField] float peakScale = 0.85f;
    [SerializeField] float endScale = 0.45f;
    [SerializeField] Color damageColor = new Color(1f, 0.35f, 0.25f, 1f);
    [SerializeField] Color healColor = new Color(0.35f, 0.95f, 0.45f, 1f);
    [SerializeField] Vector3 spawnOffset = new Vector3(0f, 0.35f, 0f);

    Camera _camera;
    DamagePopupPool.Bucket _pool;
    Vector3 _moveDir;
    float _age;
    Color _baseColor;
    byte _alpha;
    bool _setup;

    public static void Prewarm(DamagePopup prefab)
    {
        if (prefab != null && Application.isPlaying)
            DamagePopupPool.GetBucket(prefab);
    }

    public static DamagePopup Create(DamagePopup prefab, Vector3 worldPosition,
        float amount, Vector3 up, bool isHeal = false)
    {
        if (prefab == null) return null;
        DamagePopup popup = DamagePopupPool.GetBucket(prefab).Take();
        popup.transform.SetPositionAndRotation(worldPosition, Quaternion.identity);
        popup.gameObject.SetActive(true);
        popup.Setup(amount, up, isHeal);
        return popup;
    }

    void Awake()
    {
        if (label == null) label = GetComponentInChildren<TextMeshPro>(true);
        if (label == null) return;
        if (label.font == null) label.font = TMP_Settings.defaultFontAsset;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.richText = false;
        label.enableAutoSizing = false;
        label.extraPadding = true;
        Renderer textRenderer = label.renderer;
        if (textRenderer == null) return;
        textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        textRenderer.receiveShadows = false;
    }

    public void Setup(float amount, Vector3 up, bool isHeal = false)
    {
        if (label == null) return;
        _moveDir = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
        Vector3 right = Vector3.Cross(_moveDir, Vector3.forward);
        if (right.sqrMagnitude < 0.001f) right = Vector3.Cross(_moveDir, Vector3.right);
        right.Normalize();
        Vector3 forward = Vector3.Cross(right, _moveDir).normalized;
        transform.position += right * (spawnOffset.x + Random.Range(-0.35f, 0.35f))
            + _moveDir * spawnOffset.y + forward * spawnOffset.z;
        transform.localScale = Vector3.one * startScale;
        _baseColor = isHeal ? healColor : damageColor;
        label.color = _baseColor;
        label.SetText("{0}", Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(amount))));
        label.ForceMeshUpdate();
        _alpha = ((Color32)_baseColor).a;
        _age = 0;
        _setup = true;
    }

    void LateUpdate()
    {
        if (!_setup) return;
        if (_camera == null) _camera = Camera.main;
        if (_camera != null) transform.rotation = _camera.transform.rotation;
        _age += Time.deltaTime;
        float t = lifetime > 0 ? Mathf.Clamp01(_age / lifetime) : 1;
        transform.position += _moveDir * (floatSpeed * Time.deltaTime);
        float scale = t < 0.2f
            ? Mathf.Lerp(startScale, peakScale, t / 0.2f)
            : Mathf.Lerp(peakScale, endScale, (t - 0.2f) / 0.8f);
        transform.localScale = Vector3.one * scale;
        float fade = _age <= fadeDelay ? 0 : lifetime > fadeDelay
            ? Mathf.Clamp01((_age - fadeDelay) / (lifetime - fadeDelay)) : 1;
        byte alpha = (byte)Mathf.RoundToInt(_baseColor.a * (1 - fade) * 255);
        if (alpha != _alpha)
        {
            _alpha = alpha;
            // Fade colors without regenerating glyph positions/UVs each frame.
            TMP_TextInfo info = label.textInfo;
            for (int m = 0; m < info.materialCount; m++)
            {
                Color32[] colors = info.meshInfo[m].colors32;
                for (int i = 0; i < info.meshInfo[m].vertexCount; i++) colors[i].a = alpha;
            }
            label.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
        }
        if (_age >= lifetime)
        {
            _setup = false;
            if (_pool != null) _pool.Return(this);
            else Destroy(gameObject);
        }
    }

    internal void SetPool(DamagePopupPool.Bucket pool) => _pool = pool;
}
