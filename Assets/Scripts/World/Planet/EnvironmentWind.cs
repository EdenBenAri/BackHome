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

    static readonly int DirId = Shader.PropertyToID("_EnvWindDir");
    static readonly int ParamsId = Shader.PropertyToID("_EnvWindParams");

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
        Apply();
    }

    void Apply()
    {
        float now = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
        float angle = now * directionTurnSpeed;
        Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        Shader.SetGlobalVector(DirId, new Vector4(dir.x, dir.y, dir.z, 0f));
        Shader.SetGlobalVector(ParamsId, new Vector4(minSpeed, maxSpeed, gustSpacing, 0f));
    }
}
