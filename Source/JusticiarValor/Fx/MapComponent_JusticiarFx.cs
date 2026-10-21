using System;
using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace JusticiarValor
{
    // Something that draws an effect every frame while it is on a map: the psychic shield, the psy-orbs.
    public interface IFxDrawer
    {
        // False once the drawer no longer belongs to the map (despawned, unequipped...); it is then dropped.
        bool FxActiveOn(Map map);

        void DrawFx(FxDrawContext ctx);
    }

    public sealed class FxDrawContext
    {
        public DrawBatch batch;
        public double now;
        public CellRect view;
        public Map map;
    }

    // A short-lived effect: drawn every frame until its duration has passed.
    public abstract class FxEffect
    {
        public double start;
        public float duration = 1f;

        public virtual bool Cancelled => false;

        // progress runs 0..1 over the effect's duration, age is in seconds.
        public abstract void Draw(FxDrawContext ctx, float progress, float age);
    }

    // Draws the Justiciar Valor effects of one map. Everything is submitted through a DrawBatch of its own and flushed
    // once a frame, from MapComponentUpdate (Map.MapUpdate calls it after the map's own drawing, every frame).
    public class MapComponent_JusticiarFx : MapComponent
    {
        private readonly List<FxEffect> effects = new List<FxEffect>();
        private readonly List<IFxDrawer> drawers = new List<IFxDrawer>();
        private readonly HashSet<IFxDrawer> drawerSet = new HashSet<IFxDrawer>();
        private readonly DrawBatch batch = new DrawBatch();
        private readonly FxDrawContext ctx = new FxDrawContext();

        public MapComponent_JusticiarFx(Map map) : base(map)
        {
        }

        public int ActiveEffectCount => effects.Count;

        public int DrawerCount => drawers.Count;

        public static MapComponent_JusticiarFx Of(Map map) => map?.GetComponent<MapComponent_JusticiarFx>();

        public static void Spawn(Map map, FxEffect effect)
        {
            MapComponent_JusticiarFx comp = Of(map);
            if (comp == null || effect == null)
            {
                return;
            }
            effect.start = FxClock.Now;
            comp.effects.Add(effect);
        }

        public static void Register(Map map, IFxDrawer drawer)
        {
            MapComponent_JusticiarFx comp = Of(map);
            if (comp != null && drawer != null && comp.drawerSet.Add(drawer))
            {
                comp.drawers.Add(drawer);
            }
        }

        public static bool IsRegistered(Map map, IFxDrawer drawer)
        {
            MapComponent_JusticiarFx comp = Of(map);
            return comp != null && comp.drawerSet.Contains(drawer);
        }

        public override void MapComponentUpdate()
        {
            double now = FxClock.Now;
            bool visible = WorldRendererUtility.DrawingMap && Find.CurrentMap == map;
            if (visible)
            {
                ctx.batch = batch;
                ctx.now = now;
                ctx.view = Find.CameraDriver.CurrentViewRect.ExpandedBy(5);
                ctx.map = map;
            }
            try
            {
                for (int i = effects.Count - 1; i >= 0; i--)
                {
                    FxEffect effect = effects[i];
                    float age = (float)(now - effect.start);
                    if (age >= effect.duration || effect.Cancelled)
                    {
                        effects.RemoveAt(i);
                        continue;
                    }
                    if (visible)
                    {
                        effect.Draw(ctx, age < 0f ? 0f : age / effect.duration, age < 0f ? 0f : age);
                    }
                }
                for (int i = drawers.Count - 1; i >= 0; i--)
                {
                    IFxDrawer drawer = drawers[i];
                    if (!drawer.FxActiveOn(map))
                    {
                        drawers.RemoveAt(i);
                        drawerSet.Remove(drawer);
                        continue;
                    }
                    if (visible)
                    {
                        drawer.DrawFx(ctx);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Justiciar Valor] Drawing effects failed: " + ex, 0x4A56F1);
                effects.Clear();
            }
            finally
            {
                if (visible)
                {
                    batch.Flush();
                }
            }
        }
    }
}
