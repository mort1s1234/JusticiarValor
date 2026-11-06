using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace JusticiarValor
{
    public class JusticiarValorMod : Mod
    {
        public const string HarmonyId = "Mortis.JusticiarValor";
        public static JusticiarValorSettings Settings { get; private set; } = new JusticiarValorSettings();

        public JusticiarValorMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<JusticiarValorSettings>();

            // Patch the game's entry points before their callers can inline them (1.7 uses tiered compilation).
            // Mod ability classes need their Defs first; their patches are deferred below.
            var harmony = new Harmony(HarmonyId);
            Patch(harmony, "arrest immunity (Promachos helmet)",
                AccessTools.Method(typeof(Pawn), nameof(Pawn.CheckAcceptArrest)),
                prefix: AccessTools.Method(typeof(Patch_ArrestImmunity), nameof(Patch_ArrestImmunity.Prefix)));
            Patch(harmony, "spell deflection (Warlockbane shield)",
                Patch_WarlockbaneDeflect.TargetMethod(),
                prefix: AccessTools.Method(typeof(Patch_WarlockbaneDeflect), nameof(Patch_WarlockbaneDeflect.Prefix)),
                finalizer: AccessTools.Method(typeof(Patch_WarlockbaneDeflect), nameof(Patch_WarlockbaneDeflect.Finalizer)));
            Patch(harmony, "psychic lance deflection (Warlockbane shield)",
                Patch_WarlockbaneDeflectItem.TargetMethod(),
                prefix: AccessTools.Method(typeof(Patch_WarlockbaneDeflectItem), nameof(Patch_WarlockbaneDeflectItem.Prefix)),
                finalizer: AccessTools.Method(typeof(Patch_WarlockbaneDeflectItem), nameof(Patch_WarlockbaneDeflectItem.Finalizer)));
            // Harmony can initialize an ability's static fields while patching Cast, especially on Mono. VPE's
            // FireTornadoDef would otherwise cache null before ThingDefs exist. Defer discovery and patching until
            // the loading event has resolved Defs and bound DefOfs, before gameplay can cast any abilities.
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                foreach (MethodBase cast in Patch_WarlockbaneDeflectVEF.TargetMethods())
                {
                    Patch(harmony, "spell deflection against Vanilla Expanded Framework abilities (Warlockbane shield)", cast,
                        prefix: AccessTools.Method(typeof(Patch_WarlockbaneDeflectVEF), nameof(Patch_WarlockbaneDeflectVEF.Prefix)),
                        finalizer: AccessTools.Method(typeof(Patch_WarlockbaneDeflectVEF), nameof(Patch_WarlockbaneDeflectVEF.Finalizer)));
                }
            });
            Patch(harmony, "justiciars leading tribute collector caravans",
                Patch_ImperialJusticiars.GenerateTraderMethod(),
                prefix: AccessTools.Method(typeof(Patch_ImperialJusticiars), nameof(Patch_ImperialJusticiars.GenerateTraderPrefix)),
                finalizer: AccessTools.Method(typeof(Patch_ImperialJusticiars), nameof(Patch_ImperialJusticiars.GenerateTraderFinalizer)));
            Patch(harmony, "justiciars in the bestower's escort",
                Patch_ImperialJusticiars.BestowingRunIntMethod(),
                prefix: AccessTools.Method(typeof(Patch_ImperialJusticiars), nameof(Patch_ImperialJusticiars.BestowingRunIntPrefix)),
                transpiler: AccessTools.Method(typeof(Patch_ImperialJusticiars), nameof(Patch_ImperialJusticiars.BestowingRunIntTranspiler)));
            Patch(harmony, "rare codex-auspex carried by imperial nobles",
                Patch_ImperialCodexInventory.TargetMethod(),
                postfix: AccessTools.Method(typeof(Patch_ImperialCodexInventory), nameof(Patch_ImperialCodexInventory.Postfix)));
        }

        public override string SettingsCategory() => "Justiciar Valor";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.CheckboxLabeled("MJV_SettingsAllowRecruitment".Translate(), ref Settings.allowImperialRecruitment,
                "MJV_SettingsAllowRecruitmentDesc".Translate());
            listing.CheckboxLabeled("MJV_SettingsImperialMissions".Translate(), ref Settings.enableImperialMissions,
                "MJV_SettingsImperialMissionsDesc".Translate());
            listing.CheckboxLabeled("MJV_SettingsDisgracedSermon".Translate(), ref Settings.enableDisgracedSermon,
                "MJV_SettingsDisgracedSermonDesc".Translate());
            listing.Label("MJV_SettingsQuestChanges".Translate());
            listing.End();
        }

        // Each patch is applied on its own, so that one target changing in a game update only disables its own feature.
        private static void Patch(Harmony harmony, string feature, MethodBase original, MethodInfo prefix = null, MethodInfo postfix = null,
            MethodInfo transpiler = null, MethodInfo finalizer = null)
        {
            if (original == null)
            {
                Log.Error($"[Justiciar Valor] Could not find the method patched for {feature}; this feature is disabled.");
                return;
            }
            try
            {
                harmony.Patch(original, prefix: prefix == null ? null : new HarmonyMethod(prefix), postfix: postfix == null ? null : new HarmonyMethod(postfix),
                    transpiler: transpiler == null ? null : new HarmonyMethod(transpiler), finalizer: finalizer == null ? null : new HarmonyMethod(finalizer));
            }
            catch (Exception ex)
            {
                Log.Error($"[Justiciar Valor] Patching {original.DeclaringType?.Name}.{original.Name} for {feature} failed; this feature is disabled.\n{ex}");
            }
        }
    }
}
