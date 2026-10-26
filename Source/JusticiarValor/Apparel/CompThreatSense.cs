using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace JusticiarValor
{
    public class CompProperties_ThreatSense : CompProperties
    {
        // How far the helmet senses murderous intent.
        public float range = 30f;

        public int checkIntervalTicks = 30;

        // Threats beyond this many add nothing more.
        public int maxThreats = 5;

        // Severity = number of hostile pawns currently attacking or aiming at the wearer.
        public HediffDef hediff;

        public CompProperties_ThreatSense()
        {
            compClass = typeof(CompThreatSense);
        }
    }

    // Promachos helmet: scans for murderous intent aimed at its wearer. Each hostile pawn attacking or aiming at the
    // wearer raises the severity of a hediff that sharpens reflexes, speed and consciousness.
    public class CompThreatSense : ThingComp, IEltexGlowSource
    {
        private int lastThreats;

        // The pawns counted by the last scan, for the pulse's markers.
        private readonly List<Pawn> sources = new List<Pawn>();

        public CompProperties_ThreatSense Props => (CompProperties_ThreatSense)props;

        public int CurrentThreats => lastThreats;

        // The helmet's eltex inlays glow with the murderous intent it senses: the hediff's severity, which settles slowly
        // once nobody aims at the wearer any more.
        public float EltexGlow
        {
            get
            {
                Pawn wearer = JVUtility.WearerOf(parent);
                if (wearer == null || wearer.Dead || Props.maxThreats <= 0)
                {
                    return 0f;
                }
                Hediff intent = Props.hediff == null ? null : wearer.health.hediffSet.GetFirstHediffOfDef(Props.hediff);
                return JVMath.Clamp01((intent?.Severity ?? lastThreats) / Props.maxThreats);
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            Pawn wearer = JVUtility.WearerOf(parent);
            if (wearer == null || !wearer.Spawned || !wearer.IsHashIntervalTick(Props.checkIntervalTicks))
            {
                return;
            }
            int threats = JVUtility.IsConscious(wearer) ? CountThreats(wearer) : 0;
            UpdateHediff(wearer, threats);
            if (threats > lastThreats && wearer.Map == Find.CurrentMap)
            {
                MapComponent_JusticiarFx.Spawn(wearer.Map, new ThreatPulseFx(wearer, (float)threats / Props.maxThreats, sources));
            }
            lastThreats = threats;
        }

        private int CountThreats(Pawn wearer)
        {
            sources.Clear();
            int count = 0;
            float rangeSquared = Props.range * Props.range;
            List<IAttackTarget> potential = wearer.Map.attackTargetsCache.GetPotentialTargetsFor(wearer);
            for (int i = 0; i < potential.Count && count < Props.maxThreats; i++)
            {
                if (potential[i].Thing is Pawn pawn && !pawn.Dead && !pawn.Downed
                    && pawn.Position.DistanceToSquared(wearer.Position) <= rangeSquared && JVUtility.IsTargeting(pawn, wearer))
                {
                    count++;
                    sources.Add(pawn);
                }
            }
            return count;
        }

        private void UpdateHediff(Pawn wearer, int threats)
        {
            if (Props.hediff == null)
            {
                return;
            }
            Hediff hediff = wearer.health.hediffSet.GetFirstHediffOfDef(Props.hediff);
            if (threats > 0)
            {
                if (hediff == null)
                {
                    hediff = HediffMaker.MakeHediff(Props.hediff, wearer);
                    hediff.Severity = threats;
                    wearer.health.AddHediff(hediff);
                }
                else
                {
                    hediff.Severity = threats;
                }
            }
            else if (hediff != null)
            {
                // Reflexes settle over a few seconds once nobody is aiming at the wearer any more.
                hediff.Severity -= 0.5f;
                if (hediff.Severity <= 0.01f)
                {
                    wearer.health.RemoveHediff(hediff);
                }
            }
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            lastThreats = 0;
            Hediff hediff = Props.hediff == null ? null : pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediff);
            if (hediff != null)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }
    }
}
