using System;
using UnityEngine;

[Serializable]
public sealed class MiniMapPalette
{
    public Color grass = new Color32(140, 208, 91, 255);
    public Color forest = new Color32(94, 175, 92, 255);
    public Color dirt = new Color32(221, 173, 108, 255);
    public Color sand = new Color32(255, 222, 143, 255);
    public Color water = new Color32(80, 183, 225, 255);
    public Color mushroom = new Color32(79, 192, 180, 255);
    public Color darkMushroom = new Color32(57, 155, 154, 255);
    public Color voidGround = new Color32(180, 134, 216, 255);
    public Color darkVoid = new Color32(143, 105, 186, 255);
    public Color crimson = new Color32(239, 121, 132, 255);
    public Color darkCrimson = new Color32(209, 92, 115, 255);
    public Color fog = new Color32(234, 244, 250, 255);
    public Color fogEdge = new Color32(181, 207, 225, 255);

    public Color32 Ground(int terrain)
    {
        switch (terrain)
        {
            case PlanetTileset.TerrainDirt: return dirt;
            case PlanetTileset.TerrainSand: return sand;
            case PlanetTileset.TerrainDarkGrass: return forest;
            case PlanetTileset.TerrainVoid: return voidGround;
            case PlanetTileset.TerrainDarkVoid: return darkVoid;
            case PlanetTileset.TerrainCrimson: return crimson;
            case PlanetTileset.TerrainDarkCrimson: return darkCrimson;
            case PlanetTileset.TerrainGlowingMushroom: return mushroom;
            case PlanetTileset.TerrainDarkGlowingMushroom: return darkMushroom;
            default: return grass;
        }
    }
}

/// <summary>Allocation-free after warm-up. Colors are cached per cell; no source texture reads.</summary>
public sealed class MiniMapRaster : IDisposable
{
    public Texture2D Texture { get; }
    readonly int _size;
    readonly Color32[] _pixels;
    readonly byte[] _fog;
    readonly Vector3[] _projection;
    Color32[] _cells;
    byte[] _stages;
    PlanetTileMap _map;
    PlanetStageProgression _progress;
    int _version = -1;
    float _radius = -1f, _viewRadius = -1f;

    public MiniMapRaster(int resolution)
    {
        _size = resolution;
        _pixels = new Color32[_size * _size];
        _fog = new byte[_pixels.Length];
        _projection = new Vector3[_pixels.Length];
        Texture = new Texture2D(_size, _size, TextureFormat.RGBA32, false, false)
        { name = "MiniMap terrain (runtime)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
    }

    public void Invalidate() { _version = -1; }

    /// <summary>Disc coordinates in [-1, 1]. Reuse the baked cloud mask for marker occlusion.</summary>
    public bool IsFogAt(Vector2 point)
    {
        int x = Mathf.Clamp((int)((point.x * .5f + .5f) * _size), 0, _size - 1);
        int y = Mathf.Clamp((int)((point.y * .5f + .5f) * _size), 0, _size - 1);
        return _fog[y * _size + x] != 0;
    }

    public void Bake(PlanetTileMap map, PlanetStageProgression progress, int fogMask, MiniMapPalette palette,
        Vector3 center, Vector3 up, Vector3 right, Vector3 forward, float radius, float viewRadius)
    {
        CacheCells(map, progress, palette);
        CacheProjection(radius, viewRadius);
        int columns = map.LongitudeBands, rows = map.LatitudeBands;
        for (int i = 0; i < _pixels.Length; i++)
        {
            Vector3 p = _projection[i];
            Vector3 direction = up * p.z + right * p.x + forward * p.y;
            // Same world-aligned grid as PlanetTileMap.WorldToCell, retaining the fractions
            // for rounded clouds that stay attached to the world when the player moves.
            float latitude = (Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) / Mathf.PI + .5f) * rows;
            float longitude = Mathf.Atan2(direction.z, direction.x) / (2f * Mathf.PI);
            if (longitude < 0f) longitude += 1f;
            longitude *= columns;
            int lat = Mathf.Min((int)latitude, rows - 1), lon = Mathf.Min((int)longitude, columns - 1);
            int cell = lat * columns + lon;
            _pixels[i] = _cells[cell];
            _fog[i] = (byte)(Covered(latitude, longitude, lat, lon, rows, columns, fogMask) ? 1 : 0);
        }
        // Opaque cloud fill with a single clean blue edge. No terrain can bleed through it.
        Color32 fog = palette.fog, edge = palette.fogEdge;
        fog.a = edge.a = 255;
        for (int y = 0; y < _size; y++)
        for (int x = 0; x < _size; x++)
        {
            int i = y * _size + x;
            if (_fog[i] == 0) continue;
            bool border = _fog[y * _size + Mathf.Max(0, x - 2)] == 0
                || _fog[y * _size + Mathf.Min(_size - 1, x + 2)] == 0
                || _fog[Mathf.Max(0, y - 2) * _size + x] == 0
                || _fog[Mathf.Min(_size - 1, y + 2) * _size + x] == 0;
            _pixels[i] = border ? edge : fog;
        }
        Texture.SetPixels32(_pixels);
        Texture.Apply(false, false);
    }

    bool Covered(float latitude, float longitude, int lat, int lon, int rows, int columns, int mask)
    {
        if ((mask & (1 << _stages[lat * columns + lon])) != 0) return true;
        // A rounded bank around each covered cell, similar to the world's cloud quilt.
        // Radius covers every corner of locked land; never smooth holes into hidden stages.
        const float radiusSquared = .8f * .8f;
        for (int dy = -1; dy <= 1; dy++)
        {
            int row = lat + dy;
            if (row < 0 || row >= rows) continue;
            float y = latitude - (row + .5f);
            for (int dx = -1; dx <= 1; dx++)
            {
                float x = longitude - (lon + dx + .5f);
                if (x * x + y * y > radiusSquared) continue;
                int column = (lon + dx + columns) % columns;
                if ((mask & (1 << _stages[row * columns + column])) != 0) return true;
            }
        }
        return false;
    }

    void CacheCells(PlanetTileMap map, PlanetStageProgression progress, MiniMapPalette palette)
    {
        if (_map == map && _progress == progress && _version == map.SurfaceVersion) return;
        int count = map.LatitudeBands * map.LongitudeBands;
        if (_cells == null || _cells.Length != count) { _cells = new Color32[count]; _stages = new byte[count]; }
        for (int lat = 0; lat < map.LatitudeBands; lat++)
        for (int lon = 0; lon < map.LongitudeBands; lon++)
        {
            int i = lat * map.LongitudeBands + lon;
            bool water = map.GetHeight(lat, lon) <= PlanetTileMap.WaterHeight;
            _cells[i] = water ? (Color32)palette.water : palette.Ground(map.GetTerrain(lat, lon));
            _cells[i].a = 255;
            // Match PlanetStageFog: ocean stays visible even when it inherits a locked stage.
            _stages[i] = water || progress == null ? (byte)0 : (byte)progress.StageAtCell(lat, lon);
        }
        _map = map; _progress = progress; _version = map.SurfaceVersion;
    }

    void CacheProjection(float radius, float viewRadius)
    {
        if (_radius == radius && _viewRadius == viewRadius) return;
        for (int y = 0; y < _size; y++)
        for (int x = 0; x < _size; x++)
        {
            float px = ((x + .5f) / _size - .5f) * viewRadius * 2f;
            float py = ((y + .5f) / _size - .5f) * viewRadius * 2f;
            float distance = Mathf.Sqrt(px * px + py * py);
            float angle = distance / Mathf.Max(1f, radius);
            float scale = distance > .0001f ? Mathf.Sin(angle) / distance : 0f;
            _projection[y * _size + x] = new Vector3(px * scale, py * scale, Mathf.Cos(angle));
        }
        _radius = radius; _viewRadius = viewRadius;
    }

    public void Dispose()
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(Texture);
        else UnityEngine.Object.DestroyImmediate(Texture);
    }
}
