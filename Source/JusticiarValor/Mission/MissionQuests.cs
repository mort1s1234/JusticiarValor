using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace JusticiarValor
{
    // Checks that the Empire can send one of its justiciars on a mission to the quest's map and puts the Empire ("empire")
    // and the name of its faith ("faithName") in the slate. With requireFaith the map needs at least minUnconverted
    // colonists who do not follow the imperial faith (and Ideology).
    public class QuestNode_MJV_ImperialMission : QuestNode
    {
        public SlateRef<bool> requireFaith;
        public SlateRef<int> minUnconverted;
        public bool disgracedSermon;

        protected override bool TestRunInt(Slate slate)
        {
            // Gate new offers only. Existing quests keep their parts and can still finish after a settings change.
            return (disgracedSermon ? JusticiarValorMod.Settings.enableDisgracedSermon : JusticiarValorMod.Settings.enableImperialMissions)
                && Setup(slate);
        }

        protected override void RunInt()
        {
            Setup(QuestGen.slate);
        }

        private bool Setup(Slate slate)
        {
            var map = slate.Get<Map>("map");
            Faction empire = ImperialFaith.Empire;
            if (map == null || empire == null || !ImperialFaith.EmpireAtPeace)
            {
                return false;
            }
            if (requireFaith.GetValue(slate) && !ImperialFaith.CanHostMission(map, System.Math.Max(1, minUnconverted.GetValue(slate))))
            {
                return false;
            }
            slate.Set("empire", empire);
            slate.Set("faithName", ImperialFaith.Ideo?.name ?? (string)"MJV_ImperialFaithFallback".Translate());
            slate.Set("unconvertedCount", ImperialFaith.UnconvertedCount(map));
            slate.Set("missionRecruitmentWarning", JusticiarValorMod.Settings.allowImperialRecruitment
                ? "\n\n" + (string)"MJV_MissionRecruitmentWarning".Translate() : "");
            return true;
        }
    }

    // Has the given pawn preach the Empire's primary faith.
    public class QuestNode_MJV_Ordain : QuestNode
    {
        public SlateRef<Pawn> pawn;

        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            ImperialFaith.Ordain(pawn.GetValue(QuestGen.slate));
        }
    }

    // The faction the disgraced justiciar failed to convert, now hunting him: the hostile humanlike faction with the
    // settlement nearest to the map.
    public class QuestNode_MJV_GetPursuers : QuestNode
    {
        [NoTranslate]
        public SlateRef<string> storeAs = "enemyFaction";

        protected override bool TestRunInt(Slate slate)
        {
            Faction faction = FindPursuers(slate.Get<Map>("map"));
            if (faction == null)
            {
                return false;
            }
            slate.Set(storeAs.GetValue(slate), faction);
            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Faction faction = FindPursuers(slate.Get<Map>("map"));
            if (faction != null)
            {
                slate.Set(storeAs.GetValue(slate), faction);
            }
        }

        public static Faction FindPursuers(Map map)
        {
            if (map == null)
            {
                return null;
            }
            Faction best = null;
            float bestDistance = float.MaxValue;
            // The raid that follows must be possible: the same test the game applies to raiding factions (earliest raid
            // day, arrival temperatures, planet layer, a usable raid strategy).
            var raid = (IncidentWorker_RaidEnemy)IncidentDefOf.RaidEnemy.Worker;
            var parms = new IncidentParms { target = map, points = StorytellerUtility.DefaultThreatPointsNow(map) };
            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
            {
                if (faction.IsPlayer || faction.defeated || faction.Hidden || faction.temporary || !faction.def.humanlikeFaction
                    || faction == ImperialFaith.Empire || !faction.HostileTo(Faction.OfPlayer)
                    || faction.def.pawnGroupMakers == null || !faction.def.pawnGroupMakers.Any(m => m.kindDef == PawnGroupKindDefOf.Combat)
                    || !raid.FactionCanBeGroupSource(faction, parms))
                {
                    continue;
                }
                float distance = 100000f;
                List<Settlement> settlements = Find.WorldObjects.Settlements;
                for (int i = 0; i < settlements.Count; i++)
                {
                    Settlement settlement = settlements[i];
                    if (settlement.Faction == faction && settlement.Tile.Layer == map.Tile.Layer)
                    {
                        distance = System.Math.Min(distance, Find.WorldGrid.ApproxDistanceInTiles(map.Tile, settlement.Tile));
                    }
                }
                if (distance < bestDistance)
                {
                    best = faction;
                    bestDistance = distance;
                }
            }
            return best;
        }
    }


    // The lodging justiciar's sermons, see QuestPart_MJV_LodgerSermons.
    public class QuestNode_MJV_LodgerSermons : QuestNode
    {
        public SlateRef<Pawn> preacher;
        public SlateRef<IntRange> intervalTicks;
        public SlateRef<IntRange> firstSermonDelayTicks;
        public SlateRef<float> callChance;
        public SlateRef<Thing> shuttle;
        public SlateRef<bool> blueprintMission;
        public SlateRef<bool> suppressCodex;

        // How he preaches, what he asks of the colony and what it earns or costs with the Empire.
        public SermonSettings sermon = new SermonSettings();

        [NoTranslate]
        public SlateRef<string> inSignalShuttleArrived;

        [NoTranslate]
        public SlateRef<string> outSignalColonyConverted;

        [NoTranslate]
        public SlateRef<string> outSignalMissionSucceeded;

        [NoTranslate]
        public SlateRef<string> outSignalMissionFailed;

        protected override bool TestRunInt(Slate slate) => slate.Get<Map>("map") != null;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            var part = new QuestPart_MJV_LodgerSermons
            {
                inSignalEnable = slate.Get<string>("inSignal"),
                preacher = preacher.GetValue(slate),
                mapParent = slate.Get<Map>("map").Parent,
                intervalTicks = intervalTicks.GetValue(slate),
                firstSermonDelayTicks = firstSermonDelayTicks.GetValue(slate),
                callChance = callChance.GetValue(slate),
                shuttle = shuttle.GetValue(slate),
                blueprintMission = blueprintMission.GetValue(slate),
                suppressCodex = suppressCodex.GetValue(slate),
                settings = sermon?.Copy() ?? new SermonSettings(),
                inSignalShuttleArrived = QuestGenUtility.HardcodedSignalWithQuestID(inSignalShuttleArrived.GetValue(slate)),
                inSignalShuttleSent = QuestGenUtility.HardcodedSignalWithQuestID("pickupShipThing.SentSatisfied"),
                inSignalShuttleSentUnsatisfied = QuestGenUtility.HardcodedSignalWithQuestID("pickupShipThing.SentUnsatisfied"),
                outSignalColonyConverted = QuestGenUtility.HardcodedSignalWithQuestID(outSignalColonyConverted.GetValue(slate)),
                outSignalMissionSucceeded = QuestGenUtility.HardcodedSignalWithQuestID(outSignalMissionSucceeded.GetValue(slate)),
                outSignalMissionFailed = QuestGenUtility.HardcodedSignalWithQuestID(outSignalMissionFailed.GetValue(slate))
            };
            if (part.blueprintMission)
            {
                part.settings.requiredFraction = MissionRewards.BlueprintAttendance;
                part.settings.codexMinMood = MissionRewards.BlueprintMood;
                part.requiredSermons = MissionRewards.BlueprintSermons;
            }
            QuestGen.quest.AddPart(part);
        }
    }

    // The imperial mission, lodging: while the justiciar (a quest lodger) stays with the colony he asks it for a sermon
    // every so often (SermonRequests). Once every colonist follows the imperial faith his work is done and the pickup
    // shuttle is called early. When the shuttle comes there is a small chance that one of his converts has heard the call
    // and leaves with him to serve the Empire (MentalState_MJV_CalledToService). When he has left, what his sermons earned
    // and cost is settled with the Empire (MissionRecord).
    public class QuestPart_MJV_LodgerSermons : QuestPartActivable, IJusticiarMission
    {
        public Pawn preacher;
        public MapParent mapParent;
        public SermonSettings settings = new SermonSettings();
        public IntRange intervalTicks = new IntRange(60000, 90000);
        public IntRange firstSermonDelayTicks = new IntRange(5000, 10000);
        public float callChance;
        public Thing shuttle;
        public string inSignalShuttleArrived;
        public string inSignalShuttleSent;
        public string inSignalShuttleSentUnsatisfied;
        public string outSignalColonyConverted;
        public string outSignalMissionSucceeded;
        public string outSignalMissionFailed;
        public bool blueprintMission;
        // Missing in old saves: their already-promised codex and original completion signals remain intact.
        public bool suppressCodex;
        public int requiredSermons;

        private int nextSermonTick = -1;
        private bool departing;
        private bool colonyConverted;
        private List<Pawn> converts = new List<Pawn>();
        private Pawn called;
        private int sermonsHeld;
        private SermonRequest request;
        private MissionRecord record = new MissionRecord();
        private int nextMoodSampleTick;
        private int qualifyingSermons;
        private bool poorAttendance;

        public int QualifyingSermons => qualifyingSermons;

        public bool BlueprintRequirementsMet => !blueprintMission || record.refusals == 0 && !poorAttendance
            && record.AverageMood >= settings.codexMinMood && (qualifyingSermons >= requiredSermons || colonyConverted);

        public override string DescriptionPart => !blueprintMission || State != QuestPartState.Enabled ? null
            : (string)"MJV_BlueprintMissionProgress".Translate(qualifyingSermons.Named("HELD"), requiredSermons.Named("REQUIRED"),
                (record.AverageMood < 0f ? "-" : record.AverageMood.ToStringPercent()).Named("MOOD"), record.refusals.Named("REFUSED"),
                (colonyConverted ? "Yes".Translate() : "No".Translate()).Named("CONVERTED"),
                (poorAttendance ? "No".Translate() : "Yes".Translate()).Named("ATTENDANCE"));

        public Pawn Called => called;

        public int SermonsHeld => sermonsHeld;

        public List<Pawn> Converts => converts;

        public int NextSermonTick
        {
            get => nextSermonTick;
            set => nextSermonTick = value;
        }

        public Pawn Justiciar => preacher;

        public SermonSettings Settings => settings;

        public MissionRecord Record => record;

        public SermonRequest Request => request;

        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                foreach (GlobalTargetInfo target in base.QuestLookTargets)
                {
                    yield return target;
                }
                if (preacher != null)
                {
                    yield return preacher;
                }
            }
        }

        protected override void Enable(SignalArgs receivedArgs)
        {
            base.Enable(receivedArgs);
            nextSermonTick = Find.TickManager.TicksGame + firstSermonDelayTicks.RandomInRange;
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (State != QuestPartState.Enabled || Find.TickManager.TicksGame % 120 != 0)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            MaintainCalling();
            if (now >= nextMoodSampleTick && preacher != null && preacher.Spawned)
            {
                nextMoodSampleTick = now + GenDate.TicksPerHour;
                record.SampleMood(preacher);
            }
            if (request != null)
            {
                if (request.TicksLeft <= 0)
                {
                    Expire();
                }
                return;
            }
            if (departing || colonyConverted || now < nextSermonTick)
            {
                return;
            }
            Map map = mapParent?.Map;
            if (map == null || preacher == null || preacher.Map != map || preacher.GetLord() != null || preacher.InBed()
                || !SermonRequests.CanRequestNow(preacher, map))
            {
                return;
            }
            request = SermonRequests.TryMakeRequest(preacher, preacher.Position, settings, quest);
            if (request == null)
            {
                nextSermonTick = now + 2500;
            }
        }

        private void Expire()
        {
            if (SermonRequests.ExpiryIsRefusal(this))
            {
                record.RecordRefusal(preacher);
                Messages.Message("MJV_SermonRequestIgnored".Translate(preacher.Named("JUSTICIAR"), (-settings.refusalGoodwill).Named("PENALTY")),
                    preacher, MessageTypeDefOf.NegativeEvent);
                nextSermonTick = Find.TickManager.TicksGame + intervalTicks.RandomInRange;
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
            nextSermonTick = Find.TickManager.TicksGame + intervalTicks.RandomInRange;
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
            sermonsHeld++;
        }

        public void Notify_SermonEnded(SermonResult result)
        {
            converts.AddRange(result.converts.Where(p => p != null && !converts.Contains(p)));
            if (result.outcome == SermonOutcome.Held)
            {
                record.RecordSermon(preacher, settings, result.turnout);
                if (blueprintMission)
                {
                    if (result.eligible > 0 && result.attendees >= SermonRequests.Required(result.eligible, settings.requiredFraction))
                    {
                        qualifyingSermons++;
                    }
                    else
                    {
                        poorAttendance = true;
                    }
                }
            }
            else if (result.outcome == SermonOutcome.Cancelled)
            {
                record.RecordRefusal(preacher);
            }
            CheckColonyConverted();
        }

        public override IEnumerable<Gizmo> ExtraGizmos(ISelectable target)
        {
            IEnumerable<Gizmo> baseGizmos = base.ExtraGizmos(target);
            if (baseGizmos != null)
            {
                foreach (Gizmo gizmo in baseGizmos)
                {
                    yield return gizmo;
                }
            }
            if (target == preacher)
            {
                foreach (Gizmo gizmo in SermonRequests.Gizmos(this))
                {
                    yield return gizmo;
                }
            }
        }

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag == inSignalShuttleArrived)
            {
                // He is leaving: a sermon he was still waiting for is off.
                departing = true;
                request = null;
                TryCall();
            }
            else if (signal.tag == inSignalShuttleSent)
            {
                if (record.Settled || quest.State != QuestState.Ongoing)
                {
                    return;
                }
                CompleteCalling();
                // Conversion through other means also fulfils the colony-wide objective. Only the sermon count is
                // waived; mood, attendance and refusals still matter.
                colonyConverted |= mapParent?.Map != null && ImperialFaith.ColonyConverted(mapParent.Map);
                bool success = BlueprintRequirementsMet;
                record.Settle(preacher, settings, mapParent?.Map, giveCodex: !suppressCodex);
                string completionSignal = success ? outSignalMissionSucceeded : outSignalMissionFailed;
                if (!completionSignal.NullOrEmpty())
                {
                    Find.SignalManager.SendSignal(new Signal(completionSignal));
                }
            }
            else if (signal.tag == inSignalShuttleSentUnsatisfied)
            {
                CancelCalling();
            }
        }

        private void CheckColonyConverted()
        {
            Map map = mapParent?.Map;
            if (colonyConverted || departing || map == null || !ImperialFaith.ColonyConverted(map))
            {
                return;
            }
            colonyConverted = true;
            request = null;
            Find.LetterStack.ReceiveLetter("MJV_MissionColonyConvertedLabel".Translate(),
                "MJV_MissionColonyConvertedText".Translate(preacher.Named("PREACHER"), SermonRequests.FaithName.Named("FAITH")),
                LetterDefOf.PositiveEvent, preacher, null, quest);
            if (!outSignalColonyConverted.NullOrEmpty())
            {
                Find.SignalManager.SendSignal(new Signal(outSignalColonyConverted));
            }
            // His stay ends now: the shuttle waiting to collect him is sent at once.
            foreach (QuestPart part in quest.PartsListForReading)
            {
                if (part is QuestPart_ShuttleDelay delay && delay.State == QuestPartState.Enabled && delay.lodgers.Contains(preacher))
                {
                    delay.delayTicks -= delay.TicksLeft;
                }
            }
        }

        private void TryCall()
        {
            // Read the current setting here, so it also prevents new calls in missions generated before it changed.
            // A call that has already started keeps its saved pawn/shuttle state and completes normally.
            if (!JusticiarValorMod.Settings.allowImperialRecruitment)
            {
                return;
            }
            CompShuttle comp = shuttle?.TryGetComp<CompShuttle>();
            Map map = shuttle?.MapHeld;
            if (called != null || comp == null || map == null || callChance <= 0f || !Rand.Chance(callChance))
            {
                return;
            }
            // Never one of the colony's last people.
            if (ImperialFaith.Congregation(map).Count() <= 2)
            {
                return;
            }
            IEnumerable<Pawn> candidates = converts.Where(p => p != null && p.Spawned && p.Map == map && ImperialFaith.IsCongregant(p)
                && ImperialFaith.IsBeliever(p) && p.DevelopmentalStage.Adult() && !p.Downed && !p.InMentalState && !p.Drafted);
            if (!candidates.TryRandomElement(out Pawn pawn)
                || !pawn.mindState.mentalStateHandler.TryStartMentalState(MJV_DefOf.MJV_CalledToService, null, forced: true, forceWake: true, transitionSilently: true))
            {
                return;
            }
            if (pawn.MentalState is MentalState_MJV_CalledToService state)
            {
                state.shuttle = shuttle;
            }
            comp.requiredPawns.Add(pawn);
            called = pawn;
            Find.LetterStack.ReceiveLetter("MJV_CalledToServiceLabel".Translate(pawn.Named("PAWN")),
                "MJV_CalledToServiceText".Translate(pawn.Named("PAWN"), preacher.Named("PREACHER"), SermonRequests.FaithName.Named("FAITH")),
                LetterDefOf.NegativeEvent, new LookTargets(pawn, shuttle), null, quest);
        }

        // A called convert who is no longer on the way (arrested, downed, came to their senses) is not waited for.
        private void MaintainCalling()
        {
            if (called == null)
            {
                return;
            }
            CompShuttle comp = shuttle?.TryGetComp<CompShuttle>();
            if (comp != null && comp.Transporter.innerContainer.Contains(called))
            {
                return;
            }
            // The shuttle would wait for them in vain (and leave unsatisfied, failing the quest) if they cannot get there.
            if (called.Dead || !called.Spawned || !(called.MentalState is MentalState_MJV_CalledToService)
                || shuttle.Spawned && !called.CanReach(shuttle, PathEndMode.Touch, Danger.Deadly))
            {
                CancelCalling();
            }
        }

        private void CompleteCalling()
        {
            CompShuttle comp = shuttle?.TryGetComp<CompShuttle>();
            if (called == null || comp == null || !comp.Transporter.innerContainer.Contains(called))
            {
                CancelCalling();
                return;
            }
            Pawn pawn = called;
            called = null;
            pawn.mindState?.mentalStateHandler?.Reset();
            Faction empire = ImperialFaith.Empire;
            if (empire != null)
            {
                pawn.SetFaction(empire);
            }
            Find.LetterStack.ReceiveLetter("MJV_CalledToServiceLeftLabel".Translate(pawn.Named("PAWN")),
                "MJV_CalledToServiceLeftText".Translate(pawn.Named("PAWN"), preacher.Named("PREACHER")), LetterDefOf.NegativeEvent, null, null, quest);
        }

        private void CancelCalling()
        {
            if (called == null)
            {
                return;
            }
            Pawn pawn = called;
            called = null;
            shuttle?.TryGetComp<CompShuttle>()?.requiredPawns.Remove(pawn);
            if (pawn.Spawned && pawn.MentalState is MentalState_MJV_CalledToService state)
            {
                state.RecoverFromState();
            }
        }

        public override void Cleanup()
        {
            base.Cleanup();
            request = null;
            CancelCalling();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref preacher, "preacher");
            Scribe_References.Look(ref mapParent, "mapParent");
            Scribe_Deep.Look(ref settings, "settings");
            Scribe_Values.Look(ref intervalTicks, "intervalTicks");
            Scribe_Values.Look(ref firstSermonDelayTicks, "firstSermonDelayTicks");
            Scribe_Values.Look(ref callChance, "callChance", 0f);
            Scribe_References.Look(ref shuttle, "shuttle");
            Scribe_Values.Look(ref inSignalShuttleArrived, "inSignalShuttleArrived");
            Scribe_Values.Look(ref inSignalShuttleSent, "inSignalShuttleSent");
            Scribe_Values.Look(ref inSignalShuttleSentUnsatisfied, "inSignalShuttleSentUnsatisfied");
            Scribe_Values.Look(ref outSignalColonyConverted, "outSignalColonyConverted");
            Scribe_Values.Look(ref outSignalMissionSucceeded, "outSignalMissionSucceeded");
            Scribe_Values.Look(ref outSignalMissionFailed, "outSignalMissionFailed");
            Scribe_Values.Look(ref blueprintMission, "blueprintMission", false);
            Scribe_Values.Look(ref suppressCodex, "suppressCodex", false);
            Scribe_Values.Look(ref requiredSermons, "requiredSermons", 0);
            Scribe_Values.Look(ref qualifyingSermons, "qualifyingSermons", 0);
            Scribe_Values.Look(ref poorAttendance, "poorAttendance", false);
            Scribe_Values.Look(ref nextSermonTick, "nextSermonTick", -1);
            Scribe_Values.Look(ref departing, "departing", false);
            Scribe_Values.Look(ref colonyConverted, "colonyConverted", false);
            Scribe_Collections.Look(ref converts, "converts", LookMode.Reference);
            Scribe_References.Look(ref called, "called");
            Scribe_Values.Look(ref sermonsHeld, "sermonsHeld", 0);
            Scribe_Deep.Look(ref request, "request");
            Scribe_Deep.Look(ref record, "record");
            Scribe_Values.Look(ref nextMoodSampleTick, "nextMoodSampleTick", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                settings ??= new SermonSettings();
                record ??= new MissionRecord();
                converts ??= new List<Pawn>();
                converts.RemoveAll(p => p == null);
            }
        }
    }
}
