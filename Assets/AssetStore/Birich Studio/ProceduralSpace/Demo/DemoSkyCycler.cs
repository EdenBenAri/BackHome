using UnityEngine;

namespace BirichStudio.ProceduralSpace.Demo
{
    /// <summary>
    /// Demo helper: cycles the background through profiles and fresh seeds on a timer, and
    /// labels what is currently on screen. Deliberately input-free so the demo scene runs
    /// identically under the legacy Input Manager, the Input System package, or none at all —
    /// for hands-on exploration use the Randomize Seed button on the SpaceBackground inspector.
    /// </summary>
    [RequireComponent(typeof(SpaceBackground))]
    public class DemoSkyCycler : MonoBehaviour
    {
        [Tooltip("Profiles to rotate through. Empty = keep the background's own profile and " +
                 "only change seeds.")]
        [SerializeField] private SpaceBackgroundProfile[] profiles;

        [Tooltip("Seconds each sky stays on screen.")]
        [SerializeField] private float interval = 6f;

        [Tooltip("Optional 3D text label that shows profile name, archetype and seed.")]
        [SerializeField] private TextMesh label;

        private SpaceBackground _background;
        private float _timer;
        private int _profileIndex = -1;
        private int _step;

        private void Awake() => _background = GetComponent<SpaceBackground>();

        private void Start() => Next();

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < interval) return;
            _timer = 0f;
            Next();
        }

        private void Next()
        {
            _step++;
            if (profiles != null && profiles.Length > 0)
            {
                _profileIndex = (_profileIndex + 1) % profiles.Length;
                _background.Profile = profiles[_profileIndex];
            }

            // Deterministic walk instead of Random: replaying the demo shows the same tour,
            // which makes screenshots and store videos reproducible.
            _background.Rebuild(_step * 7919 + 12345);

            if (label != null)
            {
                string profileName = _background.Profile != null ? _background.Profile.name : "Default";
                label.text = $"{profileName}\n{_background.Archetype}  ·  seed {_background.Seed}";
            }
        }
    }
}
