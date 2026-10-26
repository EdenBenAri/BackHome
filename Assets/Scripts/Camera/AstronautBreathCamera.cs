using UnityEngine;

/// <summary>
/// Seated astronaut POV. The pose in the scene is the rest pose; on Play the
/// camera rises, leans, and nods on a slow inhale / hold / exhale cycle.
/// </summary>
public class AstronautBreathCamera : MonoBehaviour
{
    [Header("Cycle")]
    [Tooltip("Resting breaths per minute. Around 12–16 feels calm.")]
    [SerializeField] float breathsPerMinute = 13f;
    [Tooltip("Share of the cycle spent inhaling. The rest is a short hold, then the exhale.")]
    [SerializeField, Range(0.35f, 0.7f)] float inhalePortion = 0.46f;
    [SerializeField, Range(0f, 0.2f)] float holdPortion = 0.08f;

    [Header("Motion")]
    [Tooltip("How far the helmet rises at the top of an inhale, in meters.")]
    [SerializeField] float rise = 0.055f;
    [Tooltip("Lean toward the console on the inhale, in meters.")]
    [SerializeField] float lean = 0.018f;
    [Tooltip("Slow side-to-side drift, in meters. Half the breath rate.")]
    [SerializeField] float sway = 0.012f;
    [SerializeField] float pitchDegrees = 1.5f;
    [SerializeField] float rollDegrees = 0.4f;
    [SerializeField] float yawDegrees = 0.2f;

    Vector3 _restPosition;
    Quaternion _restRotation;
    bool _captured;

    void OnEnable()
    {
        CaptureRestPose();
    }

    void LateUpdate()
    {
        if (!_captured)
            CaptureRestPose();

        float cycle = Mathf.Repeat(Time.time * (breathsPerMinute / 60f), 1f);
        float breath = Breath(cycle);
        float side = Mathf.Sin(cycle * Mathf.PI);

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        Vector3 offset = Vector3.up * (breath * rise)
            + forward * (breath * lean)
            + right * (side * sway);

        Quaternion nod = Quaternion.Euler(
            breath * pitchDegrees,
            side * yawDegrees,
            side * rollDegrees);

        transform.SetPositionAndRotation(_restPosition + offset, _restRotation * nod);
    }

    void CaptureRestPose()
    {
        _restPosition = transform.position;
        _restRotation = transform.rotation;
        _captured = true;
    }

    float Breath(float cycle)
    {
        float inhale = Mathf.Clamp(inhalePortion, 0.2f, 0.8f);
        float hold = Mathf.Clamp(holdPortion, 0f, 0.25f);
        if (inhale + hold > 0.9f)
            hold = Mathf.Max(0f, 0.9f - inhale);

        if (cycle < inhale)
            return Smooth(cycle / inhale);

        if (cycle < inhale + hold)
            return 1f;

        float exhale = Mathf.Max(0.05f, 1f - inhale - hold);
        return 1f - Smooth((cycle - inhale - hold) / exhale);
    }

    static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
