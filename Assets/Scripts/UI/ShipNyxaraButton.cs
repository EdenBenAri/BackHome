using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Click target for the Nyxara button on the Ship canvas.
/// </summary>
public class ShipNyxaraButton : MonoBehaviour
{
    [SerializeField] string planetSceneName = "PlanetNyxara";

    public void Travel()
    {
        if (string.IsNullOrWhiteSpace(planetSceneName))
            return;

        SceneManager.LoadScene(planetSceneName);
    }
}
