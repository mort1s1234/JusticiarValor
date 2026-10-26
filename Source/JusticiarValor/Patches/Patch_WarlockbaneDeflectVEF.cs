using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace JusticiarValor
{
    // Warlockbane shield against Vanilla Expanded Framework abilities, Vanilla Psycasts Expanded's psycasts first of all.
    // VEF.Abilities.Ability is unrelated to RimWorld.Ability, which Patch_WarlockbaneDeflect never sees. A VEF ability
    // resolves in its virtual Cast(params GlobalTargetInfo[]): the base sets the cooldown and runs its extensions' Cast,
    // where VPE's AbilityExtension_Psycast takes the psyfocus and neural heat. Ability classes override Cast and act on
    // their own targets array, before calling base.Cast (VPE's Ability_Teleport) or after it (most of them). So every
    // declaration of Cast gets the prefix, which takes the deflected bearers out of the array before that body sees it; the
    // caster still pays for the cast. The cast resolves inside its WarlockbaneWard (begun by the outermost Cast, ended
    // after it), which also shields bearers it reaches without naming them, and burns the caster once it has paid.
    // Psychic: VPE's psycasts, and abilities whose class, def or extensions go by psychic sensitivity (PsychicAttack).
    // Bound by reflection: the build does not reference VEF, and VPE is optional.
    public static class Patch_WarlockbaneDeflectVEF
    {
        private const string VpePackageId = "VanillaExpanded.VPsycastsE";

        private static Type vefAbility, psycastExtension, psycastHediff;
        private static FieldInfo pawnField, defField, cooldownField, targetCountField;
        private static MethodInfo cooldownForPawn, psyfocusUsedByPawn, entropyUsedByPawn, useAbility;
        private static readonly Dictionary<Def, bool> psychic = new Dictionary<Def, bool>();

        // Every Cast(GlobalTargetInfo[]) declared by VEF.Abilities.Ability and its subclasses, from any mod; a single null
        // when VEF's members are not where expected (the caller reports it).
        public static IEnumerable<MethodBase> TargetMethods()
        {
            vefAbility = GenTypes.GetTypeInAnyAssembly("VEF.Abilities.Ability");
            Type abilityDef = GenTypes.GetTypeInAnyAssembly("VEF.Abilities.AbilityDef");
            if (vefAbility == null || abilityDef == null)
            {
                yield return null;
                yield break;
            }
            pawnField = AccessTools.Field(vefAbility, "pawn");
            defField = AccessTools.Field(vefAbility, "def");
            cooldownField = AccessTools.Field(vefAbility, "cooldown");
            targetCountField = AccessTools.Field(abilityDef, "targetCount");
            cooldownForPawn = AccessTools.Method(vefAbility, "GetCooldownForPawn", Type.EmptyTypes);
            if (pawnField?.FieldType != typeof(Pawn) || defField == null || !typeof(Def).IsAssignableFrom(defField.FieldType)
                || cooldownField?.FieldType != typeof(int) || targetCountField?.FieldType != typeof(int) || cooldownForPawn?.ReturnType != typeof(int))
            {
                yield return null;
                yield break;
            }
            BindVpe();
            var types = new List<Type> { vefAbility };
            types.AddRange(vefAbility.AllSubclasses());
            foreach (Type type in types)
            {
                MethodInfo cast = type.ContainsGenericParameters ? null : AccessTools.DeclaredMethod(type, "Cast", new[] { typeof(GlobalTargetInfo[]) });
                if (cast != null && !cast.IsAbstract)
                {
                    yield return cast;
                }
            }
        }

        // Vanilla Psycasts Expanded, if loaded: what tells its psycasts and charges them their psyfocus and neural heat.
        private static void BindVpe()
        {
            if (ModLister.GetActiveModWithIdentifier(VpePackageId, true) == null)
            {
                return;
            }
            psycastExtension = GenTypes.GetTypeInAnyAssembly("VanillaPsycastsExpanded.AbilityExtension_Psycast");
            psycastHediff = GenTypes.GetTypeInAnyAssembly("VanillaPsycastsExpanded.Hediff_PsycastAbilities");
            if (psycastExtension != null && psycastHediff != null)
            {
                psyfocusUsedByPawn = AccessTools.Method(psycastExtension, "GetPsyfocusUsedByPawn", new[] { typeof(Pawn) });
                entropyUsedByPawn = AccessTools.Method(psycastExtension, "GetEntropyUsedByPawn", new[] { typeof(Pawn) });
                useAbility = AccessTools.Method(psycastHediff, "UseAbility", new[] { typeof(float), typeof(float) });
            }
            if (psyfocusUsedByPawn?.ReturnType != typeof(float) || entropyUsedByPawn?.ReturnType != typeof(float) || useAbility == null)
            {
                psycastExtension = null;
                Log.Error("[Justiciar Valor] Vanilla Psycasts Expanded's psycast members are not where expected: the Warlockbane shield "
                    + "turns its psycasts aside only as far as they go by psychic sensitivity.");
            }
        }

        public static bool Prefix(object __instance, ref GlobalTargetInfo[] __0)
        {
            Pawn caster = pawnField.GetValue(__instance) as Pawn;
            WarlockbaneWard.Begin(caster);
            if (caster == null || !(defField.GetValue(__instance) is Def def) || !IsPsychic(def, __instance.GetType()))
            {
                return true;
            }
            WarlockbaneWard.Psychic();
            GlobalTargetInfo[] targets = __0;
            if (targets == null || targets.Length == 0)
            {
                return true;
            }
            // A psycast with several targets takes them in groups: Skip the pawn, then where it goes; Mass Chaos Skip one such
            // pair for each pawn in the area. A deflecting bearer takes its whole group out of the cast.
            int group = (int)targetCountField.GetValue(def);
            if (group < 1 || targets.Length % group != 0)
            {
                group = 1;
            }
            var kept = new List<GlobalTargetInfo>(targets.Length);
            for (int start = 0; start < targets.Length; start += group)
            {
                if (!DeflectsGroup(targets, start, group))
                {
                    for (int i = start; i < start + group; i++)
                    {
                        kept.Add(targets[i]);
                    }
                }
            }
            if (kept.Count == targets.Length)
            {
                return true;
            }
            if (kept.Count > 0)
            {
                __0 = kept.ToArray();
                return true;
            }
            // Turned aside by every target: the cast does not happen, but the caster pays for it as base.Cast would have.
            PayForCast(__instance, caster, def);
            return false;
        }

        public static Exception Finalizer(Exception __exception)
        {
            WarlockbaneWard.End();
            return __exception;
        }

        // Whether a bearer among targets[start, start + count) turns the cast aside.
        private static bool DeflectsGroup(GlobalTargetInfo[] targets, int start, int count)
        {
            for (int i = start; i < start + count; i++)
            {
                if (WarlockbaneWard.Turns(targets[i].Thing as Pawn))
                {
                    return true;
                }
            }
            return false;
        }

        // What base.Cast would have charged: the cooldown, and for a VPE psycast AbilityExtension_Psycast.Cast's psyfocus
        // and neural heat.
        private static void PayForCast(object ability, Pawn caster, Def def)
        {
            cooldownField.SetValue(ability, Find.TickManager.TicksGame + (int)cooldownForPawn.Invoke(ability, null));
            if (psycastExtension == null || def.modExtensions == null)
            {
                return;
            }
            Hediff psycasts = caster.health.hediffSet.hediffs.Find(h => psycastHediff.IsInstanceOfType(h));
            if (psycasts == null)
            {
                return;
            }
            foreach (DefModExtension extension in def.modExtensions)
            {
                if (psycastExtension.IsInstanceOfType(extension))
                {
                    var args = new object[] { caster };
                    useAbility.Invoke(psycasts, new[] { psyfocusUsedByPawn.Invoke(extension, args), entropyUsedByPawn.Invoke(extension, args) });
                }
            }
        }

        private static bool IsPsychic(Def def, Type abilityClass)
        {
            if (!psychic.TryGetValue(def, out bool result))
            {
                result = IsVpePsycast(def) || PsychicAttack.ReadsSensitivity(abilityClass, vefAbility) || PsychicAttack.NamesSensitivity(def);
                if (!result && def.modExtensions != null)
                {
                    foreach (DefModExtension extension in def.modExtensions)
                    {
                        result = result || PsychicAttack.NamesSensitivity(extension) || PsychicAttack.ReadsSensitivity(extension.GetType(), typeof(DefModExtension));
                    }
                }
                psychic[def] = result;
            }
            return result;
        }

        private static bool IsVpePsycast(Def def)
        {
            if (psycastExtension == null || def.modExtensions == null)
            {
                return false;
            }
            for (int i = 0; i < def.modExtensions.Count; i++)
            {
                if (psycastExtension.IsInstanceOfType(def.modExtensions[i]))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
