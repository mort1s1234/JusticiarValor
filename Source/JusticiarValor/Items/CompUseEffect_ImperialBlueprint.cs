using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace JusticiarValor
{
    public class CompProperties_UseEffectImperialBlueprint : CompProperties_UseEffect
    {
        // Research held by factions with these category tags counts as an imperial blueprint.
        public List<string> factionCategoryTags = new List<string> { "Empire" };

        public CompProperties_UseEffectImperialBlueprint()
        {
            compClass = typeof(CompUseEffect_ImperialBlueprint);
        }
    }

    // Imperial tome: studying it applies a techprint of a random unfinished project whose techprints the Empire holds,
    // as if the colony had bought one.
    public class CompUseEffect_ImperialBlueprint : CompUseEffect
    {
        public CompProperties_UseEffectImperialBlueprint Props => (CompProperties_UseEffectImperialBlueprint)props;

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);
            List<ResearchProjectDef> candidates = Candidates();
            if (candidates.Count == 0)
            {
                Messages.Message("MJV_TomeNothingToLearn".Translate(usedBy.Named("PAWN")), usedBy, MessageTypeDefOf.NeutralEvent);
                return;
            }
            ResearchProjectDef project = candidates.RandomElement();
            Find.ResearchManager.ApplyTechprint(project, usedBy);
            SoundDefOf.TechprintApplied.PlayOneShotOnCamera();
            Find.LetterStack.ReceiveLetter("MJV_TomeStudiedLabel".Translate(),
                "MJV_TomeStudiedText".Translate(usedBy.Named("PAWN"), project.LabelCap, Find.ResearchManager.GetTechprints(project), project.TechprintCount),
                LetterDefOf.PositiveEvent, usedBy);
        }

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            if (Candidates().Count == 0)
            {
                return "MJV_TomeNoBlueprints".Translate();
            }
            return base.CanBeUsedBy(p);
        }

        private List<ResearchProjectDef> Candidates()
        {
            var result = new List<ResearchProjectDef>();
            List<ResearchProjectDef> all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ResearchProjectDef project = all[i];
                if (project.TechprintCount <= 0 || project.IsFinished || project.TechprintRequirementMet || project.heldByFactionCategoryTags == null)
                {
                    continue;
                }
                for (int j = 0; j < Props.factionCategoryTags.Count; j++)
                {
                    if (project.heldByFactionCategoryTags.Contains(Props.factionCategoryTags[j]))
                    {
                        result.Add(project);
                        break;
                    }
                }
            }
            return result;
        }
    }
}
