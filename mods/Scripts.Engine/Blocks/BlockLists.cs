using System;
using System.Collections.Generic;
using System.Linq;

namespace Scripts.Blocks
{
    /// <summary>
    /// Where the lists the host owns come from. The block dropdowns read them here and nowhere else, so a block can never offer a name the
    /// host would refuse. Each property is the one place that decides: the host's data when the host has it, else the small list below.
    /// </summary>
    internal static class HostData
    {
        private static string[] Sorted(IEnumerable<string> names) => names.OrderBy(n => n, StringComparer.Ordinal).ToArray();

        internal static string[] Effects => Sorted(Scripts.Lists.StatusEffects.Keys);
        internal static string[] VisualEffects => Sorted(Scripts.Lists.VisualEffects);
        internal static string[] Sounds => Sorted(Scripts.Lists.Sounds);
        internal static string[] Pieces => Sorted(Scripts.Lists.Pieces);
        /// <summary>The random events a script may start: the host sets this from the owner's setting; these are the defaults.</summary>
        internal static System.Func<string[]> EventsProvider;
        internal static string[] Events => EventsProvider != null ? EventsProvider() : new[] { "army_eikthyr", "army_theelder", "foresttrolls" };
    }

    /// <summary>The dropdown lists. Items and creatures come from the same data the host checks (Scripts.Lists), so a block can't name one the host would refuse.</summary>
    internal static class BlockLists
    {
        private static readonly string[] Skills = { "Swords", "Knives", "Clubs", "Polearms", "Spears", "Blocking", "Axes", "Bows", "ElementalMagic", "BloodMagic", "Unarmed", "Pickaxes", "WoodCutting", "Crossbows", "Jump", "Sneak", "Run", "Swim", "Fishing", "Ride" };

        internal static string[] Get(string name, Program program = null)
        {
            switch (name)
            {
                case "items": return Scripts.Lists.Allow.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
                case "creatures": return Scripts.Lists.Creatures.OrderBy(k => k, StringComparer.Ordinal).ToArray();
                case "creaturesAny":
                    return new[] { "", "Boar", "Neck", "Greyling", "Deer", "Greydwarf", "Greydwarf_Elite", "Skeleton", "Draugr", "Blob", "Wolf", "Fenring", "Lox", "Troll", "Surtling", "Serpent", "Leech", "Wraith", "Abomination", "Fuling", "Deathsquito", "Hen", "Chicken", "Crow", "Seagal" };
                case "weathers": return new[] { "clear", "rain", "storm", "mist", "snow" };
                case "times": return new[] { "morning", "noon", "evening", "midnight" };
                case "units": return new[] { "seconds", "minutes" };
                case "mathops": return new[] { "+", "-", "x", "/", "mod" };
                case "cmp": return new[] { "=", "!=", "<", ">", "<=", ">=" };
                case "textops": return new[] { "is", "contains", "starts with" };
                case "var": return program != null ? program.Vars.ToArray() : new string[0];
                case "lists": return program != null ? program.Vars.ToArray() : new string[0];
                case "procedures": return program != null ? program.Procedures().ToArray() : new string[0];
                case "params": return program != null ? program.Params().ToArray() : new string[0];
                case "locals": return program != null ? program.Locals().ToArray() : new string[0];
                case "signals": return program != null ? program.Signals().ToArray() : new string[0];
                case "effectsAll": return HostData.Effects;
                case "visualEffects": return HostData.VisualEffects;
                case "sounds": return HostData.Sounds;
                case "pieces": return HostData.Pieces;
                case "events": return HostData.Events;
                case "slots": return new[] { "right", "left", "chest", "legs", "head" };
                case "skills": return Skills;
                case "factions": return new[] { "players", "wild", "boss", "tamed" };
                case "mathfns": return new[] { "absolute value", "round down", "round up", "round", "square root", "sine", "cosine", "tangent" };
                case "mathfns2": return new[] { "smaller", "larger", "power", "angle" };
                case "textfns": return new[] { "uppercase", "lowercase", "trim" };
                case "launchdirs": return new[] { "up", "forward", "backward" };
                case "itemfields": return new[] { "name", "count", "prefab", "quality" };
                case "seconds": return new[] { "seconds" };
                default: return new string[0];
            }
        }

        internal static bool Has(string list, string value, Program program) => Get(list, program).Contains(value);

        private static readonly HashSet<string> Nice = new HashSet<string> { "items", "creatures", "creaturesAny", "effectsAll", "visualEffects", "sounds", "pieces", "events", "skills" };

        /// <summary>The words the editor shows for a choice: CamelCase and underscores split into words ("Greydwarf_Elite" is "Greydwarf Elite").</summary>
        internal static string Show(string list, string value)
        {
            if (value.Length == 0) return list == "creaturesAny" ? "anything" : "";
            if (!Nice.Contains(list)) return value;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '_') { sb.Append(' '); continue; }
                if (i > 0 && char.IsUpper(c) && char.IsLower(value[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
