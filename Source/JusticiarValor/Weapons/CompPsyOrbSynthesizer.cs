using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;
using Texture2D = UnityEngine.Texture2D;
using Vector3 = UnityEngine.Vector3;

namespace JusticiarValor
{
    public class CompProperties_PsyOrbSynthesizer : CompProperties
    {
        // Time to synthesize one orb.
        public int synthesisTicks = 300;

        public int maxOrbs = 3;

        public float range = 15.9f;

        // Minimum time between two launches.
        public int launchIntervalTicks = 60;

        public ThingDef projectile;

        public SoundDef launchSound;

        public string toggleIconPath = "UI/Abilities/MJV_PsyOrb";

        public CompProperties_PsyOrbSynthesizer()
        {
            compClass = typeof(CompPsyOrbSynthesizer);
        }
    }

    // Energy psyspear: its internal energy system synthesizes psy-orbs that circle the wielder and are launched at
    // enemies on their own, like a turret, while the auto-ability is switched on.
    // Replaces the weapon's CompEquippable (the equipment tracker only asks the CompEquippable for equipped gizmos);
    // the spear ticks normally (tickerType Normal), which CompEquippable.CompTick also uses to tick its verbs.
    [StaticConstructorOnStartup]
    public class CompPsyOrbSynthesizer : CompEquippable, IFxDrawer
    {
        private static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        private bool autoLaunch = true;
        private int orbs;
        private int synthesisProgress;
        private int launchCooldown;
        private float seed = -1f;
        private Map registeredMap;

        public CompProperties_PsyOrbSynthesizer OrbProps => (CompProperties_PsyOrbSynthesizer)props;

        public int Orbs => orbs;

        public bool AutoLaunch
        {
            get => autoLaunch;
            set => autoLaunch = value;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref autoLaunch, "autoLaunch", true);
            Scribe_Values.Look(ref orbs, "orbs", 0);
            Scribe_Values.Look(ref synthesisProgress, "synthesisProgress", 0);
            Scribe_Values.Look(ref launchCooldown, "launchCooldown", 0);
        }

        public override void CompTick()
        {
            base.CompTick();
            Pawn wielder = Holder;
            if (wielder == null || !wielder.Spawned || wielder.Dead || wielder.Downed)
            {
                return;
            }
            if (registeredMap != wielder.Map)
            {
                registeredMap = wielder.Map;
                MapComponent_JusticiarFx.Register(registeredMap, this);
            }
            if (!autoLaunch)
            {
                return;
            }
            if (orbs < OrbProps.maxOrbs && ++synthesisProgress >= OrbProps.synthesisTicks)
            {
                synthesisProgress = 0;
                orbs++;
            }
            if (launchCooldown > 0)
            {
                launchCooldown--;
                return;
            }
            // Calm or fighting mad: a wielder in a non-violent mental break does not attack.
            bool canAttack = (!wielder.InMentalState || wielder.InAggroMentalState) && !wielder.WorkTagIsDisabled(WorkTags.Violent);
            if (orbs > 0 && canAttack && wielder.IsHashIntervalTick(10))
            {
                Thing target = FindTarget(wielder);
                if (target != null)
                {
                    Launch(wielder, target);
                }
            }
        }

        private Thing FindTarget(Pawn wielder)
        {
            Map map = wielder.Map;
            float rangeSquared = OrbProps.range * OrbProps.range;
            Thing best = null;
            float bestDistance = float.MaxValue;
            List<IAttackTarget> potential = map.attackTargetsCache.GetPotentialTargetsFor(wielder);
            for (int i = 0; i < potential.Count; i++)
            {
                if (!(potential[i].Thing is Pawn pawn) || pawn.Dead || pawn.Downed || !pawn.Spawned)
                {
                    continue;
                }
                if (!GenHostility.IsActiveThreatTo(potential[i], wielder.Faction) && !pawn.HostileTo(wielder))
                {
                    continue;
                }
                float distance = pawn.Position.DistanceToSquared(wielder.Position);
                if (distance > rangeSquared || distance >= bestDistance)
                {
                    continue;
                }
                if (!GenSight.LineOfSight(wielder.Position, pawn.Position, map, true))
                {
                    continue;
                }
                best = pawn;
                bestDistance = distance;
            }
            return best;
        }

        private void Launch(Pawn wielder, Thing target)
        {
            if (OrbProps.projectile == null)
            {
                return;
            }
            Vector3 origin = OrbPosition(wielder, orbs - 1, FxClock.Now);
            var projectile = (Projectile)GenSpawn.Spawn(OrbProps.projectile, wielder.Position, wielder.Map);
            projectile.Launch(wielder, origin, target, target, ProjectileHitFlags.IntendedTarget, true, parent);
            OrbProps.launchSound?.PlayOneShot(new TargetInfo(wielder.Position, wielder.Map));
            orbs--;
            launchCooldown = OrbProps.launchIntervalTicks;
            wielder.mindState.lastCombatantTick = Find.TickManager.TicksGame;
        }

        // Where the index-th orb circles the wielder.
        private Vector3 OrbPosition(Pawn wielder, int index, double now)
        {
            if (seed < 0f) seed = FxDraw.Seed();
            // Fixed slots: firing or synthesizing an orb must not teleport the others to a new orbit.
            float angle = (float)(now * 80.0 % 360.0) + index * 360f / System.Math.Max(OrbProps.maxOrbs, 1) + seed * 360f;
            Vector3 center = wielder.DrawPos;
            Vector3 offset = JVMath.Direction(angle);
            float bob = (float)System.Math.Sin(now * 2.4 + index * 1.7) * 0.045f;
            return JVMath.Offset(center, offset.x * 0.90f, offset.z * 0.60f + 0.28f + bob);
        }

        public override IEnumerable<Gizmo> CompGetEquippedGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetEquippedGizmosExtra())
            {
                yield return gizmo;
            }
            Pawn wielder = Holder;
            if (wielder == null || !wielder.IsColonistPlayerControlled)
            {
                yield break;
            }
            yield return new Command_Toggle
            {
                defaultLabel = "MJV_PsyOrbToggle".Translate(),
                defaultDesc = "MJV_PsyOrbToggleDesc".Translate(orbs, OrbProps.maxOrbs),
                icon = Icon(OrbProps.toggleIconPath),
                isActive = () => autoLaunch,
                toggleAction = () => autoLaunch = !autoLaunch
            };
        }

        private static Texture2D Icon(string path)
        {
            if (!icons.TryGetValue(path, out Texture2D icon))
            {
                icon = ContentFinder<Texture2D>.Get(path, false) ?? BaseContent.BadTex;
                icons[path] = icon;
            }
            return icon;
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            // The orbs collapse when the spear leaves the wielder's hands.
            orbs = 0;
            synthesisProgress = 0;
        }

        // ---- drawing ----

        public bool FxActiveOn(Map map)
        {
            Pawn wielder = Holder;
            bool active = wielder != null && wielder.Spawned && wielder.Map == map && !parent.Destroyed;
            if (!active && registeredMap == map)
            {
                registeredMap = null;
            }
            return active;
        }

        public void DrawFx(FxDrawContext ctx)
        {
            Pawn wielder = Holder;
            if (wielder == null || wielder.Dead || wielder.Downed || !ctx.view.Contains(wielder.Position))
            {
                return;
            }
            if (seed < 0f)
            {
                seed = FxDraw.Seed();
            }
            float altitude = FxDraw.AltitudeGlow;
            for (int i = 0; i < orbs; i++)
            {
                Vector3 pos = JVMath.WithY(OrbPosition(wielder, i, ctx.now), altitude);
                // A soft halo and a short comet tail of where it just was on its orbit.
                FxDraw.Sprite(ctx, FxAssets.SpriteUnder, pos, 0f, 0.95f, 0.95f, FxSheet.SoftGlow, 0.35f, 1.2f);
                for (int k = 3; k >= 1; k--)
                {
                    Vector3 trail = JVMath.WithY(OrbPosition(wielder, i, ctx.now - k * 0.045), altitude);
                    float s = 0.56f * (1f - 0.18f * k);
                    FxDraw.Clip(ctx, FxAssets.PsyOrb, trail, 0f, s, s, FxDraw.LoopPhase(ctx.now, 2.4f, seed + i * 0.31f), 0.3f / k, 1f, true);
                }
                FxDraw.Clip(ctx, FxAssets.PsyOrb, pos, 0f, 0.56f, 0.56f,
                    FxDraw.LoopPhase(ctx.now, 2.4f, seed + i * 0.31f), 0.88f, 1.15f, true);
            }
            // The last part of synthesis coalesces in the vacant slot and joins the orbit continuously: sparks spiral in
            // (Piloto StarCrossFlare) as the orb grows.
            if (autoLaunch && orbs < OrbProps.maxOrbs)
            {
                float forming = JVMath.Clamp01((synthesisProgress / (float)System.Math.Max(1, OrbProps.synthesisTicks) - 0.7f) / 0.3f);
                if (forming > 0f)
                {
                    Vector3 pos = JVMath.WithY(OrbPosition(wielder, orbs, ctx.now), altitude);
                    float size = JVMath.Lerp(0.18f, 0.56f, forming);
                    FxDraw.Clip(ctx, FxAssets.PsyOrb, pos, 0f, size, size,
                        FxDraw.LoopPhase(ctx.now, 2.4f, seed + orbs * 0.31f), forming * 0.88f, 1.15f, true);
                    float spin = FxClock.Wrapped(ctx.now) * 260f;
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 spark = JVMath.AlongAngle(pos, spin + k * 90f, 0.1f + (1f - forming) * 0.8f);
                        FxDraw.Sprite(ctx, FxAssets.SpriteOver, spark, spin, 0.35f, 0.35f, FxSheet.CrossFlare,
                            forming * (1f - forming) * 3f, 1.6f);
                    }
                }
            }
        }
    }
}
