using RimWorld;
using Verse;

namespace JusticiarValor
{
    // A pawn thrown back by Justiciar Valor. The game's own stun flyer (PawnFlyer_Stun) stuns its pawn on landing too, but
    // silently: no stun mote, nothing in the combat log and no instigator. This one stuns it as the wave's blow, by the
    // caster. Its ThingDef leaves stunDurationTicksRange at zero so the base class does not stun as well.
    public class PawnFlyer_MJV_Knockback : PawnFlyer
    {
        public Thing instigator;
        public int stunTicks;

        protected override void RespawnPawn()
        {
            Pawn pawn = FlyingPawn;
            base.RespawnPawn();
            if (pawn != null && pawn.Spawned && !pawn.Dead && stunTicks > 0)
            {
                pawn.stances?.stunner?.StunFor(stunTicks, instigator, true, true, false);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // A destroyed caster's reference would not resolve on load.
            if (Scribe.mode == LoadSaveMode.Saving && instigator != null && instigator.Destroyed)
            {
                instigator = null;
            }
            Scribe_References.Look(ref instigator, "instigator");
            Scribe_Values.Look(ref stunTicks, "stunTicks", 0);
        }
    }
}
