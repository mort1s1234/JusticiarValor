using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace JusticiarValor
{
    // A visiting justiciar waits near the colony for someone to talk to him. The float menu on him sends a colonist over
    // (JobDriver_MJV_TalkToJusticiar), and the conversation lets the player host him or send him away.
    public class LordToil_MJV_AwaitAudience : LordToil_DefendPoint
    {
        private readonly Pawn justiciar;

        public LordToil_MJV_AwaitAudience(IntVec3 point, Pawn justiciar) : base(point)
        {
            this.justiciar = justiciar;
        }

        public override IEnumerable<FloatMenuOption> ExtraFloatMenuOptions(Pawn target, Pawn forPawn)
        {
            if (target != justiciar || forPawn == null || !forPawn.IsFreeNonSlaveColonist
                || !(lord.LordJob is LordJob_MJV_MissionaryVisit visit) || !visit.AwaitingAudience)
            {
                yield break;
            }
            string label = "MJV_TalkToJusticiar".Translate(justiciar.Named("JUSTICIAR"));
            if (forPawn.WorkTagIsDisabled(WorkTags.Social) || !forPawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
            {
                yield return new FloatMenuOption(label + ": " + "MJV_TalkIncapable".Translate(forPawn.Named("PAWN")), null);
                yield break;
            }
            if (!forPawn.CanReach(justiciar, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption(label + ": " + "CannotReach".Translate().CapitalizeFirst(), null);
                yield break;
            }
            yield return new FloatMenuOption(label, () =>
                forPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(MJV_DefOf.MJV_TalkToJusticiar, justiciar), JobTag.Misc));
        }
    }

    // A colonist walks up to the waiting justiciar; the conversation opens when they meet.
    public class JobDriver_MJV_TalkToJusticiar : JobDriver
    {
        private Pawn Justiciar => job.targetA.Pawn;

        private LordJob_MJV_MissionaryVisit Visit => Justiciar?.GetLord()?.LordJob as LordJob_MJV_MissionaryVisit;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Visit?.AwaitingAudience != true);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil talk = ToilMaker.MakeToil("MJV_TalkToJusticiar");
            talk.initAction = () =>
            {
                pawn.rotationTracker.FaceTarget(Justiciar);
                JusticiarAudience.Open(Visit, pawn);
            };
            talk.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return talk;
        }
    }

    public static class JusticiarAudience
    {
        // The justiciar tells the colonist what hosting him brings and costs; the player hosts him, sends him away, or
        // takes time to think while he keeps waiting.
        public static void Open(LordJob_MJV_MissionaryVisit visit, Pawn speaker)
        {
            Pawn justiciar = visit?.Preacher;
            if (justiciar == null || !visit.AwaitingAudience)
            {
                return;
            }
            SermonSettings settings = visit.Settings;
            Faction empire = justiciar.Faction;
            TaggedString text = "MJV_AudienceText".Translate(justiciar.Named("PREACHER"), speaker.Named("SPEAKER"), empire.Named("FACTION"),
                SermonRequests.FaithName.Named("FAITH"), visit.VisitTicks.ToStringTicksToPeriod().Named("DURATION"),
                settings.requiredFraction.ToStringPercent().Named("FRACTION"), settings.goodwillPerConvert.Named("GOODWILL"),
                settings.fullTurnoutGoodwill.ToString("0.#").Named("BONUS"), settings.maxTurnoutGoodwill.Named("MAXBONUS"),
                (-settings.refusalGoodwill).Named("PENALTY"), (-visit.TurnedAwayGoodwill).Named("DECLINE"));
            var node = new DiaNode(text);
            node.options.Add(new DiaOption("MJV_AudienceHost".Translate(justiciar.Named("PREACHER")))
            {
                action = visit.Host,
                resolveTree = true
            });
            node.options.Add(new DiaOption("MJV_AudienceDecline".Translate(justiciar.Named("PREACHER"), (-visit.TurnedAwayGoodwill).Named("DECLINE")))
            {
                action = () => visit.TurnAway(false),
                resolveTree = true
            });
            node.options.Add(new DiaOption("MJV_AudienceLater".Translate(visit.AudienceTicksLeft.ToStringTicksToPeriod().Named("TIME")))
            {
                resolveTree = true
            });
            Find.WindowStack.Add(new Dialog_NodeTreeWithFactionInfo(node, empire, true, false, "MJV_AudienceTitle".Translate(justiciar.Named("PREACHER"))));
        }

        // Every visiting justiciar still waiting for someone to talk to him.
        public static IEnumerable<LordJob_MJV_MissionaryVisit> Waiting()
        {
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                List<Lord> lords = maps[m].lordManager.lords;
                for (int i = 0; i < lords.Count; i++)
                {
                    if (lords[i].LordJob is LordJob_MJV_MissionaryVisit visit && visit.AwaitingAudience && visit.Preacher != null && visit.Preacher.Spawned)
                    {
                        yield return visit;
                    }
                }
            }
        }
    }

    // A visiting justiciar is waiting for someone to talk to him.
    public class Alert_MJV_JusticiarAwaitsAudience : Alert
    {
        private readonly List<Pawn> justiciars = new List<Pawn>();

        public Alert_MJV_JusticiarAwaitsAudience()
        {
            defaultPriority = AlertPriority.High;
        }

        public override string GetLabel() => "MJV_AlertJusticiarAwaitsAudience".Translate();

        public override TaggedString GetExplanation()
        {
            var text = new StringBuilder();
            foreach (LordJob_MJV_MissionaryVisit visit in JusticiarAudience.Waiting())
            {
                text.AppendLine("  - " + visit.Preacher.LabelShort + ": " + visit.AudienceTicksLeft.ToStringTicksToPeriod());
            }
            return "MJV_AlertJusticiarAwaitsAudienceDesc".Translate(text.ToString().TrimEndNewlines());
        }

        public override AlertReport GetReport()
        {
            justiciars.Clear();
            foreach (LordJob_MJV_MissionaryVisit visit in JusticiarAudience.Waiting())
            {
                justiciars.Add(visit.Preacher);
            }
            return justiciars.Count == 0 ? AlertReport.Inactive : AlertReport.CulpritsAre(justiciars);
        }
    }
}
