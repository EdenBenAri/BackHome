using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Editor-only coastline import and tracing overlay for the authored Reference plane.</summary>
public static class PlanetReferenceTerrain
{
    public static MeshRenderer Find(PlanetAuthoring owner)
    {
        var reference = owner.AuthoringRoot != null ? owner.AuthoringRoot.Find("Reference") : null;
        return reference != null ? reference.GetComponent<MeshRenderer>() : null;
    }

    public static float Opacity(PlanetAuthoring owner) =>
        SessionState.GetFloat("PlanetReferenceOpacity." + owner.GetEntityId(), .5f);

    public static bool Visible(PlanetAuthoring owner) =>
        SessionState.GetBool("PlanetReferenceVisible." + owner.GetEntityId(), true);

    public static void DrawInspector(PlanetAuthoring owner)
    {
        var reference = Find(owner);
        if (reference == null) return;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Reference / Coastline", EditorStyles.boldLabel);
        bool visible = Visible(owner);
        if (GUILayout.Button(visible ? "Hide Reference" : "Show Reference"))
        {
            visible = !visible;
            SessionState.SetBool("PlanetReferenceVisible." + owner.GetEntityId(), visible);
            PlanetAuthoringPreview.UpdateReference(owner);
        }
        if (!reference.enabled && visible)
        {
            EditorGUI.BeginChangeCheck();
            float opacity = EditorGUILayout.Slider("Reference opacity", Opacity(owner), 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                SessionState.SetFloat("PlanetReferenceOpacity." + owner.GetEntityId(), opacity);
                PlanetAuthoringPreview.UpdateReference(owner);
            }
        }
        EditorGUILayout.HelpBox("White = water. Colored areas = land for you to paint. Prepare resets terrain and heights on this world; Undo restores them. The Water paint tool adds or erases water by hand. Hide Reference shows only your tiles and keeps the current opacity.", MessageType.None);
        if (GUILayout.Button("Prepare Land + Water From Reference (Reset Tiles)"))
        {
            try { Prepare(owner); }
            catch (Exception error) { Debug.LogException(error, owner); }
        }
    }

    public static Texture2D Texture(MeshRenderer reference, out Vector2 scale, out Vector2 offset)
    {
        scale = Vector2.one;
        offset = Vector2.zero;
        var material = reference.sharedMaterial;
        if (material == null) return null;
        foreach (string property in new[] { "_BaseMap", "_MainTex" })
        {
            if (!material.HasProperty(property) || !(material.GetTexture(property) is Texture2D texture)) continue;
            scale = material.GetTextureScale(property);
            offset = material.GetTextureOffset(property);
            return texture;
        }
        return null;
    }

    // Derive UVs from the actual mesh, including Unity Plane's UV orientation, then
    // apply the authored transform. Import and overlay share this exact mapping.
    public static Matrix4x4 WorldToUV(MeshRenderer reference)
    {
        var filter = reference.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) throw new InvalidOperationException("Reference needs a plane mesh.");
        Mesh mesh = filter.sharedMesh;
        var vertices = mesh.vertices;
        var uv = mesh.uv;
        var triangles = mesh.triangles;
        if (uv.Length != vertices.Length) throw new InvalidOperationException("Reference mesh needs UVs.");
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            Vector3 e1 = vertices[b] - vertices[a], e2 = vertices[c] - vertices[a];
            float d11 = Vector3.Dot(e1, e1), d12 = Vector3.Dot(e1, e2), d22 = Vector3.Dot(e2, e2);
            float determinant = d11 * d22 - d12 * d12;
            if (Mathf.Abs(determinant) < .000001f) continue;
            Vector3 s = (d22 * e1 - d12 * e2) / determinant;
            Vector3 t = (d11 * e2 - d12 * e1) / determinant;
            Vector3 u = (uv[b].x - uv[a].x) * s + (uv[c].x - uv[a].x) * t;
            Vector3 v = (uv[b].y - uv[a].y) * s + (uv[c].y - uv[a].y) * t;
            var matrix = Matrix4x4.identity;
            matrix.SetRow(0, new Vector4(u.x, u.y, u.z, uv[a].x - Vector3.Dot(u, vertices[a])));
            matrix.SetRow(1, new Vector4(v.x, v.y, v.z, uv[a].y - Vector3.Dot(v, vertices[a])));
            return matrix * reference.transform.worldToLocalMatrix;
        }
        throw new InvalidOperationException("Reference mesh has no usable triangles.");
    }

    public static bool IsWater(Color pixel)
    {
        float min = Mathf.Min(pixel.r, Mathf.Min(pixel.g, pixel.b));
        float max = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
        return pixel.a < .1f || (min >= .9f && max - min <= .08f);
    }

    public static void ConfigureOverlay(PlanetAuthoring owner, Material material)
    {
        material.SetFloat("_ReferenceOpacity", 0f);
        var reference = Find(owner);
        if (reference == null || reference.enabled || !Visible(owner)) return;
        var texture = Texture(reference, out var scale, out var offset);
        if (texture == null) return;
        material.SetTexture("_ReferenceTex", texture);
        material.SetMatrix("_ReferenceWorldToUV", WorldToUV(reference));
        material.SetVector("_ReferenceST", new Vector4(scale.x, scale.y, offset.x, offset.y));
        material.SetFloat("_ReferenceOpacity", Opacity(owner));
    }

    public static void Prepare(PlanetAuthoring owner)
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Prepare requires Edit Mode.");
        if (!owner.ValidateTerrainConfiguration(out string reason))
            throw new InvalidOperationException(reason);
        var reference = Find(owner);
        if (reference == null) throw new InvalidOperationException("Add a Reference plane below Authoring Plane first.");
        var texture = Texture(reference, out var scale, out var offset);
        if (texture == null) throw new InvalidOperationException("Assign the reference image to the plane's material first.");
        var map = owner.Tiles;
        var mapping = WorldToUV(reference) * owner.transform.localToWorldMatrix;
        var readable = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var water = new bool[map.CellCount];
        int waterCount = 0;
        try
        {
            // Read the source PNG without changing texture importer settings or its palette.
            if (!ImageConversion.LoadImage(readable, File.ReadAllBytes(AssetDatabase.GetAssetPath(texture))))
                throw new InvalidOperationException("Reference must be a PNG or JPG image.");
            readable.wrapMode = TextureWrapMode.Clamp;
            float width = 2f * Mathf.PI * owner.Radius, depth = width * .5f;
            for (int row = 0; row < map.LatitudeBands; row++)
            for (int column = 0; column < map.LongitudeBands; column++)
            {
                int wet = 0;
                // Majority coverage avoids single antialiased outline pixels becoming islands.
                for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                {
                    Vector3 flat = new Vector3(((column + (x + .5f) / 3f) / map.LongitudeBands - .5f) * width,
                        0f, ((row + (y + .5f) / 3f) / map.LatitudeBands - .5f) * depth);
                    Vector3 uv = mapping.MultiplyPoint3x4(flat);
                    bool outside = uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1;
                    if (outside || IsWater(readable.GetPixelBilinear(uv.x * scale.x + offset.x, uv.y * scale.y + offset.y))) wet++;
                }
                bool isWater = wet >= 5;
                water[row * map.LongitudeBands + column] = isWater;
                if (isWater) waterCount++;
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(readable); }
        if (waterCount == 0 || waterCount == map.CellCount)
            throw new InvalidOperationException("Reference did not produce both land and water. Check the image and plane alignment.");

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Prepare reference coastline");
        Undo.RegisterCompleteObjectUndo(map, "Prepare reference coastline");
        for (int row = 0; row < map.LatitudeBands; row++)
        for (int column = 0; column < map.LongitudeBands; column++)
        {
            map.SetTerrainSilent(row, column, map.Tileset.BaseTerrainIndex);
            map.SetHeightSilent(row, column, water[row * map.LongitudeBands + column] ? PlanetTileMap.WaterHeight : PlanetTileMap.DefaultGroundHeight);
        }
        var data = new SerializedObject(map);
        data.FindProperty("lastLoadedPreset").objectReferenceValue = null;
        data.ApplyModifiedPropertiesWithoutUndo();
        PlanetBlobAutotile.ResolveAll(map);
        // The source remains available for alignment, but its opaque quad and collider
        // must not cover the editable terrain. It is never part of a player build.
        Undo.RecordObjects(new UnityEngine.Object[] { reference, reference.gameObject }, "Use tracing overlay");
        reference.enabled = false;
        reference.gameObject.tag = "EditorOnly";
        foreach (var collider in reference.GetComponents<Collider>())
        {
            Undo.RecordObject(collider, "Disable reference collider");
            collider.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(reference);
        PrefabUtility.RecordPrefabInstancePropertyModifications(reference.gameObject);
        PlanetTerrainPresets.MarkDirty(map);
        PlanetAuthoringPreview.Queue(owner);
        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = owner.gameObject;
        Debug.Log($"Reference coastline prepared: {waterCount} water cells, {map.CellCount - waterCount} land cells. Paint land with the Terrain tool and water with the Water tool; reference opacity 0 shows the finished tiles.", owner);
    }
}
