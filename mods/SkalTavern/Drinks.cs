using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SkalTavern
{
    /// <summary>
    /// The four drinks and everything the mod adds to the game: the items (copies of the game's own Tankard, so they are the real mug in the
    /// hand and on the ground, with the drink's own colour), their recipes at the cauldron, and the status effects. Built once and put where
    /// the game looks for them, and can be asked again and again: ZNetScene (needed to load dropped items from a save) and ObjectDB (needed for
    /// the inventory and crafting) wake in either order, and the game swaps the item lists when a world loads (see docs/modding-pitfalls.md).
    /// The items and recipes go in through Shared/Content.cs (by name, again on every register), the effects through Shared/StatusEffects.cs.
    /// </summary>
    internal static class Drinks
    {
        internal class Def
        {
            public string Prefab, Display, Flavour;   // the words: "$keys" in translations/*.json
            public float Amount;          // how much drunker a whole mug makes you (0 to 100 is sober to very drunk): a quarter of it each swig
            public Color Liquid;
            public int Makes;
            public (string Item, int Count)[] Cost;
        }

        /// <summary>The drinks, weakest first. What they cost grows with how far into the game you are.</summary>
        internal static readonly Def[] All =
        {
            new Def { Prefab = "dh_drink_ale", Display = "$skaltavern_ale", Amount = 14f, Liquid = new Color(0.89f, 0.74f, 0.30f), Makes = 2,
                      Flavour = "$skaltavern_ale_desc",
                      Cost = new[] { ("Barley", 3), ("Honey", 1) } },
            new Def { Prefab = "dh_drink_honeymead", Display = "$skaltavern_honeymead", Amount = 22f, Liquid = new Color(0.95f, 0.58f, 0.10f), Makes = 2,
                      Flavour = "$skaltavern_honeymead_desc",
                      Cost = new[] { ("Honey", 4), ("Raspberry", 2) } },
            new Def { Prefab = "dh_drink_wine", Display = "$skaltavern_wine", Amount = 20f, Liquid = new Color(0.40f, 0.10f, 0.34f), Makes = 2,
                      Flavour = "$skaltavern_wine_desc",
                      Cost = new[] { ("Blueberries", 4), ("Honey", 1) } },
            new Def { Prefab = "dh_drink_skaldmead", Display = "$skaltavern_skaldmead", Amount = 34f, Liquid = new Color(0.97f, 0.80f, 0.20f), Makes = 2,
                      Flavour = "$skaltavern_skaldmead_desc",
                      Cost = new[] { ("Honey", 5), ("Cloudberry", 3) } },
        };

        internal static readonly int TipsyHash = "SE_SkalTipsy".GetStableHashCode(), HangoverHash = "SE_SkalHangover".GetStableHashCode(), SkalHash = "SE_SkalBuff".GetStableHashCode();

        private static GameObject _holder;
        private static bool _items;
        private static readonly Dictionary<string, GameObject> Made = new Dictionary<string, GameObject>();
        private static readonly List<GameObject> ItemPrefabs = new List<GameObject>();
        private static readonly List<Recipe> Recipes = new List<Recipe>();
        private static Sprite _mug;

        /// <summary>A prefab by name: one of ours, or one of the game's (from the scene, or the item database).</summary>
        private static GameObject Find(string name)
        {
            if (Made.TryGetValue(name, out GameObject own) && own != null) return own;
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            if (go == null && ObjectDB.instance != null) go = ObjectDB.instance.GetItemPrefab(name);
            return go;
        }

        internal static void Register()
        {
            ObjectDB db = ObjectDB.instance;
            ZNetScene scene = ZNetScene.instance;
            if (db == null && scene == null) return;

            if (_holder == null)
            {
                _holder = new GameObject("SkalTavernPrefabs");
                _holder.SetActive(false); // keeps the copies from waking up as real objects
                Object.DontDestroyOnLoad(_holder);
            }
            if (!_items)
            {
                if (!BuildItems()) return;
                _items = true;
            }

            DHack.Shared.Content.Items(ItemPrefabs);   // into ZNetScene (dropped and saved ones load by name) and ObjectDB, with the drinks' effects

            if (db != null)
            {
                DHack.Shared.StatusEffects.Register();   // the drinks' effects; after a reload, back on whoever had one, with the time left (and how drunk)
                CraftingStation cauldron = Find("piece_cauldron")?.GetComponent<CraftingStation>();
                if (cauldron != null) RegisterRecipes(cauldron, db);
                foreach (Recipe recipe in Recipes) Plugin.Synced.NeedsServer(recipe);   // not craftable on a server without the mod (the drink would be lost)
            }
            if (!_rebound) Rebind();
        }

        private static bool _rebound;

        /// <summary>
        /// After this mod reloaded (a hot reload, or the manager updating it mid-game): the drinks already in bags, chests and on the ground still
        /// point at the copies of the items and effects from before, which are gone, so they could not be dropped or drunk. Each is pointed at
        /// the new ones. (From a saved game they are made from the new ones to begin with.)
        /// </summary>
        private static void Rebind()
        {
            if (ObjectDB.instance == null || ZNetScene.instance == null) return;
            _rebound = true;
            int fixedItems = 0;
            foreach (Player player in Player.GetAllPlayers()) fixedItems += Rebind(player.GetInventory());
            foreach (Container container in Object.FindObjectsOfType<Container>()) if (container != null) fixedItems += Rebind(container.GetInventory());
            foreach (ItemDrop drop in Object.FindObjectsOfType<ItemDrop>()) if (drop != null && Fix(drop.m_itemData)) fixedItems++;
            if (fixedItems > 0) Plugin.Log.LogInfo($"{fixedItems} drink(s) already in the world were attached to the reloaded mod");
        }

        private static int Rebind(Inventory inventory) => inventory == null ? 0 : inventory.GetAllItems().Count(Fix);

        private static bool Fix(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null) return false;
            Def d = All.FirstOrDefault(x => x.Display == item.m_shared.m_name);
            if (d == null || !Made.TryGetValue(d.Prefab, out GameObject go) || go == null || item.m_dropPrefab == go) return false;
            item.m_shared = go.GetComponent<ItemDrop>().m_itemData.m_shared;
            item.m_dropPrefab = go;
            return true;
        }

        internal static void Unregister()
        {
            DHack.Shared.Content.RemoveAll();   // the items (where still these), the drinks' effects, and the recipes (destroyed)
            DHack.Shared.StatusEffects.Unload();   // the effects: off every character here (remembered for a reloaded copy), out of ObjectDB, destroyed
            Made.Clear(); ItemPrefabs.Clear(); Recipes.Clear(); ToldMissing.Clear();
            _items = _rebound = false;
            if (_holder != null) Object.Destroy(_holder);
            _holder = null;
        }

        // ---- building them ------------------------------------------------------------------------------------------

        private static bool BuildItems()
        {
            GameObject source = Find("Tankard");
            if (source == null) return false;   // (the game's items are not loaded yet: asked again)
            _mug = source.GetComponent<ItemDrop>().m_itemData.m_shared.m_icons?.FirstOrDefault();

            foreach (Def d in All)
            {
                Sprite icon = Tint(_mug, d.Liquid);
                // The effects keep the names they have always had (SE_dh_drink_..., SE_Skal...), not renamed after the mod: a rename changes the
                // hash the game finds them by, and these are ours alone.
                var effect = DHack.Shared.StatusEffects.Create<SE_Drink>("SE_" + d.Prefab, d.Display, icon);
                effect.m_ttl = 1f;
                effect.Amount = d.Amount / Swigs.PerMug;   // (one swig's worth: a mug is drunk in four, see Swigs.cs)
                effect.Share = 1f / Swigs.PerMug;
                MakeItem(source, d, icon, effect);
            }

            DHack.Shared.StatusEffects.Create<SE_Tipsy>("SE_SkalTipsy", "$skaltavern_tipsy", Tint(_mug, new Color(0.9f, 0.6f, 0.2f)));
            DHack.Shared.StatusEffects.Create<SE_Hangover>("SE_SkalHangover", "$skaltavern_hangover", Tint(_mug, new Color(0.45f, 0.5f, 0.4f)));
            DHack.Shared.StatusEffects.Create<SE_Skal>("SE_SkalBuff", "$skaltavern_skal", Tint(_mug, new Color(1f, 0.85f, 0.3f)));
            return true;
        }

        /// <summary>A copy of the game's Tankard item, dressed as one drink: its name, its colour in the mug, its icon and what it does.</summary>
        private static void MakeItem(GameObject source, Def d, Sprite icon, StatusEffect effect)
        {
            GameObject go = Object.Instantiate(source, _holder.transform);
            go.name = d.Prefab;

            // the liquid in the mug, in this drink's colour (its own copy of the material)
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name.StartsWith("mead")) mats[i] = new Material(mats[i]) { color = d.Liquid };
                r.sharedMaterials = mats;
            }

            ItemDrop drop = go.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
            s.m_name = d.Display;
            s.m_description = d.Flavour + "\n\n$skaltavern_drink_note\n$skaltavern_swig_note";   // (the game translates each $key in it)
            s.m_itemType = ItemDrop.ItemData.ItemType.Consumable;
            s.m_icons = new[] { icon };
            s.m_maxStackSize = 10;
            s.m_weight = 0.5f;
            s.m_value = 0;   // (worth nothing at the trader: they are cheap to brew, and a price made the beehive a coin machine)
            s.m_maxQuality = 1;
            s.m_variants = 0;
            s.m_food = 0f;
            s.m_foodStamina = 0f;
            s.m_foodEitr = 0f;
            s.m_foodBurnTime = 0f;
            s.m_foodEatAnimTime = 1.8f;
            s.m_teleportable = true;
            s.m_questItem = false;
            s.m_dlc = "";
            s.m_equipStatusEffect = null;
            s.m_consumeStatusEffect = effect;
            drop.m_itemData.m_dropPrefab = go;
            drop.m_itemData.m_stack = 1;
            Made[d.Prefab] = go;
            ItemPrefabs.Add(go);
        }

        /// <summary>The drinks' recipes at the cauldron: made the first time (cost: the setting, or the default when it can't be read), put
        /// back into ObjectDB by name every time (Shared/Content.cs). A drink whose ingredient isn't found has none (said once).</summary>
        private static void RegisterRecipes(CraftingStation cauldron, ObjectDB db)
        {
            for (int i = 0; i < All.Length; i++)
            {
                Def d = All[i];
                Recipe made = Recipes.Find(r => r != null && r.name == "Recipe_" + d.Prefab);
                Piece.Requirement[] cost = made != null ? null : DefaultRequirements(d);
                if (made == null && cost == null) continue;
                Recipe recipe = DHack.Shared.Content.Recipe(Made[d.Prefab], cauldron, 1, d.Makes, cost, Plugin.DrinkCost[i].Value, Plugin.Log, db: db);
                if (recipe != null && !Recipes.Contains(recipe)) Recipes.Add(recipe);
            }
        }

        private static readonly HashSet<string> ToldMissing = new HashSet<string>();

        /// <summary>What brewing a batch of this drink takes before anyone changes it, as requirements; null if an ingredient isn't found.</summary>
        private static Piece.Requirement[] DefaultRequirements(Def d)
        {
            var cost = new List<Piece.Requirement>();
            foreach ((string item, int count) in d.Cost)
            {
                ItemDrop drop = Find(item)?.GetComponent<ItemDrop>();
                if (drop == null)
                {
                    if (ToldMissing.Add(d.Prefab)) Plugin.Log.LogWarning($"{d.Display}: the item {item} was not found, so it has no recipe");
                    return null;
                }
                cost.Add(new Piece.Requirement { m_resItem = drop, m_amount = count, m_recover = false });
            }
            return cost.ToArray();
        }

        /// <summary>What brewing a batch of this drink takes before anyone changes it, as the cost setting writes it.</summary>
        internal static string DefaultCost(Def d) => string.Join(", ", d.Cost.Select(c => c.Item + ":" + c.Count).ToArray());

        /// <summary>The cost settings (the server's in multiplayer) on the recipes. One that can't be read keeps the last good cost.</summary>
        internal static void ApplyCosts()
        {
            foreach (Recipe recipe in Recipes)
            {
                int i = All.ToList().FindIndex(d => "Recipe_" + d.Prefab == recipe.name);
                if (i >= 0) DHack.Shared.Costs.Apply(recipe, Plugin.DrinkCost[i].Value, Plugin.Log);
            }
        }

        // ---- the icon: the game's mug, the liquid in the drink's colour ---------------------------------------------------

        /// <summary>The golden ale in the Tankard's icon turned to this colour (its own hue, with the shading kept).</summary>
        private static Sprite Tint(Sprite src, Color liquid)
        {
            if (src == null) return null;
            try
            {
                Texture2D tex = src.texture;
                Rect r = src.textureRect;
                int w = (int)r.width, h = (int)r.height;
                RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(tex, rt);                       // (the game's texture is not readable: this copy is)
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);

                Color.RGBToHSV(liquid, out float hue, out float sat, out _);
                Color[] pixels = copy.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color.RGBToHSV(pixels[i], out float ph, out float ps, out float pv);
                    if (ps > 0.35f && ph > 0.07f && ph < 0.18f)   // (the ale: golden)
                    {
                        Color c = Color.HSVToRGB(hue, Mathf.Clamp01(Mathf.Max(ps, sat * 0.9f)), pv);
                        c.a = pixels[i].a;
                        pixels[i] = c;
                    }
                }
                copy.SetPixels(pixels);
                copy.Apply();
                return Sprite.Create(copy, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), src.pixelsPerUnit);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Could not colour a drink's icon: " + e.Message);
                return src;
            }
        }
    }
}
