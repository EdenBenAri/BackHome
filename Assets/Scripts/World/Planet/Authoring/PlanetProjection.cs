using UnityEngine;

/// <summary>
/// Equirectangular authoring coordinates, in metres at the equator.
/// X goes east (tile longitude 0..360), Z goes north, Y is height above Radius.
/// The two X edges meet; the two Z edges collapse to the poles.
/// </summary>
public static class PlanetProjection
{
    public static Vector3 ToSphere(Vector3 flat, float radius)
    {
        float lon = flat.x / radius + Mathf.PI;
        float lat = Mathf.Clamp(flat.z / radius, -Mathf.PI * .5f, Mathf.PI * .5f);
        float cos = Mathf.Cos(lat);
        return new Vector3(cos * Mathf.Cos(lon), Mathf.Sin(lat), cos * Mathf.Sin(lon))
            * Mathf.Max(.01f, radius + flat.y);
    }

    public static Vector3 ToFlat(Vector3 sphere, float radius)
    {
        float length = sphere.magnitude;
        if (length < .00001f)
            return new Vector3(0f, -radius, 0f);
        float lon = Mathf.Repeat(Mathf.Atan2(sphere.z, sphere.x), 2f * Mathf.PI);
        return new Vector3((lon - Mathf.PI) * radius, length - radius,
            Mathf.Atan2(sphere.y, Mathf.Sqrt(sphere.x * sphere.x + sphere.z * sphere.z)) * radius);
    }

    // A proper (non-mirrored) frame: flat +X = east, +Y = radial up, +Z = north.
    public static Quaternion Frame(Vector3 flat, float radius)
    {
        float lon = flat.x / radius + Mathf.PI;
        float lat = Mathf.Clamp(flat.z / radius, -Mathf.PI * .5f, Mathf.PI * .5f);
        Vector3 up = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat),
            Mathf.Cos(lat) * Mathf.Sin(lon));
        Vector3 north = new Vector3(-Mathf.Sin(lat) * Mathf.Cos(lon), Mathf.Cos(lat),
            -Mathf.Sin(lat) * Mathf.Sin(lon));
        return Quaternion.LookRotation(north, up);
    }

    public static bool TryCell(Vector3 flat, float radius, int rows, int columns,
        out int row, out int column)
    {
        float u = flat.x / (2f * Mathf.PI * radius) + .5f;
        float v = flat.z / (Mathf.PI * radius) + .5f;
        row = column = 0;
        if (u < 0f || u > 1f || v < 0f || v > 1f)
            return false;
        row = Mathf.Min(rows - 1, Mathf.FloorToInt(v * rows));
        column = Mathf.Min(columns - 1, Mathf.FloorToInt(u * columns));
        return true;
    }
}
