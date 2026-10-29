using StarterAssets;
using UnityEngine;

/// <summary>
/// Spawns Player when a playable scene starts. HUD lives in the scene (Hud prefab instance)
/// so the full Canvas is visible in edit mode; this only instantiates it if the scene has none.
/// CameraFollow stays on Main Camera — only the target is wired at runtime.
/// Per-scene vitals bar sizing lives here because cameras differ per world.
/// The player appears at the scene's <see cref="ScenePlayerSpawnPoint"/>.
/// </summary>
public class SceneBootstrap : MonoBehaviour
{
    [SerializeField] GameObject playerPrefab;
    [Tooltip("Fallback only. Prefer a Hud instance already placed in the scene.")]
    [SerializeField] GameObject uiPrefab;

    [Header("Vitals Bars")]
    [Tooltip("World size of HP/Oxygen bars for this scene. Raise on distant/high cameras (planets).")]
    [SerializeField, Min(0.1f)] float hpBarScale = 1f;
    [Tooltip("Local offset of the bars relative to the player (XYZ).")]
    [SerializeField] Vector3 hpBarOffset = new Vector3(0f, 2.15f, 0f);

    /// <summary>Prefab this scene uses for the player.</summary>
    public GameObject PlayerPrefab => playerPrefab;

    void Awake()
    {
        if (uiPrefab != null && FindExistingHud() == null)
        {
            GameObject uiObject = Instantiate(uiPrefab);
            uiObject.name = "Hud";
        }

        PlayerInventory.EnsureExists();
        PlayerCargo.EnsureExists();
        PlayerLevel.EnsureExists();
        SpawnPlayerAndBindCamera();
    }

    void SpawnPlayerAndBindCamera()
    {
        Transform player = FindExistingPlayer();
        if (player == null && playerPrefab != null)
        {
            if (!ScenePlayerSpawnPoint.TryGetPose(out Vector3 position, out Quaternion rotation))
            {
                Debug.LogWarning("SceneBootstrap: no spawn pose resolved. Add a ScenePlayerSpawnPoint to this scene.", this);
                return;
            }

            GameObject playerObject = Instantiate(playerPrefab, position, rotation);
            playerObject.name = playerPrefab.name;
            player = playerObject.transform;
        }

        BindPlayer(player);
    }

    /// <summary>Wires camera, vitals bars, and mobile input onto an already-spawned player.</summary>
    public void BindPlayer(Transform player)
    {
        if (player == null)
            return;

        BindCameraTarget(player);
        ApplyHpBarSettings(player);
        ApplyCargoMeter(player);
        BindMobileInput(player);
    }

    static void BindMobileInput(Transform player)
    {
        MobileInputBinder binder = FindAnyObjectByType<MobileInputBinder>();
        if (binder == null)
            return;

        binder.BindPlayer(player.GetComponent<StarterAssetsInputs>());
    }

    static GameObject FindExistingHud()
    {
        GameObject named = GameObject.Find("Hud");
        if (named != null)
            return named;

        PlayerDeathUI death = Object.FindAnyObjectByType<PlayerDeathUI>(FindObjectsInactive.Include);
        return death != null ? death.gameObject : null;
    }

    static Transform FindExistingPlayer()
    {
        TouchController motor = FindAnyObjectByType<TouchController>();
        if (motor != null)
            return motor.transform;

        GameObject tagged = GameObject.FindGameObjectWithTag("Player");
        return tagged != null ? tagged.transform : null;
    }

    void ApplyHpBarSettings(Transform player)
    {
        HpBar vitals = player.GetComponent<HpBar>();
        if (vitals == null)
            return;

        vitals.SetWorldScale(hpBarScale);
        vitals.SetLocalOffset(hpBarOffset);
    }

    void ApplyCargoMeter(Transform player)
    {
        CargoMeter meter = player.GetComponentInChildren<CargoMeter>(true);
        if (meter != null)
            meter.SetWorldScale(hpBarScale);

        CargoFullCallout callout = player.GetComponentInChildren<CargoFullCallout>(true);
        if (callout != null)
            callout.SetWorldScale(hpBarScale);
    }

    static void BindCameraTarget(Transform player)
    {
        CameraFollow follow = Object.FindAnyObjectByType<CameraFollow>();
        if (follow == null && Camera.main != null)
            follow = Camera.main.GetComponent<CameraFollow>();

        if (follow != null)
            follow.SetTarget(player);
        else
            Debug.LogWarning("SceneBootstrap: add CameraFollow on Main Camera to tune offsets per scene.");
    }
}
