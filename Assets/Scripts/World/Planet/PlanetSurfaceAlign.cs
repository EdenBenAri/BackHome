using UnityEngine;

/// <summary>
/// Keeps a prop's up-axis aligned to spherical planet curvature.
/// Does NOT auto-move on Play — position is only changed when you explicitly
/// snap, drag in the editor, or enable alignEveryFrame.
/// </summary>
[DisallowMultipleComponent]
public class PlanetSurfaceAlign : MonoBehaviour
{
    [SerializeField] SphericalPlanet planet;
    [SerializeField] float hover = PlanetSurfacePose.DefaultHover;
    [SerializeField] float yaw;
    [Tooltip("If enabled, re-snap every LateUpdate (runtime). Off by default for static props.")]
    [SerializeField] bool alignEveryFrame;

    PlanetTileMap _tiles;
    bool _snapping;

    public void Configure(SphericalPlanet targetPlanet, float yawDegrees, float hoverOffset)
    {
        planet = targetPlanet;
        yaw = yawDegrees;
        hover = Mathf.Max(0f, hoverOffset);
        CacheTiles();
        SnapToSurface(recordUndo: false);
    }

    /// <summary>
    /// Sit on the walk surface at <paramref name="groundHover"/>. Nearby props do not add height.
    /// </summary>
    public void DropToGround(float groundHover, bool recordUndo = true)
    {
        hover = Mathf.Max(0f, groundHover);
        SnapToSurface(recordUndo);
    }

    void OnValidate()
    {
        hover = Mathf.Max(0f, hover);
    }

    void LateUpdate()
    {
        if (!alignEveryFrame || !Application.isPlaying)
            return;
        SnapToSurface(recordUndo: false);
    }

    void CacheTiles()
    {
        if (planet == null)
            PlanetSurfacePose.TryResolvePlanet(transform, out planet, out _tiles);
        else
            _tiles = planet.GetComponent<PlanetTileMap>();
    }

    /// <summary>
    /// Snap using current world position as a direction hint (keeps yaw + hover).
    /// </summary>
    public void SnapToSurface(bool recordUndo = true)
    {
        if (_snapping)
            return;

        CacheTiles();
        if (planet == null)
            return;

        _snapping = true;
        try
        {
            if (!PlanetSurfacePose.TryGetPoseFromWorldPoint(
                    planet,
                    _tiles,
                    transform.position,
                    yaw,
                    hover,
                    out Vector3 position,
                    out Quaternion rotation,
                    out _))
            {
                return;
            }

#if UNITY_EDITOR
            if (recordUndo && !Application.isPlaying)
                UnityEditor.Undo.RecordObject(transform, "Snap Prop To Planet Surface");
#endif
            transform.SetPositionAndRotation(position, rotation);
        }
        finally
        {
            _snapping = false;
        }
    }

    /// <summary>
    /// Align rotation only — keep the current distance from planet center (height).
    /// </summary>
    public void AlignRotationOnly(bool recordUndo = true)
    {
        if (_snapping)
            return;

        CacheTiles();
        if (planet == null)
            return;

        Vector3 radial = transform.position - planet.Center;
        if (radial.sqrMagnitude < 0.0001f)
            return;

        Vector3 up = radial.normalized;
        // Prefer terrain/walk normal when available, but keep radius unchanged.
        if (_tiles != null && _tiles.ProvidesWalkSurface)
            up = _tiles.GetWalkSurfaceNormal(radial);
        else
            up = planet.GetTerrainNormal(radial);

        if (Vector3.Dot(up, radial) < 0f)
            up = -up;

        Quaternion rotation = PlanetSurfacePose.RotationFromUp(up, yaw);

        _snapping = true;
        try
        {
#if UNITY_EDITOR
            if (recordUndo && !Application.isPlaying)
                UnityEditor.Undo.RecordObject(transform, "Align Prop Rotation");
#endif
            transform.rotation = rotation;
        }
        finally
        {
            _snapping = false;
        }
    }

    /// <summary>
    /// Editor drag: keep yaw, then stick to the ground. Hover stays as set —
    /// sliding past another prop does not lift this one.
    /// </summary>
    public void EditorHandleMoved()
    {
        if (_snapping)
            return;

        CacheTiles();
        if (planet == null)
            return;

        Vector3 radial = transform.position - planet.Center;
        if (radial.sqrMagnitude > 0.0001f)
        {
            Vector3 upHint = radial.normalized;
            float extracted = PlanetSurfacePose.ExtractYaw(transform.rotation, upHint);
            if (!Mathf.Approximately(extracted, yaw))
                yaw = extracted;
        }

        SnapToSurface(recordUndo: true);
    }

    void OnDrawGizmosSelected()
    {
        if (planet == null)
            CacheTiles();
        if (planet == null)
            return;

        Vector3 up = planet.GetUpAt(transform.position);
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.85f);
        Gizmos.DrawRay(transform.position, up * 1.5f);
    }
}
