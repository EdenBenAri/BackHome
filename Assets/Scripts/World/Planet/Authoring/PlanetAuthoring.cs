using System;
using UnityEngine;

/// <summary>
/// The planet's authoring root: edit terrain and placements on a 3D plane.
/// Activates spherical gameplay only after all authored poses have been projected.
/// No Update, per-frame deformation, runtime cloning or extra physics representation.
/// </summary>
[ExecuteAlways, DisallowMultipleComponent, SelectionBase, DefaultExecutionOrder(-10000)]
public sealed class PlanetAuthoring : MonoBehaviour
{
    [SerializeField, HideInInspector] SphericalPlanet runtimePlanet;
    [SerializeField, HideInInspector] Transform authoringRoot;
    [SerializeField, HideInInspector] Transform[] managedTargets = Array.Empty<Transform>();
    public SphericalPlanet RuntimePlanet => runtimePlanet;
    public Transform AuthoringRoot => authoringRoot;
    public PlanetTileMap Tiles => runtimePlanet != null ? runtimePlanet.GetComponent<PlanetTileMap>() : null;
    public float Radius => runtimePlanet != null ? runtimePlanet.Radius : 75f;
    public bool IsProjected { get; private set; }
    public double ProjectionMilliseconds { get; private set; }
    public double RuntimeStartupMilliseconds { get; private set; }
    public int ProjectedObjectCount { get; private set; }

#if UNITY_EDITOR
    public static event Action<PlanetAuthoring> PreviewRequested;
    public static event Action<PlanetAuthoring> PreviewReleased;
#endif

    public void Configure(SphericalPlanet planet, Transform authoring)
    {
        runtimePlanet = planet;
        authoringRoot = authoring;
    }

    public void RegisterTarget(Transform target)
    {
        Array.Resize(ref managedTargets, managedTargets.Length + 1);
        managedTargets[managedTargets.Length - 1] = target;
    }

    public void UnregisterTarget(Transform target)
    {
        managedTargets = Array.FindAll(managedTargets, item => item != null && item != target);
    }

    void OnEnable()
    {
        if (Application.IsPlaying(gameObject))
            ActivateRuntime();
#if UNITY_EDITOR
        else
            PreviewRequested?.Invoke(this);
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        PreviewReleased?.Invoke(this);
#endif
    }

    public bool ValidateConfiguration(out string reason)
    {
        reason = null;
        if (runtimePlanet == null || authoringRoot == null || Tiles == null || !Tiles.HasValidMap())
            reason = "The private runtime planet, authoring root or tile data is missing.";
        else if (runtimePlanet.transform.parent != transform || authoringRoot.parent != transform)
            reason = "The runtime snapshot and authoring root must be direct children of the world.";
        else if (!(Radius > 0f) || float.IsInfinity(Radius))
            reason = "Planet radius must be finite and positive.";
        else if ((transform.lossyScale - Vector3.one).sqrMagnitude > .000001f
            || (runtimePlanet.transform.localScale - Vector3.one).sqrMagnitude > .000001f
            || runtimePlanet.transform.localPosition.sqrMagnitude > .000001f
            || Quaternion.Angle(runtimePlanet.transform.localRotation, Quaternion.identity) > .001f
            || (authoringRoot.localScale - Vector3.one).sqrMagnitude > .000001f
            || authoringRoot.localPosition.sqrMagnitude > .000001f
            || Quaternion.Angle(authoringRoot.localRotation, Quaternion.identity) > .001f)
            reason = "Keep the world at unit scale and both Runtime Planet and Authoring Plane at their identity local transforms.";
        else
        {
            var targets = new System.Collections.Generic.HashSet<Transform>();
            foreach (var placement in authoringRoot.GetComponentsInChildren<PlanetPlacement>(true))
            {
                if (placement.RuntimeTarget == null || !placement.RuntimeTarget.IsChildOf(runtimePlanet.transform))
                {
                    reason = "A flat handle has no target in this world. Use Add Prop to create new objects.";
                    break;
                }
                if (!targets.Add(placement.RuntimeTarget))
                {
                    reason = "Two handles point to the same object. Use Duplicate Selected Prop instead of Ctrl+D.";
                    break;
                }
            }
        }
        return reason == null;
    }

    public bool ActivateRuntime()
    {
        if (IsProjected)
            return true;
        if (!Application.IsPlaying(gameObject))
            return false;
#if UNITY_EDITOR
        if (UnityEditor.EditorSettings.enterPlayModeOptionsEnabled
            && (UnityEditor.EditorSettings.enterPlayModeOptions & UnityEditor.EnterPlayModeOptions.DisableSceneReload) != 0)
        {
            Debug.LogError("Planet authoring requires Reload Scene enabled in Enter Play Mode Settings.", this);
            return false;
        }
#endif
        // Assembly reload during Play preserves the hierarchy but resets managed fields.
        if (runtimePlanet != null && runtimePlanet.gameObject.activeSelf
            && authoringRoot != null && !authoringRoot.gameObject.activeSelf)
        {
            IsProjected = true;
            return true;
        }
        if (!ValidateConfiguration(out string reason))
        {
            Debug.LogError("Nyxara: " + reason, this);
            return false;
        }
        foreach (var other in FindObjectsByType<SphericalPlanet>())
        {
            if (other != runtimePlanet && other.isActiveAndEnabled)
            {
                Debug.LogError("Nyxara requires its own scene. Another spherical planet is already active.", this);
                return false;
            }
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var placements = authoringRoot.GetComponentsInChildren<PlanetPlacement>(true);
        // Deleted/disabled authoring handles must not leave invisible gameplay objects behind.
        foreach (var target in managedTargets)
            if (target != null)
                target.gameObject.SetActive(false);
        foreach (var placement in placements)
            placement.Apply(this);
        authoringRoot.gameObject.SetActive(false);
        ProjectionMilliseconds = watch.Elapsed.TotalMilliseconds;
        runtimePlanet.gameObject.SetActive(true);
        IsProjected = true;
        ProjectedObjectCount = placements.Length;
        RuntimeStartupMilliseconds = watch.Elapsed.TotalMilliseconds;
    #if UNITY_EDITOR
        Debug.Log($"Nyxara: projected {placements.Length} placements in {ProjectionMilliseconds:F2} ms; total activation {RuntimeStartupMilliseconds:F2} ms including runtime startup.", this);
    #endif
        return true;
    }
}
