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
        // Radius of the strike around the cell the blade lands on (1.5: that cell and the eight around it).
        public float radius = 1.5f;

        // Whether the blade impales everyone around its landing, allies included, or only the wielder's foes.
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
    // the ground there. The ground breaks where it lands and everyone within the radius is impaled, friend or foe; walls
    // shield those behind them. The victims are chosen at the cast and struck as the shock of the landing reaches them,
    // unless they have left the area by then, on the timeline of CelestialPierceFx.
    public class CompAbilityEffect_CelestialPierce : CompAbilityEffect
    {
        // Targeting preview: the ground the blade will break, and the allies standing on it.
        private static readonly Color AreaColor = new Color(0.78f, 0.87f, 0.82f, 1f);
        private static readonly Color AllyColor = new Color(1f, 0.35f, 0.28f, 1f);
        private static readonly List<Pawn> tmpVictims = new List<Pawn>();
        private static readonly List<IntVec3> tmpCells = new List<IntVec3>();

        public new CompProperties_AbilityCelestialPierce Props => (CompProperties_AbilityCelestialPierce)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            Map map = caster.Map;
            if (map == null)
            {
                return;
            }
            IntVec3 center = target.Cell;
            Vector3 slam = target.CenterVector3;
            var hit = new List<Pawn>();
            Victims(caster, center, hit, null);
            Pawn intended = target.Pawn != null && target.Pawn.HostileTo(caster) ? target.Pawn : JVUtility.ClosestFoe(caster, hit);
            Thing weapon = caster.equipment?.Primary;
            int now = Find.TickManager.TicksGame;
            float angle = JVMath.AngleFlat(caster.DrawPos, slam);
            Vector3 areaCenter = center.ToVector3Shifted();
            MapComponent_JusticiarStrikes.Schedule(map, new PendingStrike
            {
                landing = true,
                cell = center,
                tick = now + Ticks(CelestialPierceFx.Impact),
                hitSound = Props.impactSound,
                shake = Props.impactShake
            });
            foreach (Pawn pawn in hit)
            {
                // Struck as the shock of the landing spreads out to them.
                float distance = JVMath.DistanceFlat(pawn.DrawPos, slam);
                float delay = CelestialPierceFx.Impact + System.Math.Max(0f, distance - 0.35f) / CelestialPierceFx.ShockSpeed;
                MapComponent_JusticiarStrikes.Schedule(map, new PendingStrike
                {
                    victim = pawn,
                    caster = caster,
                    intendedTarget = intended,
                    tick = now + Ticks(delay),
                    angle = JVMath.AngleFlat(slam, pawn.DrawPos),
                    areaCenter = areaCenter,
                    areaRadius = Props.radius,
                    weapon = weapon?.def,
                    damageDef = Props.damageDef ?? DamageDefOf.Stab,
                    damage = Props.damage,
                    armorPenetration = Props.armorPenetration,
                    stunTicks = Props.stunTicks,
                    hitSound = Props.hitSound
                });
            }
            HeldWeaponPose(caster, weapon, angle, out Vector3 held, out float heldAngle, out bool mirrored);
            // The cracks split out from the landing about as far as the strike reaches, both ways along the throw.
            MapComponent_JusticiarFx.Spawn(map, new CelestialPierceFx(weapon, held, heldAngle, mirrored, slam, angle, Props.radius, Props.radius));
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

        // The cells the landing breaks: within the radius of the center and in its line of sight, so walls shield those
        // behind them.
        private void AreaCells(Map map, IntVec3 center, List<IntVec3> cells)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, Props.radius, true))
            {
                if (cell.InBounds(map) && (cell == center || GenSight.LineOfSight(center, cell, map, true)))
                {
                    cells.Add(cell);
                }
            }
        }

        // Everyone the blade impales around a landing cell, each once, and the cells it breaks (when cells is given).
        private void Victims(Pawn caster, IntVec3 center, List<Pawn> victims, List<IntVec3> cells)
        {
            Map map = caster.Map;
            List<IntVec3> area = cells ?? new List<IntVec3>();
            area.Clear();
            if (!center.InBounds(map))
            {
                return;
            }
            AreaCells(map, center, area);
            for (int c = 0; c < area.Count; c++)
            {
                List<Thing> things = area[c].GetThingList(map);
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

        // How many of the wielder's allies (JVUtility.IsAlly) stand where the blade would land on target.
        private int AlliesInTheWay(LocalTargetInfo target, List<IntVec3> cells = null)
        {
            Pawn caster = parent.pawn;
            tmpVictims.Clear();
            if (caster?.Map == null || !target.IsValid)
            {
                return 0;
            }
            Victims(caster, target.Cell, tmpVictims, cells);
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
            // The AI does not bring the blade down on its own side.
            return !Props.friendlyFire || AlliesInTheWay(target) == 0;
        }

        // While aiming: the ground the blade will break, and the allies standing on it outlined in red.
        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            tmpCells.Clear();
            int allies = AlliesInTheWay(target, tmpCells);
            if (tmpCells.Count == 0)
            {
                return;
            }
            GenDraw.DrawFieldEdges(tmpCells, AreaColor);
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
