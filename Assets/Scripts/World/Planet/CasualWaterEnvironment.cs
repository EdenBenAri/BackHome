using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Keeps the camera depth texture available so the casual sea can
/// read where the water meets the ground.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshRenderer))]
public sealed class CasualWaterEnvironment : MonoBehaviour
{
    void OnEnable()
    {
        EnableCameraDepth();
    }

    static void EnableCameraDepth()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        if (data == null)
            return;

        data.requiresDepthOption = CameraOverrideOption.On;
    }
}
