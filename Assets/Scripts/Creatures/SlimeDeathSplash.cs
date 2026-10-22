using UnityEngine;

/// <summary>
/// Spawns the death VFX prefab assigned in the inspector when the slime dies.
/// The body itself has no death pose; <see cref="Creature"/> calls this and then removes it.
/// </summary>
public class SlimeDeathSplash : MonoBehaviour
{
    [Tooltip("Drag the slimeDeathVFX prefab here. Spawned when the slime dies.")]
    [SerializeField] GameObject deathVfxPrefab;
    [SerializeField] float bodyRadius = 1.8f;

    bool _played;

    /// <summary>Called from <see cref="Creature"/> when this slime dies.</summary>
    public void PlaySplash()
    {
        if (_played)
            return;

        _played = true;
        HideBody();

        if (deathVfxPrefab == null)
            return;

        Vector3 up = transform.up.sqrMagnitude > 0.0001f ? transform.up.normalized : Vector3.up;
        Vector3 position = transform.position + up * (bodyRadius * 0.32f);
        Quaternion rotation = Quaternion.LookRotation(transform.forward, up);
        Instantiate(deathVfxPrefab, position, rotation);
    }

    void HideBody()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = false;
        }
    }
}
