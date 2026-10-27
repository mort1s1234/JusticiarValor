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
        public static ThingDef MJV_ImperialCodexAuspex;
        public static ThingDef MJV_Projectile_PsyOrb;

        // Justiciar Valor's throw: its victims land stunned (PawnFlyer_MJV_Knockback).
        public static ThingDef MJV_PawnFlyer_Knockback;

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

        public static ResearchProjectDef MJV_JusticiarGear;

        public static DutyDef MJV_DeliverSermon;
        public static DutyDef MJV_HearSermon;

        public static JobDef MJV_PreachSermon;
        public static JobDef MJV_TalkToJusticiar;

        public static ThoughtDef MJV_HeardImperialSermon;
        public static ThoughtDef MJV_SermonWellAttended;
        public static ThoughtDef MJV_SermonRefused;

        public static HistoryEventDef MJV_ConvertedToImperialFaith;
        public static HistoryEventDef MJV_SermonsWellAttended;
        public static HistoryEventDef MJV_SermonsRefused;
        public static HistoryEventDef MJV_JusticiarTurnedAway;

        // The sermon's ritual window (Dialog_MJV_BeginSermon).
        public static RitualOutcomeEffectDef MJV_ImperialSermon;

        // The sermon's command buttons (SermonRequests, LordJob_MJV_Sermon).
        public static CommandIconDef MJV_HoldSermon;
        public static CommandIconDef MJV_RefuseSermon;
        public static CommandIconDef MJV_LeaveSermon;
        public static CommandIconDef MJV_CancelSermon;

        public static MentalStateDef MJV_CalledToService;

        static MJV_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MJV_DefOf));
        }
    }
}
