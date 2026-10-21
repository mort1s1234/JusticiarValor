using RimWorld;
using Verse;
using Verse.Sound;

namespace JusticiarValor
{
    public class CompProperties_AbilityInvictus : CompProperties_AbilityEffect
    {
        // Stored energy needed to cast, as a fraction of the armor's charge capacity.
        public float minChargeFraction = 0.25f;

        // Overdrive length at the minimum and at a full charge.
        public FloatRange durationSeconds = new FloatRange(4f, 10f);

        public HediffDef hediff;

        public SoundDef releaseSound;

        // The AI releases Invictus in combat once it has stored at least this much.
        public float aiMinChargeFraction = 0.5f;

        public CompProperties_AbilityInvictus()
        {
            compClass = typeof(CompAbilityEffect_Invictus);
        }
    }

    // Invictus (Promachos armor): releases the energy the psychic shield stored from absorbed hits into the shield and
    // the armor's servos: the shield is refilled and overcharged, and the wearer moves and strikes far faster.
    public class CompAbilityEffect_Invictus : CompAbilityEffect
    {
        public new CompProperties_AbilityInvictus Props => (CompProperties_AbilityInvictus)props;

        private CompPsychicShield Shield => parent.pawn.WornComp<CompPsychicShield>();

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn pawn = parent.pawn;
            CompPsychicShield shield = Shield;
            if (shield == null)
            {
                return;
            }
            float fraction = shield.ChargeFraction;
            int ticks = (int)(JVMath.Lerp(Props.durationSeconds.min, Props.durationSeconds.max,
                JVMath.InverseLerp(Props.minChargeFraction, 1f, fraction)) * 60f);
            shield.ReleaseCharge(ticks);
            if (Props.hediff != null)
            {
                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediff);
                if (existing != null)
                {
                    pawn.health.RemoveHediff(existing);
                }
                Hediff hediff = HediffMaker.MakeHediff(Props.hediff, pawn);
                HediffComp_Disappears disappears = hediff.TryGetComp<HediffComp_Disappears>();
                if (disappears != null)
                {
                    disappears.ticksToDisappear = ticks;
                }
                pawn.health.AddHediff(hediff);
            }
            if (pawn.Spawned)
            {
                MapComponent_JusticiarFx.Spawn(pawn.Map, new InvictusBurstFx(pawn));
                Props.releaseSound?.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
            }
        }

        public override bool GizmoDisabled(out string reason)
        {
            CompPsychicShield shield = Shield;
            if (shield == null)
            {
                reason = "MJV_InvictusNoArmor".Translate();
                return true;
            }
            if (shield.ChargeFraction < Props.minChargeFraction)
            {
                reason = "MJV_InvictusNotCharged".Translate(shield.ChargeFraction.ToStringPercent(), Props.minChargeFraction.ToStringPercent());
                return true;
            }
            return base.GizmoDisabled(out reason);
        }

        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            Pawn pawn = parent.pawn;
            CompPsychicShield shield = Shield;
            if (shield == null || shield.ChargeFraction < Props.aiMinChargeFraction || pawn.Map == null)
            {
                return false;
            }
            if (Props.hediff != null && pawn.health.hediffSet.HasHediff(Props.hediff))
            {
                return false;
            }
            return JVUtility.HostilePawnsAround(pawn, pawn.Position, pawn.Map, 14f).Count > 0;
        }
    }
}
