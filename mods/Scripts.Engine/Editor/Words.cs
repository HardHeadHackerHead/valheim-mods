using System;
using System.Collections.Generic;
using System.Linq;
using Scripts.Blocks;

namespace Scripts.Editor
{
    /// <summary>
    /// The editor's words. Every word is written in the code as its English text next to its key (L("scripts_run", "Run")); a translation
    /// of the same key, given to the game by the mod (translations/*.json), wins when there is one, so a language file never has to be
    /// complete. tools/ lists every key from the code.
    /// </summary>
    internal static class Words
    {
        /// <summary>The words for a key in the player's language, or the English text.</summary>
        internal static string L(string key, string english)
        {
            string translated = Translated(key);
            return translated ?? english;
        }

        /// <summary>The game's translation of a key, or null when there is none (the game answers a missing key with the key in brackets).</summary>
        private static string Translated(string key)
        {
            if (Localization.instance == null) return null;
            string t = Localization.instance.Localize("$" + key);
            return string.IsNullOrEmpty(t) || (t.Length > 2 && t[0] == '[' && t[t.Length - 1] == ']') ? null : t;
        }

        internal static string L(string key, string english, params object[] args)
        {
            string text = L(key, english);
            try { return string.Format(text, args); } catch (FormatException) { return string.Format(english, args); }
        }

        /// <summary>A block's label, in the player's language if there is a translation with the same slots.</summary>
        internal static List<Part> Parts(BlockDef def)
        {
            string text = Translated("scripts_block_" + def.Id);
            if (text != null)
            {
                List<Part> parsed = BlockDef.ParseLabel(text);
                var wanted = new HashSet<string>(def.Parts.Where(p => p.Slot != null).Select(p => p.Slot.Key));
                var got = new HashSet<string>(parsed.Where(p => p.Slot != null).Select(p => p.Slot.Key));
                if (wanted.SetEquals(got))
                {
                    // a translation brings words, not behaviour: the slots keep what the definition says
                    foreach (Part part in parsed)
                        if (part.Slot != null) part.Slot = def.SlotOf(part.Slot.Key);
                    return parsed;
                }
            }
            return def.Parts;
        }

        /// <summary>What a power means, in words a player understands.</summary>
        internal static string Power(string power)
        {
            switch (power)
            {
                case "messages": return L("scripts_power_messages", "show messages and play sounds");
                case "store": return L("scripts_power_store", "remember small things between sessions");
                case "items": return L("scripts_power_items", "give players small amounts of items");
                case "takeItems": return L("scripts_power_takeItems", "take items from players");
                case "spawn": return L("scripts_power_spawn", "spawn creatures and drop items near players");
                case "combat": return L("scripts_power_combat", "change health and damage in fights");
                case "weather": return L("scripts_power_weather", "change the weather and the time of day");
                case "world": return L("scripts_power_world", "place pieces in the world");
                case "teleport": return L("scripts_power_teleport", "move players");
                case "proxy": return L("scripts_power_proxy", "drive the Terminal's Proxy character");
                case "effects": return L("scripts_power_effects", "play visual and sound effects (lightning, fire, thunder)");
                case "net": return L("scripts_power_net", "send messages to a web address");
                default: return power;
            }
        }

        internal static bool Risky(string power) => power == "takeItems" || power == "world" || power == "teleport" || power == "net";
    }
}
