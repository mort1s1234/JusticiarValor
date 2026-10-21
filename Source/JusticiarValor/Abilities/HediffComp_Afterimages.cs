using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;
using Color = UnityEngine.Color;
using Mesh = UnityEngine.Mesh;
using Texture = UnityEngine.Texture;
using Vector3 = UnityEngine.Vector3;

namespace JusticiarValor
{
    public class HediffCompProperties_Afterimages : HediffCompProperties
    {
        public int intervalTicks = 3;

        public float lifeSeconds = 0.42f;

        // Legacy customization is retained for save/Def compatibility; rendering normalizes the hue to the armor.
        public List<Color> colors;

        public HediffCompProperties_Afterimages()
        {
            compClass = typeof(HediffComp_Afterimages);
        }
    }

    // Invictus overdrive: the wearer leaves fading afterimages wherever it moves.
    public class HediffComp_Afterimages : HediffComp
    {
        private static readonly Color DefaultColor = FxPalette.Sage;
        private Vector3 lastPos;
        private int counter;

        public HediffCompProperties_Afterimages Props => (HediffCompProperties_Afterimages)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            Pawn pawn = Pawn;
            if (!pawn.Spawned || pawn.Map != Find.CurrentMap || !pawn.IsHashIntervalTick(Props.intervalTicks))
            {
                return;
            }
            Vector3 pos = pawn.DrawPos;
            if (JVMath.DistanceFlatSquared(pos, lastPos) < 0.04f)
            {
                return;
            }
            Vector3 previous = JVMath.WithY(lastPos, FxDraw.AltitudeGlow);
            lastPos = pos;
            if (!AfterimageUtility.TryCapture(pawn, out Texture atlas, out Mesh mesh))
            {
                return;
            }
            Color color = Props.colors.NullOrEmpty() ? DefaultColor : Props.colors[counter++ % Props.colors.Count];
            // Projected light, like the other eltex effects; it fades in once the pawn has moved on.
            Vector3 at = JVMath.WithY(pos, FxDraw.AltitudeGlow);
            MapComponent_JusticiarFx.Spawn(pawn.Map, new AfterimageFx(atlas, mesh, at, color, Props.lifeSeconds, previous));
        }
    }

    public static class AfterimageUtility
    {
        // PawnRenderer.GetBlitMeshUpdatedFrame renders the pawn into its frame of the pawn texture atlas when that frame
        // is dirty and returns the frame's mesh. The game only does this while zoomed out (ZoomRootSize > 18, where pawns
        // are drawn from the atlas); afterimages need the frame at every zoom, so they ask for it here.
        private static readonly Func<PawnRenderer, PawnTextureAtlasFrameSet, Rot4, PawnDrawMode, Mesh> blitFrame = CreateBlit();

        private static Func<PawnRenderer, PawnTextureAtlasFrameSet, Rot4, PawnDrawMode, Mesh> CreateBlit()
        {
            var method = AccessTools.Method(typeof(PawnRenderer), "GetBlitMeshUpdatedFrame",
                new[] { typeof(PawnTextureAtlasFrameSet), typeof(Rot4), typeof(PawnDrawMode) });
            if (method == null)
            {
                Log.Warning("[Justiciar Valor] PawnRenderer.GetBlitMeshUpdatedFrame not found: Invictus afterimages only appear while zoomed out.");
                return null;
            }
            return AccessTools.MethodDelegate<Func<PawnRenderer, PawnTextureAtlasFrameSet, Rot4, PawnDrawMode, Mesh>>(method);
        }

        public static bool CanBlit => blitFrame != null;

        // The pawn's current look as an atlas texture and the mesh mapping its frame, for drawing a copy of it.
        public static bool TryCapture(Pawn pawn, out Texture atlas, out Mesh mesh)
        {
            atlas = null;
            mesh = null;
            if (!pawn.RaceProps.Humanlike || pawn.Drawer?.renderer == null
                || !GlobalTextureAtlasManager.TryGetPawnFrameSet(pawn, out PawnTextureAtlasFrameSet frameSet, out bool _, true))
            {
                return false;
            }
            int index = frameSet.GetIndex(pawn.Rotation, PawnDrawMode.BodyAndHead);
            if (blitFrame != null)
            {
                mesh = blitFrame(pawn.Drawer.renderer, frameSet, pawn.Rotation, PawnDrawMode.BodyAndHead);
            }
            else if (!frameSet.isDirty[index])
            {
                mesh = frameSet.meshes[index];
            }
            atlas = frameSet.atlas;
            return mesh is not null && atlas is not null;
        }
    }
}
