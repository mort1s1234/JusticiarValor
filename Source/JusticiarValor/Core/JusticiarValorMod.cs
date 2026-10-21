using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace JusticiarValor
{
    public class JusticiarValorMod : Mod
    {
        public const string HarmonyId = "Mortis.JusticiarValor";

        public JusticiarValorMod(ModContentPack content) : base(content)
        {
            // Patching from the Mod constructor: the patched methods' callers are all compiled after this point, so the
            // JIT cannot have inlined the originals into them yet (RimWorld 1.7 runs tiered compilation).
            var harmony = new Harmony(HarmonyId);
            Patch(harmony, "arrest immunity (Promachos helmet)",
                AccessTools.Method(typeof(Pawn), nameof(Pawn.CheckAcceptArrest)),
                prefix: AccessTools.Method(typeof(Patch_ArrestImmunity), nameof(Patch_ArrestImmunity.Prefix)));
            Patch(harmony, "spell deflection (Warlockbane shield)",
                Patch_WarlockbaneDeflect.TargetMethod(),
                prefix: AccessTools.Method(typeof(Patch_WarlockbaneDeflect), nameof(Patch_WarlockbaneDeflect.Prefix)));
        }

        // Each patch is applied on its own, so that one target changing in a game update only disables its own feature.
        private static void Patch(Harmony harmony, string feature, MethodBase original, MethodInfo prefix = null, MethodInfo postfix = null)
        {
            if (original == null)
            {
                Log.Error($"[Justiciar Valor] Could not find the method patched for {feature}; this feature is disabled.");
                return;
            }
            try
            {
                harmony.Patch(original, prefix: prefix == null ? null : new HarmonyMethod(prefix), postfix: postfix == null ? null : new HarmonyMethod(postfix));
            }
            catch (Exception ex)
            {
                Log.Error($"[Justiciar Valor] Patching {original.DeclaringType?.Name}.{original.Name} for {feature} failed; this feature is disabled.\n{ex}");
            }
        }
    }
}
