using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI.Group;
using Color = UnityEngine.Color;
using Texture2D = UnityEngine.Texture2D;

namespace JusticiarValor
{
    // A justiciar's sermon is a ritual the colony must hold: he asks for it, and the player either assembles at least the
    // share of the colony's conscious colonists he asks for (more pleases him) or refuses (he takes offense).
    public static class SermonRequests
    {
        // Requests are made in the morning, so the answer window ends by evening.
        private const int LastRequestHour = 14;

        // Whether a colonist can be called to a sermon: a free colonist of the colony, awake or able to be woken, of sound
        // mind, able to hear, and not already part of a ritual, a caravan or the like (a party they would leave is fine).
        public static bool CanAttend(Pawn pawn)
        {
            if (!ImperialFaith.IsCongregant(pawn) || !pawn.Spawned || !JVUtility.IsConscious(pawn) || pawn.InMentalState
                || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Hearing))
            {
                return false;
            }
            Lord lord = pawn.GetLord();
            return lord == null || lord.LordJob is LordJob_VoluntarilyJoinable;
        }

        public static List<Pawn> Eligible(Map map)
        {
            var result = new List<Pawn>();
            foreach (Pawn pawn in ImperialFaith.Congregation(map))
            {
                if (CanAttend(pawn))
                {
                    result.Add(pawn);
                }
            }
            return result;
        }

        // The congregation he asks for: the share of the eligible colonists, one at least.
        public static int Required(int eligible, float fraction)
        {
            if (eligible <= 0)
            {
                return 0;
            }
            return System.Math.Min(eligible, System.Math.Max(1, (int)System.Math.Ceiling(eligible * fraction - 0.0001f)));
        }

        // Share of the colony beyond the minimum that heard the sermon, 0..1.
        public static float Turnout(int attendees, int required, int eligible)
        {
            if (eligible <= required)
            {
                return 0f;
            }
            return JVMath.Clamp01((attendees - required) / (float)(eligible - required));
        }

        // Whether it is time to ask: in the morning, and when a sermon could begin now.
        public static bool CanRequestNow(Pawn justiciar, Map map)
        {
            return GenLocalDate.HourInteger(map) <= LastRequestHour && SermonUtility.CanPreachNow(justiciar, map);
        }

        // Asks the colony for a sermon at a gathering spot: a letter, an alert and two commands on the justiciar. answerTicks:
        // how long he waits for the answer, when less than settings.answerTicks (his visit ends sooner).
        public static SermonRequest TryMakeRequest(Pawn justiciar, IntVec3 fallbackSpot, SermonSettings settings, Quest quest, int answerTicks = -1)
        {
            Map map = justiciar?.Map;
            if (map == null || !SermonUtility.TryFindSermonSpot(justiciar, fallbackSpot, out IntVec3 spot))
            {
                return null;
            }
            int waits = answerTicks > 0 ? System.Math.Min(answerTicks, settings.answerTicks) : settings.answerTicks;
            var request = new SermonRequest { spot = spot, expiresTick = Find.TickManager.TicksGame + waits };
            int eligible = Eligible(map).Count;
            int required = Required(eligible, settings.requiredFraction);
            Find.LetterStack.ReceiveLetter("MJV_SermonRequestedLabel".Translate(justiciar.Named("JUSTICIAR")),
                "MJV_SermonRequestedText".Translate(justiciar.Named("JUSTICIAR"), FaithName.Named("FAITH"), required.Named("REQUIRED"),
                    eligible.Named("ELIGIBLE"), waits.ToStringTicksToPeriod().Named("TIME"),
                    (-settings.refusalGoodwill).Named("PENALTY"), settings.fullTurnoutGoodwill.ToString("0.#").Named("BONUS")),
                LetterDefOf.NeutralEvent, new LookTargets(justiciar), null, quest);
            return request;
        }

        public static string FaithName => ImperialFaith.Ideo?.name ?? (string)"MJV_ImperialFaithFallback".Translate();

        // Why the sermon cannot be held right now (the commands are disabled), or null.
        public static string CannotHoldReason(IJusticiarMission mission)
        {
            Pawn justiciar = mission.Justiciar;
            Map map = justiciar?.Map;
            if (map == null || !justiciar.Spawned || justiciar.Downed || justiciar.InMentalState || !justiciar.Awake())
            {
                return "MJV_SermonJusticiarUnavailable".Translate(justiciar.Named("JUSTICIAR"));
            }
            if (map.dangerWatcher.DangerRating != StoryDanger.None)
            {
                return "MJV_SermonDanger".Translate();
            }
            if (GatheringsUtility.AnyLordJobPreventsNewGatherings(map))
            {
                return "MJV_SermonOtherGathering".Translate();
            }
            if (Eligible(map).Count == 0)
            {
                return "MJV_SermonNobodyToListen".Translate();
            }
            return null;
        }

        // A request whose time is up: silence is a refusal, unless the sermon could not have been held at the end anyway
        // (danger, the justiciar unavailable), when it lapses.
        public static bool ExpiryIsRefusal(IJusticiarMission mission) => CannotHoldReason(mission) == null;

        public static IEnumerable<Gizmo> Gizmos(IJusticiarMission mission)
        {
            SermonRequest request = mission.Request;
            Pawn justiciar = mission.Justiciar;
            if (request == null || justiciar == null || !justiciar.Spawned)
            {
                yield break;
            }
            var hold = new Command_Action
            {
                defaultLabel = "MJV_CommandHoldSermon".Translate(),
                defaultDesc = "MJV_CommandHoldSermonDesc".Translate(justiciar.Named("JUSTICIAR"), request.TicksLeft.ToStringTicksToPeriod().Named("TIME")),
                icon = GizmoTextures.SermonHold,
                action = () => OpenDialog(mission)
            };
            string reason = CannotHoldReason(mission);
            if (reason != null)
            {
                hold.Disable(reason);
            }
            yield return hold;
            yield return new Command_Action
            {
                defaultLabel = "MJV_CommandRefuseSermon".Translate(),
                defaultDesc = "MJV_CommandRefuseSermonDesc".Translate(justiciar.Named("JUSTICIAR"), (-mission.Settings.refusalGoodwill).Named("PENALTY")),
                icon = GizmoTextures.SermonRefuse,
                action = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "MJV_ConfirmRefuseSermon".Translate(justiciar.Named("JUSTICIAR"), (-mission.Settings.refusalGoodwill).Named("PENALTY")),
                    () => mission.Answer(null, 0, 0), true))
            };
        }

        public static void OpenDialog(IJusticiarMission mission)
        {
            Pawn justiciar = mission.Justiciar;
            Map map = justiciar?.Map;
            SermonRequest request = mission.Request;
            if (map == null || request == null)
            {
                return;
            }
            int eligible = Eligible(map).Count;
            int required = Required(eligible, mission.Settings.requiredFraction);
            Find.WindowStack.Add(new Dialog_MJV_BeginSermon(mission, map, request.spot, required, eligible));
        }
    }

    // The sermon as the game's ritual window (as for the bestowing ceremony: no ideoligion ritual behind it): pick who
    // attends, at least the minimum the justiciar asked for. Beyond the minimum, the attendance shows as his satisfaction.
    public class Dialog_MJV_BeginSermon : Dialog_BeginRitual
    {
        private readonly IJusticiarMission mission;
        private readonly Pawn justiciar;
        private readonly int required;
        private readonly int eligible;
        private readonly List<QualityFactor> factors = new List<QualityFactor>();

        public Dialog_MJV_BeginSermon(IJusticiarMission mission, Map map, IntVec3 spot, int required, int eligible)
            : base("MJV_SermonRitualLabel".Translate(SermonRequests.FaithName), null, new TargetInfo(spot, map), map,
                assignments =>
                {
                    List<Pawn> congregation = assignments.Participants.Where(p => p != mission.Justiciar).ToList();
                    mission.Answer(congregation, required, eligible);
                    return true;
                },
                mission.Justiciar, null, (pawn, voluntary, allowOtherIdeos) => SermonRequests.CanAttend(pawn), "MJV_SermonBegin".Translate(),
                new List<Pawn> { mission.Justiciar }, null, PrepareOutcome(),
                new List<string> { "MJV_SermonDialogRules".Translate(mission.Justiciar.Named("JUSTICIAR"), (-mission.Settings.refusalGoodwill).Named("PENALTY"),
                    mission.Settings.fullTurnoutGoodwill.ToString("0.#").Named("BONUS")) })
        {
            this.mission = mission;
            justiciar = mission.Justiciar;
            this.required = required;
            this.eligible = eligible;
        }

        // The outcome the window shows its quality factors for; it has none of its own (PopulateQualityFactors adds the
        // attendance), and the window needs the list.
        private static RitualOutcomeEffectDef PrepareOutcome()
        {
            RitualOutcomeEffectDef def = MJV_DefOf.MJV_ImperialSermon;
            if (def.comps == null)
            {
                def.comps = new List<RitualOutcomeComp>();
            }
            return def;
        }

        private int Attending => assignments.Participants.Count(p => p != justiciar);

        public override TaggedString DescriptionLabel => "MJV_SermonDialogDesc".Translate(justiciar.Named("JUSTICIAR"), SermonRequests.FaithName.Named("FAITH"),
            required.Named("REQUIRED"), eligible.Named("ELIGIBLE"));

        public override TaggedString QualityFactorsLabel => "MJV_SermonAttendanceLabel".Translate();

        public override TaggedString ExpectedQualityLabel => "MJV_SermonSatisfactionLabel".Translate(justiciar.Named("JUSTICIAR"));

        public override Color QualitySummaryColor(FloatRange qualityRange) => Attending >= required ? JVMath.White : ColorLibrary.RedReadable;

        protected override IEnumerable<string> BlockingIssues()
        {
            IEnumerable<string> issues = base.BlockingIssues();
            if (issues != null)
            {
                foreach (string issue in issues)
                {
                    yield return issue;
                }
            }
            int attending = Attending;
            if (attending < required)
            {
                yield return "MJV_SermonNeedsCongregation".Translate(required, eligible, attending);
            }
        }

        // The attendance against the minimum, and the satisfaction the turnout beyond it brings: the share of the colony
        // beyond the minimum, which is also the share of the full goodwill bonus the sermon earns.
        protected override List<QualityFactor> PopulateQualityFactors(out FloatRange qualityRange)
        {
            int attending = Attending;
            float turnout = SermonRequests.Turnout(attending, required, eligible);
            factors.Clear();
            factors.Add(new QualityFactor
            {
                label = "MJV_SermonCongregationFactor".Translate(),
                count = attending + " / " + eligible,
                qualityChange = attending < required ? (string)"MJV_SermonBelowMinimum".Translate(required)
                    : (string)"MJV_SermonTurnoutBonus".Translate((mission.Settings.fullTurnoutGoodwill * turnout).ToString("0.#")),
                quality = turnout,
                positive = attending >= required,
                priority = 4f,
                toolTip = "MJV_SermonCongregationTip".Translate(required, eligible)
            });
            qualityRange = new FloatRange(turnout, turnout);
            return factors;
        }
    }

    // The colony has been asked for a sermon and has not answered yet.
    public class Alert_MJV_SermonRequested : Alert
    {
        private readonly List<Pawn> justiciars = new List<Pawn>();

        public Alert_MJV_SermonRequested()
        {
            defaultPriority = AlertPriority.High;
        }

        private List<Pawn> Justiciars
        {
            get
            {
                justiciars.Clear();
                foreach (IJusticiarMission mission in JusticiarMissions.AwaitingAnswer())
                {
                    if (mission.Justiciar != null && mission.Justiciar.Spawned)
                    {
                        justiciars.Add(mission.Justiciar);
                    }
                }
                return justiciars;
            }
        }

        public override string GetLabel() => "MJV_AlertSermonRequested".Translate();

        public override TaggedString GetExplanation()
        {
            var text = new StringBuilder();
            foreach (IJusticiarMission mission in JusticiarMissions.AwaitingAnswer())
            {
                if (mission.Justiciar != null && mission.Request != null)
                {
                    text.AppendLine("  - " + mission.Justiciar.LabelShort + ": " + mission.Request.TicksLeft.ToStringTicksToPeriod());
                }
            }
            return "MJV_AlertSermonRequestedDesc".Translate(text.ToString().TrimEndNewlines());
        }

        public override AlertReport GetReport()
        {
            List<Pawn> pawns = Justiciars;
            return pawns.Count == 0 ? AlertReport.Inactive : AlertReport.CulpritsAre(pawns);
        }
    }
}
