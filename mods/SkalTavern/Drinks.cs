using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SkalTavern
{
    /// <summary>
    /// The four drinks and everything the mod adds to the game: the items (copies of the game's own Tankard, so they are the real mug in the
    /// hand and on the ground, with the drink's own colour), their recipes at the cauldron, and the status effects. Built once and put where
    /// the game looks for them, and can be asked again and again: ZNetScene (needed to load dropped items from a save) and ObjectDB (needed for
    /// the inventory and crafting) wake in either order, and the game swaps the item lists when a world loads (see docs/modding-pitfalls.md).
    /// </summary>
    internal static class Drinks
    {
        internal class Def
        {
            public string Prefab, Display, Flavour;
            public float Amount;          // how much drunker one makes you (0 to 100 is sober to very drunk)
            public Color Liquid;
            public int Makes;
            public (string Item, int Count)[] Cost;
        }

        /// <summary>The drinks, weakest first. What they cost grows with how far into the game you are.</summary>
        internal static readonly Def[] All =
        {
            new Def { Prefab = "dh_drink_ale", Display = "Tankard of Ale", Amount = 14f, Liquid = new Color(0.89f, 0.74f, 0.30f), Makes = 2,
                      Flavour = "Cold, golden and honest. A tankard warms you and puts a spring in your step.",
                      Cost = new[] { ("Barley", 3), ("Honey", 1) } },
            new Def { Prefab = "dh_drink_honeymead", Display = "Honey Mead", Amount = 22f, Liquid = new Color(0.95f, 0.58f, 0.10f), Makes = 2,
                      Flavour = "Sweet, strong and dangerous. The skalds swear it carries songs.",
                      Cost = new[] { ("Honey", 4), ("Raspberry", 2) } },
            new Def { Prefab = "dh_drink_wine", Display = "Blueberry Wine", Amount = 20f, Liquid = new Color(0.40f, 0.10f, 0.34f), Makes = 2,
                      Flavour = "Dark and sharp, pressed from the berries of the Black Forest. Sneaks up on you.",
                      Cost = new[] { ("Blueberries", 4), ("Honey", 1) } },
            new Def { Prefab = "dh_drink_skaldmead", Display = "Skaldic Mead", Amount = 34f, Liquid = new Color(0.97f, 0.80f, 0.20f), Makes = 2,
                      Flavour = "Cloudberry mead brewed for feast nights on the plains. Two of these and the longhouse starts to spin.",
                      Cost = new[] { ("Honey", 5), ("Cloudberry", 3) } },
        };

        internal static readonly int TipsyHash = "SE_SkalTipsy".GetStableHashCode(), HangoverHash = "SE_SkalHangover".GetStableHashCode(), SkalHash = "SE_SkalBuff".GetStableHashCode();

        private static GameObject _holder;
        private static bool _items, _recipes;
        private static readonly Dictionary<string, GameObject> Made = new Dictionary<string, GameObject>();
        private static readonly List<GameObject> ItemPrefabs = new List<GameObject>();
        private static readonly List<StatusEffect> Effects = new List<StatusEffect>();
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

        private static Dictionary<int, GameObject> Named(ZNetScene scene) =>
            (Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene);

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

            if (scene != null)
            {
                Dictionary<int, GameObject> named = Named(scene);
                foreach (GameObject prefab in Made.Values)
                {
                    if (!scene.m_prefabs.Contains(prefab)) scene.m_prefabs.Add(prefab);
                    named[prefab.name.GetStableHashCode()] = prefab; // lets it be dropped and loaded from saves
                }
            }

            if (db != null)
            {
                if (!_recipes) { CraftingStation cauldron = Find("piece_cauldron")?.GetComponent<CraftingStation>(); if (cauldron != null) { BuildRecipes(cauldron); _recipes = true; } }
                bool changed = false;
                foreach (GameObject item in ItemPrefabs)
                    if (!db.m_items.Contains(item)) { db.m_items.RemoveAll(i => i == null || i.name == item.name); db.m_items.Add(item); changed = true; }
                foreach (StatusEffect effect in Effects)
                    if (!db.m_StatusEffects.Contains(effect)) { db.m_StatusEffects.RemoveAll(e => e == null || e.name == effect.name); db.m_StatusEffects.Add(effect); changed = true; }
                foreach (Recipe recipe in Recipes)
                    if (!db.m_recipes.Contains(recipe)) { db.m_recipes.RemoveAll(r => r == null || r.name == recipe.name); db.m_recipes.Add(recipe); }
                if (changed) AccessTools.Method(typeof(ObjectDB), "UpdateRegisters").Invoke(db, null);
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
            }
            ObjectDB db = ObjectDB.instance;
            if (db != null)
            {
                db.m_items.RemoveAll(i => i == null || ItemPrefabs.Contains(i));
                db.m_StatusEffects.RemoveAll(e => e == null || Effects.Contains(e));
                db.m_recipes.RemoveAll(r => r == null || Recipes.Contains(r));
                AccessTools.Method(typeof(ObjectDB), "UpdateRegisters").Invoke(db, null);
            }
            foreach (Recipe r in Recipes) Object.Destroy(r);
            foreach (StatusEffect e in Effects) Object.Destroy(e);
            Made.Clear(); ItemPrefabs.Clear(); Effects.Clear(); Recipes.Clear();
            _items = _recipes = _rebound = false;
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
                var effect = ScriptableObject.CreateInstance<SE_Drink>();
                effect.name = "SE_" + d.Prefab;
                effect.m_name = d.Display;
                effect.m_icon = icon;
                effect.m_ttl = 1f;
                effect.Amount = d.Amount;
                Effects.Add(effect);
                MakeItem(source, d, icon, effect);
            }

            var tipsy = ScriptableObject.CreateInstance<SE_Tipsy>();
            tipsy.name = "SE_SkalTipsy"; tipsy.m_name = "Tipsy"; tipsy.m_icon = Tint(_mug, new Color(0.9f, 0.6f, 0.2f));
            var hangover = ScriptableObject.CreateInstance<SE_Hangover>();
            hangover.name = "SE_SkalHangover"; hangover.m_name = "Hangover"; hangover.m_icon = Tint(_mug, new Color(0.45f, 0.5f, 0.4f));
            var skal = ScriptableObject.CreateInstance<SE_Skal>();
            skal.name = "SE_SkalBuff"; skal.m_name = "Skal!"; skal.m_icon = Tint(_mug, new Color(1f, 0.85f, 0.3f));
            Effects.Add(tipsy); Effects.Add(hangover); Effects.Add(skal);
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
            s.m_description = d.Flavour + "\n\nGetting drunk: a little warms you and gives some stamina; more makes the world sway; too much and you fall.";
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

        private static void BuildRecipes(CraftingStation cauldron)
        {
            foreach (Def d in All)
            {
                var cost = new List<Piece.Requirement>();
                foreach ((string item, int count) in d.Cost)
                {
                    ItemDrop drop = Find(item)?.GetComponent<ItemDrop>();
                    if (drop == null) { Plugin.Log.LogWarning($"{d.Display}: the item {item} was not found, so it has no recipe"); cost.Clear(); break; }
                    cost.Add(new Piece.Requirement { m_resItem = drop, m_amount = count, m_recover = false });
                }
                if (cost.Count == 0) continue;
                Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
                recipe.name = "Recipe_" + d.Prefab;
                recipe.m_item = Made[d.Prefab].GetComponent<ItemDrop>();
                recipe.m_amount = d.Makes;
                recipe.m_craftingStation = cauldron;
                recipe.m_minStationLevel = 1;
                recipe.m_resources = cost.ToArray();
                Recipes.Add(recipe);
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
