using UnityEngine;

/// <summary>
/// Fixed spawn location. On a planet, face the next undefeated king at this location.
/// </summary>
[DisallowMultipleComponent]
public class ScenePlayerSpawnPoint : MonoBehaviour
{
    public static bool TryGetPose(out Vector3 position, out Quaternion rotation)
    {
        ScenePlayerSpawnPoint point = FindAnyObjectByType<ScenePlayerSpawnPoint>();
        if (point == null)
        {
            position = default;
            rotation = default;
            return false;
        }

        position = point.transform.position;
        rotation = point.transform.rotation;
        CreatureSpawner spawner = FindAnyObjectByType<CreatureSpawner>();
        if (spawner != null && spawner.TryGetNextKingFacing(position, out Vector3 forward, out Vector3 up))
            rotation = Quaternion.LookRotation(forward, up);
        return true;
    }
}
