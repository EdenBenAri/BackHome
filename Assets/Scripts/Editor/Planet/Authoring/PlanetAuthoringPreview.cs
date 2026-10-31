using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Editor-only disposable views; never saved in a prefab, scene or player build.</summary>
[InitializeOnLoad]
public static class PlanetAuthoringPreview
{
    struct PickMesh
    {
        public MeshFilter filter;
        public GameObject handle;
    }

    sealed class View
    {
        public GameObject root;
        public Mesh mesh;
        public Vector3[] gridLines;
        public MeshCollider surface;
        public Mesh seaMesh;
        public Material ground;
        public Material sea;
        public readonly List<GameObject> props = new List<GameObject>();
        public readonly List<PickMesh> pickMeshes = new List<PickMesh>();
        public readonly Dictionary<Mesh, MeshCollider> pickColliders = new Dictionary<Mesh, MeshCollider>();
    }

    static readonly Dictionary<PlanetAuthoring, View> Views = new Dictionary<PlanetAuthoring, View>();
    static readonly HashSet<PlanetAuthoring> Pending = new HashSet<PlanetAuthoring>();

    static PlanetAuthoringPreview()
    {
        PlanetAuthoring.PreviewRequested += Queue;
        PlanetAuthoring.PreviewReleased += Release;
        Undo.undoRedoPerformed += RefreshAll;
        Selection.selectionChanged += SelectAuthoringObject;
        HandleUtility.pickGameObjectCustomPasses += PickGameObject;
        AssemblyReloadEvents.beforeAssemblyReload += ClearAll;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) ClearAll();
            if (state == PlayModeStateChange.EnteredEditMode) RefreshAll();
        };
        EditorApplication.delayCall += RefreshAll;
    }

    // Mesh children are disposable. Even a deep/double click must edit the saved handle.
    static void SelectAuthoringObject()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var selected = Selection.objects;
        bool changed = false;
        for (int i = 0; i < selected.Length; i++)
        {
            if (!(selected[i] is GameObject go)) continue;
            var authoringObject = GetAuthoringObject(go);
            if (authoringObject == go) continue;
            selected[i] = authoringObject;
            changed = true;
        }
        if (changed) Selection.objects = selected;
    }

    public static GameObject GetAuthoringObject(GameObject go)
    {
        if (go == null || (go.hideFlags & HideFlags.DontSaveInEditor) == 0) return go;
        var owner = go.GetComponentInParent<PlanetAuthoring>();
        if (owner == null) return go;
        var handle = go.GetComponentInParent<PlanetPlacement>();
        return handle != null ? handle.gameObject : owner.gameObject;
    }

    /// <summary>Hit only this world's editor terrain, regardless of props or water in front of it.</summary>
    public static bool Raycast(PlanetAuthoring owner, Ray ray, out RaycastHit hit)
    {
        hit = default;
        return owner != null && Views.TryGetValue(owner, out var view) && view.surface != null
            && view.surface.Raycast(ray, out hit, float.MaxValue);
    }

    public static void DrawGrid(PlanetAuthoring owner)
    {
        if (!Views.TryGetValue(owner, out var view) || view.mesh == null) return;
        if (view.gridLines == null)
        {
            // Cache once per terrain revision; repainting the grid allocates no geometry.
            Vector3[] vertices = view.mesh.vertices;
            view.gridLines = new Vector3[vertices.Length * 2];
            for (int start = 0; start < vertices.Length; start += 4)
            for (int corner = 0; corner < 4; corner++)
            {
                int edge = (start + corner) * 2;
                view.gridLines[edge] = vertices[start + corner] + Vector3.up * .06f;
                view.gridLines[edge + 1] = vertices[start + (corner + 1) % 4] + Vector3.up * .06f;
            }
        }
        using (new Handles.DrawingScope(new Color(1, 1, 1, .25f), owner.transform.localToWorldMatrix))
            Handles.DrawLines(view.gridLines);
    }

    static GameObject PickGameObject(Camera camera, int layers, Vector2 position,
        GameObject[] ignore, GameObject[] filter, out int materialIndex)
    {
        materialIndex = 0;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return null;
        Ray ray = camera.ScreenPointToRay(position);
        GameObject result = null;
        float nearest = float.MaxValue;
        foreach (var pair in Views)
        {
            if (pair.Key == null || !StageUtility.GetCurrentStageHandle().Contains(pair.Key.gameObject)) continue;
            var picked = Pick(pair.Key, ray, out float distance, layers, ignore, filter);
            if (picked != null && distance < nearest) { result = picked; nearest = distance; }
        }
        return result;
    }

    // Unity skips DontSave visuals during ordinary GPU picking. Supply exact mesh hits
    // through its editor picking extension, preserving normal selection/shift-click/tool handling.
    public static GameObject Pick(PlanetAuthoring owner, Ray ray, out float nearest,
        int layers = ~0, GameObject[] ignore = null, GameObject[] filter = null)
    {
        nearest = float.MaxValue;
        if (!Views.TryGetValue(owner, out var view)) return null;
        GameObject result = null;
        if (CanPick(owner.gameObject, layers, ignore, filter) && Raycast(owner, ray, out var ground))
        { nearest = ground.distance; result = owner.gameObject; }
        foreach (var entry in view.pickMeshes)
        {
            var meshFilter = entry.filter;
            if (meshFilter == null || !meshFilter.gameObject.activeInHierarchy || meshFilter.sharedMesh == null) continue;
            GameObject handle = entry.handle;
            if (!CanPick(handle, layers, ignore, filter)) continue;
            Matrix4x4 inverse = meshFilter.transform.worldToLocalMatrix;
            var localRay = new Ray(inverse.MultiplyPoint3x4(ray.origin), inverse.MultiplyVector(ray.direction).normalized);
            Mesh mesh = meshFilter.sharedMesh;
            if (!mesh.bounds.IntersectRay(localRay)) continue;
            // One lazily cooked query collider per shared mesh, not per placed object.
            // Only tested when a picking ray crosses the cheap local bounding box.
            if (!view.pickColliders.TryGetValue(mesh, out var collider))
            {
                var query = new GameObject("3D prop picking (editor only)") { hideFlags = HideFlags.HideAndDontSave };
                collider = query.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                view.pickColliders.Add(mesh, collider);
            }
            if (!collider.Raycast(localRay, out var hit, float.MaxValue)) continue;
            float distance = Vector3.Distance(ray.origin, meshFilter.transform.TransformPoint(hit.point));
            if (distance < nearest) { nearest = distance; result = handle; }
        }
        return result;
    }

    static bool CanPick(GameObject go, int layers, GameObject[] ignore, GameObject[] filter)
    {
        return go != null && go.activeInHierarchy && (layers & (1 << go.layer)) != 0
            && !SceneVisibilityManager.instance.IsHidden(go) && !SceneVisibilityManager.instance.IsPickingDisabled(go)
            && (ignore == null || System.Array.IndexOf(ignore, go) < 0)
            && (filter == null || System.Array.IndexOf(filter, go) >= 0);
    }

    public static void Queue(PlanetAuthoring owner)
    {
        if (owner == null || !Pending.Add(owner)) return;
        EditorApplication.delayCall += () => { Pending.Remove(owner); Rebuild(owner); };
    }

    public static void RefreshAll()
    {
        ClearAll();
        foreach (var owner in Resources.FindObjectsOfTypeAll<PlanetAuthoring>())
            Queue(owner);
    }

    public static void ClearAll()
    {
        foreach (var view in Views.Values) Dispose(view);
        Views.Clear();
    }

    static void Release(PlanetAuthoring owner)
    {
        if (!Views.TryGetValue(owner, out var view)) return;
        Views.Remove(owner);
        Dispose(view);
    }

    public static void QueueTerrain(PlanetAuthoring owner)
    {
        if (owner == null || !Pending.Add(owner)) return;
        EditorApplication.delayCall += () =>
        {
            Pending.Remove(owner);
            if (owner == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!Views.TryGetValue(owner, out var view) || view.root == null) { Rebuild(owner); return; }
            Mesh next = BuildMesh(owner);
            view.root.GetComponent<MeshFilter>().sharedMesh = next;
            view.surface.sharedMesh = next;
            Object.DestroyImmediate(view.mesh);
            view.mesh = next;
            view.gridLines = null;
            SceneView.RepaintAll();
        };
    }

    static void Dispose(View view)
    {
        foreach (var collider in view.pickColliders.Values)
            if (collider != null) Object.DestroyImmediate(collider.gameObject);
        foreach (var prop in view.props) if (prop != null) Object.DestroyImmediate(prop);
        if (view.root != null) Object.DestroyImmediate(view.root);
        if (view.mesh != null) Object.DestroyImmediate(view.mesh);
        if (view.seaMesh != null) Object.DestroyImmediate(view.seaMesh);
        if (view.ground != null) Object.DestroyImmediate(view.ground);
        if (view.sea != null) Object.DestroyImmediate(view.sea);
    }

    static GameObject PreviewObject(string name, Transform parent)
    {
        // Keep meshes pickable in Scene View, but never serialize them or include them in a build.
        var go = new GameObject(name) { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
        go.transform.SetParent(parent, false);
        return go;
    }

    public static void Rebuild(PlanetAuthoring owner)
    {
        if (owner == null || EditorUtility.IsPersistent(owner) || !owner.gameObject.scene.IsValid()
            || EditorApplication.isPlayingOrWillChangePlaymode || owner.RuntimePlanet == null
            || owner.AuthoringRoot == null || !owner.isActiveAndEnabled) return;
        if (Views.TryGetValue(owner, out var previous)) Dispose(previous);
        var map = owner.Tiles;
        if (map == null || !map.HasValidMap() || map.Tileset == null) return;
        var view = new View();
        Views[owner] = view;
        view.root = PreviewObject("3D Authoring Plane (editor only)", owner.AuthoringRoot);
        view.mesh = BuildMesh(owner);
        view.ground = BuildMaterial(map.Tileset);
        view.root.AddComponent<MeshFilter>().sharedMesh = view.mesh;
        view.root.AddComponent<MeshRenderer>().sharedMaterial = view.ground;
        view.surface = view.root.AddComponent<MeshCollider>();
        view.surface.sharedMesh = view.mesh;

        // Water is an editor plane; the original sea component still owns runtime water.
        var water = owner.RuntimePlanet.GetComponentInChildren<NyxaraSeaFit>(true);
        if (water != null)
        {
            float clearance = new SerializedObject(water).FindProperty("clearance").floatValue;
            float seaY = owner.RuntimePlanet.GetTerrainRadius(Vector3.up) + Mathf.Max(0, clearance) - owner.Radius;
            float halfW = Mathf.PI * owner.Radius, halfH = halfW * .5f;
            view.seaMesh = new Mesh { name = "Flat ocean preview", hideFlags = HideFlags.HideAndDontSave };
            view.seaMesh.vertices = new[] { new Vector3(-halfW,seaY,-halfH), new Vector3(halfW,seaY,-halfH),
                new Vector3(halfW,seaY,halfH), new Vector3(-halfW,seaY,halfH) };
            view.seaMesh.triangles = new[] { 0,2,1,0,3,2 };
            view.seaMesh.RecalculateNormals();
            view.seaMesh.RecalculateBounds();
            view.sea = new Material(view.ground.shader) { hideFlags = HideFlags.HideAndDontSave };
            view.sea.SetColor("_BaseColor", new Color(.12f,.52f,.68f));
            var sea = PreviewObject("Flat ocean (editor preview)", view.root.transform);
            sea.AddComponent<MeshFilter>().sharedMesh = view.seaMesh;
            sea.AddComponent<MeshRenderer>().sharedMaterial = view.sea;
        }
        foreach (var handle in owner.AuthoringRoot.GetComponentsInChildren<PlanetPlacement>(true))
        {
            if (handle.RuntimeTarget == null) continue;
            var prop = PreviewObject("Prop preview", handle.transform);
            view.props.Add(prop);
            CopyVisuals(handle.RuntimeTarget, prop.transform, true);
            foreach (var filter in prop.GetComponentsInChildren<MeshFilter>(true))
                view.pickMeshes.Add(new PickMesh { filter = filter, handle = handle.gameObject });
        }
        Physics.SyncTransforms();
        SceneView.RepaintAll();
    }

    static void CopyVisuals(Transform source, Transform destination, bool isRoot)
    {
        if (!isRoot)
        {
            destination.localPosition = source.localPosition;
            destination.localRotation = source.localRotation;
            destination.localScale = source.localScale;
            destination.gameObject.SetActive(source.gameObject.activeSelf);
        }
        var filter = source.GetComponent<MeshFilter>();
        var renderer = source.GetComponent<MeshRenderer>();
        if (filter != null && renderer != null && renderer.enabled && filter.sharedMesh != null)
        {
            destination.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var copy = destination.gameObject.AddComponent<MeshRenderer>();
            copy.sharedMaterials = renderer.sharedMaterials;
            copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        for (int i = 0; i < source.childCount; i++)
        {
            Transform child = source.GetChild(i);
            if ((child.hideFlags & HideFlags.DontSaveInEditor) != 0) continue;
            CopyVisuals(child, PreviewObject(child.name, destination).transform, false);
        }
    }

    static Material BuildMaterial(PlanetTileset tileset)
    {
        var material = new Material(Shader.Find("Hidden/BackHome/PlanetAuthoringPreview"))
            { hideFlags = HideFlags.HideAndDontSave, name = "Flat ground preview" };
        material.SetFloat("_UseSplat", tileset.UsesSplatBlending ? 1f : 0f);
        material.SetFloat("_Sharpness", tileset.SplatBlendSharpness);
        material.SetTexture("_MainTex", tileset.Texture);
        for (int i = 0; i < 4; i++) material.SetTexture("_Splat" + i, tileset.GetSplatAlbedo(i));
        return material;
    }

    public static Mesh BuildMesh(PlanetAuthoring owner)
    {
        var map = owner.Tiles;
        var tileset = map.Tileset;
        int rows = map.LatitudeBands, columns = map.LongitudeBands;
        var vertices = new Vector3[rows * columns * 4];
        var uv = new Vector2[vertices.Length];
        var colors = new Color[vertices.Length];
        var indices = new int[rows * columns * 6];
        var heights = new float[(rows + 1) * (columns + 1)];
        float width = 2f * Mathf.PI * owner.Radius, depth = width * .5f;
        for (int y = 0; y <= rows; y++)
        for (int x = 0; x <= columns; x++)
        {
            var flat = new Vector3((x / (float)columns - .5f) * width, 0f, (y / (float)rows - .5f) * depth);
            heights[y * (columns + 1) + x] = map.GetWalkSurfaceRadius(PlanetProjection.ToSphere(flat, owner.Radius)) - owner.Radius;
        }
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            int v = (y * columns + x) * 4, t = (y * columns + x) * 6;
            for (int corner = 0; corner < 4; corner++)
            {
                int cx = x + (corner == 1 || corner == 2 ? 1 : 0);
                int cy = y + (corner >= 2 ? 1 : 0);
                vertices[v + corner] = new Vector3((cx / (float)columns - .5f) * width,
                    heights[cy * (columns + 1) + cx], (cy / (float)rows - .5f) * depth);
                uv[v + corner] = new Vector2(cx, cy) * tileset.SplatTiling;
                colors[v + corner] = tileset.SplatWeight(map.GetTerrain(Mathf.Min(cy, rows - 1), cx));
            }
            if (!tileset.UsesSplatBlending)
            {
                int visual = PlanetBlobAutotile.ResolveVisualIndex(map, y, x, tileset.DefaultVisualIndex());
                tileset.TryGetCornerUvs(visual, out uv[v], out uv[v+1], out uv[v+2], out uv[v+3]);
            }
            indices[t] = v; indices[t+1] = v+2; indices[t+2] = v+1;
            indices[t+3] = v; indices[t+4] = v+3; indices[t+5] = v+2;
        }
        var mesh = new Mesh { name = "Nyxara editor grid", hideFlags = HideFlags.HideAndDontSave,
            indexFormat = vertices.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        mesh.vertices = vertices; mesh.uv = uv; mesh.colors = colors; mesh.triangles = indices;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
}
