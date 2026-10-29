using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One cargo icon plus its count. The Cargo prefab keeps a disabled copy of this
/// as the template; <see cref="CargoMeter"/> clones it for each stack.
/// </summary>
public class CargoChip : MonoBehaviour
{
    [SerializeField] Image icon;
    [SerializeField] TMP_Text count;

    public Image Icon => icon;
    public TMP_Text Count => count;
    public RectTransform Rect => (RectTransform)transform;
}
