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
            var told = new List<Said>();
            ZInput input = ZInput.instance;
            if (input == null) return new List<string>();
            var buttons = AccessTools.Field(typeof(ZInput), "m_buttons")?.GetValue(input) as Dictionary<string, ZInput.ButtonDef>;
            if (buttons == null) return new List<string>();

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
                    told.Add(new Said("buildorders_key_unbound", owner, kv.Key, kv.Value, Nice(def.Name)));
                }
            }
            if (changed)
            {
                input.Save();
                PlayerPrefs.SetString(record, string.Join("|", done.ToArray()));
                PlayerPrefs.Save();
                foreach (Said line in told) Debug.Log("[" + owner + "] " + line.English);
            }
            return told.Select(line => line.Shown).ToList();
        }

        /// <summary>
        /// Give back the game's actions this mod unbound for a key that none of <paramref name="inUse"/> is any more (the player chose another
        /// key, or the mod's default changed): each is bound to its key again if it is still unbound. Returns the lines to tell the player.
        /// </summary>
        public static List<string> GiveBack(string owner, IEnumerable<KeyCode> inUse)
        {
            var told = new List<Said>();
            ZInput input = ZInput.instance;
            if (input == null) return new List<string>();
            var buttons = AccessTools.Field(typeof(ZInput), "m_buttons")?.GetValue(input) as Dictionary<string, ZInput.ButtonDef>;
            if (buttons == null) return new List<string>();
            string record = "DHack.FreedGameKeys." + owner;
            var done = new HashSet<string>(PlayerPrefs.GetString(record, "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
            var used = new HashSet<string>(inUse.Select(PathOf).Where(p => p != null).Select(p => p.ToLowerInvariant()));
            bool changed = false;
            foreach (string mark in done.ToList())
            {
                int colon = mark.IndexOf(':');
                if (colon <= 0) continue;
                string action = mark.Substring(0, colon), path = mark.Substring(colon + 1);
                if (used.Contains(path)) continue;
                done.Remove(mark);
                changed = true;
                if (!buttons.TryGetValue(action, out ZInput.ButtonDef def) || def == null || !string.IsNullOrEmpty(def.GetActionPath())) continue; // bound to something since: the player's choice
                def.Rebind(path);
                told.Add(new Said("buildorders_key_back", owner, Nice(action), path.Substring(path.IndexOf('/') + 1).ToUpperInvariant()));
            }
            if (changed)
            {
                input.Save();
                PlayerPrefs.SetString(record, string.Join("|", done.ToArray()));
                PlayerPrefs.Save();
                foreach (Said line in told) Debug.Log("[" + owner + "] " + line.English);
            }
            return told.Select(line => line.Shown).ToList();
        }

        /// <summary>A game action's name, as Settings, Controls calls it (a key: the game words it in the player's language).</summary>
        private static Said Nice(string name)
        {
            switch (name)
            {
                case "AutoPickup": return new Said("buildorders_key_autopickup");
                case "ToggleWalk": return new Said("buildorders_key_walk");
                case "AutoRun": return new Said("buildorders_key_autorun");
                case "OpenRadial": return new Said("buildorders_key_radial");
                case "TabLeft": return new Said("buildorders_key_tableft");
                case "OpenEmote": return new Said("buildorders_key_emotes");
                case "GP": return new Said("buildorders_key_power");
                default: return new Said(name);
            }
        }
    }
}
