using RimWorld;
using Verse;
using Verse.Sound;
using Vector3 = UnityEngine.Vector3;

namespace JusticiarValor
{
    public class PsyOrbExtension : DefModExtension
    {
        // Slows the pawn the orb hits.
        public HediffDef slowHediff;

        public SoundDef impactSound;

        public float drawSize = 0.7f;
    }

    // A psy-orb launched by the energy psyspear: burns what it hits and slows pawns for a few seconds.
    // The map effect component draws the Piloto orb animation and its wake.
    public class Projectile_PsyOrb : Projectile, IFxDrawer
    {
        private float seed = -1f;

        private PsyOrbExtension Extension => def.GetModExtension<PsyOrbExtension>();

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            MapComponent_JusticiarFx.Register(map, this);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map map = Map;
            Vector3 pos = ExactPosition;
            float angle = JVMath.AngleFlat(origin, destination);
            PsyOrbExtension ext = Extension;
            base.Impact(hitThing, blockedByShield);
            if (map == null)
            {
                return;
            }
            if (hitThing != null && !blockedByShield)
            {
                var dinfo = new DamageInfo(DamageDef, DamageAmount, ArmorPenetration, angle, launcher, null, equipmentDef,
                    DamageInfo.SourceCategory.ThingOrUnknown, intendedTarget.Thing);
                hitThing.TakeDamage(dinfo);
                if (ext?.slowHediff != null && hitThing is Pawn pawn && !pawn.Dead)
                {
                    Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(ext.slowHediff);
                    if (existing != null)
                    {
                        pawn.health.RemoveHediff(existing);
                    }
                    pawn.health.AddHediff(ext.slowHediff);
                }
            }
            if (seed < 0f)
            {
                seed = FxDraw.Seed();
            }
            float flown = JVMath.DistanceFlat(origin, pos);
            MapComponent_JusticiarFx.Spawn(map, new OrbWakeFx(pos, angle, flown, System.Math.Min(flown, OrbWake.MaxLength), seed));
            MapComponent_JusticiarFx.Spawn(map, new OrbImpactFx(pos, angle));
            ext?.impactSound?.PlayOneShot(new TargetInfo(pos.ToIntVec3(), map));
        }

        public bool FxActiveOn(Map map) => Spawned && !Destroyed && Map == map;

        public void DrawFx(FxDrawContext ctx)
        {
            if (seed < 0f)
            {
                seed = FxDraw.Seed();
            }
            Vector3 pos = JVMath.WithY(ExactPosition, FxDraw.AltitudeGlow);
            float angle = JVMath.AngleFlat(origin, destination);
            // The wake grows over the first stretch of the flight; it never reaches back past the launch point.
            float flown = JVMath.DistanceFlat(origin, pos);
            OrbWake.Draw(ctx, pos, angle, flown, System.Math.Min(flown, OrbWake.MaxLength), seed, 0.95f, 0f);
            float size = (Extension?.drawSize ?? 0.7f) * 0.8f;
            FxDraw.Clip(ctx, FxAssets.PsyOrb, pos, 0f, size, size,
                FxDraw.LoopPhase(ctx.now, 2.4f, seed), 0.88f, 1.15f, true);
            // A turning glint (Piloto StarCrossFlare) on the orb.
            FxDraw.Sprite(ctx, FxAssets.SpriteOver, pos, FxClock.Wrapped(ctx.now) * 140f % 360f, size * 1.5f, size * 1.5f,
                FxSheet.CrossFlare, 0.55f, 1.5f);
        }
    }
}
