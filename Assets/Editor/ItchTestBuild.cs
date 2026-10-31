using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ItchTestBuild
{
    const string FlagPath = "Builds/request-itch-build.flag";
    const string ResultPath = "Builds/itch-build-result.txt";
    const string OutputDir = "Builds/itch-mobile-release";
    static readonly Dictionary<string, TextureImporterPlatformSettings> SavedTextures = new();
    static readonly Dictionary<string, byte[]> SavedImportMetadata = new();

    static ItchTestBuild()
    {
        EditorApplication.update += MaybeBuild;
    }

    public static void BuildFromCommandLine()
    {
        try
        {
            Build();
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            WriteResult("Failed\n" + ex);
            Debug.LogError("ItchTestBuild: WebGL player was not produced. " + ex.Message);
        }

        EditorApplication.Exit(0);
    }

    static void MaybeBuild()
    {
        var flag = Path.GetFullPath(FlagPath);
        if (!File.Exists(flag))
            return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.isPlaying = false;
            return;
        }

        try
        {
            File.Delete(flag);
        }
        catch (IOException ex)
        {
            Debug.LogError("ItchTestBuild: could not clear the request flag. " + ex.Message);
            return;
        }

        try { Build(); }
        catch (System.Exception ex)
        {
            WriteResult("Failed\n" + ex);
            Debug.LogException(ex);
        }
    }

    static void Build()
    {
        WriteResult("Building\noutput=" + Path.GetFullPath(OutputDir));
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            WriteResult("Failed\nNo scenes are enabled in Build Settings.");
            return;
        }

        MobileReleaseTools.PrepareDamageText();
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
        {
            WriteResult("Unsupported\nWebGL Build Support is not available in this editor session.");
            Debug.LogError("ItchTestBuild: WebGL Build Support is not available.");
            return;
        }

        PlayerSettings.productName = "BackHome";
        PlayerSettings.defaultWebScreenWidth = 450;
        PlayerSettings.defaultWebScreenHeight = 800;
        // itch.io serves .br files with Content-Encoding: br. Native browser
        // decompression avoids the larger JavaScript fallback and compresses .data too.
        var previousCompression = PlayerSettings.WebGL.compressionFormat;
        var previousFallback = PlayerSettings.WebGL.decompressionFallback;
        var previousPower = PlayerSettings.WebGL.powerPreference;
        var previousExceptions = PlayerSettings.WebGL.exceptionSupport;
        var previousSubtarget = EditorUserBuildSettings.webGLBuildSubtarget;
        var previousOptimization = UnityEditor.WebGL.UserBuildSettings.codeOptimization;
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = false;
        PlayerSettings.WebGL.powerPreference = WebGLPowerPreference.Default;
        UnityEditor.WebGL.UserBuildSettings.codeOptimization = UnityEditor.WebGL.WasmCodeOptimization.DiskSizeLTO;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
        // ASTC is not supported by desktop Chrome, so those textures were unpacked into
        // raw memory and the fixed heap aborted. Generic keeps sizes predictable, and
        // growth lets a computer expand while the phone still starts from a 256 MB block.
        EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.Generic;

        var previousGrowth = PlayerSettings.WebGL.memoryGrowthMode;
        var previousInitialMemory = PlayerSettings.WebGL.initialMemorySize;
        var previousMaximumMemory = PlayerSettings.WebGL.maximumMemorySize;
        var previousGrowthStep = PlayerSettings.WebGL.geometricMemoryGrowthStep;
        PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
        PlayerSettings.WebGL.initialMemorySize = 256;
        PlayerSettings.WebGL.maximumMemorySize = 2048;
        PlayerSettings.WebGL.geometricMemoryGrowthStep = 0.2f;

        var previousQuality = QualitySettings.GetQualityLevel();
        var phoneQuality = System.Array.IndexOf(QualitySettings.names, "Phone");
        if (phoneQuality >= 0)
            QualitySettings.SetQualityLevel(phoneQuality, false);

        var meshes = new MeshCompressionPass();
        var mobile = new MobileGraphics();
        var changedTextures = 0;
        var compressedMeshes = 0;
        var graphicsApplied = false;
        BuildReport report = null;
        var output = Path.GetFullPath(OutputDir);
        try
        {
            changedTextures = CapWebGlTextures(128);
            compressedMeshes = meshes.Apply();
            mobile.Apply();
            graphicsApplied = true;

            ClearOutput(output);

            Debug.Log("ItchTestBuild: starting mobile WebGL build. Textures capped: " + changedTextures
                + ", meshes compressed: " + compressedMeshes + " -> " + output);

            AssetDatabase.SaveAssets();
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
        }
        finally
        {
            if (graphicsApplied)
                mobile.Restore();
            meshes.Restore();
            RestoreTextures();
            RestoreImportMetadata();
            PlayerSettings.WebGL.memoryGrowthMode = previousGrowth;
            PlayerSettings.WebGL.initialMemorySize = previousInitialMemory;
            PlayerSettings.WebGL.maximumMemorySize = previousMaximumMemory;
            PlayerSettings.WebGL.geometricMemoryGrowthStep = previousGrowthStep;
            PlayerSettings.WebGL.compressionFormat = previousCompression;
            PlayerSettings.WebGL.decompressionFallback = previousFallback;
            PlayerSettings.WebGL.powerPreference = previousPower;
            PlayerSettings.WebGL.exceptionSupport = previousExceptions;
            EditorUserBuildSettings.webGLBuildSubtarget = previousSubtarget;
            UnityEditor.WebGL.UserBuildSettings.codeOptimization = previousOptimization;
            if (phoneQuality >= 0)
                QualitySettings.SetQualityLevel(previousQuality, false);
            AssetDatabase.SaveAssets();
        }

        if (report == null)
        {
            WriteResult("Failed\nBuildPlayer did not return a report.");
            Debug.LogError("ItchTestBuild: WebGL player was not produced. No build report.");
            return;
        }

        var summary = report.summary;
        var message = summary.result
            + "\nerrors=" + summary.totalErrors
            + "\nwarnings=" + summary.totalWarnings
            + "\noutput=" + output
            + "\nbytes=" + summary.totalSize
            + "\nseconds=" + summary.totalTime.TotalSeconds.ToString("0");

        if (summary.result != BuildResult.Succeeded && report.steps != null)
        {
            foreach (var step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        message += "\n" + msg.content;
                }
            }
        }

        var indexPath = Path.Combine(output, "index.html");
        if (summary.result != BuildResult.Succeeded || !File.Exists(indexPath))
        {
            message = "Failed\n" + message + "\nindex.html=" + File.Exists(indexPath);
            WriteResult(message);
            Debug.LogError("ItchTestBuild: WebGL player was not produced. " + message);
            return;
        }

        PatchPhonePage(indexPath);
        WriteResult(message);
        Debug.Log("ItchTestBuild: succeeded. " + indexPath);
    }

    // Rebuilds the itch.io browser player while the editor is open.
    // The request file is Builds/request-itch-build.flag.

    static void PatchPhonePage(string indexPath)
    {
        var html = File.ReadAllText(indexPath);
        if (html.IndexOf("name=\"viewport\"", System.StringComparison.Ordinal) < 0)
        {
            html = html.Replace(
                "<meta charset=\"utf-8\">",
                "<meta charset=\"utf-8\">\n    <meta name=\"viewport\" content=\"width=device-width, height=device-height, initial-scale=1.0, maximum-scale=1.0, user-scalable=no, viewport-fit=cover\">");
        }

        if (html.IndexOf("matchWebGLToCanvasSize: false", System.StringComparison.Ordinal) < 0)
        {
            html = html.Replace(
                "showBanner: unityShowBanner,",
                "showBanner: unityShowBanner,\n        matchWebGLToCanvasSize: false,\n        devicePixelRatio: 1,");
        }

        var branch = html.IndexOf("if (/iPhone|iPad|iPod|Android/i.test", System.StringComparison.Ordinal);
        var script = html.IndexOf("var script = document.createElement", System.StringComparison.Ordinal);
        if (branch > 0 && script > branch)
        {
            html = html.Remove(branch, script - branch);
            html = html.Insert(branch, @"function fitPhoneFrame() {
        var w = window.innerWidth;
        var h = window.innerHeight;
        var frameW = w;
        var frameH = Math.floor(w * 16 / 9);
        if (frameH > h) {
          frameH = h;
          frameW = Math.floor(h * 9 / 16);
        }
        fullscreenContainer.style.width = frameW + ""px"";
        fullscreenContainer.style.height = frameH + ""px"";
      }
      window.addEventListener(""resize"", fitPhoneFrame);
      window.addEventListener(""orientationchange"", fitPhoneFrame);
      fitPhoneFrame();


      ");
        }

        File.WriteAllText(indexPath, html);

        var cssPath = Path.Combine(Path.GetDirectoryName(indexPath), "TemplateData", "style.css");
        if (!File.Exists(cssPath))
            return;

        var css = File.ReadAllText(cssPath);
        css = css.Replace(
            "body { padding: 0; margin: 0 }",
            "body { padding: 0; margin: 0; background: #111318; overflow: hidden; overscroll-behavior: none }\n#unity-canvas { touch-action: none }");
        css = css.Replace(
            "#unity-footer { position: relative }\n.unity-mobile #unity-footer { display: none }",
            "#unity-footer { display: none }");
        File.WriteAllText(cssPath, css);
    }

    static void ClearOutput(string output)
    {
        string expected = Path.GetFullPath(OutputDir).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar), expected,
            System.StringComparison.OrdinalIgnoreCase)
            || !expected.StartsWith(Path.GetFullPath("Builds") + Path.DirectorySeparatorChar,
                System.StringComparison.OrdinalIgnoreCase))
            throw new System.InvalidOperationException("Refusing to clear a path outside this build output.");
        if (!Directory.Exists(output))
        {
            Directory.CreateDirectory(output);
            return;
        }

        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Directory.Delete(output, true);
                break;
            }
            catch (IOException)
            {
                System.Threading.Thread.Sleep(400);
            }
        }

        if (Directory.Exists(output))
        {
            foreach (var file in Directory.GetFiles(output, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
        }

        Directory.CreateDirectory(output);
    }

    static int CapWebGlTextures(int maxSize)
    {
        var guids = AssetDatabase.FindAssets("t:Texture", new[] { "Assets/Resources" });
        var changed = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("/Editor/", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;

                // Preserve legibility of UI/font atlases and recognizable hero/ship details.
                if (path.IndexOf("font", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("atlas", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                int limit = path.StartsWith("Assets/Resources/Ship/") ? 512
                    : path.StartsWith("Assets/Resources/Player/") || importer.textureType == TextureImporterType.Sprite ? 256
                    : maxSize;
                if (importer.textureType == TextureImporterType.NormalMap)
                    limit = Mathf.Max(64, limit / 2);
                var web = importer.GetPlatformTextureSettings("WebGL");
                limit = Mathf.Min(limit, importer.maxTextureSize);
                if (web.overridden && web.maxTextureSize == limit && web.format == TextureImporterFormat.RGBA32)
                    continue;

                SavedTextures[path] = importer.GetPlatformTextureSettings("WebGL");
                SaveImportMetadata(path);
                web.name = "WebGL";
                web.overridden = true;
                web.maxTextureSize = limit;
                // Uncompressed so a computer does not unpack ASTC into a second, much larger copy.
                web.format = TextureImporterFormat.RGBA32;
                web.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SetPlatformTextureSettings(web);
                importer.SaveAndReimport();
                changed++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        return changed;
    }

    static void RestoreTextures()
    {
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var saved in SavedTextures)
            {
                if (AssetImporter.GetAtPath(saved.Key) is not TextureImporter importer) continue;
                importer.SetPlatformTextureSettings(saved.Value);
                importer.SaveAndReimport();
            }
        }
        finally { SavedTextures.Clear(); AssetDatabase.StopAssetEditing(); }
    }

    static void SaveImportMetadata(string assetPath)
    {
        string meta = assetPath + ".meta";
        if (!SavedImportMetadata.ContainsKey(meta))
            SavedImportMetadata.Add(meta, File.ReadAllBytes(meta));
    }

    static void RestoreImportMetadata()
    {
        // Importer serialization may normalize disabled overrides and unrelated
        // humanoid metadata. Preserve the exact authored files as well.
        foreach (var entry in SavedImportMetadata)
            File.WriteAllBytes(entry.Key, entry.Value);
        SavedImportMetadata.Clear();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }

    sealed class MeshCompressionPass
    {
        struct Saved
        {
            public string Path;
            public ModelImporterMeshCompression Compression;
        }

        readonly List<Saved> _saved = new List<Saved>();

        public int Apply()
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Resources" });
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.IndexOf("/Editor/", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null || importer.meshCompression == ModelImporterMeshCompression.High)
                        continue;

                    _saved.Add(new Saved { Path = path, Compression = importer.meshCompression });
                    SaveImportMetadata(path);
                    importer.meshCompression = ModelImporterMeshCompression.High;
                    importer.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            return _saved.Count;
        }

        public void Restore()
        {
            if (_saved.Count == 0)
                return;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var saved in _saved)
                {
                    var importer = AssetImporter.GetAtPath(saved.Path) as ModelImporter;
                    if (importer == null)
                        continue;
                    importer.meshCompression = saved.Compression;
                    importer.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }
    }

    sealed class MobileGraphics
    {
        float _lodBias;
        float _shadowDistance;
        ShadowQuality _shadows;
        int _cascades;
        int _pixelLights;
        int _antiAliasing;
        readonly List<UrpValue> _urp = new List<UrpValue>();
        SerializedObject _pipeline;

        public void Apply()
        {
            _lodBias = QualitySettings.lodBias;
            _shadowDistance = QualitySettings.shadowDistance;
            _shadows = QualitySettings.shadows;
            _cascades = QualitySettings.shadowCascades;
            _pixelLights = QualitySettings.pixelLightCount;
            _antiAliasing = QualitySettings.antiAliasing;
            QualitySettings.lodBias = 0.4f;
            QualitySettings.shadowDistance = 0f;
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.shadowCascades = 1;
            QualitySettings.pixelLightCount = 1;
            QualitySettings.antiAliasing = 0;

            var pipeline = AssetDatabase.LoadAssetAtPath<Object>("Assets/Settings/Mobile_RPAsset.asset");
            if (pipeline == null)
                return;

            _pipeline = new SerializedObject(pipeline);
            SetBool("m_SupportsHDR", false);
            SetBool("m_RequireOpaqueTexture", false);
            SetBool("m_MainLightShadowsSupported", false);
            SetBool("m_AdditionalLightShadowsSupported", false);
            SetBool("m_SoftShadowsSupported", false);
            SetInt("m_MainLightShadowmapResolution", 256);
            SetInt("m_ShadowCascadeCount", 1);
            SetInt("m_AdditionalLightsPerObjectLimit", 1);
            SetFloat("m_ShadowDistance", 0f);
            SetFloat("m_RenderScale", 1f);
            _pipeline.ApplyModifiedPropertiesWithoutUndo();
        }

        public void Restore()
        {
            QualitySettings.lodBias = _lodBias;
            QualitySettings.shadowDistance = _shadowDistance;
            QualitySettings.shadows = _shadows;
            QualitySettings.shadowCascades = _cascades;
            QualitySettings.pixelLightCount = _pixelLights;
            QualitySettings.antiAliasing = _antiAliasing;

            if (_pipeline == null)
                return;

            foreach (var value in _urp)
            {
                var property = _pipeline.FindProperty(value.Name);
                if (property == null)
                    continue;
                switch (value.Kind)
                {
                    case 0: property.boolValue = value.BoolValue; break;
                    case 1: property.intValue = value.IntValue; break;
                    case 2: property.floatValue = value.FloatValue; break;
                }
            }

            _pipeline.ApplyModifiedPropertiesWithoutUndo();
        }

        void SetBool(string name, bool value)
        {
            var property = _pipeline.FindProperty(name);
            if (property == null)
                return;
            _urp.Add(new UrpValue { Name = name, Kind = 0, BoolValue = property.boolValue });
            property.boolValue = value;
        }

        void SetInt(string name, int value)
        {
            var property = _pipeline.FindProperty(name);
            if (property == null)
                return;
            _urp.Add(new UrpValue { Name = name, Kind = 1, IntValue = property.intValue });
            property.intValue = value;
        }

        void SetFloat(string name, float value)
        {
            var property = _pipeline.FindProperty(name);
            if (property == null)
                return;
            _urp.Add(new UrpValue { Name = name, Kind = 2, FloatValue = property.floatValue });
            property.floatValue = value;
        }

        struct UrpValue
        {
            public string Name;
            public int Kind;
            public bool BoolValue;
            public int IntValue;
            public float FloatValue;
        }
    }

    static void WriteResult(string message)
    {
        var path = Path.GetFullPath(ResultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, message);
    }
}
