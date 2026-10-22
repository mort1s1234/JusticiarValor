using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;
using Color = UnityEngine.Color;
using Vector3 = UnityEngine.Vector3;

namespace JusticiarValor
{
    public class CompProperties_AbilityCelestialPierce : CompProperties_AbilityEffect
    {
        // Reach of the conjured blade, from the wielder.
        public float length = 3.6f;

        // Half the width of the strip of ground the blade sweeps through.
        public float halfWidth = 0.45f;

        // Whether the blade impales everyone on its line, allies included, or only the wielder's foes.
        public bool friendlyFire = true;

        public DamageDef damageDef;

        public float damage = 24f;

        public float armorPenetration = 0.85f;

        public int stunTicks = 20;

        public SoundDef conjureSound;

        public SoundDef hitSound;

        // Heard as the blade slams into the ground.
        public SoundDef impactSound;

        // Camera shake of the landing, as CompProperties_AbilityEffect.screenShakeIntensity.
        public float impactShake = 0.3f;

        public CompProperties_AbilityCelestialPierce()
        {
            compClass = typeof(CompAbilityEffect_CelestialPierce);
        }
    }

    // Celestial Pierce (energy psyblade): a spectral copy of the psyblade grows, lifts, flies over the target and slams into
    // the ground there; the ground splits along the line from the wielder, three cells long, and impales everyone on it,
    // friend or foe. The crack stops at walls. The victims are chosen at the cast and struck as the crack reaches them,
    // unless they have left the line by then, and the landing is heard and felt, on the timeline of CelestialPierceFx.
    public class CompAbilityEffect_CelestialPierce : CompAbilityEffect
    {
        // Targeting preview: the strip the blade sweeps, and the allies standing in it.
        private static readonly Color LineColor = new Color(0.78f, 0.87f, 0.82f, 1f);
        private static readonly Color AllyColor = new Color(1f, 0.35f, 0.28f, 1f);
        private static readonly List<Pawn> tmpVictims = new List<Pawn>();
        private static readonly HashSet<IntVec3> tmpCellSet = new HashSet<IntVec3>();
        private static readonly List<IntVec3> tmpCells = new List<IntVec3>();

        public new CompProperties_AbilityCelestialPierce Props => (CompProperties_AbilityCelestialPierce)props;

        // A cast's line: from the wielder towards the target, as far as the blade reaches before a wall. The copy slams
        // down slamAt along it (on the target, within the reach) and the fissure runs from there along the whole line.
        private struct Line
        {
            public Vector3 origin;
            public float angle;
            public float reach;
            public float slamAt;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            Map map = caster.Map;
            if (map == null)
            {
                return;
            }
            Line line = LineTo(caster, target);
            var hit = new List<Pawn>();
            Sweep(caster, line, hit, null);
            Pawn intended = target.Pawn != null && target.Pawn.HostileTo(caster) ? target.Pawn : JVUtility.ClosestFoe(caster, hit);
            Thing weapon = caster.equipment?.Primary;
            int now = Find.TickManager.TicksGame;
            Vector3 dir = JVMath.Direction(line.angle);
            Vector3 slam = JVMath.AlongAngle(line.origin, line.angle, line.slamAt);
            MapComponent_JusticiarStrikes.Schedule(map, new PendingStrike
            {
                landing = true,
                cell = slam.ToIntVec3(),
                tick = now + Ticks(CelestialPierceFx.Impact),
                hitSound = Props.impactSound,
                shake = Props.impactShake
            });
            foreach (Pawn pawn in hit)
            {
                // Those around the blade are struck as it lands, the others when the fissure reaches them, either way.
                float along = (pawn.DrawPos.x - line.origin.x) * dir.x + (pawn.DrawPos.z - line.origin.z) * dir.z;
                float delay = CelestialPierceFx.Impact + System.Math.Max(0f, System.Math.Abs(along - line.slamAt) - 0.35f) / CelestialPierceFx.CrackSpeed;
                MapComponent_JusticiarStrikes.Schedule(map, new PendingStrike
                {
                    victim = pawn,
                    caster = caster,
                    intendedTarget = intended,
                    tick = now + Ticks(delay),
                    angle = line.angle,
                    origin = line.origin,
                    reach = line.reach,
                    halfWidth = Props.halfWidth,
                    weapon = weapon?.def,
                    damageDef = Props.damageDef ?? DamageDefOf.Stab,
                    damage = Props.damage,
                    armorPenetration = Props.armorPenetration,
                    stunTicks = Props.stunTicks,
                    hitSound = Props.hitSound
                });
            }
            HeldWeaponPose(caster, weapon, line.angle, out Vector3 held, out float heldAngle, out bool mirrored);
            MapComponent_JusticiarFx.Spawn(map, new CelestialPierceFx(weapon, held, heldAngle, mirrored, slam, line.angle,
                line.slamAt - 0.7f, line.reach - line.slamAt));
            Props.conjureSound?.PlayOneShot(new TargetInfo(caster.Position, map));
            caster.Drawer?.Notify_MeleeAttackOn(target.Thing ?? caster);
        }

        private static int Ticks(float seconds) => (int)System.Math.Ceiling(seconds * GenTicks.TicksPerRealSecond);

        // Where and how the game draws a weapon aimed at `aim` (PawnRenderUtility.DrawEquipmentAndApparelExtras and
        // DrawEquipmentAiming), so the conjured copy starts exactly on the blade in the wielder's hand.
        private static void HeldWeaponPose(Pawn pawn, Thing weapon, float aim, out Vector3 pos, out float rotation, out bool mirrored)
        {
            float distance = 0.4f + (weapon?.def.equippedDistanceOffset ?? 0f);
            float factor = pawn.ageTracker?.CurLifeStage?.equipmentDrawDistanceFactor ?? 1f;
            pos = JVMath.AlongAngle(pawn.DrawPos, aim, distance * factor);
            float offset = weapon?.def.equippedAngleOffset ?? 0f;
            mirrored = aim > 200f && aim < 340f;
            rotation = mirrored ? aim - 90f - 180f - offset : aim - 90f + offset;
            rotation = (rotation % 360f + 360f) % 360f;
        }

        private Line LineTo(Pawn caster, LocalTargetInfo target)
        {
            var line = new Line { origin = caster.DrawPos };
            line.angle = JVMath.AngleFlat(line.origin, target.CenterVector3);
            line.reach = Reach(caster.Map, line.origin, line.angle);
            line.slamAt = System.Math.Min(line.reach, System.Math.Max(0.9f, JVMath.DistanceFlat(line.origin, target.CenterVector3)));
            return line;
        }

        // Everyone the blade impales along a line, each once, and the cells it sweeps (when cells is given).
        private void Sweep(Pawn caster, Line line, List<Pawn> victims, HashSet<IntVec3> cells)
        {
            Map map = caster.Map;
            for (float d = 0.6f; d <= line.reach + 0.01f; d += 0.25f)
            {
                for (int side = -1; side <= 1; side++)
                {
                    Vector3 point = JVMath.AlongAngle(JVMath.AlongAngle(line.origin, line.angle, d), line.angle + 90f, side * Props.halfWidth * 0.7f);
                    IntVec3 cell = point.ToIntVec3();
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }
                    cells?.Add(cell);
                    List<Thing> things = cell.GetThingList(map);
                    for (int i = 0; i < things.Count; i++)
                    {
                        if (things[i] is Pawn pawn && pawn != caster && !pawn.Dead && (Props.friendlyFire || pawn.HostileTo(caster))
                            && !victims.Contains(pawn))
                        {
                            victims.Add(pawn);
                        }
                    }
                }
            }
        }

        // How far the blade reaches before a wall stops it.
        private float Reach(Map map, Vector3 origin, float angle)
        {
            for (float d = 0.6f; d <= Props.length; d += 0.2f)
            {
                IntVec3 cell = JVMath.AlongAngle(origin, angle, d).ToIntVec3();
                if (!cell.InBounds(map) || cell.Impassable(map))
                {
                    return System.Math.Max(0.6f, d - 0.2f);
                }
            }
            return Props.length;
        }

        // How many of the wielder's allies (JVUtility.IsAlly) stand on the line towards target.
        private int AlliesInTheWay(LocalTargetInfo target, HashSet<IntVec3> cells = null)
        {
            Pawn caster = parent.pawn;
            tmpVictims.Clear();
            if (caster?.Map == null || !target.IsValid)
            {
                return 0;
            }
            Sweep(caster, LineTo(caster, target), tmpVictims, cells);
            int allies = 0;
            for (int i = 0; i < tmpVictims.Count; i++)
            {
                if (JVUtility.IsAlly(caster, tmpVictims[i]))
                {
                    allies++;
                }
            }
            return allies;
        }

        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            Pawn caster = parent.pawn;
            if (target.Pawn == null || target.Pawn.Downed || !target.Pawn.HostileTo(caster))
            {
                return false;
            }
            // The AI does not thrust through its own side.
            return !Props.friendlyFire || AlliesInTheWay(target) == 0;
        }

        // While aiming: the strip the blade will sweep, and the allies standing in it outlined in red.
        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            tmpCellSet.Clear();
            int allies = AlliesInTheWay(target, tmpCellSet);
            if (tmpCellSet.Count == 0)
            {
                return;
            }
            tmpCells.Clear();
            tmpCells.AddRange(tmpCellSet);
            GenDraw.DrawFieldEdges(tmpCells, LineColor);
            if (allies > 0)
            {
                tmpCells.Clear();
                Pawn caster = parent.pawn;
                for (int i = 0; i < tmpVictims.Count; i++)
                {
                    if (JVUtility.IsAlly(caster, tmpVictims[i]))
                    {
                        tmpCells.Add(tmpVictims[i].Position);
                    }
                }
                GenDraw.DrawFieldEdges(tmpCells, AllyColor);
            }
        }

        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            int allies = AlliesInTheWay(target);
            return allies > 0 ? (string)"MJV_PierceHitsAllies".Translate(allies) : null;
        }
    }
}
