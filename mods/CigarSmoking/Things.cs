using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace CigarSmoking
{
    /// <summary>
    /// Everything the mod adds to the game: the items (seeds, leaves, cigars), the pieces (plants, rack, barrel, table, humidor), the
    /// status effects and the recipes. It builds them once and puts them where the game looks for them, and can be asked again and again:
    /// ZNetScene (needed to load pieces and dropped items from a save) and ObjectDB (needed for the inventory and crafting) wake in either
    /// order, and the game swaps the item lists when a world loads (see docs/modding-pitfalls.md).
    ///
    /// Split across files: Things.cs (this: registering), Items.cs (the items), Pieces.cs (the pieces and plants).
    /// </summary>
    internal static partial class Things
    {
        private static GameObject _holder;
        private static bool _items, _pieces, _recipes;

        /// <summary>Every prefab the mod adds, by name.</summary>
        internal static readonly Dictionary<string, GameObject> Made = new Dictionary<string, GameObject>();
        private static readonly List<GameObject> ItemPrefabs = new List<GameObject>();
        private static readonly List<GameObject> HammerPieces = new List<GameObject>();
        private static readonly List<GameObject> CultivatorPieces = new List<GameObject>();
        internal static readonly List<SE_Smoking> Effects = new List<SE_Smoking>();
        private static readonly List<Recipe> Recipes = new List<Recipe>();

        internal static CraftingStation TableStation;

        internal static SE_Smoking EffectFor(string itemSharedName)
        {
            for (int i = 0; i < Types.All.Length; i++)
                if (Types.All[i].Display == itemSharedName && i < Effects.Count) return Effects[i];
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
            player.Message(MessageHud.MessageType.Center, "Your bag is full: the rest is on the ground");
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

            if (scene != null)
            {
                Dictionary<int, GameObject> named = Named(scene);
                foreach (GameObject prefab in Made.Values)
                {
                    if (!scene.m_prefabs.Contains(prefab)) scene.m_prefabs.Add(prefab);
                    named[prefab.name.GetStableHashCode()] = prefab; // lets it be placed, dropped and loaded from saves
                }
                AddToTable(scene.GetPrefab("Hammer"), HammerPieces);
                AddToTable(scene.GetPrefab("Cultivator"), CultivatorPieces);
            }

            if (db != null)
            {
                if (_pieces && !_recipes) { BuildRecipes(db); _recipes = true; }
                bool changed = false;
                foreach (GameObject item in ItemPrefabs)
                    if (!db.m_items.Contains(item)) { db.m_items.RemoveAll(i => i == null || i.name == item.name); db.m_items.Add(item); changed = true; }
                foreach (SE_Smoking effect in Effects)
                    if (!db.m_StatusEffects.Contains(effect)) { db.m_StatusEffects.RemoveAll(e => e == null || e.name == effect.name); db.m_StatusEffects.Add(effect); }
                foreach (Recipe recipe in Recipes)
                    if (!db.m_recipes.Contains(recipe)) { db.m_recipes.RemoveAll(r => r == null || r.name == recipe.name); db.m_recipes.Add(recipe); }
                if (changed) AccessTools.Method(typeof(ObjectDB), "UpdateRegisters").Invoke(db, null);
            }

            World.Add();
        }

        private static void AddToTable(GameObject tool, List<GameObject> pieces)
        {
            PieceTable table = tool != null ? tool.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
            if (table == null) return;
            foreach (GameObject piece in pieces) if (!table.m_pieces.Contains(piece)) table.m_pieces.Add(piece);
        }

        internal static void Unregister()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene != null)
            {
                Dictionary<int, GameObject> named = Named(scene);
                foreach (GameObject prefab in Made.Values)
                {
                    scene.m_prefabs.Remove(prefab);
                    int hash = prefab.name.GetStableHashCode();
                    if (named.TryGetValue(hash, out GameObject current) && current == prefab) named.Remove(hash); // never another copy's entry
                }
                foreach (string tool in new[] { "Hammer", "Cultivator" })
                {
                    PieceTable table = scene.GetPrefab(tool)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
                    if (table != null) table.m_pieces.RemoveAll(p => p == null || Made.ContainsValue(p));
                }
            }
            ObjectDB db = ObjectDB.instance;
            if (db != null)
            {
                db.m_items.RemoveAll(i => i == null || ItemPrefabs.Contains(i));
                db.m_StatusEffects.RemoveAll(e => e == null || Effects.Contains(e));
                db.m_recipes.RemoveAll(r => r == null || Recipes.Contains(r));
                AccessTools.Method(typeof(ObjectDB), "UpdateRegisters").Invoke(db, null);
            }
            World.Remove();
            foreach (Recipe r in Recipes) Object.Destroy(r);
            foreach (SE_Smoking e in Effects) Object.Destroy(e);
            Made.Clear(); ItemPrefabs.Clear(); HammerPieces.Clear(); CultivatorPieces.Clear(); Effects.Clear(); Recipes.Clear();
            TableStation = null;
            _items = _pieces = _recipes = false;
            if (_holder != null) Object.Destroy(_holder);
            _holder = null;
            Look.Clear(); Icons.Clear(); Meshes.Clear();
        }

        private static void BuildRecipes(ObjectDB db)
        {
            if (TableStation == null) return;
            foreach (CigarType t in Types.All)
            {
                var cost = new List<Piece.Requirement>();
                ItemDrop leaf = Find(Strains.ById(t.Strain).Item(t.Stage))?.GetComponent<ItemDrop>();
                if (leaf != null) cost.Add(new Piece.Requirement { m_resItem = leaf, m_amount = t.Leaves, m_recover = false });
                for (int i = 0; i < t.Extra.Length; i++)
                {
                    ItemDrop extra = Find(t.Extra[i])?.GetComponent<ItemDrop>();
                    if (extra != null) cost.Add(new Piece.Requirement { m_resItem = extra, m_amount = t.ExtraAmount[i], m_recover = false });
                }
                Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
                recipe.name = "Recipe_" + t.Prefab;
                recipe.m_item = Made[t.Prefab].GetComponent<ItemDrop>();
                recipe.m_amount = t.Makes;
                recipe.m_craftingStation = TableStation;
                recipe.m_minStationLevel = t.Level;
                recipe.m_resources = cost.ToArray();
                Recipes.Add(recipe);
            }
        }

        /// <summary>A build cost: how many of an item (ours or the game's).</summary>
        private static Piece.Requirement Req(string item, int amount) =>
            new Piece.Requirement { m_resItem = Find(item)?.GetComponent<ItemDrop>(), m_amount = amount, m_recover = true };
    }
}
