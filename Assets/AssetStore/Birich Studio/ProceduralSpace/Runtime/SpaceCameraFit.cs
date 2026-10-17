using UnityEngine;

namespace BirichStudio.ProceduralSpace
{
    /// <summary>
    /// Keeps the background quad glued to a camera and scaled to cover its full view — the
    /// zero-setup path: drop the prefab in, press play, the sky fills the screen.
    ///
    /// Following the camera is not just convenience, it is what makes the parallax factors mean
    /// what they say: with the quad riding along, a layer with parallax 0 becomes perfectly
    /// screen-locked (infinitely far) and a layer with parallax p drifts at exactly p × camera
    /// motion. Both orthographic and perspective cameras are supported; for perspective the quad
    /// is sized to the view frustum at its own depth.
    ///
    /// Disable this component (or remove it) to place and scale the quad manually.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Birich Studio/Procedural Space/Space Camera Fit")]
    public class SpaceCameraFit : MonoBehaviour
    {
        [Tooltip("Camera to follow. Empty = the main camera.")]
        [SerializeField] private Camera targetCamera;

        [Tooltip("Extra scale so the quad always overhangs the view edges — protects against " +
                 "aspect-ratio changes and post-processing that samples outside the frame.")]
        [SerializeField, Range(1f, 2f)] private float padding = 1.15f;

        [Tooltip("Distance the quad is pushed along the camera's forward axis (perspective " +
                 "cameras). For orthographic 2D setups only the sorting order matters, but the " +
                 "quad must still sit inside the far plane.")]
        [SerializeField] private float distance = 50f;

        /// <summary>The camera being followed (resolved).</summary>
        public Camera TargetCamera
        {
            get
            {
                if (targetCamera != null) return targetCamera;
                return Camera.main;
            }
            set => targetCamera = value;
        }

        private void LateUpdate()
        {
            Camera cam = TargetCamera;
            if (cam == null) return;

            float d = cam.orthographic
                ? Mathf.Min(distance, cam.farClipPlane * 0.9f)
                : Mathf.Clamp(distance, cam.nearClipPlane * 2f, cam.farClipPlane * 0.9f);

            transform.position = cam.transform.position + cam.transform.forward * d;
            transform.rotation = cam.transform.rotation;

            float height = cam.orthographic
                ? cam.orthographicSize * 2f
                : 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float width = height * cam.aspect;

            transform.localScale = new Vector3(width * padding, height * padding, 1f);
        }
    }
}
