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

        public override void ExposeData()
        {
            Scribe_Values.Look(ref spot, "spot", IntVec3.Invalid);
            Scribe_Values.Look(ref ticks, "ticks", 0);
        }
    }

    // A visiting justiciar preaches the sermon the colony agreed to: he walks to its spot and preaches there, his escort
    // keeps close. The colonists' side of it is the LordJob_MJV_Sermon started with the player's answer; this toil ends
    // with it.
    public class LordToil_MJV_Preach : LordToil
    {
        private readonly Pawn preacher;

        public LordToil_MJV_Preach(Pawn preacher)
        {
            this.preacher = preacher;
            data = new LordToilData_MJV_Preach();
        }

        public LordToilData_MJV_Preach Data => (LordToilData_MJV_Preach)data;

        public override bool AllowSatisfyLongNeeds => false;

        public override void Init()
        {
            base.Init();
            Data.ticks = 0;
            Data.spot = SermonUtility.SermonOf(preacher)?.Spot ?? IntVec3.Invalid;
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
            // The sermon reports its end with the memo; this only catches one that vanished without concluding.
            if (!Data.spot.IsValid || Data.ticks % 250 == 0 && SermonUtility.SermonOf(preacher) == null)
            {
                lord.ReceiveMemo(SermonUtility.MemoSermonEnded);
            }
        }
    }

    // A justiciar visiting the colony on a mission, alone or with an escort. He first waits near the colony for someone to
    // talk to him (LordToil_MJV_AwaitAudience) and stays only if the colony agrees to host him; sent away or left waiting,
    // he goes home and the Empire takes offense. Hosted, he waits near the colony like other visitors, and now and then he
    // asks the colony for a sermon (SermonRequests). He leaves when his visit is over, or early once the whole colony
    // follows the imperial faith, and then settles with the Empire what his sermons earned and cost.
    public class LordJob_MJV_MissionaryVisit : LordJob, IJusticiarMission
    {
        public const string MemoSermonAccepted = "MJV_SermonAccepted";
        public const string MemoHosted = "MJV_JusticiarHosted";
        public const string MemoTurnedAway = "MJV_JusticiarTurnedAway";

        private Faction faction;
        private IntVec3 chillSpot;
        private Pawn preacher;
        private SermonSettings settings;
        private IntRange sermonIntervalTicks;
        private int nextSermonTick;
        private int leaveTick;
        private SermonRequest request;
        private MissionRecord record = new MissionRecord();
        private int nextMoodSampleTick;

        // The colony's answer to his arrival, and what he told it: how long he would stay, when his first sermon would
        // come, what sending him away costs, and until when he waits for someone to talk to him.
        private bool hosted;
        private bool turnedAway;
        private int visitTicks;
        private int firstSermonDelayTicks;
        private int turnedAwayGoodwill;
        private int audienceEndTick;

        public StateGraph exitSubgraph;

        public LordJob_MJV_MissionaryVisit()
        {
        }

        public LordJob_MJV_MissionaryVisit(Faction faction, IntVec3 chillSpot, Pawn preacher, MissionExtension ext)
        {
            this.faction = faction;
            this.chillSpot = chillSpot;
            this.preacher = preacher;
            sermonIntervalTicks = ext.sermonIntervalTicks;
            settings = ext.sermon?.Copy() ?? new SermonSettings();
            visitTicks = (int)(ext.visitDays.RandomInRange * GenDate.TicksPerDay);
            firstSermonDelayTicks = ext.firstSermonDelayTicks.RandomInRange;
            turnedAwayGoodwill = ext.turnedAwayGoodwill.RandomInRange;
            audienceEndTick = Find.TickManager.TicksGame + ext.audienceTicks;
        }

        public Pawn Preacher => preacher;

        // He is waiting for a colonist to talk to him.
        public bool AwaitingAudience => !hosted && !turnedAway && lord?.CurLordToil is LordToil_MJV_AwaitAudience;

        public int AudienceTicksLeft => audienceEndTick - Find.TickManager.TicksGame;

        public int VisitTicks => visitTicks;

        public int TurnedAwayGoodwill => turnedAwayGoodwill;

        public int LeaveTick => leaveTick;

        public int NextSermonTick
        {
            get => nextSermonTick;
            set => nextSermonTick = value;
        }

        public Pawn Justiciar => preacher;

        public SermonSettings Settings => settings;

        public MissionRecord Record => record;

        public SermonRequest Request => request;

        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            var audience = new LordToil_MJV_AwaitAudience(chillSpot, preacher);
            graph.StartingToil = audience;
            var wait = new LordToil_DefendPoint(chillSpot);
            graph.AddToil(wait);
            var preach = new LordToil_MJV_Preach(preacher);
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
            // Whatever ends the visit, a pending request goes with it.
            var dropRequest = new TransitionAction_Custom(() => request = null);

            var hostedByColony = new Transition(audience, wait);
            hostedByColony.AddTrigger(new Trigger_Memo(MemoHosted));
            graph.AddTransition(hostedByColony);

            // TurnAway has settled it with the Empire.
            var sentAway = new Transition(audience, exit);
            sentAway.AddTrigger(new Trigger_Memo(MemoTurnedAway));
            sentAway.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            sentAway.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(sentAway);

            // The colony agreed to a sermon (Answer has started it): he goes to preach.
            var startSermon = new Transition(wait, preach);
            startSermon.AddTrigger(new Trigger_Memo(MemoSermonAccepted));
            startSermon.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(startSermon);

            var sermonOver = new Transition(preach, wait);
            sermonOver.AddTrigger(new Trigger_Memo(SermonUtility.MemoSermonEnded));
            graph.AddTransition(sermonOver);

            var dangerousTemperature = new Transition(audience, exit);
            dangerousTemperature.AddSources(wait, preach);
            dangerousTemperature.AddTrigger(new Trigger_PawnExperiencingDangerousTemperatures());
            dangerousTemperature.AddPreAction(new TransitionAction_Message("MessageVisitorsDangerousTemperature".Translate(pawnsPlural, factionName)));
            dangerousTemperature.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            dangerousTemperature.AddPreAction(dropRequest);
            dangerousTemperature.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(dangerousTemperature);

            var anomalousWeather = new Transition(audience, exit);
            anomalousWeather.AddSources(wait, preach);
            anomalousWeather.AddTrigger(new Trigger_PawnExperiencingAnomalousWeather());
            anomalousWeather.AddPreAction(new TransitionAction_Message("MessageVisitorsAnomalousWeather".Translate(pawnsPlural, factionName)));
            anomalousWeather.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            anomalousWeather.AddPreAction(dropRequest);
            anomalousWeather.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(anomalousWeather);

            var trapped = new Transition(audience, exitDigging);
            trapped.AddSources(wait, preach, takeWounded);
            trapped.AddSources(exitSubgraph.lordToils);
            trapped.AddTrigger(new Trigger_PawnCannotReachMapEdge());
            trapped.AddPreAction(new TransitionAction_Message("MessageVisitorsTrappedLeaving".Translate(pawnsPlural, factionName)));
            trapped.AddPreAction(dropRequest);
            graph.AddTransition(trapped);

            var freed = new Transition(exitDigging, exit);
            freed.AddTrigger(new Trigger_PawnCanReachMapEdge());
            freed.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            freed.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(freed);

            var wounded = new Transition(wait, takeWounded);
            wounded.AddSources(audience, preach);
            wounded.AddTrigger(new Trigger_WoundedGuestPresent());
            wounded.AddPreAction(new TransitionAction_Message("MessageVisitorsTakingWounded".Translate(pawnsPlural, factionName)));
            wounded.AddPreAction(dropRequest);
            graph.AddTransition(wounded);

            var hostile = new Transition(wait, exitDefending);
            hostile.AddSources(audience, takeWounded, preach);
            hostile.AddTrigger(new Trigger_BecamePlayerEnemy());
            hostile.AddPreAction(new TransitionAction_SetDefendLocalGroup());
            hostile.AddPreAction(dropRequest);
            hostile.AddPostAction(new TransitionAction_WakeAll());
            hostile.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(hostile);

            // Without the justiciar there is no mission: his escort goes home.
            var preacherLost = new Transition(wait, exit);
            preacherLost.AddSources(audience, preach);
            preacherLost.AddTrigger(new Trigger_PawnLost(PawnLostCondition.Undefined, preacher));
            preacherLost.AddPreAction(new TransitionAction_Message("MJV_MissionEscortLeaving".Translate(factionName)));
            preacherLost.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            preacherLost.AddPreAction(dropRequest);
            preacherLost.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(preacherLost);

            var converted = new Transition(wait, exit);
            converted.AddSource(audience);
            converted.AddTrigger(new Trigger_TickCondition(() => ImperialFaith.ColonyConverted(Map), 250));
            converted.AddPreAction(new TransitionAction_Custom(() => Messages.Message(
                "MJV_MissionaryLeavingConverted".Translate(preacher.Named("PREACHER"), SermonRequests.FaithName.Named("FAITH")),
                preacher, MessageTypeDefOf.PositiveEvent)));
            converted.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            converted.AddPreAction(new TransitionAction_Custom(Depart));
            converted.AddPostAction(new TransitionAction_WakeAll());
            graph.AddTransition(converted);

            // A visit's end waits for a sermon in progress to finish.
            var visitOver = new Transition(wait, exit);
            visitOver.AddTrigger(new Trigger_TickCondition(() => Find.TickManager.TicksGame >= leaveTick, 60));
            visitOver.AddPreAction(new TransitionAction_Custom(() => Messages.Message("MJV_MissionaryLeaving".Translate(preacher.Named("PREACHER")),
                preacher, MessageTypeDefOf.NeutralEvent)));
            visitOver.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            visitOver.AddPreAction(new TransitionAction_Custom(Depart));
            visitOver.AddPostAction(new TransitionAction_WakeAll());
            graph.AddTransition(visitOver);
            return graph;
        }

        // He sets off back to the Empire: what his sermons earned and cost is settled now.
        private void Depart()
        {
            request = null;
            record.Settle(preacher, settings, Map);
        }

        // The colony agreed to host him: his visit begins.
        public void Host()
        {
            if (!AwaitingAudience)
            {
                return;
            }
            hosted = true;
            int now = Find.TickManager.TicksGame;
            leaveTick = now + visitTicks;
            nextSermonTick = now + firstSermonDelayTicks;
            nextMoodSampleTick = now;
            lord.ReceiveMemo(MemoHosted);
            Messages.Message("MJV_MissionaryHosted".Translate(preacher.Named("PREACHER"), visitTicks.ToStringTicksToPeriod().Named("DURATION")),
                preacher, MessageTypeDefOf.PositiveEvent);
        }

        // The colony sent him away (ignored: nobody came to talk to him in time): he goes home, and the Empire takes offense.
        public void TurnAway(bool ignored)
        {
            if (!AwaitingAudience)
            {
                return;
            }
            turnedAway = true;
            Faction empire = preacher.Faction;
            if (empire != null && !empire.HostileTo(Faction.OfPlayer) && turnedAwayGoodwill != 0)
            {
                empire.TryAffectGoodwillWith(Faction.OfPlayer, turnedAwayGoodwill, false, true, MJV_DefOf.MJV_JusticiarTurnedAway);
            }
            Messages.Message((ignored ? "MJV_MissionaryGaveUp" : "MJV_MissionaryTurnedAway").Translate(preacher.Named("PREACHER"),
                (empire?.Name ?? string.Empty).Named("FACTION"), (-turnedAwayGoodwill).Named("PENALTY")), preacher, MessageTypeDefOf.NegativeEvent);
            lord.ReceiveMemo(MemoTurnedAway);
        }

        public override void LordJobTick()
        {
            base.LordJobTick();
            int now = Find.TickManager.TicksGame;
            if (!hosted)
            {
                if (now >= audienceEndTick && AwaitingAudience)
                {
                    TurnAway(true);
                }
                return;
            }
            if (now >= nextMoodSampleTick)
            {
                nextMoodSampleTick = now + GenDate.TicksPerHour;
                record.SampleMood(preacher);
            }
            if (now % 60 != 0)
            {
                return;
            }
            if (request != null)
            {
                if (request.TicksLeft <= 0)
                {
                    Expire();
                }
            }
            else if (lord.CurLordToil is LordToil_DefendPoint && SermonDue())
            {
                // He waits for the answer at most until shortly before he has to leave.
                request = SermonRequests.TryMakeRequest(preacher, chillSpot, settings, null, leaveTick - now - LeaveMarginTicks);
                if (request == null)
                {
                    nextSermonTick = now + GenDate.TicksPerHour;
                }
            }
        }

        // He does not ask for a sermon he would have to leave in the middle of: one lasts under an hour of preaching.
        private const int LeaveMarginTicks = 2500;

        private bool SermonDue()
        {
            int now = Find.TickManager.TicksGame;
            return now >= nextSermonTick && now < leaveTick - LeaveMarginTicks * 2 && SermonRequests.CanRequestNow(preacher, Map);
        }

        private void Expire()
        {
            if (SermonRequests.ExpiryIsRefusal(this))
            {
                record.RecordRefusal(preacher);
                Messages.Message("MJV_SermonRequestIgnored".Translate(preacher.Named("JUSTICIAR"), (-settings.refusalGoodwill).Named("PENALTY")),
                    preacher, MessageTypeDefOf.NegativeEvent);
                nextSermonTick = Find.TickManager.TicksGame + sermonIntervalTicks.RandomInRange;
            }
            else
            {
                Messages.Message("MJV_SermonRequestLapsed".Translate(preacher.Named("JUSTICIAR")), preacher, MessageTypeDefOf.NeutralEvent);
                nextSermonTick = Find.TickManager.TicksGame + GenDate.TicksPerHour * 2;
            }
            request = null;
        }

        public void Answer(List<Pawn> congregation, int required, int eligible)
        {
            if (request == null)
            {
                return;
            }
            nextSermonTick = Find.TickManager.TicksGame + sermonIntervalTicks.RandomInRange;
            if (congregation == null)
            {
                request = null;
                record.RecordRefusal(preacher);
                Messages.Message("MJV_SermonRefused".Translate(preacher.Named("JUSTICIAR"), (-settings.refusalGoodwill).Named("PENALTY")),
                    preacher, MessageTypeDefOf.NegativeEvent);
                return;
            }
            IntVec3 spot = request.spot;
            request = null;
            SermonUtility.StartSermon(preacher, spot, settings, congregation, required, eligible);
            lord.ReceiveMemo(MemoSermonAccepted);
        }

        public void Notify_SermonEnded(SermonResult result)
        {
            if (result.outcome == SermonOutcome.Held)
            {
                record.RecordSermon(preacher, settings, result.turnout);
            }
            else if (result.outcome == SermonOutcome.Cancelled)
            {
                record.RecordRefusal(preacher);
            }
        }

        public override IEnumerable<Gizmo> GetPawnGizmos(Pawn p)
        {
            if (p == preacher)
            {
                foreach (Gizmo gizmo in SermonRequests.Gizmos(this))
                {
                    yield return gizmo;
                }
            }
        }

        public override string GetReport(Pawn pawn)
        {
            if (pawn == preacher)
            {
                if (AwaitingAudience)
                {
                    return "MJV_LordReportAwaitingAudience".Translate();
                }
                if (lord.CurLordToil is LordToil_MJV_Preach)
                {
                    return "MJV_LordReportPreaching".Translate();
                }
                if (request != null)
                {
                    return "MJV_LordReportAwaitingAnswer".Translate();
                }
            }
            return base.GetReport(pawn);
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
            Scribe_Deep.Look(ref request, "request");
            Scribe_Deep.Look(ref record, "record");
            Scribe_Values.Look(ref nextMoodSampleTick, "nextMoodSampleTick", 0);
            Scribe_Values.Look(ref hosted, "hosted", false);
            Scribe_Values.Look(ref turnedAway, "turnedAway", false);
            Scribe_Values.Look(ref visitTicks, "visitTicks", 0);
            Scribe_Values.Look(ref firstSermonDelayTicks, "firstSermonDelayTicks", 0);
            Scribe_Values.Look(ref turnedAwayGoodwill, "turnedAwayGoodwill", 0);
            Scribe_Values.Look(ref audienceEndTick, "audienceEndTick", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                settings ??= new SermonSettings();
                record ??= new MissionRecord();
            }
        }
    }
}
