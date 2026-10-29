using TMPro;
using UnityEngine;

/// <summary>
/// Plays the particle systems already saved on this prefab, and pops the LEVEL UP
/// label that is also saved here. Nothing in this component creates objects.
/// </summary>
public class LevelUpBurst : MonoBehaviour
{
    [SerializeField] TextMeshPro title;
    [Tooltip("How fast the letters grow from nothing to full size.")]
    [SerializeField] float titlePop = 0.14f;
    [Tooltip("Short wobble after the pop, before the text sits still.")]
    [SerializeField] float titleShake = 0.42f;
    [SerializeField] float titleHold = 0.32f;
    [Tooltip("Exit. Squashes the letters flat on Y.")]
    [SerializeField] float titleClose = 0.16f;

    PlayerLevel _level;
    Vector3 _titleScale = Vector3.one;
    float _age;
    float _roll;
    bool _playing;

    void Awake()
    {
        if (title != null)
        {
            _titleScale = title.transform.localScale;
            title.transform.localScale = Vector3.zero;
        }
    }

    void OnEnable()
    {
        Bind();
    }

    void OnDisable()
    {
        Unbind();
    }

    void Update()
    {
        if (_level == null)
            Bind();
        TickTitle();
    }

    void Bind()
    {
        if (_level != null)
            return;

        _level = PlayerLevel.EnsureExists();
        if (_level != null)
            _level.LevelChanged += OnLevelChanged;
    }

    void Unbind()
    {
        if (_level == null)
            return;

        _level.LevelChanged -= OnLevelChanged;
        _level = null;
    }

    void OnLevelChanged(int level)
    {
        Play();
    }

    [ContextMenu("Preview Level Up")]
    public void Play()
    {
        ParticleSystem[] systems = GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem ps = systems[i];
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }

        _age = 0f;
        _roll = 0f;
        _playing = title != null;
        if (title != null)
            title.transform.localScale = Vector3.zero;
    }

    void TickTitle()
    {
        if (!_playing || title == null)
            return;

        _age += Time.deltaTime;
        float popEnd = titlePop;
        float shakeEnd = popEnd + titleShake;
        float holdEnd = shakeEnd + titleHold;
        float closeEnd = holdEnd + titleClose;
        float x = 1f;
        float y = 1f;

        if (_age < popEnd)
        {
            float u = popEnd <= 0f ? 1f : _age / popEnd;
            float grown = Mathf.Max(0f, EaseOutBack(u));
            x = grown;
            y = grown;
            _roll = 0f;
        }
        else if (_age < shakeEnd)
        {
            float t = _age - popEnd;
            float decay = 1f - Mathf.Clamp01(t / Mathf.Max(0.01f, titleShake));
            float wobble = Mathf.Sin(t * 28f) * decay;
            x = 1f + wobble * 0.07f;
            y = 1f - wobble * 0.09f;
            _roll = wobble * 5f;
        }
        else if (_age < holdEnd)
        {
            _roll = 0f;
        }
        else if (_age < closeEnd)
        {
            float u = (_age - holdEnd) / Mathf.Max(0.01f, titleClose);
            float shut = u * u;
            x = Mathf.Lerp(1f, 1.06f, shut);
            y = Mathf.Lerp(1f, 0f, shut);
            _roll = 0f;
        }
        else
        {
            title.transform.localScale = Vector3.zero;
            _roll = 0f;
            _playing = false;
            return;
        }

        title.transform.localScale = new Vector3(
            _titleScale.x * x,
            _titleScale.y * y,
            _titleScale.z);
    }

    void LateUpdate()
    {
        if (!_playing || title == null || Camera.main == null)
            return;

        Vector3 toCamera = Camera.main.transform.position - title.transform.position;
        if (toCamera.sqrMagnitude < 0.001f)
            return;

        Quaternion face = Quaternion.LookRotation(-toCamera, Camera.main.transform.up);
        title.transform.rotation = face * Quaternion.Euler(0f, 0f, _roll);
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}
