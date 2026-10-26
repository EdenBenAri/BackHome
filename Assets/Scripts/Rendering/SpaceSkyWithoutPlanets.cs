using UnityEngine;

/// <summary>
/// Drops planets and moons from the procedural space sky after it is composed.
/// Nebula, star field, the sun and the nearby sparkle stay.
/// </summary>
[DefaultExecutionOrder(200)]
[ExecuteAlways]
[DisallowMultipleComponent]
public class SpaceSkyWithoutPlanets : MonoBehaviour
{
    const int StarKind = 5;

    static readonly int PlanetAId = Shader.PropertyToID("_PlanetA");
    static readonly int PlanetBId = Shader.PropertyToID("_PlanetB");
    static readonly int PlanetCId = Shader.PropertyToID("_PlanetC");
    static readonly int PlanetDId = Shader.PropertyToID("_PlanetD");
    static readonly int PlanetEId = Shader.PropertyToID("_PlanetE");
    static readonly int PlanetColId = Shader.PropertyToID("_PlanetCol");
    static readonly int PlanetCol2Id = Shader.PropertyToID("_PlanetCol2");
    static readonly int PlanetCol3Id = Shader.PropertyToID("_PlanetCol3");
    static readonly int PlanetCountId = Shader.PropertyToID("_PlanetCount");
    static readonly int MoonCountId = Shader.PropertyToID("_MoonCount");

    MeshRenderer _renderer;
    MaterialPropertyBlock _block;

    void LateUpdate()
    {
        if (_renderer == null)
            _renderer = GetComponent<MeshRenderer>();
        if (_renderer == null)
            return;

        // The quad mesh is built at runtime and does not survive a scene save.
        // Rebuild puts it back, then the planet filter below runs on the new sky.
        var filter = GetComponent<MeshFilter>();
        var sky = GetComponent<BirichStudio.ProceduralSpace.SpaceBackground>();
        if (sky != null && (filter == null || filter.sharedMesh == null || _renderer.sharedMaterial == null))
            sky.Rebuild();

        _block ??= new MaterialPropertyBlock();
        _renderer.GetPropertyBlock(_block);

        Vector4[] a = _block.GetVectorArray(PlanetAId);
        Vector4[] b = _block.GetVectorArray(PlanetBId);
        Vector4[] c = _block.GetVectorArray(PlanetCId);
        Vector4[] d = _block.GetVectorArray(PlanetDId);
        Vector4[] e = _block.GetVectorArray(PlanetEId);
        Vector4[] col = _block.GetVectorArray(PlanetColId);
        Vector4[] col2 = _block.GetVectorArray(PlanetCol2Id);
        Vector4[] col3 = _block.GetVectorArray(PlanetCol3Id);
        if (a == null || b == null || c == null || d == null || e == null
            || col == null || col2 == null || col3 == null
            || a.Length == 0 || b.Length != a.Length)
            return;

        int count = Mathf.Min(a.Length, Mathf.RoundToInt(_block.GetFloat(PlanetCountId)));
        int write = 0;
        bool dropped = false;
        for (int i = 0; i < count; i++)
        {
            if (Mathf.RoundToInt(b[i].w) != StarKind)
            {
                dropped = true;
                continue;
            }

            if (write != i)
            {
                a[write] = a[i];
                b[write] = b[i];
                c[write] = c[i];
                d[write] = d[i];
                e[write] = e[i];
                col[write] = col[i];
                col2[write] = col2[i];
                col3[write] = col3[i];
            }

            write++;
        }

        int moons = Mathf.RoundToInt(_block.GetFloat(MoonCountId));
        if (!dropped && moons == 0 && write == count)
            return;

        for (int i = write; i < a.Length; i++)
        {
            a[i] = Vector4.zero;
            b[i] = Vector4.zero;
            c[i] = Vector4.zero;
            d[i] = Vector4.zero;
            e[i] = Vector4.zero;
            col[i] = Vector4.zero;
            col2[i] = Vector4.zero;
            col3[i] = Vector4.zero;
        }

        _block.SetVectorArray(PlanetAId, a);
        _block.SetVectorArray(PlanetBId, b);
        _block.SetVectorArray(PlanetCId, c);
        _block.SetVectorArray(PlanetDId, d);
        _block.SetVectorArray(PlanetEId, e);
        _block.SetVectorArray(PlanetColId, col);
        _block.SetVectorArray(PlanetCol2Id, col2);
        _block.SetVectorArray(PlanetCol3Id, col3);
        _block.SetFloat(PlanetCountId, write);
        _block.SetFloat(MoonCountId, 0f);
        _renderer.SetPropertyBlock(_block);
    }
}
