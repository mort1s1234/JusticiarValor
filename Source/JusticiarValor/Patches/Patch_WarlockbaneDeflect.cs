using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace JusticiarValor
{
    // Warlockbane shield: psychic attacks (PsychicAttack) that would affect its bearer are turned aside.
    // Prefix and finalizer on Ability's private ApplyEffects(IEnumerable<CompAbilityEffect>, List<LocalTargetInfo>, LocalTargetInfo),
    // which Ability.Activate calls with the affected targets (the target, or everyone in the area) after the psyfocus, neural
    // heat and cooldown have been paid (Psycast.Activate pays them before base.Activate). Deflected bearers are removed from
    // that list, so the caster still pays for the cast. The effects resolve inside the cast's WarlockbaneWard, which also
    // shields the bearers they reach without naming them (Neuroquake). The method has the same signature on 1.6 and 1.7.
    // Vanilla Expanded Framework abilities (Vanilla Psycasts Expanded's psycasts): Patch_WarlockbaneDeflectVEF. Items (the
    // psychic lances): Patch_WarlockbaneDeflectItem.
    public static class Patch_WarlockbaneDeflect
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Ability), "ApplyEffects",
                new[] { typeof(IEnumerable<CompAbilityEffect>), typeof(List<LocalTargetInfo>), typeof(LocalTargetInfo) });
        }

        public static void Prefix(Ability __instance, List<LocalTargetInfo> targets)
        {
            WarlockbaneWard.Begin(__instance.pawn);
            if (!PsychicAttack.IsPsychic(__instance.def))
            {
                return;
            }
            WarlockbaneWard.Psychic();
            for (int i = (targets?.Count ?? 0) - 1; i >= 0; i--)
            {
                if (WarlockbaneWard.Turns(targets[i].Pawn))
                {
                    targets.RemoveAt(i);
                }
            }
        }

        public static Exception Finalizer(Exception __exception)
        {
            WarlockbaneWard.End();
            return __exception;
        }
    }
}
