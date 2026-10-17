using UnityEngine;
// BIRICH_INPUTSYSTEM comes from this assembly's own versionDefines and is set only when the
// Input System package is actually installed; ENABLE_INPUT_SYSTEM says only that the new backend
// is ACTIVE. Both are needed: the first guards the reference, the second guards the behaviour.
#if BIRICH_INPUTSYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BirichStudio.ProceduralSpace.Demo
{
    /// <summary>
    /// Demo helper: steers the camera from the mouse position so the parallax ladder can be
    /// explored directly — push the pointer to a corner and the near layers slide across the far
    /// ones. With a static camera every layer sits still and the depth work goes unnoticed.
    ///
    /// The motion is smoothed rather than locked to the cursor: a backdrop that snaps 1:1 to the
    /// mouse reads as a picture being dragged, while a backdrop that eases toward it reads as a
    /// heavy world the viewer is turning to look at.
    ///
    /// Demo-only and purely cosmetic; delete it (or the whole Demo folder) in a real project.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Birich Studio/Procedural Space/Demo/Camera Look")]
    public class DemoCameraLook : MonoBehaviour
    {
        [Tooltip("How far the camera travels from its starting point at the screen edges, in " +
                 "world units. The horizontal value normally wants to be the larger of the two.")]
        [SerializeField] private Vector2 range = new Vector2(6f, 2f);

        [Tooltip("How quickly the camera catches up with the pointer. Low values feel heavy and " +
                 "cinematic, high values feel immediate.")]
        [SerializeField, Range(0.5f, 20f)] private float responsiveness = 4f;

        [Tooltip("Move away from the pointer instead of toward it.")]
        [SerializeField] private bool invert;

        private Vector3 _origin;

        private void Awake() => _origin = transform.position;

        private void Update()
        {
            Vector2 aim = PointerFromCenter();
            if (invert) aim = -aim;

            Vector3 target = _origin + new Vector3(aim.x * range.x, aim.y * range.y, 0f);

            // Exponential smoothing written frame-rate independently: a plain Lerp with a
            // constant factor eases at a speed that depends on the frame rate, so the same scene
            // feels different on a 60 Hz and a 144 Hz display.
            float t = 1f - Mathf.Exp(-responsiveness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, target, t);
        }

        /// <summary>Pointer position as -1..1 from the centre of the screen, or zero if unknown.</summary>
        private static Vector2 PointerFromCenter()
        {
            Vector2 p;
#if BIRICH_INPUTSYSTEM && ENABLE_INPUT_SYSTEM
            // The project may be configured for the Input System alone, where the legacy
            // Input.mousePosition throws rather than returning anything.
            if (Mouse.current == null) return Vector2.zero;
            p = Mouse.current.position.ReadValue();
#else
            p = Input.mousePosition;
#endif
            // Screen dimensions can momentarily be zero while the window is being created or
            // resized, and the pointer can sit outside the view entirely.
            if (Screen.width <= 0 || Screen.height <= 0) return Vector2.zero;

            Vector2 n = new Vector2(p.x / Screen.width, p.y / Screen.height) * 2f - Vector2.one;
            return new Vector2(Mathf.Clamp(n.x, -1f, 1f), Mathf.Clamp(n.y, -1f, 1f));
        }
    }
}
