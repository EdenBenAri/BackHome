using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Persistent, ordered stage access. Only configured bosses count, once per area.
/// Empty stages never complete themselves. Terrain and content share the same access rule.
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(SphericalPlanet))]
[DefaultExecutionOrder(-9000)]
public sealed class PlanetStageProgression : MonoBehaviour
{
    [SerializeField] string worldId = "ChapterOne";
    [SerializeField] bool persistProgress = true;

    [Serializable]
    sealed class SaveData
    {
        public int completedMask;
        public List<string> defeatedBosses = new();
    }

    readonly Dictionary<PlanetStageId, HashSet<string>> _requiredBosses = new();
    readonly HashSet<string> _defeatedBosses = new();
    PlanetStage[] _stages;
    SphericalPlanet _planet;
    PlanetTileMap _tiles;
    PlanetStageFog _fog;
    PlanetStageId[] _cellStages;
    int _completedMask;
    bool _initialized;
    static string _respawnSaveKey, _respawnScenePath, _respawnSave;

    public bool IsResumingAfterDeath { get; private set; }

#if UNITY_EDITOR
    // Each Play starts fresh, while death/restart and scene travel in that Play keep progress.
    // SubsystemRegistration also runs when Enter Play Mode has domain reload disabled.
    static readonly Dictionary<string, string> EditorPlaySaves = new();
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void BeginPlaySession()
    {
        _respawnSaveKey = _respawnScenePath = _respawnSave = null;
#if UNITY_EDITOR
        EditorPlaySaves.Clear();
#endif
    }

    public event Action<PlanetStageId> StageUnlocked;
    public event Action ProgressChanged;
    public string SaveKey => "BackHome.Stages.v1." + worldId;
    public PlanetStageId HighestUnlocked
    {
        get
        {
            int stage = 1;
            while (stage < 4 && IsComplete((PlanetStageId)stage)) stage++;
            return (PlanetStageId)stage;
        }
    }

    void Awake()
    {
        if (Application.isPlaying) Initialize();
    }

    /// <summary>Called after projection, before the runtime hierarchy is activated.</summary>
    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _planet = GetComponent<SphericalPlanet>();
        _tiles = GetComponent<PlanetTileMap>();
        _fog = GetComponent<PlanetStageFog>();
        BuildStageMap();
        Transform areas = transform.Find("Areas");
        _stages = areas != null ? areas.GetComponentsInChildren<PlanetStage>(true) : Array.Empty<PlanetStage>();
        IsResumingAfterDeath = _respawnSaveKey == SaveKey && _respawnScenePath == gameObject.scene.path;
        string savedJson = IsResumingAfterDeath ? _respawnSave : persistProgress ? ReadSave() : null;
        if (IsResumingAfterDeath) _respawnSaveKey = _respawnScenePath = _respawnSave = null;
        if (!string.IsNullOrEmpty(savedJson))
        {
            try
            {
                var saved = JsonUtility.FromJson<SaveData>(savedJson);
                if (saved != null)
                {
                    // Only a contiguous prefix is valid; corrupt data cannot skip stages.
                    for (int i = 0; i < 4 && (saved.completedMask & (1 << i)) != 0; i++)
                        _completedMask |= 1 << i;
                    if (saved.defeatedBosses != null)
                        foreach (string boss in saved.defeatedBosses)
                            if (!string.IsNullOrWhiteSpace(boss)) _defeatedBosses.Add(boss);
                }
            }
            catch (ArgumentException) { Debug.LogWarning("Could not read stage progress for " + worldId, this); }
        }
        ApplyStageAccess();
    }

    public bool IsUnlocked(PlanetStageId stage) => (int)stage >= 1 && (int)stage <= (int)HighestUnlocked;
    public bool IsComplete(PlanetStageId stage) => (int)stage >= 1 && (int)stage <= 4
        && (_completedMask & (1 << ((int)stage - 1))) != 0;

    static string BossKey(PlanetStageId stage, string areaId) => (int)stage + ":" + areaId.Trim();

    public bool HasDefeatedBoss(PlanetStageId stage, string areaId) => !string.IsNullOrWhiteSpace(areaId)
        && (IsComplete(stage) || _defeatedBosses.Contains(BossKey(stage, areaId)));

    public void RegisterBoss(PlanetStageId stage, string areaId)
    {
        if ((int)stage < 1 || (int)stage > 4 || string.IsNullOrWhiteSpace(areaId)) return;
        if (!_requiredBosses.TryGetValue(stage, out var bosses))
            _requiredBosses.Add(stage, bosses = new HashSet<string>());
        bosses.Add(BossKey(stage, areaId));
    }

    public int RequiredBossCount(PlanetStageId stage) => _requiredBosses.TryGetValue(stage, out var bosses) ? bosses.Count : 0;
    public int DefeatedBossCount(PlanetStageId stage)
    {
        int count = 0;
        if (_requiredBosses.TryGetValue(stage, out var bosses))
            foreach (string boss in bosses)
                if (_defeatedBosses.Contains(boss)) count++;
        return count;
    }

    public void RecordBossDefeat(PlanetStageId stage, string areaId)
    {
        if (!IsUnlocked(stage) || IsComplete(stage) || string.IsNullOrWhiteSpace(areaId)) return;
        string key = BossKey(stage, areaId);
        if (!_requiredBosses.TryGetValue(stage, out var bosses) || !bosses.Contains(key)
            || !_defeatedBosses.Add(key)) return;

        PlanetStageId previous = HighestUnlocked;
        if (bosses.Count > 0 && bosses.IsSubsetOf(_defeatedBosses))
            _completedMask |= 1 << ((int)stage - 1);
        Save();
        ApplyStageAccess();
        ProgressChanged?.Invoke();
        if (HighestUnlocked != previous) StageUnlocked?.Invoke(HighestUnlocked);
    }

    string ReadSave()
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
            return EditorPlaySaves.TryGetValue(SaveKey, out string saved) ? saved : null;
#endif
        return PlayerPrefs.GetString(SaveKey, "");
    }

    /// <summary>Carry this chapter through the death reload, including when disk saves are disabled.</summary>
    public void PrepareForRespawn()
    {
        Initialize();
        _respawnSaveKey = SaveKey;
        _respawnScenePath = gameObject.scene.path;
        _respawnSave = CreateSaveJson();
    }

    string CreateSaveJson() => JsonUtility.ToJson(new SaveData
    {
        completedMask = _completedMask,
        defeatedBosses = new List<string>(_defeatedBosses)
    });

    void Save()
    {
        if (!persistProgress) return;
        string saved = CreateSaveJson();
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            EditorPlaySaves[SaveKey] = saved;
            return;
        }
#endif
        PlayerPrefs.SetString(SaveKey, saved);
        PlayerPrefs.Save();
    }

    void ApplyStageAccess()
    {
        foreach (var stage in _stages)
            if (stage != null) stage.gameObject.SetActive(IsUnlocked(stage.Id));
    }

    public PlanetStageId StageAt(Vector3 worldPosition)
    {
        if (_tiles != null && _tiles.WorldToCell(worldPosition, out int lat, out int lon))
            return StageAtCell(lat, lon);
        return PlanetStageId.A;
    }

    /// <summary>Shared ownership for gameplay and fog, including roads and shoreline.</summary>
    public PlanetStageId StageAtCell(int latitude, int longitude)
    {
        if (_tiles == null || latitude < 0 || latitude >= _tiles.LatitudeBands
            || longitude < 0 || longitude >= _tiles.LongitudeBands) return PlanetStageId.A;
        return _cellStages != null ? _cellStages[latitude * _tiles.LongitudeBands + longitude]
            : PlanetStage.FromTerrain(_tiles.GetTerrain(latitude, longitude));
    }

    void BuildStageMap()
    {
        if (_tiles == null || !_tiles.HasValidMap()) return;
        int rows = _tiles.LatitudeBands, columns = _tiles.LongitudeBands;
        _cellStages = new PlanetStageId[rows * columns];
        var pending = new Queue<int>(_cellStages.Length);
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                int terrain = _tiles.GetTerrain(row, col);
                // Roads and shoreline inherit their nearest biome. Treating all dirt as A
                // would leave an unlocked corridor straight through later stages.
                if (terrain == PlanetTileset.TerrainDirt || terrain == PlanetTileset.TerrainSand
                    || _tiles.GetHeight(row, col) <= PlanetTileMap.WaterHeight) continue;
                int cell = row * columns + col;
                _cellStages[cell] = PlanetStage.FromTerrain(terrain);
                pending.Enqueue(cell);
            }
        }
        void Visit(int cell, PlanetStageId stage)
        {
            if ((int)_cellStages[cell] != 0) return;
            _cellStages[cell] = stage;
            pending.Enqueue(cell);
        }
        while (pending.Count > 0)
        {
            int cell = pending.Dequeue(), row = cell / columns, col = cell % columns;
            PlanetStageId stage = _cellStages[cell];
            Visit(row * columns + (col + columns - 1) % columns, stage);
            Visit(row * columns + (col + 1) % columns, stage);
            if (row > 0) Visit(cell - columns, stage);
            if (row + 1 < rows) Visit(cell + columns, stage);
        }
        for (int i = 0; i < _cellStages.Length; i++)
            if ((int)_cellStages[i] == 0) _cellStages[i] = PlanetStageId.A;
    }

    public PlanetStageId StageFor(Transform content)
    {
        var group = content.GetComponentInParent<PlanetStage>(true);
        return group != null ? group.Id : StageAt(content.position);
    }

    public bool IsPositionUnlocked(Vector3 worldPosition) => IsUnlocked(StageAt(worldPosition));

    /// <summary>Sweep the whole movement arc so a long frame cannot jump locked or unwalkable terrain.</summary>
    public Vector3 ConstrainMove(Vector3 from, Vector3 to, float bodyRadius = 0f)
    {
        if (_tiles == null || (to - from).sqrMagnitude < .0000001f)
            return to;
        Vector3 start = from - _planet.Center;
        Vector3 end = to - _planet.Center;
        if (start.sqrMagnitude < .0001f || end.sqrMagnitude < .0001f) return from;
        float distance = Vector3.Angle(start, end) * Mathf.Deg2Rad * Mathf.Max(start.magnitude, end.magnitude);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / .25f));
        Vector3 safe = from;
        float fogClearance = _fog != null ? _fog.GetMovementClearance(from, bodyRadius) : float.PositiveInfinity;
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            Vector3 candidate = _planet.Center + Vector3.Slerp(start.normalized, end.normalized, t)
                * Mathf.Lerp(start.magnitude, end.magnitude, t);
            if (!IsPositionUnlocked(candidate)
                || (_tiles.TryGetTile(candidate, out var tile) && !tile.walkable)) return safe;
            float nextClearance = _fog != null ? _fog.GetMovementClearance(candidate, bodyRadius) : float.PositiveInfinity;
            // A player already in the newly expanded edge can walk out, but cannot go
            // deeper. Normal movement stops before the body overlaps the visible fog.
            if (nextClearance < 0f && (fogClearance >= 0f || nextClearance < fogClearance - .0001f)) return safe;
            fogClearance = nextClearance;
            safe = candidate;
        }
        return to;
    }

    public static Vector3 ConstrainMove(SphericalPlanet planet, Vector3 from, Vector3 to, float bodyRadius = 0f)
    {
        return planet != null && planet.TryGetComponent<PlanetStageProgression>(out var progress)
            ? progress.ConstrainMove(from, to, bodyRadius) : to;
    }
}
