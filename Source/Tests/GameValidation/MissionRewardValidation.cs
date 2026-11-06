using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using JusticiarValor;
using RimWorld;
using Verse;

namespace JusticiarValorValidation
{
    public sealed partial class MissionValidation
    {
        private static int savedEventGiftId = -1;
        private static int savedBlueprintQuestId = -1;
        private static int savedTechprintId = -1;

        private static IEnumerable<Thing> RewardItems(Quest quest) => quest.PartsListForReading.OfType<QuestPart_Choice>()
            .SelectMany(c => c.choices).SelectMany(c => c.rewards).OfType<Reward_Items>().SelectMany(r => r.items);

        private void CheckRewardGeneration(Faction empire)
        {
            JusticiarValidation.Line("-- Native mission reward generation");
            bool valid = true;
            int armor = 0;
            int weapons = 0;
            Rand.PushState(761032);
            try
            {
                foreach (float budget in new[] { 2800f, 3000f, 3700f, 4300f, 10000f })
                {
                    for (int sample = 0; sample < 80; sample++)
                    {
                        List<Thing> items = MissionRewards.GenerateGear(budget, empire);
                        valid &= items.Count == 1 && items[0].stackCount == 1 && items[0].MarketValue <= budget + 0.01f
                            && items[0].def.thingSetMakerTags?.Contains(MissionRewards.GearTag) == true;
                        foreach (Thing item in items)
                        {
                            if (item.def.IsApparel) armor++;
                            if (item.def.IsWeapon) weapons++;
                            item.Destroy();
                        }
                    }
                }
                Check(valid, "400 native reward rolls yield one tagged item each and respect the event/quest budgets");
                Check(armor > 0 && armor < 50 && weapons > armor, "armor remains a rare eligible reward (" + armor + " armor, " + weapons + " weapons)");
                Check(MissionRewards.GenerateGear(1f, empire).Count == 0, "an impossible budget never generates an unaffordable reward");
            }
            finally
            {
                Rand.PopState();
            }
        }

        private void CheckCodexCarriers(Faction empire)
        {
            PawnKindDef nobleKind = DefDatabase<PawnKindDef>.AllDefsListForReading.FirstOrDefault(k => k != MJV_DefOf.MJV_Justiciar
                && !k.factionLeader && k.defaultFactionDef == empire.def && (k.titleRequired != null || k.minTitleRequired != null));
            Check(nobleKind != null && Harmony.GetPatchInfo(Patch_ImperialCodexInventory.TargetMethod())?.Postfixes
                .Any(p => p.owner == JusticiarValorMod.HarmonyId) == true, "the new-pawn inventory hook is installed and an imperial noble kind is available");
            if (nobleKind == null) return;
            int carriers = 0;
            int titled = 0;
            bool singular = true;
            Rand.PushState(530881);
            try
            {
                for (int i = 0; i < 160; i++)
                {
                    Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(nobleKind, empire, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
                    if (pawn.royalty?.GetCurrentTitle(empire) != null) titled++;
                    int count = pawn.inventory.innerContainer.Where(t => t.def == MJV_DefOf.MJV_ImperialCodexAuspex).Sum(t => t.stackCount);
                    singular &= count <= 1;
                    if (count > 0) carriers++;
                    pawn.Destroy();
                }
            }
            finally
            {
                Rand.PopState();
            }
            Check(singular && titled == 160 && carriers < 16, "real noble generation keeps codices rare and never adds duplicates ("
                + carriers + " / " + titled + " titled " + nobleKind.defName + ")");
            Pawn probe = PawnGenerator.GeneratePawn(new PawnGenerationRequest(nobleKind, empire, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            var request = new PawnGenerationRequest(nobleKind, empire, forceGenerateNewPawn: true, canGeneratePawnRelations: false);
            int chosenSeed = 0;
            for (; chosenSeed < 4096; chosenSeed++)
            {
                Rand.PushState(chosenSeed);
                bool wins = Rand.Chance(Patch_ImperialCodexInventory.CarryChance);
                Rand.PopState();
                if (wins) break;
            }
            Rand.PushState(chosenSeed);
            try { Patch_ImperialCodexInventory.Postfix(probe, ref request); }
            finally { Rand.PopState(); }
            Check(probe.inventory.innerContainer.Count(t => t.def == MJV_DefOf.MJV_ImperialCodexAuspex) == 1,
                "a successful rare roll adds one codex to a real titled noble");
            Rand.PushState(chosenSeed);
            try { Patch_ImperialCodexInventory.Postfix(probe, ref request); }
            finally { Rand.PopState(); }
            Check(probe.inventory.innerContainer.Count(t => t.def == MJV_DefOf.MJV_ImperialCodexAuspex) == 1,
                "an existing codex is preserved without duplication");
            probe.Destroy();
        }

        private IEnumerable<object> PrepareBlueprintSave()
        {
            RestoreFaith();
            bool recruitmentEnabled = JusticiarValorMod.Settings.allowImperialRecruitment;
            Quest quest;
            try
            {
                JusticiarValorMod.Settings.allowImperialRecruitment = false;
                quest = MakeQuest("MJV_ImperialMission_Blueprint", 400f);
            }
            finally
            {
                JusticiarValorMod.Settings.allowImperialRecruitment = recruitmentEnabled;
            }
            Check(quest != null, "the blueprint variant can be offered while justiciar techprints are needed");
            if (quest == null) yield break;
            Check(!((string)quest.description).Contains((string)"MJV_MissionRecruitmentWarning".Translate())
                && !((string)quest.description).Contains("[missionRecruitmentWarning]"),
                "a new mission with recruitment disabled omits the recruitment warning without unresolved grammar");
            var part = quest.PartsListForReading.OfType<QuestPart_MJV_LodgerSermons>().Single();
            Thing reward = RewardItems(quest).SingleOrDefault();
            Check(part.blueprintMission && part.suppressCodex && part.requiredSermons == 3
                && Math.Abs(part.Settings.requiredFraction - 0.75f) < 0.001f && Math.Abs(part.Settings.codexMinMood - 0.7f) < 0.001f
                && reward?.def == MJV_DefOf.MJV_JusticiarGear.Techprint, "the harder offer has its three hosting requirements and a single native gear techprint");
            savedBlueprintQuestId = quest.id;
            savedTechprintId = reward?.thingIDNumber ?? -1;
            Accept(quest);
            foreach (object step in TickUntil(() => part.preacher.Spawned, 3000)) yield return step;
            Check(lastWait, "the blueprint mission arrives through the ordinary shuttle flow");
            if (!lastWait) yield break;
            part.NextSermonTick = int.MaxValue;
            part.callChance = 0f;
            Refresh(part.preacher);
            part.preacher.needs.mood.CurLevelPercentage = 0.85f;
            for (int i = 0; i < 20; i++) part.Record.SampleMood(part.preacher);
            part.Notify_SermonEnded(new SermonResult { outcome = SermonOutcome.Held, attendees = 3, eligible = 4 });
            Check(part.QualifyingSermons == 1 && !part.BlueprintRequirementsMet, "one 75%-attendance sermon is recorded but does not unlock a blueprint");
            CheckBlueprintBoundaries(part.preacher);
        }

        private void CheckBlueprintBoundaries(Pawn preacher)
        {
            QuestPart_MJV_LodgerSermons Probe(float mood)
            {
                var probe = new QuestPart_MJV_LodgerSermons
                {
                    blueprintMission = true, requiredSermons = 3, preacher = preacher,
                    settings = new SermonSettings { requiredFraction = 0.75f, codexMinMood = 0.7f }
                };
                preacher.needs.mood.CurLevelPercentage = mood;
                probe.Record.SampleMood(preacher);
                return probe;
            }
            var good = Probe(0.7f);
            for (int i = 0; i < 3; i++) good.Notify_SermonEnded(new SermonResult { outcome = SermonOutcome.Held, attendees = 3, eligible = 4 });
            Check(good.BlueprintRequirementsMet, "exactly 70% mood and three 75%-attendance sermons qualify");
            good.Record.refusals++;
            Check(!good.BlueprintRequirementsMet, "a refusal blocks the blueprint despite enough sermons");
            var lowMood = Probe(0.69f);
            for (int i = 0; i < 3; i++) lowMood.Notify_SermonEnded(new SermonResult { outcome = SermonOutcome.Held, attendees = 3, eligible = 4 });
            Check(!lowMood.BlueprintRequirementsMet, "69% average mood does not meet the blueprint threshold");
            var attendance = Probe(1f);
            for (int i = 0; i < 3; i++) attendance.Notify_SermonEnded(new SermonResult { outcome = SermonOutcome.Held, attendees = 2, eligible = 4 });
            Check(attendance.QualifyingSermons == 0 && !attendance.BlueprintRequirementsMet, "insufficient actual attendance does not qualify");
            var early = Probe(0.8f);
            AccessTools.Field(typeof(QuestPart_MJV_LodgerSermons), "colonyConverted").SetValue(early, true);
            Check(early.BlueprintRequirementsMet, "early conversion fulfils the sermon count");
            early.Record.refusals++;
            Check(!early.BlueprintRequirementsMet, "early conversion does not waive refusals");
            AccessTools.Field(typeof(QuestPart_MJV_LodgerSermons), "colonyConverted").SetValue(lowMood, true);
            Check(!lowMood.BlueprintRequirementsMet, "early conversion does not waive the mood threshold");
            AccessTools.Field(typeof(QuestPart_MJV_LodgerSermons), "colonyConverted").SetValue(attendance, true);
            Check(!attendance.BlueprintRequirementsMet, "early conversion does not waive attendance");
            Refresh(preacher);
        }

        private IEnumerable<object> FinishBlueprintSave()
        {
            if (savedBlueprintQuestId < 0) yield break;
            Quest quest = Find.QuestManager.QuestsListForReading.FirstOrDefault(q => q.id == savedBlueprintQuestId);
            var part = quest?.PartsListForReading.OfType<QuestPart_MJV_LodgerSermons>().SingleOrDefault();
            Thing reward = quest == null ? null : RewardItems(quest).SingleOrDefault();
            Check(part != null && part.QualifyingSermons == 1 && part.blueprintMission && part.suppressCodex
                && reward?.thingIDNumber == savedTechprintId, "blueprint progress, conditions and the promised techprint survive a real save/reload");
            if (part == null) yield break;
            Refresh(part.preacher);
            part.preacher.needs.mood.CurLevelPercentage = 0.85f;
            for (int i = 0; i < 20; i++) part.Record.SampleMood(part.preacher);
            for (int i = 0; i < 2; i++) part.Notify_SermonEnded(new SermonResult { outcome = SermonOutcome.Held, attendees = 3, eligible = 4 });
            Check(part.BlueprintRequirementsMet, "finishing the remaining qualifying sermons unlocks the saved reward");
            int codices = map.listerThings.ThingsOfDef(MJV_DefOf.MJV_ImperialCodexAuspex).Count;
            Find.SignalManager.SendSignal(new Signal(part.inSignalShuttleSent));
            foreach (object step in TickUntil(() => reward?.Spawned == true, 1800)) yield return step;
            Check(lastWait && quest.State == QuestState.EndedSuccess && part.Record.Settled,
                "successful return delivers the original techprint through native quest rewards");
            Find.SignalManager.SendSignal(new Signal(part.inSignalShuttleSent));
            Check(map.listerThings.AllThings.Count(t => t.thingIDNumber == savedTechprintId) == 1
                && map.listerThings.ThingsOfDef(MJV_DefOf.MJV_ImperialCodexAuspex).Count == codices,
                "repeated completion neither duplicates the blueprint nor adds a codex");
            EndQuest(quest);
            if (part.preacher.Spawned) part.preacher.Destroy();

            RestoreFaith();
            Quest failed = MakeQuest("MJV_ImperialMission_Blueprint", 400f);
            Check(failed != null, "another unfulfilled commission can be generated");
            if (failed == null) yield break;
            var failedPart = failed.PartsListForReading.OfType<QuestPart_MJV_LodgerSermons>().Single();
            Thing withheld = RewardItems(failed).Single();
            Accept(failed);
            Find.SignalManager.SendSignal(new Signal(failedPart.inSignalShuttleSent));
            Check(failed.State == QuestState.EndedFailed && !withheld.Spawned,
                "returning without meeting the blueprint conditions fails the commission and withholds the reward");
            EndQuest(failed);
        }
    }
}
