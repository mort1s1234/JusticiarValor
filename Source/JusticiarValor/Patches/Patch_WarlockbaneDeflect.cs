using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace JusticiarValor
{
    // Warlockbane shield: hostile psycasts that would affect its bearer are turned aside.
    // Prefix on Ability's private ApplyEffects(IEnumerable<CompAbilityEffect>, List<LocalTargetInfo>, LocalTargetInfo),
    // which Ability.Activate calls with the affected targets after the psyfocus, neural heat and cooldown have been paid
    // (Psycast.Activate pays them before base.Activate). Deflected bearers are removed from that list, so the caster still
    // pays for the cast. The method has the same signature on 1.6 and 1.7.
    public static class Patch_WarlockbaneDeflect
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Ability), "ApplyEffects",
                new[] { typeof(IEnumerable<CompAbilityEffect>), typeof(List<LocalTargetInfo>), typeof(LocalTargetInfo) });
        }

        public static void Prefix(Ability __instance, List<LocalTargetInfo> targets)
        {
            if (targets == null || targets.Count == 0 || !IsSpell(__instance))
            {
                return;
            }
            Pawn caster = __instance.pawn;
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                if (!(targets[i].Thing is Pawn bearer) || bearer == caster || caster == null || !caster.HostileTo(bearer))
                {
                    continue;
                }
                CompWarlockbane shield = bearer.WornComp<CompWarlockbane>();
                if (shield != null && JVUtility.IsConscious(bearer) && shield.TryDeflect(__instance, bearer))
                {
                    targets.RemoveAt(i);
                }
            }
        }

        // Psycasts, and other abilities whose effects are psychic.
        private static bool IsSpell(Ability ability)
        {
            if (ability.def.IsPsycast)
            {
                return true;
            }
            List<CompAbilityEffect> effects = ability.EffectComps;
            if (effects == null)
            {
                return false;
            }
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].Props.psychic)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
