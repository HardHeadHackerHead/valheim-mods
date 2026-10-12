using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static DHack.Shared.Translations;

namespace CigarSmoking
{
    /// <summary>
    /// Everything the mod adds to the game: the items (seeds, leaves, cigars), the pieces (plants, rack, barrel, table, humidor), the
    /// status effects and the recipes. It builds them once and puts them where the game looks for them, and can be asked again and again:
    /// ZNetScene (needed to load pieces and dropped items from a save) and ObjectDB (needed for the inventory and crafting) wake in either
    /// order, and the game swaps the item lists when a world loads (see docs/modding-pitfalls.md).
    ///
    /// Split across files: Things.cs (this: registering), Items.cs (the items), Pieces.cs (the pieces and plants). The items and recipes
    /// go in through Shared/Content.cs (by name, again on every register).
    /// </summary>
    internal static partial class Things
    {
        private static GameObject _holder;
        private static bool _items, _pieces;

        /// <summary>Every prefab the mod adds, by name.</summary>
        internal static readonly Dictionary<string, GameObject> Made = new Dictionary<string, GameObject>();
        private static readonly List<GameObject> ItemPrefabs = new List<GameObject>();
        private static readonly List<GameObject> HammerPieces = new List<GameObject>();
        private static readonly List<GameObject> CultivatorPieces = new List<GameObject>();
        internal static readonly List<SE_Smoking> Effects = new List<SE_Smoking>();
        private static readonly Recipe[] Recipes = new Recipe[Types.All.Length];   // in the order of Types.All (and Plugin.CigarCost)

        internal static CraftingStation TableStation;

        /// <summary>The effect of a cigar, found by its prefab (or its name's key: the words change with the language).</summary>
        internal static SE_Smoking EffectFor(ItemDrop.ItemData item)
        {
            string prefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
            for (int i = 0; i < Types.All.Length; i++)
                if ((Types.All[i].Prefab == prefab || Types.All[i].Display == item.m_shared.m_name) && i < Effects.Count) return Effects[i];
            return null;
        }

        /// <summary>A prefab by name: one of ours, or one of the game's (from the scene, or the item database).</summary>
        internal static GameObject Find(string name)
        {
            if (Made.TryGetValue(name, out GameObject own) && own != null) return own;
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            if (go == null && ObjectDB.instance != null) go = ObjectDB.instance.GetItemPrefab(name);
            return go;
        }

        internal static string SharedName(string prefab)
        {
            ItemDrop drop = Find(prefab)?.GetComponent<ItemDrop>();
            return drop != null ? drop.m_itemData.m_shared.m_name : prefab;
        }

        internal static string DisplayName(string prefab) => Localization.instance.Localize(SharedName(prefab));

        /// <summary>
        /// Puts items in the player's bag; whatever does not fit lands at their feet as a stack. (The game's own add-by-name call drops a single
        /// copy when the bag is full, and loses the rest.)
        /// </summary>
        internal static void Give(Player player, string prefab, int amount)
        {
            GameObject go = Find(prefab);
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop == null) return;
            ItemDrop.ItemData data = drop.m_itemData.Clone();
            data.m_dropPrefab = go;
            data.m_stack = Mathf.Max(1, amount);
            data.m_worldLevel = (byte)Game.m_worldLevel;
            if (player.GetInventory().AddItem(data)) return;
            // it did not all fit: m_stack is now what is left over
            ItemDrop.DropItem(data, Mathf.Max(1, data.m_stack), player.transform.position + player.transform.forward * 0.8f + Vector3.up, Quaternion.identity);
            player.Message(MessageHud.MessageType.Center, T("cigarsmoking_bag_full"));
        }

        /// <summary>Puts items on the ground as one stack (what a broken rack or barrel held).</summary>
        internal static void Drop(string prefab, int amount, Vector3 at)
        {
            GameObject go = Find(prefab);
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop == null || amount <= 0) return;
            ItemDrop.ItemData data = drop.m_itemData.Clone();
            data.m_dropPrefab = go;
            data.m_worldLevel = (byte)Game.m_worldLevel;
            ItemDrop.DropItem(data, amount, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        }

        /// <summary>
        /// Runs the part of building something that only makes it look right (models, materials). If it fails, the thing is still
        /// registered, only plainer: a piece that is not registered is deleted from the world by the host (docs/modding-pitfalls.md).
        /// </summary>
        private static void Safely(string what, System.Action look)
        {
            try { look(); }
            catch (System.Exception e) { Debug.LogWarning("[" + Plugin.Name + "] could not build the look of " + what + ": " + e.Message); }
        }

        /// <summary>The game's crafting sound and sparks, played when something is put in or taken out.</summary>
        internal static void PlayEffects(Vector3 at)
        {
            try
            {
                CraftingStation bench = ZNetScene.instance?.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
                bench?.m_craftItemEffects.Create(at, Quaternion.identity);
            }
            catch { /* effects are optional */ }
        }

        private static Dictionary<int, GameObject> Named(ZNetScene scene) =>
            (Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene);

        internal static void Register()
        {
            ObjectDB db = ObjectDB.instance;
            ZNetScene scene = ZNetScene.instance;
            if (db == null && scene == null) return;

            if (_holder == null)
            {
                _holder = new GameObject("QuadsCigarsPrefabs");
                _holder.SetActive(false); // keeps the copies from waking up as real objects
                Object.DontDestroyOnLoad(_holder);
            }

            if (!_items)
            {
                if (!Look.Ready) Safely("the materials", () => Look.Harvest(scene));
                if (!BuildItems()) return;
                _items = true;
            }
            if (scene != null && !_pieces)
            {
                Safely("the materials", () => Look.Harvest(scene));   // now with the game's own wood, stone and iron
                BuildPieces(scene);
                _pieces = true;
            }

            DHack.Shared.Content.Items(ItemPrefabs);   // into ZNetScene (dropped and saved ones load by name) and ObjectDB, with the cigars' effects
            if (scene != null)
            {
                Dictionary<int, GameObject> named = Named(scene);
                foreach (GameObject prefab in Made.Values)
                {
                    if (ItemPrefabs.Contains(prefab)) continue;   // (the items: above)
                    if (!scene.m_prefabs.Contains(prefab)) scene.m_prefabs.Add(prefab);
                    named[prefab.name.GetStableHashCode()] = prefab; // lets it be placed and loaded from saves
                }
                foreach (GameObject piece in HammerPieces.Concat(CultivatorPieces)) Plugin.Synced.NeedsServer(piece);   // not in the menus on a server without the mod (it would delete them)
                AddToTable(scene.GetPrefab("Hammer"), HammerPieces);
                AddToTable(scene.GetPrefab("Cultivator"), CultivatorPieces);
            }

            if (db != null)
            {
                DHack.Shared.StatusEffects.Register();   // the cigars' effects (Shared/StatusEffects.cs); after a reload, a lit cigar back on with the time it had left
                if (_pieces) RegisterRecipes(db);
                foreach (Recipe recipe in Recipes) Plugin.Synced.NeedsServer(recipe);   // not craftable on a server without the mod (the cigar would be lost)
            }

            World.Add();
        }

        private static readonly HashSet<PieceTable> Tables = new HashSet<PieceTable>();   // the build-menu lists its pieces were put on

        private static void AddToTable(GameObject tool, List<GameObject> pieces)
        {
            PieceTable table = tool != null ? tool.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
            if (table == null) return;
            table.m_pieces.RemoveAll(p => p == null);   // (destroyed pieces left by an earlier copy, ours or another mod's)
            foreach (GameObject piece in pieces) if (!table.m_pieces.Contains(piece)) table.m_pieces.Add(piece);
            Tables.Add(table);
        }

        internal static void Unregister()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene != null)
            {
                Dictionary<int, GameObject> named = Named(scene);
                foreach (GameObject prefab in Made.Values)
                {
                    if (ItemPrefabs.Contains(prefab)) continue;   // (the items: Content.RemoveAll below)
                    scene.m_prefabs.Remove(prefab);
                    int hash = prefab.name.GetStableHashCode();
                    if (named.TryGetValue(hash, out GameObject current) && current == prefab) named.Remove(hash); // never another copy's entry
                }
            }
            // From the lists they were put on, kept: at the main menu there is no scene to look the tools up in, and a destroyed piece left on
            // the hammer's list made every change to the player's bag throw, half-way through crafting (free crafts).
            foreach (PieceTable table in Tables) table.m_pieces.RemoveAll(p => p == null || Made.ContainsValue(p));
            Tables.Clear();
            DHack.Shared.Content.RemoveAll();   // the items (where still these), the cigars' effects, and the recipes (destroyed)
            DHack.Shared.StatusEffects.Unload();   // the cigars' effects: off every character here (a lit one remembered for a reloaded copy), out of ObjectDB, destroyed
            World.Remove();
            Made.Clear(); ItemPrefabs.Clear(); HammerPieces.Clear(); CultivatorPieces.Clear(); Effects.Clear(); System.Array.Clear(Recipes, 0, Recipes.Length);
            TableStation = null;
            _items = _pieces = false;
            if (_holder != null) Object.Destroy(_holder);
            _holder = null;
            Look.Clear(); Icons.Clear(); Meshes.Clear();
        }

        /// <summary>The cigars' recipes at the rolling table: made the first time (cost: the setting, or the default when it can't be read),
        /// put back into ObjectDB by name every time (Shared/Content.cs).</summary>
        private static void RegisterRecipes(ObjectDB db)
        {
            if (TableStation == null) return;
            for (int i = 0; i < Types.All.Length; i++)
            {
                CigarType t = Types.All[i];
                Recipes[i] = DHack.Shared.Content.Recipe(Made[t.Prefab], TableStation, t.Level, t.Makes, DefaultRequirements(t),
                                                         Plugin.CigarCost[i].Value, Plugin.Log, db: db);
            }
        }

        /// <summary>What rolling a batch of this cigar takes before anyone changes it, as requirements (an item that can't be found is left out).</summary>
        private static Piece.Requirement[] DefaultRequirements(CigarType t)
        {
            var cost = new List<Piece.Requirement>();
            ItemDrop leaf = Find(Strains.ById(t.Strain).Item(t.Stage))?.GetComponent<ItemDrop>();
            if (leaf != null) cost.Add(new Piece.Requirement { m_resItem = leaf, m_amount = t.Leaves, m_recover = false });
            for (int i = 0; i < t.Extra.Length; i++)
            {
                ItemDrop extra = Find(t.Extra[i])?.GetComponent<ItemDrop>();
                if (extra != null) cost.Add(new Piece.Requirement { m_resItem = extra, m_amount = t.ExtraAmount[i], m_recover = false });
            }
            return cost.ToArray();
        }

        /// <summary>What rolling a batch of this cigar takes before anyone changes it, as the cost setting writes it.</summary>
        internal static string CigarDefault(CigarType t)
        {
            var parts = new List<string> { Strains.ById(t.Strain).Item(t.Stage) + ":" + t.Leaves };
            for (int i = 0; i < t.Extra.Length; i++) parts.Add(t.Extra[i] + ":" + t.ExtraAmount[i]);
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>The cost settings (the server's in multiplayer) on the pieces and recipes. One that can't be read keeps the last good cost.</summary>
        internal static void ApplyCosts()
        {
            ApplyPiece("dh_cigar_table", Plugin.TableCost);
            ApplyPiece("dh_humidor", Plugin.HumidorCost);
            ApplyPiece("dh_drying_rack", Plugin.RackCost);
            ApplyPiece("dh_curing_barrel", Plugin.BarrelCost);
            for (int i = 0; i < Recipes.Length && i < Plugin.CigarCost.Length; i++)
                DHack.Shared.Costs.Apply(Recipes[i], Plugin.CigarCost[i].Value, Plugin.Log);
        }

        private static void ApplyPiece(string prefab, BepInEx.Configuration.ConfigEntry<string> cost)
        {
            if (Made.TryGetValue(prefab, out GameObject go) && go != null) DHack.Shared.Costs.Apply(go.GetComponent<Piece>(), cost.Value, Plugin.Log);
        }
    }
}
