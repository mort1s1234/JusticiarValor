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

        // The orbs come out of the spear while its wielder fights: drafted (if deployWhenDrafted), harmed within the last
        // harmedRecentlyTicks, or with an active threat within threatRadius cells. They go back in once none of that has
        // held for combatGraceTicks.
        public bool deployWhenDrafted = true;

        public int harmedRecentlyTicks = 600;

        public float threatRadius = 28f;

        public int combatGraceTicks = 600;

        public ThingDef projectile;

        public SoundDef launchSound;

        public string toggleIconPath = "UI/Abilities/MJV_PsyOrb";

        public CompProperties_PsyOrbSynthesizer()
        {
            compClass = typeof(CompPsyOrbSynthesizer);
        }
    }

    // Energy psyspear: its internal energy system synthesizes psy-orbs and keeps them inside the spear. While the wielder
    // fights, the orbs come out to circle the wielder and, while the auto-ability is switched on, are launched at enemies
    // on their own, like a turret; out of combat they go back in, so nothing circles a colonist at work and nothing is
    // drawn or scanned for.
    // Replaces the weapon's CompEquippable (the equipment tracker only asks the CompEquippable for equipped gizmos);
    // the spear ticks normally (tickerType Normal), which CompEquippable.CompTick also uses to tick its verbs.
    [StaticConstructorOnStartup]
    public class CompPsyOrbSynthesizer : CompEquippable, IFxDrawer
    {
        // Seconds an orb takes between the spear and its orbit, and between two orbs coming out (or going back in).
        private const float OrbFlight = 0.28f;
        private const float OrbStagger = 0.09f;

        // How often the wielder's surroundings are scanned for threats, in ticks.
        private const int ThreatScanInterval = 30;

        private static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        private bool autoLaunch = true;
        private int orbs;
        private int synthesisProgress;
        private int launchCooldown;
        // Whether the orbs are out, and the last tick the wielder was found fighting.
        private bool deployed;
        private int lastCombatTick = -99999;
        private float seed = -1f;
        private Map registeredMap;
        // When the orbs last came out or went back in (FxClock), and how many went in. Not saved: after a load the orbs
        // are simply where they belong.
        private double deployedAt = -1e6;
        private double recalledAt = -1e6;
        private int recalledOrbs;

        public CompProperties_PsyOrbSynthesizer OrbProps => (CompProperties_PsyOrbSynthesizer)props;

        public int Orbs => orbs;

        public bool Deployed => deployed;

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
            Scribe_Values.Look(ref deployed, "deployed", false);
            Scribe_Values.Look(ref lastCombatTick, "lastCombatTick", -99999);
        }

        public override void CompTick()
        {
            base.CompTick();
            Pawn wielder = Holder;
            if (wielder == null || !wielder.Spawned || wielder.Dead)
            {
                deployed = false;
                return;
            }
            int now = Find.TickManager.TicksGame;
            if (launchCooldown > 0)
            {
                launchCooldown--;
            }
            if (autoLaunch && !wielder.Downed && orbs < OrbProps.maxOrbs && ++synthesisProgress >= OrbProps.synthesisTicks)
            {
                synthesisProgress = 0;
                orbs++;
            }
            if (autoLaunch && !wielder.Downed && Fighting(wielder))
            {
                lastCombatTick = now;
            }
            bool wanted = autoLaunch && !wielder.Downed && wielder.Awake() && now - lastCombatTick <= OrbProps.combatGraceTicks;
            if (wanted != deployed)
            {
                SetDeployed(wanted);
            }
            if (!deployed)
            {
                return;
            }
            if (registeredMap != wielder.Map)
            {
                registeredMap = wielder.Map;
                MapComponent_JusticiarFx.Register(registeredMap, this);
            }
            // Calm or fighting mad: a wielder in a non-violent mental break does not attack.
            bool canAttack = (!wielder.InMentalState || wielder.InAggroMentalState) && !wielder.WorkTagIsDisabled(WorkTags.Violent);
            if (launchCooldown == 0 && orbs > 0 && canAttack && wielder.IsHashIntervalTick(10))
            {
                Thing target = FindTarget(wielder);
                if (target != null)
                {
                    Launch(wielder, target);
                }
            }
        }

        // Whether the wielder is in a fight right now: drafted, harmed lately, or an active threat close by (scanned
        // every half second).
        private bool Fighting(Pawn wielder)
        {
            if (OrbProps.deployWhenDrafted && wielder.Drafted)
            {
                return true;
            }
            if (wielder.mindState != null && Find.TickManager.TicksGame - wielder.mindState.lastHarmTick <= OrbProps.harmedRecentlyTicks)
            {
                return true;
            }
            return wielder.IsHashIntervalTick(ThreatScanInterval) && JVUtility.ActiveThreatWithin(wielder, OrbProps.threatRadius);
        }

        private void SetDeployed(bool value)
        {
            deployed = value;
            if (value)
            {
                deployedAt = FxClock.Now;
            }
            else
            {
                recalledAt = FxClock.Now;
                recalledOrbs = orbs;
            }
        }

        // The nearest active threat in range and in sight: sleeping or dormant foes, insects keeping to their hive and
        // anything downed or in the fog are left alone, as turrets leave them.
        private Thing FindTarget(Pawn wielder)
        {
            Map map = wielder.Map;
            float rangeSquared = OrbProps.range * OrbProps.range;
            Thing best = null;
            float bestDistance = float.MaxValue;
            List<IAttackTarget> potential = map.attackTargetsCache.GetPotentialTargetsFor(wielder);
            for (int i = 0; i < potential.Count; i++)
            {
                if (!(potential[i].Thing is Pawn pawn) || pawn.Dead || pawn.Downed || !pawn.Spawned
                    || !JVUtility.IsActiveThreatTo(potential[i], wielder))
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
            Vector3 origin = OrbDrawPosition(wielder, orbs - 1, FxClock.Now);
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

        // Where the orbs leave the spear and go back into it.
        private static Vector3 SpearPoint(Pawn wielder) => JVMath.Offset(wielder.DrawPos, 0f, 0.15f);

        // How far the index-th orb is out of the spear: 0 inside, 1 on its orbit. They come out one after another, and go
        // back in the other way round.
        private float OutFraction(int index, double now)
        {
            if (deployed)
            {
                return JVMath.Clamp01((float)((now - deployedAt - index * OrbStagger) / OrbFlight));
            }
            return 1f - JVMath.Clamp01((float)((now - recalledAt - (recalledOrbs - 1 - index) * OrbStagger) / OrbFlight));
        }

        // Where the index-th orb is drawn: on its orbit, or on its way between the spear and the orbit.
        private Vector3 OrbDrawPosition(Pawn wielder, int index, double now)
        {
            Vector3 slot = OrbPosition(wielder, index, now);
            float t = OutFraction(index, now);
            if (t >= 1f)
            {
                return slot;
            }
            Vector3 from = SpearPoint(wielder);
            float k = FxDraw.EaseOut(t);
            return new Vector3(from.x + (slot.x - from.x) * k, slot.y, from.z + (slot.z - from.z) * k);
        }

        private bool Recalling(double now) => !deployed && recalledOrbs > 0 && now - recalledAt < OrbFlight + (recalledOrbs - 1) * OrbStagger;

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
            deployed = false;
            recalledOrbs = 0;
        }

        // ---- drawing ----

        public bool FxActiveOn(Map map)
        {
            Pawn wielder = Holder;
            bool active = wielder != null && wielder.Spawned && wielder.Map == map && !parent.Destroyed
                && (deployed || Recalling(FxClock.Now));
            if (!active && registeredMap == map)
            {
                registeredMap = null;
            }
            return active;
        }

        public void DrawFx(FxDrawContext ctx)
        {
            Pawn wielder = Holder;
            if (wielder == null || wielder.Dead || !ctx.view.Contains(wielder.Position))
            {
                return;
            }
            if (seed < 0f)
            {
                seed = FxDraw.Seed();
            }
            float altitude = FxDraw.AltitudeGlow;
            Vector3 spear = JVMath.WithY(SpearPoint(wielder), altitude);
            int count = deployed ? orbs : recalledOrbs;
            for (int i = 0; i < count; i++)
            {
                float t = OutFraction(i, ctx.now);
                if (t <= 0f)
                {
                    continue;
                }
                Vector3 pos = JVMath.WithY(OrbDrawPosition(wielder, i, ctx.now), altitude);
                float grow = JVMath.Lerp(0.35f, 1f, t);
                // A soft halo and, on the orbit, a short comet tail of where it just was.
                FxDraw.Sprite(ctx, FxAssets.SpriteUnder, pos, 0f, 0.95f * grow, 0.95f * grow, FxSheet.SoftGlow, 0.35f, 1.2f);
                if (t >= 1f)
                {
                    for (int k = 3; k >= 1; k--)
                    {
                        Vector3 trail = JVMath.WithY(OrbPosition(wielder, i, ctx.now - k * 0.045), altitude);
                        float s = 0.56f * (1f - 0.18f * k);
                        FxDraw.Clip(ctx, FxAssets.PsyOrb, trail, 0f, s, s, FxDraw.LoopPhase(ctx.now, 2.4f, seed + i * 0.31f), 0.3f / k, 1f, true);
                    }
                }
                FxDraw.Clip(ctx, FxAssets.PsyOrb, pos, 0f, 0.56f * grow, 0.56f * grow,
                    FxDraw.LoopPhase(ctx.now, 2.4f, seed + i * 0.31f), 0.88f, 1.15f, true);
                // A glint (Piloto StarCrossFlare) on the spear as the orb leaves it or goes back in.
                if (t < 0.4f)
                {
                    FxDraw.Sprite(ctx, FxAssets.SpriteOver, spear, seed * 90f + i * 40f, 0.7f, 0.7f, FxSheet.CrossFlare,
                        (1f - t / 0.4f) * 0.8f, 1.6f);
                }
            }
            // The last part of synthesis coalesces in the vacant slot and joins the orbit continuously: sparks spiral in
            // (Piloto StarCrossFlare) as the orb grows. Only once every orb is out.
            if (deployed && autoLaunch && orbs < OrbProps.maxOrbs
                && ctx.now - deployedAt >= OrbFlight + System.Math.Max(0, orbs - 1) * OrbStagger)
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
