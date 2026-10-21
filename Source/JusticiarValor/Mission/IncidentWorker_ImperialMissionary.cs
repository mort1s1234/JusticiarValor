using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace JusticiarValor
{
    // Settings of the imperial missionary incident.
    public class MissionExtension : DefModExtension
    {
        public PawnKindDef missionaryKind;
        public PawnKindDef escortKind;
        public IntRange escortCount = new IntRange(0, 2);
        public FloatRange visitDays = new FloatRange(1f, 1.5f);
        public IntRange firstSermonDelayTicks = new IntRange(1500, 3000);
        public IntRange sermonIntervalTicks = new IntRange(12000, 18000);
        public SermonSettings sermon = new SermonSettings();
    }

    // An imperial missionary arrives, alone or with an escort of justiciars, and stays a while to preach.
    public class IncidentWorker_MJV_ImperialMissionary : IncidentWorker
    {
        private MissionExtension Ext => def.GetModExtension<MissionExtension>() ?? new MissionExtension();

        public static bool AnyMissionaryOn(Map map)
        {
            List<Lord> lords = map.lordManager.lords;
            for (int i = 0; i < lords.Count; i++)
            {
                if (lords[i].LordJob is LordJob_MJV_MissionaryVisit)
                {
                    return true;
                }
            }
            return false;
        }

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!base.CanFireNowSub(parms) || Ext.missionaryKind == null)
            {
                return false;
            }
            var map = (Map)parms.target;
            return ImperialFaith.CanHostMission(map) && !AnyMissionaryOn(map) && RCellFinder.TryFindRandomPawnEntryCell(out _, map, CellFinder.EdgeRoadChance_Neutral);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            var map = (Map)parms.target;
            MissionExtension ext = Ext;
            Faction empire = ImperialFaith.Empire;
            if (empire == null || ext.missionaryKind == null || !ImperialFaith.CanHostMission(map)
                || !RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, CellFinder.EdgeRoadChance_Neutral))
            {
                return false;
            }
            Pawn missionary = PawnGenerator.GeneratePawn(ext.missionaryKind, empire);
            ImperialFaith.Ordain(missionary);
            var pawns = new List<Pawn> { missionary };
            int escorts = ext.escortKind == null ? 0 : ext.escortCount.RandomInRange;
            for (int i = 0; i < escorts; i++)
            {
                pawns.Add(PawnGenerator.GeneratePawn(ext.escortKind, empire));
            }
            foreach (Pawn pawn in pawns)
            {
                GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(entry, map, 4), map);
            }
            if (!RCellFinder.TryFindRandomSpotJustOutsideColony(entry, map, out IntVec3 chillSpot))
            {
                chillSpot = CellFinder.RandomClosewalkCellNear(entry, map, 8);
            }
            int duration = (int)(ext.visitDays.RandomInRange * GenDate.TicksPerDay);
            var job = new LordJob_MJV_MissionaryVisit(empire, chillSpot, missionary, duration, ext.firstSermonDelayTicks.RandomInRange,
                ext.sermonIntervalTicks, ext.sermon);
            LordMaker.MakeNewLord(empire, job, map, pawns);

            string faith = ImperialFaith.Ideo?.name ?? string.Empty;
            TaggedString text = escorts == 0
                ? "MJV_MissionaryArrivedSolo".Translate(missionary.Named("PREACHER"), empire.Named("FACTION"), faith.Named("FAITH"))
                : "MJV_MissionaryArrivedEscorted".Translate(missionary.Named("PREACHER"), empire.Named("FACTION"), faith.Named("FAITH"), escorts.Named("COUNT"),
                    (escorts == 1 ? ext.escortKind.label : ext.escortKind.GetLabelPlural()).Named("ESCORT"));
            text += "\n\n" + "MJV_MissionaryArrivedExplain".Translate(missionary.Named("PREACHER"), duration.ToStringTicksToPeriod().Named("DURATION"),
                ext.sermon.goodwillPerConvert.Named("GOODWILL"), empire.Named("FACTION"));
            SendStandardLetter(def.letterLabel, text, def.letterDef, parms, missionary);
            return true;
        }
    }
}
