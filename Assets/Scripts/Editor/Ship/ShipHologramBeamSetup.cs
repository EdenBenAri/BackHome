using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Places a projection beam from the cockpit screen up to NyxaraHologram.
/// Does not move or restyle the hologram.
/// Menu: BackHome → Ship → Add Hologram Beam
/// </summary>
public static class ShipHologramBeamSetup
{
    const string ScenePath = "Assets/Scenes/Ship.unity";
    const string ShaderName = "BackHome/HologramBeam";
    const string MaterialPath = "Assets/Resources/Galaxy/Shaders/HologramBeam.mat";

    static readonly string TriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeHologramBeam.trigger");
    static readonly string ConeTriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeHologramCone.trigger");
    static readonly string ResultPath = Path.Combine(Application.dataPath, "..", "Temp", "HologramBeam.result.txt");
    static readonly string PreviewPath = Path.Combine(Application.dataPath, "..", "Temp", "HologramBeam.preview.png");

    static int _shaderWaits;

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.update += WatchTrigger;
    }

    static void WatchTrigger()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (File.Exists(ConeTriggerPath))
        {
            try { File.Delete(ConeTriggerPath); } catch { /* ignore */ }
            ShapeCone();
            return;
        }

        if (!File.Exists(TriggerPath))
            return;
        if (Shader.Find(ShaderName) == null && _shaderWaits < 120)
        {
            _shaderWaits++;
            return;
        }

        _shaderWaits = 0;
        try { File.Delete(TriggerPath); } catch { /* ignore */ }
        Build();
    }

    [MenuItem("BackHome/Ship/Shape Hologram Cone")]
    public static void ShapeCone()
    {
        var report = new StringBuilder();
        Camera camera = null;
        try
        {
            var beams = Object.FindObjectsByType<HologramBeam>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (beams.Length == 0)
                throw new System.InvalidOperationException("No HologramBeam in the open scenes.");

            foreach (HologramBeam beam in beams)
            {
                var data = new SerializedObject(beam);
                data.FindProperty("topRadiusScale").floatValue = 1.3f;
                data.FindProperty("topAlong").floatValue = 0f;
                data.ApplyModifiedPropertiesWithoutUndo();
                beam.Rebuild();
                if (camera == null)
                    camera = beam.GetComponentInParent<Camera>();
                EditorUtility.SetDirty(beam);
            }

            var scene = beams[0].gameObject.scene;
            if (scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            if (camera != null)
                RenderPreview(camera);
            report.AppendLine("cone beams " + beams.Length);
            report.AppendLine("OK");
        }
        catch (System.Exception ex)
        {
            report.AppendLine(ex.ToString());
        }
        finally
        {
            if (camera != null)
                camera.targetTexture = null;
            File.WriteAllText(ResultPath, report.ToString());
        }
    }

    [MenuItem("BackHome/Ship/Add Hologram Beam")]
    public static void Build()
    {
        var report = new StringBuilder();
        Scene scene = default;
        bool opened = false;
        Camera camera = null;
        var hidden = new List<Renderer>();
        try
        {
            scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != ScenePath)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                opened = true;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                camera = root.GetComponentInChildren<Camera>(true);
                if (camera != null)
                    break;
            }

            if (camera == null)
                throw new System.InvalidOperationException("No camera in the Ship scene.");

            Transform hologram = FindNamed(camera.transform, "NyxaraHologram");
            if (hologram == null)
                throw new System.InvalidOperationException("NyxaraHologram is not under the camera.");

            HideRenderers(hologram, hidden);
            Transform existingBeam = FindNamed(camera.transform, "HologramBeam");
            if (existingBeam != null)
                HideRenderers(existingBeam, hidden);

            if (!TryFindScreen(camera, out Vector3 center, out Vector3 axisX, out Vector3 axisY, out float halfW, out float halfH, out string how))
                throw new System.InvalidOperationException("Could not find the cockpit screen. " + how);

            report.AppendLine(how);
            report.AppendLine("screen " + center.ToString("F3") + " half " + halfW.ToString("F3") + " x " + halfH.ToString("F3"));

            foreach (Renderer renderer in hidden)
            {
                if (renderer != null)
                    renderer.enabled = true;
            }
            hidden.Clear();

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
                throw new System.InvalidOperationException("Shader " + ShaderName + " was not found.");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "HologramBeam" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetColor("_Color", new Color(0.22f, 0.72f, 1f, 1f));
            material.SetColor("_CoreColor", new Color(0.55f, 0.95f, 1f, 1f));
            material.SetFloat("_Intensity", 0.72f);
            material.SetFloat("_Fill", 0.018f);
            material.SetFloat("_LineCount", 14f);
            material.SetFloat("_LineSharpness", 16f);
            material.SetFloat("_BeamCount", 4f);
            material.SetFloat("_RiseFrequency", 2.4f);
            material.SetFloat("_RiseSpeed", 0.35f);
            material.SetFloat("_ScanFrequency", 22f);
            material.SetFloat("_ScanSpeed", 0.18f);
            material.SetFloat("_BaseGlow", 0.35f);
            material.SetFloat("_TopFade", 0.9f);
            EditorUtility.SetDirty(material);

            GameObject beamObject;
            if (existingBeam != null)
            {
                beamObject = existingBeam.gameObject;
            }
            else
            {
                beamObject = new GameObject("HologramBeam");
                Undo.RegisterCreatedObjectUndo(beamObject, "Add hologram beam");
                beamObject.transform.SetParent(camera.transform, false);
            }

            beamObject.transform.localPosition = Vector3.zero;
            beamObject.transform.localRotation = Quaternion.identity;
            beamObject.transform.localScale = Vector3.one;

            Vector3 localCenter = camera.transform.InverseTransformPoint(center);
            Vector3 localX = camera.transform.InverseTransformDirection(axisX);
            Vector3 localY = camera.transform.InverseTransformDirection(axisY);

            var beam = beamObject.GetComponent<HologramBeam>();
            if (beam == null)
                beam = beamObject.AddComponent<HologramBeam>();
            beam.Configure(hologram, material, localCenter, localX, localY, halfW * 0.82f, halfH * 0.82f);
            var beamData = new SerializedObject(beam);
            beamData.FindProperty("topRadiusScale").floatValue = 1.3f;
            beamData.FindProperty("topAlong").floatValue = 0f;
            beamData.ApplyModifiedPropertiesWithoutUndo();
            beam.Rebuild();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            RenderPreview(camera);
            report.AppendLine("preview " + PreviewPath);
            report.AppendLine("OK");
        }
        catch (System.Exception ex)
        {
            report.AppendLine(ex.ToString());
        }
        finally
        {
            foreach (Renderer renderer in hidden)
            {
                if (renderer != null)
                    renderer.enabled = true;
            }

            if (camera != null)
                camera.targetTexture = null;
            if (opened && scene.IsValid())
                EditorSceneManager.CloseScene(scene, true);
            File.WriteAllText(ResultPath, report.ToString());
        }
    }

    static void RenderPreview(Camera camera)
    {
        var rt = new RenderTexture(540, 960, 24, RenderTextureFormat.ARGB32);
        var previous = camera.targetTexture;
        var previousActive = RenderTexture.active;
        try
        {
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            image.Apply(false, false);
            File.WriteAllBytes(PreviewPath, image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
        finally
        {
            camera.targetTexture = previous;
            RenderTexture.active = previousActive;
            Object.DestroyImmediate(rt);
        }
    }

    static bool TryFindScreen(
        Camera camera,
        out Vector3 center,
        out Vector3 axisX,
        out Vector3 axisY,
        out float halfW,
        out float halfH,
        out string how)
    {
        center = default;
        axisX = camera.transform.right;
        axisY = camera.transform.up;
        halfW = 0.12f;
        halfH = 0.07f;
        how = "";

        const int width = 540;
        const int height = 960;
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var previous = camera.targetTexture;
        var previousActive = RenderTexture.active;
        Texture2D image = null;
        try
        {
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply(false, false);
            Color[] pixels = image.GetPixels();

            int y0 = Mathf.RoundToInt(height * 0.18f);
            int y1 = Mathf.RoundToInt(height * 0.62f);
            int x0 = Mathf.RoundToInt(width * 0.22f);
            int x1 = Mathf.RoundToInt(width * 0.78f);
            var solid = new bool[pixels.Length];
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                    solid[y * width + x] = IsScreenCyan(pixels, width, height, x, y);
            }

            if (!LargestBlob(solid, width, height, x0, y0, x1, y1, out int minX, out int minY, out int maxX, out int maxY, out int count))
            {
                how = "no solid cyan screen";
                return false;
            }

            float vx0 = (minX + 0.5f) / width;
            float vx1 = (maxX + 0.5f) / width;
            float vy0 = (minY + 0.5f) / height;
            float vy1 = (maxY + 0.5f) / height;
            Vector2[] view =
            {
                new Vector2((vx0 + vx1) * 0.5f, (vy0 + vy1) * 0.5f),
                new Vector2(vx0, vy0),
                new Vector2(vx1, vy0),
                new Vector2(vx0, vy1),
                new Vector2(vx1, vy1)
            };

            if (!TryHit(camera, view[0], out RaycastHit hit))
            {
                how = "screen pixels found but the ray missed the cockpit";
                return false;
            }

            Vector3 normal = hit.normal;
            if (Vector3.Dot(normal, camera.ViewportPointToRay(view[0]).direction) > 0f)
                normal = -normal;

            var plane = new Plane(normal, hit.point);
            var corners = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                Ray ray = camera.ViewportPointToRay(view[i + 1]);
                if (!plane.Raycast(ray, out float enter))
                {
                    how = "screen plane miss";
                    return false;
                }

                corners[i] = ray.GetPoint(enter);
            }

            axisX = Vector3.ProjectOnPlane(camera.transform.right, normal).normalized;
            axisY = Vector3.ProjectOnPlane(camera.transform.up, normal).normalized;
            float minDotX = float.PositiveInfinity;
            float maxDotX = float.NegativeInfinity;
            float minDotY = float.PositiveInfinity;
            float maxDotY = float.NegativeInfinity;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 delta = corners[i] - hit.point;
                float dx = Vector3.Dot(delta, axisX);
                float dy = Vector3.Dot(delta, axisY);
                if (dx < minDotX) minDotX = dx;
                if (dx > maxDotX) maxDotX = dx;
                if (dy < minDotY) minDotY = dy;
                if (dy > maxDotY) maxDotY = dy;
            }

            halfW = Mathf.Max(0.02f, (maxDotX - minDotX) * 0.5f);
            halfH = Mathf.Max(0.02f, (maxDotY - minDotY) * 0.5f);
            Vector3 mid = hit.point + axisX * ((minDotX + maxDotX) * 0.5f) + axisY * ((minDotY + maxDotY) * 0.5f);
            center = mid + normal * 0.03f;
            how = "screen from cyan panel, pixels " + count + " box " + (maxX - minX) + "x" + (maxY - minY) + " hit " + hit.collider.name;
            return true;
        }
        finally
        {
            camera.targetTexture = previous;
            RenderTexture.active = previousActive;
            if (image != null)
                Object.DestroyImmediate(image);
            Object.DestroyImmediate(rt);
        }
    }

    static bool IsScreenCyan(Color[] pixels, int width, int height, int x, int y)
    {
        int hits = 0;
        for (int oy = -1; oy <= 1; oy++)
        {
            int py = y + oy;
            if (py < 0 || py >= height)
                continue;
            for (int ox = -1; ox <= 1; ox++)
            {
                int px = x + ox;
                if (px < 0 || px >= width)
                    continue;
                Color c = pixels[py * width + px];
                if (c.r < 0.48f && c.g > 0.5f && c.b > 0.62f && c.b + 0.05f > c.g && c.g > c.r + 0.12f)
                    hits++;
            }
        }

        return hits >= 7;
    }

    static bool LargestBlob(
        bool[] solid,
        int width,
        int height,
        int x0,
        int y0,
        int x1,
        int y1,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY,
        out int count)
    {
        minX = width;
        minY = height;
        maxX = 0;
        maxY = 0;
        count = 0;
        var seen = new bool[solid.Length];
        var stack = new Stack<int>();
        int bestCount = 0;
        int bestMinX = 0;
        int bestMinY = 0;
        int bestMaxX = 0;
        int bestMaxY = 0;

        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int start = y * width + x;
                if (!solid[start] || seen[start])
                    continue;

                int blob = 0;
                int bMinX = x;
                int bMinY = y;
                int bMaxX = x;
                int bMaxY = y;
                stack.Push(start);
                seen[start] = true;
                while (stack.Count > 0)
                {
                    int id = stack.Pop();
                    int px = id % width;
                    int py = id / width;
                    blob++;
                    if (px < bMinX) bMinX = px;
                    if (py < bMinY) bMinY = py;
                    if (px > bMaxX) bMaxX = px;
                    if (py > bMaxY) bMaxY = py;

                    TryPush(solid, seen, stack, width, height, x0, y0, x1, y1, px - 1, py);
                    TryPush(solid, seen, stack, width, height, x0, y0, x1, y1, px + 1, py);
                    TryPush(solid, seen, stack, width, height, x0, y0, x1, y1, px, py - 1);
                    TryPush(solid, seen, stack, width, height, x0, y0, x1, y1, px, py + 1);
                }

                if (blob <= bestCount)
                    continue;
                bestCount = blob;
                bestMinX = bMinX;
                bestMinY = bMinY;
                bestMaxX = bMaxX;
                bestMaxY = bMaxY;
            }
        }

        if (bestCount < 400)
            return false;

        minX = bestMinX;
        minY = bestMinY;
        maxX = bestMaxX;
        maxY = bestMaxY;
        count = bestCount;
        return true;
    }

    static void TryPush(bool[] solid, bool[] seen, Stack<int> stack, int width, int height, int x0, int y0, int x1, int y1, int x, int y)
    {
        if (x < x0 || y < y0 || x >= x1 || y >= y1 || x < 0 || y < 0 || x >= width || y >= height)
            return;
        int id = y * width + x;
        if (!solid[id] || seen[id])
            return;
        seen[id] = true;
        stack.Push(id);
    }

    static bool TryHit(Camera camera, Vector2 viewport, out RaycastHit hit)
    {
        Ray ray = camera.ViewportPointToRay(viewport);
        if (Physics.Raycast(ray, out hit, 30f, ~0, QueryTriggerInteraction.Ignore))
            return true;

        hit = default;
        float best = float.PositiveInfinity;
        RaycastHit chosen = default;
        bool found = false;
        var colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null || colliders[i].isTrigger)
                continue;
            if (!colliders[i].Raycast(ray, out RaycastHit candidate, 30f))
                continue;
            if (candidate.distance >= best)
                continue;
            best = candidate.distance;
            chosen = candidate;
            found = true;
        }

        hit = chosen;
        return found;
    }

    static void HideRenderers(Transform root, List<Renderer> hidden)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!renderers[i].enabled)
                continue;
            renderers[i].enabled = false;
            hidden.Add(renderers[i]);
        }
    }

    static Transform FindNamed(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamed(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }
}
