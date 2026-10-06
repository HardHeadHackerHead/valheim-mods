using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// Our keys win over the game's: when one of this mod's keys is also bound to a game action (V is the game's auto-pickup toggle,
    /// X sit, C walk, Q auto-run, G the emote wheel...), the game action is unbound, saved in the game's own settings, and the player is
    /// told. They can bind it to another key in Settings, Controls. Each game action is unbound only once for a key: if the player binds
    /// it back to that key, it is left alone. Essential actions (moving, use, jump, attack, inventory, map, chat, menu, hotbar) are
    /// never touched. (The same file is in each mod that has keys; each keeps its own record.)
    /// </summary>
    internal static class GameKeys
    {
        private static readonly HashSet<string> Essential = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Forward", "Backward", "Left", "Right", "Use", "Jump", "Inventory", "Tab", "Escape", "Map", "Chat", "Run", "Crouch",
            "Attack", "SecondaryAttack", "Block", "AltPlace", "LShift", "Console", "BuildMenu", "Remove", "MapZoomIn", "MapZoomOut",
            "Hotbar1", "Hotbar2", "Hotbar3", "Hotbar4", "Hotbar5", "Hotbar6", "Hotbar7", "Hotbar8", "TabRight", "ChatUp", "ChatDown",
        };

        /// <summary>The input-system path for a key ("&lt;Keyboard&gt;/v"), or null for keys we do not map.</summary>
        private static string PathOf(KeyCode key)
        {
            if (key >= KeyCode.A && key <= KeyCode.Z) return "<Keyboard>/" + key.ToString().ToLowerInvariant();
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return "<Keyboard>/" + (key - KeyCode.Alpha0);
            if (key >= KeyCode.F1 && key <= KeyCode.F12) return "<Keyboard>/" + key.ToString().ToLowerInvariant();
            switch (key)
            {
                case KeyCode.LeftAlt: return "<Keyboard>/leftAlt";
                case KeyCode.RightAlt: return "<Keyboard>/rightAlt";
                case KeyCode.LeftControl: return "<Keyboard>/leftCtrl";
                case KeyCode.LeftShift: return "<Keyboard>/leftShift";
                case KeyCode.Delete: return "<Keyboard>/delete";
                case KeyCode.Insert: return "<Keyboard>/insert";
                case KeyCode.Home: return "<Keyboard>/home";
                case KeyCode.End: return "<Keyboard>/end";
                case KeyCode.BackQuote: return "<Keyboard>/backquote";
                case KeyCode.Mouse3: return "<Mouse>/backButton";
                case KeyCode.Mouse4: return "<Mouse>/forwardButton";
                default: return null;
            }
        }

        /// <summary>
        /// Free our keys from the game. <paramref name="keys"/>: each key with a short description of what we use it for. Returns the lines
        /// to tell the player (empty when nothing changed).
        /// </summary>
        public static List<string> Free(string owner, IEnumerable<KeyValuePair<KeyCode, string>> keys)
        {
            var told = new List<string>();
            ZInput input = ZInput.instance;
            if (input == null) return told;
            var buttons = AccessTools.Field(typeof(ZInput), "m_buttons")?.GetValue(input) as Dictionary<string, ZInput.ButtonDef>;
            if (buttons == null) return told;

            string record = "DHack.FreedGameKeys." + owner;
            var done = new HashSet<string>(PlayerPrefs.GetString(record, "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
            bool changed = false;
            foreach (var kv in keys)
            {
                string path = PathOf(kv.Key);
                if (path == null) continue;
                foreach (ZInput.ButtonDef def in buttons.Values.ToList())
                {
                    if (def == null || !def.Rebindable || def.Source == ZInput.InputSource.Gamepad) continue;
                    if (!string.Equals(def.GetActionPath(), path, StringComparison.OrdinalIgnoreCase)) continue;
                    if (Essential.Contains(def.Name))
                    {
                        Debug.LogWarning($"[{owner}] {kv.Key} ({kv.Value}) is also the game's {def.Name} key; that one is left as it is. Choose another key in the mod's settings.");
                        continue;
                    }
                    string mark = def.Name + ":" + path.ToLowerInvariant();
                    if (done.Contains(mark)) continue;   // unbound once already: the player has bound it back to this key on purpose
                    def.Rebind("");
                    done.Add(mark);
                    changed = true;
                    told.Add($"{owner}: {kv.Key} is used for {kv.Value}, so the game's \"{Nice(def.Name)}\" key was unbound (bind it to another key in Settings, Controls)");
                }
            }
            if (changed)
            {
                input.Save();
                PlayerPrefs.SetString(record, string.Join("|", done.ToArray()));
                PlayerPrefs.Save();
                foreach (string line in told) Debug.Log("[" + owner + "] " + line);
            }
            return told;
        }

        private static string Nice(string name)
        {
            switch (name)
            {
                case "AutoPickup": return "Auto pickup";
                case "ToggleWalk": return "Walk";
                case "AutoRun": return "Auto run";
                case "OpenRadial": return "Radial menu";
                case "TabLeft": return "Previous tab";
                case "OpenEmote": return "Emotes";
                case "GP": return "Forsaken power";
                default: return name;
            }
        }
    }
}
