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

        [Tooltip("World meters that fill one screen. Camera travel is scaled into the sky " +
                 "by this, so a parallax of 1 slides the sky by one screen per this many meters. " +
                 "Lower values make the background move faster.")]
        [SerializeField] private float parallaxScreenMeters = 32f;

        static readonly int SkyTravelId = Shader.PropertyToID("_SkyTravel");

        MeshRenderer _renderer;
        MaterialPropertyBlock _block;
        Vector2 _travel;
        Vector3 _motionPos;
        Vector3 _groundForward = Vector3.forward;
        bool _hasMotion;

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
            AccumulateTravel(cam, height);
        }

        /// <summary>
        /// Scroll the sky by how the camera actually moved across the ground.
        /// The offset is in view-plane units, so turning around the planet slides
        /// the stars instead of flattening one axis and stretching them.
        /// </summary>
        void AccumulateTravel(Camera cam, float screenHeight)
        {
            Vector3 pos = cam.transform.position;
            Vector3 up = cam.transform.up;
            Vector3 right = cam.transform.right;
            Vector3 groundForward = Vector3.ProjectOnPlane(cam.transform.forward, up);
            if (groundForward.sqrMagnitude > 0.0001f)
                groundForward.Normalize();
            else if (_groundForward.sqrMagnitude > 0.0001f)
                groundForward = _groundForward;
            _groundForward = groundForward;

            if (!_hasMotion)
            {
                _motionPos = pos;
                _hasMotion = true;
            }
            else if ((pos - _motionPos).sqrMagnitude > 40f * 40f)
            {
                // A portal or a camera cut is not walking.
                _motionPos = pos;
            }
            else
            {
                float dt = Time.deltaTime > 0.0001f ? Time.deltaTime : 0.016f;
                float blend = 1f - Mathf.Exp(-10f * dt);
                Vector3 next = Vector3.Lerp(_motionPos, pos, blend);
                Vector3 delta = next - _motionPos;
                _motionPos = next;

                float gain = screenHeight / Mathf.Max(1f, parallaxScreenMeters);
                _travel.x += Vector3.Dot(delta, right) * gain;
                _travel.y += Vector3.Dot(delta, groundForward) * gain;
            }

            if (_renderer == null)
                _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null)
                return;

            _block ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);
            _block.SetVector(SkyTravelId, new Vector4(_travel.x, _travel.y, 0f, 0f));
            _renderer.SetPropertyBlock(_block);
        }
    }
}
