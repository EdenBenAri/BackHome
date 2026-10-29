using TMPro;
using UnityEngine;

/// <summary>
/// Plays the particle systems already saved on this prefab, and pops the LEVEL UP
/// label that is also saved here. Nothing in this component creates objects.
/// </summary>
public class LevelUpBurst : MonoBehaviour
{
    [SerializeField] TextMeshPro title;
    [SerializeField] float titlePop = 0.18f;
    [SerializeField] float titleHold = 0.7f;
    [SerializeField] float titleFade = 0.4f;

    PlayerLevel _level;
    Vector3 _titleScale = Vector3.one;
    float _age;
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
        float holdEnd = popEnd + titleHold;
        float fadeEnd = holdEnd + titleFade;
        float scale;
        float alpha;

        if (_age < popEnd)
        {
            float u = popEnd <= 0f ? 1f : _age / popEnd;
            scale = EaseOutBack(u);
            alpha = Mathf.Clamp01(u * 1.6f);
        }
        else if (_age < holdEnd)
        {
            scale = 1f;
            alpha = 1f;
        }
        else if (_age < fadeEnd)
        {
            float u = (_age - holdEnd) / Mathf.Max(0.01f, titleFade);
            scale = Mathf.Lerp(1f, 1.06f, u);
            alpha = 1f - u;
        }
        else
        {
            title.transform.localScale = Vector3.zero;
            _playing = false;
            return;
        }

        title.transform.localScale = _titleScale * Mathf.Max(0f, scale);
        Color color = title.color;
        color.a = alpha;
        title.color = color;
        Color outline = title.outlineColor;
        outline.a = alpha;
        title.outlineColor = outline;
    }

    void LateUpdate()
    {
        if (!_playing || title == null || Camera.main == null)
            return;

        Vector3 toCamera = Camera.main.transform.position - title.transform.position;
        if (toCamera.sqrMagnitude < 0.001f)
            return;

        title.transform.rotation = Quaternion.LookRotation(-toCamera, Camera.main.transform.up);
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}
