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
using Verse.Sound;

namespace JusticiarValorValidation
{
    // The imperial missions against the real game: the visiting justiciar asking to be hosted (sent away, left waiting,
    // hosted) and asking the colony for sermons (one left running across the save and reload, his mission settled when he
    // leaves), the lodging mission played to its end (a sermon held beyond the minimum, one refused, a convert called to
    // service, the settlement), and the disgraced sermon with its pursuing raid and its codex-auspex.
    public sealed class MissionValidation
    {
        private static readonly FieldInfo effectsField = AccessTools.Field(typeof(MapComponent_JusticiarFx), "effects");
        private static readonly FieldInfo leaveTickField = AccessTools.Field(typeof(LordJob_MJV_MissionaryVisit), "leaveTick");
        private static readonly FieldInfo audienceEndTickField = AccessTools.Field(typeof(LordJob_MJV_MissionaryVisit), "audienceEndTick");
        private static readonly FieldInfo chillSpotField = AccessTools.Field(typeof(LordJob_MJV_MissionaryVisit), "chillSpot");
        private static readonly FieldInfo ambienceField = AccessTools.Field(typeof(LordJob_MJV_Sermon), "ambience");
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

        // Mid-morning: justiciars ask for sermons until the early afternoon (SermonRequests).
        private static void Morning(Map map)
        {
            int hour = GenLocalDate.HourOfDay(map);
            if (hour < 9 || hour > 12)
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

        // Colonists enough that the congregation can be larger than the minimum a justiciar asks for (half of the eligible,
        // the gear test's drafted pawns included).
        private void EnsureCongregation(int listeners)
        {
            int missing = listeners - Listeners().Count;
            if (missing <= 0)
            {
                return;
            }
            IntVec3 near = map.mapPawns.FreeColonistsSpawned.First().Position;
            for (int i = 0; i < missing; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false));
                GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(near, map, 6), map);
            }
            JusticiarValidation.Line("Added " + missing + " colonist(s): the congregation can be larger than the minimum");
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

        // Why a justiciar would not ask for a sermon now, for the report.
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
                + ImperialFaith.UnconvertedCount(map) + ", gatherings blocked by [" + string.Join(", ", blocking) + "], can request "
                + SermonRequests.CanRequestNow(preacher, map);
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

        // The player's answer to a justiciar's request, as the sermon window gives it: the listeners free to come (the gear
        // test's pawns stay drafted and out of it) against the minimum the whole colony makes.
        private void Hold(IJusticiarMission mission, out List<Pawn> congregation, out int required, out int eligible)
        {
            eligible = SermonRequests.Eligible(map).Count;
            required = SermonRequests.Required(eligible, mission.Settings.requiredFraction);
            congregation = Listeners().Where(SermonRequests.CanAttend).ToList();
            JusticiarValidation.Line("Congregation: " + congregation.Count + " called, " + eligible + " eligible, minimum " + required);
            mission.Answer(congregation, required, eligible);
        }

        private bool GotLetter(TaggedString label) => Find.LetterStack.LettersListForReading.Any(l => l.Label.RawText == label.RawText);

        // The pursuers on the map and the drop pods still bringing them in.
        private void ClearPursuers(Faction pursuers)
        {
            foreach (Pawn raider in map.mapPawns.SpawnedPawnsInFaction(pursuers).ToList())
            {
                raider.Destroy();
            }
            foreach (Thing pod in map.listerThings.ThingsOfDef(ThingDefOf.DropPodIncoming).Concat(map.listerThings.ThingsOfDef(ThingDefOf.ActiveDropPod)).ToList())
            {
                pod.Destroy();
            }
        }

        // A justiciar arrives on a mission and waits for someone to talk to him; his visit, or null.
        private LordJob_MJV_MissionaryVisit Arrive(IncidentDef def)
        {
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(def.category, map);
            parms.forced = true;
            if (!def.Worker.TryExecute(parms))
            {
                return null;
            }
            return map.lordManager.lords.Select(l => l.LordJob).OfType<LordJob_MJV_MissionaryVisit>().FirstOrDefault();
        }

        // A visit the test is done with: his party is taken off the map at once rather than walked to its edge.
        private static void SendHome(LordJob_MJV_MissionaryVisit job)
        {
            foreach (Pawn pawn in job.lord.ownedPawns.ToList())
            {
                pawn.Destroy();
            }
        }

        // Morning, with the visit lasting another day: a justiciar asks for sermons in the morning only, and the jump to
        // the next one could take the visit to its end.
        private void VisitMorning(LordJob_MJV_MissionaryVisit job)
        {
            Morning(map);
            if (job.LeaveTick < Find.TickManager.TicksGame + GenDate.TicksPerDay)
            {
                leaveTickField.SetValue(job, Find.TickManager.TicksGame + GenDate.TicksPerDay);
            }
        }

        public IEnumerable<object> BeforeSave()
        {
            JusticiarValidation.Line("== Imperial missions");
            JusticiarValidation.Line("Threats at the start: " + Threats());
            ClearThreats();
            Faction empire = ImperialFaith.Empire;
            Check(MJV_DefOf.MJV_Justiciar != null && MJV_DefOf.MJV_ImperialSermon != null && DefDatabase<QuestScriptDef>.GetNamedSilentFail("MJV_DisgracedSermon") != null,
                "the justiciar, his sermon's ritual window and the disgraced sermon quest are loaded");
            SermonSounds sounds = SermonSounds.Get;
            Check(sounds != null && sounds.start != null && sounds.convert != null && sounds.held != null && sounds.cutShort != null
                && MJV_DefOf.MJV_TalkToJusticiar != null && MJV_DefOf.MJV_JusticiarTurnedAway != null,
                "the sermon's sounds and the talk with a visiting justiciar are loaded");
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

            Pawn sample = PawnGenerator.GeneratePawn(new PawnGenerationRequest(MJV_DefOf.MJV_Justiciar, empire, forceGenerateNewPawn: true));
            // Its Social range is 10~16; genes can shift the level, but never take the conversion power away.
            Check(!sample.WorkTagIsDisabled(WorkTags.Social) && sample.GetStatValue(StatDefOf.ConversionPower) > 0f,
                "a generated justiciar can preach (Social " + sample.skills.GetSkill(SkillDefOf.Social).Level + ", conversion power "
                + sample.GetStatValue(StatDefOf.ConversionPower).ToStringPercent() + ")");
            Check(sample.royalty != null && sample.royalty.HasAnyTitleIn(empire), "a generated justiciar holds an imperial title (for royal favor rewards)");
            sample.Destroy();

            if (!ModsConfig.IdeologyActive || ImperialFaith.Ideo == null)
            {
                Check(DefDatabase<IncidentDef>.GetNamedSilentFail("MJV_ImperialMissionary") == null
                    && DefDatabase<QuestScriptDef>.GetNamedSilentFail("MJV_ImperialMission") == null, "without Ideology the converting events are not loaded");
            }
            else
            {
                EnsureCongregation(5);
                foreach (object step in LodgingMission(empire))
                {
                    yield return step;
                }
                // Leaves a sermon running for the save.
                foreach (object step in VisitingJusticiar(empire))
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

        private IEnumerable<object> VisitingJusticiar(Faction empire)
        {
            JusticiarValidation.Line("-- The visiting justiciar");
            ClearThreats();
            RestoreFaith();
            // The lodging mission may have taken a convert away to serve the Empire.
            EnsureCongregation(5);
            PrepareCongregation();
            Morning(map);
            IncidentDef def = DefDatabase<IncidentDef>.GetNamed("MJV_ImperialMissionary");
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(def.category, map);
            parms.forced = true;
            Check(def.Worker.CanFireNow(parms), "a justiciar can come on a mission to a colony that does not follow the imperial faith");

            // Sent away after talking to him.
            LordJob_MJV_MissionaryVisit job = Arrive(def);
            Check(job != null, "the justiciar arrives");
            if (job == null)
            {
                yield break;
            }
            Pawn preacher = job.Preacher;
            Check(job.AwaitingAudience && new Alert_MJV_JusticiarAwaitsAudience().GetReport().active
                && job.TurnedAwayGoodwill >= -14 && job.TurnedAwayGoodwill <= -8,
                "he waits for someone to talk to him, and an alert says so (sending him away costs " + job.TurnedAwayGoodwill + ")");
            job.NextSermonTick = Find.TickManager.TicksGame;
            foreach (object step in TickUntil(() => job.Request != null, 600))
            {
                yield return step;
            }
            Check(!lastWait, "he asks for no sermon before the colony has agreed to host him");
            Pawn speaker = Listeners().First(p => !p.WorkTagIsDisabled(WorkTags.Social) && p.health.capacities.CapableOf(PawnCapacityDefOf.Talking));
#if RIMWORLD_1_6
            // A float menu option measures its label with Unity GUI calls, which 1.6 only allows inside OnGUI, and the hidden
            // validation window never runs OnGUI: the colonist is ordered to talk as the option does (1.7 checks the option).
            Action talk = () => speaker.jobs.TryTakeOrderedJob(JobMaker.MakeJob(MJV_DefOf.MJV_TalkToJusticiar, preacher), JobTag.Misc);
#else
            FloatMenuOption option = job.lord.CurLordToil.ExtraFloatMenuOptions(preacher, speaker).FirstOrDefault();
            Check(option != null && !option.Disabled && option.action != null, "a colonist can be sent to talk to him (" + option?.Label + ")");
            Action talk = option?.action;
#endif
            if (talk != null)
            {
                talk();
                foreach (object step in TickUntil(() => Find.WindowStack.IsOpen<Dialog_NodeTreeWithFactionInfo>(), 6000))
                {
                    yield return step;
                }
                Check(lastWait, "the colonist walks up to him and their conversation opens (" + speaker.LabelShort + ", " + speaker.CurJobDef?.defName + ")");
                Find.WindowStack.TryRemove(typeof(Dialog_NodeTreeWithFactionInfo), false);
            }
            int goodwill0 = empire.PlayerGoodwill;
            int expected = empire.CalculateAdjustedGoodwillChange(Faction.OfPlayer, job.TurnedAwayGoodwill);
            job.TurnAway(false);
            Check(empire.PlayerGoodwill == goodwill0 + expected && !job.AwaitingAudience && job.exitSubgraph.lordToils.Contains(job.lord.CurLordToil),
                "sent away, he goes home and the Empire takes offense (" + goodwill0 + " -> " + empire.PlayerGoodwill + ")");
            SendHome(job);

            // Left waiting until he gives up.
            job = Arrive(def);
            if (job == null)
            {
                yield break;
            }
            goodwill0 = empire.PlayerGoodwill;
            expected = empire.CalculateAdjustedGoodwillChange(Faction.OfPlayer, job.TurnedAwayGoodwill);
            audienceEndTickField.SetValue(job, Find.TickManager.TicksGame);
            foreach (object step in TickUntil(() => !job.AwaitingAudience, 120))
            {
                yield return step;
            }
            Check(lastWait && empire.PlayerGoodwill == goodwill0 + expected && job.exitSubgraph.lordToils.Contains(job.lord.CurLordToil),
                "left waiting, he gives up and goes home, and the Empire takes offense (" + goodwill0 + " -> " + empire.PlayerGoodwill + ")");
            SendHome(job);
            empire.RelationWith(Faction.OfPlayer).baseGoodwill = 10;
            Faction.OfPlayer.RelationWith(empire).baseGoodwill = 10;

            // Hosted: the visit proper.
            job = Arrive(def);
            if (job == null)
            {
                yield break;
            }
            Lord visit = job.lord;
            preacher = job.Preacher;
            preacherId = preacher.thingIDNumber;
            JusticiarValidation.Line("Justiciar " + preacher.LabelShort + " with " + (visit.ownedPawns.Count - 1) + " escort(s): "
                + string.Join(", ", visit.ownedPawns.Where(p => p != preacher).Select(p => p.kindDef.defName)));
            Check(preacher.kindDef == MJV_DefOf.MJV_Justiciar && preacher.Faction == empire && ImperialFaith.IsBeliever(preacher),
                "the preacher is a justiciar of the imperial faith");
            var chillSpot = (IntVec3)chillSpotField.GetValue(job);
            foreach (object step in TickUntil(() => preacher.Position.InHorDistOf(chillSpot, 8f), 15000))
            {
                yield return step;
            }
            Check(lastWait, "the justiciar reaches the colony");
            job.Host();
            Check(!job.AwaitingAudience && visit.CurLordToil is LordToil_DefendPoint && !(visit.CurLordToil is LordToil_MJV_AwaitAudience)
                && job.LeaveTick == Find.TickManager.TicksGame + job.VisitTicks,
                "hosted, he stays for his visit (" + job.VisitTicks.ToStringTicksToPeriod() + ")");
            VisitMorning(job);
            List<Pawn> congregation = Listeners();
            Pawn weak = congregation.First();
            Weaken(weak);
            Refresh(preacher);
            goodwill0 = empire.PlayerGoodwill;
            job.NextSermonTick = Find.TickManager.TicksGame;
            foreach (object step in TickUntil(() => job.Request != null, 4000))
            {
                yield return step;
            }
            Check(lastWait, "when a sermon is due the justiciar asks the colony for one");
            if (!lastWait)
            {
                JusticiarValidation.Line("Justiciar state: " + PreachState(preacher) + ", visit toil " + visit.CurLordToil?.GetType().Name);
                JusticiarValidation.Line("Threats: " + Threats());
                yield break;
            }
            Check(job.GetPawnGizmos(preacher).OfType<Command_Action>().Count() == 2 && new Alert_MJV_SermonRequested().GetReport().active,
                "his commands to hold or refuse the sermon show on him, and an alert reminds the player");
            Hold(job, out List<Pawn> called, out int required, out int eligible);
            LordJob_MJV_Sermon sermon = SermonUtility.SermonOf(preacher);
            Check(sermon != null && visit.CurLordToil is LordToil_MJV_Preach && called.All(p => p.GetLord() == sermon.lord),
                "the colony's answer begins the sermon with the congregation it called, and he goes to preach");
            if (sermon == null)
            {
                yield break;
            }
            Check(!sermon.lord.AllowsDrafting(called[0]).Accepted, "a listener cannot be drafted out of the sermon while the colony is safe");
            foreach (object step in TickUntil(() => preacher.jobs.curDriver is JobDriver_MJV_DeliverSermon d && d.OnStage
                && MapComponent_JusticiarFx.IsRegistered(map, d), 2500))
            {
                yield return step;
            }
            Check(lastWait, "the preaching justiciar stands in his halo");
            foreach (object step in TickUntil(() => ambienceField.GetValue(sermon) is Sustainer playing && !playing.Ended, 300))
            {
                yield return step;
            }
            Check(lastWait, "the sermon plays " + SermonSounds.Get?.AmbienceFor(preacher)?.defName + " while he preaches");
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
            Check(visit.CurLordToil is LordToil_DefendPoint, "the justiciar goes back to waiting after preaching");
            Check(job.Record.sermonsHeld == 1 && (called.Count > required) == (job.Record.turnoutGoodwill > 0f),
                "the sermon is recorded for his return, with goodwill for the congregation beyond the minimum (" + job.Record.turnoutGoodwill.ToString("0.##") + ")");

            // A second sermon is left running across the save and reload.
            Pawn weak2 = congregation.First(p => p != weak);
            Weaken(weak2);
            Refresh(preacher);
            VisitMorning(job);
            job.NextSermonTick = Find.TickManager.TicksGame;
            foreach (object step in TickUntil(() => job.Request != null, 4000))
            {
                yield return step;
            }
            Check(lastWait, "he asks for a second sermon");
            if (job.Request == null)
            {
                JusticiarValidation.Line("Justiciar state: " + PreachState(preacher) + ", visit toil " + visit.CurLordToil?.GetType().Name
                    + ", leaves in " + (job.LeaveTick - Find.TickManager.TicksGame) + " ticks");
                yield break;
            }
            Hold(job, out _, out _, out _);
            foreach (object step in TickUntil(() => SermonUtility.SermonOf(preacher)?.lord.CurLordToil is LordToil_MJV_Sermon toil
                && toil.Data.preachingTicks > 300 && toil.Data.presentForTicks.Count > 0, 2500))
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
            Morning(map);
            Quest quest = MakeQuest("MJV_ImperialMission", 400f);
            Check(quest != null, "the lodging mission can be offered");
            if (quest == null)
            {
                yield break;
            }
            QuestPart_MJV_LodgerSermons sermons = quest.PartsListForReading.OfType<QuestPart_MJV_LodgerSermons>().FirstOrDefault();
            Check(sermons != null && sermons.preacher != null && sermons.preacher.kindDef == MJV_DefOf.MJV_Justiciar,
                "the mission has its justiciar and sermon schedule");
            Check(quest.PartsListForReading.OfType<QuestPart_Choice>().Any(c => c.choices.Any(ch => ch.rewards.Any(r => r is Reward_RoyalFavor || r is Reward_Goodwill))),
                "the mission rewards royal favor or goodwill");
            if (sermons == null)
            {
                yield break;
            }
            Pawn justiciar = sermons.preacher;
            Accept(quest);
            foreach (object step in TickUntil(() => justiciar.Spawned, 3000))
            {
                yield return step;
            }
            Check(lastWait && justiciar.Faction == Faction.OfPlayer && justiciar.IsQuestLodger(), "the justiciar arrives by shuttle and lodges with the colony");
            Check(ImperialFaith.IsBeliever(justiciar), "the lodging justiciar preaches the imperial faith");
            Check(justiciar.WorkTagIsDisabled(WorkTags.ManualDumb) && !justiciar.WorkTagIsDisabled(WorkTags.Social),
                "the lodging justiciar does no manual work but can still talk");

            PrepareCongregation();
            foreach (Pawn pawn in Listeners())
            {
                Weaken(pawn);
            }
            sermons.callChance = 1f;
            Refresh(justiciar);
            Morning(map);
            sermons.NextSermonTick = Find.TickManager.TicksGame;
            foreach (object step in TickUntil(() => sermons.Request != null, 2000))
            {
                yield return step;
            }
            Check(lastWait, "the lodging justiciar asks the colony for a sermon");
            if (!lastWait)
            {
                JusticiarValidation.Line("Justiciar state: " + PreachState(justiciar));
                JusticiarValidation.Line("Threats: " + Threats());
                EndQuest(quest);
                yield break;
            }
            Check(sermons.ExtraGizmos(justiciar).OfType<Command_Action>().Count() == 2, "his commands to hold or refuse the sermon come with the quest");
            // The sermon's ritual window, opened as the command opens it: everyone free to come is called, enough to begin.
            Dialog_MJV_BeginSermon dialog = null;
            try
            {
                SermonRequests.OpenDialog(sermons);
                dialog = Find.WindowStack.WindowOfType<Dialog_MJV_BeginSermon>();
                Check(dialog != null && dialog.CanBegin, "the sermon window opens with enough colonists called to begin");
            }
            catch (Exception ex)
            {
                Check(false, "the sermon window opens (" + ex.GetType().Name + ": " + ex.Message + ")");
            }
            dialog?.Close(false);

            Hold(sermons, out List<Pawn> called, out int required, out int eligible);
            LordJob_MJV_Sermon sermon = SermonUtility.SermonOf(justiciar);
            Check(sermon != null && justiciar.GetLord() == sermon.lord && called.All(p => p.GetLord() == sermon.lord),
                "the lodging justiciar holds his sermon among the colonists called to it");
            int listeners = 0;
            int preached = 0;
            var seen = new HashSet<string>();
            foreach (object step in TickUntil(() =>
            {
                if (SermonUtility.SermonOf(justiciar)?.lord.CurLordToil is LordToil_MJV_Sermon toil)
                {
                    listeners = Math.Max(listeners, toil.Data.presentForTicks.Count);
                    preached = toil.Data.preachingTicks;
                    seen.Add(justiciar.CurJobDef?.defName + "/" + justiciar.mindState.duty?.def?.defName + "/" + justiciar.Position.DistanceTo(toil.lord.LordJob is LordJob_MJV_Sermon j ? j.Spot : justiciar.Position).ToString("0"));
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
                JusticiarValidation.Line("Lodging justiciar during the sermon (job/duty/distance to the spot): " + string.Join(", ", seen.Take(12)));
                JusticiarValidation.Line("Called: " + string.Join(", ", called.Select(p => p.LabelShort + " " + (ImperialFaith.IsBeliever(p) ? "imperial" : p.Ideo?.name)
                    + " certainty " + p.ideo.Certainty.ToString("0.00") + " lord " + (p.GetLord()?.LordJob?.GetType().Name ?? "none")))
                    + "; justiciar faith " + justiciar.Ideo?.name + ", conversion power " + justiciar.GetStatValue(StatDefOf.ConversionPower).ToString("0.00")
                    + ", sermons held " + sermons.Record.sermonsHeld + ", refusals " + sermons.Record.refusals);
            }
            Check(sermons.Record.sermonsHeld == 1 && (called.Count > required) == (sermons.Record.turnoutGoodwill > 0f)
                && (called.Count <= required || justiciar.needs.mood.thoughts.memories.GetFirstMemoryOfDef(MJV_DefOf.MJV_SermonWellAttended) != null),
                "a congregation beyond the minimum pleases him and counts towards the goodwill he brings back ("
                + sermons.Record.turnoutGoodwill.ToString("0.##") + ", sermons held " + sermons.Record.sermonsHeld + ")");

            // The next one is refused.
            Refresh(justiciar);
            Morning(map);
            sermons.NextSermonTick = Find.TickManager.TicksGame;
            foreach (object step in TickUntil(() => sermons.Request != null, 2000))
            {
                yield return step;
            }
            Check(lastWait, "he asks for another sermon");
            if (sermons.Request != null)
            {
                sermons.Answer(null, 0, 0);
            }
            Check(sermons.Record.refusals == 1 && sermons.Request == null
                && justiciar.needs.mood.thoughts.memories.GetFirstMemoryOfDef(MJV_DefOf.MJV_SermonRefused) != null,
                "a refused sermon offends him and is recorded against the colony");

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
            Check(delay.State != QuestPartState.Enabled || delay.TicksLeft <= 0, "once the colony has converted the justiciar's shuttle is called early");
            foreach (object step in TickUntil(() => sermons.Called != null, 3000))
            {
                yield return step;
            }
            Pawn calledToService = sermons.Called;
            Check(calledToService != null && calledToService.MentalStateDef == MJV_DefOf.MJV_CalledToService, "a convert is called to serve the Empire when the shuttle comes");
            Thing shuttle = sermons.shuttle;
            CompShuttle comp = shuttle.TryGetComp<CompShuttle>();
            if (calledToService != null)
            {
                Check(comp.requiredPawns.Contains(calledToService), "the shuttle waits for the called convert");
                // At a walk, from wherever the convert was on the map.
                foreach (object step in TickUntil(() => comp.Transporter.innerContainer.Contains(calledToService), 12000))
                {
                    yield return step;
                }
                Check(lastWait, "the called convert walks to the shuttle and boards it");
                if (!lastWait)
                {
                    JusticiarValidation.Line("Called convert: " + calledToService.LabelShort + ", spawned " + calledToService.Spawned + ", mental state "
                        + calledToService.MentalStateDef?.defName + ", job " + calledToService.CurJobDef?.defName + ", shuttle spawned " + shuttle.Spawned);
                }
            }
            foreach (object step in TickUntil(() => shuttle.Spawned, 1500))
            {
                yield return step;
            }
            justiciar.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.EnterTransporter, shuttle), JobTag.Misc);
            foreach (object step in TickUntil(() => quest.State != QuestState.Ongoing, 4000))
            {
                yield return step;
            }
            Check(quest.State == QuestState.EndedSuccess, "the mission ends in success when the justiciar leaves (" + quest.State + ")");
            EndQuest(quest);
            Check(calledToService != null && calledToService.Faction == empire, "the called convert left the colony to serve the Empire");
            Check(sermons.Record.Settled && GotLetter("MJV_MissionSettledLabel".Translate(justiciar.Named("JUSTICIAR"))),
                "his mission is settled with the Empire when he leaves");
            Check(!sermons.Record.EarnedCodex(sermons.Settings), "a justiciar whose sermon was refused leaves no codex-auspex");
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
                .Any(r => r.items.Any(t => t.def == MJV_DefOf.MJV_ImperialCodexAuspex)))), "the sheltered justiciar promises an imperial codex-auspex");
            Check(quest.PartsListForReading.OfType<QuestPart_Incident>().Any(p => p.incident == IncidentDefOf.RaidEnemy), "a raid of pursuers is part of the quest");
            Accept(quest);
            Pawn justiciar = null;
            foreach (object step in TickUntil(() => (justiciar = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => p.kindDef == MJV_DefOf.MJV_Justiciar
                && p.IsQuestLodger())) != null, 600))
            {
                yield return step;
            }
            Check(justiciar != null, "the beset justiciar arrives on foot and lodges with the colony");
            disgracedQuestId = quest.id;
            disgracedMissionaryId = justiciar?.thingIDNumber ?? -1;
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
                    "the visiting justiciar is still preaching after the reload");
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
                    Check(job.Record.sermonsHeld == 2, "both of his sermons are recorded for his return (" + job.Record.sermonsHeld + ")");
                    int bonus = job.Record.TurnoutGoodwill(job.Settings);
                    int goodwill0 = empire.PlayerGoodwill;
                    leaveTickField.SetValue(job, Find.TickManager.TicksGame);
                    foreach (object step in TickUntil(() => !(visit.CurLordToil is LordToil_DefendPoint) && !(visit.CurLordToil is LordToil_MJV_Preach), 600))
                    {
                        yield return step;
                    }
                    Check(lastWait, "the justiciar leaves when his visit is over");
                    Check(job.Record.Settled && GotLetter("MJV_MissionSettledLabel".Translate(preacher.Named("JUSTICIAR"))) && empire.PlayerGoodwill >= goodwill0 + bonus,
                        "his mission is settled when he leaves: +" + bonus + " goodwill for his congregations (" + goodwill0 + " -> " + empire.PlayerGoodwill + ")");
                    foreach (Pawn pawn in visit.ownedPawns.ToList())
                    {
                        pawn.Destroy();
                    }
                }
            }

            Quest disgraced = Find.QuestManager.QuestsListForReading.FirstOrDefault(q => q.id == disgracedQuestId);
            Pawn justiciar = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.thingIDNumber == disgracedMissionaryId);
            Check(disgraced != null && disgraced.State == QuestState.Ongoing && justiciar != null && justiciar.IsQuestLodger(),
                "the disgraced sermon is still under way after the reload (" + disgraced?.State + ", justiciar " + (justiciar == null ? "missing"
                    : justiciar.LabelShort + " lodger " + justiciar.IsQuestLodger()) + ")");
            if (disgraced == null || justiciar == null)
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
            foreach (object step in TickUntil(() =>
            {
                ClearPursuers(pursuers);
                return false;
            }, 900))
            {
                yield return step;
            }
            ClearPursuers(pursuers);
            QuestPart_ShuttleDelay shuttleDelay = disgraced.PartsListForReading.OfType<QuestPart_ShuttleDelay>().First();
            shuttleDelay.DebugForceEnd();
            Thing shuttle = null;
            foreach (object step in TickUntil(() => (shuttle = map.listerThings.AllThings.FirstOrDefault(t => t.TryGetComp<CompShuttle>()?.requiredPawns.Contains(justiciar) == true && t.Spawned)) != null, 1500))
            {
                yield return step;
            }
            Check(shuttle != null, "an imperial shuttle comes to collect the justiciar");
            if (shuttle == null)
            {
                yield break;
            }
            int codices0 = map.listerThings.ThingsOfDef(MJV_DefOf.MJV_ImperialCodexAuspex).Count;
            float distance0 = justiciar.Position.DistanceTo(shuttle.Position);
            justiciar.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.EnterTransporter, shuttle), JobTag.Misc);
            foreach (object step in TickUntil(() => disgraced.State != QuestState.Ongoing, 9000))
            {
                yield return step;
            }
            Check(disgraced.State == QuestState.EndedSuccess, "the disgraced sermon ends in success when the justiciar leaves (" + disgraced.State
                + ", " + distance0.ToString("0") + " cells to walk)");
            if (disgraced.State == QuestState.Ongoing)
            {
                JusticiarValidation.Line("Justiciar: spawned " + justiciar.Spawned + ", " + (justiciar.Spawned ? justiciar.Position.DistanceTo(shuttle.Position).ToString("0")
                    : "-") + " cells from the shuttle, job " + justiciar.CurJobDef?.defName + ", awake " + justiciar.Awake() + ", drafted " + justiciar.Drafted
                    + ", downed " + justiciar.Downed + ", mental state " + justiciar.MentalStateDef?.defName + ", lord " + justiciar.GetLord()?.LordJob?.GetType().Name);
            }
            foreach (object step in TickUntil(() => map.listerThings.ThingsOfDef(MJV_DefOf.MJV_ImperialCodexAuspex).Count > codices0, 1500))
            {
                yield return step;
            }
            Check(lastWait, "the promised imperial codex-auspex arrives by drop pod");
        }
    }
}
