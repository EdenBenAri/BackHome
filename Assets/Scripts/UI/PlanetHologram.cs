using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Lightweight hologram of a planet. One transparent shell, no tile mesh,
/// no props, no walk colliders. A click on the globe loads the planet scene.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class PlanetHologram : MonoBehaviour
{
    [SerializeField] string planetSceneName = "PlanetNyxara";
    [SerializeField] Material hologramMaterial;
    [SerializeField] float spinDegreesPerSecond = 12f;

    SphereCollider _collider;
    MeshRenderer _shell;
    bool _traveling;

    void Awake()
    {
        _collider = GetComponent<SphereCollider>();
        if (_collider != null)
        {
            _collider.isTrigger = true;
            _collider.enabled = true;
        }

        var baseRenderer = GetComponent<MeshRenderer>();
        if (baseRenderer != null)
        {
            baseRenderer.enabled = false;
            baseRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            baseRenderer.receiveShadows = false;
        }
    }

    void LateUpdate()
    {
        ApplyShell();
        if (Application.isPlaying)
            transform.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.Self);
    }

    void Update()
    {
        if (!Application.isPlaying || _traveling || _collider == null)
            return;
        if (!TryGetPress(out Vector2 screen))
            return;

        Camera camera = Camera.main;
        if (camera == null)
            return;

        Ray ray = camera.ScreenPointToRay(screen);
        if (_collider.Raycast(ray, out _, 80f))
            Travel();
    }

    public void Travel()
    {
        if (_traveling || string.IsNullOrWhiteSpace(planetSceneName))
            return;

        _traveling = true;
        SceneManager.LoadScene(planetSceneName);
    }

    void ApplyShell()
    {
        if (hologramMaterial == null)
            return;

        if (_shell == null)
        {
            Transform shell = transform.Find("VisualShell");
            if (shell == null)
                return;
            _shell = shell.GetComponent<MeshRenderer>();
            if (_shell == null)
                return;
        }

        if (_shell.sharedMaterial != hologramMaterial)
            _shell.sharedMaterial = hologramMaterial;

        _shell.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _shell.receiveShadows = false;
        _shell.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _shell.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    static bool TryGetPress(out Vector2 screen)
    {
        screen = default;
        Pointer pointer = Pointer.current;
        if (pointer == null || !pointer.press.wasPressedThisFrame)
            return false;

        screen = pointer.position.ReadValue();
        return true;
    }
}
