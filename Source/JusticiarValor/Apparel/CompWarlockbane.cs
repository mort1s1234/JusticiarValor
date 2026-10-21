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

        // The caster of a deflected psycast is stunned for this long by the backlash.
        public int backlashStunTicks = 90;

        // ...and takes this much neural heat, if it has any.
        public float backlashEntropy = 12f;

        public CompProperties_Warlockbane()
        {
            compClass = typeof(CompWarlockbane);
        }
    }

    // Warlockbane shield: turns aside hostile psycasts aimed at its bearer, a charge each.
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

        // Whether the bearer turns this ability aside; spends a charge and plays the deflection.
        public bool TryDeflect(Ability ability, Pawn bearer)
        {
            if (Charges <= 0)
            {
                return false;
            }
            charges = Charges - 1;
            Pawn caster = ability.pawn;
            if (bearer.Spawned)
            {
                float fromAngle = caster != null && caster.Spawned && caster.Map == bearer.Map
                    ? JVMath.AngleFlat(bearer.DrawPos, caster.DrawPos) : FxDraw.Seed() * 360f;
                Pawn shownCaster = caster != null && caster.Spawned && caster.Map == bearer.Map && caster != bearer ? caster : null;
                MapComponent_JusticiarFx.Spawn(bearer.Map, new DeflectFx(bearer, fromAngle, shownCaster));
                Props.deflectSound?.PlayOneShot(new TargetInfo(bearer.Position, bearer.Map));
                MoteMaker.ThrowText(bearer.DrawPos, bearer.Map, "MJV_Deflected".Translate(), 2.5f);
            }
            if (caster != null && !caster.Dead && caster != bearer)
            {
                if (caster.Spawned && Props.backlashStunTicks > 0)
                {
                    caster.stances?.stunner?.StunFor(Props.backlashStunTicks, bearer, true, true, false);
                }
                if (Props.backlashEntropy > 0f && caster.psychicEntropy != null && caster.psychicEntropy.NeedsPsyfocus)
                {
                    caster.psychicEntropy.TryAddEntropy(Props.backlashEntropy, bearer, true, true);
                }
            }
            return true;
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
}
