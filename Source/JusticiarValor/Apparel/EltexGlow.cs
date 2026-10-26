using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Color = UnityEngine.Color;
using Material = UnityEngine.Material;
using MaterialPropertyBlock = UnityEngine.MaterialPropertyBlock;
using Quaternion = UnityEngine.Quaternion;
using Shader = UnityEngine.Shader;
using Texture2D = UnityEngine.Texture2D;
using Vector3 = UnityEngine.Vector3;

namespace JusticiarValor
{
    // A comp of Promachos gear whose eltex inlays glow with its charge: 0 dark, 1 at full charge.
    public interface IEltexGlowSource
    {
        float EltexGlow { get; }
    }

    // The eltex inlays of a piece of Promachos gear, glowing with its charge (IEltexGlowSource): an additive texture over the
    // worn one (Source/Textures/make_eltex_glow.py), added to the pawn's render tree through the apparel's renderNodeProperties.
    public class PawnRenderNodeProperties_EltexGlow : PawnRenderNodeProperties
    {
        // texPath + "_" + the wearer's body type, as body apparel's worn textures are named.
        public bool perBodyType;

        // Opacity of the glow at full charge, on top of color's alpha.
        public float maxOpacity = 0.3f;

        // How the glow grows with the charge: charge ^ response, so below 1 a little charge already shows.
        public float response = 0.7f;

        // Seconds the glow takes to follow a change of charge (two thirds of the way).
        public float fadeSeconds = 0.35f;

        // The glow breathes: dimmer by up to this fraction, once every pulseSeconds.
        public float pulseDepth = 0.2f;

        public float pulseSeconds = 1.8f;

        public PawnRenderNodeProperties_EltexGlow()
        {
            nodeClass = typeof(PawnRenderNode_EltexGlow);
            workerClass = typeof(PawnRenderNodeWorker_EltexGlow);
        }
    }

    public class PawnRenderNode_EltexGlow : PawnRenderNode
    {
        private PawnRenderNode worn;
        private IEltexGlowSource source;
        private bool sourceResolved;
        private float shown;
        private double lastTime = -1.0;

        public PawnRenderNode_EltexGlow(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree)
        {
        }

        public PawnRenderNodeProperties_EltexGlow GlowProps => (PawnRenderNodeProperties_EltexGlow)Props;

        protected override Shader DefaultShader => ShaderDatabase.MoteGlow;

        // The apparel's own node, beside this one under the same parent: the glow takes its place, size and layer.
        public PawnRenderNode Worn
        {
            get
            {
                if (worn == null && apparel != null && parent?.children != null)
                {
                    foreach (PawnRenderNode child in parent.children)
                    {
                        if (child is PawnRenderNode_Apparel && child.apparel == apparel)
                        {
                            worn = child;
                            break;
                        }
                    }
                }
                return worn;
            }
        }

        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            return Props.parentTagDef == PawnRenderNodeTagDefOf.ApparelHead && !Props.overrideMeshSize.HasValue
                ? HumanlikeMeshPoolUtility.GetHumanlikeHeadSetForPawn(pawn)
                : base.MeshSetFor(pawn);
        }

        protected override string TexPathFor(Pawn pawn)
        {
            string path = base.TexPathFor(pawn);
            if (path.NullOrEmpty())
            {
                return null;
            }
            if (GlowProps.perBodyType)
            {
                if (pawn.story?.bodyType == null)
                {
                    return null;
                }
                path += "_" + pawn.story.bodyType.defName;
            }
            // A body type the gear has no glow for (one added by another mod) shows none, rather than a missing texture.
            foreach (string facing in new[] { "_south", "_east", "_north" })
            {
                if (ContentFinder<Texture2D>.Get(path + facing, false) is not null)
                {
                    return path;
                }
            }
            return null;
        }

        // The glow at full charge: color, faded to maxOpacity. The worker dims it to the charge every frame.
        public override Color ColorFor(Pawn pawn)
        {
            Color color = base.ColorFor(pawn);
            return new Color(color.r, color.g, color.b, color.a * GlowProps.maxOpacity);
        }

        protected override IEnumerable<Graphic> GraphicsFor(Pawn pawn)
        {
            Graphic graphic = HasGraphic(pawn) ? GraphicFor(pawn) : null;
            if (graphic != null)
            {
                yield return graphic;
            }
        }

        // Never drawn invisible (PawnRenderNodeWorker_EltexGlow.CanDrawNow): no invisible variant to make.
        protected override void EnsureMaterialVariantsInitialized(Graphic g)
        {
        }

        // How bright the inlays are at this time (seconds of game time), 0 dark to 1: the charge, followed over fadeSeconds,
        // breathing.
        public float Brightness(double now)
        {
            if (!sourceResolved)
            {
                sourceResolved = true;
                List<ThingComp> comps = apparel?.AllComps;
                for (int i = 0; comps != null && i < comps.Count; i++)
                {
                    if (comps[i] is IEltexGlowSource glowSource)
                    {
                        source = glowSource;
                        break;
                    }
                }
            }
            PawnRenderNodeProperties_EltexGlow p = GlowProps;
            float charge = source == null ? 0f : JVMath.Clamp01(source.EltexGlow);
            float target = charge <= 0f ? 0f : (float)Math.Pow(charge, p.response);
            if (lastTime < 0.0 || now < lastTime || p.fadeSeconds <= 0f)
            {
                shown = target;
            }
            else
            {
                shown += (target - shown) * (1f - (float)Math.Exp(-(now - lastTime) / p.fadeSeconds));
            }
            lastTime = now;
            if (p.pulseSeconds <= 0f || p.pulseDepth <= 0f)
            {
                return shown;
            }
            // Each wearer breathes at its own phase.
            double cycles = now / p.pulseSeconds + tree.pawn.thingIDNumber * 0.618;
            float phase = (float)(cycles - Math.Floor(cycles));
            return shown * (1f - p.pulseDepth * 0.5f * (1f - MathF.Cos(phase * 2f * MathF.PI)));
        }
    }

    // Draws PawnRenderNode_EltexGlow exactly over the apparel it lights: that apparel node's offset, rotation, scale and
    // layer (just above it), shown whenever it is, its brightness set every frame.
    public class PawnRenderNodeWorker_EltexGlow : PawnRenderNodeWorker
    {
        // Above the apparel it lights, below the next one.
        private const float LayerAbove = 0.5f;

        private static PawnRenderNode Worn(PawnRenderNode node) => (node as PawnRenderNode_EltexGlow)?.Worn;

        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            // Portraits and the far zoom's pawn atlas are drawn once and kept, so a glow following the charge stays out.
            if (parms.Portrait || parms.Cache || parms.Statue || parms.flags.FlagSet(PawnRenderFlags.Invisible) || !base.CanDrawNow(node, parms))
            {
                return false;
            }
            PawnRenderNode worn = Worn(node);
            return worn != null && worn.Worker.CanDrawNow(worn, parms);
        }

        // The brightness goes in through the draw's tint, which the game turns into the block's colour (tint x the
        // material's colour) just before drawing, on the main thread: the block itself is left to the game.
        public override MaterialPropertyBlock GetMaterialPropertyBlock(PawnRenderNode node, Material material, PawnDrawParms parms)
        {
            if (node is PawnRenderNode_EltexGlow glow)
            {
                Color tint = parms.tint;
                parms.tint = new Color(tint.r, tint.g, tint.b, tint.a * glow.Brightness(FxClock.Now));
            }
            return base.GetMaterialPropertyBlock(node, material, parms);
        }

        protected override Material GetMaterial(PawnRenderNode node, PawnDrawParms parms)
        {
            // Headgear turns with the head of a pawn crawling face down (PawnRenderNodeWorker_FlipWhenCrawling).
            if (parms.flipHead && Worn(node)?.Worker is PawnRenderNodeWorker_FlipWhenCrawling)
            {
                parms.facing = parms.facing.Opposite;
            }
            return base.GetMaterial(node, parms);
        }

        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            PawnRenderNode worn = Worn(node);
            return worn != null ? worn.Worker.OffsetFor(worn, parms, out pivot) : base.OffsetFor(node, parms, out pivot);
        }

        public override Quaternion RotationFor(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNode worn = Worn(node);
            return worn != null ? worn.Worker.RotationFor(worn, parms) : base.RotationFor(node, parms);
        }

        public override Vector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNode worn = Worn(node);
            return worn != null ? worn.Worker.ScaleFor(worn, parms) : base.ScaleFor(node, parms);
        }

        public override float LayerFor(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNode worn = Worn(node);
            return worn != null ? worn.Worker.LayerFor(worn, parms) + LayerAbove : base.LayerFor(node, parms);
        }
    }
}
