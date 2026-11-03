using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Run through the isolated startup probe with the request "king-ring".</summary>
public static class KingLeashIndicatorValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Report = "Logs/KingLeashIndicatorValidation.txt";

    public static void Prepare()
    {
        // The normal spawn can be farther from every king than the streaming radius.
        // Pin them just for this isolated Play run, and allow Start/LateUpdate to run.
        foreach (var director in Object.FindObjectsByType<KingCinematicDirector>())
            director.enabled = false; // A finishing reveal would otherwise unpin its featured king.
        foreach (var king in Object.FindObjectsByType<AreaKing>(FindObjectsInactive.Include))
            PlanetEnvironmentStreamer.SetCinematicVisible(king.gameObject, true);
    }

    public static void Run()
    {
        File.WriteAllText(Report, "Live king boundary visual checks\n");
        var rings = Object.FindObjectsByType<AreaKingLeashIndicator>();
        Require(rings.Length > 0, "No active king indicators.");
        AreaKingLeashIndicator featured = null;
        float nearest = float.PositiveInfinity;
        var player = Object.FindAnyObjectByType<PlayerVitals>();
        var planet = SphericalPlanet.Instance;
        foreach (var ring in rings)
        {
            Tick(ring);
            var renderer = Get<MeshRenderer>(ring, "_renderer");
            Require(renderer != null && renderer.enabled && !renderer.transform.IsChildOf(ring.transform),
                "Ring missing or parented to the moving king.");
            Require(renderer.sharedMaterial == Resources.Load<Material>("Player/VFX/Shaders/PlayerRangeIndicator"),
                "Ring does not use the player's range material.");
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Color color = properties.GetColor("_Color");
            Require(color.r > .9f && color.g < .2f && color.b < .2f && color.a > .3f, "Ring is not visible red.");
            Require(Get<float>(ring, "lineWidth") > .22f && Get<float>(ring, "lineWidth") < .4f, "Width is not slightly above player ring.");

            var chase = ring.GetComponent<CreatureChase>();
            Require(chase.TryGetKingLeash(out Vector3 center, out float radius), "No king boundary.");
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            float maxError = 0f;
            for (int i = 0; i < vertices.Length; i += 2)
            {
                float inner = PlanetSurfacePose.GetSurfaceDistance(planet.Center, center, renderer.transform.TransformPoint(vertices[i]));
                float outer = PlanetSurfacePose.GetSurfaceDistance(planet.Center, center, renderer.transform.TransformPoint(vertices[i + 1]));
                maxError = Mathf.Max(maxError, Mathf.Abs((inner + outer) * .5f - radius));
            }
            Require(maxError < .12f, "Ring does not match the actual surface leash: " + maxError);

            Vector3 oldPosition = ring.transform.position, visualPosition = renderer.transform.position;
            Quaternion oldRotation = ring.transform.rotation;
            ring.transform.SetPositionAndRotation(oldPosition + ring.transform.right * 8f, oldRotation * Quaternion.Euler(0, 60, 0));
            Tick(ring);
            Require(renderer.transform.position == visualPosition && mesh.vertices[0] == vertices[0], "Ring followed the king.");
            ring.transform.SetPositionAndRotation(oldPosition, oldRotation);
            ring.enabled = false;
            Require(!renderer.enabled, "Disabled king left its ring visible.");
            ring.enabled = true;
            Tick(ring);
            Require(renderer.enabled, "Re-enabled king lost its ring.");
            var creature = ring.GetComponent<Creature>();
            float health = creature.CurrentHealth;
            Set(creature, "_currentHealth", 0f);
            Tick(ring);
            Require(!renderer.enabled, "Dead king left a ring visible.");
            Set(creature, "_currentHealth", health);
            Tick(ring);
            File.AppendAllText(Report, $"PASS {ring.name}: red shared-material ring, width 0.32; max boundary error {maxError:F4}m; fixed while moving; disable/death visibility.\n");
            float distance = Vector3.Distance(center, player.transform.position);
            if (distance < nearest) { nearest = distance; featured = ring; }
        }
        Capture(featured, planet);
        File.AppendAllText(Report, "PASS all visual checks; overview saved.\n");
    }

    static void Capture(AreaKingLeashIndicator ring, SphericalPlanet planet)
    {
        ring.GetComponent<CreatureChase>().TryGetKingLeash(out Vector3 center, out _);
        Vector3 up = (center - planet.Center).normalized;
        Vector3 north = Vector3.ProjectOnPlane(Vector3.up, up).normalized;
        if (north.sqrMagnitude < .01f) north = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
        var go = new GameObject("Temporary king ring overview");
        var camera = go.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.enabled = false;
        camera.cullingMask &= ~LayerMask.GetMask("UI");
        camera.aspect = 1f;
        camera.fieldOfView = 60f;
        camera.transform.position = center + up * 65f - north * 12f;
        camera.transform.rotation = Quaternion.LookRotation(center - camera.transform.position, north);
        var target = RenderTexture.GetTemporary(1024, 1024, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var image = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
        try
        {
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
            image.Apply();
            File.WriteAllBytes("Logs/KingLeashIndicator-overview.png", image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(go);
        }
    }

    static T Get<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private).GetValue(obj);
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private).SetValue(obj, value);
    static void Tick(AreaKingLeashIndicator ring) => typeof(AreaKingLeashIndicator).GetMethod("LateUpdate", Private).Invoke(ring, null);
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
