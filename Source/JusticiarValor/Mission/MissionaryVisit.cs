using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace JusticiarValor
{
    public class LordToilData_MJV_Preach : LordToilData
    {
        public IntVec3 spot = IntVec3.Invalid;
        public int ticks;
        public bool started;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref spot, "spot", IntVec3.Invalid);
            Scribe_Values.Look(ref ticks, "ticks", 0);
            Scribe_Values.Look(ref started, "started", false);
        }
    }

    // A visiting missionary holds a sermon: he walks to a gathering spot of the colony and preaches, his escort
    // keeps close. The colonists' side of it is the LordJob_MJV_Sermon this toil starts once he is in place.
    public class LordToil_MJV_Preach : LordToil
    {
        private const int GiveUpTicks = 3000;

        private readonly Pawn preacher;
        private readonly IntVec3 fallbackSpot;
        private readonly SermonSettings settings;

        public LordToil_MJV_Preach(Pawn preacher, IntVec3 fallbackSpot, SermonSettings settings)
        {
            this.preacher = preacher;
            this.fallbackSpot = fallbackSpot;
            this.settings = settings;
            data = new LordToilData_MJV_Preach();
        }

        public LordToilData_MJV_Preach Data => (LordToilData_MJV_Preach)data;

        public override bool AllowSatisfyLongNeeds => false;

        public override void Init()
        {
            base.Init();
            Data.ticks = 0;
            Data.started = false;
            Data.spot = preacher != null && preacher.Spawned && SermonUtility.TryFindSermonSpot(preacher, fallbackSpot, out IntVec3 spot) ? spot : IntVec3.Invalid;
        }

        public override void UpdateAllDuties()
        {
            List<Pawn> pawns = lord.ownedPawns;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == preacher)
                {
                    pawn.mindState.duty = LordToil_MJV_Sermon.PreacherDuty(Data.spot.IsValid ? Data.spot : pawn.Position);
                }
                else
                {
                    pawn.mindState.duty = new PawnDuty(DutyDefOf.Escort, preacher, 6f);
                }
            }
        }

        public override void LordToilTick()
        {
            Data.ticks++;
            if (!Data.spot.IsValid)
            {
                lord.ReceiveMemo(SermonUtility.MemoSermonEnded);
                return;
            }
            if (!Data.started)
            {
                if (LordToil_MJV_Sermon.IsPreaching(preacher, Data.spot))
                {
                    Data.started = true;
                    SermonUtility.StartSermon(preacher, Data.spot, settings, null);
                }
                else if (Data.ticks > GiveUpTicks)
                {
                    lord.ReceiveMemo(SermonUtility.MemoSermonEnded);
                }
                return;
            }
            // The sermon reports its end with the memo; this only catches one that vanished without concluding.
            if (Data.ticks % 250 == 0 && SermonUtility.SermonOf(preacher) == null)
            {
                lord.ReceiveMemo(SermonUtility.MemoSermonEnded);
            }
        }
    }

    // An imperial missionary visiting the colony, alone or with an escort of justiciars: like other visitors he
    // waits near the colony, and from time to time he holds a sermon. He leaves when his visit is over, or early once
    // the whole colony follows the imperial faith.
    public class LordJob_MJV_MissionaryVisit : LordJob
    {
        private Faction faction;
        private IntVec3 chillSpot;
        private Pawn preacher;
        private SermonSettings settings;
        private IntRange sermonIntervalTicks;
        private int nextSermonTick;
        private int leaveTick;

        public StateGraph exitSubgraph;

        public LordJob_MJV_MissionaryVisit()
        {
        }

        public LordJob_MJV_MissionaryVisit(Faction faction, IntVec3 chillSpot, Pawn preacher, int durationTicks, int firstSermonDelayTicks,
            IntRange sermonIntervalTicks, SermonSettings settings)
        {
            this.faction = faction;
            this.chillSpot = chillSpot;
            this.preacher = preacher;
            this.sermonIntervalTicks = sermonIntervalTicks;
            this.settings = settings?.Copy() ?? new SermonSettings();
            int now = Find.TickManager.TicksGame;
            nextSermonTick = now + firstSermonDelayTicks;
            leaveTick = now + durationTicks;
        }

        public Pawn Preacher => preacher;

        public int LeaveTick => leaveTick;

        public int NextSermonTick
        {
            get => nextSermonTick;
            set => nextSermonTick = value;
        }

        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            LordToil travel = graph.StartingToil = graph.AttachSubgraph(new LordJob_Travel(chillSpot).CreateGraph()).StartingToil;
            var wait = new LordToil_DefendPoint(chillSpot);
            graph.AddToil(wait);
            var preach = new LordToil_MJV_Preach(preacher, chillSpot, settings);
            graph.AddToil(preach);
            var takeWounded = new LordToil_TakeWoundedGuest();
            graph.AddToil(takeWounded);
            exitSubgraph = new LordJob_TravelAndExit(IntVec3.Invalid).CreateGraph();
            LordToil exit = graph.AttachSubgraph(exitSubgraph).StartingToil;
            LordToil exitDefending = exitSubgraph.lordToils[1];
            var exitDigging = new LordToil_ExitMap(LocomotionUrgency.Walk, canDig: true);
            graph.AddToil(exitDigging);
            string pawnsPlural = faction?.def.pawnsPlural.CapitalizeFirst() ?? string.Empty;
            string factionName = faction?.Name ?? string.Empty;

            var arrived = new Transition(travel, wait);
            arrived.AddTrigger(new Trigger_Memo("TravelArrived"));
            graph.AddTransition(arrived);

            var startSermon = new Transition(wait, preach);
            startSermon.AddTrigger(new Trigger_TickCondition(SermonDue, 120));
            startSermon.AddPreAction(new TransitionAction_Custom(() => nextSermonTick = Find.TickManager.TicksGame + sermonIntervalTicks.RandomInRange));
            startSermon.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(startSermon);

            var sermonOver = new Transition(preach, wait);
            sermonOver.AddTrigger(new Trigger_Memo(SermonUtility.MemoSermonEnded));
            graph.AddTransition(sermonOver);

            var dangerousTemperature = new Transition(travel, exit);
            dangerousTemperature.AddSources(wait, preach);
            dangerousTemperature.AddTrigger(new Trigger_PawnExperiencingDangerousTemperatures());
            dangerousTemperature.AddPreAction(new TransitionAction_Message("MessageVisitorsDangerousTemperature".Translate(pawnsPlural, factionName)));
            dangerousTemperature.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            dangerousTemperature.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(dangerousTemperature);

            var anomalousWeather = new Transition(travel, exit);
            anomalousWeather.AddSources(wait, preach);
            anomalousWeather.AddTrigger(new Trigger_PawnExperiencingAnomalousWeather());
            anomalousWeather.AddPreAction(new TransitionAction_Message("MessageVisitorsAnomalousWeather".Translate(pawnsPlural, factionName)));
            anomalousWeather.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            anomalousWeather.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(anomalousWeather);

            var trapped = new Transition(travel, exitDigging);
            trapped.AddSources(wait, preach, takeWounded);
            trapped.AddSources(exitSubgraph.lordToils);
            trapped.AddTrigger(new Trigger_PawnCannotReachMapEdge());
            trapped.AddPreAction(new TransitionAction_Message("MessageVisitorsTrappedLeaving".Translate(pawnsPlural, factionName)));
            graph.AddTransition(trapped);

            var freed = new Transition(exitDigging, exit);
            freed.AddTrigger(new Trigger_PawnCanReachMapEdge());
            freed.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            freed.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(freed);

            var wounded = new Transition(wait, takeWounded);
            wounded.AddSource(preach);
            wounded.AddTrigger(new Trigger_WoundedGuestPresent());
            wounded.AddPreAction(new TransitionAction_Message("MessageVisitorsTakingWounded".Translate(pawnsPlural, factionName)));
            graph.AddTransition(wounded);

            var hostile = new Transition(wait, exitDefending);
            hostile.AddSources(takeWounded, travel, preach);
            hostile.AddTrigger(new Trigger_BecamePlayerEnemy());
            hostile.AddPreAction(new TransitionAction_SetDefendLocalGroup());
            hostile.AddPostAction(new TransitionAction_WakeAll());
            hostile.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(hostile);

            // Without the missionary there is no mission: his escort goes home.
            var preacherLost = new Transition(wait, exit);
            preacherLost.AddSources(travel, preach);
            preacherLost.AddTrigger(new Trigger_PawnLost(PawnLostCondition.Undefined, preacher));
            preacherLost.AddPreAction(new TransitionAction_Message("MJV_MissionEscortLeaving".Translate(factionName)));
            preacherLost.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            preacherLost.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(preacherLost);

            var converted = new Transition(wait, exit);
            converted.AddTrigger(new Trigger_TickCondition(() => ImperialFaith.ColonyConverted(Map), 250));
            converted.AddPreAction(new TransitionAction_Custom(() => Messages.Message(
                "MJV_MissionaryLeavingConverted".Translate(preacher.Named("PREACHER"), (ImperialFaith.Ideo?.name ?? string.Empty).Named("FAITH")),
                preacher, MessageTypeDefOf.PositiveEvent)));
            converted.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            converted.AddPostAction(new TransitionAction_WakeAll());
            graph.AddTransition(converted);

            // A visit's end waits for a sermon in progress to finish.
            var visitOver = new Transition(wait, exit);
            visitOver.AddTrigger(new Trigger_TickCondition(() => Find.TickManager.TicksGame >= leaveTick, 60));
            visitOver.AddPreAction(new TransitionAction_Custom(() => Messages.Message("MJV_MissionaryLeaving".Translate(preacher.Named("PREACHER")),
                preacher, MessageTypeDefOf.NeutralEvent)));
            visitOver.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            visitOver.AddPostAction(new TransitionAction_WakeAll());
            graph.AddTransition(visitOver);
            return graph;
        }

        private bool SermonDue()
        {
            return Find.TickManager.TicksGame >= nextSermonTick && Find.TickManager.TicksGame < leaveTick - 2500
                && SermonUtility.CanPreachNow(preacher, Map);
        }

        public override string GetReport(Pawn pawn)
        {
            return lord.CurLordToil is LordToil_MJV_Preach && pawn == preacher ? (string)"MJV_LordReportPreaching".Translate() : base.GetReport(pawn);
        }

        public override void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref chillSpot, "chillSpot");
            Scribe_References.Look(ref preacher, "preacher");
            Scribe_Deep.Look(ref settings, "settings");
            Scribe_Values.Look(ref sermonIntervalTicks, "sermonIntervalTicks");
            Scribe_Values.Look(ref nextSermonTick, "nextSermonTick", 0);
            Scribe_Values.Look(ref leaveTick, "leaveTick", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && settings == null)
            {
                settings = new SermonSettings();
            }
        }
    }
}
