using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace JusticiarValor
{
    // Justiciars in the Empire's own events (the Stellarch's party is an XML patch, Common/Patches/Empire_RoyalAscent.xml):
    // - Tribute collector caravans are led by a justiciar: the trader of a caravan whose trader kind is a tribute collector
    //   (IncidentWorker_CaravanArrivalTributeCollector) is a justiciar. Ordinary imperial caravans keep their traders. The
    //   Empire has a single trader group for both, so PawnGroupKindWorker_Trader.GenerateTrader is patched: its private
    //   GenerateTrader(PawnGroupMakerParms, PawnGroupMaker, TraderKindDef) has the same signature on 1.6 and 1.7.
    // - Two of the six janissaries escorting a bestower are justiciars. QuestNode_Root_BestowingCeremony.RunInt generates
    //   the escort in a loop from PawnKindDefOf.Empire_Fighter_Janissary (1.6: Quest.GeneratePawn, 1.7: GeneratePawn_NewTemp);
    //   the transpiler replaces that one field load with BestowerEscortKind.
    public static class Patch_ImperialJusticiars
    {
        public const string TributeCollectorCategory = "TributeCollector";

        // How many of the bestower's escort are justiciars.
        public const int JusticiarsInBestowerEscort = 2;

        private static int bestowerEscortMade;

        public static MethodBase GenerateTraderMethod() => AccessTools.Method(typeof(PawnGroupKindWorker_Trader), "GenerateTrader");

        public static MethodBase BestowingRunIntMethod() => AccessTools.Method(typeof(QuestNode_Root_BestowingCeremony), "RunInt");

        public static void GenerateTraderPrefix(PawnGroupMakerParms parms, PawnGroupMaker groupMaker, TraderKindDef traderKind, out List<PawnGenOption> __state)
        {
            __state = null;
            if (traderKind?.category != TributeCollectorCategory || groupMaker == null || MJV_DefOf.MJV_Justiciar == null
                || parms?.faction == null || parms.faction != Find.FactionManager.OfEmpire)
            {
                return;
            }
            __state = groupMaker.traders;
            groupMaker.traders = new List<PawnGenOption> { new PawnGenOption { kind = MJV_DefOf.MJV_Justiciar, selectionWeight = 1f } };
        }

        // The Empire's trader group is shared by every caravan: its traders are put back whatever happened.
        public static System.Exception GenerateTraderFinalizer(System.Exception __exception, PawnGroupMaker groupMaker, List<PawnGenOption> __state)
        {
            if (__state != null && groupMaker != null)
            {
                groupMaker.traders = __state;
            }
            return __exception;
        }

        public static void BestowingRunIntPrefix()
        {
            bestowerEscortMade = 0;
        }

        public static IEnumerable<CodeInstruction> BestowingRunIntTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo janissary = AccessTools.Field(typeof(PawnKindDefOf), nameof(PawnKindDefOf.Empire_Fighter_Janissary));
            MethodInfo escortKind = AccessTools.Method(typeof(Patch_ImperialJusticiars), nameof(BestowerEscortKind));
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.LoadsField(janissary))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = escortKind;
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1)
            {
                Log.Warning("[Justiciar Valor] Expected one janissary in the bestowing ceremony's escort loop, found " + replaced
                    + "; justiciars may not join the bestower's escort.");
            }
        }

        // The kind of the bestower's next escort: justiciars first, then janissaries.
        public static PawnKindDef BestowerEscortKind()
        {
            bestowerEscortMade++;
            return bestowerEscortMade <= JusticiarsInBestowerEscort && MJV_DefOf.MJV_Justiciar != null
                ? MJV_DefOf.MJV_Justiciar
                : PawnKindDefOf.Empire_Fighter_Janissary;
        }
    }
}
