using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using JusticiarValor;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Vector3 = UnityEngine.Vector3;

namespace JusticiarValorValidation
{
    // Records a clip of every Justiciar Valor effect in a staged scene (-jvrecord). The game stays paused and every
    // video frame advances it by exactly one tick, 1/60 s of game time, which is what the effects run on: the clips play
    // at 60 fps however long a capture takes. Each clip's frames go to Screenshots/jvrec/<clip>/f0000.png...; once they
    // are all written, <clip>.done tells Run-GameValidation.ps1 -Record to encode it. Pawn labels are hidden, the rest of
    // the screen is what the player sees with the interface hidden.
    public sealed class EffectRecorder
    {
        // Yielded to the driver: take this screenshot, then wait until FrameCaptured.
        public sealed class Frame
        {
            // Relative to the screenshot folder, without the extension (ScreenshotTaker adds it).
            public string fileName;
            public string path;
            // A frame Backlog frames back that must be on disk before the next tick.
            public string waitFor;
        }

        public const string FolderName = "jvrec";
        private const int Backlog = 10;

        private static readonly FieldInfo effectsField = AccessTools.Field(typeof(MapComponent_JusticiarFx), "effects");
        private static readonly FieldInfo orbsField = AccessTools.Field(typeof(CompPsyOrbSynthesizer), "orbs");
        private static readonly FieldInfo chargeField = AccessTools.Field(typeof(CompPsychicShield), "charge");
        private static readonly FieldInfo energyField = AccessTools.Field(typeof(CompPsychicShield), "energy");
        private static readonly FieldInfo resetField = AccessTools.Field(typeof(CompPsychicShield), "ticksToReset");
        private static readonly FieldInfo synthesisField = AccessTools.Field(typeof(CompPsyOrbSynthesizer), "synthesisProgress");
        // RimWorld 1.7 reads the screen back at the end of the frame and encodes the PNG on a worker thread; a capture is
        // taken once its file has left this list. Unity (1.6) writes the file at the end of the frame.
        private static readonly IList pendingCaptures = FindPendingCaptures();

        private readonly Map map;
        private readonly List<Thing> spawned = new List<Thing>();
        private string root;
        private IntVec3 center;
        private Faction enemyFaction;
        private PawnKindDef enemyKind;
        private Vector3 cameraFocus;
        private float cameraSize;
        private float shake;

        public int Failures { get; private set; }

        public EffectRecorder(Map map)
        {
            this.map = map;
        }

        private static IList FindPendingCaptures()
        {
            Type type = AccessTools.TypeByName("UnityEngine.ScreenCapture");
            FieldInfo field = type == null ? null : AccessTools.Field(type, "PendingFiles");
            return field?.GetValue(null) as IList;
        }

        public static bool FrameCaptured(Frame frame)
        {
            if (pendingCaptures == null)
            {
                return File.Exists(frame.path);
            }
            return pendingCaptures.Count == 0 && (frame.waitFor == null || File.Exists(frame.waitFor));
        }

        private static bool SkipPawnOverlay() => false;

        // ---------------------------------------------------------------------------------------------------------

        public IEnumerable<object> Run()
        {
            root = Path.Combine(GenFilePaths.ScreenshotFolderPath, FolderName);
            JusticiarValidation.Line("== Effect recording to " + root);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
            Directory.CreateDirectory(root);
            new Harmony("Mortis.JusticiarValor.Recorder").Patch(AccessTools.Method(typeof(PawnUIOverlay), "DrawPawnGUIOverlay"),
                prefix: new HarmonyMethod(typeof(EffectRecorder), nameof(SkipPawnOverlay)));
            DebugSettings.enableStoryteller = false;
            DebugSettings.enableRandomMentalStates = false;
            DebugSettings.enableRandomDiseases = false;
            Find.CameraDriver.config.sizeRange = new FloatRange(3f, 60f);
            Find.UIRoot.screenshotMode.Active = true;
            map.weatherManager.curWeather = WeatherDefOf.Clear;
            map.weatherManager.lastWeather = WeatherDefOf.Clear;
            map.fogGrid.ClearAllFog();
            int hour = GenLocalDate.HourOfDay(map);
            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + (11 - hour + 24) % 24 * GenDate.TicksPerHour);

            enemyFaction = Find.FactionManager.FirstFactionOfDef(FactionDefOf.Pirate);
            if (enemyFaction == null || !enemyFaction.HostileTo(Faction.OfPlayer))
            {
                enemyFaction = Find.FactionManager.AllFactions.FirstOrDefault(f => !f.IsPlayer && f.def.humanlikeFaction && f.HostileTo(Faction.OfPlayer) && !f.Hidden && !f.temporary)
                    ?? Find.FactionManager.AllFactions.First(f => f.HostileTo(Faction.OfPlayer));
            }
            List<PawnKindDef> kinds = enemyFaction.def.pawnGroupMakers?.Where(m => m.kindDef == PawnGroupKindDefOf.Combat).SelectMany(m => m.options)
                .Select(o => o.kind).Where(k => k.RaceProps.Humanlike).Distinct().ToList() ?? new List<PawnKindDef>();
            enemyKind = kinds.FirstOrDefault(k => k.defName == "Mercenary_Gunner") ?? kinds.OrderBy(k => Math.Abs(k.combatPower - 85f)).FirstOrDefault()
                ?? PawnKindDefOf.Colonist;
            center = FindOpenArea(map.Center, 16, 10);
            JusticiarValidation.Line("Scene centre " + center + ", enemies: " + enemyKind.defName + " of " + enemyFaction.Name);
            PrepareStage();

            var clips = new (string name, Func<IEnumerable<object>> scene)[]
            {
                ("JusticiarValorWave", JusticiarValorWave), ("ThreatPulse", ThreatPulse), ("PsychicShield", PsychicShield), ("Invictus", Invictus),
                ("PsyOrbs", PsyOrbs), ("OrbImpact", OrbImpact), ("CelestialPierce", CelestialPierce), ("WarlockbaneDeflect", WarlockbaneDeflect),
                ("SermonAndConversion", SermonAndConversion), ("EltexGlow", EltexGlow)
            };
            // -jvrecordonly=<scene,scene>: just those scenes.
            HashSet<string> only = GenCommandLine.TryGetCommandLineArg("jvrecordonly", out string onlyArg) && !string.IsNullOrEmpty(onlyArg)
                ? new HashSet<string>(onlyArg.Split(','))
                : null;
            foreach (var clip in clips.Where(c => only == null || only.Contains(c.name)))
            {
                foreach (object step in clip.scene())
                {
                    yield return step;
                }
            }
            ClearScene();
        }

        // ---------------------------------------------------------------------------------------------------------
        // Clips

        private IEnumerable<object> JusticiarValorWave()
        {
            NewScene();
            Pawn justiciar = Justiciar(center, drafted: true);
            foreach (IntVec3 offset in new[] { new IntVec3(2, 0, 1), new IntVec3(-2, 0, 1), new IntVec3(2, 0, -2), new IntVec3(-2, 0, -2) })
            {
                Hostile(center + offset).rotationTracker.FaceCell(justiciar.Position);
            }
            Ability valor = justiciar.abilities.GetAbility(MJV_DefOf.MJV_JusticiarValor, true);
            Camera(center, 6f);
            yield return 10;
            foreach (object step in Clip("01_JusticiarValor_PsychicWave", 190, f =>
            {
                if (f == 25)
                {
                    Cast(justiciar, valor, justiciar);
                }
                return true;
            }))
            {
                yield return step;
            }
        }

        // One more hostile sets its sights on the undrafted wearer every second: each new threat sets off a scan pulse,
        // stronger with every one.
        private IEnumerable<object> ThreatPulse()
        {
            NewScene();
            Pawn justiciar = Justiciar(center, drafted: false);
            justiciar.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
            justiciar.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 100000), JobCondition.InterruptForced);
            var hostiles = new List<Pawn>();
            foreach (IntVec3 offset in new[] { new IntVec3(-5, 0, 2), new IntVec3(5, 0, -1), new IntVec3(-3, 0, -3), new IntVec3(3, 0, 3) })
            {
                Pawn hostile = Hostile(center + offset);
                hostile.rotationTracker.FaceCell(justiciar.Position);
                hostiles.Add(hostile);
            }
            Camera(center, 4.2f);
            yield return 10;
            foreach (object step in Clip("02_ThreatSense_Pulse", 290, f =>
            {
                int active = f < 20 ? 0 : Math.Min(hostiles.Count, 1 + (f - 20) / 60);
                for (int i = 0; i < active; i++)
                {
                    hostiles[i].mindState.enemyTarget = justiciar;
                }
                return true;
            }))
            {
                yield return step;
            }
        }

        // Separate the dome, directional hits, break and reset so every effect has its own preview.
        // Damage goes through the normal pawn damage path; only the reset wait is skipped for the recovery shot.
        private IEnumerable<object> PsychicShield()
        {
            NewScene();
            Pawn justiciar = Justiciar(center, drafted: true);
            CompPsychicShield shield = justiciar.WornComp<CompPsychicShield>();
            Camera(center, 3.5f);
            yield return 10;
            foreach (object step in Clip("03_PsychicShield_Idle", 240, null))
            {
                yield return step;
            }
            float beforeHits = shield.Energy;
            foreach (object step in Clip("04_PsychicShield_Hit", 270, f =>
            {
                if (f >= 30 && (f - 30) % 60 == 0 && f <= 210)
                {
                    float angle = (f - 30) / 60 * 90f;
                    justiciar.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 12f, 0f, angle));
                }
                return true;
            }))
            {
                yield return step;
            }
            Expect(shield.Energy < beforeHits && !shield.Resetting, "shield absorbed the four directional hits");
            foreach (object step in Clip("05_PsychicShield_Break", 210, f =>
            {
                if (f == 45)
                {
                    justiciar.TakeDamage(new DamageInfo(DamageDefOf.Bullet, shield.Energy + 10f, 0f, 180f));
                }
                return true;
            }))
            {
                yield return step;
            }
            Expect(shield.Resetting, "shield broke and is waiting to recover");
            // Leave 45 ticks before the real component reset; preserve its ordinary reset glow and energy recovery.
            resetField.SetValue(shield, 45);
            foreach (object step in Clip("06_PsychicShield_Recharge", 240, null))
            {
                yield return step;
            }
            Expect(!shield.Resetting && shield.Energy > 0f, "shield reset and resumed recharging");
        }

        private IEnumerable<object> Invictus()
        {
            NewScene();
            Pawn justiciar = Justiciar(center, drafted: true);
            chargeField.SetValue(justiciar.WornComp<CompPsychicShield>(), 120f);
            Ability invictus = justiciar.abilities.GetAbility(MJV_DefOf.MJV_Invictus, true);
            Camera(center, 4.5f);
            yield return 10;
            foreach (object step in Clip("07_Invictus_Burst", 180, f =>
            {
                if (f == 30)
                {
                    Cast(justiciar, invictus, justiciar);
                }
                return true;
            }))
            {
                yield return step;
            }
            Expect(justiciar.WornComp<CompPsychicShield>().Overdriven, "Invictus release activated overdrive");
            Camera(center, 5.5f);
            yield return 10;
            foreach (object step in Clip("08_Invictus_Afterimages", 300, f =>
            {
                if (f == 15)
                {
                    Goto(justiciar, center + new IntVec3(5, 0, 1));
                }
                else if (f == 100)
                {
                    Goto(justiciar, center + new IntVec3(-5, 0, -1));
                }
                else if (f == 215)
                {
                    Goto(justiciar, center + new IntVec3(4, 0, 2));
                }
                return true;
            }))
            {
                yield return step;
            }
        }

        // Three justiciars facing south, east and north: the eltex inlays of their armor and helmet light up as the stored
        // Invictus charge and the sensed murderous intent rise together over two seconds, hold, then fade as both drain.
        // Undrafted and waiting, so the shield's dome stays down and the inlays show plainly.
        private IEnumerable<object> EltexGlow()
        {
            NewScene();
            var pawns = new List<(Pawn pawn, Rot4 facing)>();
            foreach ((int dx, Rot4 facing) in new[] { (-2, Rot4.South), (0, Rot4.East), (2, Rot4.North) })
            {
                Pawn justiciar = Justiciar(center + new IntVec3(dx, 0, 0), drafted: false);
                justiciar.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
                justiciar.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 100000), JobCondition.InterruptForced);
                pawns.Add((justiciar, facing));
            }
            Camera(center, 3.2f);
            yield return 10;
            foreach (object step in Clip("14_Eltex_Glow", 320, f =>
            {
                float k = f < 20 ? 0f : f < 140 ? (f - 20) / 120f : f < 200 ? 1f : Math.Max(0f, 1f - (f - 200) / 100f);
                foreach ((Pawn pawn, Rot4 facing) in pawns)
                {
                    pawn.Rotation = facing;
                    CompPsychicShield shield = pawn.WornComp<CompPsychicShield>();
                    chargeField.SetValue(shield, shield.Props.chargeMax * k);
                    CompThreatSense sense = pawn.WornComp<CompThreatSense>();
                    Hediff intent = pawn.health.hediffSet.GetFirstHediffOfDef(sense.Props.hediff);
                    float severity = sense.Props.maxThreats * k;
                    if (severity > 0.05f)
                    {
                        if (intent == null)
                        {
                            intent = HediffMaker.MakeHediff(sense.Props.hediff, pawn);
                            intent.Severity = severity;
                            pawn.health.AddHediff(intent);
                        }
                        intent.Severity = severity;
                    }
                    else if (intent != null)
                    {
                        pawn.health.RemoveHediff(intent);
                    }
                }
                return true;
            }))
            {
                yield return step;
            }
        }

        // A close-up of orbit/synthesis, followed by a separate firing shot.
        private IEnumerable<object> PsyOrbs()
        {
            NewScene();
            Pawn spearman = Colonist(center, drafted: true);
            Wield(spearman, MJV_DefOf.MJV_Weapon_EnergyPsySpear);
            var synthesizer = spearman.equipment.Primary.TryGetComp<CompPsyOrbSynthesizer>();
            synthesizer.AutoLaunch = true;
            orbsField.SetValue(synthesizer, synthesizer.OrbProps.maxOrbs - 1);
            synthesisField.SetValue(synthesizer, (int)(synthesizer.OrbProps.synthesisTicks * 0.4f));
            Camera(center, 3.5f);
            yield return 10;
            foreach (object step in Clip("09_PsyOrb_Orbit_Synthesis", 300, null))
            {
                yield return step;
            }
            Expect(synthesizer.Orbs == synthesizer.OrbProps.maxOrbs, "new orb formed in its vacant orbital slot");

            NewScene();
            spearman = Colonist(center + new IntVec3(-5, 0, 0), drafted: true);
            Wield(spearman, MJV_DefOf.MJV_Weapon_EnergyPsySpear);
            synthesizer = spearman.equipment.Primary.TryGetComp<CompPsyOrbSynthesizer>();
            synthesizer.AutoLaunch = false;
            orbsField.SetValue(synthesizer, 3);
            Pawn target = Hostile(center + new IntVec3(5, 0, 1));
            target.rotationTracker.FaceCell(spearman.Position);
            Camera(center, 5f);
            yield return 10;
            foreach (object step in Clip("10_PsyOrb_Flight", 300, f =>
            {
                if (f == 30)
                {
                    synthesizer.AutoLaunch = true;
                }
                return true;
            }))
            {
                yield return step;
            }
            Expect(synthesizer.Orbs < 3, "psy-orbs launched at the target");
        }

        // Replay the runtime impact animation at a fixed point so its short lifetime can be inspected clearly.
        private IEnumerable<object> OrbImpact()
        {
            NewScene();
            Camera(center, 3.5f);
            yield return 10;
            foreach (object step in Clip("11_PsyOrb_Impact", 240, f =>
            {
                if (f == 30 || f == 100 || f == 170)
                {
                    MapComponent_JusticiarFx.Spawn(map, new OrbImpactFx(center.ToVector3Shifted()));
                }
                return true;
            }))
            {
                yield return step;
            }
        }

        private IEnumerable<object> CelestialPierce()
        {
            NewScene();
            Pawn bladesman = Colonist(center + new IntVec3(-2, 0, 0), drafted: true);
            Wield(bladesman, MJV_DefOf.MJV_MeleeWeapon_EnergyPsyblade);
            // Not adjacent: a drafted bladesman would cut at it first.
            Pawn near = Hostile(center + new IntVec3(0, 0, 0));
            Pawn far = Hostile(center + new IntVec3(1, 0, 0));
            near.rotationTracker.FaceCell(bladesman.Position);
            far.rotationTracker.FaceCell(bladesman.Position);
            Ability pierce = bladesman.abilities.GetAbility(MJV_DefOf.MJV_CelestialPierce, true);
            Camera(center, 4.5f);
            yield return 10;
            foreach (object step in Clip("12_CelestialPierce", 230, f =>
            {
                if (f == 20)
                {
                    Cast(bladesman, pierce, far);
                }
                return true;
            }))
            {
                yield return step;
            }

            // Verify the weapon texture points along the thrust in every cardinal direction.
            NewScene();
            bladesman = Colonist(center, drafted: true);
            Wield(bladesman, MJV_DefOf.MJV_MeleeWeapon_EnergyPsyblade);
            pierce = bladesman.abilities.GetAbility(MJV_DefOf.MJV_CelestialPierce, true);
            var directions = new[] { IntVec3.North, IntVec3.East, IntVec3.South, IntVec3.West };
            var targets = new Pawn[directions.Length];
            for (int i = 0; i < targets.Length; i++)
                targets[i] = Hostile(center + directions[i] * 3);
            Camera(center, 4.5f);
            yield return 10;
            foreach (object step in Clip("12b_CelestialPierce_Directions", 420, f =>
            {
                if (f >= 20 && (f - 20) % 95 == 0)
                {
                    int i = (f - 20) / 95;
                    if (i < targets.Length)
                    {
                        bladesman.rotationTracker.FaceCell(targets[i].Position);
                        pierce.Activate(new LocalTargetInfo(targets[i]), new LocalTargetInfo(targets[i]));
                    }
                }
                return true;
            }))
            {
                yield return step;
            }
        }

        // A hostile psycaster stuns the justiciar; the warlockbane shield turns the psycast back on him.
        private IEnumerable<object> WarlockbaneDeflect()
        {
            NewScene();
            Pawn justiciar = Justiciar(center + new IntVec3(-2, 0, 0), drafted: true);
            // Without the helmet: its threat pulse (the warlock takes aim) would play over the deflection being shown.
            foreach (Apparel helmet in justiciar.apparel.WornApparel.Where(a => a.def == MJV_DefOf.MJV_Apparel_ArmorHelmetPromachosHelmet).ToList())
            {
                justiciar.apparel.Remove(helmet);
                helmet.Destroy();
            }
            Pawn warlock = Hostile(center + new IntVec3(4, 0, 1));
            PawnUtility.ChangePsylinkLevel(warlock, 1, false);
            AbilityDef stunDef = DefDatabase<AbilityDef>.GetNamed("Stun");
            warlock.abilities.GainAbility(stunDef);
            Ability stun = warlock.abilities.GetAbility(stunDef);
            warlock.psychicEntropy.OffsetPsyfocusDirectly(1f);
            warlock.rotationTracker.FaceCell(justiciar.Position);
            Camera(center + new IntVec3(1, 0, 0), 5f);
            yield return 10;
            foreach (object step in Clip("13_Warlockbane_Deflect", 240, f =>
            {
                if (f == 15)
                {
                    warlock.stances.stunner.StopStun();
                    warlock.jobs.StartJob(stun.GetJob(justiciar, justiciar), JobCondition.InterruptForced);
                }
                return true;
            }))
            {
                yield return step;
            }
        }

        // A short sermon by a justiciar to four colonists: the halo while he preaches, then its end, where the two
        // listeners whose faith was already shaken convert.
        private IEnumerable<object> SermonAndConversion()
        {
            NewScene();
            Faction empire = Find.FactionManager.OfEmpire;
            if (!ModsConfig.IdeologyActive || empire == null || ImperialFaith.Ideo == null)
            {
                Expect(false, "sermon recording requires Ideology and an imperial faith");
                yield break;
            }
            IntVec3 spot = center + new IntVec3(0, 0, 2);
            Pawn missionary = PawnGenerator.GeneratePawn(new PawnGenerationRequest(MJV_DefOf.MJV_Justiciar, empire, forceGenerateNewPawn: true,
                canGeneratePawnRelations: false));
            missionary.SetFaction(Faction.OfPlayer);
            ImperialFaith.Ordain(missionary);
            GenSpawn.Spawn(missionary, spot, map);
            spawned.Add(missionary);
            Rested(missionary);
            var listeners = new List<Pawn>();
            foreach (IntVec3 offset in new[] { new IntVec3(-2, 0, -3), new IntVec3(0, 0, -4), new IntVec3(2, 0, -3), new IntVec3(-1, 0, -5) })
            {
                bool shaken = listeners.Count % 2 == 0;
                Pawn listener = Colonist(spot + offset, drafted: false, beforeSpawn: p =>
                {
                    if (shaken)
                    {
                        p.ideo.OffsetCertainty(-(p.ideo.Certainty - 0.01f));
                    }
                });
                Rested(listener);
                listeners.Add(listener);
            }
            // Long enough for the listeners called to it to walk over and hear most of it.
            var settings = new SermonSettings { durationTicks = new IntRange(1200, 1200) };
            LordJob_MJV_Sermon sermon = SermonUtility.StartSermon(missionary, spot, settings, listeners, 1, listeners.Count);
            Camera(spot + new IntVec3(0, 0, -2), 5.5f);
            yield return 10;
            foreach (object step in Clip("14_Sermon_Halo", 300, null))
            {
                yield return step;
            }
            // On to a second before the end of the sermon.
            for (int i = 0; i < 3000 && sermon.lord != null && sermon.TicksLeft > 60; i++)
            {
                Find.TickManager.DoSingleTick();
            }
            SnapTweens();
            if (sermon.lord == null)
            {
                Failures++;
                JusticiarValidation.Line("FAIL the sermon ended before its conversion clip");
                yield break;
            }
            // Preserve the staged premise despite certainty recovery while the sermon was fast-forwarded.
            for (int i = 0; i < listeners.Count; i += 2)
                listeners[i].ideo.OffsetCertainty(-(listeners[i].ideo.Certainty - 0.0001f));
            yield return 5;
            foreach (object step in Clip("15_Conversion", 240, null))
            {
                yield return step;
            }
            int converts = listeners.Count(ImperialFaith.IsBeliever);
            JusticiarValidation.Line((converts > 0 ? "" : "FAIL ") + "Sermon converts: " + converts + " of " + listeners.Count);
            if (converts == 0)
            {
                Failures++;
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // Recording

        private void Expect(bool condition, string message)
        {
            JusticiarValidation.Line((condition ? "PASS " : "FAIL ") + message);
            if (!condition) Failures++;
        }

        private static string FrameName(int index) => "f" + index.ToString("0000");

        private IEnumerable<object> Clip(string name, int maxFrames, Func<int, bool> script)
        {
            string dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            int frames = 0;
            var observed = new HashSet<string>();
            for (int f = 0; f < maxFrames; f++)
            {
                if (script != null && !script(f))
                {
                    break;
                }
                Find.TickManager.DoSingleTick();
                foreach (object effect in (IList)effectsField.GetValue(MapComponent_JusticiarFx.Of(map)))
                    observed.Add(effect.GetType().Name);
                SnapTweens();
                ApplyCamera();
                yield return new Frame
                {
                    fileName = Path.Combine(FolderName, name, FrameName(f)),
                    path = Path.Combine(dir, FrameName(f) + ".png"),
                    waitFor = f >= Backlog ? Path.Combine(dir, FrameName(f - Backlog) + ".png") : null
                };
                frames++;
            }
            yield return (Func<bool>)(() => Enumerable.Range(0, frames).All(i => File.Exists(Path.Combine(dir, FrameName(i) + ".png"))));
            // The last files may still be being written.
            yield return 30;
            File.WriteAllText(Path.Combine(root, name + ".done"), frames.ToString());
            JusticiarValidation.Line("Clip " + name + ": " + frames + " frames, " + (frames / 60f).ToString("0.00") + " s");
            if (observed.Count > 0) JusticiarValidation.Line("Observed " + name + ": " + string.Join(", ", observed.OrderBy(n => n)));
        }

        // Pawns are drawn at a tweened position that only follows them while the game runs; snap it to where they are,
        // so that they move tick by tick.
        private void SnapTweens()
        {
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                pawn.Drawer?.tweener?.ResetTweenedPosToRoot();
            }
        }

        // Holds the camera on the shot. The game only shakes the camera while it runs (CameraShaker.ShakeOffset is zero
        // when paused, and decays in real time): the same shake is replayed here in game time.
        private void ApplyCamera()
        {
            CameraShaker shaker = Find.CameraDriver.shaker;
            shake = Math.Max(shake, shaker.CurShakeMag);
            shaker.StopAllShaking();
            float t = Find.TickManager.TicksGame / 60f;
            var offset = new Vector3((float)Math.Sin(t * 24f) * shake, 0f, (float)Math.Sin(t * 24f * 1.1f) * shake);
            Find.CameraDriver.SetRootPosAndSize(cameraFocus + offset, cameraSize);
            shake = Math.Max(0f, shake - 0.5f / 60f);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Staging

        private void Camera(IntVec3 focus, float size)
        {
            cameraFocus = focus.ToVector3Shifted();
            cameraSize = size;
            shake = 0f;
            Find.CameraDriver.shaker.StopAllShaking();
            Find.CameraDriver.SetRootPosAndSize(cameraFocus, cameraSize);
        }

        // The way the player orders a cast, without the confirmation dialogs.
        private static void Cast(Pawn caster, Ability ability, LocalTargetInfo target)
        {
            if (ability.verb.verbProps.nonInterruptingSelfCast)
            {
                ability.verb.TryStartCastOn(ability.verb.Caster);
                return;
            }
            caster.jobs.TryTakeOrderedJob(ability.GetJob(target, target), JobTag.Misc);
        }

        private static void Goto(Pawn pawn, IntVec3 cell)
        {
            pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, cell), JobTag.Misc);
        }

        // Stunned without the stun overlay: stands still and does nothing.
        private static void Freeze(Pawn pawn) => pawn.stances.stunner.StunFor(100000, null, false, false, false);

        private static void Rested(Pawn pawn)
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
                pawn.needs.joy.CurLevelPercentage = 0.5f;
            }
            if (pawn.timetable != null)
            {
                for (int hour = 0; hour < 24; hour++)
                {
                    pawn.timetable.SetAssignment(hour, TimeAssignmentDefOf.Anything);
                }
            }
        }

        private Pawn Colonist(IntVec3 cell, bool drafted, Action<Pawn> beforeSpawn = null)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true,
                mustBeCapableOfViolence: true, canGeneratePawnRelations: false));
            pawn.equipment?.DestroyAllEquipment();
            beforeSpawn?.Invoke(pawn);
            GenSpawn.Spawn(pawn, cell, map);
            if (pawn.drafter != null)
            {
                pawn.drafter.Drafted = drafted;
            }
            spawned.Add(pawn);
            return pawn;
        }

        private Pawn Justiciar(IntVec3 cell, bool drafted)
        {
            Pawn pawn = Colonist(cell, drafted);
            Wear(pawn, MJV_DefOf.MJV_Apparel_ArmorHelmetPromachosHelmet);
            Wear(pawn, MJV_DefOf.MJV_Apparel_PromachosArmor);
            Wear(pawn, MJV_DefOf.MJV_Apparel_WarlockbaneShield);
            Wield(pawn, MJV_DefOf.MJV_MeleeWeapon_EnergyPsyblade);
            return pawn;
        }

        // An enemy, frozen until a clip sets it going; no shield belt to get in the way of the effects.
        private Pawn Hostile(IntVec3 cell, ThingDef weapon = null)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(enemyKind, enemyFaction, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            pawn.equipment?.DestroyAllEquipment();
            foreach (Apparel apparel in pawn.apparel?.WornApparel.Where(a => a.TryGetComp<CompShield>() != null).ToList() ?? new List<Apparel>())
            {
                pawn.apparel.Remove(apparel);
                apparel.Destroy();
            }
            if (weapon != null)
            {
                pawn.equipment.AddEquipment(Make<ThingWithComps>(weapon));
            }
            GenSpawn.Spawn(pawn, cell, map);
            Freeze(pawn);
            spawned.Add(pawn);
            return pawn;
        }

        private static T Make<T>(ThingDef def) where T : Thing => (T)ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);

        private static void Wear(Pawn pawn, ThingDef def) => pawn.apparel.Wear(Make<Apparel>(def), false);

        private static void Wield(Pawn pawn, ThingDef def)
        {
            pawn.equipment.DestroyAllEquipment();
            pawn.equipment.AddEquipment(Make<ThingWithComps>(def));
        }

        // Clears what the previous clip left and lets its flecks fade before the next is staged.
        private void NewScene()
        {
            ClearScene();
            for (int i = 0; i < 180; i++)
            {
                Find.TickManager.DoSingleTick();
            }
            ClearScene();
        }

        private void ClearScene()
        {
            foreach (Lord lord in map.lordManager.lords.Where(l => l.ownedPawns.Any(p => spawned.Contains(p))).ToList())
            {
                map.lordManager.RemoveLord(lord);
            }
            foreach (Thing thing in spawned)
            {
                if (!thing.Destroyed && !(thing is Pawn pawn && pawn.Dead))
                {
                    thing.Destroy();
                }
            }
            spawned.Clear();
            var leftovers = new List<Thing>();
            for (int i = 0; i < map.spawnedThings.Count; i++)
            {
                Thing t = map.spawnedThings[i];
                if (t.Position.InHorDistOf(center, 30f) && (t is PawnFlyer || t is Projectile || t is Filth || t is Corpse || t is Mote
                    || t.def.category == ThingCategory.Item))
                {
                    leftovers.Add(t);
                }
            }
            foreach (Thing thing in leftovers)
            {
                if (!thing.Destroyed)
                {
                    thing.Destroy();
                }
            }
            ((IList)effectsField.GetValue(MapComponent_JusticiarFx.Of(map))).Clear();
            ClearMap();
        }

        // Only the actors on stage: everyone else not of the colony leaves the map, the colony's own are moved out of
        // the shot and frozen.
        private void ClearMap()
        {
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (spawned.Contains(pawn))
                {
                    continue;
                }
                if (pawn.Faction != Faction.OfPlayer)
                {
                    pawn.Destroy();
                    continue;
                }
                if (pawn.Position.InHorDistOf(center, 30f))
                {
                    pawn.Position = CellFinder.RandomClosewalkCellNear(center + new IntVec3(0, 0, center.z > map.Size.z / 2 ? -40 : 40), map, 6);
                    pawn.Notify_Teleported();
                }
                if (!pawn.stances.stunner.Stunned)
                {
                    pawn.jobs?.StopAll();
                    Freeze(pawn);
                }
            }
        }

        // No trees, items or snow on the stage.
        private void PrepareStage()
        {
            ValidationScene.PrepareEffectStage(map, center, 18, 12);
            ClearMap();
        }

        private IntVec3 FindOpenArea(IntVec3 near, int halfWidth, int halfHeight)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(near, 50f, true))
            {
                var rect = CellRect.CenteredOn(cell, halfWidth, halfHeight);
                if (!rect.InBounds(map))
                {
                    continue;
                }
                if (rect.Cells.All(c => c.Standable(map) && c.GetEdifice(map) == null && !c.Roofed(map) && c.GetFirstPawn(map) == null
                    && !c.GetTerrain(map).IsWater))
                {
                    return cell;
                }
            }
            return near;
        }
    }
}
