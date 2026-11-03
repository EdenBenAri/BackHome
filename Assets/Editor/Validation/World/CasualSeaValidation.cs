using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Opt-in GPU checks for spherical water. Does not save or edit scene assets.</summary>
[InitializeOnLoad]
public static class CasualSeaValidation
{
    const string Trigger = "Temp/BackHomeSeaValidation.trigger";
    const string Output = "Logs/WaterValidation";
    const int Resolution = 512;

    static CasualSeaValidation() => EditorApplication.update += Poll;

    static void Poll()
    {
        if (File.Exists("Temp/BackHomeSeaCapture.trigger") && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
        {
            File.Delete("Temp/BackHomeSeaCapture.trigger");
            CapturePlanet();
        }
        if (!File.Exists(Trigger) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(Trigger);
        Run();
    }

    [MenuItem("BackHome/Water/Capture Planet")]
    public static void CapturePlanet()
    {
        var sea = Object.FindAnyObjectByType<CasualWaterSphereMesh>();
        var view = SceneView.lastActiveSceneView;
        if (sea == null || view == null) { Debug.LogWarning("Open the planet in Play mode first."); return; }
        Directory.CreateDirectory(Output);
        var go = new GameObject("Water capture camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.enabled = false;
        camera.CopyFrom(view.camera);
        camera.cameraType = CameraType.Game;
        camera.enabled = false;
        camera.cullingMask = 1 << sea.gameObject.layer;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.025f, .035f, .055f);
        camera.orthographic = false;
        camera.fieldOfView = 45;
        camera.nearClipPlane = .3f;
        camera.farClipPlane = 1500;
        var data = camera.GetUniversalAdditionalCameraData();
        data.requiresDepthOption = CameraOverrideOption.On;
        data.renderPostProcessing = false;
        data.volumeLayerMask = 0;
        float radius = sea.transform.lossyScale.x * .5f;
        Vector3 direction = (view.camera.transform.position - sea.transform.position).normalized;
        camera.transform.position = sea.transform.position + direction * radius * 2.9f;
        camera.transform.rotation = Quaternion.LookRotation(-direction, view.camera.transform.up);
        camera.ResetWorldToCameraMatrix();
        camera.ResetProjectionMatrix();
        camera.ResetCullingMatrix();
        try
        {
            Render(camera, "planet", 1024);
            // Also cover the ocean hemisphere independently of the Scene view framing.
            var planet = sea.GetComponentInParent<SphericalPlanet>();
            Vector3 ocean = sea.transform.TransformDirection(new Vector3(0, .45f, -1).normalized);
            if (planet != null)
            {
                // Find a wet patch without depending on the planet's scene position.
                var map = planet.GetComponent<PlanetTileMap>();
                if (map != null)
                {
                    float deepest = float.PositiveInfinity;
                    for (int lat = 0; lat < map.LatitudeBands; lat++)
                    for (int lon = 0; lon < map.LongitudeBands; lon++)
                    {
                        float height = map.GetHeight(lat, lon);
                        if (height >= deepest || !map.TryGetCellCenter(lat, lon, out Vector3 point)) continue;
                        deepest = height;
                        ocean = (point - sea.transform.position).normalized;
                    }
                }
            }
            camera.transform.position = sea.transform.position + ocean * radius * 2.9f;
            camera.transform.rotation = Quaternion.LookRotation(-ocean, Mathf.Abs(ocean.y) > .95f ? Vector3.forward : Vector3.up);
            Render(camera, "planet-ocean", 1024);
        }
        finally { Object.DestroyImmediate(go); }
    }

    [MenuItem("BackHome/Water/Validate Spherical Rendering")]
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        string report = Output + "/results.txt";
        File.WriteAllText(report, "Spherical water GPU checks - Unity " + Application.unityVersion + "\n");
        GameObject root = null;
        Material material = null;
        Material bedMaterial = null;
        try
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Galaxy/Shaders/ChapterOneSeaWater.mat");
            Require(source != null && source.shader.isSupported, "Water shader is supported");
            material = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
            // Freeze animation and isolate the surface pattern from scene lighting.
            material.SetFloat("_WaveSpeed", 0);
            material.SetFloat("_SparkleSpeed", 0);
            material.SetFloat("_GlintStrength", 0);
            material.SetFloat("_ShadeFloor", 1);
            root = new GameObject("Water validation (temporary)") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.position = new Vector3(10000, 10000, 10000);
            var sea = new GameObject("Sea", typeof(MeshFilter), typeof(MeshRenderer));
            sea.transform.SetParent(root.transform, false);
            sea.layer = 31;
            sea.transform.localScale = Vector3.one * 150.9f;
            sea.AddComponent<CasualWaterSphereMesh>();
            sea.GetComponent<MeshRenderer>().sharedMaterial = material;

            var cameraObject = new GameObject("Water validation camera", typeof(Camera));
            cameraObject.transform.SetParent(root.transform, false);
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.025f, .035f, .055f);
            camera.nearClipPlane = .3f;
            camera.farClipPlane = 600;
            camera.fieldOfView = 40;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            var data = camera.GetUniversalAdditionalCameraData();
            data.requiresDepthOption = CameraOverrideOption.On;
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.volumeLayerMask = 0;
            Pose(camera, new Vector3(.25f, .65f, -1));
            Color32[] original = Render(camera, "perspective");
            Require(BluePixelCount(original) > Resolution * Resolution / 5, "Visible blue water, no error shader");
            root.transform.position += new Vector3(137, -83, 211);
            Color32[] moved = Render(camera, "translated");
            float translationError = Difference(original, moved);
            Require(translationError < .002f, "Translation preserves ripples (mean error " + translationError.ToString("F6") + ")");
            root.transform.rotation = Quaternion.Euler(37, 81, -23);
            Color32[] rotated = Render(camera, "rotated");
            float rotationError = Difference(moved, rotated);
            Require(rotationError < .003f, "Rotation preserves planet-local ripples (mean error " + rotationError.ToString("F6") + ")");
            root.transform.rotation = Quaternion.identity;
            camera.orthographic = true;
            camera.orthographicSize = 92;
            Render(camera, "orthographic");
            Pose(camera, Vector3.up);
            Render(camera, "north-pole");
            Pose(camera, Vector3.down);
            Render(camera, "south-pole");

            // A concentric seabed has constant radial depth, including in an ortho view.
            // Neutral water colors make a missing depth texture fail this check visibly.
            material.SetColor("_ShallowColor", Color.red);
            material.SetColor("_DeepColor", Color.blue);
            material.SetFloat("_CrestStrength", 0);
            material.SetFloat("_WaveHeight", 0);
            material.SetFloat("_RippleNormal", 0);
            material.SetFloat("_ShoreWidth", .2f);
            material.SetFloat("_DepthDistance", 6);
            material.SetFloat("_DepthPower", 1);
            var bed = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bed.name = "Seabed";
            bed.transform.SetParent(root.transform, false);
            bed.layer = 31;
            bed.transform.localScale = Vector3.one * 145f;
            bed.AddComponent<CasualWaterSphereMesh>();
            bedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            bed.GetComponent<MeshRenderer>().sharedMaterial = bedMaterial;
            Pose(camera, Vector3.back);
            Color32[] orthoDepth = Render(camera, "depth-orthographic");
            camera.orthographic = false;
            Color32[] perspectiveDepth = Render(camera, "depth-perspective");
            Color ortho = orthoDepth[(Resolution / 2) * Resolution + Resolution / 2];
            Color perspective = perspectiveDepth[(Resolution / 2) * Resolution + Resolution / 2];
            Require(ortho.r > .4f && ortho.b > .4f && ortho.g < .1f, "Orthographic camera reads seabed depth");
            Require(Mathf.Abs(ortho.r - perspective.r) < .03f && Mathf.Abs(ortho.b - perspective.b) < .03f,
                "Perspective and orthographic radial depth agree");
            foreach (var message in ShaderUtil.GetShaderMessages(source.shader))
                Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
            File.AppendAllText(report, "PASS shader compilation\nRESULT: PASS\n");
            Debug.Log("Water validation passed. " + report);
        }
        catch (Exception exception)
        {
            File.AppendAllText(report, "FAIL " + exception + "\n");
            Debug.LogException(exception);
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            if (material != null) Object.DestroyImmediate(material);
            if (bedMaterial != null) Object.DestroyImmediate(bedMaterial);
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        File.AppendAllText(Output + "/results.txt", "PASS " + message + "\n");
    }

    static void Pose(Camera camera, Vector3 direction)
    {
        camera.transform.localPosition = direction.normalized * 245;
        camera.transform.localRotation = Quaternion.LookRotation(-direction,
            Mathf.Abs(direction.normalized.y) > .99f ? Vector3.forward : Vector3.up);
    }

    static Color32[] Render(Camera camera, string name, int resolution = Resolution)
    {
        var target = RenderTexture.GetTemporary(resolution, resolution, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var texture = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
        try
        {
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", texture.EncodeToPNG());
            return texture.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(texture);
        }
    }

    static int BluePixelCount(Color32[] pixels)
    {
        int count = 0;
        foreach (var p in pixels) if (p.b > 120 && p.g > 90 && p.b > p.r + 20) count++;
        return count;
    }

    static float Difference(Color32[] a, Color32[] b)
    {
        double difference = 0;
        for (int i = 0; i < a.Length; i++)
            difference += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
        return (float)(difference / (a.Length * 3 * 255.0));
    }
}
