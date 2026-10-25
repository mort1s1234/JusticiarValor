using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace JusticiarValor
{
    // How a justiciar preaches and what his sermons mean to the Empire. Set in XML on the missionary incident
    // (MissionExtension) and on the lodging quest (QuestNode_MJV_LodgerSermons).
    public class SermonSettings : IExposable
    {
        public IntRange durationTicks = new IntRange(2000, 2600);

        // Certainty a full sermon takes from a listener, as a multiple of one of the game's conversion attempts by
        // the same preacher (which already weighs his conversion power and the listener's certainty loss factor).
        public float certaintyFactor = 2.5f;

        public int goodwillPerConvert = 4;

        // The congregation he asks for: at least this share of the colony's conscious colonists, one at least.
        public float requiredFraction = 0.5f;

        // How long he waits for the colony's answer; silence counts as a refusal.
        public int answerTicks = 15000;

        // Goodwill he brings back to the Empire for a sermon the whole colony heard, in proportion to the attendance
        // beyond the minimum, and its cap over his stay.
        public float fullTurnoutGoodwill = 5f;

        public int maxTurnoutGoodwill = 20;

        // Goodwill lost when he returns, for every sermon refused or cancelled.
        public int refusalGoodwill = -4;

        // The mood he must have had on average, with no sermon refused and one held at least, to leave an imperial
        // codex-auspex when he goes.
        public float codexMinMood = 0.5f;

        public SermonSettings Copy() => (SermonSettings)MemberwiseClone();

        public void ExposeData()
        {
            Scribe_Values.Look(ref durationTicks, "durationTicks", new IntRange(2000, 2600));
            Scribe_Values.Look(ref certaintyFactor, "certaintyFactor", 2.5f);
            Scribe_Values.Look(ref goodwillPerConvert, "goodwillPerConvert", 4);
            Scribe_Values.Look(ref requiredFraction, "requiredFraction", 0.5f);
            Scribe_Values.Look(ref answerTicks, "answerTicks", 15000);
            Scribe_Values.Look(ref fullTurnoutGoodwill, "fullTurnoutGoodwill", 5f);
            Scribe_Values.Look(ref maxTurnoutGoodwill, "maxTurnoutGoodwill", 20);
            Scribe_Values.Look(ref refusalGoodwill, "refusalGoodwill", -4);
            Scribe_Values.Look(ref codexMinMood, "codexMinMood", 0.5f);
        }
    }

    public static class SermonUtility
    {
        public const string MemoSermonEnded = "MJV_SermonEnded";

        // The sermon this pawn is preaching, if any.
        public static LordJob_MJV_Sermon SermonOf(Pawn preacher)
        {
            if (preacher?.MapHeld == null)
            {
                return null;
            }
            List<Lord> lords = preacher.MapHeld.lordManager.lords;
            for (int i = 0; i < lords.Count; i++)
            {
                if (lords[i].LordJob is LordJob_MJV_Sermon sermon && sermon.Preacher == preacher)
                {
                    return sermon;
                }
            }
            return null;
        }

        public static bool AnySermonOn(Map map)
        {
            List<Lord> lords = map.lordManager.lords;
            for (int i = 0; i < lords.Count; i++)
            {
                if (lords[i].LordJob is LordJob_MJV_Sermon)
                {
                    return true;
                }
            }
            return false;
        }

        // Whether a preacher can begin a sermon now: daytime, no danger, no other gathering, someone to convert.
        public static bool CanPreachNow(Pawn preacher, Map map)
        {
            if (preacher == null || !preacher.Spawned || preacher.Map != map || !preacher.Awake()
                || !GatheringsUtility.PawnCanStartOrContinueGathering(preacher))
            {
                return false;
            }
            if (preacher.needs?.food != null && preacher.needs.food.CurCategory >= HungerCategory.UrgentlyHungry)
            {
                return false;
            }
            if (preacher.needs?.rest != null && preacher.needs.rest.CurCategory >= RestCategory.VeryTired)
            {
                return false;
            }
            int hour = GenLocalDate.HourInteger(map);
            if (hour < 8 || hour > 19 || map.dangerWatcher.DangerRating != StoryDanger.None)
            {
                return false;
            }
            return !GatheringsUtility.AnyLordJobPreventsNewGatherings(map) && ImperialFaith.UnconvertedCount(map) > 0;
        }

        // Where to preach: one of the colony's gathering spots if possible, else somewhere close by.
        public static bool TryFindSermonSpot(Pawn preacher, IntVec3 fallback, out IntVec3 spot)
        {
            if (RCellFinder.TryFindGatheringSpot(preacher, GatheringDefOf.Party, true, out spot))
            {
                return true;
            }
            Map map = preacher.Map;
            if (fallback.IsValid && CellFinder.TryFindRandomReachableNearbyCell(fallback, map, 6f, TraverseParms.For(preacher),
                c => c.Standable(map) && !c.IsForbidden(preacher) && preacher.CanReserve(c), null, out spot))
            {
                return true;
            }
            spot = IntVec3.Invalid;
            return false;
        }

        // Starts the sermon the colony agreed to, with the congregation the player called to it: they drop what they are
        // doing and gather at the spot. A preacher of the player's faction (a lodger) joins the gathering himself; a visitor
        // stays in his own lord, which sends him to the spot. required and eligible: the minimum he asked for and the
        // colonists who could have come, for the turnout.
        public static LordJob_MJV_Sermon StartSermon(Pawn preacher, IntVec3 spot, SermonSettings settings, List<Pawn> congregation, int required, int eligible)
        {
            var job = new LordJob_MJV_Sermon(preacher, spot, settings.durationTicks.RandomInRange, settings, required, eligible);
            var starting = new List<Pawn>();
            if (preacher.Faction == Faction.OfPlayer)
            {
                starting.Add(preacher);
            }
            if (congregation != null)
            {
                foreach (Pawn pawn in congregation)
                {
                    if (pawn == null || pawn == preacher || !pawn.Spawned || pawn.Dead || pawn.Map != preacher.Map || starting.Contains(pawn))
                    {
                        continue;
                    }
                    starting.Add(pawn);
                }
            }
            foreach (Pawn pawn in starting)
            {
                // Out of a party they were at, and undrafted: the sermon is what they do now.
                pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ForcedToJoinOtherLord);
                if (pawn.drafter != null && pawn.Drafted)
                {
                    pawn.drafter.Drafted = false;
                }
            }
            LordMaker.MakeNewLord(Faction.OfPlayer, job, preacher.Map, starting);
            foreach (Pawn pawn in starting)
            {
                // A new duty does not interrupt the job at hand: they go now.
                pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            }
            Messages.Message("MJV_SermonStarted".Translate(preacher.Named("PREACHER"), SermonRequests.FaithName.Named("FAITH")),
                new TargetInfo(spot, preacher.Map), MessageTypeDefOf.NeutralEvent);
            return job;
        }
    }

    public class LordToilData_MJV_Sermon : LordToilData
    {
        public Dictionary<Pawn, int> presentForTicks = new Dictionary<Pawn, int>();
        public int preachingTicks;

        private List<Pawn> tmpPawns;
        private List<int> tmpTicks;

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                presentForTicks.RemoveAll(x => x.Key == null || x.Key.Destroyed);
            }
            Scribe_Collections.Look(ref presentForTicks, "presentForTicks", LookMode.Reference, LookMode.Value, ref tmpPawns, ref tmpTicks);
            Scribe_Values.Look(ref preachingTicks, "preachingTicks", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && presentForTicks == null)
            {
                presentForTicks = new Dictionary<Pawn, int>();
            }
        }
    }

    // The sermon proper: the preacher speaks at the spot, the congregation the player called gathers to listen. When it
    // ends every listener's certainty is shaken in proportion to how much of it they heard; those whose certainty breaks
    // adopt the imperial faith.
    public class LordToil_MJV_Sermon : LordToil
    {
        private const float JoyPerTick = 2.5E-05f;

        private readonly IntVec3 spot;
        private readonly Pawn preacher;

        public LordToil_MJV_Sermon(IntVec3 spot, Pawn preacher)
        {
            this.spot = spot;
            this.preacher = preacher;
            data = new LordToilData_MJV_Sermon();
        }

        public LordToilData_MJV_Sermon Data => (LordToilData_MJV_Sermon)data;

        public override void UpdateAllDuties()
        {
            List<Pawn> pawns = lord.ownedPawns;
            for (int i = 0; i < pawns.Count; i++)
            {
                pawns[i].mindState.duty = pawns[i] == preacher ? PreacherDuty(spot) : ListenerDuty(spot);
            }
        }

        public static PawnDuty PreacherDuty(IntVec3 spot)
        {
            return new PawnDuty(MJV_DefOf.MJV_DeliverSermon, spot);
        }

        public static PawnDuty ListenerDuty(IntVec3 spot)
        {
            // The congregation gathers in front of the preacher and to his sides.
            return new PawnDuty(MJV_DefOf.MJV_HearSermon, spot)
            {
                spectateRect = CellRect.SingleCell(spot),
                spectateRectAllowedSides = SpectateRectSide.Down | SpectateRectSide.Left | SpectateRectSide.Right,
                spectateRectPreferredSide = SpectateRectSide.Down
            };
        }

        public override void LordToilTick()
        {
            bool preaching = IsPreaching(preacher, spot);
            if (preaching)
            {
                Data.preachingTicks++;
            }
            List<Pawn> pawns = lord.ownedPawns;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == preacher || !preaching || !pawn.Spawned || !pawn.Awake() || !GatheringsUtility.InGatheringArea(pawn.Position, spot, Map))
                {
                    continue;
                }
                Data.presentForTicks.TryGetValue(pawn, out int ticks);
                Data.presentForTicks[pawn] = ticks + 1;
                pawn.needs?.joy?.GainJoy(JoyPerTick, JoyKindDefOf.Social);
            }
        }

        public static bool IsPreaching(Pawn preacher, IntVec3 spot)
        {
            return preacher != null && preacher.Spawned && preacher.CurJobDef == MJV_DefOf.MJV_PreachSermon
                && preacher.Position.InHorDistOf(spot, 2.9f) && preacher.jobs.curDriver is JobDriver_MJV_DeliverSermon driver && driver.OnStage;
        }
    }

    // A justiciar's sermon, held as a ritual: the congregation the player called (SermonRequests) is bound to it until it
    // ends, and cannot be drafted out of it while the colony is safe. If the player calls it off, or sends people away until
    // fewer than the minimum remain, it is cancelled; danger, the preacher hurt or gone, or listeners downed only interrupt
    // it. Its outcome goes to the justiciar's mission (IJusticiarMission.Notify_SermonEnded).
    public class LordJob_MJV_Sermon : LordJob
    {
        private Pawn preacher;
        private IntVec3 spot;
        private int durationTicks;
        private SermonSettings settings;
        private int required;
        private int eligible;
        private bool concluded;
        private bool cancelRequested;
        private bool sentAwayByPlayer;
        private LordToil_MJV_Sermon sermonToil;

        // A preacher that has not got going by then (could not reach the spot...) gives up.
        private const int StartGraceTicks = 2500;

        public LordJob_MJV_Sermon()
        {
        }

        public LordJob_MJV_Sermon(Pawn preacher, IntVec3 spot, int durationTicks, SermonSettings settings, int required, int eligible)
        {
            this.preacher = preacher;
            this.spot = spot;
            this.durationTicks = durationTicks;
            this.settings = settings?.Copy() ?? new SermonSettings();
            this.required = required;
            this.eligible = eligible;
        }

        public Pawn Preacher => preacher;

        public IntVec3 Spot => spot;

        public int Required => required;

        // The sermon lasts durationTicks of actual preaching, from when the preacher reaches the spot.
        public int TicksLeft => durationTicks - (sermonToil?.Data.preachingTicks ?? 0);

        public override bool AllowStartNewGatherings => false;

        public override bool AllowStartNewRituals => false;

        private bool PreacherIsMember => preacher != null && lord != null && preacher.Faction == lord.faction;

        // The congregation still bound to the sermon.
        public int Listening
        {
            get
            {
                int count = 0;
                List<Pawn> pawns = lord.ownedPawns;
                for (int i = 0; i < pawns.Count; i++)
                {
                    if (pawns[i] != preacher)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            var sermon = new LordToil_MJV_Sermon(spot, preacher);
            sermonToil = sermon;
            graph.AddToil(sermon);
            var end = new LordToil_End();
            graph.AddToil(end);

            var finished = new Transition(sermon, end);
            finished.AddTrigger(new Trigger_TickCondition(() => sermon.Data.preachingTicks >= durationTicks, 30));
            finished.AddPreAction(new TransitionAction_Custom(() => Conclude(sermon, SermonOutcome.Held)));
            graph.AddTransition(finished);

            var cancelled = new Transition(sermon, end);
            cancelled.AddTrigger(new Trigger_TickCondition(() => cancelRequested || Listening < required, 30));
            cancelled.AddPreAction(new TransitionAction_Custom(() =>
                Conclude(sermon, cancelRequested || sentAwayByPlayer ? SermonOutcome.Cancelled : SermonOutcome.Interrupted)));
            graph.AddTransition(cancelled);

            var calledOff = new Transition(sermon, end);
            calledOff.AddTrigger(new Trigger_TickCondition(ShouldBeCalledOff, 30));
            calledOff.AddTrigger(new Trigger_TickCondition(() => sermon.Data.preachingTicks == 0 && lord.ticksInToil > StartGraceTicks, 60));
            calledOff.AddTrigger(new Trigger_PawnKilled());
            calledOff.AddPreAction(new TransitionAction_Custom(() => Conclude(sermon, SermonOutcome.Interrupted)));
            graph.AddTransition(calledOff);
            return graph;
        }

        private bool ShouldBeCalledOff()
        {
            if (preacher == null || preacher.Dead || !preacher.Spawned || preacher.Map != Map || !GatheringsUtility.PawnCanStartOrContinueGathering(preacher))
            {
                return true;
            }
            if (preacher.Faction == null || preacher.Faction.HostileTo(Faction.OfPlayer) || !GatheringsUtility.AcceptableGameConditionsToContinueGathering(Map)
                || Map.dangerWatcher.DangerRating == StoryDanger.High)
            {
                return true;
            }
            if (PreacherIsMember)
            {
                return preacher.GetLord() != lord;
            }
            // A visitor preaches for his own lord; it leaves the preaching toil when the visit ends or goes wrong.
            return !(preacher.GetLord()?.CurLordToil is LordToil_MJV_Preach);
        }

        // The congregation stays: no drafting out of it while the colony is safe. In danger the player may draft them, and
        // the time they spend away is time they did not hear.
        public override AcceptanceReport AllowsDrafting(Pawn pawn)
        {
            if (concluded || Map == null || Map.dangerWatcher.DangerRating != StoryDanger.None)
            {
                return true;
            }
            return new AcceptanceReport("MJV_SermonNoDrafting".Translate(pawn.Named("PAWN"), preacher.Named("PREACHER")));
        }

        public override IEnumerable<Gizmo> GetPawnGizmos(Pawn p)
        {
            if (concluded || p.Faction != Faction.OfPlayer)
            {
                yield break;
            }
            if (p != preacher)
            {
                yield return new Command_Action
                {
                    defaultLabel = "MJV_CommandLeaveSermon".Translate(),
                    defaultDesc = "MJV_CommandLeaveSermonDesc".Translate(p.Named("PAWN"), required.Named("REQUIRED"), Listening.Named("LISTENING")),
                    icon = GizmoTextures.SermonHold,
                    action = () =>
                    {
                        sentAwayByPlayer = true;
                        lord.Notify_PawnLost(p, PawnLostCondition.ForcedByPlayerAction);
                    }
                };
            }
            yield return new Command_Action
            {
                defaultLabel = "MJV_CommandCancelSermon".Translate(),
                defaultDesc = "MJV_CommandCancelSermonDesc".Translate(preacher.Named("PREACHER"), (-settings.refusalGoodwill).Named("PENALTY")),
                icon = GizmoTextures.SermonRefuse,
                action = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "MJV_ConfirmCancelSermon".Translate(preacher.Named("PREACHER"), (-settings.refusalGoodwill).Named("PENALTY")),
                    () => cancelRequested = true, true))
            };
        }

        public override string GetReport(Pawn pawn)
        {
            return pawn == preacher ? "MJV_LordReportPreaching".Translate() : "MJV_LordReportListening".Translate(preacher.Named("PREACHER"));
        }

        private void Conclude(LordToil_MJV_Sermon toil, SermonOutcome outcome)
        {
            if (concluded)
            {
                return;
            }
            concluded = true;
            Ideo faith = preacher?.Ideo;
            // A preacher who cannot talk (social disabled, which disables his conversion power) converts nobody.
            bool canConvert = faith != null && preacher != null && ImperialFaith.ConversionPossible
                && !StatDefOf.ConversionPower.Worker.IsDisabledFor(preacher);
            var converts = new List<Pawn>();
            var listeners = new List<Pawn>();
            // The congregation the colony gave him: those called who stayed to the end (one sent away does not count) and
            // heard some of it, even if they came late.
            int attendees = 0;
            foreach (KeyValuePair<Pawn, int> entry in toil.Data.presentForTicks)
            {
                Pawn pawn = entry.Key;
                float heard = JVMath.Clamp01(entry.Value / (float)durationTicks);
                if (pawn == null || pawn.Dead || pawn == preacher || heard < 0.15f)
                {
                    continue;
                }
                listeners.Add(pawn);
                if (lord.ownedPawns.Contains(pawn))
                {
                    attendees++;
                }
                bool converted = false;
                if (canConvert && pawn.ideo != null && pawn.Ideo != faith)
                {
                    float reduction = InteractionWorker_ConvertIdeoAttempt.CertaintyReduction(preacher, pawn) * settings.certaintyFactor * heard;
                    converted = pawn.ideo.IdeoConversionAttempt(reduction, faith);
                }
                if (converted)
                {
                    converts.Add(pawn);
                    if (pawn.Spawned)
                    {
                        ShowConversion(pawn, faith);
                    }
                }
                GiveSermonThought(pawn, heard, faith);
            }
            ImperialFaith.RewardConversions(converts.Count, settings.goodwillPerConvert, converts.Count > 0 ? converts[0] : null);
            Report(listeners, converts, outcome);
            var result = new SermonResult
            {
                outcome = outcome,
                turnout = outcome == SermonOutcome.Held ? SermonRequests.Turnout(attendees, required, eligible) : 0f,
                converts = converts,
                listeners = listeners.Count
            };
            JusticiarMissions.For(preacher)?.Notify_SermonEnded(result);
            Lord preacherLord = preacher?.GetLord();
            if (preacherLord != null && preacherLord != lord)
            {
                preacherLord.ReceiveMemo(SermonUtility.MemoSermonEnded);
            }
        }

        // The game's own feedback for a conversion, as when a colonist talks someone into their ideoligion: the certainty
        // drop IdeoConversionAttempt already floated over the convert, and the interaction bubble of a conversion attempt
        // (InteractionDefOf.ConvertIdeoAttempt) with the icon of the faith they now hold. The bubble rises over each
        // convert, so several converts each show theirs.
        private void ShowConversion(Pawn convert, Ideo faith)
        {
            InteractionDef def = InteractionDefOf.ConvertIdeoAttempt;
            Faction faction = preacher?.Faction;
            Pawn towards = preacher != null && preacher.Spawned && preacher.Map == convert.Map ? preacher : null;
            MoteMaker.MakeInteractionBubble(convert, towards, def.interactionMote, def.GetSymbol(faction, faith), def.GetSymbolColor(faction));
        }

        private static void GiveSermonThought(Pawn pawn, float heard, Ideo faith)
        {
            if (pawn.needs?.mood == null)
            {
                return;
            }
            var thought = (Thought_Memory)ThoughtMaker.MakeThought(MJV_DefOf.MJV_HeardImperialSermon);
            // Stage 0: a sermon of one's own faith, stage 1: a foreign one.
            thought.SetForcedStage(faith != null && pawn.Ideo == faith ? 0 : 1);
            thought.moodPowerFactor = JVMath.Clamp(heard + 0.1f, 0.3f, 1f);
            pawn.needs.mood.thoughts.memories.TryGainMemory(thought);
        }

        private void Report(List<Pawn> listeners, List<Pawn> converts, SermonOutcome outcome)
        {
            if (preacher == null)
            {
                return;
            }
            if (converts.Count > 0)
            {
                var names = new StringBuilder();
                foreach (Pawn convert in converts)
                {
                    names.AppendLine("  - " + convert.LabelShort);
                }
                TaggedString text = "MJV_SermonConvertsText".Translate(preacher.Named("PREACHER"), (preacher.Ideo?.name ?? string.Empty).Named("FAITH"),
                    names.ToString().TrimEndNewlines().Named("CONVERTS"), (converts.Count * settings.goodwillPerConvert).Named("GOODWILL"));
                Find.LetterStack.ReceiveLetter("MJV_SermonConvertsLabel".Translate(), text, LetterDefOf.NeutralEvent, new LookTargets(converts));
            }
            if (outcome == SermonOutcome.Cancelled)
            {
                Messages.Message("MJV_SermonCancelled".Translate(preacher.Named("PREACHER")), new TargetInfo(spot, Map), MessageTypeDefOf.NegativeEvent);
            }
            else if (outcome == SermonOutcome.Interrupted)
            {
                Messages.Message("MJV_SermonInterrupted".Translate(preacher.Named("PREACHER")), new TargetInfo(spot, Map), MessageTypeDefOf.NeutralEvent);
            }
            else if (converts.Count == 0)
            {
                Messages.Message("MJV_SermonFinished".Translate(preacher.Named("PREACHER"), listeners.Count.Named("COUNT")), new TargetInfo(spot, Map), MessageTypeDefOf.NeutralEvent);
            }
        }

        public override void Cleanup()
        {
            base.Cleanup();
            // Release the preacher (a lodger) and the listeners from their sermon jobs right away.
            if (preacher != null && preacher.Spawned && preacher.CurJobDef == MJV_DefOf.MJV_PreachSermon && PreacherIsMember)
            {
                preacher.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        public override void ExposeData()
        {
            Scribe_References.Look(ref preacher, "preacher");
            Scribe_Values.Look(ref spot, "spot");
            Scribe_Values.Look(ref durationTicks, "durationTicks", 0);
            Scribe_Deep.Look(ref settings, "settings");
            Scribe_Values.Look(ref required, "required", 0);
            Scribe_Values.Look(ref eligible, "eligible", 0);
            Scribe_Values.Look(ref concluded, "concluded", false);
            Scribe_Values.Look(ref cancelRequested, "cancelRequested", false);
            Scribe_Values.Look(ref sentAwayByPlayer, "sentAwayByPlayer", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && settings == null)
            {
                settings = new SermonSettings();
            }
        }
    }
}
