using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace JusticiarValor
{
    public static class JVUtility
    {
        public static T WornComp<T>(this Pawn pawn) where T : ThingComp
        {
            List<Apparel> worn = pawn?.apparel?.WornApparel;
            if (worn == null)
            {
                return null;
            }
            for (int i = 0; i < worn.Count; i++)
            {
                T comp = worn[i].GetComp<T>();
                if (comp != null)
                {
                    return comp;
                }
            }
            return null;
        }

        public static Pawn WearerOf(ThingWithComps thing) => (thing as Apparel)?.Wearer;

        // Awake or able to be: the Promachos helmet speaks of its wearer being conscious.
        public static bool IsConscious(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Downed && pawn.health.capacities.CanBeAwake;
        }

        // Pawns hostile to the caster around a cell.
        public static List<Pawn> HostilePawnsAround(Pawn caster, IntVec3 center, Map map, float radius)
        {
            var result = new List<Pawn>();
            if (map == null)
            {
                return result;
            }
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(center, map, radius, true))
            {
                if (thing is Pawn pawn && pawn != caster && !pawn.Dead && pawn.HostileTo(caster))
                {
                    result.Add(pawn);
                }
            }
            return result;
        }

        // A hostile that is actually fighting the pawn: GenHostility.IsActiveThreatTo measured against the pawn itself
        // (so a berserk or factionless pawn counts too). Downed, sleeping or fleeing pawns, prisoners, dormant mechanoids,
        // insects keeping to their hive and anything in the fog are not.
        public static bool IsActiveThreatTo(IAttackTarget target, Pawn pawn)
        {
            Thing thing = target?.Thing;
            if (thing == null || !thing.HostileTo(pawn) || thing.Fogged())
            {
                return false;
            }
            if (thing is Pawn other && other.GetLord()?.LordJob is LordJob_DefendAndExpandHive
                && other.mindState?.duty?.def != DutyDefOf.AssaultColony)
            {
                return false;
            }
            return GenHostility.IsPotentialThreat(target);
        }

        // Whether an active threat to the pawn stands within radius cells of it.
        public static bool ActiveThreatWithin(Pawn pawn, float radius)
        {
            float radiusSquared = radius * radius;
            List<IAttackTarget> potential = pawn.Map.attackTargetsCache.GetPotentialTargetsFor(pawn);
            for (int i = 0; i < potential.Count; i++)
            {
                if (potential[i].Thing.Position.DistanceToSquared(pawn.Position) <= radiusSquared && IsActiveThreatTo(potential[i], pawn))
                {
                    return true;
                }
            }
            return false;
        }

        // Whether a pawn a friendly-fire blow would catch is on the caster's side: a member of a faction not hostile to it
        // (colonists, guests, colony animals, allied visitors). Wild animals and other factionless pawns are not.
        public static bool IsAlly(Pawn caster, Pawn pawn) => pawn.Faction != null && !pawn.HostileTo(caster);

        // The foe a blow is meant for, among those it strikes: the one closest to the caster. Vanilla charges a third
        // faction no goodwill when one of its members is caught in the crossfire of a fight against a common enemy
        // (Faction.IsMutuallyHostileCrossfire, through DamageInfo.IntendedTarget), and charges it in full otherwise.
        public static Pawn ClosestFoe(Pawn caster, IEnumerable<Pawn> struck)
        {
            Pawn best = null;
            float bestDistance = float.MaxValue;
            foreach (Pawn pawn in struck)
            {
                if (pawn.HostileTo(caster))
                {
                    float distance = JVMath.DistanceFlatSquared(caster.DrawPos, pawn.DrawPos);
                    if (distance < bestDistance)
                    {
                        best = pawn;
                        bestDistance = distance;
                    }
                }
            }
            return best;
        }

        // Whether a pawn is attacking or aiming at the target right now.
        public static bool IsTargeting(Pawn pawn, Thing target)
        {
            if (pawn.mindState?.enemyTarget == target)
            {
                return true;
            }
            if (pawn.stances?.curStance is Stance_Busy busy && busy.focusTarg.Thing == target)
            {
                return true;
            }
            Job job = pawn.CurJob;
            if (job != null && job.targetA.Thing == target)
            {
                JobDef def = job.def;
                return def == JobDefOf.AttackMelee || def == JobDefOf.AttackStatic || def == JobDefOf.UseVerbOnThing
                    || def == JobDefOf.UseVerbOnThingStatic || def == JobDefOf.CastAbilityOnThing;
            }
            return false;
        }

        // The farthest standable cell within distance cells of the victim, moving away from the source; stops at walls.
        public static IntVec3 KnockbackCell(IntVec3 source, Pawn victim, float distance)
        {
            Map map = victim.Map;
            IntVec3 start = victim.Position;
            float dx = start.x - source.x;
            float dz = start.z - source.z;
            if (dx == 0f && dz == 0f)
            {
                float angle = Rand.Range(0f, 360f);
                dx = (float)System.Math.Sin(angle * JVMath.Deg2Rad);
                dz = (float)System.Math.Cos(angle * JVMath.Deg2Rad);
            }
            float len = (float)System.Math.Sqrt(dx * dx + dz * dz);
            dx /= len;
            dz /= len;
            IntVec3 best = start;
            for (float d = 0.5f; d <= distance + 0.01f; d += 0.5f)
            {
                var cell = new IntVec3((int)System.Math.Round(start.x + dx * d), 0, (int)System.Math.Round(start.z + dz * d));
                if (cell == best)
                {
                    continue;
                }
                if (!cell.InBounds(map) || !cell.Standable(map) || cell.GetDoor(map) != null)
                {
                    break;
                }
                best = cell;
            }
            return best;
        }
    }
}
