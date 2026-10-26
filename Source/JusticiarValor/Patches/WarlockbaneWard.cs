using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace JusticiarValor
{
    // The Warlockbane shield over one cast. Every ability or item cast resolves inside Begin/End (the Warlockbane patches),
    // marked Psychic when it is a psychic attack (PsychicAttack). Whether a bearer turns it aside is decided once (Turns):
    // when the psychic cast names the bearer a target, or when it reads the bearer's psychic sensitivity as it resolves
    // (StatPart_WarlockbaneWard). The latter catches the casts that pick their own victims without naming them, like
    // Neuroquake going through every pawn on the map. A bearer the cast attacks spends a charge and reads as psychically
    // deaf until the cast is over, so whatever goes by psychic sensitivity passes it by; one without a charge left is not
    // warded. At the end the shields' backlash heat reaches the caster, once it has paid for the cast, and the wards'
    // bearers read true again.
    public static class WarlockbaneWard
    {
        private sealed class Cast
        {
            public Pawn caster;
            public int depth;
            public bool psychic;
            // A cast resolves within the tick it began in.
            public int tick;
            // The pawns decided on in this cast: warded (true) or not (false).
            public readonly Dictionary<Pawn, bool> pawns = new Dictionary<Pawn, bool>();
            public readonly List<(CompWarlockbane shield, Pawn bearer)> wards = new List<(CompWarlockbane, Pawn)>();
        }

        // Casts in progress on this thread: one cast inside another (an effect casting) is its own, a caster's cast reached
        // again (a VEF ability's Cast calling its base) the same.
        [ThreadStatic] private static List<Cast> casts;

        // Set while a psychic sensitivity is read for something other than the cast (TrueSensitivity), and while a bearer's
        // is worked out again at the end of its ward (Restore).
        [ThreadStatic] private static bool suspended;

        private static readonly AccessTools.FieldRef<Pawn_PsychicEntropyTracker, int> sensitivityCachedTick = SensitivityCachedTick();

        // Casts are only ever left unended by a broken patch: one from an earlier tick wards nothing, and the next cast
        // clears it away.
        private static bool Active => casts != null && casts.Count > 0 && !suspended && casts[casts.Count - 1].tick == Now;

        // Whether a psychic sensitivity read now is the psychic cast in progress asking after a pawn.
        internal static bool Warding => Active && casts[casts.Count - 1].psychic;

        private static int Now => Current.Game?.tickManager?.TicksGame ?? -1;

        public static void Begin(Pawn caster)
        {
            casts ??= new List<Cast>();
            int now = Now;
            if (casts.Count > 0 && casts[0].tick != now)
            {
                Log.ErrorOnce("[Justiciar Valor] A Warlockbane ward was left open by a cast of an earlier tick; it is closed now.", 0x4A56574D);
                foreach (Cast stale in casts)
                {
                    foreach ((CompWarlockbane _, Pawn bearer) in stale.wards)
                    {
                        Restore(bearer);
                    }
                }
                casts.Clear();
            }
            if (casts.Count > 0 && casts[casts.Count - 1].caster == caster)
            {
                casts[casts.Count - 1].depth++;
                return;
            }
            casts.Add(new Cast { caster = caster, depth = 1, tick = now });
        }

        // The cast in progress is a psychic attack.
        public static void Psychic()
        {
            if (casts != null && casts.Count > 0)
            {
                casts[casts.Count - 1].psychic = true;
            }
        }

        public static void End()
        {
            if (casts == null || casts.Count == 0)
            {
                return;
            }
            Cast cast = casts[casts.Count - 1];
            if (--cast.depth > 0)
            {
                return;
            }
            casts.RemoveAt(casts.Count - 1);
            foreach ((CompWarlockbane shield, Pawn bearer) in cast.wards)
            {
                Restore(bearer);
                shield.BurnCaster(cast.caster, bearer);
            }
        }

        // Whether the pawn's Warlockbane shield turns the cast in progress aside: decided the first time it is asked in a
        // cast, spending a charge (and playing the deflection, stunning the caster) if it does.
        public static bool Turns(Pawn pawn)
        {
            if (!Active || pawn == null)
            {
                return false;
            }
            Cast cast = casts[casts.Count - 1];
            if (cast.pawns.TryGetValue(pawn, out bool warded))
            {
                return warded;
            }
            // Undecided while deciding: a psychic sensitivity read on the way sees the pawn as it is.
            cast.pawns[pawn] = false;
            CompWarlockbane shield = pawn.WornComp<CompWarlockbane>();
            if (shield == null || !PsychicAttack.Attacks(cast.caster, pawn) || !JVUtility.IsConscious(pawn) || !shield.TryDeflect(cast.caster, pawn))
            {
                return false;
            }
            cast.pawns[pawn] = true;
            cast.wards.Add((shield, pawn));
            return true;
        }

        // The pawn's psychic sensitivity as it is, for this mod's own uses of it (the Promachos shield's capacity): a cast in
        // progress reading it then is not asking after the pawn, and must not find it warded.
        public static float TrueSensitivity(Pawn pawn, int cacheStaleAfterTicks = -1)
        {
            bool wasSuspended = suspended;
            suspended = true;
            try
            {
                // Uncached during a cast: the stat's cache may hold the zero of a ward.
                return pawn.GetStatValue(StatDefOf.PsychicSensitivity, true, casts != null && casts.Count > 0 ? -1 : cacheStaleAfterTicks);
            }
            finally
            {
                suspended = wasSuspended;
            }
        }

        // The bearer's psychic sensitivity as it is, now its ward is over: the pawn's own cache of it cleared, and the stat
        // worked out again, which overwrites the zero the stat's cache may have kept.
        private static void Restore(Pawn bearer)
        {
            if (bearer.psychicEntropy != null && sensitivityCachedTick != null)
            {
                sensitivityCachedTick(bearer.psychicEntropy) = -1;
            }
            TrueSensitivity(bearer);
        }

        private static AccessTools.FieldRef<Pawn_PsychicEntropyTracker, int> SensitivityCachedTick()
        {
            try
            {
                return AccessTools.FieldRefAccess<Pawn_PsychicEntropyTracker, int>("psychicSensitivityCachedTick");
            }
            catch (Exception)
            {
                Log.Warning("[Justiciar Valor] Pawn_PsychicEntropyTracker.psychicSensitivityCachedTick is gone: a Warlockbane bearer may read as psychically deaf until the end of the tick it turned a cast aside in.");
                return null;
            }
        }
    }

    // On PsychicSensitivity, last of its parts (added at startup): none for a Warlockbane bearer the psychic cast in
    // progress attacks (WarlockbaneWard). A pawn already psychically deaf is left as it is, without spending a charge.
    public class StatPart_WarlockbaneWard : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (val > 0f && WarlockbaneWard.Warding && req.Thing is Pawn pawn && WarlockbaneWard.Turns(pawn))
            {
                val = 0f;
            }
        }

        public override string ExplanationPart(StatRequest req) => null;
    }

    [StaticConstructorOnStartup]
    public static class StatPart_WarlockbaneWardSetup
    {
        static StatPart_WarlockbaneWardSetup()
        {
            // In code rather than a patch: after every mod's patches, so that no part offsets the zero again.
            StatDef stat = StatDefOf.PsychicSensitivity;
            stat.parts ??= new List<StatPart>();
            stat.parts.Add(new StatPart_WarlockbaneWard { parentStat = stat });
        }
    }
}
