using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace JusticiarValor
{
    // Warlockbane shield against psychic items: the psychic lances, and any item working CompTargetEffects on its target
    // that PsychicAttack finds psychic. Prefix and finalizer on Verb_CastTargetEffect.TryCastShot (1.6 and 1.7 alike),
    // which works the item's target effects on the target and then spends one of its charges. A bearer the shot is aimed at
    // turns it aside: the effects never reach it, and the charge is spent all the same. The effects of a shot that goes
    // through resolve inside the cast's WarlockbaneWard, like an ability's.
    public static class Patch_WarlockbaneDeflectItem
    {
        public static MethodBase TargetMethod() => AccessTools.Method(typeof(Verb_CastTargetEffect), "TryCastShot");

        public static bool Prefix(Verb_CastTargetEffect __instance, ref bool __result)
        {
            WarlockbaneWard.Begin(__instance.CasterPawn);
            if (!PsychicAttack.IsPsychic(__instance))
            {
                return true;
            }
            WarlockbaneWard.Psychic();
            if (!(__instance.CurrentTarget.Thing is Pawn bearer) || !WarlockbaneWard.Turns(bearer))
            {
                return true;
            }
            __instance.ReloadableCompSource?.UsedOnce();
            __result = true;
            return false;
        }

        public static Exception Finalizer(Exception __exception)
        {
            WarlockbaneWard.End();
            return __exception;
        }
    }
}
