using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace JusticiarValor
{
    // The sermon a justiciar on a mission has asked the colony for: where, and until when he waits for an answer.
    public class SermonRequest : IExposable
    {
        public IntVec3 spot = IntVec3.Invalid;
        public int expiresTick;

        public int TicksLeft => expiresTick - Find.TickManager.TicksGame;

        public void ExposeData()
        {
            Scribe_Values.Look(ref spot, "spot", IntVec3.Invalid);
            Scribe_Values.Look(ref expiresTick, "expiresTick", 0);
        }
    }

    public enum SermonOutcome
    {
        // Preached to the end.
        Held,
        // Called off by the player, or the congregation fell below the minimum because the player sent people away.
        Cancelled,
        // Cut short through nobody's fault: danger, the preacher hurt or gone, listeners downed.
        Interrupted
    }

    public class SermonResult
    {
        public SermonOutcome outcome;
        // Share of the colony that heard it beyond the minimum the justiciar asked for, 0..1.
        public float turnout;
        public List<Pawn> converts = new List<Pawn>();
        public int listeners;
    }

    // A justiciar's mission to the colony: his visit (LordJob_MJV_MissionaryVisit) or his stay as a lodger
    // (QuestPart_MJV_LodgerSermons). Both ask for sermons, keep a MissionRecord and settle it when he returns to the Empire.
    public interface IJusticiarMission
    {
        Pawn Justiciar { get; }

        SermonSettings Settings { get; }

        MissionRecord Record { get; }

        // The sermon he is waiting for an answer to, or null.
        SermonRequest Request { get; }

        // The player's answer to the request: the colonists who will listen (the sermon begins), or null for a refusal.
        void Answer(List<Pawn> congregation, int required, int eligible);

        void Notify_SermonEnded(SermonResult result);
    }

    public static class JusticiarMissions
    {
        // The mission the justiciar is on, if any.
        public static IJusticiarMission For(Pawn justiciar)
        {
            if (justiciar == null)
            {
                return null;
            }
            Map map = justiciar.MapHeld;
            if (map != null)
            {
                List<Lord> lords = map.lordManager.lords;
                for (int i = 0; i < lords.Count; i++)
                {
                    if (lords[i].LordJob is LordJob_MJV_MissionaryVisit visit && visit.Preacher == justiciar)
                    {
                        return visit;
                    }
                }
            }
            List<Quest> quests = Find.QuestManager.QuestsListForReading;
            for (int i = 0; i < quests.Count; i++)
            {
                if (quests[i].State != QuestState.Ongoing)
                {
                    continue;
                }
                List<QuestPart> parts = quests[i].PartsListForReading;
                for (int j = 0; j < parts.Count; j++)
                {
                    if (parts[j] is QuestPart_MJV_LodgerSermons lodger && lodger.preacher == justiciar && lodger.State == QuestPartState.Enabled)
                    {
                        return lodger;
                    }
                }
            }
            return null;
        }

        // Every mission waiting for the player's answer to a sermon.
        public static IEnumerable<IJusticiarMission> AwaitingAnswer()
        {
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                List<Lord> lords = maps[m].lordManager.lords;
                for (int i = 0; i < lords.Count; i++)
                {
                    if (lords[i].LordJob is LordJob_MJV_MissionaryVisit visit && visit.Request != null)
                    {
                        yield return visit;
                    }
                }
            }
            List<Quest> quests = Find.QuestManager.QuestsListForReading;
            for (int i = 0; i < quests.Count; i++)
            {
                if (quests[i].State != QuestState.Ongoing)
                {
                    continue;
                }
                List<QuestPart> parts = quests[i].PartsListForReading;
                for (int j = 0; j < parts.Count; j++)
                {
                    if (parts[j] is QuestPart_MJV_LodgerSermons lodger && lodger.State == QuestPartState.Enabled && lodger.Request != null)
                    {
                        yield return lodger;
                    }
                }
            }
        }
    }

    // What a justiciar's stay earns or costs the colony with the Empire, settled once, when he returns to it: goodwill for
    // the congregations beyond the minimum he asked for (capped), a penalty for every sermon refused or cancelled, and an
    // imperial codex-auspex if he was treated well (no sermon refused, one held at least, his mood kept up).
    public class MissionRecord : IExposable
    {
        public int sermonsHeld;
        public int refusals;
        public float turnoutGoodwill;
        private float moodSum;
        private int moodSamples;
        private bool settled;

        public bool Settled => settled;

        // His mean mood over the stay, sampled hourly; -1 before the first sample.
        public float AverageMood => moodSamples == 0 ? -1f : moodSum / moodSamples;

        public void SampleMood(Pawn justiciar)
        {
            if (justiciar?.needs?.mood != null && justiciar.Spawned && !justiciar.Dead)
            {
                moodSum += justiciar.needs.mood.CurLevelPercentage;
                moodSamples++;
            }
        }

        public bool EarnedCodex(SermonSettings settings)
        {
            return refusals == 0 && sermonsHeld >= 1 && (moodSamples == 0 || AverageMood >= settings.codexMinMood);
        }

        public int TurnoutGoodwill(SermonSettings settings)
        {
            return (int)System.Math.Round(System.Math.Min(turnoutGoodwill, settings.maxTurnoutGoodwill));
        }

        // A sermon the colony held: the turnout beyond the minimum pleases him and counts towards the goodwill he brings back.
        public void RecordSermon(Pawn justiciar, SermonSettings settings, float turnout)
        {
            sermonsHeld++;
            turnoutGoodwill += settings.fullTurnoutGoodwill * JVMath.Clamp01(turnout);
            if (turnout > 0f && justiciar?.needs?.mood != null && MJV_DefOf.MJV_SermonWellAttended != null)
            {
                var thought = (Thought_Memory)ThoughtMaker.MakeThought(MJV_DefOf.MJV_SermonWellAttended);
                thought.moodPowerFactor = JVMath.Clamp(turnout, 0.2f, 1f);
                justiciar.needs.mood.thoughts.memories.TryGainMemory(thought);
            }
        }

        // A sermon refused, cancelled or left unanswered.
        public void RecordRefusal(Pawn justiciar)
        {
            refusals++;
            if (justiciar?.needs?.mood != null && MJV_DefOf.MJV_SermonRefused != null)
            {
                justiciar.needs.mood.thoughts.memories.TryGainMemory(MJV_DefOf.MJV_SermonRefused);
            }
        }

        // He is back with the Empire: the goodwill his sermons earned and cost, and his parting gift, delivered to the map by
        // drop pod.
        public void Settle(Pawn justiciar, SermonSettings settings, Map map)
        {
            if (settled)
            {
                return;
            }
            settled = true;
            Faction empire = ImperialFaith.Empire;
            if (empire == null || empire.defeated || empire.HostileTo(Faction.OfPlayer) || justiciar == null)
            {
                return;
            }
            int bonus = TurnoutGoodwill(settings);
            int penalty = refusals * settings.refusalGoodwill;
            bool codex = EarnedCodex(settings) && map != null && MJV_DefOf.MJV_ImperialCodexAuspex != null;
            if (sermonsHeld == 0 && refusals == 0)
            {
                return;
            }
            if (bonus != 0)
            {
                empire.TryAffectGoodwillWith(Faction.OfPlayer, bonus, true, false, MJV_DefOf.MJV_SermonsWellAttended);
            }
            if (penalty != 0)
            {
                empire.TryAffectGoodwillWith(Faction.OfPlayer, penalty, true, true, MJV_DefOf.MJV_SermonsRefused);
            }
            LookTargets look = null;
            if (codex)
            {
                Thing gift = ThingMaker.MakeThing(MJV_DefOf.MJV_ImperialCodexAuspex);
                IntVec3 drop = DropCellFinder.TradeDropSpot(map);
                DropPodUtility.DropThingsNear(drop, map, new List<Thing> { gift });
                look = new LookTargets(new TargetInfo(drop, map));
            }
            TaggedString text = "MJV_MissionSettledText".Translate(justiciar.Named("JUSTICIAR"), empire.Named("FACTION"), sermonsHeld.Named("HELD"),
                bonus.ToStringWithSign().Named("BONUS"), refusals.Named("REFUSED"), penalty.ToStringWithSign().Named("PENALTY"));
            if (codex)
            {
                text += "\n\n" + "MJV_MissionSettledCodex".Translate(justiciar.Named("JUSTICIAR"));
            }
            else if (refusals == 0 && sermonsHeld > 0)
            {
                text += "\n\n" + "MJV_MissionSettledNoCodex".Translate(justiciar.Named("JUSTICIAR"), settings.codexMinMood.ToStringPercent());
            }
            Find.LetterStack.ReceiveLetter("MJV_MissionSettledLabel".Translate(justiciar.Named("JUSTICIAR")), text,
                bonus + penalty >= 0 ? LetterDefOf.PositiveEvent : LetterDefOf.NegativeEvent, look);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref sermonsHeld, "sermonsHeld", 0);
            Scribe_Values.Look(ref refusals, "refusals", 0);
            Scribe_Values.Look(ref turnoutGoodwill, "turnoutGoodwill", 0f);
            Scribe_Values.Look(ref moodSum, "moodSum", 0f);
            Scribe_Values.Look(ref moodSamples, "moodSamples", 0);
            Scribe_Values.Look(ref settled, "settled", false);
        }
    }
}
