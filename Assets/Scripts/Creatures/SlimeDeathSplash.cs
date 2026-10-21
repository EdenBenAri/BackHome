using UnityEngine;

/// <summary>
/// Spawns the death VFX prefab assigned in the inspector when the slime hits the ground.
/// </summary>
public class SlimeDeathSplash : MonoBehaviour
{
    [Tooltip("Drag the slimeDeathVFX prefab here. Spawned when the death animation hits the ground.")]
    [SerializeField] GameObject deathVfxPrefab;
    [SerializeField] float bodyRadius = 1.8f;

    bool _played;

    /// <summary>Animation event on the Death clip, as the body hits the ground.</summary>
    public void PlaySplash()
    {
        if (_played || deathVfxPrefab == null)
            return;

        _played = true;

        Vector3 up = transform.up.sqrMagnitude > 0.0001f ? transform.up.normalized : Vector3.up;
        Vector3 position = transform.position + up * (bodyRadius * 0.32f);
        Quaternion rotation = Quaternion.LookRotation(transform.forward, up);
        Instantiate(deathVfxPrefab, position, rotation);
    }
}
