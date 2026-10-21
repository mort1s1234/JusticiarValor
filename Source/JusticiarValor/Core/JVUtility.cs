using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

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
