using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Picks the Phone quality level on handhelds, including phones that open the WebGL build.
/// Desktop and the editor keep the PC pipeline.
/// </summary>
public static class PhonePerformance
{
    public static bool IsPhone { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        var go = new GameObject(nameof(PhonePerformance));
        go.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(go);
        go.AddComponent<PhonePerformanceDriver>();
        ApplyIfPhone();
    }

    internal static bool ApplyIfPhone()
    {
        if (IsPhone)
            return true;
        if (!Detect())
            return false;

        IsPhone = true;
        ApplyQuality();
        bool web = Application.platform == RuntimePlatform.WebGLPlayer;
        Application.targetFrameRate = web ? 30 : 60;
        QualitySettings.vSyncCount = 0;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
        QualitySettings.skinWeights = SkinWeights.TwoBones;
        QualitySettings.pixelLightCount = 1;
        if (web)
            QualitySettings.lodBias = 0.4f;
        else
            QualitySettings.lodBias = Mathf.Min(QualitySettings.lodBias, 0.6f);

        QualitySettings.shadows = UnityEngine.ShadowQuality.Disable;
        QualitySettings.shadowDistance = 0f;
        QualitySettings.shadowCascades = 1;
        Physics.defaultSolverIterations = 4;
        Shader.SetGlobalFloat("_PhoneQuality", 1f);
        return true;
    }

    static void ApplyQuality()
    {
        string[] names = QualitySettings.names;
        for (int i = 0; i < names.Length; i++)
        {
            if (names[i] != "Phone")
                continue;
            if (QualitySettings.GetQualityLevel() != i)
                QualitySettings.SetQualityLevel(i, true);
            return;
        }
    }

    static bool Detect()
    {
        // The itch.io test build is a phone build. Browser OS strings often look like a desktop.
        if (Application.platform == RuntimePlatform.WebGLPlayer)
            return true;
        if (Application.isMobilePlatform)
            return true;
        if (SystemInfo.deviceType == DeviceType.Handheld)
            return true;

        string os = SystemInfo.operatingSystem ?? string.Empty;
        string model = SystemInfo.deviceModel ?? string.Empty;
        if (Has(os, "Android") || Has(os, "iPhone") || Has(os, "iPad") || Has(os, "iOS"))
            return true;
        if (Has(model, "Android") || Has(model, "iPhone") || Has(model, "iPad"))
            return true;

        // A phone browser often reports a desktop OS, but the short side is only a few inches.
        if (Application.platform == RuntimePlatform.WebGLPlayer && Input.touchSupported && Screen.dpi > 80f)
        {
            float inches = Mathf.Min(Screen.width, Screen.height) / Screen.dpi;
            if (inches > 0f && inches < 7.2f)
                return true;
        }

        return false;
    }

    static bool Has(string text, string value)
    {
        return text.IndexOf(value, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

sealed class PhonePerformanceDriver : MonoBehaviour
{
    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Start()
    {
        if (!PhonePerformance.ApplyIfPhone())
            return;

        ApplyResolution();
        enabled = false;
    }

    void Update()
    {
        if (PhonePerformance.ApplyIfPhone())
        {
            ApplyResolution();
            enabled = false;
            return;
        }

        if (Time.frameCount > 8)
            enabled = false;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!PhonePerformance.IsPhone)
            return;
        ApplyResolution();
    }

    static void ApplyResolution()
    {
        // The WebGL canvas is already a small phone buffer. Don't scale it a second time.
        float scale = 1f;
        if (Application.platform != RuntimePlatform.WebGLPlayer)
        {
            int pixels = Mathf.Max(1, Screen.width * Screen.height);
            scale = 0.75f;
            if (pixels >= 2800000)
                scale = 0.5f;
            else if (pixels >= 1800000)
                scale = 0.6f;
            else if (pixels >= 1200000)
                scale = 0.7f;
        }

        // Writing renderScale in the editor marks the pipeline asset dirty.
        if (!Application.isEditor
            && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            urp.renderScale = scale;

        Camera[] cameras = Camera.allCameras;
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam == null)
                continue;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            if (Application.platform != RuntimePlatform.WebGLPlayer)
                continue;
            UniversalAdditionalCameraData extra = cam.GetUniversalAdditionalCameraData();
            if (extra == null)
                continue;
            extra.renderShadows = false;
        }
    }
}
