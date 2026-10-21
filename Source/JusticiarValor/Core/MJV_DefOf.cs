using RimWorld;
using Verse;
using Verse.AI;

namespace JusticiarValor
{
    [DefOf]
    public static class MJV_DefOf
    {
        public static ThingDef MJV_Apparel_ArmorHelmetPromachosHelmet;
        public static ThingDef MJV_Apparel_PromachosArmor;
        public static ThingDef MJV_Weapon_EnergyPsySpear;
        public static ThingDef MJV_MeleeWeapon_EnergyPsyblade;
        public static ThingDef MJV_Apparel_WarlockbaneShield;
        public static ThingDef MJV_ImperialTome;
        public static ThingDef MJV_Projectile_PsyOrb;

        // Core: the flyer the game uses for pawns that are thrown and land stunned.
        public static ThingDef PawnFlyer_Stun;

        public static HediffDef MJV_MurderousIntent;
        public static HediffDef MJV_PsychicDaze;
        public static HediffDef MJV_PsyOrbSlow;
        public static HediffDef MJV_InvictusOverdrive;

        public static AbilityDef MJV_JusticiarValor;
        public static AbilityDef MJV_Invictus;
        public static AbilityDef MJV_CelestialPierce;

        public static DamageDef MJV_PsychicConcussion;
        public static DamageDef MJV_PsyBurn;

        public static ShaderTypeDef MJV_Afterimage;
        public static ShaderTypeDef MJV_CelestialBlade;
        public static ShaderTypeDef MJV_Deflect;
        public static ShaderTypeDef MJV_GroundFissure;
        public static ShaderTypeDef MJV_InvictusBurst;
        public static ShaderTypeDef MJV_OrbTrail;
        public static ShaderTypeDef MJV_PsyOrb;
        public static ShaderTypeDef MJV_Sprite;
        public static ShaderTypeDef MJV_PsychicShield;
        public static ShaderTypeDef MJV_PsychicWave;
        public static ShaderTypeDef MJV_SermonHalo;
        public static ShaderTypeDef MJV_ThreatPulse;

        public static PawnKindDef MJV_Justiciar;
        public static PawnKindDef MJV_ImperialMissionary;

        public static ResearchProjectDef MJV_JusticiarGear;

        public static DutyDef MJV_DeliverSermon;
        public static DutyDef MJV_HearSermon;

        public static JobDef MJV_PreachSermon;

        public static ThoughtDef MJV_HeardImperialSermon;

        public static HistoryEventDef MJV_ConvertedToImperialFaith;

        public static MentalStateDef MJV_CalledToService;

        static MJV_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MJV_DefOf));
        }
    }
}
