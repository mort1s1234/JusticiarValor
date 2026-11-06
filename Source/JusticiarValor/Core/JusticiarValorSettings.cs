using Verse;

namespace JusticiarValor
{
    public class JusticiarValorSettings : ModSettings
    {
        public bool allowImperialRecruitment = true;
        public bool enableImperialMissions = true;
        public bool enableDisgracedSermon = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref allowImperialRecruitment, "allowImperialRecruitment", true);
            Scribe_Values.Look(ref enableImperialMissions, "enableImperialMissions", true);
            Scribe_Values.Look(ref enableDisgracedSermon, "enableDisgracedSermon", true);
        }
    }
}
