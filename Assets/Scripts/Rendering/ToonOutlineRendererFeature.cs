using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Screen-space ink outline. Ink is drawn only where creatures, the player, and the
/// player's weapons stamped the stencil. Planet ground and environment props stay unmarked.
/// </summary>
public class ToonOutlineRendererFeature : ScriptableRendererFeature
{
    [SerializeField] Material material;

    ToonOutlinePass _pass;

    public override void Create()
    {
        _pass = new ToonOutlinePass();
        _pass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null)
            return;

        // The outline pass needs an extra full-screen target. Phones running the web build skip it.
        if (Application.platform == RuntimePlatform.WebGLPlayer)
            return;

        CameraType cameraType = renderingData.cameraData.cameraType;
        if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
            return;

        _pass.Setup(material);
        _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        _pass.requiresIntermediateTexture = true;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass = null;
    }

    sealed class ToonOutlinePass : ScriptableRenderPass
    {
        static readonly int DepthTextureId = Shader.PropertyToID("_CameraDepthTexture");

        Material _material;

        public ToonOutlinePass()
        {
            profilingSampler = new ProfilingSampler("Toon Outline");
        }

        public void Setup(Material outlineMaterial)
        {
            _material = outlineMaterial;
        }

        class MaskPassData
        {
            public RendererListHandle planets;
            public RendererListHandle subjects;
            public RendererListHandle kings;
        }

        class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle depth;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (_material == null || !resources.cameraColor.IsValid() || !resources.activeColorTexture.IsValid())
                return;
            if (!resources.cameraDepthTexture.IsValid())
                return;

            TextureDesc colorDesc = renderGraph.GetTextureDesc(resources.cameraColor);
            colorDesc.name = "_ToonOutlineColor";
            colorDesc.clearBuffer = false;
            TextureHandle source = renderGraph.CreateTexture(colorDesc);
            renderGraph.AddBlitPass(resources.cameraColor, source, Vector2.one, Vector2.zero, passName: "Toon Outline Copy");

            if (resources.activeDepthTexture.IsValid())
            {
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(
                    new ShaderTagId("PlanetStencil"), renderingData, cameraData, lightData, SortingCriteria.None);
                FilteringSettings filterSettings = new FilteringSettings(RenderQueueRange.opaque);
                RendererListParams rendererListParams = new RendererListParams(renderingData.cullResults, drawSettings, filterSettings);
                RendererListHandle planetMask = renderGraph.CreateRendererList(rendererListParams);

                DrawingSettings subjectDraw = RenderingUtils.CreateDrawingSettings(
                    new ShaderTagId("OutlineSubject"), renderingData, cameraData, lightData, SortingCriteria.None);
                FilteringSettings subjectFilter = new FilteringSettings(
                    RenderQueueRange.opaque, -1, ToonOutlineSubjects.RenderingLayer);
                RendererListParams subjectParams = new RendererListParams(renderingData.cullResults, subjectDraw, subjectFilter);
                RendererListHandle subjectMask = renderGraph.CreateRendererList(subjectParams);

                DrawingSettings kingDraw = RenderingUtils.CreateDrawingSettings(
                    new ShaderTagId("OutlineSubjectKing"), renderingData, cameraData, lightData, SortingCriteria.None);
                FilteringSettings kingFilter = new FilteringSettings(
                    RenderQueueRange.opaque, -1, ToonOutlineSubjects.KingRenderingLayer);
                RendererListParams kingParams = new RendererListParams(renderingData.cullResults, kingDraw, kingFilter);
                RendererListHandle kingMask = renderGraph.CreateRendererList(kingParams);

                using (IRasterRenderGraphBuilder maskBuilder = renderGraph.AddRasterRenderPass("Outline Mask", out MaskPassData maskData))
                {
                    maskData.planets = planetMask;
                    maskData.subjects = subjectMask;
                    maskData.kings = kingMask;
                    maskBuilder.UseRendererList(planetMask);
                    maskBuilder.UseRendererList(subjectMask);
                    maskBuilder.UseRendererList(kingMask);
                    maskBuilder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    maskBuilder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                    maskBuilder.SetRenderFunc(static (MaskPassData data, RasterGraphContext context) =>
                    {
                        context.cmd.DrawRendererList(data.planets);
                        context.cmd.DrawRendererList(data.subjects);
                        context.cmd.DrawRendererList(data.kings);
                    });
                }
            }

            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Toon Outline", out PassData passData, profilingSampler))
            {
                passData.material = _material;
                passData.source = source;
                passData.depth = resources.cameraDepthTexture;
                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.UseTexture(passData.depth, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                if (resources.activeDepthTexture.IsValid())
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(DepthTextureId, data.depth);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 1);
                });
            }
        }
    }
}

/// <summary>
/// Marks meshes that should receive the toon ink outline: creatures, the player, and weapons.
/// Regular subjects use light layer 7 (black ink). Area kings use light layer 8 (red ink).
/// </summary>
public static class ToonOutlineSubjects
{
    public const uint RenderingLayer = 1u << 7;
    public const uint KingRenderingLayer = 1u << 8;

    public static void Mark(Component root)
    {
        ApplyLayer(root, RenderingLayer, KingRenderingLayer);
    }

    public static void MarkKing(Component root)
    {
        ApplyLayer(root, KingRenderingLayer, RenderingLayer);
    }

    static void ApplyLayer(Component root, uint add, uint remove)
    {
        if (root == null)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            uint mask = (renderer.renderingLayerMask & ~remove) | add;
            if (renderer.renderingLayerMask != mask)
                renderer.renderingLayerMask = mask;
        }
    }
}
