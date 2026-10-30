using UnityEngine;
using UnityEngine.UI;

/// <summary>A clean hollow circle around the Layer Lab house, with a soft pixel edge.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public class HomeNavigationRing : MaskableGraphic
{
    [SerializeField, Min(0.5f)] float thickness = 3f;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float outer = Mathf.Min(rect.width, rect.height) * 0.5f;
        float inner = Mathf.Max(0f, outer - thickness);
        float feather = Mathf.Min(0.8f, thickness * 0.5f);
        Color clear = color;
        clear.a = 0f;
        const int segments = 64;

        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            vh.AddVert(rect.center + direction * outer, clear, Vector2.zero);
            vh.AddVert(rect.center + direction * (outer - feather), color, Vector2.zero);
            vh.AddVert(rect.center + direction * (inner + feather), color, Vector2.zero);
            vh.AddVert(rect.center + direction * inner, clear, Vector2.zero);
            if (i == 0)
                continue;
            int start = (i - 1) * 4;
            for (int band = 0; band < 3; band++)
            {
                int a = start + band;
                vh.AddTriangle(a, a + 4, a + 1);
                vh.AddTriangle(a + 1, a + 4, a + 5);
            }
        }
    }
}
