using UnityEngine;

/// <summary>
/// Marks a rigid decoration as one environment streaming unit.
/// Placement is authored on the plane and projected once by PlanetAuthoring; no per-frame snapping.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlanetEnvironmentObject : MonoBehaviour { }
