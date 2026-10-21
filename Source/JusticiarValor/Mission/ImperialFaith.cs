using System.Collections.Generic;
using RimWorld;
using Verse;

namespace JusticiarValor
{
    // The imperial faith is the Empire's primary ideoligion. Conversion needs Ideology (and not classic mode); without
    // it the missionaries have nothing to preach and the events that convert stay off.
    public static class ImperialFaith
    {
        public static Faction Empire => Find.FactionManager?.OfEmpire;

        public static bool ConversionPossible => ModsConfig.IdeologyActive && Find.IdeoManager != null && !Find.IdeoManager.classicMode;

        public static Ideo Ideo => ConversionPossible ? Empire?.ideos?.PrimaryIdeo : null;

        // The Empire exists, is not defeated and is not at war with the player.
        public static bool EmpireAtPeace
        {
            get
            {
                Faction empire = Empire;
                return empire != null && !empire.defeated && !empire.HostileTo(Faction.OfPlayer);
            }
        }

        public static bool IsBeliever(Pawn pawn) => pawn?.ideo != null && pawn.Ideo != null && pawn.Ideo == Ideo;

        // The colony's own people, the ones a mission is sent to: free colonists that can hold a faith, not quest
        // lodgers and not babies.
        public static bool IsCongregant(Pawn pawn)
        {
            return pawn != null && pawn.IsFreeColonist && !pawn.Dead && pawn.RaceProps.Humanlike && !pawn.IsMutant
                && pawn.ideo != null && pawn.Ideo != null && !pawn.IsQuestLodger() && !pawn.DevelopmentalStage.Baby();
        }

        public static IEnumerable<Pawn> Congregation(Map map)
        {
            if (map == null)
            {
                yield break;
            }
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                if (IsCongregant(colonists[i]))
                {
                    yield return colonists[i];
                }
            }
        }

        public static int UnconvertedCount(Map map)
        {
            int count = 0;
            foreach (Pawn pawn in Congregation(map))
            {
                if (!IsBeliever(pawn))
                {
                    count++;
                }
            }
            return count;
        }

        // Every congregant follows the imperial faith.
        public static bool ColonyConverted(Map map)
        {
            if (Ideo == null)
            {
                return false;
            }
            bool any = false;
            foreach (Pawn pawn in Congregation(map))
            {
                if (!IsBeliever(pawn))
                {
                    return false;
                }
                any = true;
            }
            return any;
        }

        // Whether an imperial mission can be sent to this map at all.
        public static bool CanHostMission(Map map, int minUnconverted = 1)
        {
            return map != null && map.IsPlayerHome && Ideo != null && EmpireAtPeace && UnconvertedCount(map) >= minUnconverted;
        }

        // The missionary preaches the Empire's primary faith, even when generated with one of its minor ideoligions.
        public static void Ordain(Pawn missionary)
        {
            Ideo faith = Ideo;
            if (faith != null && missionary?.ideo != null && missionary.Ideo != faith)
            {
                missionary.ideo.SetIdeo(faith);
            }
        }

        public static void RewardConversions(int converts, int goodwillPerConvert, Pawn convert)
        {
            Faction empire = Empire;
            if (converts <= 0 || goodwillPerConvert == 0 || empire == null || empire.defeated)
            {
                return;
            }
            empire.TryAffectGoodwillWith(Faction.OfPlayer, converts * goodwillPerConvert, true, false, MJV_DefOf.MJV_ConvertedToImperialFaith, convert);
        }
    }
}
