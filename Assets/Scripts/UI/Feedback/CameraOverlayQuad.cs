using UnityEngine;

/// <summary>
/// Camera-parented quad that fills the view. Shared by the low-HP, damage, and death overlays.
/// </summary>
public static class CameraOverlayQuad
{
    public static Mesh Create(string meshName)
    {
        var mesh = new Mesh { name = meshName };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Camera Resolve(ref Camera camera)
    {
        if (camera != null)
            return camera;

        camera = Camera.main;
        if (camera == null)
            camera = Object.FindAnyObjectByType<Camera>();
        return camera;
    }

    public static void Fit(
        Transform overlay,
        Camera cam,
        ref float lastFov,
        ref float lastAspect,
        float nearPadding,
        bool force = false)
    {
        if (cam == null || overlay == null)
            return;

        float fov = cam.fieldOfView;
        float aspect = cam.aspect;
        if (!force && Mathf.Abs(fov - lastFov) < 0.01f && Mathf.Abs(aspect - lastAspect) < 0.001f)
            return;

        lastFov = fov;
        lastAspect = aspect;

        float z = cam.nearClipPlane + nearPadding;
        float height = 2f * z * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        overlay.localPosition = new Vector3(0f, 0f, z);
        overlay.localScale = new Vector3(height * aspect, height, 1f);
    }
}
