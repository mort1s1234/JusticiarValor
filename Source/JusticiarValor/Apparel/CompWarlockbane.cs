using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace JusticiarValor
{
    public class CompProperties_Warlockbane : CompProperties
    {
        public int maxCharges = 2;

        // Time to regain one charge.
        public int rechargeTicks = 2400;

        public SoundDef deflectSound;

        // The caster of a deflected psychic attack is stunned for this long by the backlash, from when the attack flies back
        // into it.
        public int backlashStunTicks = 90;

        // Heard on the caster as the attack strikes it.
        public SoundDef backlashSound;

        // ...and takes this much neural heat, if it has any.
        public float backlashEntropy = 12f;

        public CompProperties_Warlockbane()
        {
            compClass = typeof(CompWarlockbane);
        }
    }

    // Warlockbane shield: turns aside psychic attacks on its bearer, a charge each. What is one, and when the bearer turns
    // it aside: PsychicAttack, WarlockbaneWard.
    public class CompWarlockbane : ThingComp
    {
        private int charges = -1;
        private int rechargeProgress;

        public CompProperties_Warlockbane Props => (CompProperties_Warlockbane)props;

        public int Charges => charges < 0 ? Props.maxCharges : charges;

        public float RechargeFraction => Charges >= Props.maxCharges ? 1f : (float)rechargeProgress / Props.rechargeTicks;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref charges, "charges", -1);
            Scribe_Values.Look(ref rechargeProgress, "rechargeProgress", 0);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (Charges >= Props.maxCharges)
            {
                rechargeProgress = 0;
                return;
            }
            if (JVUtility.WearerOf(parent) == null)
            {
                return;
            }
            rechargeProgress++;
            if (rechargeProgress >= Props.rechargeTicks)
            {
                rechargeProgress = 0;
                charges = Charges + 1;
            }
        }

        // Whether the bearer turns aside this caster's attack; spends a charge, plays the deflection and stuns the caster:
        // when the attack flying back strikes it (DeflectFx.HitTime), at once if none is seen flying.
        // The backlash's neural heat is BurnCaster, once the caster has paid for the cast.
        public bool TryDeflect(Pawn caster, Pawn bearer)
        {
            if (Charges <= 0)
            {
                return false;
            }
            charges = Charges - 1;
            float hitTime = 0f;
            if (bearer.Spawned)
            {
                float fromAngle = caster != null && caster.Spawned && caster.Map == bearer.Map
                    ? JVMath.AngleFlat(bearer.DrawPos, caster.DrawPos) : FxDraw.Seed() * 360f;
                Pawn shownCaster = caster != null && caster.Spawned && caster.Map == bearer.Map && caster != bearer ? caster : null;
                var fx = new DeflectFx(bearer, fromAngle, shownCaster);
                MapComponent_JusticiarFx.Spawn(bearer.Map, fx);
                hitTime = fx.HitTime;
                Props.deflectSound?.PlayOneShot(new TargetInfo(bearer.Position, bearer.Map));
                MoteMaker.ThrowText(bearer.DrawPos, bearer.Map, "MJV_Deflected".Translate(), 2.5f);
            }
            if (caster != null && !caster.Dead && caster != bearer && caster.Spawned)
            {
                var backlash = new PendingBacklash
                {
                    victim = caster,
                    caster = bearer,
                    tick = Find.TickManager.TicksGame + (int)Math.Ceiling(hitTime * GenTicks.TicksPerRealSecond),
                    stunTicks = Props.backlashStunTicks,
                    hitSound = Props.backlashSound
                };
                if (hitTime > 0f)
                {
                    MapComponent_JusticiarStrikes.Schedule(caster.Map, backlash);
                }
                else
                {
                    backlash.Land(caster.Map);
                }
            }
            return true;
        }

        // The backlash's neural heat, on top of the heat the caster paid for the deflected cast: past its limit if need be,
        // which is why it comes after the cast's own heat (added only while it fits).
        public void BurnCaster(Pawn caster, Pawn bearer)
        {
            if (caster != null && !caster.Dead && caster != bearer && Props.backlashEntropy > 0f
                && caster.psychicEntropy != null && caster.psychicEntropy.NeedsPsyfocus)
            {
                caster.psychicEntropy.TryAddEntropy(Props.backlashEntropy, bearer, true, true);
            }
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetWornGizmosExtra())
            {
                yield return gizmo;
            }
            Pawn wearer = JVUtility.WearerOf(parent);
            if (wearer != null && wearer.Faction == Faction.OfPlayer && Find.Selector.SingleSelectedThing == wearer)
            {
                yield return new Gizmo_WarlockbaneStatus(this);
            }
        }
    }

    // The Warlockbane's backlash, landing as the deflected attack strikes its caster: victim is that caster, caster the
    // bearer who turned the attack aside.
    public class PendingBacklash : PendingStrike
    {
        public override void Land(Map map)
        {
            if (victim == null || victim.Dead || !victim.Spawned || victim.Map != map)
            {
                return;
            }
            if (stunTicks > 0)
            {
                victim.stances?.stunner?.StunFor(stunTicks, caster, true, true, false);
            }
            hitSound?.PlayOneShot(new TargetInfo(victim.Position, map));
        }
    }
}
