using UnityEngine;

/// <summary>Shared spherical gameplay distance and allocation-conscious ground raycasts.</summary>
public static class PlanetSurfacePose
{
    public const float DefaultHover = 0.05f;

    /// <summary>Great-circle distance using the average radius of both points.</summary>
    public static float GetSurfaceDistance(Vector3 center, Vector3 a, Vector3 b)
    {
        Vector3 dirA = a - center;
        Vector3 dirB = b - center;
        float radiusA = dirA.magnitude;
        float radiusB = dirB.magnitude;
        if (radiusA < 0.0001f || radiusB < 0.0001f)
            return Vector3.Distance(a, b);

        float cosAngle = Mathf.Clamp(Vector3.Dot(dirA, dirB) / (radiusA * radiusB), -1f, 1f);
        float angle = Mathf.Acos(cosAngle);
        float averageRadius = (radiusA + radiusB) * 0.5f;
        return angle * averageRadius;
    }

    static readonly RaycastHit[] GroundHits = new RaycastHit[32];

    /// <summary>
    /// Ground ray that does not allocate on the common path.
    /// The returned buffer is reused, so read it before the next call.
    /// </summary>
    public static int RaycastGround(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        int layerMask,
        out RaycastHit[] hits)
    {
        int count = Physics.RaycastNonAlloc(
            origin,
            direction,
            GroundHits,
            maxDistance,
            layerMask,
            QueryTriggerInteraction.Ignore);

        if (count < GroundHits.Length)
        {
            hits = GroundHits;
            return count;
        }

        hits = Physics.RaycastAll(origin, direction, maxDistance, layerMask, QueryTriggerInteraction.Ignore);
        return hits != null ? hits.Length : 0;
    }
}
