using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DHack.Shared
{
    /// <summary>
    /// What a piece or a recipe costs, and where it's made, as settings a server owner can change: "FineWood:12, BronzeNails:30" (an item's
    /// prefab name and its amount; for a recipe an optional third number, how many more each upgrade level takes, as "Iron:20:10"; for a
    /// piece an optional true/false, whether taking it down gives the item back, as PieceManager's settings write it), and a station as
    /// "piece_workbench" or "forge:2" (its prefab name and, for a recipe, the station level it needs; "none" for no station).
    /// Make the settings server-decided (ServerSettings.Bind or Add: the client pays when it builds, so it must charge the server's cost),
    /// apply them when the prefab is built, and again on SettingChanged:
    ///
    ///   JetSkiCost = Synced.Bind("3 - Building", "Cost", "FineWood:12, BronzeNails:30", "What a jet ski costs to build. " + Costs.Help);
    ///   JetSkiCost.SettingChanged += (s, e) => Costs.Apply(piece, JetSkiCost.Value, Logger);
    ///
    /// A cost with anything wrong in it (a misspelt name, no amount, an item twice) keeps the cost it had, whole: a typo never makes a piece
    /// free or cheaper. The log names what was wrong. Settings are keyed by your own names, never a display name (renaming a piece mustn't
    /// move its settings). Pieces already standing keep the cost they were built with until their area reloads (the game copies the
    /// prefab's list into each one), so a raised cost refunds more when an old piece is taken down: the game's own behaviour, worth knowing.
    /// Shared source (mods/Shared/Costs.cs), linked into the mods that use it: JetSkis. Claude Tools ships it as a template.
    /// </summary>
    internal static class Costs
    {
        /// <summary>For the setting's description (settings stay English).</summary>
        public const string Help = "Item prefab names and amounts, separated by commas, like FineWood:12, BronzeNails:30 (Claude Tools' `prefabs <text>` shows exact names). A cost with a mistake in it keeps the last good one.";

        public const string StationHelp = "The crafting station's prefab name (piece_workbench, forge, piece_stonecutter, piece_artisanstation, blackforge, piece_magetable), or none.";

        /// <summary>Read a cost (forRecipe: the third number is the upgrade amount; else a true/false for "gives it back"). Null when
        /// anything in it is wrong, or it's empty (keep the old cost); what was wrong goes in problems.</summary>
        public static Piece.Requirement[] Parse(string text, List<string> problems, bool forRecipe)
        {
            problems = problems ?? new List<string>();
            int before = problems.Count;
            var result = new List<Piece.Requirement>();
            foreach (string raw in (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                string[] bits = part.Split(':').Select(b => b.Trim()).ToArray();
                if (bits.Length < 2 || bits.Length > 3 || !Whole(bits[1], out int amount) || amount < 1)
                {
                    problems.Add($"\"{part}\" isn't Name:amount");
                    continue;
                }
                int perLevel = 0;
                bool recover = true;
                if (bits.Length == 3)
                {
                    bool ok = forRecipe ? Whole(bits[2], out perLevel) && perLevel >= 0 : bool.TryParse(bits[2], out recover);
                    if (!ok)
                    {
                        problems.Add(forRecipe ? $"\"{part}\": the upgrade amount isn't a number" : $"\"{part}\": the last part isn't true or false");
                        continue;
                    }
                }
                ItemDrop item = FindItem(bits[0]);
                if (item == null)
                {
                    problems.Add($"no item called \"{bits[0]}\"");
                    continue;
                }
                if (result.Any(r => r.m_resItem == item))
                {
                    problems.Add($"{bits[0]} is listed twice");
                    continue;
                }
                result.Add(new Piece.Requirement { m_resItem = item, m_amount = amount, m_amountPerLevel = perLevel, m_recover = recover });
            }
            if (result.Count == 0 && problems.Count == before) problems.Add("it's empty (nothing would be free: write at least one item)");
            return problems.Count == before ? result.ToArray() : null;
        }

        private static bool Whole(string text, out int value) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        /// <summary>A cost written back as text ("FineWood:12, BronzeNails:30"), for a setting's default or the log.</summary>
        public static string Write(IEnumerable<Piece.Requirement> cost, bool forRecipe) =>
            string.Join(", ", (cost ?? Enumerable.Empty<Piece.Requirement>()).Where(r => r?.m_resItem != null)
                .Select(r => r.m_resItem.name + ":" + r.m_amount
                    + (forRecipe ? (r.m_amountPerLevel > 0 ? ":" + r.m_amountPerLevel : "") : (r.m_recover ? "" : ":false"))).ToArray());

        /// <summary>An item by its prefab name (the scene knows every item, ObjectDB may not be up yet). Null if there's none.</summary>
        public static ItemDrop FindItem(string name)
        {
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            if (go == null && ObjectDB.instance != null) go = ObjectDB.instance.GetItemPrefab(name);
            return go != null ? go.GetComponent<ItemDrop>() : null;
        }

        /// <summary>A crafting station by its prefab name ("none" or empty: no station, found = true). found is false for a name that isn't one.</summary>
        public static CraftingStation FindStation(string name, out bool found)
        {
            name = (name ?? "").Trim();
            found = true;
            if (name.Length == 0 || name.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            CraftingStation station = go != null ? go.GetComponent<CraftingStation>() : null;
            found = station != null;
            return station;
        }

        /// <summary>Set a piece's cost from the text. False (and the cost left as it was) when it can't be read.</summary>
        public static bool Apply(Piece piece, string text, BepInEx.Logging.ManualLogSource log)
        {
            if (piece == null) return false;
            var problems = new List<string>();
            Piece.Requirement[] cost = Parse(text, problems, forRecipe: false);
            Report(log, piece.name, problems);
            if (cost == null) return false;
            piece.m_resources = cost;
            return true;
        }

        /// <summary>Set a recipe's cost from the text (with upgrade amounts). False (and the cost left as it was) when it can't be read.</summary>
        public static bool Apply(Recipe recipe, string text, BepInEx.Logging.ManualLogSource log)
        {
            if (recipe == null) return false;
            var problems = new List<string>();
            Piece.Requirement[] cost = Parse(text, problems, forRecipe: true);
            Report(log, recipe.name, problems);
            if (cost == null) return false;
            recipe.m_resources = cost;
            return true;
        }

        /// <summary>Set the station a piece needs nearby ("piece_workbench", "none"). False when the name isn't a station.</summary>
        public static bool ApplyStation(Piece piece, string text, BepInEx.Logging.ManualLogSource log)
        {
            if (piece == null) return false;
            CraftingStation station = FindStation((text ?? "").Split(':')[0], out bool found);
            if (!found) { log?.LogWarning($"{piece.name}: no crafting station called \"{text}\", it keeps its station"); return false; }
            piece.m_craftingStation = station;
            return true;
        }

        /// <summary>Set the station (and level, "forge:2") a recipe is made at. False when the name isn't a station.</summary>
        public static bool ApplyStation(Recipe recipe, string text, BepInEx.Logging.ManualLogSource log)
        {
            if (recipe == null) return false;
            string[] bits = (text ?? "").Split(':');
            CraftingStation station = FindStation(bits[0], out bool found);
            int level = 1;
            if (!found || (bits.Length > 1 && (!Whole(bits[1].Trim(), out level) || level < 1)))
            {
                log?.LogWarning($"{recipe.name}: \"{text}\" isn't a crafting station (and level), it keeps its station");
                return false;
            }
            recipe.m_craftingStation = station;
            recipe.m_minStationLevel = level;
            return true;
        }

        private static void Report(BepInEx.Logging.ManualLogSource log, string what, List<string> problems)
        {
            if (problems.Count > 0) log?.LogWarning($"{what}'s cost setting has a mistake, so it keeps its last cost: {string.Join("; ", problems.ToArray())}");
        }
    }
}
