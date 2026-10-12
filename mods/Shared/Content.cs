using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DHack.Shared
{
    /// <summary>
    /// Puts a mod's items, recipes, smelter/oven/fermenter conversions, creature drops and trader goods where the game looks for them, by
    /// name, safely again and again. Register from a ZNetScene.Awake postfix, an ObjectDB.Awake postfix, an ObjectDB.CopyOtherDB postfix
    /// and the plugin's Awake (a reload in a world): they run in either order and more than once, and every call here replaces this mod's
    /// earlier entry of the same name instead of adding another.
    ///
    ///   Content.Items(myItemPrefabs);                                          // ZNetScene, ObjectDB, their status effects
    ///   Recipe r = Content.Recipe(cigar, table, level: 2, amount: 3, cost: built, costText: CigarCost.Value, log: Logger);
    ///   Synced.NeedsServer(r);                                                 // (ServerSettings.cs: not craftable on a server without the mod)
    ///   Content.Conversion(scene.GetPrefab("smelter"), ore, ingot);            // also CookingStation (cookSeconds) and Fermenter (makes)
    ///   Content.Drop(scene.GetPrefab("Greydwarf"), seeds, chance: 0.1f, min: 1, max: 2);
    ///   Content.Sell(_harmony, "Haldor", cigar, price: 120, globalKey: "defeated_eikthyr");
    ///   OnDestroy: Content.RemoveAll();                                       // only entries that still point at this copy's objects
    ///
    /// Recipe makes its Recipe once (named Recipe_prefab or Recipe_prefab_way) and on later calls only puts that same one back: change its
    /// cost and station afterwards with Costs.Apply / Costs.ApplyStation (a cost setting's SettingChanged). A mod that keeps its items
    /// across a reload with ItemReload.cs registers them with ItemReload.Replace, calls Content.ItemExtras for the rest, and unloads with
    /// RemoveAll(keepItems: ItemReload.KeepOnUnload).
    ///
    /// What it guards against (each cost players something in one of our mods or a popular library):
    ///   - A prefab under an inactive holder never runs ItemDrop.Awake, which is what sets m_dropPrefab: without it the item's copies save
    ///     with no name and are gone on load. Items sets it.
    ///   - ObjectDB.UpdateRegisters adds every item to a Dictionary: two items of one name, or a destroyed one, and it throws (and every
    ///     mod registering after it fails). Items replace by name and sweep destroyed entries.
    ///   - Lists on the game's prefabs (m_conversion, m_drops) last the whole session: adding in an Awake postfix adds again at every world
    ///     load and reload (a smelter taking your ore three times). Removed by name before adding, never by reference.
    ///   - The game knows a recipe by its item's m_shared.m_name (known recipes, repairs and upgrades use the first recipe for that name:
    ///     ObjectDB.GetRecipe), not by the Recipe's own name; a station by CraftingStation.m_name; a repair needs min(station level, 4)
    ///     at least m_minStationLevel.
    ///   - Unloading removes only what still points at this copy's objects, never a newer copy's or another mod's.
    ///   - An item with m_value above 0 sells to the trader: one that is cheap to make becomes a coin machine.
    ///   - Recipes and status effects added to ObjectDB keep a mod at the "menu" reload level (modcheck).
    /// Needs Costs.cs beside it (for costText). Shared source (mods/Shared/Content.cs), linked into the mods that use it: CigarSmoking and
    /// SkalTavern (items and recipes; Conversion, Drop and Sell aren't used by one of our mods yet). Claude Tools ships it as a template.
    /// </summary>
    internal static class Content
    {
        private static readonly FieldInfo NamedField = AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");
        private static readonly MethodInfo UpdateRegisters = AccessTools.Method(typeof(ObjectDB), "UpdateRegisters");

        private static readonly List<GameObject> OurItems = new List<GameObject>();
        private static readonly List<StatusEffect> OurEffects = new List<StatusEffect>();
        private static readonly Dictionary<string, Recipe> OurRecipes = new Dictionary<string, Recipe>();
        private static readonly List<(GameObject Station, string From, string To)> OurConversions = new List<(GameObject, string, string)>();
        private static readonly List<(GameObject Creature, string Item)> OurDrops = new List<(GameObject, string)>();
        private static readonly List<Sale> OurSales = new List<Sale>();

        private static Dictionary<int, GameObject> Named(ZNetScene scene) => (Dictionary<int, GameObject>)NamedField.GetValue(scene);

        /// <summary>A reference that was set but whose object is destroyed (an unloaded copy's), as against one never set.</summary>
        private static bool Destroyed(Object o) => !ReferenceEquals(o, null) && o == null;

        // ---- items ----------------------------------------------------------------------------------------------

        public static void Item(GameObject prefab, ObjectDB db = null) => Items(new[] { prefab }, db);

        /// <summary>
        /// Registers item prefabs: m_dropPrefab set, into ZNetScene (dropped and saved ones load by name) and ObjectDB (bags, crafting),
        /// replacing any other entry of the same name, and the status effects they use into ObjectDB. Either may be missing yet (registers
        /// in what's there). db: the ObjectDB to add to (null: ObjectDB.instance, which a CopyOtherDB postfix's own is).
        /// </summary>
        public static void Items(IEnumerable<GameObject> prefabs, ObjectDB db = null)
        {
            db = db != null ? db : ObjectDB.instance;
            ZNetScene scene = ZNetScene.instance;
            bool changed = false;
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null || prefab.GetComponent<ItemDrop>() == null) continue;
                if (scene != null)
                {
                    if (!scene.m_prefabs.Contains(prefab))
                    {
                        scene.m_prefabs.RemoveAll(p => p == null || p.name == prefab.name);   // (an older copy's, a destroyed one)
                        scene.m_prefabs.Add(prefab);
                    }
                    Named(scene)[prefab.name.GetStableHashCode()] = prefab;
                }
                if (db != null && !db.m_items.Contains(prefab))
                {
                    db.m_items.RemoveAll(i => i == null || i.name == prefab.name);
                    db.m_items.Add(prefab);
                    changed = true;
                }
                if (!OurItems.Contains(prefab)) OurItems.Add(prefab);
            }
            ItemExtras(prefabs, db);
            if (changed) UpdateRegisters?.Invoke(db, null);
        }

        /// <summary>The part of Items that isn't ZNetScene and ObjectDB.m_items: m_dropPrefab, and the item's status effects in ObjectDB.
        /// For a mod that registers its items with ItemReload.Replace.</summary>
        public static void ItemExtras(IEnumerable<GameObject> prefabs, ObjectDB db = null)
        {
            db = db != null ? db : ObjectDB.instance;
            foreach (GameObject prefab in prefabs)
            {
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                drop.m_itemData.m_dropPrefab = prefab;   // (ItemDrop.Awake sets it, which a prefab under an inactive holder never runs)
                if (db == null) continue;
                ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
                foreach (StatusEffect effect in new[] { s.m_consumeStatusEffect, s.m_equipStatusEffect, s.m_attackStatusEffect, s.m_setStatusEffect })
                {
                    if (effect == null) continue;
                    if (!db.m_StatusEffects.Contains(effect))
                    {
                        db.m_StatusEffects.RemoveAll(e => e == null || e.name == effect.name);
                        db.m_StatusEffects.Add(effect);
                    }
                    if (!OurEffects.Contains(effect)) OurEffects.Add(effect);
                }
            }
        }

        /// <summary>Takes item prefabs (and the status effects they brought) out of ZNetScene and ObjectDB, where they're still these.
        /// The prefabs themselves are the mod's to destroy.</summary>
        public static void RemoveItems(IEnumerable<GameObject> prefabs)
        {
            ZNetScene scene = ZNetScene.instance;
            ObjectDB db = ObjectDB.instance;
            var gone = new HashSet<GameObject>(prefabs.Where(p => !ReferenceEquals(p, null)));
            foreach (GameObject prefab in gone)
            {
                OurItems.Remove(prefab);
                if (scene != null) scene.m_prefabs.Remove(prefab);
                if (prefab == null) continue;   // (destroyed already: its name can't be read)
                if (scene != null)
                {
                    Dictionary<int, GameObject> named = Named(scene);
                    int hash = prefab.name.GetStableHashCode();
                    if (named.TryGetValue(hash, out GameObject current) && current == prefab) named.Remove(hash);   // never another copy's entry
                }
                ItemDrop drop = prefab.GetComponent<ItemDrop>();
                if (drop != null && db != null)
                {
                    ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
                    foreach (StatusEffect effect in new[] { s.m_consumeStatusEffect, s.m_equipStatusEffect, s.m_attackStatusEffect, s.m_setStatusEffect })
                        if (effect != null && OurEffects.Remove(effect)) db.m_StatusEffects.Remove(effect);
                }
            }
            if (db != null)
            {
                db.m_items.RemoveAll(i => i == null || gone.Contains(i));
                db.m_StatusEffects.RemoveAll(e => e == null);
                UpdateRegisters?.Invoke(db, null);
            }
        }

        // ---- recipes --------------------------------------------------------------------------------------------

        /// <summary>
        /// The recipe that makes amount of item at station (null: by hand) from level: made the first time (named Recipe_item or
        /// Recipe_item_way, one per way to make it), then put back into ObjectDB.m_recipes by name on every call. Its cost is costText
        /// (Costs.cs: "Honey:4, Raspberry:2", the server's setting) when that can be read, else cost. upgradeOnly: can be upgraded there,
        /// not made (m_noCraftOnlyUpgrade); repairStation: where it's repaired and upgraded when there's no station. Returns the recipe (pass
        /// it to ServerSettings.NeedsServer), or null without an item. Later changes go through Costs.Apply on the returned recipe.
        /// </summary>
        public static Recipe Recipe(GameObject item, CraftingStation station, int level = 1, int amount = 1, Piece.Requirement[] cost = null,
                                    string costText = null, ManualLogSource log = null, string way = null, bool upgradeOnly = false,
                                    CraftingStation repairStation = null, ObjectDB db = null)
        {
            ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
            if (drop == null) return null;
            string name = "Recipe_" + item.name + (string.IsNullOrEmpty(way) ? "" : "_" + way);
            if (!OurRecipes.TryGetValue(name, out Recipe recipe) || recipe == null)
            {
                recipe = ScriptableObject.CreateInstance<Recipe>();
                recipe.name = name;
                recipe.m_item = drop;
                recipe.m_amount = Mathf.Max(1, amount);
                recipe.m_craftingStation = station;
                recipe.m_repairStation = repairStation;
                recipe.m_minStationLevel = Mathf.Max(1, level);
                recipe.m_noCraftOnlyUpgrade = upgradeOnly;
                recipe.m_resources = cost ?? new Piece.Requirement[0];
                if (costText != null) Costs.Apply(recipe, costText, log);   // (keeps cost when the text has a mistake)
                OurRecipes[name] = recipe;
            }
            db = db != null ? db : ObjectDB.instance;
            if (db != null && !db.m_recipes.Contains(recipe))
            {
                db.m_recipes.RemoveAll(r => r == null || r.name == name);
                db.m_recipes.Add(recipe);
            }
            return recipe;
        }

        /// <summary>Takes a recipe made here out of ObjectDB and destroys it.</summary>
        public static void RemoveRecipe(Recipe recipe)
        {
            if (ReferenceEquals(recipe, null)) return;
            ObjectDB.instance?.m_recipes.RemoveAll(r => r == null || r == recipe);
            foreach (string key in OurRecipes.Where(kv => kv.Value == recipe).Select(kv => kv.Key).ToList()) OurRecipes.Remove(key);
            if (recipe != null) Object.Destroy(recipe);
        }

        // ---- smelting, cooking, fermenting ------------------------------------------------------------------------

        /// <summary>
        /// from becomes to in the station prefab's Smelter (smelter, blastfurnace, charcoal_kiln, eitrrefinery...), CookingStation (cooking
        /// station, iron cooking station, oven: after cookSeconds) or Fermenter (makes of it per batch). This mod's earlier entry for the same
        /// pair, and entries left by an unloaded copy, are removed first. False when the prefab has none of the three.
        /// The game's defaults: cookSeconds 10, makes 4.
        /// </summary>
        public static bool Conversion(GameObject stationPrefab, GameObject from, GameObject to, float cookSeconds = 10f, int makes = 4)
        {
            ItemDrop a = from != null ? from.GetComponent<ItemDrop>() : null, b = to != null ? to.GetComponent<ItemDrop>() : null;
            if (stationPrefab == null || a == null || b == null) return false;
            RemoveConversion(stationPrefab, from.name, to.name, forget: false);
            bool added = false;
            if (stationPrefab.GetComponent<Smelter>() is Smelter smelter)
            { smelter.m_conversion.Add(new Smelter.ItemConversion { m_from = a, m_to = b }); added = true; }
            else if (stationPrefab.GetComponent<CookingStation>() is CookingStation cooking)
            { cooking.m_conversion.Add(new CookingStation.ItemConversion { m_from = a, m_to = b, m_cookTime = cookSeconds }); added = true; }
            else if (stationPrefab.GetComponent<Fermenter>() is Fermenter fermenter)
            { fermenter.m_conversion.Add(new Fermenter.ItemConversion { m_from = a, m_to = b, m_producedItems = Mathf.Max(1, makes) }); added = true; }
            if (added && !OurConversions.Contains((stationPrefab, from.name, to.name))) OurConversions.Add((stationPrefab, from.name, to.name));
            return added;
        }

        /// <summary>Takes the from-to entry out of the station prefab's conversions (and any left by an unloaded copy).</summary>
        public static void RemoveConversion(GameObject stationPrefab, string from, string to) => RemoveConversion(stationPrefab, from, to, forget: true);

        private static void RemoveConversion(GameObject stationPrefab, string from, string to, bool forget)
        {
            if (stationPrefab == null) return;
            bool Match(ItemDrop f, ItemDrop t) => Destroyed(f) || Destroyed(t) || (f != null && t != null && f.name == from && t.name == to);
            stationPrefab.GetComponent<Smelter>()?.m_conversion.RemoveAll(c => c == null || Match(c.m_from, c.m_to));
            stationPrefab.GetComponent<CookingStation>()?.m_conversion.RemoveAll(c => c == null || Match(c.m_from, c.m_to));
            stationPrefab.GetComponent<Fermenter>()?.m_conversion.RemoveAll(c => c == null || Match(c.m_from, c.m_to));
            if (forget) OurConversions.Remove((stationPrefab, from, to));
        }

        // ---- creature drops --------------------------------------------------------------------------------------

        /// <summary>
        /// The creature prefab drops item: chance 0 to 1, min to max of it (both included; the game's own m_amountMax is never reached, so it's
        /// stored as max + 1). perLevel: chance and amount double with each star, as the game's own drops do. Any earlier entry for an item of
        /// this name is removed first. Returns the entry (set m_onePerPlayer, m_dontScale on it if wanted), or null.
        /// </summary>
        public static CharacterDrop.Drop Drop(GameObject creaturePrefab, GameObject item, float chance = 1f, int min = 1, int max = 1, bool perLevel = true)
        {
            if (creaturePrefab == null || item == null) return null;
            CharacterDrop drops = creaturePrefab.GetComponent<CharacterDrop>();
            if (drops == null) drops = creaturePrefab.AddComponent<CharacterDrop>();   // (a creature that drops nothing has none)
            drops.m_drops.RemoveAll(d => d == null || Destroyed(d.m_prefab) || (d.m_prefab != null && d.m_prefab.name == item.name));
            min = Mathf.Max(0, min);
            var drop = new CharacterDrop.Drop { m_prefab = item, m_chance = Mathf.Clamp01(chance), m_amountMin = min, m_amountMax = Mathf.Max(min, max) + 1, m_levelMultiplier = perLevel };
            drops.m_drops.Add(drop);
            if (!OurDrops.Contains((creaturePrefab, item.name))) OurDrops.Add((creaturePrefab, item.name));
            return drop;
        }

        /// <summary>Takes the creature prefab's drop of the item of this name away (and entries left by an unloaded copy).</summary>
        public static void RemoveDrop(GameObject creaturePrefab, string item)
        {
            if (creaturePrefab == null) return;
            creaturePrefab.GetComponent<CharacterDrop>()?.m_drops.RemoveAll(d => d == null || Destroyed(d.m_prefab) || (d.m_prefab != null && d.m_prefab.name == item));
            OurDrops.Remove((creaturePrefab, item));
        }

        // ---- trader goods ----------------------------------------------------------------------------------------

        private class Sale { public string Trader; public Trader.TradeItem Item; public Func<bool> When; }

        /// <summary>
        /// The trader (its prefab name: Haldor, Hildir, BogWitch) sells stack of item for price coins, once globalKey is set in the world
        /// (null: always) and when says so (null: always; pass () => Synced.Server != ServerSettings.ServerMod.Missing, or a bought item
        /// would be deleted by a server without the mod). Added through a Trader.GetAvailableItems postfix on your own Harmony (gone with
        /// UnpatchSelf; no prefab changed), in a fixed order (the store window picks by index). Calling it again for the same trader and
        /// item changes the same entry. Mind m_value: the trader also buys anything worth more than 0.
        /// </summary>
        public static Trader.TradeItem Sell(Harmony harmony, string trader, GameObject item, int price, int stack = 1, string globalKey = null, Func<bool> when = null)
        {
            ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
            if (harmony == null || string.IsNullOrEmpty(trader) || drop == null) return null;
            Sale sale = OurSales.Find(s => s.Trader == trader && s.Item.m_prefab != null && s.Item.m_prefab.name == item.name);
            if (sale == null) OurSales.Add(sale = new Sale { Trader = trader, Item = new Trader.TradeItem() });
            sale.Item.m_prefab = drop;
            sale.Item.m_price = Mathf.Max(0, price);
            sale.Item.m_stack = Mathf.Max(1, stack);
            sale.Item.m_requiredGlobalKey = globalKey ?? "";
            sale.When = when;
            MethodInfo original = AccessTools.Method(typeof(Trader), nameof(global::Trader.GetAvailableItems));
            MethodInfo postfix = AccessTools.Method(typeof(Content), nameof(AddSales));
            Patches info = Harmony.GetPatchInfo(original);
            if (info == null || !info.Postfixes.Any(p => p.owner == harmony.Id && p.PatchMethod == postfix))
                harmony.Patch(original, postfix: new HarmonyMethod(postfix));
            return sale.Item;
        }

        /// <summary>The trader stops selling the item of this name.</summary>
        public static void RemoveSale(string trader, string item) =>
            OurSales.RemoveAll(s => s.Trader == trader && (s.Item.m_prefab == null || s.Item.m_prefab.name == item));

        private static void AddSales(Trader __instance, List<Trader.TradeItem> __result)
        {
            if (__result == null || OurSales.Count == 0) return;
            string name = Utils.GetPrefabName(__instance.gameObject);
            foreach (Sale s in OurSales)
            {
                if (s.Trader != name || s.Item.m_prefab == null || __result.Contains(s.Item)) continue;
                if (!string.IsNullOrEmpty(s.Item.m_requiredGlobalKey) && (ZoneSystem.instance == null || !ZoneSystem.instance.GetGlobalKey(s.Item.m_requiredGlobalKey))) continue;
                bool ok;
                try { ok = s.When == null || s.When(); } catch { ok = false; }
                if (ok) __result.Add(s.Item);
            }
        }

        // ---- all of it --------------------------------------------------------------------------------------------

        /// <summary>The plugin unloading: takes out everything registered here (only what still points at this copy's objects) and destroys
        /// the recipes made here. Items, status effects and stations are the mod's own to destroy. keepItems: leave the items and their
        /// status effects registered (ItemReload.KeepOnUnload: the next copy replaces them by name).</summary>
        public static void RemoveAll(bool keepItems = false)
        {
            if (!keepItems)
            {
                if (OurItems.Count > 0) RemoveItems(OurItems.ToList());
                ObjectDB db = ObjectDB.instance;
                if (db != null) foreach (StatusEffect e in OurEffects) db.m_StatusEffects.Remove(e);
            }
            OurItems.Clear();
            OurEffects.Clear();
            foreach (Recipe r in OurRecipes.Values.ToList()) RemoveRecipe(r);
            OurRecipes.Clear();
            foreach (var c in OurConversions.ToList()) RemoveConversion(c.Station, c.From, c.To);
            OurConversions.Clear();
            foreach (var d in OurDrops.ToList()) RemoveDrop(d.Creature, d.Item);
            OurDrops.Clear();
            OurSales.Clear();
        }
    }
}
