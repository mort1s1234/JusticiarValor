using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;
using Vector3 = UnityEngine.Vector3;

namespace JusticiarValor
{
    // A hit that lands a moment after its cast: Celestial Pierce strikes each foe when its fissure reaches them. A landing
    // strike has no victim: it is the blade going into the ground at `cell`, heard and shaking the camera.
    public class PendingStrike : IExposable
    {
        public bool landing;
        public IntVec3 cell;
        public float shake;
        public Pawn victim;
        public Pawn caster;
        public int tick;
        public float angle;
        public ThingDef weapon;
        public DamageDef damageDef;
        public float damage;
        public float armorPenetration;
        public int stunTicks;
        public SoundDef hitSound;

        public void Land(Map map)
        {
            if (landing)
            {
                hitSound?.PlayOneShot(new TargetInfo(cell, map));
                if (shake > 0f && map == Find.CurrentMap && Find.CameraDriver.CurrentViewRect.ExpandedBy(8).Contains(cell))
                {
                    Find.CameraDriver.shaker.DoShake(shake);
                }
                return;
            }
            if (victim == null || victim.Dead || !victim.Spawned || victim.Map != map)
            {
                return;
            }
            Vector3 at = victim.DrawPos;
            IntVec3 where = victim.Position;
            victim.TakeDamage(new DamageInfo(damageDef ?? DamageDefOf.Stab, damage, armorPenetration, angle, caster, null, weapon));
            if (!victim.Dead && victim.Spawned && stunTicks > 0)
            {
                victim.stances?.stunner?.StunFor(stunTicks, caster, false, false, false);
            }
            MapComponent_JusticiarFx.Spawn(map, new PierceHitFx(at, angle));
            hitSound?.PlayOneShot(new TargetInfo(where, map));
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref landing, "landing");
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref shake, "shake");
            Scribe_References.Look(ref victim, "victim");
            Scribe_References.Look(ref caster, "caster");
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref angle, "angle");
            Scribe_Defs.Look(ref weapon, "weapon");
            Scribe_Defs.Look(ref damageDef, "damageDef");
            Scribe_Values.Look(ref damage, "damage");
            Scribe_Values.Look(ref armorPenetration, "armorPenetration");
            Scribe_Values.Look(ref stunTicks, "stunTicks");
            Scribe_Defs.Look(ref hitSound, "hitSound");
        }
    }

    // The strikes of one map still to land, kept with the map so a save made in between still delivers them.
    public class MapComponent_JusticiarStrikes : MapComponent
    {
        private List<PendingStrike> pending = new List<PendingStrike>();

        public MapComponent_JusticiarStrikes(Map map) : base(map)
        {
        }

        public int PendingCount => pending.Count;

        public static void Schedule(Map map, PendingStrike strike)
        {
            MapComponent_JusticiarStrikes comp = map?.GetComponent<MapComponent_JusticiarStrikes>();
            if (comp != null && strike != null)
            {
                comp.pending.Add(strike);
            }
        }

        public override void MapComponentTick()
        {
            if (pending.Count == 0)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingStrike strike = pending[i];
                if (now >= strike.tick)
                {
                    pending.RemoveAt(i);
                    strike.Land(map);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // A destroyed pawn's reference would not resolve on load.
                pending.RemoveAll(s => !s.landing && (s.victim == null || s.victim.Destroyed));
                foreach (PendingStrike strike in pending)
                {
                    if (strike.caster != null && strike.caster.Destroyed)
                    {
                        strike.caster = null;
                    }
                }
            }
            Scribe_Collections.Look(ref pending, "pending", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pending ??= new List<PendingStrike>();
                pending.RemoveAll(s => s == null || !s.landing && s.victim == null);
            }
        }
    }
}
