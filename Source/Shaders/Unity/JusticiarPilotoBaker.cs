// Reproduces Piloto Studio's authored particle effects as view-independent sprite sequences.
// Only instantiated copies are adjusted; the vendor prefabs, shaders and materials remain untouched.
// Both RimWorld engines consume these same atlases. No Unity particle runtime is needed in the mod.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Rendering.BuiltIn.ShaderGraph;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class JusticiarPilotoBaker
{
    private const string Vendor = "Assets/Piloto Studio/Elemental VFX Mega Bundle/";
    private const int Cell = 256;
    private const int Columns = 8;
    private const int Frames = 64;
    private const int Layer = 30;

    private sealed class Clip
    {
        public string name, prefab;
        public float start, seconds;
        public bool loop, front;

        // prefab: relative to the Elemental VFX Mega Bundle, or a full "Assets/..." path for the other Piloto packs.
        public Clip(string name, string prefab, float seconds, bool loop = false, float start = 0f, bool front = false)
        {
            this.name = name;
            this.prefab = (prefab.StartsWith("Assets/") ? prefab : Vendor + prefab) + ".prefab";
            this.seconds = seconds;
            this.loop = loop;
            this.start = start;
            this.front = front;
        }
    }

    private static readonly Clip[] Clips =
    {
        new Clip("PsychicWave", "Holy/AOE_Explosion_Holy", 1.4f, false, 2.6f),
        new Clip("ThreatPulse", "Tech/MagicRing_Hexagon", 1.8f),
        new Clip("PsychicShield", "Tech/TechMagic_TechnoShield", 2.4f, true, 1.2f),
        new Clip("InvictusBurst", "Wind/GaleWind_Burst", 0.85f, false, 0.2f),
        new Clip("CelestialBlade", "Holy/Angel's Pierce", 1.4f),
        new Clip("Deflect", "Holy/Angelical Shield Guard", 1.8f, false, 0f, true),
        new Clip("PsyOrb", "Arcane/Orb_Arcane", 2.4f, true, 1.2f),
        new Clip("OrbFlight", "Arcane/Projectile_Arcane", 1.2f, true, 0.3f),
        new Clip("OrbImpact", "Arcane/Impact_Arcane", 1.2f),
        new Clip("SermonHalo", "Holy/HolyRing", 3.2f, true, 1.2f),
        new Clip("Conversion", "Arcane/VFX_LevelUpLevelUp_Arcane", 2f),
        new Clip("ShieldHit", "Tech/Tech_ImpactSimple", 0.9f),
        new Clip("ShieldBreak", "Tech/Tech_CubeBurst", 1.4f),
        new Clip("ShieldRecharge", "Holy/MagicRing_Blessed", 1.5f),
        new Clip("AfterimageWake", "Wind/GaleWind_Woosh", 0.8f),
        // 2026-10-21 composites. Windows crop each source to its visible action.
        new Clip("ThreatLock", "Tech/Tech_GroundAOEIndicator", 0.9f, false, 0.02f),
        new Clip("OrbBurst", "Tech/Projectile_Hit_Tech", 0.42f, false, 0.03f),
        new Clip("OrbGlitter", "Light/Impact_Light", 0.85f, false, 0.04f),
        new Clip("OrbPetals", SuperRealistic2 + "Realistic HighTech VFX/ARPG_HighTech_Hit", 0.3f, false, 0.03f),
        new Clip("PierceShock", SuperRealistic + "ARPG Realistic Paladin_Pack/ARPG_Paladin_Sword_Shock", 1.2f, false, 0.06f),
        // Composites of the other effects.
        new Clip("WaveShock", SuperRealistic2 + "Realistic Bard/volume/Flash Shockwave", 1.3f, false, 0.1f),
        new Clip("Overdrive", "Wind/Feathering Speed Aura", 2.4f, true, 0.6f)
    };

    private const string SuperRealistic = "Assets/Piloto Studio/Super Realistic FX Bundle/";
    private const string SuperRealistic2 = "Assets/Piloto Studio/Super Realistic FX Bundle 02/";

    public static void Bake()
    {
        string output = Arg("-jvPilotoOut");
        if (string.IsNullOrEmpty(output)) throw new ArgumentException("Missing -jvPilotoOut");
        string only = Arg("-jvPilotoOnly");
        // Scouting: bake the clips listed in a TSV (name, prefab, seconds, loop, start, front) instead of the mod's own.
        string scout = Arg("-jvPilotoScout");
        Clip[] clips = string.IsNullOrEmpty(scout) ? Clips : File.ReadAllLines(scout)
            .Where(l => l.Trim().Length > 0 && !l.StartsWith("#"))
            .Select(l => l.Split('\t'))
            .Select(f => new Clip(f[0], f[1], float.Parse(f[2], CultureInfo.InvariantCulture), f.Length > 3 && bool.Parse(f[3]),
                f.Length > 4 ? float.Parse(f[4], CultureInfo.InvariantCulture) : 0f, f.Length > 5 && bool.Parse(f[5])))
            .ToArray();
        Directory.CreateDirectory(output);
        string preview = Path.Combine(output, "Previews");
        Directory.CreateDirectory(preview);
        // Use an empty, unsaved scene: the source project's current scene must not leak into the capture.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cameraObject = new GameObject("Justiciar Piloto bake camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.cullingMask = 1 << Layer;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 200f;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.aspect = 1f;
        var target = new RenderTexture(Cell, Cell, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        target.Create();
        camera.targetTexture = target;
        var pixels = new Texture2D(Cell, Cell, TextureFormat.RGBA32, false);
        var timing = new CommandBuffer { name = "Deterministic Piloto material time" };
        camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, timing);
        camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, timing);
        var manifest = new List<string> { "clip\tprefab\tguid\tframes\tcolumns\tstartSeconds\tseconds\tloop\tfrontView\tcameraHalfSize\tcameraCenterX\tcameraCenterY" };
        int failures = 0;
        try
        {
            foreach (Clip clip in clips.Where(c => string.IsNullOrEmpty(only) || only.Split(',').Contains(c.name)))
            {
                try { BakeClip(clip, output, preview, camera, target, pixels, timing, manifest); }
                catch (Exception ex) { Debug.LogError("[JusticiarValor] Piloto " + clip.name + ": " + ex); failures++; }
            }
            File.WriteAllLines(Path.Combine(output, string.IsNullOrEmpty(only) ? "sources.tsv" : "sources-partial.tsv"), manifest);
        }
        finally
        {
            camera.RemoveAllCommandBuffers();
            timing.Release();
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(cameraObject);
        }
        Debug.Log("[JusticiarValor] Piloto baking complete: " + output);
        if (failures > 0) throw new InvalidOperationException(failures + " Piloto clips failed");
    }

    private static void BakeClip(Clip clip, string output, string preview, Camera camera, RenderTexture target,
        Texture2D readback, CommandBuffer timing, List<string> manifest)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(clip.prefab);
        if (prefab == null) throw new FileNotFoundException(clip.prefab);
        GameObject instance = Object.Instantiate(prefab);
        var materials = new List<Material>();
        try
        {
            instance.transform.position = Vector3.zero;
            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            // Demo movement, sound and destruction scripts are not part of the authored visual animation.
            foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                Object.DestroyImmediate(behaviour);
            foreach (AudioSource audio in instance.GetComponentsInChildren<AudioSource>(true))
                Object.DestroyImmediate(audio);
            foreach (Light light in instance.GetComponentsInChildren<Light>(true)) light.enabled = false;

            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] copies = renderer.sharedMaterials.Select(source =>
                {
                    if (source == null) return null;
                    Material copy = new Material(source);
                    // These vendor materials were migrated from URP. Let Shader Graph synchronize
                    // their Built-in transparency state on our copies (otherwise quads render solid).
                    if (copy.HasProperty("_BUILTIN_Surface")) BuiltInBaseShaderGUI.SetupSurface(copy);
                    // A depth fade against an absent ground plane should not erase a standalone capture.
                    copy.DisableKeyword("_USESOFTALPHA");
                    if (copy.HasProperty("_USESOFTALPHA")) copy.SetFloat("_USESOFTALPHA", 0);
                    if (copy.HasProperty("_UseSoftAlpha")) copy.SetFloat("_UseSoftAlpha", 0);
                    if (copy.HasProperty("_SoftParticlesEnabled")) copy.SetFloat("_SoftParticlesEnabled", 0);
                    copy.DisableKeyword("SOFTPARTICLES_ON");
                    if (ShaderUtil.ShaderHasError(copy.shader))
                        throw new InvalidOperationException("Piloto shader compile error: " + copy.shader.name);
                    materials.Add(copy);
                    return copy;
                }).ToArray();
                renderer.sharedMaterials = copies;
            }
            ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                systems[i].useAutoRandomSeed = false;
                systems[i].randomSeed = (uint)(731 + i * 53);
                var main = systems[i].main;
                main.stopAction = ParticleSystemStopAction.None;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            }
            ParticleSystem[] roots = systems.Where(p => !HasParentSystem(p.transform.parent, instance.transform.parent)).ToArray();
            camera.transform.rotation = clip.front ? Quaternion.identity : Quaternion.Euler(90f, 0f, 0f);
            Vector3 forward = camera.transform.forward;
            camera.transform.position = -forward * 60f;
            camera.orthographicSize = 14f;
            // Fit from visible pixels over the whole animation, not Unity's inflated particle bounds.
            // A single fit is shared by every frame, so expanding rings keep their authored motion.
            int minX = Cell, minY = Cell, maxX = -1, maxY = -1;
            for (int i = 0; i < 16; i++)
            {
                float t = clip.start + clip.seconds * (i + 0.5f) / 16f;
                Color32[] shot = Capture(t, roots, camera, target, readback, timing);
                for (int p = 0; p < shot.Length; p++)
                {
                    if (Math.Max(shot[p].r, Math.Max(shot[p].g, shot[p].b)) < 10) continue;
                    int x = p % Cell, y = p / Cell;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
            }
            if (maxX < 0) throw new InvalidOperationException("Empty Piloto capture: " + clip.prefab);
            float cx = ((minX + maxX + 1f) / Cell - 1f) * 14f;
            float cy = ((minY + maxY + 1f) / Cell - 1f) * 14f;
            float half = Math.Max(maxX - minX + 1, maxY - minY + 1) / (float)Cell * 14f * 1.18f;
            camera.orthographicSize = Math.Max(0.2f, half);
            camera.transform.position = -forward * 60f + camera.transform.right * cx + camera.transform.up * cy;
            var atlas = new Texture2D(Cell * Columns, Cell * (Frames / Columns), TextureFormat.RGBA32, false);
            var contact = new Texture2D(Cell * 8, Cell * 2, TextureFormat.RGBA32, false);
            int visible = 0, magenta = 0;
            try
            {
                for (int frame = 0; frame < Frames; frame++)
                {
                    float t = clip.start + clip.seconds * frame / (Frames - (clip.loop ? 0f : 1f));
                    Color32[] shot = Capture(t, roots, camera, target, readback, timing);
                    for (int p = 0; p < shot.Length; p++)
                    {
                        Color32 s = shot[p];
                        if (s.r > 220 && s.b > 220 && s.g < 40) magenta++;
                        // Store emission luminance. The runtime palette supplies the armor's gray-green hues.
                        // Max preserves saturated vendor glows without making purple/blue clips much dimmer.
                        byte value = Math.Max(s.r, Math.Max(s.g, s.b));
                        shot[p] = new Color32(value, value, value, value);
                        if (value > 10) visible++;
                    }
                    // Rows count from the bottom in both targets, with a 2px transparent gutter per cell.
                    for (int y = 0; y < Cell; y++)
                        for (int x = 0; x < Cell; x++)
                            if (x < 2 || y < 2 || x >= Cell - 2 || y >= Cell - 2)
                                shot[y * Cell + x] = new Color32(0, 0, 0, 0);
                    atlas.SetPixels32(frame % Columns * Cell, frame / Columns * Cell, Cell, Cell, shot);
                    if (frame % 4 == 0)
                    {
                        Color32[] swatch = shot.Select(s =>
                        {
                            float v = s.r / 255f;
                            return (Color32)new Color(0.10f + v * 0.52f, 0.12f + v * 0.70f, 0.115f + v * 0.64f, 1f);
                        }).ToArray();
                        int n = frame / 4;
                        contact.SetPixels32(n % 8 * Cell, (1 - n / 8) * Cell, Cell, Cell, swatch);
                    }
                }
                if (visible < 128 || magenta > visible / 20)
                    throw new InvalidOperationException($"Bad Piloto capture {clip.name}: visible={visible}, magenta={magenta}");
                atlas.Apply();
                contact.Apply();
                File.WriteAllBytes(Path.Combine(output, clip.name + ".png"), atlas.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(preview, clip.name + ".png"), contact.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(atlas);
                Object.DestroyImmediate(contact);
            }
            manifest.Add(string.Join("\t", clip.name, clip.prefab, AssetDatabase.AssetPathToGUID(clip.prefab),
                Frames, Columns, clip.start.ToString(CultureInfo.InvariantCulture), clip.seconds.ToString(CultureInfo.InvariantCulture), clip.loop, clip.front,
                half.ToString(CultureInfo.InvariantCulture), cx.ToString(CultureInfo.InvariantCulture), cy.ToString(CultureInfo.InvariantCulture)));
            Debug.Log($"[JusticiarValor] Piloto {clip.name}: {systems.Length} systems, {materials.Count} materials, view={half:F2}, {visible} visible pixels");
        }
        finally
        {
            Object.DestroyImmediate(instance);
            foreach (Material material in materials) Object.DestroyImmediate(material);
        }
    }

    private static bool HasParentSystem(Transform current, Transform stop)
    {
        while (current != stop && current != null)
        {
            if (current.GetComponent<ParticleSystem>() != null) return true;
            current = current.parent;
        }
        return false;
    }

    private static Color32[] Capture(float seconds, ParticleSystem[] roots, Camera camera, RenderTexture target,
        Texture2D pixels, CommandBuffer timing)
    {
        foreach (ParticleSystem root in roots) root.Simulate(Math.Max(0.001f, seconds), true, true, true);
        timing.Clear();
        timing.SetGlobalVector("_Time", new Vector4(seconds / 20f, seconds, seconds * 2f, seconds * 3f));
        timing.SetGlobalVector("_SinTime", new Vector4(Mathf.Sin(seconds / 8), Mathf.Sin(seconds / 4), Mathf.Sin(seconds / 2), Mathf.Sin(seconds)));
        timing.SetGlobalVector("_CosTime", new Vector4(Mathf.Cos(seconds / 8), Mathf.Cos(seconds / 4), Mathf.Cos(seconds / 2), Mathf.Cos(seconds)));
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        pixels.ReadPixels(new Rect(0, 0, Cell, Cell), 0, 0);
        pixels.Apply();
        RenderTexture.active = previous;
        return pixels.GetPixels32();
    }

    private static string Arg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
