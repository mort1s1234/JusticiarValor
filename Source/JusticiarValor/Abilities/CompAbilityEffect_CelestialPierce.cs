using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;
using Vector3 = UnityEngine.Vector3;

namespace JusticiarValor
{
    public class CompProperties_AbilityCelestialPierce : CompProperties_AbilityEffect
    {
        // Reach of the conjured blade, from the wielder.
        public float length = 3.6f;

        // Half the width of the strip of ground the blade sweeps through.
        public float halfWidth = 0.45f;

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
    // the ground there; the ground splits along the line from the wielder, three cells long, and impales every foe on it.
    // The crack stops at walls. The foes are chosen at the cast and struck as the crack reaches them, and the landing is
    // heard and felt, on the timeline of CelestialPierceFx.
    public class CompAbilityEffect_CelestialPierce : CompAbilityEffect
    {
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
            Vector3 origin = caster.DrawPos;
            float angle = JVMath.AngleFlat(origin, target.CenterVector3);
            float reach = Reach(caster, angle);
            // The copy slams down on the target (within the reach); the fissure runs from there along the whole line.
            float slamAt = System.Math.Min(reach, System.Math.Max(0.9f, JVMath.DistanceFlat(origin, target.CenterVector3)));
            var hit = new HashSet<Pawn>();
            for (float d = 0.6f; d <= reach + 0.01f; d += 0.25f)
            {
                for (int side = -1; side <= 1; side++)
                {
                    Vector3 point = JVMath.AlongAngle(JVMath.AlongAngle(origin, angle, d), angle + 90f, side * Props.halfWidth * 0.7f);
                    IntVec3 cell = point.ToIntVec3();
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }
                    List<Thing> things = cell.GetThingList(map);
                    for (int i = 0; i < things.Count; i++)
                    {
                        if (things[i] is Pawn pawn && pawn != caster && !pawn.Dead && pawn.HostileTo(caster))
                        {
                            hit.Add(pawn);
                        }
                    }
                }
            }
            Thing weapon = caster.equipment?.Primary;
            int now = Find.TickManager.TicksGame;
            Vector3 dir = JVMath.Direction(angle);
            Vector3 slam = JVMath.AlongAngle(origin, angle, slamAt);
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
                float along = (pawn.DrawPos.x - origin.x) * dir.x + (pawn.DrawPos.z - origin.z) * dir.z;
                float delay = CelestialPierceFx.Impact + System.Math.Max(0f, System.Math.Abs(along - slamAt) - 0.35f) / CelestialPierceFx.CrackSpeed;
                MapComponent_JusticiarStrikes.Schedule(map, new PendingStrike
                {
                    victim = pawn,
                    caster = caster,
                    tick = now + Ticks(delay),
                    angle = angle,
                    weapon = weapon?.def,
                    damageDef = Props.damageDef ?? DamageDefOf.Stab,
                    damage = Props.damage,
                    armorPenetration = Props.armorPenetration,
                    stunTicks = Props.stunTicks,
                    hitSound = Props.hitSound
                });
            }
            HeldWeaponPose(caster, weapon, angle, out Vector3 held, out float heldAngle, out bool mirrored);
            MapComponent_JusticiarFx.Spawn(map, new CelestialPierceFx(weapon, held, heldAngle, mirrored, slam, angle, slamAt - 0.7f, reach - slamAt));
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

        // How far the blade reaches before a wall stops it.
        private float Reach(Pawn caster, float angle)
        {
            Map map = caster.Map;
            Vector3 origin = caster.DrawPos;
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

        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            return target.Pawn != null && !target.Pawn.Downed && target.Pawn.HostileTo(parent.pawn);
        }
    }
}
