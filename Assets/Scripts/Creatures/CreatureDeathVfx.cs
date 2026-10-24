using UnityEngine;

/// <summary>
/// Hides the body and spawns the assigned death VFX when a mob dies.
/// Kings keep a Death clip and do not use this. <see cref="Creature"/> calls <see cref="PlaySplash"/>.
/// </summary>
public class CreatureDeathVfx : MonoBehaviour
{
    [Tooltip("Spawned when this mob dies. Edit the particle system on the prefab itself.")]
    [SerializeField] GameObject deathVfxPrefab;
    [SerializeField] float bodyRadius = 1.2f;

    bool _played;

    /// <summary>Called from <see cref="Creature"/> when this mob dies.</summary>
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
