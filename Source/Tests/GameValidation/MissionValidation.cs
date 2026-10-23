using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using JusticiarValor;
using RimWorld;
using RimWorld.QuestGen;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace JusticiarValorValidation
{
    // The imperial missions against the real game: the visiting missionary and his sermons (one left running across
    // the save and reload), the lodging mission played to its end with a convert called to service, and the disgraced
    // sermon with its pursuing raid and its tome.
    public sealed class MissionValidation
    {
        private static readonly FieldInfo effectsField = AccessTools.Field(typeof(MapComponent_JusticiarFx), "effects");
        private static readonly FieldInfo leaveTickField = AccessTools.Field(typeof(LordJob_MJV_MissionaryVisit), "leaveTick");
        private static readonly MethodInfo checkConvertedMethod = AccessTools.Method(typeof(QuestPart_MJV_LodgerSermons), "CheckColonyConverted");
        private static readonly FieldInfo incidentParmsField = AccessTools.Field(typeof(QuestPart_Incident), "incidentParms");
        private static int preacherId = -1;
        private static int disgracedQuestId = -1;
        private static int disgracedMissionaryId = -1;
        // The colony's faith before the missions: once every colonist has converted, the player faction's primary
        // ideoligion is the imperial one.
        private static Ideo originalIdeo;

        private readonly Map map;
        private readonly HashSet<int> testPawnIds;
        private bool lastWait;
        public int Failures { get; private set; }

        // testPawnIds: the gear test's pawns, kept drafted (out of the sermons and of the call to service) so that
        // the later checks find them.
        public MissionValidation(Map map, IEnumerable<int> testPawnIds)
        {
            this.map = map;
            this.testPawnIds = new HashSet<int>(testPawnIds);
        }

        private bool IsTestPawn(Pawn pawn) => testPawnIds.Contains(pawn.thingIDNumber);

        private List<Pawn> Listeners() => ImperialFaith.Congregation(map).Where(p => !IsTestPawn(p)).OrderBy(p => p.thingIDNumber).ToList();

        private void Check(bool value, string message)
        {
            if (!value)
            {
                Failures++;
            }
            JusticiarValidation.Line((value ? "PASS " : "FAIL ") + message);
        }

        // Runs the game tick by tick (a batch per frame) until the condition holds; the outcome is in lastWait. Returns
        // in the frame the condition came true, so short-lived effects spawned on that tick are still there to check.
        private IEnumerable<object> TickUntil(Func<bool> condition, int maxTicks, int perFrame = 250)
        {
            int ticks = 0;
            while (!condition())
            {
                if (ticks >= maxTicks)
                {
                    lastWait = false;
                    yield break;
                }
                for (int i = 0; i < perFrame && ticks < maxTicks && !condition(); i++, ticks++)
                {
                    Find.TickManager.DoSingleTick();
                }
                if (condition())
                {
                    break;
                }
                yield return 1;
            }
            lastWait = true;
        }

        private int EffectCount(string typeName)
        {
            var effects = (IList)effectsField.GetValue(MapComponent_JusticiarFx.Of(map));
            return effects.Cast<object>().Count(e => e.GetType().Name == typeName);
        }

        private static void Noon(Map map)
        {
            int hour = GenLocalDate.HourOfDay(map);
            if (hour < 9 || hour > 13)
            {
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + (10 - hour + 24) % 24 * GenDate.TicksPerHour);
            }
        }

        // The congregation free to listen: undrafted, fed, rested, recreation allowed all day.
        private void PrepareCongregation()
        {
            foreach (Pawn pawn in ImperialFaith.Congregation(map).ToList())
            {
                if (pawn.drafter != null)
                {
                    pawn.drafter.Drafted = IsTestPawn(pawn);
                }
                pawn.jobs.StopAll();
                if (pawn.timetable != null)
                {
                    for (int hour = 0; hour < 24; hour++)
                    {
                        pawn.timetable.SetAssignment(hour, TimeAssignmentDefOf.Anything);
                    }
                }
                if (pawn.needs?.food != null)
                {
                    pawn.needs.food.CurLevelPercentage = 1f;
                }
                if (pawn.needs?.rest != null)
                {
                    pawn.needs.rest.CurLevelPercentage = 1f;
                }
                pawn.health.Reset();
            }
        }

        private void RestoreFaith()
        {
            Ideo own = originalIdeo;
            foreach (Pawn pawn in ImperialFaith.Congregation(map))
            {
                if (pawn.Ideo != own)
                {
                    pawn.ideo.SetIdeo(own);
                }
                pawn.ideo.OffsetCertainty(1f);
            }
        }

        // Why a preacher would not start a sermon now, for the report.
        private string PreachState(Pawn preacher)
        {
            if (preacher == null)
            {
                return "no preacher";
            }
            IEnumerable<string> blocking = map.lordManager.lords.Where(l => l.ownedPawns.Count != 0 && !l.LordJob.AllowStartNewGatherings)
                .Select(l => l.LordJob.GetType().Name);
            return "spawned " + preacher.Spawned + ", awake " + preacher.Awake() + ", in bed " + preacher.InBed() + ", can gather "
                + GatheringsUtility.PawnCanStartOrContinueGathering(preacher) + ", food " + preacher.needs?.food?.CurCategory + ", rest "
                + preacher.needs?.rest?.CurCategory + ", hour " + GenLocalDate.HourInteger(map) + ", danger " + map.dangerWatcher.DangerRating
                + ", lord " + preacher.GetLord()?.LordJob?.GetType().Name + ", job " + preacher.CurJobDef?.defName + ", unconverted "
                + ImperialFaith.UnconvertedCount(map) + ", gatherings blocked by [" + string.Join(", ", blocking) + "], can preach "
                + SermonUtility.CanPreachNow(preacher, map);
        }

        // The map's active threats (what raises the danger rating that keeps sermons from starting).
        private string Threats()
        {
            IEnumerable<string> threats = map.attackTargetsCache.TargetsHostileToColony.Where(t => GenHostility.IsActiveThreatToPlayer(t))
                .Select(t => t.Thing.LabelShort + " [" + t.Thing.def.defName + ", " + (t.Thing.Faction?.Name ?? "no faction") + ", " + t.Thing.Position
                    + (t.Thing is Pawn p ? ", power " + p.kindDef.combatPower + ", lord " + p.GetLord()?.LordJob?.GetType().Name + ", job " + p.CurJobDef?.defName : "") + "]");
            return "danger " + map.dangerWatcher.DangerRating + ": " + string.Join("; ", threats);
        }

        // A mission test that stopped half-way should not leave its quest (and shuttle) running into the next one.
        private static void EndQuest(Quest quest)
        {
            if (quest.State == QuestState.Ongoing)
            {
                quest.End(QuestEndOutcome.Unknown, false);
            }
        }

        // Quick-test maps come with whatever the map generator put there, an awake mech cluster for one, and high danger
        // rightly keeps sermons (like parties) from starting. The missions are tested on a quiet map.
        private void ClearThreats()
        {
            List<Pawn> hostile = map.mapPawns.AllPawnsSpawned.Where(p => p.HostileTo(Faction.OfPlayer)).ToList();
            if (hostile.Count > 0)
            {
                JusticiarValidation.Line("Removed " + hostile.Count + " hostile pawn(s) for a quiet test map: "
                    + string.Join(", ", hostile.GroupBy(p => p.kindDef.label).Select(g => g.Count() + " " + g.Key)));
            }
            foreach (Pawn pawn in hostile)
            {
                pawn.Destroy();
            }
            // Mech cluster structures (assemblers, turrets) would bring the danger back.
            List<Thing> structures = map.listerThings.AllThings.Where(t => t is Building && t.Spawned && t.Faction != null && t.Faction.HostileTo(Faction.OfPlayer)).ToList();
            if (structures.Count > 0)
            {
                JusticiarValidation.Line("Removed " + structures.Count + " hostile structure(s)");
            }
            foreach (Thing thing in structures)
            {
                thing.Destroy();
            }
        }

        private static void Refresh(Pawn pawn)
        {
            if (pawn.needs?.food != null)
            {
                pawn.needs.food.CurLevelPercentage = 1f;
            }
            if (pawn.needs?.rest != null)
            {
                pawn.needs.rest.CurLevelPercentage = 1f;
            }
            if (pawn.needs?.joy != null)
            {
                pawn.needs.joy.CurLevelPercentage = 1f;
            }
        }

        private static void Weaken(Pawn pawn) => pawn.ideo.OffsetCertainty(-(pawn.ideo.Certainty - 0.01f));

        // Picks the first reward of every choice, as the player does in the quest window before accepting.
        private static void Accept(Quest quest)
        {
            // Choosing removes the other choices' parts from the quest: iterate over a copy.
            foreach (QuestPart_Choice choice in quest.PartsListForReading.OfType<QuestPart_Choice>().ToList())
            {
                if (choice.choices.Count > 0)
                {
                    choice.Choose(choice.choices[0]);
                }
            }
            quest.Accept(null);
        }

        private Quest MakeQuest(string defName, float points)
        {
            QuestScriptDef def = DefDatabase<QuestScriptDef>.GetNamed(defName);
            var slate = new Slate();
            slate.Set("points", points);
            if (!def.CanRun(slate, map))
            {
                return null;
            }
            return QuestUtility.GenerateQuestAndMakeAvailable(def, slate);
        }

        public IEnumerable<object> BeforeSave()
        {
            JusticiarValidation.Line("== Imperial missions");
            JusticiarValidation.Line("Threats at the start: " + Threats());
            ClearThreats();
            Faction empire = ImperialFaith.Empire;
            Check(MJV_DefOf.MJV_ImperialMissionary != null && DefDatabase<QuestScriptDef>.GetNamedSilentFail("MJV_DisgracedSermon") != null,
                "the missionary pawn kind and the disgraced sermon quest are loaded");
            Check(MJV_DefOf.MJV_CalledToService != null && DefDatabase<ThinkTreeDef>.GetNamed("Humanlike").thinkRoot != null,
                "the call-to-service mental state is loaded");
            if (empire == null)
            {
                JusticiarValidation.Line("SKIP no Empire faction in this world");
                yield break;
            }
            // Neutral, with room to improve, and no random incidents, breaks or diseases while the game runs.
            empire.RelationWith(Faction.OfPlayer).baseGoodwill = 10;
            Faction.OfPlayer.RelationWith(empire).baseGoodwill = 10;
            originalIdeo = Faction.OfPlayer.ideos.PrimaryIdeo;
            DebugSettings.enableStoryteller = false;
            DebugSettings.enableRandomMentalStates = false;
            DebugSettings.enableRandomDiseases = false;

            Pawn sample = PawnGenerator.GeneratePawn(new PawnGenerationRequest(MJV_DefOf.MJV_ImperialMissionary, empire, forceGenerateNewPawn: true));
            Check(sample.skills.GetSkill(SkillDefOf.Social).Level >= 12, "a generated missionary is a gifted speaker (Social " + sample.skills.GetSkill(SkillDefOf.Social).Level + ")");
            Check(sample.royalty != null && sample.royalty.HasAnyTitleIn(empire), "a generated missionary holds an imperial title (for royal favor rewards)");
            Check(sample.equipment.Primary != null || sample.WorkTagIsDisabled(WorkTags.Violent),
                "a generated missionary carries a staff unless incapable of violence (" + (sample.equipment.Primary?.def.defName ?? "pacifist") + ")");
            sample.Destroy();

            if (!ModsConfig.IdeologyActive || ImperialFaith.Ideo == null)
            {
                Check(DefDatabase<IncidentDef>.GetNamedSilentFail("MJV_ImperialMissionary") == null
                    && DefDatabase<QuestScriptDef>.GetNamedSilentFail("MJV_ImperialMission") == null, "without Ideology the converting events are not loaded");
            }
            else
            {
                foreach (object step in LodgingMission(empire))
                {
                    yield return step;
                }
                // Leaves a sermon running for the save.
                foreach (object step in VisitingMissionary(empire))
                {
                    yield return step;
                }
            }
            // Last before the save: its pursuers come a quarter day or more after it is accepted, and only after the
            // reload does the test call them in. Earlier, they would raid the other missions' long runs.
            foreach (object step in DisgracedSermon(empire))
            {
                yield return step;
            }
            RecordSermonForSave();
        }

        private static int savedPreachingTicks = -1;
        private static int savedListeners = -1;

        private void RecordSermonForSave()
        {
            Pawn preacher = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.thingIDNumber == preacherId);
            if (SermonUtility.SermonOf(preacher)?.lord.CurLordToil is LordToil_MJV_Sermon toil)
            {
                savedPreachingTicks = toil.Data.preachingTicks;
                savedListeners = toil.Data.presentForTicks.Count;
                JusticiarValidation.Line("Sermon at the save: " + savedPreachingTicks + " ticks preached, " + savedListeners + " listener(s) heard it");
            }
        }

        // ---------------------------------------------------------------------------------------------------------

        private IEnumerable<object> VisitingMissionary(Faction empire)
        {
            JusticiarValidation.Line("-- The visiting missionary");
            ClearThreats();
            RestoreFaith();
            PrepareCongregation();
            Noon(map);
            IncidentDef def = DefDatabase<IncidentDef>.GetNamed("MJV_ImperialMissionary");
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(def.category, map);
            parms.forced = true;
            Check(def.Worker.CanFireNow(parms), "the imperial missionary can visit a colony that does not follow the imperial faith");
            Check(def.Worker.TryExecute(parms), "the imperial missionary arrives");
            Lord visit = map.lordManager.lords.FirstOrDefault(l => l.LordJob is LordJob_MJV_MissionaryVisit);
            Check(visit != null, "the missionary's party has its visiting lord");
            if (visit == null)
            {
                yield break;
            }
            var job = (LordJob_MJV_MissionaryVisit)visit.LordJob;
            Pawn preacher = job.Preacher;
            preacherId = preacher.thingIDNumber;
            JusticiarValidation.Line("Missionary " + preacher.LabelShort + " with " + (visit.ownedPawns.Count - 1) + " escort(s)");
            Check(preacher.Faction == empire && ImperialFaith.IsBeliever(preacher), "the missionary is an imperial believer");

            foreach (object step in TickUntil(() => visit.CurLordToil is LordToil_DefendPoint, 15000))
            {
                yield return step;
            }
            Check(lastWait, "the missionary reaches the colony");
            List<Pawn> congregation = Listeners();
            Pawn weak = congregation.First();
            Weaken(weak);
            Refresh(preacher);
            int goodwill0 = empire.PlayerGoodwill;
            job.NextSermonTick = Find.TickManager.TicksGame;
            foreach (object step in TickUntil(() => SermonUtility.SermonOf(preacher) != null, 4000))
            {
                yield return step;
            }
            Check(lastWait && visit.CurLordToil is LordToil_MJV_Preach, "when a sermon is due the missionary goes to preach and the colonists' sermon begins");
            if (!lastWait)
            {
                JusticiarValidation.Line("Missionary state: " + PreachState(preacher) + ", visit toil " + visit.CurLordToil?.GetType().Name);
                JusticiarValidation.Line("Threats: " + Threats());
            }
            LordJob_MJV_Sermon sermon = SermonUtility.SermonOf(preacher);
            if (sermon == null)
            {
                yield break;
            }
            foreach (object step in TickUntil(() => sermon.lord.ownedPawns.Contains(weak), 2500))
            {
                yield return step;
            }
            Check(lastWait, "colonists free to do so join the sermon (" + sermon.lord.ownedPawns.Count + " listeners)");
            Check(preacher.jobs.curDriver is JobDriver_MJV_DeliverSermon driver && driver.OnStage && MapComponent_JusticiarFx.IsRegistered(map, driver),
                "the preaching missionary stands in his halo");
            foreach (object step in TickUntil(() => SermonUtility.SermonOf(preacher) == null, 6000))
            {
                yield return step;
            }
            Check(lastWait, "the sermon ends");
            Check(ImperialFaith.IsBeliever(weak), "a listener whose certainty broke converted to the imperial faith");
            Check(weak.needs.mood.thoughts.memories.GetFirstMemoryOfDef(MJV_DefOf.MJV_HeardImperialSermon) != null, "listeners remember the sermon");
            Check(empire.PlayerGoodwill > goodwill0, "the Empire is pleased by the convert (" + goodwill0 + " -> " + empire.PlayerGoodwill + ")");
            Check(weak.Spawned && weak.Position.GetThingList(weak.Map).Any(t => t is MoteBubble bubble && bubble.link1.Linked && bubble.link1.Target.Thing == weak),
                "the convert shows the game's conversion bubble");
            Check(visit.CurLordToil is LordToil_DefendPoint, "the missionary goes back to waiting after preaching");

            // A second sermon is left running across the save and reload.
            Pawn weak2 = congregation.First(p => p != weak);
            Weaken(weak2);
            Refresh(preacher);
            job.NextSermonTick = Find.TickManager.TicksGame;
            foreach (object step in TickUntil(() => SermonUtility.SermonOf(preacher)?.lord.ownedPawns.Contains(weak2) == true, 6000))
            {
                yield return step;
            }
            foreach (object step in TickUntil(() => SermonUtility.SermonOf(preacher)?.lord.CurLordToil is LordToil_MJV_Sermon toil
                && toil.Data.preachingTicks > 300 && toil.Data.presentForTicks.Count > 0, 1800))
            {
                yield return step;
            }
            Check(lastWait, "a second sermon is under way for the save");
        }

        // ---------------------------------------------------------------------------------------------------------

        private IEnumerable<object> LodgingMission(Faction empire)
        {
            JusticiarValidation.Line("-- The imperial mission (lodging)");
            ClearThreats();
            Noon(map);
            Quest quest = MakeQuest("MJV_ImperialMission", 400f);
            Check(quest != null, "the lodging mission can be offered");
            if (quest == null)
            {
                yield break;
            }
            QuestPart_MJV_LodgerSermons sermons = quest.PartsListForReading.OfType<QuestPart_MJV_LodgerSermons>().FirstOrDefault();
            Check(sermons != null && sermons.preacher != null, "the mission has its missionary and sermon schedule");
            Check(quest.PartsListForReading.OfType<QuestPart_Choice>().Any(c => c.choices.Any(ch => ch.rewards.Any(r => r is Reward_RoyalFavor || r is Reward_Goodwill))),
                "the mission rewards royal favor or goodwill");
            if (sermons == null)
            {
                yield break;
            }
            Pawn missionary = sermons.preacher;
            Accept(quest);
            foreach (object step in TickUntil(() => missionary.Spawned, 3000))
            {
                yield return step;
            }
            Check(lastWait && missionary.Faction == Faction.OfPlayer && missionary.IsQuestLodger(), "the missionary arrives by shuttle and lodges with the colony");
            Check(ImperialFaith.IsBeliever(missionary), "the lodging missionary preaches the imperial faith");
            Check(missionary.WorkTagIsDisabled(WorkTags.ManualDumb) && !missionary.WorkTagIsDisabled(WorkTags.Social),
                "the lodging missionary does no manual work but can still talk");

            PrepareCongregation();
            foreach (Pawn pawn in Listeners())
            {
                Weaken(pawn);
            }
            sermons.callChance = 1f;
            Refresh(missionary);
            sermons.NextSermonTick = Find.TickManager.TicksGame;
            foreach (object step in TickUntil(() => SermonUtility.SermonOf(missionary) != null, 2000))
            {
                yield return step;
            }
            Check(lastWait && missionary.GetLord()?.LordJob is LordJob_MJV_Sermon, "the lodging missionary holds his sermon among the colonists");
            if (!lastWait)
            {
                JusticiarValidation.Line("Missionary state: " + PreachState(missionary));
                JusticiarValidation.Line("Threats: " + Threats());
            }
            // Colonists join gatherings when they next look for something to do: have them look now.
            foreach (Pawn pawn in Listeners())
            {
                pawn.jobs.StopAll();
            }
            int listeners = 0;
            int preached = 0;
            var seen = new HashSet<string>();
            foreach (object step in TickUntil(() =>
            {
                if (SermonUtility.SermonOf(missionary)?.lord.CurLordToil is LordToil_MJV_Sermon toil)
                {
                    listeners = Math.Max(listeners, toil.Data.presentForTicks.Count);
                    preached = toil.Data.preachingTicks;
                    seen.Add(missionary.CurJobDef?.defName + "/" + missionary.mindState.duty?.def?.defName + "/" + missionary.Position.DistanceTo(toil.lord.LordJob is LordJob_MJV_Sermon j ? j.Spot : missionary.Position).ToString("0"));
                    return false;
                }
                return true;
            }, 8000))
            {
                yield return step;
            }
            int converts = sermons.Converts.Count;
            Check(lastWait && converts >= 1, "the mission's sermon converts colonists (" + converts + " of " + listeners + " listener(s), " + preached + " ticks preached)");
            if (converts == 0)
            {
                JusticiarValidation.Line("Lodging missionary during the sermon (job/duty/distance to the spot): " + string.Join(", ", seen.Take(12)));
            }

            // The whole colony follows the faith: his work is done and the shuttle comes early.
            foreach (Pawn pawn in ImperialFaith.Congregation(map))
            {
                if (!ImperialFaith.IsBeliever(pawn))
                {
                    pawn.ideo.SetIdeo(ImperialFaith.Ideo);
                }
            }
            checkConvertedMethod.Invoke(sermons, null);
            QuestPart_ShuttleDelay delay = quest.PartsListForReading.OfType<QuestPart_ShuttleDelay>().First();
            Check(delay.State != QuestPartState.Enabled || delay.TicksLeft <= 0, "once the colony has converted the missionary's shuttle is called early");
            foreach (object step in TickUntil(() => sermons.Called != null, 3000))
            {
                yield return step;
            }
            Pawn called = sermons.Called;
            Check(called != null && called.MentalStateDef == MJV_DefOf.MJV_CalledToService, "a convert is called to serve the Empire when the shuttle comes");
            Thing shuttle = sermons.shuttle;
            CompShuttle comp = shuttle.TryGetComp<CompShuttle>();
            if (called != null)
            {
                Check(comp.requiredPawns.Contains(called), "the shuttle waits for the called convert");
                // At a walk, from wherever the convert was on the map.
                foreach (object step in TickUntil(() => comp.Transporter.innerContainer.Contains(called), 12000))
                {
                    yield return step;
                }
                Check(lastWait, "the called convert walks to the shuttle and boards it");
                if (!lastWait)
                {
                    JusticiarValidation.Line("Called convert: " + called.LabelShort + ", spawned " + called.Spawned + ", mental state " + called.MentalStateDef?.defName
                        + ", job " + called.CurJobDef?.defName + ", shuttle spawned " + shuttle.Spawned);
                }
            }
            foreach (object step in TickUntil(() => shuttle.Spawned, 1500))
            {
                yield return step;
            }
            missionary.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.EnterTransporter, shuttle), JobTag.Misc);
            foreach (object step in TickUntil(() => quest.State != QuestState.Ongoing, 4000))
            {
                yield return step;
            }
            Check(quest.State == QuestState.EndedSuccess, "the mission ends in success when the missionary leaves (" + quest.State + ")");
            EndQuest(quest);
            Check(called != null && called.Faction == empire, "the called convert left the colony to serve the Empire");
            RestoreFaith();
        }

        // ---------------------------------------------------------------------------------------------------------

        private IEnumerable<object> DisgracedSermon(Faction empire)
        {
            JusticiarValidation.Line("-- The disgraced sermon");
            Faction pursuers = QuestNode_MJV_GetPursuers.FindPursuers(map);
            Check(pursuers != null && pursuers.HostileTo(Faction.OfPlayer), "the pursuers are a hostile neighbour (" + pursuers?.Name + ")");
            Quest quest = MakeQuest("MJV_DisgracedSermon", 600f);
            Check(quest != null, "the disgraced sermon can be offered");
            if (quest == null)
            {
                yield break;
            }
            Check(quest.PartsListForReading.OfType<QuestPart_Choice>().Any(c => c.choices.Any(ch => ch.rewards.OfType<Reward_Items>()
                .Any(r => r.items.Any(t => t.def == MJV_DefOf.MJV_ImperialCodexAuspex)))), "the sheltered missionary promises an imperial tome");
            Check(quest.PartsListForReading.OfType<QuestPart_Incident>().Any(p => p.incident == IncidentDefOf.RaidEnemy), "a raid of pursuers is part of the quest");
            Accept(quest);
            Pawn missionary = null;
            foreach (object step in TickUntil(() => (missionary = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => p.kindDef == MJV_DefOf.MJV_ImperialMissionary
                && p.IsQuestLodger())) != null, 600))
            {
                yield return step;
            }
            Check(missionary != null, "the fleeing missionary arrives on foot and lodges with the colony");
            disgracedQuestId = quest.id;
            disgracedMissionaryId = missionary?.thingIDNumber ?? -1;
        }

        // ---------------------------------------------------------------------------------------------------------

        public IEnumerable<object> AfterReload()
        {
            JusticiarValidation.Line("== Imperial missions after the reload");
            ClearThreats();
            Faction empire = ImperialFaith.Empire;
            if (empire == null)
            {
                yield break;
            }
            if (preacherId >= 0)
            {
                Pawn preacher = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.thingIDNumber == preacherId);
                LordJob_MJV_Sermon sermon = SermonUtility.SermonOf(preacher);
                Check(preacher != null && sermon != null && preacher.GetLord()?.CurLordToil is LordToil_MJV_Preach,
                    "the visiting missionary is still preaching after the reload");
                if (sermon != null)
                {
                    var toil = (LordToil_MJV_Sermon)sermon.lord.CurLordToil;
                    // The game may run a few ticks while the save loads.
                    Check(toil.Data.preachingTicks >= savedPreachingTicks && toil.Data.preachingTicks < savedPreachingTicks + 120
                        && toil.Data.presentForTicks.Count == savedListeners,
                        "the sermon kept its progress and its listeners (" + toil.Data.preachingTicks + " ticks, " + toil.Data.presentForTicks.Count + " listener(s))");
                    foreach (object step in TickUntil(() => SermonUtility.SermonOf(preacher) == null, 6000))
                    {
                        yield return step;
                    }
                    Check(lastWait, "the reloaded sermon concludes");
                }
                Lord visit = preacher?.GetLord();
                if (visit?.LordJob is LordJob_MJV_MissionaryVisit job)
                {
                    leaveTickField.SetValue(job, Find.TickManager.TicksGame);
                    foreach (object step in TickUntil(() => !(visit.CurLordToil is LordToil_DefendPoint) && !(visit.CurLordToil is LordToil_MJV_Preach), 600))
                    {
                        yield return step;
                    }
                    Check(lastWait, "the missionary leaves when his visit is over");
                    foreach (Pawn pawn in visit.ownedPawns.ToList())
                    {
                        pawn.Destroy();
                    }
                }
            }

            Quest disgraced = Find.QuestManager.QuestsListForReading.FirstOrDefault(q => q.id == disgracedQuestId);
            Pawn missionary = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.thingIDNumber == disgracedMissionaryId);
            Check(disgraced != null && disgraced.State == QuestState.Ongoing && missionary != null && missionary.IsQuestLodger(),
                "the disgraced sermon is still under way after the reload (" + disgraced?.State + ", missionary " + (missionary == null ? "missing"
                    : missionary.LabelShort + " lodger " + missionary.IsQuestLodger()) + ")");
            if (disgraced == null || missionary == null)
            {
                yield break;
            }
            QuestPart_Incident raidPart = disgraced.PartsListForReading.OfType<QuestPart_Incident>().First(p => p.incident == IncidentDefOf.RaidEnemy);
            var raidParms = (IncidentParms)incidentParmsField.GetValue(raidPart);
            Faction pursuers = raidParms.faction;
            JusticiarValidation.Line("Pursuers' raid: " + raidParms.raidStrategy?.defName + ", " + raidParms.raidArrivalMode?.defName + ", " + raidParms.points + " points");
            int raiders0 = map.mapPawns.SpawnedPawnsInFaction(pursuers).Count;
            List<QuestPart_Delay> delays = disgraced.PartsListForReading.OfType<QuestPart_Delay>().ToList();
            JusticiarValidation.Line("Quest delays: " + string.Join(", ", delays.Select(d => d.GetType().Name + " " + d.State + " " + d.TicksLeft)));
            foreach (QuestPart_Delay delay in delays.Where(d => !(d is QuestPart_ShuttleDelay) && d.State == QuestPartState.Enabled))
            {
                delay.DebugForceEnd();
            }
            JusticiarValidation.Line("Hostiles on the map after the pursuers' delay: " + map.mapPawns.AllPawnsSpawned.Count(p => p.HostileTo(Faction.OfPlayer))
                + ", last letter: " + (Find.LetterStack.LettersListForReading.LastOrDefault()?.Label.RawText ?? "none"));
            // Drop pods and shuttles land a few seconds after the letter.
            foreach (object step in TickUntil(() => map.mapPawns.SpawnedPawnsInFaction(pursuers).Count > raiders0, 2500))
            {
                yield return step;
            }
            Check(lastWait, "the pursuers of " + pursuers.Name + " raid the colony (" + (map.mapPawns.SpawnedPawnsInFaction(pursuers).Count - raiders0) + ")");
            foreach (Pawn raider in map.mapPawns.SpawnedPawnsInFaction(pursuers).ToList())
            {
                raider.Destroy();
            }
            QuestPart_ShuttleDelay shuttleDelay = disgraced.PartsListForReading.OfType<QuestPart_ShuttleDelay>().First();
            shuttleDelay.DebugForceEnd();
            Thing shuttle = null;
            foreach (object step in TickUntil(() => (shuttle = map.listerThings.AllThings.FirstOrDefault(t => t.TryGetComp<CompShuttle>()?.requiredPawns.Contains(missionary) == true && t.Spawned)) != null, 1500))
            {
                yield return step;
            }
            Check(shuttle != null, "an imperial shuttle comes to collect the missionary");
            if (shuttle == null)
            {
                yield break;
            }
            int tomes0 = map.listerThings.ThingsOfDef(MJV_DefOf.MJV_ImperialCodexAuspex).Count;
            missionary.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.EnterTransporter, shuttle), JobTag.Misc);
            foreach (object step in TickUntil(() => disgraced.State != QuestState.Ongoing, 4000))
            {
                yield return step;
            }
            Check(disgraced.State == QuestState.EndedSuccess, "the disgraced sermon ends in success when the missionary leaves (" + disgraced.State + ")");
            foreach (object step in TickUntil(() => map.listerThings.ThingsOfDef(MJV_DefOf.MJV_ImperialCodexAuspex).Count > tomes0, 1500))
            {
                yield return step;
            }
            Check(lastWait, "the promised imperial tome arrives by drop pod");
        }
    }
}
