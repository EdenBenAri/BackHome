using UnityEngine;

public enum PlanetStageId
{
    [InspectorName("A - Grass")] A = 1,
    [InspectorName("B - GlowingMushroom")] B = 2,
    [InspectorName("C - Void")] C = 3,
    [InspectorName("D - Crimson")] D = 4
}

/// <summary>A stage groups encounter areas under the planet's Areas root.</summary>
[DisallowMultipleComponent]
public sealed class PlanetStage : MonoBehaviour
{
    [SerializeField] PlanetStageId stageId = PlanetStageId.A;
    public PlanetStageId Id => stageId;

    public static PlanetStageId FromTerrain(int terrain)
    {
        switch (terrain)
        {
            case PlanetTileset.TerrainGlowingMushroom:
            case PlanetTileset.TerrainDarkGlowingMushroom: return PlanetStageId.B;
            case PlanetTileset.TerrainVoid:
            case PlanetTileset.TerrainDarkVoid: return PlanetStageId.C;
            case PlanetTileset.TerrainCrimson:
            case PlanetTileset.TerrainDarkCrimson: return PlanetStageId.D;
            default: return PlanetStageId.A;
        }
    }

    /// <summary>
    /// Resolve an area by name inside a stage, or by an explicit path such as Stage A/A1.
    /// Direct Areas children remain supported for worlds that have not been grouped yet.
    /// </summary>
    public static Transform FindArea(Transform areasRoot, string areaId)
    {
        if (areasRoot == null || string.IsNullOrWhiteSpace(areaId))
            return null;

        string id = areaId.Trim();
        Transform direct = areasRoot.Find(id);
        if (direct != null)
            return direct;

        for (int i = 0; i < areasRoot.childCount; i++)
        {
            Transform group = areasRoot.GetChild(i);
            if (!group.TryGetComponent<PlanetStage>(out _))
                continue;

            Transform area = group.Find(id);
            if (area != null)
                return area;
        }

        return null;
    }
}
