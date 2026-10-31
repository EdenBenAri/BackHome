using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Deterministic regression checks, runnable from the menu or Unity -executeMethod.</summary>
public static class PlanetAuthoringValidation
{
    public const string ReportPath = "Logs/PlanetAuthoring-validation.txt";

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Nyxara validation: " + message);
    }

    [MenuItem("BackHome/Nyxara/Validate World")]
    public static void Run()
    {
        var report = new System.Text.StringBuilder();
        var random = new System.Random(97531);
        float maxError = 0;
        for (int i = 0; i < 4096; i++)
        {
            const float radius = 75;
            Vector3 flat = new Vector3((float)(random.NextDouble() * 2 - 1) * Mathf.PI * radius,
                (float)random.NextDouble() * 30 - 10,
                (float)(random.NextDouble() * 2 - 1) * Mathf.PI * radius * .499f);
            Vector3 sphere = PlanetProjection.ToSphere(flat, radius);
            Vector3 roundtrip = PlanetProjection.ToSphere(PlanetProjection.ToFlat(sphere, radius), radius);
            maxError = Mathf.Max(maxError, Vector3.Distance(sphere, roundtrip));
            Require(Vector3.Distance(sphere, roundtrip) < .003f, "position round trip");
            Quaternion frame = PlanetProjection.Frame(flat, radius);
            Require(Vector3.Dot(frame * Vector3.up, sphere.normalized) > .99999f, "radial up");
            Vector3 east = PlanetProjection.ToSphere(flat + Vector3.right * .01f, radius) - sphere;
            Require(Vector3.Dot(east.normalized, frame * Vector3.right) > .98f, "east tangent / mirrored chart");
            Quaternion rotation = Quaternion.Euler(i % 47, i % 360, i % 31);
            Require(Quaternion.Angle(frame * (Quaternion.Inverse(frame) * rotation), rotation) < .1f, "rotation round trip");
        }
        for (int pole = -1; pole <= 1; pole += 2)
        {
            Vector3 a = PlanetProjection.ToSphere(new Vector3(-Mathf.PI * 75, 0, pole * Mathf.PI * 37.5f), 75);
            Vector3 b = PlanetProjection.ToSphere(new Vector3(Mathf.PI * 75, 0, pole * Mathf.PI * 37.5f), 75);
            Require(Vector3.Distance(a, b) < .0001f, "pole / seam closure");
        }
        Require(!PlanetProjection.TryCell(new Vector3(1000,0,0),75,36,72,out _,out _), "reject outside map");
        Require(PlanetProjection.TryCell(new Vector3(Mathf.PI*75,0,Mathf.PI*37.5f),75,36,72,out int row,out int col)
            && row == 35 && col == 71, "inclusive final cell");
        report.AppendLine($"PASS: 4096 projection/rotation/tangent cases; maximum positional error {maxError:F6} m; poles, seam, bounds.");

        string prefabPath = PlanetAuthoringTools.PrefabPath;
        string originalBefore = File.ReadAllText(prefabPath);
        string sceneBefore = File.ReadAllText(PlanetAuthoringTools.ScenePath);
        GameObject root = PrefabUtility.LoadPrefabContents(PlanetAuthoringTools.PrefabPath);
        try
        {
            var owner = root.GetComponent<PlanetAuthoring>();
            Require(owner != null && owner.ValidateConfiguration(out _), "valid production world prefab");
            Require(!owner.RuntimePlanet.gameObject.activeSelf, "runtime snapshot starts inactive");
            Require(owner.AuthoringRoot.gameObject.activeSelf, "flat content starts active");
            Require(!PrefabUtility.IsPartOfPrefabInstance(owner.RuntimePlanet), "snapshot must not inherit original prefab");
            int count = 0;
            float maxPlacement = 0;
            foreach (var handle in owner.AuthoringRoot.GetComponentsInChildren<PlanetPlacement>(true))
            {
                Transform destination = handle.RuntimeTarget;
                // The saved handle may have been edited since importing the old world.
                // Its pose is authoritative; the inactive snapshot intentionally retains its old pose.
                Vector3 flatPose = owner.transform.InverseTransformPoint(handle.transform.position);
                Vector3 position = owner.RuntimePlanet.transform.TransformPoint(PlanetProjection.ToSphere(flatPose, owner.Radius));
                Quaternion rotation = owner.RuntimePlanet.transform.rotation * PlanetProjection.Frame(flatPose, owner.Radius)
                    * Quaternion.Inverse(owner.transform.rotation) * handle.transform.rotation;
                Vector3 scale = Vector3.Scale(handle.transform.lossyScale,
                    new SerializedObject(handle).FindProperty("runtimeScaleRatio").vector3Value);
                handle.Apply(owner);
                maxPlacement = Mathf.Max(maxPlacement, Vector3.Distance(position, destination.position));
                Require(Vector3.Distance(position, destination.position) < .005f, "placement position " + handle.name);
                Require(Quaternion.Angle(rotation, destination.rotation) < .1f, "placement rotation " + handle.name);
                Require(Vector3.Distance(scale, destination.localScale) < .001f, "placement scale " + handle.name);
                count++;
            }
            Require(count > 0, "populated flat content");
            var map = owner.Tiles;
            int hash = map.ComputeContentHash();
            float oldHeight = map.GetHeight(12,0);
            map.PaintHeightBrush(12,0,oldHeight == 2f ? 1f : 2f,1,false);
            Require(map.GetHeight(12,map.LongitudeBands - 1) == map.GetHeight(12,0), "height brush wraps seam");
            Require(map.ComputeContentHash() != hash, "flat terrain changes authoritative data");
            int fallback = (map.GetTerrain(12,0) + 1) % map.Tileset.TerrainCount;
            map.PaintTerrainBrush(12,0,fallback,1,false);
            Require(map.GetTerrain(12,map.LongitudeBands - 1) == fallback, "terrain brush wraps seam");
            Mesh mesh = PlanetAuthoringPreview.BuildMesh(owner);
            Require(mesh.vertexCount == map.CellCount * 4 && mesh.triangles.Length == map.CellCount * 6, "flat topology");
            foreach (var vertex in mesh.vertices)
                Require(!float.IsNaN(vertex.x) && !float.IsNaN(vertex.y) && !float.IsNaN(vertex.z), "finite preview geometry");
            Object.DestroyImmediate(mesh);
            report.AppendLine($"PASS: {count} authored poses/scales; maximum placement error {maxPlacement:F6} m; {map.CellCount} cells; height + terrain seam painting; preview topology.");

            Validate3DAuthoring(owner, report);
            ValidateEditingTools(owner, report);

            var first = owner.AuthoringRoot.GetComponentInChildren<PlanetPlacement>(true);
            var duplicate = Object.Instantiate(first.gameObject, first.transform.parent);
            Require(!owner.ValidateConfiguration(out _), "reject ambiguous Ctrl+D handle binding");
            Object.DestroyImmediate(duplicate);
            Require(owner.ValidateConfiguration(out _), "configuration recovers after removing duplicate");
            first.transform.localPosition += new Vector3(2, 1, 3);
            first.transform.localRotation *= Quaternion.Euler(0, 37, 0);
            Vector3 editedPosition = first.transform.localPosition;
            int editedHash = map.ComputeContentHash();
            string temporaryPrefab = "Assets/PlanetAuthoringValidation-" + Guid.NewGuid().ToString("N") + ".prefab";
            try
            {
                PlanetAuthoringPreview.Rebuild(owner);
                PrefabUtility.SaveAsPrefabAsset(root, temporaryPrefab);
                Require(!File.ReadAllText(temporaryPrefab).Contains("3D Authoring Plane"), "editor terrain is not serialized");
                GameObject reloaded = PrefabUtility.LoadPrefabContents(temporaryPrefab);
                try
                {
                    var restored = reloaded.GetComponent<PlanetAuthoring>();
                    Require(restored.ValidateConfiguration(out _), "internal bindings survive prefab reload");
                    Require(restored.Tiles.ComputeContentHash() == editedHash, "terrain edits survive save/reload");
                    var restoredHandle = restored.AuthoringRoot.GetComponentInChildren<PlanetPlacement>(true);
                    Require(Vector3.Distance(restoredHandle.transform.localPosition, editedPosition) < .0001f, "placement edit survives save/reload");
                    restoredHandle.Apply(restored);
                    Vector3 flat = restored.transform.InverseTransformPoint(restoredHandle.transform.position);
                    Require(Vector3.Distance(restoredHandle.RuntimeTarget.position,
                        restored.RuntimePlanet.transform.TransformPoint(PlanetProjection.ToSphere(flat, restored.Radius))) < .001f,
                        "edited placement projects from reloaded data");
                }
                finally { PrefabUtility.UnloadPrefabContents(reloaded); }
            }
            finally { AssetDatabase.DeleteAsset(temporaryPrefab); }
            report.AppendLine("PASS: duplicate-binding guard; edited terrain and placement save/reload; reloaded projection. Temporary fixture removed.");
        }
        finally { PlanetAuthoringPreview.ClearAll(); PrefabUtility.UnloadPrefabContents(root); PlanetAuthoringPreview.RefreshAll(); }
        Require(File.ReadAllText(prefabPath) == originalBefore && File.ReadAllText(PlanetAuthoringTools.ScenePath) == sceneBefore,
            "production assets remain byte-identical");
        report.AppendLine("PASS: production scene and prefab byte-identical; all validation edits discarded.");
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log(report.ToString());
    }

    static void ValidateEditingTools(PlanetAuthoring owner, System.Text.StringBuilder report)
    {
        var brush = owner.AuthoringRoot.GetComponentInChildren<PlanetEnvironmentBrush>(true);
        Require(brush != null && brush.CategoryCount == 3, "environment palette migrated to authoring plane");
        var prefab = brush.GetPrefab(0, 0);
        Require(prefab != null, "environment palette retains prefab references");
        int before = owner.AuthoringRoot.GetComponentsInChildren<PlanetPlacement>(true).Length;
        var added = PlanetAuthoringTools.AddProp(owner, prefab, new Vector3(10, 0, 20), null, 43, 1.2f, .07f, false);
        Require(added != null && added.RuntimeTarget.GetComponent<PlanetEnvironmentObject>() != null, "new prop preserves streaming identity");
        Require(owner.ValidateConfiguration(out _), "new prop registers an independent target");
        added.Apply(owner);
        Require(Vector3.Distance(added.RuntimeTarget.position, owner.RuntimePlanet.transform.TransformPoint(
            PlanetProjection.ToSphere(owner.transform.InverseTransformPoint(added.transform.position), owner.Radius))) < .001f, "new prop projects");
        PlanetAuthoringTools.RemoveProp(owner, added);
        Require(owner.ValidateConfiguration(out _), "erasing prop cleans target registration");
        Require(owner.AuthoringRoot.GetComponentsInChildren<PlanetPlacement>(true).Length == before, "add/erase restores placement count");
        var preset = ScriptableObject.CreateInstance<PlanetTileMapPreset>();
        try
        {
            owner.Tiles.CaptureToPreset(preset);
            int hash = owner.Tiles.ComputeContentHash();
            owner.Tiles.PaintHeightDeltaBrush(10, 10, 1, 0, false);
            Require(owner.Tiles.ApplyPreset(preset) && owner.Tiles.ComputeContentHash() == hash, "presets restore terrain and heights");
        }
        finally { Object.DestroyImmediate(preset); }
        report.AppendLine("PASS: three environment palettes retained; add/project/erase prop; streaming identity; terrain preset round trip.");
    }

    static void Validate3DAuthoring(PlanetAuthoring owner, System.Text.StringBuilder report)
    {
        Vector3 previousPosition = owner.transform.position;
        Quaternion previousRotation = owner.transform.rotation;
        try
        {
            owner.transform.SetPositionAndRotation(new Vector3(13, 7, -22), Quaternion.Euler(12, 37, 8));
            PlanetAuthoringPreview.Rebuild(owner);
            var surface = owner.AuthoringRoot.GetComponentInChildren<MeshCollider>(true);
            Require(surface != null && (surface.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0,
                "3D editor plane has a disposable collider");
            Require(surface.sharedMesh == surface.GetComponent<MeshFilter>().sharedMesh, "3D picking matches rendered terrain");
            for (int i = 0; i < 64; i++)
            {
                Vector3 local = new Vector3((i % 8 - 3.5f) * 40f, 200, (i / 8 - 3.5f) * 20f);
                var vertical = new Ray(owner.transform.TransformPoint(local), -owner.transform.up);
                Require(PlanetAuthoringPreview.Raycast(owner, vertical, out var hit), "terrain hit " + i);
                Vector3 start = hit.point + owner.transform.TransformDirection(new Vector3(1, 100, -1));
                Require(PlanetAuthoringPreview.Raycast(owner, new Ray(start, (hit.point - start).normalized), out var angled),
                    "perspective terrain hit " + i);
                Require(Vector3.Distance(hit.point, angled.point) < .005f, "perspective cursor stays on visible terrain " + i);
                Vector3 point = owner.transform.InverseTransformPoint(angled.point);
                Require(PlanetProjection.TryCell(point, owner.Radius, owner.Tiles.LatitudeBands,
                    owner.Tiles.LongitudeBands, out _, out _), "perspective paint cell");
            }
            Require(PlanetAuthoringPreview.GetAuthoringObject(surface.gameObject) == owner.gameObject, "terrain click resolves map inspector");
            var visual = owner.AuthoringRoot.GetComponentInChildren<MeshRenderer>(true);
            foreach (var renderer in owner.AuthoringRoot.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.GetComponentInParent<PlanetPlacement>() != null) { visual = renderer; break; }
            var handle = visual.GetComponentInParent<PlanetPlacement>();
            Require(handle != null, "3D prop preview exists");
            Require(PlanetAuthoringPreview.GetAuthoringObject(visual.gameObject) == handle.gameObject, "3D visual resolves saved editable handle");
            Bounds bounds = visual.bounds;
            int hits = 0;
            foreach (var direction in new[] { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                var pickRay = new Ray(bounds.center + direction * (bounds.extents.magnitude + 10), -direction);
                var picked = PlanetAuthoringPreview.Pick(owner, pickRay, out _, filter: new[] { handle.gameObject });
                if (picked == handle.gameObject) hits++;
                Require(PlanetAuthoringPreview.Pick(owner, pickRay, out _, ignore: new[] { handle.gameObject },
                    filter: new[] { handle.gameObject }) == null, "mesh picking honors selection exclusions");
            }
            Require(hits > 0, "3D prop mesh can be picked through its actual geometry");
            PlanetAuthoringPreview.ClearAll();
            Require(surface == null && visual == null, "3D preview meshes and collider are released together");
            foreach (var collider in Resources.FindObjectsOfTypeAll<MeshCollider>())
                Require(collider.name != "3D prop picking (editor only)", "temporary prop picking colliders released");
            report.AppendLine($"PASS: 64 vertical + 64 perspective terrain hits on a translated/rotated 3D plane; {hits} prop mesh hits; selection exclusions; editor collider cleanup.");
        }
        finally
        {
            owner.transform.SetPositionAndRotation(previousPosition, previousRotation);
            PlanetAuthoringPreview.ClearAll();
            PlanetAuthoringPreview.RefreshAll();
        }
    }
}
