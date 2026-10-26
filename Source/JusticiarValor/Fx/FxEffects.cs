using System;
using System.Collections.Generic;
using Verse;
using Color = UnityEngine.Color;
using Material = UnityEngine.Material;
using Mesh = UnityEngine.Mesh;
using Texture = UnityEngine.Texture;
using Vector3 = UnityEngine.Vector3;

namespace JusticiarValor
{
    // Justiciar Valor's shockwave, on the ground around the wearer: a short flash, a crisp front (Piloto Ring_Shockwave2)
    // racing out to the wave's radius over the Bard Flash Shockwave and the Holy explosion, with dust thrown up along the
    // front.
    public sealed class PsychicWaveFx : FxEffect
    {
        private static readonly Color DustColor = new Color(0.58f, 0.53f, 0.46f, 1f);

        private readonly Vector3 center;
        private readonly float radius;
        private readonly float seed = FxDraw.Seed();
        private readonly float intensity;

        public PsychicWaveFx(Vector3 center, float radius, float intensity = 1f)
        {
            this.center = JVMath.WithY(center, FxDraw.AltitudeGlow);
            this.radius = radius;
            this.intensity = intensity;
            duration = 1.3f;
        }

        public override void Draw(FxDrawContext ctx, float progress, float age)
        {
            if (age < 1f)
            {
                // Ring_Shockwave2's ring lies at 0.42 of its cell; Shockwave_Dusty's dust fills to about 0.41.
                float front = radius * FxDraw.EaseOut(age / 0.35f);
                float k = JVMath.Clamp01(age / 0.45f);
                FxDraw.Sprite(ctx, FxAssets.SpriteUnder, center, seed * 360f, front / 0.42f, front / 0.42f, FxSheet.JaggedRing,
                    (1f - k) * (1f - k) * intensity, 1.8f);
                float dust = radius * FxDraw.EaseOut(age / 0.5f) / 0.41f;
                FxDraw.Sprite(ctx, FxAssets.SpriteUnder, center, seed * 90f, dust, dust, FxSheet.DustBurst,
                    0.5f * (1f - age) * (1f - age) * intensity, 1f, 2, DustColor);
            }
            if (age < 1.3f)
            {
                FxDraw.Clip(ctx, FxAssets.WaveShock, center, seed * 180f, radius * 2.4f, radius * 2.4f, age / 1.3f, 0.85f, 1.1f * intensity);
            }
            if (age < 0.95f)
            {
                // The baked Holy explosion has transparent padding outside its shock front; its first fifth, a six-pointed
                // star, is skipped.
                FxDraw.Clip(ctx, FxAssets.PsychicWave, center, seed * 360f, radius * 2.35f, radius * 2.35f, 0.2f + 0.8f * age / 0.95f,
                    0.55f, 0.8f * intensity);
            }
            if (age < 0.18f)
            {
                float k = age / 0.18f;
                float s = JVMath.Lerp(2.6f, 1.4f, k);
                FxDraw.Sprite(ctx, FxAssets.SpriteOver, center, seed * 90f, s, s, FxSheet.ImpactFlare, (1f - k) * (1f - k) * intensity, 1.8f);
            }
        }
    }

    // The Promachos helmet's scan when it senses new murderous intent: a sight closes on the wearer and a sonar ping
    // races out around them (MJV_ThreatPulse), a chevron on the HUD ring points at each pawn taking aim, and
    // a lock-on (Piloto Tech_GroundAOEIndicator) settles over each of them.
    public sealed class ThreatPulseFx : FxEffect
    {
        // Radius of the scan, in cells.
        private const float Reach = 3.5f;

        private readonly Pawn pawn;
        private readonly float threat;
        private readonly List<Pawn> sources;
        private readonly float seed = FxDraw.Seed();

        // sources: the pawns aiming at the wearer when the pulse fired; copied.
        public ThreatPulseFx(Pawn pawn, float threat, List<Pawn> sources = null)
        {
            this.pawn = pawn;
            this.threat = JVMath.Clamp01(threat);
            this.sources = sources == null ? new List<Pawn>() : new List<Pawn>(sources);
            duration = 1.3f;
        }

        public override bool Cancelled => !pawn.Spawned;

        public override void Draw(FxDrawContext ctx, float progress, float age)
        {
            // Threat count changes strength, not hue.
            float strength = 0.6f + threat * 0.4f;
            Vector3 at = JVMath.WithY(pawn.DrawPos, FxDraw.AltitudeGlow);
            if (age < 0.95f)
            {
                FxDraw.Quad(ctx, FxAssets.ThreatPulse, at, 0f, Reach * 2f, Reach * 2f, JVMath.White,
                    JVMath.V4(age / 0.95f, seed, strength, 1.35f), JVMath.Zero4);
            }
            for (int i = 0; i < sources.Count; i++)
            {
                Pawn source = sources[i];
                if (source == null || !source.Spawned || source.Map != pawn.Map)
                {
                    continue;
                }
                float bearing = JVMath.AngleFlat(pawn.DrawPos, source.DrawPos);
                // The chevron slides in onto the HUD ring (0.36 of the reach) and blinks there.
                float shown = JVMath.Clamp01((age - 0.1f) / 0.1f) * (1f - JVMath.Clamp01((age - 0.8f) / 0.4f));
                if (shown > 0f)
                {
                    float blink = 0.7f + 0.3f * MathF.Cos(age * 22f + i * 1.7f);
                    Vector3 mark = JVMath.AlongAngle(at, bearing, Reach * 0.36f + 0.25f + 0.35f * (1f - FxDraw.EaseOut((age - 0.1f) / 0.2f)));
                    FxDraw.Sprite(ctx, FxAssets.SpriteUnder, mark, 0f, 0.9f, 0.9f, FxSheet.SoftGlow, shown * strength * 0.45f, 1.2f);
                    FxDraw.Sprite(ctx, FxAssets.SpriteOver, mark, bearing - 90f, 0.75f, 0.75f, FxSheet.Chevron, shown * blink * strength, 2.4f);
                }
                float lockAge = age - 0.12f - i * 0.05f;
                if (lockAge > 0f && lockAge < 0.9f)
                {
                    FxDraw.Clip(ctx, FxAssets.ThreatLock, JVMath.WithY(source.DrawPos, FxDraw.AltitudeGlow), seed * 90f + i * 23f,
                        2f, 2f, lockAge / 0.9f, strength, 1.7f);
                }
            }
        }
    }

    // Invictus: the armor releasing its stored energy, plainly: a flash on the wearer and two rings racing out
    // (Piloto Ring_Shockwave), in the white-violet the overcharged shield takes on.
    public sealed class InvictusBurstFx : FxEffect
    {
        private static readonly Color Overcharge = new Color(0.92f, 0.84f, 1.15f, 1f);

        private readonly Vector3 center;
        private readonly float seed = FxDraw.Seed();

        public InvictusBurstFx(Pawn pawn)
        {
            center = JVMath.WithY(pawn.DrawPos, FxDraw.AltitudeGlow);
            duration = 0.6f;
        }

        public override void Draw(FxDrawContext ctx, float progress, float age)
        {
            if (age < 0.14f)
            {
                float k = age / 0.14f;
                float s = JVMath.Lerp(2.8f, 1.8f, k);
                FxDraw.Sprite(ctx, FxAssets.SpriteOver, center, 0f, s, s, FxSheet.SoftGlow, (1f - k) * (1f - k), 1.6f, 0, Overcharge);
                FxDraw.Sprite(ctx, FxAssets.SpriteOver, center, seed * 90f, 1.8f, 1.8f, FxSheet.CrossFlare, (1f - k) * (1f - k), 2f, 0, Overcharge);
            }
            Ring(ctx, age, 0.45f, 3f, 1f);
            Ring(ctx, age - 0.08f, 0.4f, 2f, 0.6f);
        }

        // A ring racing out to `reach` cells; Ring_Shockwave's ring lies at 0.43 of its cell.
        private void Ring(FxDrawContext ctx, float t, float life, float reach, float opacity)
        {
            if (t <= 0f || t >= life)
            {
                return;
            }
            float k = t / life;
            float size = JVMath.Lerp(0.5f, reach, FxDraw.EaseOut(k)) * 2f / 0.86f;
            FxDraw.Sprite(ctx, FxAssets.SpriteUnder, center, seed * 360f, size, size, FxSheet.PingRing, (1f - k) * (1f - k) * opacity, 1.8f, 0, Overcharge);
        }
    }

    // Celestial Pierce, in beats: a spectral copy of the wielder's own blade grows in their hand, lifts straight up, flies
    // over the target turning point-down, hangs there gathering itself over a reticle on the ground, then slams down. The
    // ground breaks where it lands, cracks splitting out from it (MJV_GroundFissure), under the Piloto Paladin sword shock,
    // a ground shock ring, dust and thrown stones. Everyone around the landing is struck as the shock reaches them, and the
    // landing is heard and shakes the camera, on the same timeline (CompAbilityEffect_CelestialPierce).
    public sealed class CelestialPierceFx : FxEffect
    {
        // Seconds after the cast at which each beat ends; the blade is in the ground at Impact.
        public const float GrowEnd = 0.2f;
        public const float LiftEnd = 0.38f;
        public const float FlyEnd = 0.58f;
        public const float HangEnd = 0.7f;
        public const float Impact = 0.76f;
        // Cells per second the cracks run out from the landing.
        public const float CrackSpeed = 14f;
        // Cells per second the shock ring of the landing spreads (DrawGround: radius 0.3 to 1.9 in 0.32 s).
        public const float ShockSpeed = 5f;
        // Height of the grown copy, in cells, and how much of it ends up in the ground.
        private const float GrownHeight = 2.7f;
        private const float LiftedHeight = 2.2f;
        private const float Buried = 0.22f;
        // The blade's point lies this fraction of the quad's height from its centre.
        private const float TipOffset = 0.47f;
        // How far above the ground its point hangs over the target, and how much higher it draws back before the slam.
        private const float HoverClear = 1.1f;
        private const float WindUp = 0.3f;

        private static readonly Color DustColor = new Color(0.58f, 0.53f, 0.46f, 1f);
        private static readonly Color ShadowColor = new Color(0.04f, 0.06f, 0.05f, 1f);

        private readonly Material blade;
        private readonly Vector3 held;
        private readonly float heldAngle, upAngle, downAngle;
        private readonly float mirror;
        private readonly float height0, aspect;
        private readonly Vector3 plunge;
        private readonly float angle;
        private readonly float behind, ahead;
        private readonly float seed = FxDraw.Seed();
        private readonly Debris[] stones;
        private readonly float[] puffs;

        // held, heldAngle, mirrored: where and how the game draws the wielder's weapon (PawnRenderUtility.DrawEquipmentAiming).
        // plunge: where the copy slams down (the target); angle: the direction it was thrown in, from the wielder;
        // behind, ahead: how far the cracks split out from the landing, back towards the wielder and on away from them.
        public CelestialPierceFx(Thing weapon, Vector3 held, float heldAngle, bool mirrored, Vector3 plunge, float angle, float behind, float ahead)
        {
            blade = FxAssets.BladeFor(weapon);
            float w = weapon?.Graphic?.drawSize.x ?? 1f;
            float h = weapon?.Graphic?.drawSize.y ?? 1f;
            height0 = h;
            aspect = h > 0f ? w / h : 1f;
            this.held = held;
            this.heldAngle = heldAngle;
            // Raised point-up the short way round, then turned over towards the target's side as it flies there.
            upAngle = heldAngle + Shortest(heldAngle, 0f);
            downAngle = upAngle + (JVMath.Direction(angle).x >= 0f ? 180f : -180f);
            mirror = mirrored ? -1f : 1f;
            this.plunge = plunge;
            this.angle = angle;
            this.behind = Math.Max(0f, behind);
            this.ahead = Math.Max(0f, ahead);
            duration = 2.6f;

            var random = new System.Random((int)(seed * 1e9f));
            float R(float min, float max) => min + (float)random.NextDouble() * (max - min);
            // Dust puffs every ~0.7 cells along both arms of the fissure (negative: back towards the wielder).
            var puffList = new List<float>();
            for (float d = 0.45f; d < this.ahead; d += 0.7f)
            {
                puffList.Add(d + R(-0.15f, 0.15f));
            }
            for (float d = 0.45f; d < this.behind; d += 0.7f)
            {
                puffList.Add(-d - R(-0.15f, 0.15f));
            }
            puffs = puffList.ToArray();
            var list = new List<Debris>();
            for (int i = 0; i < 10; i++)
            {
                list.Add(Debris.Thrown(JVMath.AlongAngle(plunge, R(0f, 360f), R(0.05f, 0.35f)), R(0f, 360f), R(1.4f, 2.8f), R(2.6f, 3.8f), Impact, R));
            }
            foreach (float d in puffs)
            {
                Vector3 start = JVMath.AlongAngle(JVMath.AlongAngle(plunge, angle, d), angle + 90f, R(-0.25f, 0.25f));
                float side = random.NextDouble() < 0.5 ? -90f : 90f;
                list.Add(Debris.Thrown(start, angle + side + R(-35f, 35f), R(0.8f, 1.8f), R(1.8f, 2.8f), Impact + Math.Abs(d) / CrackSpeed, R));
            }
            stones = list.ToArray();
        }

        private static float Shortest(float from, float to) => ((to - from) % 360f + 540f) % 360f - 180f;

        public override void Draw(FxDrawContext ctx, float progress, float age)
        {
            float t = age - Impact;
            if (t < 0f)
            {
                DrawTarget(ctx, age);
            }
            else
            {
                DrawGround(ctx, t);
            }
            for (int i = 0; i < stones.Length; i++)
            {
                stones[i].Draw(ctx, age);
            }
            DrawBlade(ctx, age);
        }

        // While the copy flies over and hangs there: a sight closing on the ground under it, and its shadow.
        private void DrawTarget(FxDrawContext ctx, float age)
        {
            float shown = JVMath.Clamp01((age - LiftEnd) / 0.12f);
            if (shown <= 0f)
            {
                return;
            }
            float close = FxDraw.EaseOut((age - LiftEnd) / (HangEnd - LiftEnd));
            float s = JVMath.Lerp(4f, 2.4f, close);
            FxDraw.Sprite(ctx, FxAssets.SpriteUnder, JVMath.WithY(plunge, FxDraw.AltitudeGlow), seed * 90f + close * 70f, s, s, FxSheet.SightInner,
                shown * 0.75f, 1.5f);
            float slam = JVMath.Clamp01((age - HangEnd) / (Impact - HangEnd));
            float shadow = JVMath.Lerp(1.8f, 0.8f, slam);
            FxDraw.Sprite(ctx, FxAssets.SpriteUnder, JVMath.WithY(plunge, FxDraw.AltitudeGround), 0f, shadow, shadow * 0.55f, FxSheet.SoftGlow,
                shown * JVMath.Lerp(0.3f, 0.6f, slam), 1f, 2, ShadowColor);
        }

        private void DrawGround(FxDrawContext ctx, float t)
        {
            float fade = 1f - JVMath.Clamp01((t - 1.1f) / 0.6f);
            float ground = FxDraw.AltitudeGround;
            Vector3 at = JVMath.WithY(plunge, FxDraw.AltitudeGlow);
            // Radial cracks where the blade went in, then the cracks splitting out both ways along the throw.
            FxDraw.Quad(ctx, FxAssets.GroundFissure, JVMath.WithY(plunge, ground), seed * 360f, 2.6f, 2.6f, JVMath.White,
                JVMath.V4(0f, FxDraw.EaseOut(t / 0.1f) * 1.06f, MathF.Exp(-t * 1.8f), fade), JVMath.V4(0f, t, 0f, 0f));
            Fissure(ctx, angle, ahead, t, fade, ground, seed > 0.5f);
            Fissure(ctx, angle + 180f, behind, t, fade, ground, seed <= 0.5f);
            // The shock along the ground, the dust where the blade went in and where the crack opens.
            if (t < 0.32f)
            {
                float k = t / 0.32f;
                float s = JVMath.Lerp(0.6f, 3.8f, FxDraw.EaseOut(k));
                FxDraw.Sprite(ctx, FxAssets.SpriteUnder, at, seed * 360f, s, s, FxSheet.JaggedRing, (1f - k) * (1f - k), 1.8f);
            }
            if (t < 1f)
            {
                float d = JVMath.Lerp(1.2f, 3.4f, FxDraw.EaseOut(t / 0.6f));
                FxDraw.Sprite(ctx, FxAssets.SpriteUnder, at, seed * 360f, d, d, FxSheet.DustBurst,
                    0.55f * (1f - t) * (1f - t), 1f, 2, DustColor);
            }
            for (int i = 0; i < puffs.Length; i++)
            {
                float a = t - Math.Abs(puffs[i]) / CrackSpeed;
                if (a <= 0f || a >= 0.9f)
                {
                    continue;
                }
                float side = MathF.Sin(i * 12.9898f + seed * 78.233f) * 0.2f;
                Vector3 pos = JVMath.AlongAngle(JVMath.AlongAngle(at, angle, puffs[i]), angle + 90f, side);
                float size = JVMath.Lerp(0.5f, 1.4f, FxDraw.EaseOut(a / 0.6f));
                float o = 1f - a / 0.9f;
                FxDraw.Sprite(ctx, FxAssets.SpriteUnder, pos, seed * 360f + i * 77f + a * 40f, size, size,
                    FxDraw.SheetSprite(FxSheet.Puffs, i + (int)(seed * 4f), 2, 2), 0.45f * o * o, 1f, 2, DustColor);
            }
            // The shock of the landing (Piloto ARPG_Paladin_Sword_Shock) and its flash.
            if (t < 1.2f)
            {
                FxDraw.Clip(ctx, FxAssets.PierceShock, at, seed * 360f, 2.7f, 2.7f, t / 1.2f, 0.8f, 1.1f);
            }
            if (t < 0.2f)
            {
                float k = t / 0.2f;
                float s = JVMath.Lerp(2.6f, 1.3f, k);
                FxDraw.Sprite(ctx, FxAssets.SpriteOver, JVMath.Offset(at, 0f, 0.15f), seed * 90f, s, s, FxSheet.ImpactFlare,
                    (1f - k) * (1f - k) * 0.85f, 1.8f);
            }
        }

        private void Fissure(FxDrawContext ctx, float direction, float length, float t, float fade, float ground, bool mirrored)
        {
            if (length < 0.25f)
            {
                return;
            }
            Vector3 mid = JVMath.WithY(JVMath.AlongAngle(plunge, direction, length * 0.5f), ground);
            FxDraw.Quad(ctx, FxAssets.GroundFissure, mid, direction, 1.4f, length, JVMath.White,
                JVMath.V4(1f, JVMath.Clamp01(t * CrackSpeed / length) * 1.06f, MathF.Exp(-t * 1.6f), fade),
                JVMath.V4(mirrored ? 1f : 0f, t, 0f, 0f));
        }

        private void DrawBlade(FxDrawContext ctx, float age)
        {
            if (blade is null)
            {
                return;
            }
            // Afterimages of where the copy just was, while it moves.
            if (age > GrowEnd && age < Impact + 0.08f)
            {
                float echoes = 1f - JVMath.Clamp01((age - Impact) / 0.08f);
                for (int i = 3; i >= 1; i--)
                {
                    Sword(ctx, age - i * 0.03f, 0.3f / i * echoes, 0.8f, 0f, 0f);
                }
            }
            // It flickers as it leaves the wielder's blade, whitens as it gathers itself, flashes as it lands.
            float flash = age < 0.12f ? 0.7f * (1f - age / 0.12f)
                : age < HangEnd ? 0.15f * JVMath.Clamp01((age - FlyEnd) / (HangEnd - FlyEnd))
                : age < Impact ? 0.15f
                : 1f - JVMath.Clamp01((age - Impact) / 0.16f);
            float glow = age < Impact ? JVMath.Lerp(1.15f, 1.6f, age / HangEnd) : JVMath.Lerp(1.8f, 1.15f, (age - Impact) / 0.5f);
            Sword(ctx, age, JVMath.Lerp(0.6f, 1f, age / 0.1f), glow, flash, JVMath.Clamp01((age - 1.6f) / 0.6f));
        }

        private void Sword(FxDrawContext ctx, float time, float opacity, float glow, float flash, float dissolve)
        {
            if (time < 0f)
            {
                return;
            }
            Vector3 lifted = JVMath.Offset(held, 0f, 1.6f);
            Vector3 hover = JVMath.Offset(plunge, 0f, HoverClear + TipOffset * GrownHeight);
            Vector3 center;
            float rot, h, widen = 1f, buried = 0f;
            if (time < GrowEnd)
            {
                // Grows in the hand, swelling a little past its size before it settles.
                float k = time / GrowEnd;
                float e = FxDraw.EaseOut(k);
                center = JVMath.Offset(held, 0f, 0.15f * e);
                rot = heldAngle;
                h = height0 * (JVMath.Lerp(1f, 1.6f, e) + 0.15f * MathF.Sin(k * MathF.PI));
            }
            else if (time < LiftEnd)
            {
                // Lifts straight up, turning point-up.
                float k = (time - GrowEnd) / (LiftEnd - GrowEnd);
                float e = FxDraw.EaseOut(k);
                center = new Vector3(held.x, 0f, JVMath.Lerp(held.z + 0.15f, lifted.z, e));
                rot = JVMath.Lerp(heldAngle, upAngle, FxDraw.EaseInOut(k));
                h = JVMath.Lerp(height0 * 1.6f, LiftedHeight, e);
            }
            else if (time < FlyEnd)
            {
                // Flies over the target in an arc, turning over point-down.
                float k = (time - LiftEnd) / (FlyEnd - LiftEnd);
                float e = FxDraw.EaseInOut(k);
                center = new Vector3(JVMath.Lerp(lifted.x, hover.x, e), 0f, JVMath.Lerp(lifted.z, hover.z, e) + MathF.Sin(k * MathF.PI) * 0.6f);
                rot = JVMath.Lerp(upAngle, downAngle, e);
                h = JVMath.Lerp(LiftedHeight, GrownHeight, e);
            }
            else if (time < HangEnd)
            {
                // Hangs over it, drawing back and trembling.
                float k = (time - FlyEnd) / (HangEnd - FlyEnd);
                center = JVMath.Offset(hover, MathF.Sin(time * 130f) * 0.02f * k, WindUp * FxDraw.EaseOut(k));
                rot = downAngle;
                h = GrownHeight;
            }
            else
            {
                // Slams down, stretched by its speed; squashes and shudders in the ground.
                float k = JVMath.Clamp01((time - HangEnd) / (Impact - HangEnd));
                float landed = plunge.z + (TipOffset - Buried) * GrownHeight;
                center = new Vector3(hover.x, 0f, JVMath.Lerp(hover.z + WindUp, landed, k * k * k));
                rot = downAngle;
                h = GrownHeight * (1f + 0.6f * k * (1f - k));
                if (time > Impact)
                {
                    float since = time - Impact;
                    float squash = MathF.Exp(-since * 25f);
                    h = GrownHeight * (1f - 0.08f * squash);
                    widen = 1f + 0.14f * squash;
                    center = new Vector3(hover.x + MathF.Sin(since * 90f) * 0.06f * MathF.Exp(-since * 9f), 0f,
                        plunge.z + (TipOffset - Buried) * h);
                }
                // Fraction of the quad, from its point end, below the ground line.
                buried = JVMath.Clamp01((plunge.z - (center.z - h * 0.5f)) / h);
            }
            Texture texture = blade.mainTexture;
            FxDraw.Quad(ctx, blade, JVMath.WithY(center, FxDraw.AltitudeGlow), rot, h * aspect * widen * mirror, h, JVMath.White,
                JVMath.V4(opacity, glow, buried, flash),
                JVMath.V4(1f / texture.width, 1f / texture.height, dissolve, seed));
        }
    }

    // A pawn struck by Celestial Pierce: a burst (Piloto Projectile_Hit_Tech), dust and two stones.
    public sealed class PierceHitFx : FxEffect
    {
        private static readonly Color DustColor = new Color(0.58f, 0.53f, 0.46f, 1f);

        private readonly Vector3 pos;
        private readonly float seed = FxDraw.Seed();
        private readonly Debris[] stones;

        public PierceHitFx(Vector3 pos, float angle)
        {
            this.pos = JVMath.WithY(pos, FxDraw.AltitudeGlow);
            duration = 0.9f;
            var random = new System.Random((int)(seed * 1e9f));
            float R(float min, float max) => min + (float)random.NextDouble() * (max - min);
            stones = new[]
            {
                Debris.Thrown(pos, angle + R(-50f, 50f), R(0.8f, 1.6f), R(1.6f, 2.4f), 0f, R),
                Debris.Thrown(pos, angle + R(130f, 230f), R(0.5f, 1.2f), R(1.4f, 2.2f), 0.03f, R)
            };
        }

        public override void Draw(FxDrawContext ctx, float progress, float age)
        {
            if (age < 0.7f)
            {
                float size = JVMath.Lerp(0.5f, 1.3f, FxDraw.EaseOut(age / 0.5f));
                float o = 1f - age / 0.7f;
                FxDraw.Sprite(ctx, FxAssets.SpriteUnder, pos, seed * 360f, size, size,
                    FxDraw.SheetSprite(FxSheet.Puffs, (int)(seed * 4f), 2, 2), 0.45f * o * o, 1f, 2, DustColor);
            }
            if (age < 0.42f)
            {
                FxDraw.Clip(ctx, FxAssets.OrbBurst, pos, seed * 360f, 1.4f, 1.4f, age / 0.42f, 0.9f, 1.3f);
            }
            for (int i = 0; i < stones.Length; i++)
            {
                stones[i].Draw(ctx, age);
            }
        }
    }

    // A stone thrown out of broken ground (Piloto Dust_Rocks): an arc through the air, a moment on the ground, gone.
    public struct Debris
    {
        private const float Gravity = 11f;

        private Vector3 origin;
        private float vx, vz, vy, spin, size, launch;
        private int sprite;

        // heading: RimWorld angle it flies towards; speed: cells/s over the ground; lift: initial upward speed; launch: when
        // (seconds into the owning effect); random: the owner's seeded source.
        public static Debris Thrown(Vector3 origin, float heading, float speed, float lift, float launch, System.Func<float, float, float> random)
        {
            Vector3 dir = JVMath.Direction(heading);
            return new Debris
            {
                origin = origin,
                vx = dir.x * speed,
                vz = dir.z * speed,
                vy = lift,
                spin = random(-540f, 540f),
                size = random(0.18f, 0.34f),
                launch = launch,
                sprite = (int)random(0f, 3.99f)
            };
        }

        public void Draw(FxDrawContext ctx, float time)
        {
            float a = time - launch;
            if (a < 0f)
            {
                return;
            }
            float flight = 2f * vy / Gravity;
            float f = a < flight ? a : flight;
            float fade = 1f - JVMath.Clamp01((a - flight - 0.25f) / 0.25f);
            if (fade <= 0f)
            {
                return;
            }
            float height = vy * f - 0.5f * Gravity * f * f;
            // Height shows as distance up the screen, as RimWorld draws things in the air.
            var pos = new Vector3(origin.x + vx * f, FxDraw.AltitudeGlow, origin.z + vz * f + height);
            FxDraw.Sprite(ctx, FxAssets.SpriteOver, pos, spin * f, size, size, FxDraw.SheetSprite(FxSheet.Rocks, sprite, 2, 2), fade, 1f, 1);
        }
    }

    // Warlockbane, in three clear beats, slow enough to follow: the side of the ward facing the psycaster lights up as it
    // catches the psycast (Piloto Ring_EdgeLower) and the psycast gathers there into one bright mote; the mote drifts
    // straight back with a short tail, gathering speed, while the ward slowly fades; a flash and a ring mark the caster it
    // strikes. The backlash stuns the caster as the mote strikes: CompWarlockbane schedules it HitTime after the deflection.
    public sealed class DeflectFx : FxEffect
    {
        // Seconds the psycast gathers on the ward before it leaves, and its average speed back in cells per second. The
        // flight takes MinFlight to MaxFlight seconds: a caster next to the bearer still sees it coming, a far one does not
        // wait for ever.
        private const float Launch = 0.3f;
        private const float Speed = 7f;
        private const float MinFlight = 0.5f;
        private const float MaxFlight = 2.4f;
        // Seconds the ward holds after the catch, then takes to fade; seconds the strike on the caster lasts.
        private const float WardHold = 0.45f;
        private const float WardFade = 1f;
        private const float Strike = 0.6f;

        private readonly Pawn pawn;
        private readonly Pawn caster;
        private readonly float angle;
        private readonly float flight;
        private readonly float seed = FxDraw.Seed();
        // Where the mote leaves from and where it strikes, as last drawn: it flies on if either pawn leaves the map.
        private Vector3 from, to;

        // fromAngle: RimWorld angle from the bearer towards the caster. caster: struck by the returning psycast if it is on
        // the bearer's map.
        public DeflectFx(Pawn pawn, float fromAngle, Pawn caster = null)
        {
            this.pawn = pawn;
            angle = fromAngle;
            from = JVMath.AlongAngle(JVMath.WithY(pawn.DrawPos, FxDraw.AltitudeGlow), angle, 0.75f);
            if (caster != null && caster.Spawned && caster.Map == pawn.Map)
            {
                this.caster = caster;
                to = JVMath.WithY(caster.DrawPos, FxDraw.AltitudeGlow);
                flight = JVMath.Clamp(JVMath.DistanceFlat(from, to) / Speed, MinFlight, MaxFlight);
            }
            duration = Math.Max(WardHold + WardFade, this.caster != null ? HitTime + Strike : 0f);
        }

        // Seconds from the deflection until the mote strikes the caster; 0 when no mote flies back.
        public float HitTime => caster != null ? Launch + flight : 0f;

        public override bool Cancelled => caster == null && !pawn.Spawned;

        public override void Draw(FxDrawContext ctx, float progress, float age)
        {
            if (pawn.Spawned && pawn.Map == ctx.map)
            {
                Vector3 center = JVMath.WithY(pawn.DrawPos, FxDraw.AltitudeGlow);
                from = JVMath.AlongAngle(center, angle, 0.75f);
                // The ward: Ring_EdgeLower is lit on its lower edge, turned to face the caster. It swells as it catches the
                // psycast and holds while the mote gathers and leaves, then fades slowly, still swelling a little.
                float fade = JVMath.Clamp01((age - WardHold) / WardFade);
                float shown = JVMath.Clamp01(age / 0.08f) * (1f - FxDraw.EaseInOut(fade));
                if (shown > 0f)
                {
                    float s = JVMath.Lerp(1.6f, 2f, FxDraw.EaseOut(age / 0.6f)) + 0.15f * fade;
                    FxDraw.Sprite(ctx, FxAssets.SpriteOver, center, angle + 180f, s, s, FxSheet.WardArc, shown, 2f);
                }
            }
            if (age < 0.3f)
            {
                // The catch: a glint where the psycast meets the ward, giving way to the mote.
                float k = age / 0.3f;
                float s = JVMath.Lerp(1f, 1.3f, FxDraw.EaseOut(k));
                FxDraw.Sprite(ctx, FxAssets.SpriteOver, from, seed * 90f + age * 60f, s, s, FxSheet.CrossFlare, (1f - k) * (1f - k), 2f);
            }
            if (caster == null)
            {
                return;
            }
            if (caster.Spawned && caster.Map == ctx.map)
            {
                to = JVMath.WithY(caster.DrawPos, FxDraw.AltitudeGlow);
            }
            float t = age - Launch;
            if (t < flight)
            {
                // The mote: a soft glow with a spinning glint. It gathers on the ward, then leaves at half its average speed
                // and strikes at one and a half, its tail (Trail_TaperedSoft, head at the cell's left) stretched behind it and
                // lengthening as it speeds up.
                float grown = FxDraw.EaseOut(JVMath.InverseLerp(0.04f, Launch, age));
                float u = JVMath.Clamp01(t / flight);
                float along = u * (0.5f + 0.5f * u) * JVMath.DistanceFlat(from, to);
                float heading = JVMath.AngleFlat(from, to);
                Vector3 head = JVMath.AlongAngle(from, heading, along);
                float tail = Math.Min(0.8f + 0.6f * u, along);
                if (tail > 0.05f)
                {
                    FxDraw.Sprite(ctx, FxAssets.SpriteOver, JVMath.AlongAngle(head, heading + 180f, tail * 0.45f), heading + 90f, tail, 0.45f,
                        FxSheet.TrailCore, 0.85f, 1.8f);
                }
                float size = grown * (1f + 0.08f * MathF.Sin(age * 14f + seed * 6f));
                FxDraw.Sprite(ctx, FxAssets.SpriteOver, head, 0f, 0.8f * size, 0.8f * size, FxSheet.SoftGlow, 0.7f * grown, 1.6f);
                FxDraw.Sprite(ctx, FxAssets.SpriteOver, head, age * 240f, 0.6f * size, 0.6f * size, FxSheet.CrossFlare, 0.9f * grown, 2f);
                return;
            }
            float hit = t - flight;
            if (hit < Strike)
            {
                float k = hit / Strike;
                // Ring_Shockwave's ring lies at 0.43 of its cell.
                float ring = JVMath.Lerp(0.3f, 1.2f, FxDraw.EaseOut(k)) / 0.43f;
                FxDraw.Sprite(ctx, FxAssets.SpriteOver, to, 0f, ring, ring, FxSheet.PingRing, (1f - k) * (1f - k), 1.8f);
                if (hit < 0.3f)
                {
                    float f = hit / 0.3f;
                    FxDraw.Sprite(ctx, FxAssets.SpriteOver, to, seed * 90f, 1.7f, 1.7f, FxSheet.ImpactFlare, (1f - f) * (1f - f), 1.8f);
                }
            }
        }
    }

    // The psychic shield catching a shot: a flash and sparks (Piloto Projectile_Hit_Tech) where it struck the dome, a ripple
    // ring and a glint. Follows the wearer.
    public sealed class ShieldHitFx : FxEffect
    {
        private readonly Pawn pawn;
        private readonly float side;
        private readonly float radius;
        private readonly float scale;
        private readonly float seed = FxDraw.Seed();

        // side: RimWorld angle from the wearer to where the shot struck; radius: the dome's; scale grows with the damage.
        public ShieldHitFx(Pawn pawn, float side, float radius, float scale)
        {
            this.pawn = pawn;
            this.side = side;
            this.radius = radius;
            this.scale = scale;
            duration = 0.45f;
        }

        public override bool Cancelled => !pawn.Spawned;

        public override void Draw(FxDrawContext ctx, float progress, float age)
        {
            Vector3 at = JVMath.WithY(JVMath.AlongAngle(pawn.DrawPos, side, radius * 0.85f), FxDraw.AltitudeGlow);
            if (age < 0.42f)
            {
                FxDraw.Clip(ctx, FxAssets.OrbBurst, at, side, 1.1f * scale, 1.1f * scale, age / 0.42f, 1f, 1.5f);
            }
            float k = JVMath.Clamp01(age / 0.35f);
            float ring = JVMath.Lerp(0.15f, 0.9f, FxDraw.EaseOut(k)) * scale / 0.42f;
            FxDraw.Sprite(ctx, FxAssets.SpriteOver, at, seed * 360f, ring, ring, FxSheet.JaggedRing, (1f - k) * (1f - k), 2f);
            if (age < 0.2f)
            {
                float g = age / 0.2f;
                FxDraw.Sprite(ctx, FxAssets.SpriteOver, at, seed * 90f, 1.8f * scale, 1.8f * scale, FxSheet.ImpactFlare, (1f - g) * (1f - g), 2.2f);
                FxDraw.Sprite(ctx, FxAssets.SpriteUnder, JVMath.WithY(pawn.DrawPos, FxDraw.AltitudeGlow), 0f, radius * 2.3f, radius * 2.3f,
                    FxSheet.SoftGlow, (1f - g) * 0.45f, 1.4f);
            }
        }
    }

    // The psychic shield breaking, plainly: the dome flashes and swells as it bursts, and one thin ring (Piloto
    // Ring_Shockwave) runs off it.
    public sealed class ShieldBreakFx : FxEffect
    {
        private readonly Pawn pawn;
        private readonly float radius;
        private readonly float seed = FxDraw.Seed();

        // radius: the dome's.
        public ShieldBreakFx(Pawn pawn, float radius)
        {
            this.pawn = pawn;
            this.radius = radius;
            duration = 0.5f;
        }

        public override bool Cancelled => !pawn.Spawned;

        public override void Draw(FxDrawContext ctx, float progress, float age)
        {
            Vector3 at = JVMath.WithY(pawn.DrawPos, FxDraw.AltitudeGlow);
            float k = JVMath.Clamp01(age / 0.3f);
            if (k < 1f)
            {
                // The dome itself, flashing and swelling as it fades.
                float size = radius * 2f * (1f + 0.25f * FxDraw.EaseOut(k));
                FxDraw.Quad(ctx, FxAssets.PsychicShield, at, 0f, size, size, JVMath.WithAlpha(JVMath.White, (1f - k) * (1f - k)),
                    JVMath.V4(1f, seed, FxClock.Wrapped(ctx.now), 1f - k), JVMath.V4(0f, 1f, 10f, 0f));
            }
            float r = JVMath.Clamp01(age / 0.5f);
            float ring = JVMath.Lerp(radius, radius * 1.8f, FxDraw.EaseOut(r)) * 2f / 0.86f;
            FxDraw.Sprite(ctx, FxAssets.SpriteUnder, at, seed * 360f, ring, ring, FxSheet.PingRing, (1f - r) * (1f - r) * 0.8f, 1.6f);
        }
    }

    // The psychic shield coming back, plainly: one ring (Piloto Ring_Shockwave) closes onto the wearer as the dome fades
    // back in (CompPsychicShield.DrawFx), with a soft glow as they meet.
    public sealed class ShieldRechargeFx : FxEffect
    {
        // Seconds the ring takes to close; the dome forms over the same time.
        public const float Closing = 0.4f;

        private readonly Pawn pawn;
        private readonly float radius;
        private readonly float seed = FxDraw.Seed();

        public ShieldRechargeFx(Pawn pawn, float radius)
        {
            this.pawn = pawn;
            this.radius = radius;
            duration = 0.6f;
        }

        public override bool Cancelled => !pawn.Spawned;

        public override void Draw(FxDrawContext ctx, float progress, float age)
        {
            Vector3 at = JVMath.WithY(pawn.DrawPos, FxDraw.AltitudeGlow);
            if (age < Closing)
            {
                float k = age / Closing;
                float ring = JVMath.Lerp(radius * 1.9f, radius, FxDraw.EaseInOut(k)) * 2f / 0.86f;
                FxDraw.Sprite(ctx, FxAssets.SpriteOver, at, seed * 90f, ring, ring, FxSheet.PingRing, JVMath.Clamp01(k * 4f) * 0.8f, 1.6f);
            }
            float glow = 1f - Math.Abs(age - Closing) / 0.15f;
            if (glow > 0f)
            {
                FxDraw.Sprite(ctx, FxAssets.SpriteOver, at, 0f, radius * 2.2f, radius * 2.2f, FxSheet.SoftGlow, glow * glow * 0.5f, 1.4f);
            }
        }
    }

    // A psy-orb bursting where it struck: a white-hot flash (Piloto StarImpactFlare) and a jagged ring (Ring_Shockwave2)
    // under three baked Piloto bursts, the core and its sparks (Projectile_Hit_Tech), radial petals (ARPG_HighTech_Hit) along
    // the flight and lingering glitter (Impact_Light).
    public sealed class OrbImpactFx : FxEffect
    {
        private readonly Vector3 pos;
        private readonly float angle;
        private readonly float seed = FxDraw.Seed();

        // angle: the orb's heading when it struck.
        public OrbImpactFx(Vector3 pos, float angle = 0f)
        {
            this.pos = JVMath.WithY(pos, FxDraw.AltitudeGlow);
            this.angle = angle;
            duration = 0.9f;
        }

        public override void Draw(FxDrawContext ctx, float progress, float age)
        {
            float ring = age / 0.45f;
            if (ring < 1f)
            {
                float size = JVMath.Lerp(0.4f, 2.2f, FxDraw.EaseOut(ring));
                FxDraw.Sprite(ctx, FxAssets.SpriteUnder, pos, seed * 360f, size, size, FxSheet.JaggedRing, (1f - ring) * (1f - ring) * 0.9f, 1.6f);
            }
            FxDraw.Clip(ctx, FxAssets.OrbGlitter, pos, seed * 360f, 2.1f, 2.1f, age / 0.85f, 0.85f, 1.1f);
            if (age < 0.3f)
            {
                FxDraw.Clip(ctx, FxAssets.OrbPetals, pos, angle, 2f, 2f, age / 0.3f, 0.9f, 1.2f);
            }
            if (age < 0.42f)
            {
                FxDraw.Clip(ctx, FxAssets.OrbBurst, pos, seed * 180f, 1.5f, 1.5f, age / 0.42f, 1f, 1.4f);
            }
            if (age < 0.2f)
            {
                float k = age / 0.2f;
                float size = JVMath.Lerp(2.4f, 1.4f, k);
                FxDraw.Sprite(ctx, FxAssets.SpriteOver, pos, seed * 90f, size, size, FxSheet.ImpactFlare, (1f - k) * (1f - k), 2.2f);
            }
        }
    }

    // A psy-orb's wake (MJV_OrbTrail): anchored to the ground it flew over, from the orb back along its flight.
    public static class OrbWake
    {
        public const float MaxLength = 1.9f;
        public const float Width = 0.7f;

        // head: the orb; flown: distance from the launch point; consumed: how much of the wake, from its end, is gone.
        public static void Draw(FxDrawContext ctx, Vector3 head, float angle, float flown, float length, float seed, float opacity, float consumed)
        {
            if (length < 0.05f)
            {
                return;
            }
            Vector3 mid = JVMath.WithY(JVMath.AlongAngle(head, angle + 180f, length * 0.5f), FxDraw.AltitudeGlow);
            FxDraw.Quad(ctx, FxAssets.OrbTrail, mid, angle, Width, length, JVMath.White,
                JVMath.V4(flown, length, FxClock.Wrapped(ctx.now), seed), JVMath.V4(opacity, Width, consumed, 0f));
        }
    }

    // The wake of an orb that struck: its end catches up with the impact while the shed sparkles fade.
    public sealed class OrbWakeFx : FxEffect
    {
        private readonly Vector3 head;
        private readonly float angle, flown, length, seed;

        public OrbWakeFx(Vector3 head, float angle, float flown, float length, float seed)
        {
            this.head = head;
            this.angle = angle;
            this.flown = flown;
            this.length = length;
            this.seed = seed;
            duration = 0.3f;
        }

        public override void Draw(FxDrawContext ctx, float progress, float age)
        {
            OrbWake.Draw(ctx, head, angle, flown, length, seed, 1f - progress * 0.6f, FxDraw.EaseOut(progress) * 1.1f);
        }
    }

    // The missionary's halo fading out where he stopped preaching (the halo itself is drawn by his sermon job).
    public sealed class SermonHaloFadeFx : FxEffect
    {
        private readonly Vector3 pos;
        private readonly float size;
        private readonly float seed;
        private readonly float shown;

        public SermonHaloFadeFx(Vector3 pos, float size, float seed, float shown)
        {
            this.pos = JVMath.WithY(pos, FxDraw.AltitudeGlow);
            this.size = size;
            this.seed = seed;
            this.shown = shown;
            duration = 0.9f;
        }

        public override void Draw(FxDrawContext ctx, float progress, float age)
        {
            FxDraw.Quad(ctx, FxAssets.SermonHalo, pos, 0f, size, size, JVMath.White,
                JVMath.V4(0f, seed, FxClock.Wrapped(ctx.now), shown * (1f - progress)), JVMath.Zero4);
        }
    }

    // One Invictus afterimage: the pawn's cached atlas frame, tinted and fading where the pawn just was, with a wind streak
    // (the psy-orb wake's ribbons, MJV_OrbTrail) back to the previous one and the Piloto GaleWind_Woosh.
    public sealed class AfterimageFx : FxEffect
    {
        private readonly Mesh mesh;
        private readonly Material material;
        private readonly Vector3 pos;
        private readonly Vector3 from;
        private readonly float streak;
        private readonly Color color;
        private readonly float seed = FxDraw.Seed();

        // from: where the previous afterimage was left; no streak when it is far (a new run).
        public AfterimageFx(Texture atlas, Mesh mesh, Vector3 pos, Color color, float life, Vector3 from)
        {
            material = FxAssets.AfterimageFor(atlas);
            this.mesh = mesh;
            this.pos = pos;
            this.from = from;
            float distance = JVMath.DistanceFlat(from, pos);
            streak = distance > 0.1f && distance < 2.5f ? distance : 0f;
            // Legacy Defs can contain rainbow tints. Keep their alpha but normalize the theme.
            this.color = JVMath.WithAlpha(FxPalette.Sage, color.a);
            duration = life;
        }

        public override void Draw(FxDrawContext ctx, float progress, float age)
        {
            if (!ctx.view.Contains(pos.ToIntVec3()))
            {
                return;
            }
            // Fades in over the first eighth of its life, by which time the pawn has moved on, then out.
            float life = JVMath.Clamp01(progress / 0.12f) * (1f - progress);
            if (streak > 0f)
            {
                float angle = JVMath.AngleFlat(from, pos);
                Vector3 mid = new Vector3((from.x + pos.x) * 0.5f, FxDraw.AltitudeGlow, (from.z + pos.z) * 0.5f);
                FxDraw.Quad(ctx, FxAssets.OrbTrail, mid, angle, 0.9f, streak, JVMath.White,
                    JVMath.V4(seed * 20f + streak, streak, FxClock.Wrapped(ctx.now), seed), JVMath.V4(life * 0.8f, 0.9f, progress * 1.1f, 0f));
            }
            FxDraw.Mesh(ctx, mesh, material, JVMath.TRS(pos, 0f, 1f, 1f), color, JVMath.V4(life, seed, age, 0f), JVMath.Zero4);
            FxDraw.Clip(ctx, FxAssets.AfterimageWake, pos, seed * 360f, 1.2f, 1.2f, progress, life * 0.4f);
        }
    }
}
