using System;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared scene-role checks used by locomotion and navigation.
/// </summary>
public static class SceneRoles
{
    public static bool IsPlanetScene()
    {
        string name = SceneManager.GetActiveScene().name;
        if (string.IsNullOrEmpty(name))
            return false;

        return name.StartsWith("Galaxy", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("Planet", StringComparison.OrdinalIgnoreCase);
    }
}
