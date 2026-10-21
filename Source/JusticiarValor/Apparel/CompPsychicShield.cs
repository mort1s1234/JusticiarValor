using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;
using Vector3 = UnityEngine.Vector3;

namespace JusticiarValor
{
    public class CompProperties_PsychicShield : CompProperties
    {
        // Damage the shield absorbs before it breaks.
        public float energyMax = 150f;

        public float rechargePerSecond = 10f;

        // Recharge waits this long after a hit.
        public int rechargeDelayTicks = 300;

        public int resetDelayTicks = 2400;

        // Fraction of the maximum restored when the shield comes back after breaking.
        public float energyOnReset = 0.4f;

        // Absorbed damage stored for Invictus, and its cap.
        public float chargePerDamage = 1f;

        public float chargeMax = 120f;

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

    // Promachos armor: a psychic shield drawn from a large eltex core. It absorbs incoming gunfire, projectiles and
    // blasts (not melee), and every absorbed hit is stored as energy for Invictus.
    public class CompPsychicShield : ThingComp, IFxDrawer
    {
        private float energy = -1f;
        private int ticksToReset = -1;
        private float charge;
        private int overdriveUntilTick = -1;
        private int lastHitTick = -9999;
        private int lastKeepDisplayTick = -9999;
        private float lastHitAngle;
        // When the shield last came back, for the dome to form again (not saved: only seconds of drawing depend on it).
        private int resetTick = -9999;
        private float seed = -1f;
        private Map registeredMap;

        public CompProperties_PsychicShield Props => (CompProperties_PsychicShield)props;

        public Pawn Wearer => JVUtility.WearerOf(parent);

        public bool Overdriven => Find.TickManager != null && Find.TickManager.TicksGame < overdriveUntilTick;

        public float EnergyMax => Props.energyMax * (Overdriven ? Props.overdriveEnergyFactor : 1f);

        public float Energy => energy < 0f ? Props.energyMax : energy;

        public bool Resetting => ticksToReset > 0;

        public bool Active => !Resetting && Wearer != null;

        public float Charge => charge;

        public float ChargeFraction => Props.chargeMax <= 0f ? 0f : JVMath.Clamp01(charge / Props.chargeMax);

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref energy, "energy", -1f);
            Scribe_Values.Look(ref ticksToReset, "ticksToReset", -1);
            Scribe_Values.Look(ref charge, "charge", 0f);
            Scribe_Values.Look(ref overdriveUntilTick, "overdriveUntilTick", -1);
            Scribe_Values.Look(ref lastKeepDisplayTick, "lastKeepDisplayTick", -9999);
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
            if (energy < 0f)
            {
                energy = Props.energyMax;
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
                // Overcharge drains back once Invictus ends.
                energy = System.Math.Max(max, energy - Props.energyMax / 120f);
            }
            else if (energy < max && (Overdriven || Find.TickManager.TicksGame - lastHitTick > Props.rechargeDelayTicks))
            {
                energy = System.Math.Min(max, energy + Props.rechargePerSecond / 60f * (Overdriven ? 3f : 1f));
            }
        }

        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = false;
            Pawn wearer = Wearer;
            if (!Active || wearer == null || dinfo.Def == null)
            {
                return;
            }
            if (dinfo.Def.ignoreShields || !(dinfo.Def.isRanged || dinfo.Def.isExplosive))
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
            energy = Props.energyMax * Props.energyOnReset;
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
                    action = () => charge = Props.chargeMax
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

        public override float CompGetSpecialApparelScoreOffset() => Props.energyMax / 600f;

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
            float energyFraction = Energy / Props.energyMax;
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
