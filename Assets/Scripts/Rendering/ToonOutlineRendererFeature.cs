using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Thin silhouette around creature bodies, the player, and the player's weapons.
/// Mobs and the player are black, area kings are red. Player thickness is its own
/// slider and covers both the body and the weapons. The line is drawn with its own
/// shader on those meshes. It does not add a fullscreen pass or an extra color target.
/// </summary>
public class ToonOutlineRendererFeature : ScriptableRendererFeature
{
    [SerializeField] Material material;

    ToonOutlinePass _pass;

    public override void Create()
    {
        _pass?.DestroyMaterials();
        _pass = new ToonOutlinePass();
        // Before the HP bar, which is transparent and tests no depth. The ink pass
        // writes depth so the space sky, drawn in the same transparent pass, cannot cover it.
        _pass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null || !ToonOutlineSubjects.Any)
            return;

        CameraType cameraType = renderingData.cameraData.cameraType;
        if (cameraType != CameraType.Game && cameraType != CameraType.SceneView)
            return;
        if (renderingData.cameraData.renderType != CameraRenderType.Base)
            return;

        _pass.Setup(material);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.DestroyMaterials();
        _pass = null;
    }

    sealed class ToonOutlinePass : ScriptableRenderPass
    {
        static readonly int MaterialPixelsId = Shader.PropertyToID("_OutlinePixels");
        static readonly int PlayerPixelsId = Shader.PropertyToID("_PlayerOutlinePixels");
        static readonly int MaterialFadeStartId = Shader.PropertyToID("_FadeStart");
        static readonly int MaterialFadeEndId = Shader.PropertyToID("_FadeEnd");
        static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        static readonly int KingColorId = Shader.PropertyToID("_KingOutlineColor");
        static readonly int UseKingId = Shader.PropertyToID("_UseKing");
        static readonly int UsePlayerId = Shader.PropertyToID("_UsePlayer");

        readonly List<Renderer> _mobs = new List<Renderer>(64);
        readonly List<Renderer> _kings = new List<Renderer>(8);
        readonly List<Renderer> _players = new List<Renderer>(16);
        readonly Plane[] _frustum = new Plane[6];

        Material _source;
        Material _mobMaterial;
        Material _kingMaterial;
        Material _playerMaterial;

        public ToonOutlinePass()
        {
            profilingSampler = new ProfilingSampler("Creature Outline");
        }

        public void Setup(Material outlineMaterial)
        {
            _source = outlineMaterial;
        }

        public void DestroyMaterials()
        {
            CoreUtils.Destroy(_mobMaterial);
            CoreUtils.Destroy(_kingMaterial);
            CoreUtils.Destroy(_playerMaterial);
            _mobMaterial = null;
            _kingMaterial = null;
            _playerMaterial = null;
        }

        class PassData
        {
            public Renderer[] mobs;
            public Renderer[] kings;
            public Renderer[] players;
            public Material mobMaterial;
            public Material kingMaterial;
            public Material playerMaterial;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_source == null)
                return;

            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (!resources.activeColorTexture.IsValid() || !resources.activeDepthTexture.IsValid())
                return;

            ToonOutlineSubjects.Copy(_mobs, _kings, _players);
            Camera camera = frameData.Get<UniversalCameraData>().camera;
            GeometryUtility.CalculateFrustumPlanes(camera, _frustum);
            CullInvisible(_mobs, camera);
            CullInvisible(_kings, camera);
            CullInvisible(_players, camera);
            if (_mobs.Count == 0 && _kings.Count == 0 && _players.Count == 0)
                return;

            ApplySettings(_source);

            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Creature Outline", out PassData passData, profilingSampler))
            {
                passData.mobs = _mobs.ToArray();
                passData.kings = _kings.ToArray();
                passData.players = _players.ToArray();
                passData.mobMaterial = _mobMaterial;
                passData.kingMaterial = _kingMaterial;
                passData.playerMaterial = _playerMaterial;

                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    DrawBodies(context.cmd, data.mobs, data.mobMaterial);
                    DrawBodies(context.cmd, data.kings, data.kingMaterial);
                    DrawBodies(context.cmd, data.players, data.playerMaterial);
                });
            }
        }

        void ApplySettings(Material source)
        {
            if (_mobMaterial == null || _kingMaterial == null || _playerMaterial == null || _mobMaterial.shader != source.shader)
            {
                DestroyMaterials();
                _mobMaterial = new Material(source) { name = "ToonOutline Mob", hideFlags = HideFlags.HideAndDontSave };
                _kingMaterial = new Material(source) { name = "ToonOutline King", hideFlags = HideFlags.HideAndDontSave };
                _playerMaterial = new Material(source) { name = "ToonOutline Player", hideFlags = HideFlags.HideAndDontSave };
            }

            Color mob = source.GetColor(OutlineColorId);
            Color king = source.GetColor(KingColorId);
            float pixels = source.GetFloat(MaterialPixelsId);
            float playerPixels = source.GetFloat(PlayerPixelsId);
            float fadeStart = source.GetFloat(MaterialFadeStartId);
            float fadeEnd = source.GetFloat(MaterialFadeEndId);
            if (pixels < 1f)
                pixels = 4f;
            if (playerPixels < 1f)
                playerPixels = 2f;
            if (fadeEnd <= fadeStart)
            {
                fadeStart = 90f;
                fadeEnd = 160f;
            }

            Apply(_mobMaterial, mob, king, pixels, playerPixels, fadeStart, fadeEnd, 0f, 0f);
            Apply(_kingMaterial, mob, king, pixels, playerPixels, fadeStart, fadeEnd, 1f, 0f);
            Apply(_playerMaterial, mob, king, pixels, playerPixels, fadeStart, fadeEnd, 0f, 1f);
        }

        void CullInvisible(List<Renderer> renderers, Camera camera)
        {
            for (int i = renderers.Count - 1; i >= 0; i--)
            {
                Renderer renderer = renderers[i];
                // Keep a generous margin for the expanded silhouette at the screen edge.
                Bounds bounds = renderer.bounds;
                bounds.Expand(2f);
                if (renderer.forceRenderingOff
                    || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0
                    || !GeometryUtility.TestPlanesAABB(_frustum, bounds))
                    renderers.RemoveAt(i);
            }
        }

        static void Apply(
            Material material,
            Color mob,
            Color king,
            float pixels,
            float playerPixels,
            float fadeStart,
            float fadeEnd,
            float useKing,
            float usePlayer)
        {
            material.SetColor(OutlineColorId, mob);
            material.SetColor(KingColorId, king);
            material.SetFloat(MaterialPixelsId, pixels);
            material.SetFloat(PlayerPixelsId, playerPixels);
            material.SetFloat(MaterialFadeStartId, fadeStart);
            material.SetFloat(MaterialFadeEndId, fadeEnd);
            material.SetFloat(UseKingId, useKing);
            material.SetFloat(UsePlayerId, usePlayer);
        }

        static void DrawBodies(RasterCommandBuffer cmd, Renderer[] renderers, Material material)
        {
            if (renderers == null || material == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                int submeshes = SubmeshCount(renderer);
                for (int pass = 0; pass < 3; pass++)
                {
                    for (int submesh = 0; submesh < submeshes; submesh++)
                        cmd.DrawRenderer(renderer, material, submesh, pass);
                }
            }
        }

        static int SubmeshCount(Renderer renderer)
        {
            Mesh mesh = null;
            if (renderer is SkinnedMeshRenderer skin)
                mesh = skin.sharedMesh;
            else if (renderer.TryGetComponent(out MeshFilter filter))
                mesh = filter.sharedMesh;

            if (mesh == null)
                return 1;

            return Mathf.Max(1, mesh.subMeshCount);
        }
    }
}

/// <summary>
/// Body renderers that receive the silhouette. Shadows, vitals, and VFX are skipped.
/// The player list uses its own thickness and includes the player's weapons.
/// </summary>
public static class ToonOutlineSubjects
{
    static readonly List<Renderer> Mobs = new List<Renderer>(64);
    static readonly List<Renderer> Kings = new List<Renderer>(8);
    static readonly List<Renderer> Players = new List<Renderer>(16);

    public static bool Any => Mobs.Count > 0 || Kings.Count > 0 || Players.Count > 0;

    public static void Mark(Component root)
    {
        if (root != null && root.GetComponent<AreaKing>() != null)
        {
            MarkKing(root);
            return;
        }

        Move(root, Mobs, Kings);
    }

    public static void MarkKing(Component root)
    {
        Move(root, Kings, Mobs);
        RemoveRoot(root, Players);
    }

    /// <summary>
    /// Black silhouette for the player body and every weapon mesh under it.
    /// Thickness comes from the outline material's player slider.
    /// </summary>
    public static void MarkPlayer(Component root)
    {
        RemoveRoot(root, Kings);
        Move(root, Players, Mobs);
    }

    public static void Unmark(Component root)
    {
        RemoveRoot(root, Mobs);
        RemoveRoot(root, Kings);
        RemoveRoot(root, Players);
    }

    public static void Copy(List<Renderer> mobs, List<Renderer> kings, List<Renderer> players)
    {
        CopyEnabled(Mobs, mobs);
        CopyEnabled(Kings, kings);
        CopyEnabled(Players, players);
    }

    static void Move(Component root, List<Renderer> add, List<Renderer> remove)
    {
        if (root == null)
            return;

        RemoveRoot(root, remove);
        RemoveRoot(root, add);

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Transform rootTransform = root.transform;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (IsBody(renderer, rootTransform))
                add.Add(renderer);
        }
    }

    static void RemoveRoot(Component root, List<Renderer> list)
    {
        if (root == null)
            return;

        Transform rootTransform = root.transform;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            Renderer renderer = list[i];
            if (renderer == null || renderer.transform == rootTransform || renderer.transform.IsChildOf(rootTransform))
                list.RemoveAt(i);
        }
    }

    static void CopyEnabled(List<Renderer> source, List<Renderer> dest)
    {
        dest.Clear();
        for (int i = source.Count - 1; i >= 0; i--)
        {
            Renderer renderer = source[i];
            if (renderer == null)
            {
                source.RemoveAt(i);
                continue;
            }

            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            dest.Add(renderer);
        }
    }

    internal static bool IsBody(Renderer renderer, Transform root)
    {
        if (renderer == null)
            return false;
        if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
            return false;
        if (renderer.GetComponentInParent<Canvas>(true) != null)
            return false;
        if (renderer.GetComponentInParent<HpBarView>(true) != null)
            return false;
        // The attack range is a ground overlay, not part of the player's silhouette.
        if (renderer.GetComponentInParent<PlayerRangeIndicator>(true) != null)
            return false;

        Material shared = renderer.sharedMaterial;
        if (shared != null && shared.shader != null && shared.shader.name == "BackHome/CreatureBlobShadow")
            return false;

        Transform cursor = renderer.transform;
        while (cursor != null && cursor != root)
        {
            string name = cursor.name;
            if (!string.IsNullOrEmpty(name)
                && (Has(name, "vfx") || Has(name, "slash") || Has(name, "trail")
                    || Has(name, "vitals") || Has(name, "popup") || Has(name, "shadow")))
                return false;
            cursor = cursor.parent;
        }

        return true;
    }

    static bool Has(string text, string value)
    {
        return text.IndexOf(value, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
