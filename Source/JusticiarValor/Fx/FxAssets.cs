using System.Collections.Generic;
using Verse;
using Material = UnityEngine.Material;
using Shader = UnityEngine.Shader;
using Texture = UnityEngine.Texture;
using Texture2D = UnityEngine.Texture2D;

namespace JusticiarValor
{
    // Restored procedural effects, Piloto animation atlases, the packed sheet of Piloto textures the composite effects
    // sample, and the equipped weapon's own silhouette.
    // ShaderDefs on 1.7 and the Unity bundle on 1.6 share the same shader sources.
    [StaticConstructorOnStartup]
    public static class FxAssets
    {
        // Common/Textures/Effects/JusticiarValor/Piloto/Sheet.png, written by Source/Shaders/pack_sheet.py (cells: FxSheet).
        public static readonly Texture2D Sheet = ContentFinder<Texture2D>.Get("Effects/JusticiarValor/Piloto/Sheet");

        public static readonly Material SermonHalo = Make(MJV_DefOf.MJV_SermonHalo, 3092);
        public static readonly Material PsychicWave = Make(MJV_DefOf.MJV_PsychicWave, "PsychicWave", 3100);
        public static readonly Material PsychicShield = Make(MJV_DefOf.MJV_PsychicShield, 3105);
        public static readonly Material PsyOrb = Make(MJV_DefOf.MJV_PsyOrb, "PsyOrb", 3110);
        public static readonly Material CelestialBlade = Make(MJV_DefOf.MJV_CelestialBlade,
            ContentFinder<Texture2D>.Get("Things/Item/Equipment/WeaponMelee/Xiphos"), 3115);
        public static readonly Material AfterimageWake = Make(MJV_DefOf.MJV_InvictusBurst, "AfterimageWake", 3091);

        // Threat sense: the scan around the wearer (sheet) and the lock-on over each threat (Tech_GroundAOEIndicator).
        public static readonly Material ThreatPulse = Make(MJV_DefOf.MJV_ThreatPulse, Sheet, 3095);
        public static readonly Material ThreatLock = Make(MJV_DefOf.MJV_PsyOrb, "ThreatLock", 3096);

        // Psy-orb wake (sheet) and the three bursts layered on impact.
        public static readonly Material OrbTrail = Make(MJV_DefOf.MJV_OrbTrail, Sheet, 3108);
        public static readonly Material OrbGlitter = Make(MJV_DefOf.MJV_PsyOrb, "OrbGlitter", 3111);
        public static readonly Material OrbPetals = Make(MJV_DefOf.MJV_PsyOrb, "OrbPetals", 3112);
        public static readonly Material OrbBurst = Make(MJV_DefOf.MJV_PsyOrb, "OrbBurst", 3113);

        // Celestial Pierce: broken ground (sheet, at floor altitude) and the shock where the blade lands.
        public static readonly Material GroundFissure = Make(MJV_DefOf.MJV_GroundFissure, Sheet, 3088);
        public static readonly Material PierceShock = Make(MJV_DefOf.MJV_PsyOrb, "PierceShock", 3113);

        // Justiciar Valor's wave (Bard Flash Shockwave over the Holy explosion above).
        public static readonly Material WaveShock = Make(MJV_DefOf.MJV_PsyOrb, "WaveShock", 3101);

        // Invictus: the overdrive's swirl of feathers (its burst is drawn from the sheet).
        public static readonly Material Overdrive = Make(MJV_DefOf.MJV_PsyOrb, "Overdrive", 3104);

        // Sheet sprites below and above the animated layers.
        public static readonly Material SpriteUnder = Make(MJV_DefOf.MJV_Sprite, Sheet, 3089);
        public static readonly Material SpriteOver = Make(MJV_DefOf.MJV_Sprite, Sheet, 3117);

        private static readonly Shader AfterimageShader = Load(MJV_DefOf.MJV_Afterimage);
        private static readonly Dictionary<Texture, Material> afterimageMaterials = new Dictionary<Texture, Material>(ReferenceComparer<Texture>.Instance);
        private static readonly Dictionary<Texture, Material> bladeMaterials = new Dictionary<Texture, Material>(ReferenceComparer<Texture>.Instance);

        // Match the game's equipped-weapon graphic selection. Materials are cached per texture, never per frame.
        public static Material BladeFor(Thing weapon)
        {
            Graphic graphic = weapon?.Graphic;
            if (graphic is Graphic_StackCount stack)
                graphic = stack.SubGraphicForStackCount(1, weapon.def);
            Texture texture = graphic?.MatSingleFor(weapon)?.mainTexture;
            if (texture is null) return CelestialBlade;
            if (!bladeMaterials.TryGetValue(texture, out Material material))
            {
                material = Make(MJV_DefOf.MJV_CelestialBlade, texture, 3115);
                bladeMaterials[texture] = material;
            }
            return material;
        }

        // One material per pawn atlas page (a handful), cached by the MaterialPool as well.
        public static Material AfterimageFor(Texture atlas)
        {
            if (AfterimageShader is null || atlas is null)
            {
                return null;
            }
            if (!afterimageMaterials.TryGetValue(atlas, out Material mat))
            {
                mat = MaterialPool.MatFrom(new MaterialRequest(atlas, AfterimageShader) { renderQueue = 3090 });
                afterimageMaterials[atlas] = mat;
            }
            return mat;
        }

        private static Material Make(ShaderTypeDef def, string clip, int renderQueue)
            => Make(def, ContentFinder<Texture2D>.Get("Effects/JusticiarValor/Piloto/" + clip), renderQueue);

        private static Material Make(ShaderTypeDef def, int renderQueue)
        {
            Shader shader = Load(def);
            return shader is null ? null : MaterialPool.MatFrom(new MaterialRequest(shader) { renderQueue = renderQueue });
        }

        private static Material Make(ShaderTypeDef def, Texture texture, int renderQueue)
        {
            Shader shader = Load(def);
            return shader is null || texture is null ? null
                : MaterialPool.MatFrom(new MaterialRequest(texture, shader) { renderQueue = renderQueue });
        }

        private static Shader Load(ShaderTypeDef def)
        {
            Shader shader = def?.Shader;
            if (shader is null)
            {
                return null;
            }
#if RIMWORLD_1_6
            // 1.6 substitutes its default shader (and logs a warning) when the bundle has no such shader, e.g. on
            // Linux, which the bundles are not built for.
            if (ReferenceEquals(shader, ShaderDatabase.DefaultShader))
            {
                return null;
            }
#endif
            return shader;
        }
    }
}
