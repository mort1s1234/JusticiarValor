using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;
using Vector3 = UnityEngine.Vector3;

namespace JusticiarValor
{
    public class CompProperties_PsychicShield : CompProperties
    {
        // Damage the shield absorbs before it breaks, per point of the wearer's psychic sensitivity (the Promachos gear's
        // own offsets included), clamped to energyMaxRange: 100 gives about 155 to a baseline wearer of the full set.
        public float energyPerSensitivity = 100f;

        public FloatRange energyMaxRange = new FloatRange(0f, 300f);

        public float rechargePerSecond = 10f;

        // Recharge waits this long after a hit.
        public int rechargeDelayTicks = 300;

        public int resetDelayTicks = 2400;

        // Fraction of the maximum restored when the shield comes back after breaking.
        public float energyOnReset = 0.4f;

        // Whether blows struck in melee are absorbed too, besides gunfire, projectiles and blasts.
        public bool absorbMelee = true;

        // Absorbed damage stored for Invictus, and its cap.
        public float chargePerDamage = 1f;

        public float chargeMax = 120f;

        // The stored charge fades once the wearer has taken no damage for this long, at this rate.
        public int chargeDrainDelayTicks = 600;

        public float chargeDrainPerSecond = 2f;

        // Invictus overcharges the shield to this multiple of the maximum.
        public float overdriveEnergyFactor = 1.5f;

        public float drawSize = 2.05f;

        public SoundDef absorbSound;

        public SoundDef breakSound;

        public SoundDef resetSound;

        public CompProperties_PsychicShield()
        {
            compClass = typeof(CompPsychicShield);
        }
    }

    // Promachos armor: a psychic shield drawn from a large eltex core, as strong as its wearer is psychically sensitive. It
    // absorbs incoming gunfire, projectiles, blasts and blows struck in melee, and every absorbed hit is stored as energy
    // for Invictus. The stored energy fades while the wearer takes no damage.
    public class CompPsychicShield : ThingComp, IFxDrawer
    {
        // How often the wearer's psychic sensitivity is read again.
        private const int SensitivityCacheTicks = 250;

        private float energy = -1f;
        private int ticksToReset = -1;
        private float charge;
        private int overdriveUntilTick = -1;
        private int lastHitTick = -9999;
        private int lastKeepDisplayTick = -9999;
        // When the wearer last took any damage, absorbed or not: the stored charge fades after a while without.
        private int lastDamageTick = -1;
        private float lastHitAngle;
        // When the shield last came back, for the dome to form again (not saved: only seconds of drawing depend on it).
        private int resetTick = -9999;
        private float seed = -1f;
        private Map registeredMap;

        public CompProperties_PsychicShield Props => (CompProperties_PsychicShield)props;

        public Pawn Wearer => JVUtility.WearerOf(parent);

        public bool Overdriven => Find.TickManager != null && Find.TickManager.TicksGame < overdriveUntilTick;

        // The shield's maximum before any overcharge. Off a wearer: the maximum for a psychic sensitivity of 100%.
        public float BaseEnergyMax => MaxFor(Wearer);

        public float EnergyMax => BaseEnergyMax * (Overdriven ? Props.overdriveEnergyFactor : 1f);

        public float Energy => energy < 0f ? BaseEnergyMax : energy;

        public bool Resetting => ticksToReset > 0;

        // A wearer without psychic sensitivity has no shield at all.
        public bool Active => !Resetting && Wearer != null && BaseEnergyMax > 0f;

        public float Charge => charge;

        public float ChargeFraction => Props.chargeMax <= 0f ? 0f : JVMath.Clamp01(charge / Props.chargeMax);

        // Whether the stored charge is fading for want of damage taken.
        public bool ChargeDraining => charge > 0f && !Overdriven && Find.TickManager != null
            && lastDamageTick >= 0 && Find.TickManager.TicksGame - lastDamageTick > Props.chargeDrainDelayTicks;

        public float MaxFor(Pawn wearer)
        {
            float sensitivity = wearer == null ? 1f : wearer.GetStatValue(StatDefOf.PsychicSensitivity, true, SensitivityCacheTicks);
            return JVMath.Clamp(sensitivity * Props.energyPerSensitivity, Props.energyMaxRange.min, Props.energyMaxRange.max);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref energy, "energy", -1f);
            Scribe_Values.Look(ref ticksToReset, "ticksToReset", -1);
            Scribe_Values.Look(ref charge, "charge", 0f);
            Scribe_Values.Look(ref overdriveUntilTick, "overdriveUntilTick", -1);
            Scribe_Values.Look(ref lastKeepDisplayTick, "lastKeepDisplayTick", -9999);
            Scribe_Values.Look(ref lastDamageTick, "lastDamageTick", -1);
        }

        public override void CompTick()
        {
            base.CompTick();
            Pawn wearer = Wearer;
            if (wearer == null)
            {
                return;
            }
            if (wearer.Spawned && registeredMap != wearer.Map)
            {
                registeredMap = wearer.Map;
                MapComponent_JusticiarFx.Register(registeredMap, this);
            }
            int now = Find.TickManager.TicksGame;
            float baseMax = BaseEnergyMax;
            if (energy < 0f)
            {
                energy = baseMax;
            }
            if (lastDamageTick < 0)
            {
                // A shield from before the drain was saved, or just put on: its grace starts now.
                lastDamageTick = now;
            }
            if (ChargeDraining)
            {
                charge = System.Math.Max(0f, charge - Props.chargeDrainPerSecond / 60f);
            }
            if (ticksToReset > 0)
            {
                ticksToReset--;
                if (ticksToReset == 0)
                {
                    Reset(wearer);
                }
                return;
            }
            float max = EnergyMax;
            if (energy > max)
            {
                // Overcharge drains back once Invictus ends, and so does energy above a lowered sensitivity's maximum.
                energy = System.Math.Max(max, energy - System.Math.Max(baseMax, Props.energyPerSensitivity) / 120f);
            }
            else if (energy < max && (Overdriven || now - lastHitTick > Props.rechargeDelayTicks))
            {
                energy = System.Math.Min(max, energy + Props.rechargePerSecond / 60f * (Overdriven ? 3f : 1f));
            }
        }

        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = false;
            Pawn wearer = Wearer;
            if (wearer == null || dinfo.Def == null)
            {
                return;
            }
            if (dinfo.Def.harmsHealth && Find.TickManager != null)
            {
                lastDamageTick = Find.TickManager.TicksGame;
            }
            if (!Active || dinfo.Def.ignoreShields || !Absorbs(dinfo, wearer))
            {
                return;
            }
            float amount = dinfo.Amount;
            energy = Energy - amount;
            charge = System.Math.Min(Props.chargeMax, charge + amount * Props.chargePerDamage);
            lastHitTick = Find.TickManager.TicksGame;
            lastHitAngle = dinfo.Angle;
            lastKeepDisplayTick = lastHitTick;
            absorbed = true;
            if (energy < 0f)
            {
                Break(wearer);
            }
            else if (wearer.Spawned)
            {
                Props.absorbSound?.PlayOneShot(new TargetInfo(wearer.Position, wearer.Map));
                MapComponent_JusticiarFx.Spawn(wearer.Map, new ShieldHitFx(wearer, lastHitAngle + 180f, Props.drawSize * 0.5f,
                    JVMath.Clamp(0.8f + amount / 30f, 0.8f, 1.6f)));
            }
        }

        // Gunfire, projectiles and blasts, and with absorbMelee the blows of a pawn's melee attack, struck with a weapon or
        // its own body (Verb_MeleeAttackDamage sets the tool, and the weapon or the attacker's race). Surgery, executions,
        // fire and the like are none of these.
        private bool Absorbs(DamageInfo dinfo, Pawn wearer)
        {
            if (dinfo.Def.isRanged || dinfo.Def.isExplosive)
            {
                return true;
            }
            if (!Props.absorbMelee || !dinfo.Def.harmsHealth || !(dinfo.Instigator is Pawn attacker) || attacker == wearer)
            {
                return false;
            }
            ThingDef weapon = dinfo.Weapon;
            return dinfo.Tool != null || weapon != null && (weapon.IsMeleeWeapon || weapon == attacker.def);
        }

        private void Break(Pawn wearer)
        {
            energy = 0f;
            ticksToReset = Props.resetDelayTicks;
            if (wearer.Spawned)
            {
                Props.breakSound?.PlayOneShot(new TargetInfo(wearer.Position, wearer.Map));
                MapComponent_JusticiarFx.Spawn(wearer.Map, new ShieldBreakFx(wearer, Props.drawSize * 0.5f));
            }
        }

        private void Reset(Pawn wearer)
        {
            ticksToReset = -1;
            energy = BaseEnergyMax * Props.energyOnReset;
            resetTick = Find.TickManager.TicksGame;
            if (wearer.Spawned)
            {
                Props.resetSound?.PlayOneShot(new TargetInfo(wearer.Position, wearer.Map));
                MapComponent_JusticiarFx.Spawn(wearer.Map, new ShieldRechargeFx(wearer, Props.drawSize * 0.5f));
            }
        }

        // Invictus: spends the stored charge, refills the shield and overcharges it for the given time.
        public float ReleaseCharge(int overdriveTicks)
        {
            float released = ChargeFraction;
            charge = 0f;
            ticksToReset = -1;
            overdriveUntilTick = Find.TickManager.TicksGame + overdriveTicks;
            energy = EnergyMax;
            lastKeepDisplayTick = Find.TickManager.TicksGame;
            return released;
        }

        public void KeepDisplaying()
        {
            lastKeepDisplayTick = Find.TickManager.TicksGame;
        }

        private bool ShouldDisplay(Pawn wearer)
        {
            if (!wearer.Spawned || wearer.Dead || wearer.Downed)
            {
                return false;
            }
            if (wearer.Drafted || wearer.InAggroMentalState || Overdriven)
            {
                return true;
            }
            if (wearer.Faction != null && wearer.Faction.HostileTo(Faction.OfPlayer) && !wearer.IsPrisoner)
            {
                return true;
            }
            return Find.TickManager.TicksGame < lastKeepDisplayTick + 1000;
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetWornGizmosExtra())
            {
                yield return gizmo;
            }
            Pawn wearer = Wearer;
            if (wearer != null && wearer.Faction == Faction.OfPlayer && Find.Selector.SingleSelectedThing == wearer)
            {
                yield return new Gizmo_PromachosArmor(this);
            }
            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Fill Invictus charge",
                    action = () =>
                    {
                        charge = Props.chargeMax;
                        lastDamageTick = Find.TickManager.TicksGame;
                    }
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Break shield",
                    action = () =>
                    {
                        Pawn w = Wearer;
                        if (w != null)
                        {
                            Break(w);
                        }
                    }
                };
            }
        }

        public override float CompGetSpecialApparelScoreOffset() => Props.energyPerSensitivity / 600f;

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            overdriveUntilTick = -1;
        }

        // ---- drawing ----

        public bool FxActiveOn(Map map)
        {
            Pawn wearer = Wearer;
            bool active = wearer != null && wearer.Spawned && wearer.Map == map && !parent.Destroyed;
            if (!active && registeredMap == map)
            {
                registeredMap = null;
            }
            return active;
        }

        public void DrawFx(FxDrawContext ctx)
        {
            Pawn wearer = Wearer;
            if (wearer == null || !Active || !ShouldDisplay(wearer))
            {
                return;
            }
            if (seed < 0f)
            {
                seed = FxDraw.Seed();
            }
            int ticks = Find.TickManager.TicksGame;
            float sinceHit = (ticks - lastHitTick) / 60f;
            float flash = sinceHit < 0.25f ? 1f - sinceHit / 0.25f : 0f;
            float uv = JVMath.ToUvRadians(lastHitAngle + 180f);
            float energyFraction = Energy / BaseEnergyMax;
            // After a reset the dome forms again as the recharge ring closes onto it.
            float formed = FxDraw.EaseOut((ticks - resetTick) / (ShieldRechargeFx.Closing * 60f));
            float size = Props.drawSize * (0.92f + 0.08f * JVMath.Clamp01(energyFraction)) * JVMath.Lerp(0.85f, 1f, formed);
            Vector3 pos = JVMath.WithY(wearer.DrawPos, FxDraw.AltitudeGlow);
            if (Overdriven)
            {
                // Invictus overdrive: a faint swirl of wind and feathers (Piloto Feathering Speed Aura) about the wearer.
                FxDraw.Clip(ctx, FxAssets.Overdrive, pos, 0f, size * 1.25f, size * 1.25f, FxDraw.LoopPhase(ctx.now, 2.4f, seed), 0.4f, 1f, true);
            }
            FxDraw.Quad(ctx, FxAssets.PsychicShield, pos, 0f, size, size, JVMath.WithAlpha(JVMath.White, formed),
                JVMath.V4(energyFraction, seed, FxClock.Wrapped(ctx.now), flash),
                JVMath.V4((float)System.Math.Cos(uv), (float)System.Math.Sin(uv), sinceHit, Overdriven ? 1f : 0f));
        }
    }
}
