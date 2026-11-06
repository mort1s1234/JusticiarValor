using JusticiarValor;
using RimWorld;
using Verse;

namespace JusticiarValorValidation
{
    public sealed partial class MissionValidation
    {
        private int settingsQuestChecks;

        private bool CanOfferQuest(string defName)
        {
            // CanRun caches by tick and points. Vary points instead of shifting the game's tick schedules.
            return DefDatabase<QuestScriptDef>.GetNamed(defName).CanRun(600f + ++settingsQuestChecks, map);
        }

        private void CheckMissionSettings()
        {
            JusticiarValidation.Line("-- Mission settings");
            JusticiarValorSettings settings = JusticiarValorMod.Settings;
            bool recruitment = settings.allowImperialRecruitment;
            bool missions = settings.enableImperialMissions;
            bool disgraced = settings.enableDisgracedSermon;
            Rand.PushState(761033);
            try
            {
                Check(recruitment && missions && disgraced, "new installations preserve all mission features by default");
                Check(settings.Mod?.SettingsCategory() == "Justiciar Valor", "the mod exposes its settings category");
                settings.allowImperialRecruitment = false;
                settings.enableImperialMissions = false;
                settings.enableDisgracedSermon = false;
                settings.Write();
                JusticiarValorSettings restored = LoadedModManager.ReadModSettings<JusticiarValorSettings>(settings.Mod.Content.FolderName,
                    settings.Mod.GetType().Name);
                Check(!restored.allowImperialRecruitment && !restored.enableImperialMissions && !restored.enableDisgracedSermon,
                    "all three disabled settings survive writing and reading the game's mod settings file");

                settings.enableImperialMissions = true;
                settings.enableDisgracedSermon = true;
                Check(CanOfferQuest("MJV_ImperialMission") && CanOfferQuest("MJV_ImperialMission_Blueprint")
                    && CanOfferQuest("MJV_DisgracedSermon"), "all three quest offers pass their real prerequisites with recruitment disabled");

                settings.enableImperialMissions = false;
                Check(!CanOfferQuest("MJV_ImperialMission") && !CanOfferQuest("MJV_ImperialMission_Blueprint")
                    && CanOfferQuest("MJV_DisgracedSermon"), "disabling imperial missions blocks both reward variants while allowing the disgraced quest");

                settings.enableImperialMissions = true;
                settings.enableDisgracedSermon = false;
                Check(CanOfferQuest("MJV_ImperialMission") && CanOfferQuest("MJV_ImperialMission_Blueprint")
                    && !CanOfferQuest("MJV_DisgracedSermon"), "disabling the disgraced quest still allows both imperial mission variants");

                settings.enableImperialMissions = false;
                Check(!CanOfferQuest("MJV_ImperialMission") && !CanOfferQuest("MJV_ImperialMission_Blueprint")
                    && !CanOfferQuest("MJV_DisgracedSermon"), "both quest switches together block every new mod quest offer");

                settings.enableImperialMissions = true;
                settings.enableDisgracedSermon = true;
                Check(CanOfferQuest("MJV_ImperialMission") && CanOfferQuest("MJV_ImperialMission_Blueprint")
                    && CanOfferQuest("MJV_DisgracedSermon"), "re-enabling quests restores offers without a restart");
            }
            finally
            {
                settings.allowImperialRecruitment = recruitment;
                settings.enableImperialMissions = missions;
                settings.enableDisgracedSermon = disgraced;
                settings.Write();
                Rand.PopState();
            }
        }
    }
}
