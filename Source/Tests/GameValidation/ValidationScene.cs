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
using Vector3 = UnityEngine.Vector3;

namespace JusticiarValorValidation
{
    // The checks and the screenshot scene. Each step that needs frames to pass yields: an int (frames to wait), a
    // Func<bool> (wait until true) or a string (take a screenshot of that name and wait until it is written).
    public sealed class ValidationScene
    {
        private static readonly FieldInfo effectsField = AccessTools.Field(typeof(MapComponent_JusticiarFx), "effects");
        private static readonly FieldInfo orbsField = AccessTools.Field(typeof(CompPsyOrbSynthesizer), "orbs");
        private static readonly FieldInfo chargeField = AccessTools.Field(typeof(CompPsychicShield), "charge");
        private static readonly FieldInfo energyField = AccessTools.Field(typeof(CompPsychicShield), "energy");
        private static readonly FieldInfo lastDamageField = AccessTools.Field(typeof(CompPsychicShield), "lastDamageTick");
        private static readonly FieldInfo wlChargesField = AccessTools.Field(typeof(CompWarlockbane), "charges");
        private static int justiciarId, spearmanId, bladesmanId;

        public static IEnumerable<int> TestPawnIds => new[] { justiciarId, spearmanId, bladesmanId };
        private static float savedCharge;
        private static int savedOrbs, savedWarlockbaneCharges;
        private static Map savedMap;

        private readonly Map map;
        private IntVec3 center;
        private readonly List<Pawn> extras = new List<Pawn>();
        public int Failures { get; private set; }

        public ValidationScene(Map map)
        {
            this.map = map;
        }

        private void Check(bool value, string message)
        {
            if (!value)
            {
                Failures++;
            }
            JusticiarValidation.Line((value ? "PASS " : "FAIL ") + message);
        }

        private static void Ticks(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Find.TickManager.DoSingleTick();
            }
        }

        private MapComponent_JusticiarFx Fx => MapComponent_JusticiarFx.Of(map);

        private int EffectCount(string typeName) => Effects(typeName).Count();

        private IEnumerable<object> Effects(string typeName)
        {
            var effects = (IList)effectsField.GetValue(Fx);
            return effects.Cast<object>().Where(e => e.GetType().Name == typeName).ToList();
        }

        // ---------------------------------------------------------------------------------------------------------
        // Scene

        private void SetupScene()
        {
            map.weatherManager.curWeather = WeatherDefOf.Clear;
            map.weatherManager.lastWeather = WeatherDefOf.Clear;
            int hour = GenLocalDate.HourOfDay(map);
            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + (12 - hour + 24) % 24 * GenDate.TicksPerHour);
            map.fogGrid.ClearAllFog();
            Find.UIRoot.screenshotMode.Active = true;
        }

        internal static void PrepareEffectStage(Map map, IntVec3 center, int halfWidth, int halfHeight)
        {
            // Only the disposable validation colony: random rivers/walls must not block test thrusts
            // or obscure visual comparisons when FindOpenArea has to fall back to its starting cell.
            foreach (IntVec3 cell in CellRect.CenteredOn(center, halfWidth, halfHeight).ClipInsideMap(map))
            {
                map.roofGrid.SetRoof(cell, null);
                foreach (Thing thing in cell.GetThingList(map).ToList())
                {
                    // Steam geysers and the like cannot be destroyed (the game logs an error); DeSpawn clears them.
                    if (thing is Building || thing is Plant || thing is Filth || thing.def.category == ThingCategory.Item)
                    {
                        if (thing.def.destroyable)
                            thing.Destroy();
                        else
                            thing.DeSpawn();
                    }
                }
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                map.snowGrid.SetDepth(cell, 0f);
            }
        }

        private IntVec3 FindOpenArea(IntVec3 near, int halfWidth, int halfHeight)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(near, 40f, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                var rect = CellRect.CenteredOn(cell, halfWidth, halfHeight);
                if (!rect.InBounds(map))
                {
                    continue;
                }
                if (rect.Cells.All(c => c.Standable(map) && c.GetEdifice(map) == null && !c.Roofed(map) && c.GetFirstPawn(map) == null))
                {
                    return cell;
                }
            }
            return near;
        }

        private Pawn MakeColonist(IntVec3 cell)
        {
            var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true,
                mustBeCapableOfViolence: true, canGeneratePawnRelations: false);
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            pawn.equipment?.DestroyAllEquipment();
            GenSpawn.Spawn(pawn, cell, map);
            pawn.drafter.Drafted = true;
            return pawn;
        }

        private Pawn MakeHostile(IntVec3 cell, bool stun = true)
        {
            Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(f => !f.IsPlayer && f.def.humanlikeFaction && f.HostileTo(Faction.OfPlayer) && !f.Hidden)
                ?? Find.FactionManager.AllFactions.First(f => f.HostileTo(Faction.OfPlayer));
            return MakePawn(cell, faction, stun);
        }

        // A pawn of the faction for a check (removed by ClearExtras), stunned in place unless stun is false.
        private Pawn MakePawn(IntVec3 cell, Faction faction, bool stun = true)
        {
            var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, faction, forceGenerateNewPawn: true, canGeneratePawnRelations: false);
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            pawn.equipment?.DestroyAllEquipment();
            // Damage assertions use unarmored targets; randomly generated apparel can legitimately
            // stop a hit and would otherwise make an effect regression depend on armor RNG.
            foreach (Apparel apparel in pawn.apparel.WornApparel.ToList())
            {
                pawn.apparel.Remove(apparel);
                apparel.Destroy();
            }
            GenSpawn.Spawn(pawn, cell, map);
            if (stun)
            {
                pawn.stances.stunner.StunFor(100000, null, false, false, false);
            }
            extras.Add(pawn);
            return pawn;
        }

        // A mechanoid of the mechanoid faction (removed by ClearExtras), stunned in place; null without that kind or faction.
        private Pawn MakeMech(string kindName, IntVec3 cell)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(kindName);
            if (kind == null || Faction.OfMechanoids == null)
            {
                return null;
            }
            Pawn mech = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, Faction.OfMechanoids, forceGenerateNewPawn: true));
            GenSpawn.Spawn(mech, cell, map);
            mech.stances.stunner.StunFor(100000, null, false, false, false);
            extras.Add(mech);
            return mech;
        }

        // Hits the pawn's legs until its Moving capacity is down to about `moving`, as a fight would.
        private static void WoundLegs(Pawn pawn, float moving)
        {
            List<BodyPartRecord> legs = pawn.health.hediffSet.GetNotMissingParts()
                .Where(part => part.def.tags.Contains(BodyPartTagDefOf.MovingLimbCore)).ToList();
            for (int n = 0; n < 200 && legs.Count > 0 && !pawn.Dead && pawn.health.capacities.GetLevel(PawnCapacityDefOf.Moving) > moving; n++)
            {
                pawn.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 1f, 99f, -1f, null, legs[n % legs.Count]));
            }
        }

        private void ClearExtras()
        {
            foreach (Pawn pawn in extras)
            {
                if (!pawn.Destroyed)
                {
                    pawn.Destroy();
                }
            }
            extras.Clear();
            foreach (Thing flyer in map.listerThings.ThingsOfDef(MJV_DefOf.MJV_PawnFlyer_Knockback).ToList())
            {
                flyer.Destroy();
            }
        }

        // Only the check's own pawns stand within reach: colonists are moved away, anyone else is removed.
        private void ClearStrays(IntVec3 around, float radius, IEnumerable<Pawn> keep)
        {
            var kept = new HashSet<Pawn>(keep);
            foreach (Pawn stray in GenRadial.RadialDistinctThingsAround(around, map, radius, true).OfType<Pawn>().Where(p => !kept.Contains(p)).ToList())
            {
                if (stray.IsColonist)
                {
                    stray.Position = CellFinder.RandomClosewalkCellNear(around + new IntVec3(0, 0, 16), map, 4);
                    stray.Notify_Teleported();
                }
                else
                {
                    stray.Destroy();
                }
            }
        }

        private static T Make<T>(ThingDef def) where T : Thing
        {
            return (T)ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
        }

        private static void Wear(Pawn pawn, ThingDef def) => pawn.apparel.Wear(Make<Apparel>(def), false);

        private static void Wield(Pawn pawn, ThingDef def)
        {
            pawn.equipment.DestroyAllEquipment();
            pawn.equipment.AddEquipment(Make<ThingWithComps>(def));
        }

        private static float Health(Pawn pawn) => pawn.health.summaryHealth.SummaryHealthPercent;

        private static int Injuries(Pawn pawn) => pawn.health.hediffSet.hediffs.Count(h => h is Hediff_Injury);

        // A pawn's injuries and what dealt them, to tell a stray hit from the effect under test.
        private static string Wounds(Pawn pawn)
        {
            List<Hediff_Injury> injuries = pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList();
            return injuries.Count == 0 ? "" : " (" + string.Join(", ", injuries.Select(h => h.def.defName + " " + h.Severity.ToString("0.#")
                + " from " + (h.sourceDef?.defName ?? "?"))) + ")";
        }

        // ---------------------------------------------------------------------------------------------------------
        // Logic

        public IEnumerable<object> Logic()
        {
            JusticiarValidation.Line("== Load and defs");
            LogMessage[] load = Log.Messages.Where(m => m.type != LogMessageType.Message).ToArray();
            foreach (LogMessage message in load.Where(m => m.text.Contains("Justiciar") || m.text.Contains("MJV_")))
            {
                JusticiarValidation.Line("LOAD " + message.type + " " + message.text.Split('\n')[0].Trim());
            }
            Check(!load.Any(m => m.text.Contains("Justiciar") || m.text.Contains("MJV_")), "no load-time warning or error mentions Justiciar Valor");
            Check(Fx != null, "the map has the Justiciar Valor effect component");
            var pilotoMaterials = new[] { FxAssets.PsychicWave, FxAssets.PsyOrb, FxAssets.AfterimageWake,
                FxAssets.ThreatLock, FxAssets.OrbGlitter, FxAssets.OrbPetals, FxAssets.OrbBurst, FxAssets.PierceShock, FxAssets.WaveShock,
                FxAssets.Overdrive };
            var sheetMaterials = new[] { FxAssets.ThreatPulse, FxAssets.OrbTrail, FxAssets.GroundFissure, FxAssets.SpriteUnder, FxAssets.SpriteOver };
            var allMaterials = pilotoMaterials.Concat(sheetMaterials)
                .Concat(new[] { FxAssets.PsychicShield, FxAssets.CelestialBlade, FxAssets.SermonHalo }).ToArray();
            Check(allMaterials.All(m => m != null), "every effect material resolved its shader");
            JusticiarValidation.Line("Shaders: " + string.Join(", ", allMaterials.Select(m => m?.shader?.name ?? "null")));
            Check(pilotoMaterials.All(m => m != null && m.mainTexture != null
                && m.mainTexture.width == 2048 && m.mainTexture.height == 2048),
                "all " + pilotoMaterials.Length + " active Piloto animations have their complete 64-frame atlases on the running engine");
            Check(FxAssets.Sheet != null && FxAssets.Sheet.width == 2048 && sheetMaterials.All(m => m != null && ReferenceEquals(m.mainTexture, FxAssets.Sheet)),
                "the composite effects sample the packed Piloto texture sheet");
            Check(!FxAssets.PsychicShield.HasProperty("_MainTex")
                && !FxAssets.SermonHalo.HasProperty("_MainTex"),
                "the original shield, sermon halo and conversion shaders use no animation atlas");

            // The starting colonists stay out of the checks: drafted, so they do not wander in, and holding their fire, or
            // their guns would shoot at the checks' foes.
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned.ToList())
            {
                colonist.drafter.Drafted = true;
                colonist.drafter.FireAtWill = false;
            }
            center = FindOpenArea(map.mapPawns.FreeColonistsSpawned.First().Position, 9, 7);
            PrepareEffectStage(map, center, 12, 9);
            JusticiarValidation.Line("Scene centre " + center);
            SetupScene();
            Find.CameraDriver.SetRootPosAndSize(center.ToVector3Shifted(), 13f);

            Pawn justiciar = MakeColonist(center);
            Pawn spearman = MakeColonist(center + new IntVec3(-6, 0, -4));
            Pawn bladesman = MakeColonist(center + new IntVec3(6, 0, -4));
            // The armor's shield follows the wearer's psychic sensitivity: a psychically deaf justiciar would have none.
            foreach (Trait trait in justiciar.story.traits.allTraits.Where(t => t.def.defName == "PsychicSensitivity").ToList())
            {
                justiciar.story.traits.RemoveTrait(trait);
            }
            Wear(justiciar, MJV_DefOf.MJV_Apparel_ArmorHelmetPromachosHelmet);
            Wear(justiciar, MJV_DefOf.MJV_Apparel_PromachosArmor);
            Wear(justiciar, MJV_DefOf.MJV_Apparel_WarlockbaneShield);
            Wield(justiciar, MJV_DefOf.MJV_MeleeWeapon_EnergyPsyblade);
            Wield(spearman, MJV_DefOf.MJV_Weapon_EnergyPsySpear);
            Wield(bladesman, MJV_DefOf.MJV_MeleeWeapon_EnergyPsyblade);
            justiciarId = justiciar.thingIDNumber;
            spearmanId = spearman.thingIDNumber;
            bladesmanId = bladesman.thingIDNumber;
            yield return 10;

            JusticiarValidation.Line("== Gear");
            Check(justiciar.apparel.WornApparel.Count(a => a.def == MJV_DefOf.MJV_Apparel_ArmorHelmetPromachosHelmet
                || a.def == MJV_DefOf.MJV_Apparel_PromachosArmor || a.def == MJV_DefOf.MJV_Apparel_WarlockbaneShield) == 3,
                "the justiciar wears the Promachos helmet, the Promachos armor and the warlockbane shield together");
            Ability valor = justiciar.abilities.GetAbility(MJV_DefOf.MJV_JusticiarValor, true);
            Ability invictus = justiciar.abilities.GetAbility(MJV_DefOf.MJV_Invictus, true);
            Ability pierce = bladesman.abilities.GetAbility(MJV_DefOf.MJV_CelestialPierce, true);
            Check(valor != null, "the Promachos helmet grants Justiciar Valor");
            Check(invictus != null, "the Promachos armor grants Invictus");
            Check(pierce != null, "the energy psyblade grants Celestial Pierce");
            var synthesizer = spearman.equipment.Primary.TryGetComp<CompPsyOrbSynthesizer>();
            Check(synthesizer != null && spearman.equipment.PrimaryEq == synthesizer, "the energy psy-spear's CompPsyOrbSynthesizer is its CompEquippable");
            Check(spearman.equipment.GetGizmos().OfType<Command_Toggle>().Any(), "the psy-spear shows its psy-orb toggle");
            CompPsychicShield shield = justiciar.WornComp<CompPsychicShield>();
            CompWarlockbane warlockbane = justiciar.WornComp<CompWarlockbane>();
            Check(shield != null && warlockbane != null, "the armor and the shield carry their comps");

            JusticiarValidation.Line("== Justiciar Valor");
            // The spear's orbs would join in on the wave's victims.
            synthesizer.AutoLaunch = false;
            Check(new[] { MJV_DefOf.MJV_PsychicDaze, MJV_DefOf.MJV_PsyOrbSlow }.All(h => h.stages.All(s => s.capMods.NullOrEmpty())),
                "the psychic daze and the psy-orb drag only slow: no capacity offset that could down a wounded pawn");
            var victims = new List<Pawn>
            {
                MakeHostile(center + new IntVec3(2, 0, 0)),
                MakeHostile(center + new IntVec3(-2, 0, 1)),
                MakeHostile(center + new IntVec3(0, 0, -2))
            };
            // Strays would change who the wave catches (a colony animal wandering by, a passing caravan).
            ClearStrays(center, 8f, victims.Concat(new[] { justiciar, spearman, bladesman }));
            var valorComp = valor.CompOfType<CompAbilityEffect_JusticiarValor>();
            bool aiWithFoesOnly = valorComp.AICanTargetNow(new LocalTargetInfo(justiciar));
            string caught = string.Join(", ", JusticiarValorWave.Victims(justiciar, JusticiarValorWave.PropsFor(MJV_DefOf.MJV_JusticiarValor), null)
                .Select(p => p.LabelShort + " (" + (p.Faction?.Name ?? "no faction") + (p.HostileTo(justiciar) ? ", hostile" : "") + ")"));
            Pawn ally = MakePawn(center + new IntVec3(1, 0, 2), Faction.OfPlayer);
            victims.Add(ally);
            // Not stunned beforehand, so the stun it lands with is the wave's.
            Pawn lander = MakeHostile(center + new IntVec3(-1, 0, -2), stun: false);
            Check(aiWithFoesOnly && !valorComp.AICanTargetNow(new LocalTargetInfo(justiciar)),
                "the AI releases the wave among foes, but not with an ally in it" + (aiWithFoesOnly ? "" : " (in the wave: " + caught + ")"));
            // A neutral visitor caught along with the enemies, from a faction hostile to them too: vanilla crossfire.
            Faction enemies = victims[0].Faction;
            Faction neutral = Find.FactionManager.AllFactions.FirstOrDefault(f => !f.IsPlayer && !f.Hidden && !f.temporary && f.def.humanlikeFaction
                && !f.HostileTo(Faction.OfPlayer) && f != Find.FactionManager.OfEmpire && f.HostileTo(enemies));
            int goodwill0 = neutral?.PlayerGoodwill ?? 0;
            if (neutral != null)
            {
                victims.Add(MakePawn(center + new IntVec3(-2, 0, -1), neutral));
            }
            victims.Add(lander);
            var before = victims.ToDictionary(v => v, v => (health: Health(v), distance: v.Position.DistanceTo(justiciar.Position)));
            int effectsBefore = Fx.ActiveEffectCount;
            valor.Activate(new LocalTargetInfo(justiciar), new LocalTargetInfo(justiciar));
            Check(EffectCount("PsychicWaveFx") >= 1 && Fx.ActiveEffectCount > effectsBefore, "Justiciar Valor spawns its shockwave effect");
            List<PawnFlyer_MJV_Knockback> flyers = map.listerThings.ThingsOfDef(MJV_DefOf.MJV_PawnFlyer_Knockback).OfType<PawnFlyer_MJV_Knockback>().ToList();
            CompProperties_AbilityJusticiarValor valorProps = JusticiarValorWave.PropsFor(MJV_DefOf.MJV_JusticiarValor);
            Check(flyers.Count >= 1, "Justiciar Valor throws its victims back (MJV_PawnFlyer_Knockback)");
            Check(flyers.All(f => f.instigator == justiciar && f.stunTicks >= valorProps.landingStunTicks.min && f.stunTicks <= valorProps.landingStunTicks.max),
                "the thrown victims are to land stunned by the wearer (" + string.Join(", ", flyers.Select(f => f.stunTicks)) + " ticks)");
            Pawn landerFlying = lander.Spawned ? null : lander;
            Check(victims.All(v => v.Dead || v.health.hediffSet.HasHediff(MJV_DefOf.MJV_PsychicDaze)), "every victim is dazed, allies included");
            Check(!justiciar.health.hediffSet.HasHediff(MJV_DefOf.MJV_PsychicDaze), "the wearer is spared");
            Ticks(60);
            Check(landerFlying == null || lander.Dead || lander.Downed || lander.Spawned && lander.stances.stunner.Stunned,
                "a thrown victim is stunned where it lands (" + (lander.Dead ? "dead" : lander.Downed ? "downed" : lander.Spawned
                    ? "stunned " + lander.stances.stunner.Stunned : "in the air") + ")");
            Ticks(140);
            Check(victims.All(v => v.Dead || Health(v) < before[v].health), "every victim took damage, allies included");
            // A victim the blow itself downed is not thrown (by design), only stunned.
            Check(victims.Where(v => !v.Dead && !v.Downed && v.Spawned).All(v => v.Position.DistanceTo(justiciar.Position) > before[v].distance + 0.9f),
                "every victim still standing landed farther away (" + string.Join(", ", victims.Select(v => before[v].distance.ToString("0.0") + " -> "
                    + (v.Dead ? "dead" : v.Downed ? "downed" : v.Spawned ? v.Position.DistanceTo(justiciar.Position).ToString("0.0") : "in the air"))) + ")");
            if (neutral != null)
            {
                Check(neutral.PlayerGoodwill == goodwill0, "a neutral visitor caught with the enemies costs no goodwill (crossfire)");
            }
            Check(valor.OnCooldown, "Justiciar Valor goes on cooldown");
            ClearExtras();
            // A mechanoid wounded as in a fight: a capacity-based daze used to down it, so it was neither thrown nor killed
            // and got up again when the daze wore off. The blow's damage is left out: one that downs a mech kills it, as
            // in vanilla.
            Pawn mech = MakeMech("Mech_Lancer", center + new IntVec3(2, 0, 0));
            if (mech != null)
            {
                WoundLegs(mech, 0.45f);
                float moving0 = mech.health.capacities.GetLevel(PawnCapacityDefOf.Moving);
                float distance0 = mech.Position.DistanceTo(justiciar.Position);
                CompProperties_AbilityJusticiarValor props = JusticiarValorWave.PropsFor(MJV_DefOf.MJV_JusticiarValor);
                JusticiarValorWave.Release(justiciar, new CompProperties_AbilityJusticiarValor
                {
                    radius = props.radius,
                    dazeHediff = props.dazeHediff,
                    knockbackDistance = props.knockbackDistance,
                    maxKnockbackBodySize = props.maxKnockbackBodySize,
                    stunTicks = props.stunTicks
                }, null);
                Ticks(200);
                Check(!mech.Dead && !mech.Downed && mech.health.hediffSet.HasHediff(MJV_DefOf.MJV_PsychicDaze)
                    && mech.Position.DistanceTo(justiciar.Position) > distance0 + 0.9f,
                    "a wounded mechanoid is dazed and thrown back like the others, and stays up (moving " + moving0.ToString("0.00") + " -> "
                    + mech.health.capacities.GetLevel(PawnCapacityDefOf.Moving).ToString("0.00") + ", " + distance0.ToString("0.0") + " -> "
                    + (mech.Spawned ? mech.Position.DistanceTo(justiciar.Position).ToString("0.0") : "in the air") + ")");
                ClearExtras();
            }
            else
            {
                JusticiarValidation.Line("SKIP no mechanoid to test");
            }
            if (neutral != null)
            {
                // With no enemy around, the wave is aimed at whoever it strikes: vanilla charges the visitor's faction.
                FactionRelationKind relation0 = neutral.PlayerRelationKind;
                goodwill0 = neutral.PlayerGoodwill;
                MakePawn(center + new IntVec3(2, 0, 0), neutral);
                JusticiarValorWave.Release(justiciar, JusticiarValorWave.PropsFor(MJV_DefOf.MJV_JusticiarValor), null);
                Check(neutral.PlayerGoodwill < goodwill0, "the wave blasting a lone neutral visitor costs goodwill (" + goodwill0 + " -> " + neutral.PlayerGoodwill + ")");
                neutral.TryAffectGoodwillWith(Faction.OfPlayer, goodwill0 - neutral.PlayerGoodwill, false, false);
                if (neutral.PlayerRelationKind != relation0)
                {
                    neutral.SetRelationDirect(Faction.OfPlayer, relation0, false);
                }
                Ticks(200);
                ClearExtras();
            }
            else
            {
                JusticiarValidation.Line("SKIP no neutral faction hostile to " + enemies?.Name + " for the goodwill checks");
            }
            synthesizer.AutoLaunch = true;

            JusticiarValidation.Line("== Murderous intent");
            Pawn aim1 = MakeHostile(center + new IntVec3(5, 0, 3));
            Pawn aim2 = MakeHostile(center + new IntVec3(-5, 0, 3));
            aim1.mindState.enemyTarget = justiciar;
            aim2.mindState.enemyTarget = justiciar;
            Ticks(31);
            Hediff intent = justiciar.health.hediffSet.GetFirstHediffOfDef(MJV_DefOf.MJV_MurderousIntent);
            Check(intent != null && Math.Abs(intent.Severity - 2f) < 0.01f, "two hostiles aiming at the wearer give murderous intent severity 2 (" + intent?.Severity + ")");
            Check(EffectCount("ThreatPulseFx") >= 1, "the helmet plays its scan pulse when it senses new intent");
            Check(Effects("ThreatPulseFx").Any(e => ((IList)AccessTools.Field(e.GetType(), "sources").GetValue(e)).Count == 2),
                "the pulse marks both pawns taking aim");
            aim1.mindState.enemyTarget = null;
            aim2.mindState.enemyTarget = null;
            Ticks(150);
            Check(!justiciar.health.hediffSet.HasHediff(MJV_DefOf.MJV_MurderousIntent), "the hediff fades once nobody aims at the wearer");
            ClearExtras();

            JusticiarValidation.Line("== Psychic shield");
            Ticks(1);
            Pawn shooter = MakeHostile(center + new IntVec3(0, 0, 6));
            float sensitivity = justiciar.GetStatValue(StatDefOf.PsychicSensitivity);
            float max0 = shield.BaseEnergyMax;
            Check(max0 > 0f && Math.Abs(max0 - Math.Min(sensitivity * shield.Props.energyPerSensitivity, shield.Props.energyMaxRange.max)) < 0.01f,
                "the shield's maximum follows the wearer's psychic sensitivity (" + sensitivity.ToStringPercent() + " -> " + max0 + ")");
            energyField.SetValue(shield, max0);
            float energy0 = shield.Energy;
            int injuries0 = Injuries(justiciar);
            justiciar.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 20f, 0f, 180f, shooter));
            Check(Math.Abs(shield.Energy - (energy0 - 20f)) < 0.01f && Injuries(justiciar) == injuries0, "a 20-damage bullet is absorbed (" + energy0 + " -> " + shield.Energy + ")");
            Check(Math.Abs(shield.Charge - 20f) < 0.01f, "the absorbed damage is stored for Invictus (" + shield.Charge + ")");
            // A blow struck in melee, as Verb_MeleeAttackDamage deals it: the attacker's weapon (its race, unarmed).
            DamageWorker.DamageResult melee = justiciar.TakeDamage(new DamageInfo(DamageDefOf.Cut, 12f, 10f, 180f, shooter, null, shooter.def));
            Check(melee.totalDamageDealt <= 0f && Math.Abs(shield.Energy - (energy0 - 32f)) < 0.01f && Math.Abs(shield.Charge - 32f) < 0.01f,
                "a melee blow is absorbed and stored too (" + shield.Energy + ", charge " + shield.Charge + ")");
            // Bypass apparel and measure actual damage: a severed finger creates a missing-part
            // hediff rather than an Injury, so counting injuries is not a reliable shield assertion.
            DamageWorker.DamageResult cut = justiciar.TakeDamage(new DamageInfo(DamageDefOf.Cut, 6f, 10f, 180f, shooter));
            Check(cut.totalDamageDealt > 0f && Math.Abs(shield.Energy - (energy0 - 32f)) < 0.01f, "a cut that is no attack (no weapon, no tool) is not absorbed");
            justiciar.TakeDamage(new DamageInfo(DamageDefOf.Bomb, 400f, 0f, 180f, shooter));
            Check(shield.Resetting, "a hit beyond its energy breaks the shield");
            Ticks(2401);
            Check(!shield.Resetting && Math.Abs(shield.Energy - max0 * shield.Props.energyOnReset) < 1f,
                "the shield resets to " + shield.Props.energyOnReset.ToStringPercent() + " after its delay (" + shield.Energy + ")");
            // The stored charge fades once the wearer goes a while unhurt, and holds while hits keep coming.
            chargeField.SetValue(shield, 50f);
            lastDamageField.SetValue(shield, Find.TickManager.TicksGame);
            Ticks(60);
            Check(Math.Abs(shield.Charge - 50f) < 0.01f && !shield.ChargeDraining, "the stored charge holds right after the wearer was hurt");
            lastDamageField.SetValue(shield, Find.TickManager.TicksGame - shield.Props.chargeDrainDelayTicks - 1);
            Ticks(60);
            Check(shield.ChargeDraining && Math.Abs(shield.Charge - (50f - shield.Props.chargeDrainPerSecond)) < 0.1f,
                "the stored charge fades once the wearer has gone unhurt (50 -> " + shield.Charge + " in a second)");
            justiciar.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 5f, 0f, 180f, shooter));
            Check(!shield.ChargeDraining, "a new hit stops the fading");
            ClearExtras();
            justiciar.health.RestorePart(justiciar.RaceProps.body.corePart);

            JusticiarValidation.Line("== Invictus");
            chargeField.SetValue(shield, 120f);
            float speed0 = justiciar.GetStatValue(StatDefOf.MoveSpeed);
            Check(!invictus.GizmoDisabled(out string reason), "Invictus is castable with a full charge " + reason);
            invictus.Activate(new LocalTargetInfo(justiciar), new LocalTargetInfo(justiciar));
            Hediff overdrive = justiciar.health.hediffSet.GetFirstHediffOfDef(MJV_DefOf.MJV_InvictusOverdrive);
            int ticksLeft = overdrive?.TryGetComp<HediffComp_Disappears>()?.ticksToDisappear ?? -1;
            Check(overdrive != null && ticksLeft == 600, "Invictus overdrives the wearer for 10 s at a full charge (" + ticksLeft + " ticks)");
            float overcharged = shield.BaseEnergyMax * shield.Props.overdriveEnergyFactor;
            Check(shield.Overdriven && Math.Abs(shield.EnergyMax - overcharged) < 0.01f && Math.Abs(shield.Energy - overcharged) < 0.01f && shield.Charge == 0f,
                "Invictus spends the charge and overcharges the shield to " + shield.Props.overdriveEnergyFactor.ToStringPercent() + " (" + shield.Energy + ")");
            Check(justiciar.GetStatValue(StatDefOf.MoveSpeed) > speed0 + 2f, "the overdrive speeds the wearer up (" + speed0 + " -> " + justiciar.GetStatValue(StatDefOf.MoveSpeed) + ")");
            Check(EffectCount("InvictusBurstFx") == 1, "Invictus plays its burst");
            yield return 5;
            Check(AfterimageUtility.CanBlit, "afterimages can render the pawn's atlas frame at any zoom (PawnRenderer.GetBlitMeshUpdatedFrame found)");
            JusticiarValidation.Line("Camera zoom (ZoomRootSize) " + Find.CameraDriver.ZoomRootSize + ": below 18 the game draws pawns without the atlas");
            // Deterministic movement samples: a generated map can obstruct a Goto path, and paused
            // fast-forward leaves PawnTweener behind. Exercise the same draw positions the recorder uses.
            int afterimages = 0;
            justiciar.jobs.StopAll();
            for (int step = 1; step <= 5; step++)
            {
                justiciar.Position = center + new IntVec3(0, 0, step);
                justiciar.Notify_Teleported();
                justiciar.Drawer.tweener.ResetTweenedPosToRoot();
                Ticks(3);
                afterimages = Math.Max(afterimages, EffectCount("AfterimageFx"));
                yield return 1;
            }
            Check(afterimages >= 3, "the overdriven wearer leaves afterimages as it moves (" + afterimages + ")");
            Ticks(600);
            Check(!justiciar.health.hediffSet.HasHediff(MJV_DefOf.MJV_InvictusOverdrive) && !shield.Overdriven, "the overdrive ends on time");
            justiciar.jobs.StopAll();
            justiciar.Position = center;
            justiciar.Notify_Teleported();

            JusticiarValidation.Line("== Psy-orbs");
            orbsField.SetValue(synthesizer, 0);
            Ticks(301);
            Check(synthesizer.Orbs == 1, "the psy-spear synthesizes an orb in five seconds (" + synthesizer.Orbs + ")");
            Check(synthesizer.Deployed && MapComponent_JusticiarFx.IsRegistered(map, synthesizer), "the orbs are out around a drafted wielder");
            synthesizer.AutoLaunch = false;
            Ticks(301);
            Check(synthesizer.Orbs == 1, "nothing is synthesized while the auto-ability is off");
            Check(!synthesizer.Deployed, "switching the auto-ability off takes the orbs back into the spear, where they are kept");
            synthesizer.AutoLaunch = true;
            // Out of combat: an undrafted wielder at work keeps them inside, and nothing is drawn.
            HostilityResponseMode response = spearman.playerSettings.hostilityResponse;
            spearman.drafter.Drafted = false;
            spearman.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
            spearman.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 100000), JobCondition.InterruptForced);
            Ticks(synthesizer.OrbProps.combatGraceTicks + 40);
            yield return 2;
            Check(!synthesizer.Deployed && !MapComponent_JusticiarFx.IsRegistered(map, synthesizer) && synthesizer.Orbs >= 1,
                "out of combat the orbs stay inside the spear and nothing is drawn (" + synthesizer.Orbs + " kept)");
            MakeHostile(spearman.Position + new IntVec3(0, 0, 9));
            Ticks(31);
            Check(synthesizer.Deployed, "an active threat nearby brings the orbs out without drafting");
            ClearExtras();
            spearman.playerSettings.hostilityResponse = response;
            spearman.jobs.StopAll();
            spearman.drafter.Drafted = true;
            // Sleeping foes are left alone, as turrets leave them: a hit on a dormant mechanoid would wake it and its cluster.
            orbsField.SetValue(synthesizer, 3);
            Pawn dormant = MakeMech("Mech_Scyther", spearman.Position + new IntVec3(0, 0, 7));
            CompCanBeDormant dormancy = dormant?.GetComp<CompCanBeDormant>();
            if (dormancy != null)
            {
                dormancy.ToSleep();
                Ticks(40);
                Check(synthesizer.Orbs == 3 && !dormancy.Awake, "the orbs leave a dormant mechanoid alone (" + synthesizer.Orbs + " orbs)");
            }
            else
            {
                JusticiarValidation.Line("SKIP no dormant mechanoid to test");
            }
            ClearExtras();
            orbsField.SetValue(synthesizer, 3);
            var orbPosition = AccessTools.Method(typeof(CompPsyOrbSynthesizer), "OrbPosition");
            Vector3 slotBefore = (Vector3)orbPosition.Invoke(synthesizer, new object[] { spearman, 1, FxClock.Now });
            orbsField.SetValue(synthesizer, 2);
            Vector3 slotAfter = (Vector3)orbPosition.Invoke(synthesizer, new object[] { spearman, 1, FxClock.Now });
            Check(JVMath.DistanceFlatSquared(slotBefore, slotAfter) < 0.000001f,
                "remaining orbs keep their positions when another orb is fired");
            orbsField.SetValue(synthesizer, 3);
            // The steps above may have launched an orb just now; this one measures a launch from a rested spear.
            AccessTools.Field(typeof(CompPsyOrbSynthesizer), "launchCooldown").SetValue(synthesizer, 0);
            Pawn orbTarget = MakeHostile(spearman.Position + new IntVec3(0, 0, 7));
            float orbHealth = Health(orbTarget);
            Ticks(15);
            Check(map.listerThings.ThingsOfDef(MJV_DefOf.MJV_Projectile_PsyOrb).Count >= 1 && synthesizer.Orbs < 3, "an orb is launched at an enemy in range");
            Ticks(90);
            Check(orbTarget.Dead || orbTarget.health.hediffSet.HasHediff(MJV_DefOf.MJV_PsyOrbSlow) && Health(orbTarget) < orbHealth, "the orb burns and slows its target");
            Check(EffectCount("OrbImpactFx") >= 1, "the orb bursts on impact");
            Check(EffectCount("OrbWakeFx") >= 1, "the orb's wake catches up with its impact");
            ClearExtras();

            JusticiarValidation.Line("== Celestial Pierce");
            // The spear's orbs would join in on the foes around the landing.
            synthesizer.AutoLaunch = false;
            Check(ReferenceEquals(FxAssets.BladeFor(bladesman.equipment.Primary).mainTexture,
                bladesman.equipment.Primary.Graphic.MatSingleFor(bladesman.equipment.Primary).mainTexture),
                "the spectral blade uses the equipped sword's actual texture");
            JusticiarValidation.Line("Sword graphic " + bladesman.equipment.Primary.Graphic.path
                + ", draw size " + bladesman.equipment.Primary.Graphic.drawSize
                + ", equipped angle " + bladesman.equipment.Primary.def.equippedAngleOffset);
            // Manual tick batches do not update the renderer between ticks.
            bladesman.Drawer.tweener.ResetTweenedPosToRoot();
            // The blade lands on `target`, three cells off; its area is the landing cell and the eight around it.
            Pawn target = MakeHostile(bladesman.Position + new IntVec3(3, 0, 0));
            Pawn beside = MakeHostile(bladesman.Position + new IntVec3(3, 0, 1));
            Pawn areaAlly = MakePawn(bladesman.Position + new IntVec3(2, 0, -1), Faction.OfPlayer);
            Pawn dodger = MakeHostile(bladesman.Position + new IntVec3(4, 0, 0));
            Pawn outside = MakeHostile(bladesman.Position + new IntVec3(0, 0, 3));
            Pawn near = MakeHostile(bladesman.Position + new IntVec3(1, 0, -2));
            // Strays would change who the blade catches, and whether the AI holds back for an ally; a wall or rock in the
            // area would shield those behind it (as it should).
            ClearStrays(bladesman.Position, 7f, new[] { justiciar, spearman, bladesman, target, beside, areaAlly, dodger, outside, near });
            List<Building> walls = CellRect.CenteredOn(target.Position, 2).ClipInsideMap(map).Cells.Select(c => c.GetEdifice(map)).Where(b => b != null).ToList();
            if (walls.Count > 0)
            {
                JusticiarValidation.Line("Cleared from around the landing: " + string.Join(", ", walls.Select(b => b.def.defName + " " + b.Position)));
                PrepareEffectStage(map, target.Position, 2, 2);
            }
            var pierceComp = pierce.CompOfType<CompAbilityEffect_CelestialPierce>();
            Check(!pierceComp.AICanTargetNow(new LocalTargetInfo(target)) && pierceComp.AICanTargetNow(new LocalTargetInfo(outside)),
                "the AI does not bring the blade down on an ally, but does where only foes stand");
            // Measure damage directly: summary health also weights pre-existing and missing body parts.
            float hTarget = target.records.GetValue(RecordDefOf.DamageTaken);
            float hBeside = beside.records.GetValue(RecordDefOf.DamageTaken);
            float hAlly = areaAlly.records.GetValue(RecordDefOf.DamageTaken);
            float hDodger = dodger.records.GetValue(RecordDefOf.DamageTaken);
            float hOutside = outside.records.GetValue(RecordDefOf.DamageTaken);
            float hNear = near.records.GetValue(RecordDefOf.DamageTaken);
            pierce.Activate(new LocalTargetInfo(target), new LocalTargetInfo(target));
            var strikes = map.GetComponent<MapComponent_JusticiarStrikes>();
            Check(strikes.PendingCount == 5 && target.records.GetValue(RecordDefOf.DamageTaken) - hTarget < 0.0001f,
                "the landing and the four strikes around it wait for the blade to slam down (" + strikes.PendingCount + " pending)");
            var pending = ((IList)AccessTools.Field(typeof(MapComponent_JusticiarStrikes), "pending").GetValue(strikes)).Cast<PendingStrike>();
            Check(pending.Where(s => !s.landing).All(s => s.intendedTarget == target),
                "every strike is meant for the target, so anyone else around it counts as crossfire");
            // One foe gets clear of the area before the blade lands.
            dodger.Position += new IntVec3(3, 0, 0);
            dodger.Notify_Teleported();
            dodger.Drawer.tweener.ResetTweenedPosToRoot();
            if (strikes.PendingCount != 5)
            {
                // What stood in the area: a wall shields those behind it.
                JusticiarValidation.Line("Landing at " + target.Position + " from " + bladesman.Position + ", pawns around it: "
                    + string.Join(", ", GenRadial.RadialCellsAround(target.Position, 1.5f, true).SelectMany(c => c.GetThingList(map)).OfType<Pawn>()
                        .Select(p => p.LabelShort + " " + p.Position)));
            }
            Check(EffectCount("CelestialPierceFx") == 1, "Celestial Pierce draws its blade and the broken ground");
            Ticks(75);
            float damageTarget = target.records.GetValue(RecordDefOf.DamageTaken) - hTarget;
            float damageBeside = beside.records.GetValue(RecordDefOf.DamageTaken) - hBeside;
            Check(damageTarget > 0f && damageBeside > 0f && strikes.PendingCount == 0, "the landing impales the target and the foe beside it (damage "
                + damageTarget + ", " + damageBeside + ")");
            Check(areaAlly.records.GetValue(RecordDefOf.DamageTaken) - hAlly > 0f, "and the ally standing in the area");
            Check(Math.Abs(dodger.records.GetValue(RecordDefOf.DamageTaken) - hDodger) < 0.0001f,
                "a foe that got clear before the blade landed escapes" + Wounds(dodger));
            Check(Math.Abs(outside.records.GetValue(RecordDefOf.DamageTaken) - hOutside) < 0.0001f
                && Math.Abs(near.records.GetValue(RecordDefOf.DamageTaken) - hNear) < 0.0001f,
                "foes outside the area are untouched" + Wounds(outside) + Wounds(near));
            Check(EffectCount("PierceHitFx") >= 1, "each struck pawn bursts where the shock reaches it");
            ClearExtras();
            synthesizer.AutoLaunch = true;

            JusticiarValidation.Line("== Warlockbane");
            Pawn warlock = MakeHostile(center + new IntVec3(0, 0, 6), stun: false);
            // A psychically deaf warlock could not cast at all.
            foreach (Trait trait in warlock.story.traits.allTraits.Where(t => t.def.defName == "PsychicSensitivity").ToList())
            {
                warlock.story.traits.RemoveTrait(trait);
            }
            PawnUtility.ChangePsylinkLevel(warlock, 1, false);
            AbilityDef stunDef = DefDatabase<AbilityDef>.GetNamed("Stun");
            warlock.abilities.GainAbility(stunDef);
            Ability stun = warlock.abilities.GetAbility(stunDef);
            warlock.psychicEntropy.OffsetPsyfocusDirectly(1f);
            int charges0 = warlockbane.Charges;
            stun.Activate(new LocalTargetInfo(justiciar), new LocalTargetInfo(justiciar));
            Check(!justiciar.stances.stunner.Stunned && warlockbane.Charges == charges0 - 1, "a hostile Stun psycast is deflected and spends a charge");
            Check(warlock.stances.stunner.Stunned, "the warlock is stunned by the backlash");
            Check(EffectCount("DeflectFx") == 1, "the deflection plays its ward effect");
            warlock.psychicEntropy.RemoveAllEntropy();
            stun.Activate(new LocalTargetInfo(justiciar), new LocalTargetInfo(justiciar));
            warlock.psychicEntropy.RemoveAllEntropy();
            stun.Activate(new LocalTargetInfo(justiciar), new LocalTargetInfo(justiciar));
            Check(warlockbane.Charges == 0 && justiciar.stances.stunner.Stunned, "with its charges spent the shield no longer deflects");
            justiciar.stances.stunner.StopStun();
            ClearExtras();

            JusticiarValidation.Line("== Arrest");
            int waves = EffectCount("PsychicWaveFx");
            bool accepted = justiciar.CheckAcceptArrest(spearman);
            Check(!accepted, "the conscious Promachos wearer refuses arrest");
            Check(EffectCount("PsychicWaveFx") > waves, "an arrest attempt fires Justiciar Valor at once");
            justiciar.mindState.mentalStateHandler.Reset();
            Ticks(200);
            spearman.health.Reset();
            foreach (Pawn pawn in new[] { justiciar, spearman, bladesman })
            {
                if (pawn.Spawned && !pawn.Dead)
                {
                    pawn.health.Reset();
                    pawn.drafter.Drafted = true;
                }
            }

            JusticiarValidation.Line("== Imperial codex-auspex");
            List<ResearchProjectDef> empireProjects = DefDatabase<ResearchProjectDef>.AllDefs
                .Where(p => p.TechprintCount > 0 && p.heldByFactionCategoryTags != null && p.heldByFactionCategoryTags.Contains("Empire")).ToList();
            Check(empireProjects.Contains(MJV_DefOf.MJV_JusticiarGear), "justiciar gear needs techprints held by the Empire");
            int techprints0 = empireProjects.Sum(p => Find.ResearchManager.GetTechprints(p));
            Thing tome = GenSpawn.Spawn(ThingMaker.MakeThing(MJV_DefOf.MJV_ImperialCodexAuspex), bladesman.Position + new IntVec3(1, 0, 0), map);
            tome.TryGetComp<CompUsable>().UsedBy(bladesman);
            Check(empireProjects.Sum(p => Find.ResearchManager.GetTechprints(p)) == techprints0 + 1, "studying the codex-auspex applies one imperial techprint");
            Check(tome.Destroyed, "the codex-auspex is used up");
            Check(DefDatabase<TraderKindDef>.GetNamed("Empire_Caravan_TributeCollector").WillTrade(MJV_DefOf.MJV_ImperialCodexAuspex),
                "imperial tribute collectors take the codex-auspex");
            Check(Math.Abs(MJV_DefOf.MJV_ImperialCodexAuspex.GetStatValueAbstract(StatDefOf.RoyalFavorValue) - 10f) < 0.001f, "the codex-auspex is worth 10 royal favor");
            Check(DefDatabase<TraderKindDef>.GetNamed("Orbital_Empire").stockGenerators.Any(g => g.HandlesThingDef(MJV_DefOf.MJV_ImperialCodexAuspex)),
                "imperial orbital traders stock the codex-auspex");

            JusticiarValidation.Line("== Empire");
            Check(FactionDefOf.Empire.pawnGroupMakers.Where(m => m.kindDef == PawnGroupKindDefOf.Combat).All(m => m.options.Any(o => o.kind == MJV_DefOf.MJV_Justiciar)),
                "justiciars join the Empire's combat groups");
            Faction empire = Find.FactionManager.OfEmpire;
            if (empire != null)
            {
                Pawn generated = PawnGenerator.GeneratePawn(new PawnGenerationRequest(MJV_DefOf.MJV_Justiciar, empire, forceGenerateNewPawn: true));
                Check(generated.apparel.WornApparel.Any(a => a.def == MJV_DefOf.MJV_Apparel_PromachosArmor)
                    && generated.apparel.WornApparel.Any(a => a.def == MJV_DefOf.MJV_Apparel_ArmorHelmetPromachosHelmet)
                    && generated.apparel.WornApparel.Any(a => a.def == MJV_DefOf.MJV_Apparel_WarlockbaneShield),
                    "a generated justiciar wears the full Promachos gear and the warlockbane shield");
                ThingDef weapon = generated.equipment.Primary?.def;
                Check(weapon == MJV_DefOf.MJV_Weapon_EnergyPsySpear || weapon == MJV_DefOf.MJV_MeleeWeapon_EnergyPsyblade,
                    "a generated justiciar wields an energy psy-spear or psyblade (" + weapon?.defName + ")");
                // Its Social range is 10~16; genes can shift the level, but never take the conversion power away.
                Check(!generated.WorkTagIsDisabled(WorkTags.Social) && generated.GetStatValue(StatDefOf.ConversionPower) > 0f,
                    "a generated justiciar can preach (Social " + generated.skills.GetSkill(SkillDefOf.Social).Level + ", conversion power "
                    + generated.GetStatValue(StatDefOf.ConversionPower).ToStringPercent() + ")");
                generated.Destroy();

                foreach (string traderKind in new[] { "Empire_Caravan_TributeCollector", "Empire_Caravan_TraderGeneral" })
                {
                    TraderKindDef kind = DefDatabase<TraderKindDef>.GetNamedSilentFail(traderKind);
                    if (kind == null)
                    {
                        continue;
                    }
                    List<Pawn> caravan = PawnGroupMakerUtility.GeneratePawns(new PawnGroupMakerParms
                    {
                        groupKind = PawnGroupKindDefOf.Trader,
                        faction = empire,
                        traderKind = kind,
                        tile = map.Tile,
                        points = 1000f
                    }).ToList();
                    Pawn trader = caravan.FirstOrDefault(p => p.trader?.traderKind == kind);
                    bool tribute = kind.category == Patch_ImperialJusticiars.TributeCollectorCategory;
                    Check(trader != null && (trader.kindDef == MJV_DefOf.MJV_Justiciar) == tribute,
                        (tribute ? "a justiciar leads the tribute collector's caravan" : "an ordinary imperial caravan keeps its trader")
                        + " (" + trader?.kindDef.defName + ")");
                    foreach (Pawn pawn in caravan)
                    {
                        pawn.Destroy();
                    }
                }
                Check(Harmony.GetPatchInfo(Patch_ImperialJusticiars.BestowingRunIntMethod())?.Transpilers.Any(p => p.owner == JusticiarValorMod.HarmonyId) == true,
                    "the bestowing ceremony's escort is patched");
                Patch_ImperialJusticiars.BestowingRunIntPrefix();
                List<PawnKindDef> escort = Enumerable.Range(0, 6).Select(_ => Patch_ImperialJusticiars.BestowerEscortKind()).ToList();
                Check(escort.Count(k => k == MJV_DefOf.MJV_Justiciar) == Patch_ImperialJusticiars.JusticiarsInBestowerEscort
                    && escort.Count(k => k == PawnKindDefOf.Empire_Fighter_Janissary) == 6 - Patch_ImperialJusticiars.JusticiarsInBestowerEscort,
                    "two of the bestower's six guards are justiciars (" + string.Join(", ", escort.Select(k => k.defName)) + ")");
                QuestScriptDef ascent = DefDatabase<QuestScriptDef>.GetNamedSilentFail("EndGame_RoyalAscent");
                List<QuestNode_GeneratePawn> ascentPawns = (ascent?.root as QuestNode_Sequence)?.nodes.OfType<QuestNode_GeneratePawn>().ToList();
                Check(ascentPawns != null && ascentPawns.Any(n => n.kindDef.GetValue(new Slate()) == MJV_DefOf.MJV_Justiciar && n.forbidAnyTitle.GetValue(new Slate())),
                    "a justiciar without a title travels with the High Stellarch");
            }
            else
            {
                JusticiarValidation.Line("SKIP no Empire faction in this world");
            }
        }

        // State for the reload check, set right before the save (the gear recharges while the missions run).
        public void PrepareReload()
        {
            Pawn justiciar = map.mapPawns.AllPawnsSpawned.First(p => p.thingIDNumber == justiciarId);
            Pawn spearman = map.mapPawns.AllPawnsSpawned.First(p => p.thingIDNumber == spearmanId);
            CompPsychicShield shield = justiciar.WornComp<CompPsychicShield>();
            CompWarlockbane warlockbane = justiciar.WornComp<CompWarlockbane>();
            var synthesizer = spearman.equipment.Primary?.TryGetComp<CompPsyOrbSynthesizer>();
            Check(shield != null && warlockbane != null && synthesizer != null, "the test pawns still carry their gear before the save (shield "
                + (shield != null) + ", warlockbane " + (warlockbane != null) + ", psy-spear " + (synthesizer != null) + ", justiciar "
                + (justiciar.Dead ? "dead" : justiciar.Downed ? "downed" : "fine") + ", spearman " + (spearman.Dead ? "dead" : spearman.Downed ? "downed" : "fine") + ")");
            if (shield == null || warlockbane == null || synthesizer == null)
            {
                return;
            }
            energyField.SetValue(shield, shield.EnergyMax);
            chargeField.SetValue(shield, 77f);
            lastDamageField.SetValue(shield, Find.TickManager.TicksGame);
            orbsField.SetValue(synthesizer, 3);
            wlChargesField.SetValue(warlockbane, 1);
            savedCharge = shield.Charge;
            savedOrbs = synthesizer.Orbs;
            savedWarlockbaneCharges = warlockbane.Charges;
        }

        public IEnumerable<object> SaveAndReload(string saveName)
        {
            JusticiarValidation.Line("== Save and reload");
            savedMap = map;
            GameDataSaveLoader.SaveGame(saveName);
            GameDataSaveLoader.LoadGame(saveName);
            yield return (Func<bool>)(() => !LongEventHandler.AnyEventNowOrWaiting && Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null && !ReferenceEquals(Find.CurrentMap, savedMap));
            yield return 10;
            Map loaded = Find.CurrentMap;
            Pawn justiciar = loaded.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.thingIDNumber == justiciarId);
            Pawn spearman = loaded.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.thingIDNumber == spearmanId);
            Check(justiciar != null && spearman != null, "the test pawns are back after the reload");
            if (justiciar == null || spearman == null)
            {
                yield break;
            }
            CompPsychicShield shield = justiciar.WornComp<CompPsychicShield>();
            CompWarlockbane warlockbane = justiciar.WornComp<CompWarlockbane>();
            var synthesizer = spearman.equipment.Primary.TryGetComp<CompPsyOrbSynthesizer>();
            Check(Math.Abs(shield.Charge - savedCharge) < 0.001f, "the stored Invictus charge survives the reload (" + shield.Charge + ")");
            Check(synthesizer != null && synthesizer.Orbs == savedOrbs, "the synthesized psy-orbs survive the reload");
            Check(warlockbane.Charges == savedWarlockbaneCharges, "the warlockbane charges survive the reload");
            Check(MapComponent_JusticiarFx.Of(loaded) != null, "the effect component is on the reloaded map");
            Check(justiciar.abilities.GetAbility(MJV_DefOf.MJV_JusticiarValor, true) != null
                && justiciar.abilities.GetAbility(MJV_DefOf.MJV_Invictus, true) != null, "the gear's abilities are back after the reload");
        }

        // ---------------------------------------------------------------------------------------------------------
        // Visuals: a baseline screenshot, then every effect at a fixed moment of its life, then close-ups.

        public IEnumerable<object> Visuals()
        {
            JusticiarValidation.Line("== Visuals");
            Pawn justiciar = map.mapPawns.AllPawnsSpawned.First(p => p.thingIDNumber == justiciarId);
            Pawn spearman = map.mapPawns.AllPawnsSpawned.First(p => p.thingIDNumber == spearmanId);
            Pawn bladesman = map.mapPawns.AllPawnsSpawned.First(p => p.thingIDNumber == bladesmanId);
            center = FindOpenArea(justiciar.Position, 9, 7);
            PrepareEffectStage(map, center, 12, 9);
            SetupScene();
            Pawn[] actors = { justiciar, spearman, bladesman };
            IntVec3[] positions = { center, center + new IntVec3(-6, 0, -4), center + new IntVec3(6, 0, -4) };
            for (int i = 0; i < actors.Length; i++)
            {
                actors[i].jobs.StopAll();
                actors[i].Position = positions[i];
                actors[i].Notify_Teleported();
                actors[i].Drawer.tweener.ResetTweenedPosToRoot();
            }
            ((IList)effectsField.GetValue(Fx)).Clear();
            foreach (Pawn actor in actors)
            {
                actor.health.Reset();
            }
            if (spearman.equipment.Primary?.def != MJV_DefOf.MJV_Weapon_EnergyPsySpear)
            {
                JusticiarValidation.Line("The spearman had lost his psy-spear (" + (spearman.equipment.Primary?.def.defName ?? "nothing") + "): wielding a new one");
                Wield(spearman, MJV_DefOf.MJV_Weapon_EnergyPsySpear);
            }
            var synthesizer = spearman.equipment.Primary.TryGetComp<CompPsyOrbSynthesizer>();
            synthesizer.AutoLaunch = false;
            orbsField.SetValue(synthesizer, 0);
            justiciar.drafter.Drafted = false;
            Vector3 c = center.ToVector3Shifted();
            Find.CameraDriver.SetRootPosAndSize(c, 11f);
            yield return 30;
            yield return JusticiarValidation.ShotPrefix + "baseline";

            // Persistent: the shield bubble (drafted wearer, a fresh hit), three orbs out around the drafted spearman.
            justiciar.drafter.Drafted = true;
            CompPsychicShield shield = justiciar.WornComp<CompPsychicShield>();
            shield.KeepDisplaying();
            spearman.drafter.Drafted = true;
            synthesizer.AutoLaunch = true;
            orbsField.SetValue(synthesizer, 3);
            // Long enough for all three to have come out of the spear.
            Ticks(40);
            // An orb in flight: launched at an empty cell and moved a few ticks along (before the frozen effects spawn).
            var projectile = (Projectile)GenSpawn.Spawn(MJV_DefOf.MJV_Projectile_PsyOrb, spearman.Position, map);
            IntVec3 aim = spearman.Position + new IntVec3(9, 0, 0);
            projectile.Launch(spearman, spearman.DrawPos, aim, aim, ProjectileHitFlags.None);
            Ticks(8);
            // FxClock caches within a render frame. Let it catch up to the manual ticks before
            // assigning exact playback times, otherwise all short effects are photographed too late.
            yield return 2;
            Spawn(new PsychicWaveFx(Offset(c, -6f, 4f), 3f), 0.24f);
            Spawn(new ThreatPulseFx(spearman, 0.5f), 0.4f);
            Spawn(new InvictusBurstFx(bladesman), 0.20f);
            Vector3 plunge = Offset(c, 2.5f, 3.5f);
            Spawn(new CelestialPierceFx(bladesman.equipment.Primary, Offset(plunge, -2f, 0.3f), 0f, false, plunge, 90f, 1.3f, 1.2f), 0.35f);
            Spawn(new DeflectFx(justiciar, 20f), 0.25f);
            Spawn(new OrbImpactFx(Offset(c, -3f, -5f)), 0.18f);
            if (AfterimageUtility.TryCapture(bladesman, out UnityEngine.Texture atlas, out UnityEngine.Mesh mesh))
            {
                var colors = new[] { FxPalette.Sage, FxPalette.Sage, FxPalette.Pale };
                for (int i = 0; i < 3; i++)
                {
                    Vector3 at = new Vector3(bladesman.DrawPos.x - 0.55f * (i + 1), FxDraw.AltitudeGlow, bladesman.DrawPos.z);
                    Spawn(new AfterimageFx(atlas, mesh, at, colors[i], 0.42f, Offset(at, 0.55f, 0f)), 0.15f + 0.25f * i);
                }
            }
            else
            {
                Check(false, "the bladesman's atlas frame can be captured for afterimages");
            }
            yield return 15;
            yield return JusticiarValidation.ShotPrefix + "effects";

            projectile.Destroy();

            // The imperial missions: a preacher's halo, alone on the same view (a convert gets the game's own bubble).
            ((IList)effectsField.GetValue(Fx)).Clear();
            orbsField.SetValue(synthesizer, 0);
            justiciar.drafter.Drafted = false;
            Spawn(new SermonHaloFadeFx(Offset(c, -4f, 2f), 5.2f, 0.3f, 1f), 0f);
            yield return 15;
            yield return JusticiarValidation.ShotPrefix + "missions";

            // The justiciar's gear up close, as drafted justiciars face the player: the psy-spear in hand and the
            // warlockbane shield on the arm, drawn under the spear but over the armor and the helmet.
            ((IList)effectsField.GetValue(Fx)).Clear();
            Wield(justiciar, MJV_DefOf.MJV_Weapon_EnergyPsySpear);
            justiciar.equipment.Primary.TryGetComp<CompPsyOrbSynthesizer>().AutoLaunch = false;
            justiciar.drafter.Drafted = true;
            // A pawn without a job draws no weapon: the drafted justiciar stands in his combat wait.
            justiciar.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait_Combat), JobCondition.InterruptForced);
            Ticks(1);
            justiciar.Rotation = Rot4.South;
            justiciar.Drawer.tweener.ResetTweenedPosToRoot();
            Find.CameraDriver.SetRootPosAndSize(justiciar.DrawPos, 3f);
            yield return 15;
            justiciar.Rotation = Rot4.South;
            Check(justiciar.CurJob != null && justiciar.equipment.Primary?.def == MJV_DefOf.MJV_Weapon_EnergyPsySpear,
                "the gear close-up shows the drafted justiciar with his spear (" + justiciar.CurJobDef?.defName + ")");
            yield return JusticiarValidation.ShotPrefix + "gear-south";
        }

        private static Vector3 Offset(Vector3 v, float dx, float dz) => new Vector3(v.x + dx, v.y, v.z + dz);

        // Spawns an effect at the given fraction of its life. The game stays paused while the screenshots are taken, and
        // FxClock stops with it, so the effect holds that frame.
        private void Spawn(FxEffect effect, float progress)
        {
            MapComponent_JusticiarFx.Spawn(map, new FrozenFrame(effect, progress));
        }

        // Rendering tests hold the exact source frame independently of frame-cached clocks and manual tick batches.
        private sealed class FrozenFrame : FxEffect
        {
            private readonly FxEffect effect;
            private readonly float frame;
            public FrozenFrame(FxEffect effect, float frame)
            {
                this.effect = effect;
                this.frame = frame;
                duration = 3600f;
            }
            public override bool Cancelled => effect.Cancelled;
            public override void Draw(FxDrawContext ctx, float progress, float age)
                => effect.Draw(ctx, frame, frame * effect.duration);
        }
    }
}
