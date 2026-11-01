using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A quilt of opaque, gently breathing cloud banks over locked land. Geometry is
/// built once, batched by stage and spatial chunk, and released as stages open.
/// No particles, physics, texture reads, scene-depth sampling or idle CPU updates.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlanetStageProgression), typeof(PlanetTileMap))]
[DefaultExecutionOrder(-8000)]
public sealed class PlanetStageFog : MonoBehaviour
{
    [SerializeField] Material cloudMaterial;
    [SerializeField, Range(.5f, 4f)] float cloudHeight = 2.1f;
    [Tooltip("Extra width of the rounded clouds covering the terrain's blended stage borders.")]
    [SerializeField, Range(0f, 1f)] float edgePadding = .7f;
    [Tooltip("Small gap between the player's body and the visible cloud edge, in world units.")]
    [SerializeField, Range(0f, 1f)] float movementClearance = .35f;
    [SerializeField, Range(.1f, 2f)] float revealDuration = .85f;

    // At most 64 cells per renderer: spatial culling without a renderer per puff.
    const int ChunkSize = 8;
    const int Slices = 12;
    const int Rings = 4;
    static readonly int RevealId = Shader.PropertyToID("_Reveal");
    static readonly int BobHeightId = Shader.PropertyToID("_BobHeight");

    sealed class Bank
    {
        public GameObject Root;
        public Material Material;
        public readonly List<Mesh> Meshes = new();
        public int[] FootprintHeads;
        public readonly List<Footprint> Footprints = new();
    }

    struct Footprint
    {
        public Vector3 Center, East, North, Up;
        public float Width, Depth, PlaneRadius;
        public int Cell, Next;

        public float Clearance(Vector3 direction, float bodyClearance)
        {
            float facing = Vector3.Dot(direction, Up);
            if (facing <= .001f) return float.PositiveInfinity;
            // Intersect the radial ray with the same tangent plane used to build the dome.
            Vector3 offset = direction * (PlaneRadius / facing) - Center;
            float x = Vector3.Dot(offset, East) / Width;
            float y = Vector3.Dot(offset, North) / Depth;
            // Scaling by the smaller radius conservatively includes the whole player body.
            return (Mathf.Sqrt(x * x + y * y) - 1f) * Mathf.Min(Width, Depth) - bodyClearance;
        }
    }

    readonly Bank[] _banks = new Bank[5];
    PlanetStageProgression _progress;
    PlanetTileMap _tiles;
    SphericalPlanet _planet;
    float _maxFootprintAngle, _maxFootprintAspect, _minFootprintRadius;

    public int CoveredCellCount { get; private set; }
    public int BuiltVertexCount { get; private set; }
    public int BuiltChunkCount { get; private set; }

    void OnEnable()
    {
        if (Application.isPlaying) Rebuild();
    }

    /// <summary>Recreate visual resources after changing fog settings; never called each frame.</summary>
    public void Rebuild()
    {
        Release();
        _progress = GetComponent<PlanetStageProgression>();
        _tiles = GetComponent<PlanetTileMap>();
        _planet = GetComponent<SphericalPlanet>();
        _progress.Initialize();
        CoveredCellCount = BuiltVertexCount = BuiltChunkCount = 0;
        _maxFootprintAngle = 0f;
        _maxFootprintAspect = 1f;
        _minFootprintRadius = float.PositiveInfinity;
        if (!_tiles.HasValidMap() || cloudMaterial == null)
        {
            if (cloudMaterial == null) Debug.LogWarning("Stage fog needs its cloud material.", this);
            return;
        }

        for (int stage = 2; stage <= 4; stage++)
        {
            if (_progress.IsUnlocked((PlanetStageId)stage)) continue;
            BuildStage(stage);
        }
        _progress.StageUnlocked += OnStageUnlocked;
    }

    void BuildStage(int stage)
    {
        var builder = new CloudMeshBuilder(transform, cloudHeight, edgePadding);
        for (int row = 0; row < _tiles.LatitudeBands; row += ChunkSize)
        for (int col = 0; col < _tiles.LongitudeBands; col += ChunkSize)
        {
            builder.Clear();
            for (int lat = row; lat < Mathf.Min(row + ChunkSize, _tiles.LatitudeBands); lat++)
            for (int lon = col; lon < Mathf.Min(col + ChunkSize, _tiles.LongitudeBands); lon++)
            {
                // Open ocean has inherited stage ownership for movement, but is not
                // a locked level. Keep it visible instead of blanketing the globe.
                if (_tiles.GetHeight(lat, lon) <= PlanetTileMap.WaterHeight
                    || (int)_progress.StageAtCell(lat, lon) != stage) continue;
                builder.AddCell(_tiles, _planet, lat, lon);
                CoveredCellCount++;
            }
            if (builder.VertexCount == 0) continue;
            Bank bank = _banks[stage];
            if (bank == null)
            {
                bank = _banks[stage] = new Bank
                {
                    Root = new GameObject("Locked Stage " + (PlanetStageId)stage + " Clouds")
                        { hideFlags = HideFlags.DontSave, layer = gameObject.layer },
                    Material = new Material(cloudMaterial)
                        { name = "Stage " + (PlanetStageId)stage + " Clouds (runtime)", hideFlags = HideFlags.DontSave },
                    FootprintHeads = new int[_tiles.LatitudeBands * _tiles.LongitudeBands]
                };
                for (int i = 0; i < bank.FootprintHeads.Length; i++) bank.FootprintHeads[i] = -1;
                bank.Root.transform.SetParent(transform, false);
                bank.Material.SetFloat(RevealId, 0f);
            }
            foreach (Footprint generated in builder.Footprints)
            {
                Footprint footprint = generated;
                footprint.Next = bank.FootprintHeads[footprint.Cell];
                bank.FootprintHeads[footprint.Cell] = bank.Footprints.Count;
                bank.Footprints.Add(footprint);
                float size = Mathf.Max(footprint.Width, footprint.Depth);
                float offset = (footprint.Center - footprint.Up * footprint.PlaneRadius).magnitude;
                _maxFootprintAngle = Mathf.Max(_maxFootprintAngle, Mathf.Atan2(offset + size, footprint.PlaneRadius));
                _maxFootprintAspect = Mathf.Max(_maxFootprintAspect, size / Mathf.Min(footprint.Width, footprint.Depth));
                _minFootprintRadius = Mathf.Min(_minFootprintRadius, footprint.PlaneRadius);
            }
            float padding = Mathf.Abs(bank.Material.GetFloat(BobHeightId)) + .05f;
            Mesh mesh = builder.CreateMesh(padding);
            bank.Meshes.Add(mesh);
            var chunk = new GameObject("Cloud bank " + row + "," + col,
                typeof(MeshFilter), typeof(MeshRenderer))
                { hideFlags = HideFlags.DontSave, layer = gameObject.layer };
            chunk.transform.SetParent(bank.Root.transform, false);
            chunk.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = chunk.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = bank.Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            BuiltVertexCount += mesh.vertexCount;
            BuiltChunkCount++;
        }
    }

    /// <summary>
    /// Positive outside the rounded cloud footprints, negative inside. Queries only nearby
    /// source cells; no physics shapes, allocations, mesh reads or per-frame rebuilds.
    /// A revealing bank keeps its boundary until its clouds have disappeared.
    /// </summary>
    public float GetMovementClearance(Vector3 worldPosition, float bodyRadius = 0f)
    {
        if (_tiles == null || (_banks[2] == null && _banks[3] == null && _banks[4] == null))
            return float.PositiveInfinity;
        Vector3 direction = (worldPosition - _planet.Center).normalized;
        float latitude = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f));
        float longitude = Mathf.Atan2(direction.z, direction.x);
        if (longitude < 0f) longitude += 2f * Mathf.PI;
        int rows = _tiles.LatitudeBands, columns = _tiles.LongitudeBands;
        int row = Mathf.Clamp((int)((latitude / Mathf.PI + .5f) * rows), 0, rows - 1);
        int col = Mathf.Min((int)(longitude / (2f * Mathf.PI) * columns), columns - 1);
        float clearance = Mathf.Max(0f, bodyRadius) + movementClearance;
        float angle = _maxFootprintAngle + Mathf.Atan2(clearance * _maxFootprintAspect, _minFootprintRadius);
        int rowRange = Mathf.CeilToInt(angle * rows / Mathf.PI) + 1;
        float longitudeAngle = Mathf.Abs(latitude) + angle >= Mathf.PI * .5f ? Mathf.PI
            : Mathf.Asin(Mathf.Clamp01(Mathf.Sin(angle) / Mathf.Cos(latitude)));
        int colRange = Mathf.CeilToInt(longitudeAngle * columns / (2f * Mathf.PI)) + 1;
        int colCount = Mathf.Min(columns, colRange * 2 + 1);
        float nearest = float.PositiveInfinity;
        for (int stage = 2; stage <= 4; stage++)
        {
            Bank bank = _banks[stage];
            if (bank == null) continue;
            for (int lat = Mathf.Max(0, row - rowRange); lat <= Mathf.Min(rows - 1, row + rowRange); lat++)
            for (int x = 0; x < colCount; x++)
            {
                int lon = (col - colRange + x + columns) % columns;
                for (int index = bank.FootprintHeads[lat * columns + lon]; index >= 0;)
                {
                    Footprint footprint = bank.Footprints[index];
                    nearest = Mathf.Min(nearest, footprint.Clearance(direction, clearance));
                    index = footprint.Next;
                }
            }
        }
        return nearest;
    }

    void OnStageUnlocked(PlanetStageId stage)
    {
        if (_banks[(int)stage] == null) return;
        if (!Application.isPlaying || !isActiveAndEnabled) ReleaseBank((int)stage);
        else StartCoroutine(Reveal((int)stage));
    }

    IEnumerator Reveal(int stage)
    {
        Bank bank = _banks[stage];
        float elapsed = 0f;
        while (elapsed < revealDuration)
        {
            elapsed += Time.deltaTime;
            bank.Material.SetFloat(RevealId, Mathf.Clamp01(elapsed / Mathf.Max(.01f, revealDuration)));
            yield return null;
        }
        ReleaseBank(stage);
    }

    void OnDisable() => Release();
    void OnDestroy() => Release();

    void Release()
    {
        StopAllCoroutines();
        if (_progress != null) _progress.StageUnlocked -= OnStageUnlocked;
        for (int i = 2; i <= 4; i++) ReleaseBank(i);
    }

    void ReleaseBank(int stage)
    {
        Bank bank = _banks[stage];
        if (bank == null) return;
        bank.Root.SetActive(false);
        Dispose(bank.Root);
        foreach (Mesh mesh in bank.Meshes) Dispose(mesh);
        Dispose(bank.Material);
        _banks[stage] = null;
    }

    static void Dispose(Object value)
    {
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }

    sealed class CloudMeshBuilder
    {
        readonly Transform _owner;
        readonly float _height;
        readonly float _edgePadding;
        readonly List<Vector3> _vertices = new();
        readonly List<Vector3> _centers = new();
        readonly List<Color32> _colors = new();
        readonly List<int> _indices = new();
        public readonly List<Footprint> Footprints = new();
        public int VertexCount => _vertices.Count;

        public CloudMeshBuilder(Transform owner, float height, float edgePadding)
        {
            _owner = owner;
            _height = height;
            _edgePadding = Mathf.Clamp01(edgePadding);
        }
        public void Clear() { _vertices.Clear(); _centers.Clear(); _colors.Clear(); _indices.Clear(); Footprints.Clear(); }

        public void AddCell(PlanetTileMap map, SphericalPlanet planet, int lat, int lon)
        {
            float latAngle = (-90f + (lat + .5f) * 180f / map.LatitudeBands) * Mathf.Deg2Rad;
            float lonAngle = (lon + .5f) * 2f * Mathf.PI / map.LongitudeBands;
            Vector3 up = new(Mathf.Cos(latAngle) * Mathf.Cos(lonAngle), Mathf.Sin(latAngle),
                Mathf.Cos(latAngle) * Mathf.Sin(lonAngle));
            Vector3 east = new(-Mathf.Sin(lonAngle), 0f, Mathf.Cos(lonAngle));
            Vector3 north = Vector3.Cross(east, up);
            float surface = map.GetWalkSurfaceRadius(up);
            float width = Mathf.Max(.15f, surface * 2f * Mathf.PI / map.LongitudeBands * Mathf.Cos(latAngle));
            float depth = surface * Mathf.PI / map.LatitudeBands;
            // A local hash preserves UnityEngine.Random's gameplay state and repeatable art.
            float seed = Hash(lat * map.LongitudeBands + lon);
            // Terrain splat colors live at the cell's south-west corner and blend into
            // its neighbors. Bias the cloud toward that transition, retaining some jitter.
            Vector3 center = planet.Center + up * surface
                + east * (((seed - .5f) * .24f - .25f) * width)
                + north * (((Hash(lon * 157 + lat + 719) - .5f) * .24f - .25f) * depth);
            float height = _height * Mathf.Lerp(.8f, 1.55f, seed);
            // Keep the main dome aligned to the cell: rotating narrow polar cells can
            // expose their long edges. Offset/rotate only the decorative crown.
            Quaternion turn = Quaternion.AngleAxis(seed * 180f, up);
            Vector3 axis = turn * east, across = turn * north;
            float spread = 1f + _edgePadding;
            int cell = lat * map.LongitudeBands + lon;
            AddPuff(map, planet, cell, center, east, up, north,
                new Vector3(width * .92f * spread, height, depth * .88f * spread), seed);
            // A smaller, offset crown breaks the regular tile rhythm without objects/particles.
            if (seed > .7f)
                AddPuff(map, planet, cell, center + axis * width * .4f + across * depth * .2f,
                    axis, up, across, new Vector3(width * .55f * spread, height * .9f, depth * .52f * spread), 1f - seed);
        }

        void AddPuff(PlanetTileMap map, SphericalPlanet planet, int cell, Vector3 center,
            Vector3 east, Vector3 up, Vector3 north, Vector3 size, float seed)
        {
            int start = _vertices.Count;
            Footprints.Add(new Footprint
            {
                Cell = cell, Center = center - planet.Center, East = east, North = north, Up = up,
                Width = size.x, Depth = size.z, PlaneRadius = Vector3.Dot(center - planet.Center, up)
            });
            Vector3 localCenter = _owner.InverseTransformPoint(center);
            // A stable fake sky/key light gives every side of the spherical world the same
            // friendly look. Lighting is baked per vertex; the fragment shader just blends colors.
            Vector3 light = (up + east * -.45f + north * .35f).normalized;
            AddVertex(Vector3.up);
            // Only the visible dome and an embedded skirt; no hidden lower hemisphere,
            // duplicate pole vertices, duplicate seam vertices or degenerate triangles.
            for (int ring = 1; ring <= Rings; ring++)
            {
                float angle = ring * 2.05f / Rings;
                for (int slice = 0; slice < Slices; slice++)
                {
                    float around = slice * 2f * Mathf.PI / Slices;
                    Vector3 sphere = new(Mathf.Sin(angle) * Mathf.Cos(around), Mathf.Cos(angle),
                        Mathf.Sin(angle) * Mathf.Sin(around));
                    AddVertex(sphere);
                    int a = start + 1 + (ring - 1) * Slices + slice;
                    int b = start + 1 + (ring - 1) * Slices + (slice + 1) % Slices;
                    if (ring == 1)
                    {
                        _indices.Add(start); _indices.Add(b); _indices.Add(a);
                    }
                    else
                    {
                        _indices.Add(a - Slices); _indices.Add(b - Slices); _indices.Add(a);
                        _indices.Add(b - Slices); _indices.Add(b); _indices.Add(a);
                    }
                }
            }
            void AddVertex(Vector3 sphere)
            {
                Vector3 offset = east * (sphere.x * size.x) + north * (sphere.z * size.z);
                Vector3 radial = (center + offset - planet.Center).normalized;
                // Drape each rounded dome over the actual curved ground. The last ring
                // is buried below it, including during breathing, so no flat apron or
                // floating cut edge is visible. Surface sampling happens only at build.
                float lift = _height * .15f + sphere.y * size.y;
                if (sphere.y < 0f) lift = Mathf.Min(lift, -.35f);
                float radius = map.GetWalkSurfaceRadius(radial) + lift;
                Vector3 normal = east * sphere.x + up * sphere.y + north * sphere.z;
                float shade = Mathf.Clamp01(Vector3.Dot(normal, light) * .65f + .35f);
                _vertices.Add(_owner.InverseTransformPoint(planet.Center + radial * radius));
                _centers.Add(localCenter);
                // R: baked lighting; G: cloud phase; B: top highlight; A: unused.
                _colors.Add((Color32)new Color(shade, seed, Mathf.Clamp01(sphere.y), 1f));
            }
        }

        public Mesh CreateMesh(float padding)
        {
            var mesh = new Mesh { name = "Stage cloud bank", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(_vertices);
            mesh.SetColors(_colors);
            mesh.SetUVs(0, _centers);
            mesh.SetTriangles(_indices, 0);
            mesh.RecalculateBounds();
            Bounds bounds = mesh.bounds;
            bounds.Expand(padding * 2f);
            mesh.bounds = bounds;
            mesh.UploadMeshData(true);
            return mesh;
        }

        static float Hash(int value)
        {
            uint hash = unchecked((uint)value * 747796405u + 2891336453u);
            hash = ((hash >> (int)((hash >> 28) + 4)) ^ hash) * 277803737u;
            return ((hash >> 22) ^ hash) / (float)uint.MaxValue;
        }
    }
}
