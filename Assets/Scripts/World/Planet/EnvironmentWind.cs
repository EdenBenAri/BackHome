using UnityEngine;

/// <summary>
/// One wind field for the whole planet. Trees and grass read it in the toon shader
/// and sway on their own; rocks, camp props, and characters stay still because
/// their materials keep wind sway at zero.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
[AddComponentMenu("BackHome/Environment Wind")]
public class EnvironmentWind : MonoBehaviour
{
    [Tooltip("Slowest sway, in cycles-worth of speed. 0.5 is a long lazy lean.")]
    [SerializeField, Range(0.05f, 2f)] float minSpeed = 0.5f;

    [Tooltip("Fastest sway. Kept at 1 so the field never rushes.")]
    [SerializeField, Range(0.05f, 2f)] float maxSpeed = 1f;

    [Tooltip("How quickly a gust moves from one plant to the next. Higher means neighbors disagree more.")]
    [SerializeField, Min(0f)] float gustSpacing = 0.4f;

    [Tooltip("How fast the wind direction turns. A full turn takes about three minutes at 0.035.")]
    [SerializeField, Min(0f)] float directionTurnSpeed = 0.035f;

    [Tooltip("How close the player has to walk for grass to flick, in meters.")]
    [SerializeField, Min(0.2f)] float touchRadius = 1.2f;

    [Tooltip("How long the grass keeps moving after a step, in seconds.")]
    [SerializeField, Min(0.05f)] float touchDuration = 0.42f;

    const int TouchCount = 4;

    static readonly int DirId = Shader.PropertyToID("_EnvWindDir");
    static readonly int ParamsId = Shader.PropertyToID("_EnvWindParams");
    static readonly int TouchPosId = Shader.PropertyToID("_GrassTouchPos");
    static readonly int TouchDirId = Shader.PropertyToID("_GrassTouchDir");
    static readonly int TouchParamsId = Shader.PropertyToID("_GrassTouchParams");

    readonly Vector4[] _touchPos = new Vector4[TouchCount];
    readonly Vector4[] _touchDir = new Vector4[TouchCount];
    readonly float[] _touchBorn = new float[TouchCount];
    readonly bool[] _touchAlive = new bool[TouchCount];
    int _touchNext;
    Vector3 _lastStep;
    bool _hasStep;
    PlanetWalker _walker;

    void OnEnable()
    {
        Apply();
    }

    void Update()
    {
        Apply();
    }

    void OnValidate()
    {
        minSpeed = Mathf.Max(0.05f, minSpeed);
        maxSpeed = Mathf.Max(minSpeed, maxSpeed);
        gustSpacing = Mathf.Max(0f, gustSpacing);
        directionTurnSpeed = Mathf.Max(0f, directionTurnSpeed);
        touchRadius = Mathf.Max(0.2f, touchRadius);
        touchDuration = Mathf.Max(0.05f, touchDuration);
        Apply();
    }

    void Apply()
    {
        float now = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
        float angle = now * directionTurnSpeed;
        Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        Shader.SetGlobalVector(DirId, new Vector4(dir.x, dir.y, dir.z, 0f));
        Shader.SetGlobalVector(ParamsId, new Vector4(minSpeed, maxSpeed, gustSpacing, 0f));
        ApplyTouch(now);
    }

    void ApplyTouch(float now)
    {
        if (!Application.isPlaying)
            ClearTouch();
        else
            TrackPlayer(now);

        for (int i = 0; i < TouchCount; i++)
        {
            if (!_touchAlive[i])
            {
                _touchPos[i].w = -1f;
                continue;
            }

            float age = (now - _touchBorn[i]) / touchDuration;
            if (age >= 1f)
            {
                _touchAlive[i] = false;
                _touchPos[i].w = -1f;
                continue;
            }

            _touchPos[i].w = age;
        }

        Shader.SetGlobalVectorArray(TouchPosId, _touchPos);
        Shader.SetGlobalVectorArray(TouchDirId, _touchDir);
        Shader.SetGlobalVector(TouchParamsId, new Vector4(touchRadius, touchDuration, 0f, 0f));
    }

    void TrackPlayer(float now)
    {
        if (_walker == null)
            _walker = FindAnyObjectByType<PlanetWalker>();
        if (_walker == null)
            return;

        Vector3 velocity = _walker.PlanarVelocity;
        if (velocity.sqrMagnitude < 0.2f)
            return;

        Vector3 position = _walker.transform.position;
        float spacing = Mathf.Max(0.25f, velocity.magnitude * touchDuration / TouchCount);
        if (_hasStep && (position - _lastStep).sqrMagnitude < spacing * spacing)
            return;

        _lastStep = position;
        _hasStep = true;
        int slot = _touchNext;
        _touchNext = (_touchNext + 1) % TouchCount;
        _touchAlive[slot] = true;
        _touchBorn[slot] = now;
        _touchPos[slot] = new Vector4(position.x, position.y, position.z, 0f);
        Vector3 walk = velocity.normalized;
        _touchDir[slot] = new Vector4(walk.x, walk.y, walk.z, 0f);
    }

    void ClearTouch()
    {
        for (int i = 0; i < TouchCount; i++)
            _touchAlive[i] = false;
        _hasStep = false;
    }
}
