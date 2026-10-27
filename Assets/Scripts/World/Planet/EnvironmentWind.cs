using UnityEngine;

/// <summary>
/// One wind field for the whole planet. Trees and grass read it in the toon shader
/// and sway on their own; rocks, props, and characters stay still because
/// their materials keep wind sway at zero.
/// Grass also rustles around the player while they walk through it. That bend
/// is added on top of the wind, and the patch they leave behind settles back.
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

    [Tooltip("How far around the player the grass rustles, in meters.")]
    [SerializeField, Min(0.2f)] float touchRadius = 2.4f;

    [Tooltip("How long a patch keeps rustling after the player has walked on, in seconds.")]
    [SerializeField, Min(0.05f)] float touchDuration = 0.9f;

    [Tooltip("How fast the blades flutter while the player is in them.")]
    [SerializeField, Range(4f, 28f)] float rustleSpeed = 14f;

    const int WakeCount = 3;

    static readonly int DirId = Shader.PropertyToID("_EnvWindDir");
    static readonly int ParamsId = Shader.PropertyToID("_EnvWindParams");
    static readonly int BodyPosId = Shader.PropertyToID("_GrassBodyPos");
    static readonly int BodyDirId = Shader.PropertyToID("_GrassBodyDir");
    static readonly int TouchParamsId = Shader.PropertyToID("_GrassTouchParams");
    static readonly int[] WakePosIds =
    {
        Shader.PropertyToID("_GrassWake0"),
        Shader.PropertyToID("_GrassWake1"),
        Shader.PropertyToID("_GrassWake2"),
    };
    static readonly int[] WakeDirIds =
    {
        Shader.PropertyToID("_GrassWakeDir0"),
        Shader.PropertyToID("_GrassWakeDir1"),
        Shader.PropertyToID("_GrassWakeDir2"),
    };

    readonly Vector4[] _wakePos = new Vector4[WakeCount];
    readonly Vector4[] _wakeDir = new Vector4[WakeCount];
    readonly float[] _wakeBorn = new float[WakeCount];
    readonly bool[] _wakeAlive = new bool[WakeCount];
    Vector3 _lastStep;
    Vector3 _lastWalk = Vector3.forward;
    bool _hasStep;
    float _bodyStrength;
    PlanetWalker _walker;

    void OnEnable()
    {
        Apply();
    }

    void OnDisable()
    {
        ClearTouch();
        PushTouch();
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
        rustleSpeed = Mathf.Clamp(rustleSpeed, 4f, 28f);
        Apply();
    }

    void Apply()
    {
        float now = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
        float angle = now * directionTurnSpeed;
        Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        Shader.SetGlobalVector(DirId, new Vector4(dir.x, dir.y, dir.z, 0f));
        Shader.SetGlobalVector(ParamsId, new Vector4(minSpeed, maxSpeed, gustSpacing, 0f));
        if (Application.isPlaying)
            TrackPlayer(now);
        else
            ClearTouch();
        PushTouch();
    }

    void TrackPlayer(float now)
    {
        if (_walker == null)
            _walker = FindAnyObjectByType<PlanetWalker>();

        float target = 0f;
        Vector3 position = _walker != null ? _walker.transform.position : Vector3.zero;
        if (_walker != null)
        {
            Vector3 velocity = _walker.PlanarVelocity;
            if (velocity.sqrMagnitude > 0.16f)
            {
                target = 1f;
                _lastWalk = velocity.normalized;
                // Keep the samples near the body. Skip a drop while every wake slot
                // is still playing, so an old patch fades out instead of popping.
                float spacing = Mathf.Max(0.5f, touchRadius * 0.65f);
                bool farEnough = !_hasStep || (position - _lastStep).sqrMagnitude >= spacing * spacing;
                if (farEnough && TryDropWake(position, _lastWalk, now))
                {
                    _lastStep = position;
                    _hasStep = true;
                }
            }
        }

        float fadeSpeed = target > 0.5f ? 10f : 2.4f;
        _bodyStrength = Mathf.MoveTowards(_bodyStrength, target, Time.deltaTime * fadeSpeed);
        AgeWakes(now);
    }

    bool TryDropWake(Vector3 position, Vector3 walk, float now)
    {
        int slot = -1;
        for (int i = 0; i < WakeCount; i++)
        {
            if (_wakeAlive[i])
                continue;
            slot = i;
            break;
        }

        if (slot < 0)
            return false;

        _wakeAlive[slot] = true;
        _wakeBorn[slot] = now;
        _wakePos[slot] = new Vector4(position.x, position.y, position.z, 0f);
        _wakeDir[slot] = new Vector4(walk.x, walk.y, walk.z, 0f);
        return true;
    }

    void AgeWakes(float now)
    {
        for (int i = 0; i < WakeCount; i++)
        {
            if (!_wakeAlive[i])
            {
                _wakePos[i].w = -1f;
                continue;
            }

            float age = (now - _wakeBorn[i]) / touchDuration;
            if (age >= 1f)
            {
                _wakeAlive[i] = false;
                _wakePos[i].w = -1f;
                continue;
            }

            _wakePos[i].w = age;
        }
    }

    void ClearTouch()
    {
        _bodyStrength = 0f;
        _hasStep = false;
        for (int i = 0; i < WakeCount; i++)
        {
            _wakeAlive[i] = false;
            _wakePos[i].w = -1f;
        }
    }

    void PushTouch()
    {
        Vector3 position = _walker != null ? _walker.transform.position : Vector3.zero;
        Shader.SetGlobalVector(BodyPosId, new Vector4(position.x, position.y, position.z, _bodyStrength));
        Shader.SetGlobalVector(BodyDirId, new Vector4(_lastWalk.x, _lastWalk.y, _lastWalk.z, 0f));
        for (int i = 0; i < WakeCount; i++)
        {
            Shader.SetGlobalVector(WakePosIds[i], _wakePos[i]);
            Shader.SetGlobalVector(WakeDirIds[i], _wakeDir[i]);
        }

        Shader.SetGlobalVector(TouchParamsId, new Vector4(touchRadius, touchDuration, rustleSpeed, 0f));
    }
}
