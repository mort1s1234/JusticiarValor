using System.Collections.Generic;
using RimWorld;
using Verse;

namespace JusticiarValor
{
    // Promachos helmet: its wearer cannot be arrested while conscious. An attempt fires Justiciar Valor at once, the
    // arrester included, and the wearer takes it as an attack.
    // Prefix on Pawn.CheckAcceptArrest (identical in 1.6 and 1.7). The refusal mirrors the method's own refusal branch:
    // the faction notification, the message, the comps' Notify_Arrested(false) and the berserk response.
    public static class Patch_ArrestImmunity
    {
        public static bool Prefix(Pawn __instance, Pawn arrester, ref bool __result)
        {
            Pawn pawn = __instance;
            if (!JVUtility.IsConscious(pawn) || arrester == null || !WearsPromachosHelmet(pawn))
            {
                return true;
            }
            Faction homeFaction = pawn.HomeFaction;
            if (homeFaction != null && homeFaction != arrester.Faction)
            {
                homeFaction.Notify_MemberCaptured(pawn, arrester.Faction);
            }
            Messages.Message("MJV_ArrestRefused".Translate(pawn.Named("PAWN")), pawn, MessageTypeDefOf.ThreatSmall);
            List<ThingComp> comps = pawn.AllComps;
            for (int i = 0; i < comps.Count; i++)
            {
                comps[i].Notify_Arrested(false);
            }
            if (ModsConfig.AnomalyActive)
            {
                pawn.creepjoiner?.Notify_Arrested(false);
            }
            if (pawn.Spawned)
            {
                Ability ability = pawn.abilities?.GetAbility(MJV_DefOf.MJV_JusticiarValor, true);
                JusticiarValorWave.Release(pawn, JusticiarValorWave.PropsFor(MJV_DefOf.MJV_JusticiarValor), arrester);
                if (ability != null && !ability.OnCooldown)
                {
                    ability.StartCooldown(ability.def.cooldownTicksRange.RandomInRange);
                }
            }
            if (pawn.Faction == null || !arrester.HostileTo(pawn))
            {
                pawn.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Berserk);
            }
            __result = false;
            return false;
        }

        private static bool WearsPromachosHelmet(Pawn pawn)
        {
            List<Apparel> worn = pawn.apparel?.WornApparel;
            if (worn == null)
            {
                return false;
            }
            for (int i = 0; i < worn.Count; i++)
            {
                if (worn[i].def == MJV_DefOf.MJV_Apparel_ArmorHelmetPromachosHelmet)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
