using UnityEngine;

/// <summary>
/// Concentric ocean sphere, independent of <see cref="PlanetTileMap"/>.
/// Visual only — no collider. Mesh comes from <see cref="CasualWaterSphereMesh"/>.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[DefaultExecutionOrder(-25)]
public sealed class NyxaraSeaFit : MonoBehaviour
{
    public const string RootName = "Sea";

    [SerializeField, Tooltip("World units above the planet surface so ground at height 0 sits just under the water.")]
    float clearance = 0.45f;

    static readonly int SeaCenterId = Shader.PropertyToID("_PlanetSeaCenter");
    static readonly int SeaParamsId = Shader.PropertyToID("_PlanetSeaParams");

    MeshRenderer _renderer;
    SphericalPlanet _planet;

    void OnEnable()
    {
        _planet = GetComponentInParent<SphericalPlanet>();
        PlanetLayout.StripLegacyArt(_planet);
        Fit();
    }

    void OnDisable()
    {
        // Ground shading must come back in scenes that have no sea.
        Shader.SetGlobalVector(SeaParamsId, Vector4.zero);
    }

    void LateUpdate()
    {
        if (!Application.isPlaying || _planet == null)
            return;

        PushSeaGlobals();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!isActiveAndEnabled)
            return;

        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && isActiveAndEnabled)
                Fit();
        };
    }
#endif

    public void Fit()
    {
        var planet = GetComponentInParent<SphericalPlanet>();
        if (planet == null)
            return;

        _planet = planet;
        DisablePhysics();

        if (_renderer == null)
            _renderer = GetComponent<MeshRenderer>();
        if (_renderer != null)
            _renderer.enabled = true;

        float worldRadius = planet.GetTerrainRadius(Vector3.up) + Mathf.Max(0f, clearance);
        float parentScale = Mathf.Max(0.0001f, planet.transform.lossyScale.x);
        float localRadius = worldRadius / parentScale;
        float localScale = localRadius / CasualWaterSphereMesh.UnitySphereRadius;

        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one * localScale;

        var sphere = GetComponent<CasualWaterSphereMesh>();
        if (sphere != null)
            sphere.Refresh();

        PushSeaGlobals();
    }

    void PushSeaGlobals()
    {
        if (_planet == null)
            _planet = GetComponentInParent<SphericalPlanet>();
        if (_planet == null)
            return;

        if (_renderer == null)
            _renderer = GetComponent<MeshRenderer>();
        if (_renderer == null || !_renderer.enabled)
        {
            Shader.SetGlobalVector(SeaParamsId, Vector4.zero);
            return;
        }

        float wave = 0.16f;
        Material material = _renderer != null ? _renderer.sharedMaterial : null;
        if (material != null && material.HasProperty("_WaveHeight"))
            wave = material.GetFloat("_WaveHeight");

        // Stay above the tallest wave so a trough never reveals an unshaded seabed.
        float margin = Mathf.Max(0.05f, wave) + 0.1f;
        float worldRadius = _planet.GetTerrainRadius(Vector3.up) + Mathf.Max(0f, clearance);
        Shader.SetGlobalVector(SeaCenterId, _planet.Center);
        Shader.SetGlobalVector(SeaParamsId, new Vector4(worldRadius, margin, 0f, 0f));
    }

    void DisablePhysics()
    {
        var sphere = GetComponent<SphereCollider>();
        if (sphere != null)
            sphere.enabled = false;
        var meshCol = GetComponent<MeshCollider>();
        if (meshCol != null)
            meshCol.enabled = false;
    }
}
