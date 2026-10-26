using RimWorld;
using Verse;
using Color = UnityEngine.Color;
using Rect = UnityEngine.Rect;
using TextAnchor = UnityEngine.TextAnchor;
using Texture2D = UnityEngine.Texture2D;
using Vector2 = UnityEngine.Vector2;

namespace JusticiarValor
{
    [StaticConstructorOnStartup]
    internal static class GizmoTextures
    {
        public static readonly Texture2D ShieldBar = SolidColorMaterials.NewSolidColorTexture(FxPalette.Deep);
        public static readonly Texture2D ChargeBar = SolidColorMaterials.NewSolidColorTexture(FxPalette.Sage);
        public static readonly Texture2D OverdriveBar = SolidColorMaterials.NewSolidColorTexture(FxPalette.Pale);
        public static readonly Texture2D EmptyBar = SolidColorMaterials.NewSolidColorTexture(new Color(0f, 0f, 0f, 0f));
        public static readonly Texture2D PipFull = SolidColorMaterials.NewSolidColorTexture(FxPalette.Sage);
        public static readonly Texture2D PipEmpty = SolidColorMaterials.NewSolidColorTexture(new Color(0.20f, 0.24f, 0.22f));
    }

    // Promachos armor: the psychic shield's energy and the energy stored for Invictus.
    public class Gizmo_PromachosArmor : Gizmo
    {
        private readonly CompPsychicShield shield;

        public Gizmo_PromachosArmor(CompPsychicShield shield)
        {
            this.shield = shield;
            Order = -100f;
        }

        public override float GetWidth(float maxWidth) => 150f;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            var rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Rect inner = rect.ContractedBy(6f);
            Widgets.DrawWindowBackground(rect);
            float rowHeight = inner.height / 3f;
            var labelRect = new Rect(inner.x, inner.y, inner.width, rowHeight);
            var shieldRect = new Rect(inner.x, inner.y + rowHeight, inner.width, rowHeight - 1f);
            var chargeRect = new Rect(inner.x, inner.y + rowHeight * 2f, inner.width, rowHeight - 1f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(labelRect, shield.parent.LabelCap);

            float max = shield.EnergyMax;
            Widgets.FillableBar(shieldRect, max > 0f ? JVMath.Clamp01(shield.Energy / max) : 0f,
                shield.Overdriven ? GizmoTextures.OverdriveBar : GizmoTextures.ShieldBar, GizmoTextures.EmptyBar, false);
            Widgets.FillableBar(chargeRect, shield.ChargeFraction, GizmoTextures.ChargeBar, GizmoTextures.EmptyBar, false);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(shieldRect, max <= 0f ? (string)"MJV_ShieldNoSensitivity".Translate()
                : shield.Resetting ? (string)"MJV_ShieldResetting".Translate()
                : "MJV_ShieldEnergy".Translate(shield.Energy.ToString("F0"), max.ToString("F0")).Resolve());
            Widgets.Label(chargeRect, (shield.ChargeDraining ? "MJV_InvictusChargeFading" : "MJV_InvictusCharge")
                .Translate(shield.ChargeFraction.ToStringPercent()).Resolve());
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(inner, new TipSignal(Tip, shield.parent.thingIDNumber ^ 0x4D4A56));
            return new GizmoResult(GizmoState.Clear);
        }

        // Built only while the gizmo is hovered.
        private string Tip()
        {
            Pawn wearer = shield.Wearer;
            float sensitivity = wearer?.GetStatValue(StatDefOf.PsychicSensitivity) ?? 1f;
            CompProperties_PsychicShield props = shield.Props;
            return "MJV_PromachosArmorTip".Translate(sensitivity.ToStringPercent(), props.energyPerSensitivity.ToString("F0"),
                (props.chargeDrainDelayTicks / 60f).ToString("0.#"));
        }
    }

    // Warlockbane shield: its charges.
    public class Gizmo_WarlockbaneStatus : Gizmo
    {
        private readonly CompWarlockbane comp;

        public Gizmo_WarlockbaneStatus(CompWarlockbane comp)
        {
            this.comp = comp;
            Order = -99f;
        }

        public override float GetWidth(float maxWidth) => 110f;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            var rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Rect inner = rect.ContractedBy(6f);
            Widgets.DrawWindowBackground(rect);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, inner.height / 2f), comp.parent.LabelCap);
            int max = comp.Props.maxCharges;
            float gap = 4f;
            float pipWidth = (inner.width - gap * (max - 1)) / max;
            float pipY = inner.y + inner.height / 2f + 2f;
            for (int i = 0; i < max; i++)
            {
                var pip = new Rect(inner.x + i * (pipWidth + gap), pipY, pipWidth, inner.height / 2f - 4f);
                if (i < comp.Charges)
                {
                    Widgets.DrawTexturePart(pip, new Rect(0f, 0f, 1f, 1f), GizmoTextures.PipFull);
                }
                else if (i == comp.Charges)
                {
                    Widgets.FillableBar(pip, comp.RechargeFraction, GizmoTextures.ChargeBar, GizmoTextures.PipEmpty, false);
                }
                else
                {
                    Widgets.DrawTexturePart(pip, new Rect(0f, 0f, 1f, 1f), GizmoTextures.PipEmpty);
                }
            }
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(inner, "MJV_WarlockbaneTip".Translate(comp.Charges, max));
            return new GizmoResult(GizmoState.Clear);
        }
    }
}
