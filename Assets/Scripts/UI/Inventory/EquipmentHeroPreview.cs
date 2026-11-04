using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Renders the player's body, playing the same Idle1/Idle2 cycle as in the world,
/// into the equipment hero slot. The camera and animator run only while that window is open.
/// </summary>
public class EquipmentHeroPreview : MonoBehaviour
{
    const string ModelResource = "Player/Models/Starbot_Animation_Running_On_Planet";
    const string ControllerResource = "Player/Animator/StarterAssetsThirdPerson";
    const string MaterialResource = "Player/Materials/Player";
    const string PortraitShaderResource = "HUD/Shaders/EquipmentPortrait";
    const string PreviewLayerName = "EquipmentPreview";
    const int PreviewLayerFallback = 9;
    const int PortraitTexelCap = 512;
    const int PlayerRenderingLayer = 129;

    static readonly int Idle1Hash = Animator.StringToHash("Idle1");
    static readonly int MovingHash = Animator.StringToHash("Moving");
    static readonly int IdleVariantHash = Animator.StringToHash("IdleVariant");
    static readonly int DeadHash = Animator.StringToHash("Dead");
    static readonly int HitHash = Animator.StringToHash("Hit");
    static readonly int AttackingHash = Animator.StringToHash("Attacking");
    static readonly int LowOxygenHash = Animator.StringToHash("LowOxygen");
    static readonly Color ClearKey = new Color(1f, 0f, 1f, 0f);

    // The rig faces +Z, same as the player. The camera stands on that side.
    static readonly Vector3 StageOrigin = new Vector3(8000f, 8000f, 8000f);

    [SerializeField] RawImage portrait;
    bool _ownsPortraitMaterial;
    GameObject _stage;
    GameObject _body;
    Camera _camera;
    Light _light;
    RenderTexture _target;
    Animator _animator;
    int _layer;
    bool _shown;
    bool _framed;
    bool _scaled;
    bool _straight;
    bool _missingLogged;

    void OnEnable()
    {
        BindPortrait();
    }

    void OnDisable()
    {
        HideStage();
    }

    void OnDestroy()
    {
        HideStage();
        if (_stage != null)
            Destroy(_stage);
        if (_ownsPortraitMaterial && portrait != null)
            Destroy(portrait.material);
    }

    void LateUpdate()
    {
        if (!_shown || _camera == null)
            return;

        if (!_framed)
            Frame();
        if (_target == null || PortraitSizeChanged())
            Present();
        if (_target != null && !_camera.enabled)
            _camera.enabled = true;
    }

    public void SetVisible(bool visible)
    {
        BindPortrait();
        if (!visible)
        {
            HideStage();
            if (portrait != null)
                portrait.enabled = false;
            return;
        }

        if (!EnsureBuilt())
        {
            if (portrait != null)
                portrait.enabled = false;
            return;
        }

        _shown = true;
        _stage.SetActive(true);
        IsolateFromWorld();
        PoseIdle();
        if (!_framed)
            Frame();
        Present();
        _camera.enabled = _target != null;
        if (portrait != null)
            portrait.enabled = _target != null;
        ToonOutlineSubjects.MarkPlayer(_body.transform);
    }

    void HideStage()
    {
        _shown = false;
        if (_body != null)
            ToonOutlineSubjects.Unmark(_body.transform);
        if (_camera != null)
        {
            _camera.targetTexture = null;
            _camera.enabled = false;
        }

        if (portrait != null)
            portrait.texture = null;
        if (_target != null)
        {
            _target.Release();
            Destroy(_target);
            _target = null;
        }

        if (_stage != null)
            _stage.SetActive(false);
    }

    void BindPortrait()
    {
        if (portrait == null)
        {
            Transform existing = transform.Find("Portrait");
            if (existing != null)
                portrait = existing.GetComponent<RawImage>();
        }

        if (portrait == null || _ownsPortraitMaterial)
            return;

        Material current = portrait.material;
        if (current != null && current.HasProperty("_KeyColor"))
            return;

        Shader shader = Resources.Load<Shader>(PortraitShaderResource);
        if (shader == null)
            return;

        var created = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        created.SetColor("_KeyColor", new Color(1f, 0f, 1f, 1f));
        portrait.material = created;
        _ownsPortraitMaterial = true;
    }

    bool EnsureBuilt()
    {
        if (_stage != null)
            return _animator != null;

        GameObject modelPrefab = Resources.Load<GameObject>(ModelResource);
        RuntimeAnimatorController controller = Resources.Load<RuntimeAnimatorController>(ControllerResource);
        if (modelPrefab == null || controller == null)
        {
            LogMissing();
            return false;
        }

        _layer = LayerMask.NameToLayer(PreviewLayerName);
        if (_layer < 0)
            _layer = PreviewLayerFallback;

        _stage = new GameObject("EquipmentHeroStage")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        _stage.transform.SetPositionAndRotation(StageOrigin, Quaternion.identity);
        _stage.SetActive(false);

        _body = Instantiate(modelPrefab, _stage.transform);
        _body.name = "HeroModel";
        _body.tag = "Untagged";
        _body.transform.localPosition = Vector3.zero;
        _body.transform.localRotation = Quaternion.identity;
        _body.transform.localScale = Vector3.one;
        StripToVisual(_body);
        ApplyPlayerLook(_body);
        SetLayer(_body, _layer);

        _animator = _body.GetComponent<Animator>();
        if (_animator == null)
            _animator = _body.AddComponent<Animator>();
        Animator sourceAnimator = modelPrefab.GetComponent<Animator>();
        if (_animator.avatar == null && sourceAnimator != null)
            _animator.avatar = sourceAnimator.avatar;
        _animator.runtimeAnimatorController = controller;
        _animator.applyRootMotion = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        _animator.enabled = true;

        var cameraGo = new GameObject("HeroCamera");
        cameraGo.transform.SetParent(_stage.transform, false);
        _camera = cameraGo.AddComponent<Camera>();
        _camera.enabled = false;
        _camera.orthographic = true;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = ClearKey;
        _camera.cullingMask = 1 << _layer;
        _camera.allowHDR = false;
        _camera.allowMSAA = false;
        _camera.useOcclusionCulling = false;
        _camera.depth = -50f;
        _camera.nearClipPlane = 0.05f;
        _camera.farClipPlane = 30f;

        UniversalAdditionalCameraData cameraData = _camera.GetUniversalAdditionalCameraData();
        cameraData.renderType = CameraRenderType.Base;
        cameraData.renderShadows = false;
        cameraData.renderPostProcessing = false;
        cameraData.antialiasing = AntialiasingMode.None;
        cameraData.requiresColorOption = CameraOverrideOption.Off;
        cameraData.requiresDepthOption = CameraOverrideOption.Off;
        cameraData.allowXRRendering = false;

        var lightGo = new GameObject("HeroLight");
        lightGo.transform.SetParent(_stage.transform, false);
        _light = lightGo.AddComponent<Light>();
        _light.type = LightType.Directional;
        _light.color = Color.white;
        // Kept under the scene sun so gameplay that picks the brightest directional
        // never treats this portrait light as the world sun. The preview camera
        // still uses it: every other light is masked off this layer.
        _light.intensity = 1.15f;
        _light.shadows = LightShadows.None;
        _light.cullingMask = 1 << _layer;
        _light.transform.rotation = Quaternion.LookRotation(new Vector3(0.2f, -0.55f, -1f));

        // A pose in front of the body, so the first frame is not shot from inside the mesh
        // when renderer bounds are still empty.
        _camera.transform.localPosition = new Vector3(0f, 0.95f, 2.6f);
        _camera.transform.localRotation = Quaternion.Euler(6f, 180f, 0f);
        _camera.orthographicSize = 1.05f;

        return true;
    }

    void PoseIdle()
    {
        if (_animator == null)
            return;

        _animator.Rebind();
        _animator.Update(0f);
        _animator.SetBool(MovingHash, false);
        _animator.SetBool(DeadHash, false);
        _animator.SetBool(HitHash, false);
        _animator.SetBool(AttackingHash, false);
        _animator.SetBool(LowOxygenHash, false);
        _animator.SetInteger(IdleVariantHash, 0);
        if (_animator.HasState(0, Idle1Hash))
            _animator.Play(Idle1Hash, 0, 0f);
        _animator.Update(0f);
    }

    void Frame()
    {
        if (_body == null || _camera == null)
            return;

        FitScaleOnce();
        StraightenOnce();

        Vector3 center = _body.transform.position;
        float minY = center.y;
        float maxY = center.y + 1.75f;
        float halfWidth = 0.45f;
        if (!TryVisualBounds(out Bounds mesh))
        {
            Transform hips = FindNamed(_body.transform, "Hips");
            Transform head = FindNamed(_body.transform, "Head");
            if (hips != null && head != null)
            {
                float torso = Mathf.Abs(head.position.y - hips.position.y);
                if (torso > 0.02f)
                {
                    // Head and hips sit inside the helmet and pelvis, so the frame
                    // has to reach past both bones or the helmet and boots get cut.
                    minY = hips.position.y - torso * 1.8f;
                    maxY = head.position.y + torso * 1.25f;
                    center = hips.position;
                    halfWidth = torso * 0.95f;
                }
            }
        }
        else
        {
            minY = mesh.min.y;
            maxY = mesh.max.y;
            center = mesh.center;
            halfWidth = mesh.extents.x;
        }

        float span = Mathf.Max(0.5f, maxY - minY);
        float viewMin = minY - span * 0.12f;
        float viewMax = maxY + span * 0.14f;
        float aspect = PortraitAspect();
        float ortho = (viewMax - viewMin) * 0.5f;
        ortho = Mathf.Max(ortho, (halfWidth * 1.15f) / Mathf.Max(0.2f, aspect));

        var look = new Vector3(center.x, (viewMin + viewMax) * 0.5f, center.z);
        Vector3 position = look + new Vector3(0f, 0f, Mathf.Max(2.2f, span * 1.6f));
        _camera.orthographic = true;
        _camera.orthographicSize = ortho;
        _camera.nearClipPlane = 0.01f;
        _camera.farClipPlane = Mathf.Max(10f, span * 4f);
        _camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(look - position, Vector3.up));
        _framed = true;
    }

    bool TryVisualBounds(out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        Vector3 body = _body.transform.position;
        SkinnedMeshRenderer[] skins = _body.GetComponentsInChildren<SkinnedMeshRenderer>();
        for (int i = 0; i < skins.Length; i++)
        {
            SkinnedMeshRenderer skin = skins[i];
            if (skin == null || !skin.enabled)
                continue;

            Bounds candidate = skin.bounds;
            if (candidate.size.y < 0.05f || candidate.size.y > 20f)
                continue;
            if ((candidate.center - body).sqrMagnitude > 25f)
                continue;

            if (!any)
                bounds = candidate;
            else
                bounds.Encapsulate(candidate);
            any = true;
        }

        return any;
    }

    void FitScaleOnce()
    {
        if (_scaled || _body == null)
            return;

        _scaled = true;
        Transform hips = FindNamed(_body.transform, "Hips");
        Transform head = FindNamed(_body.transform, "Head");
        if (hips == null || head == null)
            return;

        float torso = Mathf.Abs(head.position.y - hips.position.y);
        if (torso < 0.05f || torso > 2.2f)
            _body.transform.localScale *= 0.55f / Mathf.Max(torso, 0.0001f);
    }

    void StraightenOnce()
    {
        if (_straight || _body == null)
            return;

        _straight = true;
        Transform hips = FindNamed(_body.transform, "Hips");
        Transform head = FindNamed(_body.transform, "Head");
        if (hips == null || head == null)
            return;

        // Level the body in the portrait. Idle still plays on the bones after this.
        Vector3 up = head.position - hips.position;
        Vector3 onScreen = Vector3.ProjectOnPlane(up, Vector3.forward);
        if (onScreen.sqrMagnitude < 0.0001f)
            return;

        Quaternion fix = Quaternion.FromToRotation(onScreen, Vector3.up);
        _body.transform.rotation = fix * _body.transform.rotation;
    }

    static Transform FindNamed(Transform root, string exact)
    {
        if (root.name == exact)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamed(root.GetChild(i), exact);
            if (found != null)
                return found;
        }

        return null;
    }

    void Present()
    {
        if (portrait == null || _camera == null)
            return;

        Canvas.ForceUpdateCanvases();
        if (!TryPortraitPixels(out int width, out int height))
            return;

        if (_target == null || _target.width != width || _target.height != height)
        {
            if (_target != null)
            {
                _camera.targetTexture = null;
                _target.Release();
                Destroy(_target);
            }

            // 24-bit depth keeps the stencil the outline mask needs. A 16-bit
            // depth buffer has no stencil, so the ink pass paints the whole body black.
            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "EquipmentHero",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.D24_UNorm_S8_UInt
            };
            _target.Create();
            _camera.targetTexture = _target;
            portrait.texture = _target;
        }

        portrait.enabled = true;
    }

    bool PortraitSizeChanged()
    {
        if (_target == null || !TryPortraitPixels(out int width, out int height))
            return false;
        return _target.width != width || _target.height != height;
    }

    bool TryPortraitPixels(out int width, out int height)
    {
        width = 0;
        height = 0;
        if (portrait == null)
            return false;

        Rect rect = portrait.rectTransform.rect;
        if (rect.width < 8f || rect.height < 8f)
            return false;

        Canvas canvas = portrait.canvas;
        float scale = canvas != null ? Mathf.Max(1f, canvas.scaleFactor) : 1f;
        float pixelW = rect.width * scale;
        float pixelH = rect.height * scale;
        float down = Mathf.Max(1f, Mathf.Max(pixelW, pixelH) / PortraitTexelCap);
        width = Mathf.Max(64, Mathf.RoundToInt(pixelW / down));
        height = Mathf.Max(64, Mathf.RoundToInt(pixelH / down));
        return true;
    }

    float PortraitAspect()
    {
        if (TryPortraitPixels(out int width, out int height) && height > 0)
            return width / (float)height;
        return 0.7f;
    }

    void IsolateFromWorld()
    {
        int bit = 1 << _layer;
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam == null || cam == _camera)
                continue;
            cam.cullingMask &= ~bit;
        }

        Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Include);
        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light == null || light == _light)
                continue;
            light.cullingMask &= ~bit;
        }
    }

    static void ApplyPlayerLook(GameObject body)
    {
        Material material = Resources.Load<Material>(MaterialResource);
        SkinnedMeshRenderer[] skins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skins.Length; i++)
        {
            SkinnedMeshRenderer skin = skins[i];
            if (material != null)
                skin.sharedMaterial = material;
            skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            skin.receiveShadows = false;
            skin.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            skin.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            skin.updateWhenOffscreen = true;
            skin.renderingLayerMask = PlayerRenderingLayer;
        }
    }

    static void StripToVisual(GameObject root)
    {
        Component[] components = root.GetComponentsInChildren<Component>(true);
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null)
                continue;
            if (component is Transform || component is Animator || component is SkinnedMeshRenderer || component is MeshRenderer || component is MeshFilter)
                continue;
            DestroyImmediate(component);
        }
    }

    static void SetLayer(GameObject go, int layer)
    {
        go.layer = layer;
        Transform root = go.transform;
        for (int i = 0; i < root.childCount; i++)
            SetLayer(root.GetChild(i).gameObject, layer);
    }

    void LogMissing()
    {
        if (_missingLogged)
            return;
        _missingLogged = true;
        Debug.LogWarning($"{nameof(EquipmentHeroPreview)}: player model or animator controller is missing.", this);
    }
}
