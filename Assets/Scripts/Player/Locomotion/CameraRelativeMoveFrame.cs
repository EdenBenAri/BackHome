using UnityEngine;

/// <summary>
/// Screen-relative movement on the local ground plane. The gameplay camera keeps
/// its compass, so no hidden compass capture or angular rebind is needed.
/// </summary>
public static class CameraRelativeMoveFrame
{
    public static Vector3 Resolve(Vector2 input, Vector3 up, Vector3 viewForward)
    {
        if (input.sqrMagnitude < 0.01f)
            return Vector3.zero;
        Vector3 forward = Tangent(viewForward, up);
        Vector3 right = Vector3.Cross(up, forward).normalized;
        return (right * input.x + forward * input.y).normalized;
    }

    static Vector3 Tangent(Vector3 forward, Vector3 up)
    {
        Vector3 projected = Vector3.ProjectOnPlane(forward, up);
        if (projected.sqrMagnitude < 0.001f)
            projected = Vector3.ProjectOnPlane(Mathf.Abs(up.z) < 0.9f ? Vector3.forward : Vector3.right, up);
        return projected.normalized;
    }
}
