using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;
using Vector3 = UnityEngine.Vector3;

namespace JusticiarValor
{
    public class CompProperties_AbilityJusticiarValor : CompProperties_AbilityEffect
    {
        public float radius = 4.9f;

        // Whether the wave strikes everyone around the wearer, allies included, or only the wearer's foes.
        public bool friendlyFire = true;

        public DamageDef damageDef;

        public float damage = 14f;

        public float armorPenetration = 0.4f;

        // How far the wave throws enemies; they land stunned (PawnFlyer_Stun).
        public float knockbackDistance = 3f;

        // Heavier pawns are only staggered.
        public float maxKnockbackBodySize = 2.5f;

        // Stun for pawns that were not thrown.
        public IntRange stunTicks = new IntRange(60, 120);

        // The daze that slows the wave's victims afterwards.
        public HediffDef dazeHediff;

        public SoundDef releaseSound;

        // The AI releases the wave once this many enemies are inside it (or one is in melee with the wearer).
        public int aiMinTargets = 2;

        public CompProperties_AbilityJusticiarValor()
        {
            compClass = typeof(CompAbilityEffect_JusticiarValor);
        }
    }

    // Justiciar Valor (Promachos helmet): the eltex resonates and releases a psychic wave around the wearer that throws
    // everyone around back, allies included, damages them and leaves them dazed.
    public class CompAbilityEffect_JusticiarValor : CompAbilityEffect
    {
        public new CompProperties_AbilityJusticiarValor Props => (CompProperties_AbilityJusticiarValor)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            JusticiarValorWave.Release(parent.pawn, Props, null);
        }

        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            Pawn caster = parent.pawn;
            if (caster?.Map == null)
            {
                return false;
            }
            List<Pawn> caught = JusticiarValorWave.Victims(caster, Props, null);
            int standing = 0;
            bool threatened = false;
            for (int i = 0; i < caught.Count; i++)
            {
                Pawn pawn = caught[i];
                if (JVUtility.IsAlly(caster, pawn))
                {
                    // The AI does not blast its own side.
                    return false;
                }
                if (!pawn.HostileTo(caster) || pawn.Downed)
                {
                    continue;
                }
                standing++;
                threatened |= pawn.Position.AdjacentTo8WayOrInside(caster.Position) && JVUtility.IsTargeting(pawn, caster);
            }
            return threatened || standing >= Props.aiMinTargets;
        }

        // While the gizmo is hovered (the game draws the wave's radius): the allies it would strike.
        public override void OnGizmoUpdate()
        {
            Pawn caster = parent.pawn;
            if (caster?.Map == null || !Props.friendlyFire)
            {
                return;
            }
            List<Pawn> caught = JusticiarValorWave.Victims(caster, Props, null);
            for (int i = 0; i < caught.Count; i++)
            {
                if (JVUtility.IsAlly(caster, caught[i]))
                {
                    GenDraw.DrawTargetHighlight(caught[i]);
                }
            }
        }
    }

    public static class JusticiarValorWave
    {
        // The wave's settings for a wearer: those of the Justiciar Valor ability (the helmet's), else the defaults.
        public static CompProperties_AbilityJusticiarValor PropsFor(AbilityDef def)
        {
            if (def?.comps != null)
            {
                for (int i = 0; i < def.comps.Count; i++)
                {
                    if (def.comps[i] is CompProperties_AbilityJusticiarValor props)
                    {
                        return props;
                    }
                }
            }
            return new CompProperties_AbilityJusticiarValor();
        }

        // Everyone the wave strikes around the caster: all but the caster with friendly fire, else the caster's foes.
        // extraVictim is struck either way: an arrester.
        public static List<Pawn> Victims(Pawn caster, CompProperties_AbilityJusticiarValor props, Pawn extraVictim)
        {
            var victims = new List<Pawn>();
            Map map = caster.Map;
            if (map == null)
            {
                return victims;
            }
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(caster.Position, map, props.radius, true))
            {
                if (thing is Pawn pawn && pawn != caster && !pawn.Dead && (props.friendlyFire || pawn == extraVictim || pawn.HostileTo(caster)))
                {
                    victims.Add(pawn);
                }
            }
            if (extraVictim != null && extraVictim.Spawned && extraVictim.Map == map && !victims.Contains(extraVictim))
            {
                victims.Add(extraVictim);
            }
            return victims;
        }

        // Releases the wave around the caster. extraVictim is hit even if not hostile: an arrester.
        public static void Release(Pawn caster, CompProperties_AbilityJusticiarValor props, Pawn extraVictim)
        {
            Map map = caster.Map;
            if (map == null)
            {
                return;
            }
            Vector3 center = caster.DrawPos;
            MapComponent_JusticiarFx.Spawn(map, new PsychicWaveFx(center, props.radius));
            props.releaseSound?.PlayOneShot(new TargetInfo(caster.Position, map));
            if (map == Find.CurrentMap)
            {
                Find.CameraDriver.shaker.DoShake(0.6f);
            }

            List<Pawn> victims = Victims(caster, props, extraVictim);
            // The arrester is the one the wave answers; otherwise the closest foe (vanilla crossfire for third parties).
            Pawn intended = extraVictim ?? JVUtility.ClosestFoe(caster, victims);
            for (int i = 0; i < victims.Count; i++)
            {
                Hit(caster, victims[i], props, intended);
            }
        }

        private static void Hit(Pawn caster, Pawn victim, CompProperties_AbilityJusticiarValor props, Pawn intended)
        {
            Map map = victim.Map;
            float angle = JVMath.AngleFlat(caster.DrawPos, victim.DrawPos);
            if (props.damageDef != null && props.damage > 0f)
            {
                victim.TakeDamage(new DamageInfo(props.damageDef, props.damage, props.armorPenetration, angle, caster, null, null,
                    DamageInfo.SourceCategory.ThingOrUnknown, intended));
            }
            if (victim.Dead || !victim.Spawned)
            {
                return;
            }
            // Whether the blow throws the victim back is settled before the daze, which must never decide it.
            bool thrown = !victim.Downed && victim.BodySize <= props.maxKnockbackBodySize && !victim.InBed() && MJV_DefOf.PawnFlyer_Stun != null;
            if (props.dazeHediff != null)
            {
                Hediff existing = victim.health.hediffSet.GetFirstHediffOfDef(props.dazeHediff);
                if (existing != null)
                {
                    victim.health.RemoveHediff(existing);
                }
                victim.health.AddHediff(props.dazeHediff);
            }
            if (thrown)
            {
                IntVec3 from = victim.Position;
                IntVec3 dest = JVUtility.KnockbackCell(caster.Position, victim, props.knockbackDistance);
                if (dest != from)
                {
                    PawnFlyer flyer = PawnFlyer.MakeFlyer(MJV_DefOf.PawnFlyer_Stun, victim, dest, null, null);
                    if (flyer != null)
                    {
                        GenSpawn.Spawn(flyer, from, map);
                        return;
                    }
                }
            }
            victim.stances?.stunner?.StunFor(props.stunTicks.RandomInRange, caster, true, true, false);
        }
    }
}
