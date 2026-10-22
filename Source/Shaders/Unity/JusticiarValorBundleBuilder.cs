// Builds the RimWorld 1.6 asset bundles of Justiciar Valor and renders previews of its effect shaders.
// Copied into a Unity 2022.3 project (Assets/MRK/JusticiarValor/Editor) by Source/Shaders/build_shaders.py --unity;
// the shaders themselves are generated into Assets/Data/Mortis.JusticiarValor/Materials/JusticiarValor, the path
// RimWorld 1.6 looks ShaderTypeDef paths up under (ContentFinder<Shader>.TryFindAssetInModBundles).
//
//   Unity.exe -batchmode -quit -projectPath <project> -executeMethod JusticiarValorBundleBuilder.Build -jvOutput <mod>/1.6/AssetBundles
//   Unity.exe -batchmode -quit -projectPath <project> -executeMethod JusticiarValorBundleBuilder.Preview -jvPreviewOut <png> [-jvPreviewTexture <png>]
//       -jvTextureRoot <mod>/Common/Textures/Effects/JusticiarValor/Piloto -jvBladeTexture <the psyblade's texture png>
//   (Invoke-Unity.ps1 -Task Preview passes them, the blade's from the psyblade def's texPath.)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

public static class JusticiarValorBundleBuilder
{
    private const string ShaderRoot = "Assets/Data/Mortis.JusticiarValor/Materials/JusticiarValor";

    public static void Build()
    {
        string output = Arg("-jvOutput");
        if (string.IsNullOrEmpty(output))
        {
            throw new ArgumentException("Missing -jvOutput <mod>/1.6/AssetBundles");
        }
        string[] shaders = LoadCheckedShaders().Keys.ToArray();
        Directory.CreateDirectory(output);
        string temp = Path.Combine(Path.GetTempPath(), "justiciarvalor-build", "bundles");
        // Windows: Direct3D 11 is RimWorld 1.6's default; Vulkan and OpenGL Core cover -force-vulkan / -force-glcore.
        BuildBundle(BuildTarget.StandaloneWindows64, "justiciarvalor_win",
            new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLCore }, shaders, temp, output);
        if (BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
        {
            BuildBundle(BuildTarget.StandaloneOSX, "justiciarvalor_mac", new[] { GraphicsDeviceType.Metal }, shaders, temp, output);
        }
        else
        {
            Debug.LogWarning("[JusticiarValor] macOS build support is not installed; no justiciarvalor_mac bundle.");
        }
    }

    private static Dictionary<string, Shader> LoadCheckedShaders()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var result = new Dictionary<string, Shader>();
        foreach (string file in Directory.GetFiles(ShaderRoot, "*.shader").OrderBy(f => f, StringComparer.Ordinal))
        {
            string path = file.Replace('\\', '/');
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
            {
                throw new InvalidOperationException("Not a shader: " + path);
            }
            ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
            foreach (ShaderMessage message in messages)
            {
                Debug.Log($"[JusticiarValor] {path}:{message.line} {message.severity} {message.platform}: {message.message}");
            }
            if (ShaderUtil.ShaderHasError(shader) || messages.Any(m => m.severity == ShaderCompilerMessageSeverity.Error))
            {
                throw new InvalidOperationException("Shader has errors: " + path);
            }
            result.Add(path, shader);
        }
        if (result.Count == 0)
        {
            throw new InvalidOperationException("No shaders under " + ShaderRoot + "; run build_shaders.py --unity first.");
        }
        return result;
    }

    private static void BuildBundle(BuildTarget target, string bundleName, GraphicsDeviceType[] apis, string[] assets, string temp, string output)
    {
        bool usedDefaultApis = PlayerSettings.GetUseDefaultGraphicsAPIs(target);
        GraphicsDeviceType[] previousApis = PlayerSettings.GetGraphicsAPIs(target);
        try
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
            PlayerSettings.SetGraphicsAPIs(target, apis);
            // The manifest bundle is named after the output folder, which therefore must not share the bundle's name.
            string dir = Path.Combine(temp, "build-" + target);
            Directory.CreateDirectory(dir);
            var builds = new[] { new AssetBundleBuild { assetBundleName = bundleName, assetNames = assets } };
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(dir, builds,
                BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.StrictMode,
                target);
            if (manifest == null)
            {
                throw new InvalidOperationException("BuildAssetBundles failed for " + target);
            }
            string built = Path.Combine(dir, bundleName);
            File.Copy(built, Path.Combine(output, bundleName), true);
            Debug.Log($"[JusticiarValor] {bundleName} ({target}, {string.Join("/", apis)}): {assets.Length} shaders, {new FileInfo(built).Length} bytes");
        }
        finally
        {
            PlayerSettings.SetGraphicsAPIs(target, previousApis);
            PlayerSettings.SetUseDefaultGraphicsAPIs(target, usedDefaultApis);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Preview: every effect at several moments of its life over a RimWorld-like ground, one row per effect.

    private const int Cell = 256;

    private struct Frame
    {
        public string label;
        public Vector4 p0;
        public Vector4 p1;
        public Color color;
        public float width;
        public float height;
    }

    public static void Preview()
    {
        string output = Arg("-jvPreviewOut");
        if (string.IsNullOrEmpty(output))
        {
            throw new ArgumentException("Missing -jvPreviewOut <png>");
        }
        Texture2D pawnTex = null;
        string texPath = Arg("-jvPreviewTexture");
        if (!string.IsNullOrEmpty(texPath) && File.Exists(texPath))
        {
            pawnTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            pawnTex.LoadImage(File.ReadAllBytes(texPath));
            pawnTex.wrapMode = TextureWrapMode.Clamp;
        }
        Dictionary<string, Shader> shaders = LoadCheckedShaders();
        var rows = new List<KeyValuePair<Shader, Frame[]>>();
        foreach (Shader shader in shaders.Values)
        {
            rows.Add(new KeyValuePair<Shader, Frame[]>(shader, FramesFor(shader.name)));
        }
        int columns = rows.Max(r => r.Value.Length);
        int width = Cell * columns;
        int height = Cell * rows.Count;
        string textureRoot = Arg("-jvTextureRoot");
        string bladeTexture = Arg("-jvBladeTexture");
        var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        rt.Create();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D ground = MakeGround(width, height);
        Graphics.Blit(ground, rt);
        RenderTexture.active = rt;
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, width, 0, height);
        for (int row = 0; row < rows.Count; row++)
        {
            var material = new Material(rows[row].Key);
            string effectName = rows[row].Key.name.Substring(rows[row].Key.name.LastIndexOf('/') + 1);
            Texture2D atlas = null;
            if (effectName == "MJV_Afterimage" && pawnTex != null)
            {
                material.SetTexture("_MainTex", pawnTex);
            }
            else if (material.HasProperty("_MainTex"))
            {
                string atlasPath = effectName == "MJV_CelestialBlade"
                    ? bladeTexture
                    : Path.Combine(textureRoot ?? "", (SheetShaders.Contains(effectName) ? "Sheet" : effectName.Substring(4)) + ".png");
                if (string.IsNullOrEmpty(atlasPath) || !File.Exists(atlasPath)) throw new FileNotFoundException("Missing effect texture", atlasPath);
                atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                atlas.LoadImage(File.ReadAllBytes(atlasPath));
                atlas.wrapMode = TextureWrapMode.Clamp;
                material.SetTexture("_MainTex", atlas);
            }
            Frame[] frames = rows[row].Value;
            for (int col = 0; col < frames.Length; col++)
            {
                Frame f = frames[col];
                material.SetVector("_JVParams", f.p0);
                material.SetVector("_JVParams2", f.p1);
                material.SetColor("_Color", f.color);
                material.SetPass(0);
                float cx = col * Cell + Cell * 0.5f;
                float cy = height - (row * Cell + Cell * 0.5f);
                float hw = f.width * 0.5f;
                float hh = f.height * 0.5f;
                GL.Begin(GL.QUADS);
                GL.TexCoord2(0, 0); GL.Vertex3(cx - hw, cy - hh, 0);
                GL.TexCoord2(0, 1); GL.Vertex3(cx - hw, cy + hh, 0);
                GL.TexCoord2(1, 1); GL.Vertex3(cx + hw, cy + hh, 0);
                GL.TexCoord2(1, 0); GL.Vertex3(cx + hw, cy - hh, 0);
                GL.End();
            }
            UnityEngine.Object.DestroyImmediate(material);
            if (atlas != null) UnityEngine.Object.DestroyImmediate(atlas);
        }
        GL.PopMatrix();
        var readback = new Texture2D(width, height, TextureFormat.RGBA32, false);
        readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        readback.Apply();
        RenderTexture.active = previous;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        File.WriteAllBytes(output, readback.EncodeToPNG());
        rt.Release();
        Debug.Log($"[JusticiarValor] preview {width}x{height}: {output}");
    }

    // Shaders that sample the packed Piloto texture sheet (Source/Shaders/pack_sheet.py) rather than an atlas of their own.
    private static readonly HashSet<string> SheetShaders = new HashSet<string> { "MJV_Sprite", "MJV_OrbTrail", "MJV_GroundFissure", "MJV_ThreatPulse" };

    private static Frame[] FramesFor(string shaderName)
    {
        string name = shaderName.Substring(shaderName.LastIndexOf('/') + 1);
        bool loop = name == "MJV_PsyOrb";
        var frames = new List<Frame>();
        foreach (float t in new[] { 0.04f, 0.12f, 0.22f, 0.35f, 0.5f, 0.65f, 0.8f, 0.94f })
        {
            if (name == "MJV_Afterimage")
                frames.Add(F("afterimage", new Vector4(1f - t, 0f, 0f, 0f), Vector4.zero,
                    new Color(0.56f, 0.72f, 0.66f, 1f), 230, 230));
            else if (name == "MJV_PsychicShield")
                frames.Add(F(name, new Vector4(1f, 0.37f, t * 6f, Mathf.Max(0f, 1f - t * 4f)),
                    new Vector4(1f, 0f, t, 0f), Color.white, 240, 240));
            else if (name == "MJV_SermonHalo")
                frames.Add(F(name, new Vector4(t > 0.5f ? 1f : 0f, 0.37f, t * 6f, t > 0.5f ? (t - 0.5f) * 2f : 1f),
                    Vector4.zero, Color.white, 240, 240));
            else if (name == "MJV_CelestialBlade")
                // Growing, buried in the ground with its flash, then dissolving.
                frames.Add(F(name, new Vector4(1f, 1.4f, t > 0.3f ? 0.25f : 0f, t > 0.3f ? Mathf.Clamp01(1f - (t - 0.35f) / 0.2f) : 0f),
                    new Vector4(1f / 512f, 1f / 512f, Mathf.Clamp01((t - 0.55f) / 0.4f), 0.37f), Color.white, 240, 240));
            else if (name == "MJV_ThreatPulse")
                frames.Add(F(name, new Vector4(t, 0.37f, 1f, 1.35f), Vector4.zero, Color.white, 240, 240));
            else if (name == "MJV_OrbTrail")
                frames.Add(F(name, new Vector4(5f + t * 3f, 1.9f, t * 2f, 0.37f), new Vector4(1f, 0.55f, t > 0.8f ? (t - 0.8f) * 5f : 0f, 0f),
                    Color.white, 70, 240));
            else if (name == "MJV_GroundFissure")
                // Radial hub for the first half, the line fissure for the second: running, then cooling.
                frames.Add(t < 0.45f
                    ? F(name, new Vector4(0f, Mathf.Clamp01(t * 3f) * 1.06f, Mathf.Exp(-t * 3f), 1f), new Vector4(0f, t, 0f, 0f), Color.white, 240, 240)
                    : F(name, new Vector4(1f, Mathf.Clamp01((t - 0.45f) * 3f) * 1.06f, Mathf.Exp(-(t - 0.45f) * 3f), 1f), new Vector4(0f, t, 0f, 0f),
                        Color.white, 90, 240));
            else if (name == "MJV_Sprite")
                // The whole sheet: glow, its own colours, tinted as dust.
                frames.Add(F(name, new Vector4(1f, 1.3f, t < 0.35f ? 0f : t < 0.7f ? 1f : 2f, 0f), new Vector4(0f, 0f, 1f, 1f),
                    t < 0.7f ? Color.white : new Color(0.58f, 0.53f, 0.46f, 1f), 240, 240));
            else
                frames.Add(F(name, new Vector4(t, loop ? 1f : 0f, 1f, 0.85f), Vector4.zero,
                    Color.white, name == "MJV_CelestialBlade" ? 125 : 240, 240));
        }
        return frames.ToArray();
    }

    private static Frame F(string label, Vector4 p0, Vector4 p1, Color color, float width, float height)
    {
        return new Frame { label = label, p0 = p0, p1 = p1, color = color, width = width, height = height };
    }

    // Soil-and-sand ground with a faint grid, roughly the brightness of a RimWorld map at noon.
    private static Texture2D MakeGround(int width, int height)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.02f, y * 0.02f) * 0.6f + Mathf.PerlinNoise(x * 0.09f, y * 0.09f) * 0.4f;
                bool sand = (y / Cell) % 2 == 1;
                Color baseCol = sand ? new Color(0.62f, 0.55f, 0.42f) : new Color(0.36f, 0.31f, 0.25f);
                Color c = baseCol * (0.85f + 0.3f * n);
                if (x % 32 == 0 || y % 32 == 0)
                {
                    c *= 0.94f;
                }
                c.a = 1f;
                pixels[y * width + x] = c;
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    private static string Arg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
