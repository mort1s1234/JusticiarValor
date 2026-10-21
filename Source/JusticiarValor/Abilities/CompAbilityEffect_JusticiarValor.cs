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
    // enemies back, damages them and leaves them dazed.
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
            List<Pawn> enemies = JVUtility.HostilePawnsAround(caster, caster.Position, caster.Map, Props.radius);
            int standing = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                Pawn enemy = enemies[i];
                if (enemy.Downed)
                {
                    continue;
                }
                standing++;
                if (enemy.Position.AdjacentTo8WayOrInside(caster.Position) && JVUtility.IsTargeting(enemy, caster))
                {
                    return true;
                }
            }
            return standing >= Props.aiMinTargets;
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

            var victims = new List<Pawn>();
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(caster.Position, map, props.radius, true))
            {
                if (thing is Pawn pawn && pawn != caster && !pawn.Dead && (pawn == extraVictim || pawn.HostileTo(caster)))
                {
                    victims.Add(pawn);
                }
            }
            if (extraVictim != null && extraVictim.Spawned && extraVictim.Map == map && !victims.Contains(extraVictim))
            {
                victims.Add(extraVictim);
            }
            for (int i = 0; i < victims.Count; i++)
            {
                Hit(caster, victims[i], props);
            }
        }

        private static void Hit(Pawn caster, Pawn victim, CompProperties_AbilityJusticiarValor props)
        {
            Map map = victim.Map;
            float angle = JVMath.AngleFlat(caster.DrawPos, victim.DrawPos);
            if (props.damageDef != null && props.damage > 0f)
            {
                victim.TakeDamage(new DamageInfo(props.damageDef, props.damage, props.armorPenetration, angle, caster));
            }
            if (victim.Dead || !victim.Spawned)
            {
                return;
            }
            if (props.dazeHediff != null)
            {
                Hediff existing = victim.health.hediffSet.GetFirstHediffOfDef(props.dazeHediff);
                if (existing != null)
                {
                    victim.health.RemoveHediff(existing);
                }
                victim.health.AddHediff(props.dazeHediff);
            }
            if (!victim.Downed && victim.BodySize <= props.maxKnockbackBodySize && !victim.InBed() && MJV_DefOf.PawnFlyer_Stun != null)
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
