using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using RimWorld;
using Verse;
using Verse.AI;

namespace JusticiarValor
{
    // What the Warlockbane shield turns aside: a psychic attack, an ability or item that is psychic, cast on its bearer by
    // someone attacking it. A cast is psychic when it is a psycast, or when it goes by psychic sensitivity: as its def's
    // data says (a psychic effect, a duration scaled by psychic sensitivity, targets that must be psychically sensitive...)
    // or as its own code shows by reading the stat. That is found out once per def, without any list, so the abilities of
    // other mods are told apart as the game's own are. Casts whose victims are not their targets, and that read a bearer's
    // psychic sensitivity as they resolve, are psychic by that very reading: WarlockbaneWard.
    public static class PsychicAttack
    {
        private static readonly Dictionary<AbilityDef, bool> abilities = new Dictionary<AbilityDef, bool>();
        private static readonly Dictionary<(Type, ThingDef), bool> items = new Dictionary<(Type, ThingDef), bool>();
        private static readonly Dictionary<Type, bool> typesReading = new Dictionary<Type, bool>();

        // Whether the caster's cast attacks the bearer: the caster is hostile to it, and not only because the bearer is out
        // of its mind. A berserk colonist is hostile to everyone, yet a colonist calming it down or stunning it attacks no one.
        public static bool Attacks(Pawn caster, Pawn bearer)
        {
            if (caster == null || bearer == null || caster == bearer || !caster.HostileTo(bearer))
            {
                return false;
            }
            return !bearer.InMentalState || caster.Faction == null || bearer.Faction == null || caster.Faction.HostileTo(bearer.Faction);
        }

        // The game's abilities: psycasts, and those whose effects go by psychic sensitivity.
        public static bool IsPsychic(AbilityDef def)
        {
            if (def == null)
            {
                return false;
            }
            if (!abilities.TryGetValue(def, out bool psychic))
            {
                abilities[def] = psychic = Decide(def);
            }
            return psychic;
        }

        private static bool Decide(AbilityDef def)
        {
            if (def.IsPsycast || TargetsPsychicOnly(def.verbProperties?.targetParams) || ReadsSensitivity(def.abilityClass, typeof(Ability)))
            {
                return true;
            }
            if (def.comps != null)
            {
                foreach (AbilityCompProperties comp in def.comps)
                {
                    if ((comp is CompProperties_AbilityEffect effect && effect.psychic) || NamesSensitivity(comp)
                        || ReadsSensitivity(comp.compClass, typeof(AbilityComp)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // An item working its CompTargetEffects on its target (Verb_CastTargetEffect): the psychic lances, which will not aim
        // at the psychically deaf. Psychic when the verb or one of the item's target effects reads psychic sensitivity, or the
        // verb only takes psychically sensitive targets. The biomutation lance does neither.
        public static bool IsPsychic(Verb verb)
        {
            ThingWithComps item = verb?.EquipmentSource;
            if (item == null)
            {
                return false;
            }
            var key = (verb.GetType(), item.def);
            if (!items.TryGetValue(key, out bool psychic))
            {
                psychic = ReadsSensitivity(verb.GetType(), typeof(Verb_CastTargetEffect)) || TargetsPsychicOnly(verb.verbProps?.targetParams);
                foreach (CompTargetEffect effect in item.GetComps<CompTargetEffect>())
                {
                    psychic = psychic || ReadsSensitivity(effect.GetType(), typeof(CompTargetEffect)) || NamesSensitivity(effect.props);
                }
                items[key] = psychic;
            }
            return psychic;
        }

        internal static bool TargetsPsychicOnly(TargetingParameters parms) => parms != null && parms.onlyTargetPsychicSensitive;

        // Whether data (comp properties, a def, a def extension) names psychic sensitivity in a field: a stat (a duration
        // multiplier), a stat modifier (stat factors and offsets), targeting parameters taking only the psychically sensitive,
        // or a list of these. The defs it refers to are not followed.
        internal static bool NamesSensitivity(object data)
        {
            if (data == null)
            {
                return false;
            }
            const BindingFlags declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (Type type = data.GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(declared))
                {
                    Type fieldType = field.FieldType;
                    if (typeof(StatDef).IsAssignableFrom(fieldType) || fieldType == typeof(StatModifier) || typeof(TargetingParameters).IsAssignableFrom(fieldType)
                        || typeof(VerbProperties).IsAssignableFrom(fieldType))
                    {
                        if (IsSensitivity(field.GetValue(data)))
                        {
                            return true;
                        }
                    }
                    else if (typeof(IEnumerable<StatDef>).IsAssignableFrom(fieldType) || typeof(IEnumerable<StatModifier>).IsAssignableFrom(fieldType)
                        || typeof(IEnumerable<TargetingParameters>).IsAssignableFrom(fieldType))
                    {
                        if (field.GetValue(data) is IEnumerable values)
                        {
                            foreach (object value in values)
                            {
                                if (IsSensitivity(value))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }

        private static bool IsSensitivity(object value)
        {
            switch (value)
            {
                case StatDef stat:
                    return stat == StatDefOf.PsychicSensitivity;
                case StatModifier modifier:
                    return modifier.stat == StatDefOf.PsychicSensitivity;
                case TargetingParameters parms:
                    return parms.onlyTargetPsychicSensitive;
                case VerbProperties verb:
                    return TargetsPsychicOnly(verb.targetParams);
                default:
                    return false;
            }
        }

        // Whether a type's own code reads psychic sensitivity: StatDefOf.PsychicSensitivity, the pawn's cached
        // Pawn_PsychicEntropyTracker.PsychicSensitivity, or the stat looked up by its name. Its base types are read too, down
        // to root (left out, with what it derives from), and so are its nested types: the closures and iterators the compiler
        // makes of its lambdas and yields.
        internal static bool ReadsSensitivity(Type type, Type root)
        {
            for (Type t = type; t != null && t != root && t != typeof(object); t = t.BaseType)
            {
                if (TypeReadsSensitivity(t))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TypeReadsSensitivity(Type type)
        {
            if (typesReading.TryGetValue(type, out bool reads))
            {
                return reads;
            }
            typesReading[type] = false;
            const BindingFlags declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                foreach (MethodInfo method in type.GetMethods(declared))
                {
                    reads = reads || MethodReadsSensitivity(method);
                }
                foreach (ConstructorInfo constructor in type.GetConstructors(declared))
                {
                    reads = reads || MethodReadsSensitivity(constructor);
                }
                foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    reads = reads || TypeReadsSensitivity(nested);
                }
            }
            catch (Exception)
            {
                // A type whose members cannot be loaded (an optional mod missing) shows nothing more.
            }
            typesReading[type] = reads;
            return reads;
        }

        private static readonly OpCode[] oneByteOpCodes = new OpCode[256], twoByteOpCodes = new OpCode[256];
        private static readonly bool[] oneByteKnown = new bool[256], twoByteKnown = new bool[256];

        static PsychicAttack()
        {
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is OpCode op)
                {
                    int value = (ushort)op.Value;
                    if (op.Size == 1)
                    {
                        oneByteOpCodes[value] = op;
                        oneByteKnown[value] = true;
                    }
                    else
                    {
                        twoByteOpCodes[value & 0xFF] = op;
                        twoByteKnown[value & 0xFF] = true;
                    }
                }
            }
        }

        // Walks the method's IL, resolving only the tokens that could name psychic sensitivity: static field loads, calls
        // and string literals.
        private static bool MethodReadsSensitivity(MethodBase method)
        {
            byte[] il;
            try
            {
                il = method.GetMethodBody()?.GetILAsByteArray();
            }
            catch (Exception)
            {
                return false;
            }
            if (il == null)
            {
                return false;
            }
            Module module = method.Module;
            Type[] typeArguments = method.DeclaringType != null && method.DeclaringType.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
            Type[] methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            int at = 0;
            while (at < il.Length)
            {
                OpCode op;
                if (il[at] == 0xFE)
                {
                    if (at + 1 >= il.Length || !twoByteKnown[il[at + 1]])
                    {
                        return false;
                    }
                    op = twoByteOpCodes[il[at + 1]];
                    at += 2;
                }
                else
                {
                    if (!oneByteKnown[il[at]])
                    {
                        return false;
                    }
                    op = oneByteOpCodes[il[at]];
                    at++;
                }
                int size = OperandSize(op, il, at);
                if (at + size > il.Length)
                {
                    return false;
                }
                if ((op == OpCodes.Ldsfld || op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Ldstr)
                    && TokenNamesSensitivity(module, op, BitConverter.ToInt32(il, at), typeArguments, methodArguments))
                {
                    return true;
                }
                at += size;
            }
            return false;
        }

        private static int OperandSize(OpCode op, byte[] il, int at)
        {
            switch (op.OperandType)
            {
                case OperandType.InlineNone:
                    return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    return 1;
                case OperandType.InlineVar:
                    return 2;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 8;
                case OperandType.InlineSwitch:
                    return at + 4 <= il.Length ? 4 + 4 * BitConverter.ToInt32(il, at) : 4;
                default:
                    return 4;
            }
        }

        private static bool TokenNamesSensitivity(Module module, OpCode op, int token, Type[] typeArguments, Type[] methodArguments)
        {
            try
            {
                if (op == OpCodes.Ldsfld)
                {
                    FieldInfo field = module.ResolveField(token, typeArguments, methodArguments);
                    return field.DeclaringType == typeof(StatDefOf) && field.Name == nameof(StatDefOf.PsychicSensitivity);
                }
                if (op == OpCodes.Ldstr)
                {
                    return module.ResolveString(token) == nameof(StatDefOf.PsychicSensitivity);
                }
                MethodBase callee = module.ResolveMethod(token, typeArguments, methodArguments);
                return callee.DeclaringType == typeof(Pawn_PsychicEntropyTracker) && callee.Name == "get_" + nameof(Pawn_PsychicEntropyTracker.PsychicSensitivity);
            }
            catch (Exception)
            {
                // A member of an assembly that is not loaded.
                return false;
            }
        }
    }
}
