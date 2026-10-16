using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Webslinger
{
    /// <summary>
    /// The Spider-Viking suit (and its Venom twin, Style.Venom): an item worn in the trinket slot that changes how the player looks, not what they wear. The armour stays
    /// equipped (its armour, its effects) but the body shows the suit instead: a red and blue web suit painted on the body, with a mask
    /// and a spider's white eyes. The player's own helmet and cape stay, dyed in the suit's colours (the cape with a red spider on its back).
    /// The Venom suit is the same in symbiote black: a huge white spider, jagged white eyes and fangs, the helmet and cape blackened (a
    /// white spider on the cape), and black webs (Look.cs, Fx.cs ask IsVenom).
    ///
    /// How: when the game shows a player's equipment (Humanoid.SetupVisEquipment), a player wearing the suit gets the suit's own pieces in
    /// the chest and legs places instead: hidden items of our own (never made, never dropped) that the game draws like any armour, the
    /// chest piece's armour material carrying the painted body texture. The game writes them into the player's ZDO, so other players with
    /// the mod see the suit; without the mod they find no such item and see the body bare. The helmet and cape are the player's own, and
    /// each game dyes what it draws of them (VisEquipment's helmet and shoulder pieces) for a player whose trinket is the suit.
    ///
    /// The body texture is painted here, once, from the game's own player body: every triangle of its mesh is drawn into its place in the
    /// texture, each texel coloured by where it sits on the body (its position in the bind pose, the bone that moves it). So the web lines
    /// radiate from the chest, the gloves end at the wrists and the boots at the ankles, whatever the texture's layout. The body is
    /// mirrored (both sides share one half of the texture), and so is the suit.
    /// </summary>
    internal static class Suit
    {
        internal enum Style { Spider, Venom }
        private static readonly Style[] Styles = { Style.Spider, Style.Venom };

        public const string PrefabName = "webslinger_suit", VenomName = "webslinger_suit_venom";
        public const string NameKey = "$webslinger_suit", VenomKey = "$webslinger_suit_venom";
        private const string LegsName = "webslinger_suit_legs";
        private static readonly string[] SuitNames = { PrefabName, VenomName }, SuitKeys = { NameKey, VenomKey },
            ChestNames = { "webslinger_suit_chest", "webslinger_suit_venom_chest" };
        private static readonly int LegsHash = LegsName.GetStableHashCode();
        private static readonly int[] SuitHashes = { PrefabName.GetStableHashCode(), VenomName.GetStableHashCode() },
            ChestHashes = { "webslinger_suit_chest".GetStableHashCode(), "webslinger_suit_venom_chest".GetStableHashCode() };

        private static GameObject _holder;
        private static readonly Dictionary<string, GameObject> Items = new Dictionary<string, GameObject>();
        private static readonly Recipe[] Recipes = new Recipe[2];
        private static readonly Texture2D[] BodyTex = new Texture2D[2];
        private static Texture2D _clearTex;
        private static readonly List<Object> Made = new List<Object>();   // textures and materials made here (destroyed on unload)

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> TrinketItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_trinketItem");
        private static readonly System.Reflection.MethodInfo SetupVis = AccessTools.Method(typeof(Humanoid), "SetupVisEquipment");

        /// <summary>Which suit an item is, if any.</summary>
        public static Style? StyleOf(ItemDrop.ItemData item)
        {
            if (item == null) return null;
            for (int i = 0; i < 2; i++)
                if ((item.m_dropPrefab != null && item.m_dropPrefab.name == SuitNames[i]) || item.m_shared?.m_name == SuitKeys[i]) return Styles[i];
            return null;
        }

        public static bool IsSuit(ItemDrop.ItemData item) => StyleOf(item) != null;

        public static bool Wears(Humanoid h) => h != null && IsSuit(TrinketItem(h));

        /// <summary>A player shows the Venom suit (any player: what their game shows, read from their ZDO): their webs are black.</summary>
        public static bool IsVenom(Player p) => p != null && StyleShown(p.GetComponent<VisEquipment>()) == Style.Venom;

        public static GameObject Current(string name) => name != null && Items.TryGetValue(name, out GameObject go) ? go : null;
        public static bool Registered => Items.ContainsKey(PrefabName);

        // ---- registering ----

        public static void Register()
        {
            ZNetScene scene = ZNetScene.instance;
            ObjectDB db = ObjectDB.instance;
            if (scene == null) return;   // (copied from the game's items, which the scene knows: ZNetScene.Awake calls this again)
            if (Items.Count == 0)
            {
                if (_holder == null)
                {
                    _holder = new GameObject("WebslingerSuitPrefabs");
                    _holder.SetActive(false);   // keeps the copies from waking up as real objects
                    Object.DontDestroyOnLoad(_holder);
                }
                try { Build(scene); }
                catch (Exception e) { Plugin.Log.LogError("could not build the Spider-Viking suit: " + e); Items.Clear(); return; }
            }
            ItemReload.Replace(scene, db, Items.Values);
            if (db == null) return;
            GameObject bench = scene.GetPrefab("piece_workbench");
            CraftingStation station = bench != null ? bench.GetComponent<CraftingStation>() : null;
            Recipes[0] = DHack.Shared.Content.Recipe(Items[PrefabName], station, 2, 1, null, Plugin.SuitCost.Value, Plugin.Log, db: db);
            Recipes[1] = DHack.Shared.Content.Recipe(Items[VenomName], station, 2, 1, null, Plugin.VenomCost.Value, Plugin.Log, db: db);
            foreach (Recipe r in Recipes) Plugin.Synced.NeedsServer(r);
        }

        public static void ApplyCost()
        {
            if (Recipes[0] != null) DHack.Shared.Costs.Apply(Recipes[0], Plugin.SuitCost.Value, Plugin.Log);
            if (Recipes[1] != null) DHack.Shared.Costs.Apply(Recipes[1], Plugin.VenomCost.Value, Plugin.Log);
        }

        /// <summary>The local player's look again (after a reload, or before unloading: back to their real armour).</summary>
        public static void Refresh()
        {
            foreach (Player other in Player.GetAllPlayers()) Redraw(other != null ? other.GetComponent<VisEquipment>() : null);   // capes and helmets: dyed or not, by this copy
            Player p = Player.m_localPlayer;
            VisEquipment vis = p != null ? p.GetComponent<VisEquipment>() : null;
            if (vis == null || SetupVis == null) return;
            try { SetupVis.Invoke(p, new object[] { vis, false }); } catch (Exception e) { Plugin.Log.LogWarning("suit refresh: " + e.Message); }
        }

        /// <summary>Every suit in the local player's bag gets this copy's prefab and data (one the give command made has its own copy).</summary>
        public static void RefreshBag()
        {
            if (Player.m_localPlayer == null) return;
            foreach (ItemDrop.ItemData item in Player.m_localPlayer.GetInventory().GetAllItems())
            {
                Style? style = StyleOf(item);
                if (style == null || !Items.TryGetValue(SuitNames[(int)style], out GameObject suit)) continue;
                item.m_dropPrefab = suit;
                item.m_shared = suit.GetComponent<ItemDrop>().m_itemData.m_shared;
            }
        }

        public static void Unregister()
        {
            bool keep = ItemReload.KeepOnUnload;   // inside a world: the pieces stay registered and alive, the next copy replaces them
            Recipes[0] = Recipes[1] = null;
            if (!keep)
            {
                ZNetScene scene = ZNetScene.instance;
                ObjectDB db = ObjectDB.instance;
                foreach (GameObject item in Items.Values)
                {
                    if (item == null) continue;
                    if (scene != null)
                    {
                        scene.m_prefabs.Remove(item);
                        var named = (Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene);
                        int hash = item.name.GetStableHashCode();
                        if (named.TryGetValue(hash, out GameObject current) && current == item) named.Remove(hash);
                    }
                    db?.m_items.RemoveAll(i => i == null || i == item);
                }
                if (db != null) AccessTools.Method(typeof(ObjectDB), "UpdateRegisters")?.Invoke(db, null);
                if (_holder != null) Object.Destroy(_holder);
                foreach (Object o in Made) if (o != null) Object.Destroy(o);
            }
            Made.Clear();
            Dyed.Clear();
            Items.Clear();
            _holder = null;
            BodyTex[0] = BodyTex[1] = null;
            _clearTex = null;
        }

        // While the suit is worn, the suit's pieces are shown in the armour's places (the armour itself stays equipped).
        [HarmonyPatch(typeof(Humanoid), "SetupVisEquipment")]
        private static class Humanoid_SetupVisEquipment
        {
            private static void Postfix(Humanoid __instance, VisEquipment visEq)
            {
                Style? style = __instance is Player ? StyleOf(TrinketItem(__instance)) : null;
                if (visEq == null || style == null || !Items.ContainsKey(ChestNames[(int)style])) return;
                visEq.SetChestItem(ChestHashes[(int)style]);
                visEq.SetLegItem(LegsHash);
            }
        }

        // ---- the items ----

        private static void Build(ZNetScene scene)
        {
            bool draw = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            foreach (Style style in Styles)
            {
                if (draw)
                {
                    try { PaintBody(scene, style); }
                    catch (Exception e) { Plugin.Log.LogWarning("could not paint the " + style + " suit: " + e); }
                }
                // the chest: the painted body (everything is in its texture), nothing hung on the bones
                GameObject chest = Copy(scene, "ArmorTrollLeatherChest", ChestNames[(int)style]);
                Strip(chest, keepAttach: false);
                ItemDrop.ItemData.SharedData cs = Hidden(chest, style);
                if (cs.m_armorMaterial != null)
                {
                    var m = new Material(cs.m_armorMaterial) { name = ChestNames[(int)style] };
                    Made.Add(m);
                    Texture2D tex = BodyTex[(int)style];
                    m.SetTexture("_ChestTex", tex != null ? tex : cs.m_armorMaterial.GetTexture("_ChestTex"));
                    m.SetTexture("_ChestBumpMap", null);
                    m.SetTexture("_ChestMetal", null);
                    cs.m_armorMaterial = m;
                }
            }

            // the legs: nothing of their own (the body texture has them), only so the real greaves aren't drawn over it
            GameObject legs = Copy(scene, "ArmorTrollLeatherLegs", LegsName);
            Strip(legs, keepAttach: false);
            ItemDrop.ItemData.SharedData ls = Hidden(legs, Style.Spider);
            if (ls.m_armorMaterial != null)
            {
                var m = new Material(ls.m_armorMaterial) { name = "webslinger_suit_legs" };
                Made.Add(m);
                m.SetTexture("_LegsTex", Clear());
                m.SetTexture("_LegsBumpMap", null);
                m.SetTexture("_LegsMetal", null);
                ls.m_armorMaterial = m;
            }

            foreach (Style style in Styles) BuildSuit(scene, style, draw);
        }

        /// <summary>The suit itself: a trinket, folded on the ground.</summary>
        private static void BuildSuit(ZNetScene scene, Style style, bool draw)
        {
            string name = SuitNames[(int)style], key = SuitKeys[(int)style];
            GameObject suit = Copy(scene, "TrinketBronzeHealth", name);
            for (int i = suit.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(suit.transform.GetChild(i).gameObject);
            foreach (Collider c in suit.GetComponents<Collider>()) Object.DestroyImmediate(c);
            GameObject folded = scene.GetPrefab("ArmorTrollLeatherChest")?.transform.Find("log")?.gameObject;
            var look = new GameObject("attach") { layer = suit.layer };
            look.transform.SetParent(suit.transform, false);
            if (folded != null)
            {
                GameObject f = Object.Instantiate(folded, look.transform, false);
                f.name = "suit";
                Color dye = style == Style.Venom ? new Color(0.1f, 0.1f, 0.13f) : Red * 1.5f;
                if (draw) foreach (Renderer r in f.GetComponentsInChildren<Renderer>(true)) Tint(r, dye, 0.1f);   // (its own collider kept: what lies on the ground)
            }
            else suit.AddComponent<BoxCollider>().size = new Vector3(0.5f, 0.15f, 0.5f);
            ItemDrop drop = suit.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
            s.m_name = key;
            s.m_description = key + "_desc";
            s.m_itemType = ItemDrop.ItemData.ItemType.Trinket;
            s.m_equipStatusEffect = null;
            s.m_fullAdrenalineSE = null;
            s.m_maxAdrenaline = 0f;
            s.m_setStatusEffect = null;
            s.m_setName = "";
            s.m_setSize = 0;
            s.m_armor = 0f;
            s.m_armorPerLevel = 0f;
            s.m_weight = 2f;
            s.m_value = 0;   // never sold: the trader buys anything worth more than 0
            s.m_maxStackSize = 1;
            s.m_maxQuality = 1;
            s.m_variants = 0;
            s.m_useDurability = false;
            s.m_teleportable = true;
            s.m_questItem = false;
            s.m_dlc = "";
            drop.m_itemData.m_dropPrefab = suit;
            drop.m_itemData.m_stack = 1;
            Items[name] = suit;
            if (draw) DHack.Shared.Icon.MakeItem(suit, setIcon: true);
        }

        // ---- the player's own cape and helmet, dyed ----

        private static readonly AccessTools.FieldRef<VisEquipment, int> ShownTrinket = AccessTools.FieldRefAccess<VisEquipment, int>("m_currentTrinketItemHash");
        private static readonly AccessTools.FieldRef<VisEquipment, int> ShownShoulder = AccessTools.FieldRefAccess<VisEquipment, int>("m_currentShoulderItemHash");
        private static readonly AccessTools.FieldRef<VisEquipment, int> ShownHelmet = AccessTools.FieldRefAccess<VisEquipment, int>("m_currentHelmetItemHash");
        private static readonly AccessTools.FieldRef<VisEquipment, List<GameObject>> ShoulderShown = AccessTools.FieldRefAccess<VisEquipment, List<GameObject>>("m_shoulderItemInstances");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> HelmetShown = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_helmetItemInstance");
        private static readonly Dictionary<(Material, Mesh, bool, Style), Material> Dyed = new Dictionary<(Material, Mesh, bool, Style), Material>();

        /// <summary>Which suit this player shows, if any (their trinket as shown, which other players' games read from the ZDO; or the test preview).</summary>
        private static Style? StyleShown(VisEquipment vis)
        {
            if (vis == null) return null;
            int shown = ShownTrinket(vis);
            for (int i = 0; i < 2; i++) if (shown == SuitHashes[i]) return Styles[i];
            if (Preview != null && Player.m_localPlayer != null && ReferenceEquals(vis, Player.m_localPlayer.GetComponent<VisEquipment>())) return Preview;
            return null;
        }

        [HarmonyPatch(typeof(VisEquipment), "SetShoulderEquipped")]
        private static class VisEquipment_SetShoulderEquipped
        {
            private static void Postfix(VisEquipment __instance, bool __result)
            {
                Style? style = __result ? StyleShown(__instance) : null;
                if (style == null) return;
                List<GameObject> shown = ShoulderShown(__instance);
                if (shown != null) foreach (GameObject g in shown) Dye(g, cape: true, style.Value);
            }
        }

        [HarmonyPatch(typeof(VisEquipment), "SetHelmetEquipped")]
        private static class VisEquipment_SetHelmetEquipped
        {
            private static void Postfix(VisEquipment __instance, bool __result)
            {
                Style? style = __result ? StyleShown(__instance) : null;
                if (style != null) Dye(HelmetShown(__instance), cape: false, style.Value);
            }
        }

        // The suit put on or taken off: the cape and helmet are drawn again (fresh from their items, then dyed or not).
        [HarmonyPatch(typeof(VisEquipment), "SetTrinketEquipped")]
        private static class VisEquipment_SetTrinketEquipped
        {
            private static void Postfix(VisEquipment __instance, bool __result)
            {
                if (__result) Redraw(__instance);
            }
        }

        private static void Redraw(VisEquipment vis)
        {
            if (vis == null) return;
            ShownShoulder(vis) = int.MinValue;   // (no item has this hash: the next update draws them again)
            ShownHelmet(vis) = int.MinValue;
        }

        private static void Dye(GameObject shown, bool cape, Style style)
        {
            if (shown == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            try
            {
                foreach (Renderer r in shown.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.GetType().Name == "ParticleSystemRenderer" || r is LineRenderer || r is TrailRenderer) continue;
                    Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        Material src = mats[i];
                        if (src == null || src.name.EndsWith("(webslinger)")) continue;
                        var key = (src, cape ? mesh : null, cape, style);
                        if (!Dyed.TryGetValue(key, out Material dyed) || dyed == null)
                        {
                            bool venom = style == Style.Venom;
                            dyed = cape ? DyeMaterial(src, venom ? VenomCape : CapeColour, venom ? VenomCapeKeep : CapeKeep)
                                : DyeMaterial(src, venom ? VenomHelm : HelmColour, venom ? VenomHelmKeep : HelmKeep);
                            if (cape && r is SkinnedMeshRenderer && mesh != null)
                                try { CapeSpider(mesh, dyed, style); } catch (Exception e) { Plugin.Log.LogWarning("the cape's spider: " + e.Message); }
                            Dyed[key] = dyed;
                        }
                        mats[i] = dyed;
                    }
                    r.sharedMaterials = mats;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("could not dye " + shown.name + ": " + e.Message); }
        }

        /// <summary>
        /// A big red spider on the cape's back, as on the suit's back in the comics: drawn into the dyed cape's texture where the cape's mesh
        /// (as it hangs in its bind pose) is in the middle of its width, a third of the way down.
        /// </summary>
        private static void CapeSpider(Mesh mesh, Material mat, Style style)
        {
            Texture2D tex = mat != null ? mat.GetTexture("_MainTex") as Texture2D : null;
            if (mesh == null || !mesh.isReadable || tex == null || !Made.Contains(tex)) return;
            Vector3[] v = mesh.vertices;
            Vector2[] uv = mesh.uv;
            Bounds b = mesh.bounds;
            Vector3 e = b.extents;
            // the cape's height is its longest side, its width the next
            int up = e.x >= e.y && e.x >= e.z ? 0 : e.y >= e.z ? 1 : 2;
            int side = up == 0 ? (e.y >= e.z ? 1 : 2) : up == 1 ? (e.x >= e.z ? 0 : 2) : (e.x >= e.y ? 0 : 1);
            float metres = 1.25f / Mathf.Max(1e-6f, e[up] * 2f);
            float top = CapeSpiderFlip ? b.min[up] : b.max[up], downSign = CapeSpiderFlip ? 1f : -1f;
            Vector2 centre = new Vector2(b.center[side], top + downSign * e[up] * 2f * CapeSpiderDown);
            int w = tex.width, h = tex.height;
            Color32[] px = tex.GetPixels32();
            Color red = style == Style.Venom ? new Color(0.62f, 0.62f, 0.65f) : new Color(0.78f, 0.09f, 0.07f);
            float size = style == Style.Venom ? CapeSpiderSize * 1.3f : CapeSpiderSize;
            int[] tris = mesh.triangles;
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 A = v[tris[t]], B = v[tris[t + 1]], C = v[tris[t + 2]];
                Vector2 a = uv[tris[t]], bb = uv[tris[t + 1]], c = uv[tris[t + 2]];
                RasterAt(w, h, a, bb, c, (x, y, w0, w1, w2) =>
                {
                    Vector3 P = A * w0 + B * w1 + C * w2;
                    Vector2 q = new Vector2(P[side] - centre.x, (P[up] - centre.y) * -downSign) * metres;
                    if (!Spider(q, size)) return;
                    int k = y * w + x;
                    Color under = px[k];
                    float lum = Mathf.Clamp(under.r * 0.3f + under.g * 0.55f + under.b * 0.15f, 0.25f, 1f);
                    Color o = red * Mathf.Lerp(0.8f, 1.6f, lum);
                    o.a = under.a;
                    px[k] = o;
                });
            }
            tex.SetPixels32(px);
            tex.Apply(true);
        }

        /// <summary>For trying looks while playing (web suit cape / tune): the cape or the painting made again, and the player shown with it.</summary>
        internal static string Rebuild(bool cape, bool paint)
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null || !Items.ContainsKey(ChestNames[0])) return "not built";
            if (cape) Dyed.Clear();   // (dyed again in the new colours as they are drawn)
            if (paint)
            {
                foreach (Style style in Styles)
                {
                    PaintBody(scene, style);
                    Items[ChestNames[(int)style]].GetComponent<ItemDrop>().m_itemData.m_shared.m_armorMaterial.SetTexture("_ChestTex", BodyTex[(int)style]);
                }
            }
            // shown again: the game only redraws a piece whose item changes
            Player p = Player.m_localPlayer;
            VisEquipment vis = p != null ? p.GetComponent<VisEquipment>() : null;
            if (vis != null)
            {
                vis.SetChestItem(0);
                AccessTools.Method(typeof(VisEquipment), "UpdateEquipmentVisuals")?.Invoke(vis, null);
                Refresh();
                AccessTools.Method(typeof(VisEquipment), "UpdateEquipmentVisuals")?.Invoke(vis, null);
            }
            return "rebuilt";
        }

        private static GameObject Copy(ZNetScene scene, string source, string name)
        {
            GameObject src = scene.GetPrefab(source);
            if (src == null) throw new Exception("the game's " + source + " was not found");
            GameObject go = Object.Instantiate(src, _holder.transform);
            go.name = name;
            go.GetComponent<ItemDrop>().m_itemData.m_dropPrefab = go;
            Items[name] = go;
            return go;
        }

        /// <summary>A piece only ever shown on a player: never made, sold or kept.</summary>
        private static ItemDrop.ItemData.SharedData Hidden(GameObject go, Style style)
        {
            ItemDrop drop = go.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
            s.m_name = SuitKeys[(int)style];
            s.m_description = SuitKeys[(int)style] + "_desc";
            s.m_value = 0;
            s.m_armor = 0f;
            s.m_armorPerLevel = 0f;
            s.m_equipStatusEffect = null;
            s.m_setStatusEffect = null;
            s.m_setName = "";
            s.m_setSize = 0;
            s.m_dlc = "";
            drop.m_itemData.m_stack = 1;
            return s;
        }

        private static void Strip(GameObject go, bool keepAttach)
        {
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                Transform c = go.transform.GetChild(i);
                if (c.name.StartsWith("attach") && !keepAttach) Object.DestroyImmediate(c.gameObject);
            }
        }

        /// <summary>A renderer's materials dyed (DyeMaterial).</summary>
        private static void Tint(Renderer r, Color colour, float keep)
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) if (mats[i] != null) mats[i] = DyeMaterial(mats[i], colour, keep);
            r.sharedMaterials = mats;
        }

        /// <summary>A copy of a material dyed: each texel's brightness times the colour (keeps the leather's, fur's or metal's detail); keep: how
        /// much of the old colour stays.</summary>
        private static Material DyeMaterial(Material src, Color colour, float keep)
        {
            {
                var m = new Material(src) { name = src.name + " (webslinger)" };
                Made.Add(m);
                Texture main = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
                if (main != null)
                {
                    Texture2D t = SuitProbe.Read(main, Mathf.Clamp(Mathf.Max(main.width, main.height), 64, 1024));
                    Color32[] px = t.GetPixels32();
                    for (int k = 0; k < px.Length; k++)
                    {
                        Color c = px[k];
                        float lum = c.r * 0.3f + c.g * 0.55f + c.b * 0.15f;
                        Color dyed = colour * lum;
                        Color o = Color.Lerp(dyed, c, keep);
                        o.a = c.a;
                        px[k] = o;
                    }
                    t.SetPixels32(px);
                    t.Apply(true);
                    t.name = main.name + "_webslinger";
                    Made.Add(t);
                    m.SetTexture("_MainTex", t);
                }
                else if (m.HasProperty("_Color")) m.color = colour;
                return m;
            }
        }

        private static Texture2D Clear()
        {
            if (_clearTex != null) return _clearTex;
            _clearTex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "webslinger_clear" };
            var px = new Color32[16];
            _clearTex.SetPixels32(px);
            _clearTex.Apply();
            Made.Add(_clearTex);
            return _clearTex;
        }

        // ---- the body texture ----

        private static readonly Color Red = new Color(0.60f, 0.06f, 0.05f), Blue = new Color(0.07f, 0.12f, 0.33f), Line = new Color(0.05f, 0.03f, 0.03f),
            Leather = new Color(0.30f, 0.18f, 0.10f), DarkLeather = new Color(0.17f, 0.10f, 0.06f), Fur = new Color(0.55f, 0.48f, 0.38f),
            Gold = new Color(0.85f, 0.66f, 0.26f),
            Symbiote = new Color(0.03f, 0.032f, 0.045f), Sheen = new Color(0.10f, 0.13f, 0.22f), White = new Color(0.94f, 0.94f, 0.96f),
            BlackLeather = new Color(0.07f, 0.06f, 0.06f), Silver = new Color(0.7f, 0.72f, 0.76f), DarkFur = new Color(0.2f, 0.2f, 0.22f);
        private const int Size = 1024;

        // what the painting can be tuned by (web suit tune): the eyes on the mask, the spiders' size
        internal static float EyeUp = 0.108f, EyeSide = 0.034f, EyeWide = 0.026f, EyeTall = 0.02f, EyeTilt = 25f, SpiderFront = 1.7f, SpiderBack = 2.3f;
        internal static Color CapeColour = new Color(0.2f, 0.32f, 0.85f), HelmColour = new Color(0.95f, 0.11f, 0.09f);
        internal static float HelmKeep = 0.35f;
        internal static float CapeKeep = 0.15f;
        internal static float CapeSpiderSize = 2.6f, CapeSpiderDown = 0.38f;
        internal static bool CapeSpiderFlip;
        internal static Color VenomCape = new Color(0.13f, 0.13f, 0.18f), VenomHelm = new Color(0.2f, 0.2f, 0.24f);
        internal static float VenomCapeKeep = 0.05f, VenomHelmKeep = 0.2f;

        private enum Part { Head, Torso, Hips, UpperArm, ForeArm, Hand, Thigh, Shin, Foot }

        /// <summary>The body in metres, as the painting sees it: x to the side, f forward, h up from the soles.</summary>
        private sealed class Frame
        {
            public float Scale, MinZ, Front;
            public Dictionary<string, Vector3> Joints = new Dictionary<string, Vector3>();
            public Vector3 P(Vector3 v) => new Vector3(v.x * Scale, v.y * Scale * Front, (v.z - MinZ) * Scale);
            public Vector3 N(Vector3 n) => new Vector3(n.x, n.y * Front, n.z).normalized;
            public Vector3 J(string name) => Joints.TryGetValue(name, out Vector3 j) ? j : Vector3.zero;
        }

        private static void PaintBody(ZNetScene scene, Style style)
        {
            GameObject player = scene.GetPrefab("Player");
            SkinnedMeshRenderer body = player != null ? player.GetComponent<VisEquipment>()?.m_bodyModel : null;
            Mesh mesh = body != null ? body.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) { Plugin.Log.LogWarning("the player's body mesh can't be read: the suit keeps the troll leather look"); return; }

            Vector3[] v = mesh.vertices, nrm = mesh.normals;
            Vector2[] uv = mesh.uv;
            BoneWeight[] w = mesh.boneWeights;
            Transform[] bones = body.bones;
            Matrix4x4[] bind = mesh.bindposes;

            // the frame: height along z, metres from the body's height, forward the way the toes point
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Vector3 q in v) { minZ = Mathf.Min(minZ, q.z); maxZ = Mathf.Max(maxZ, q.z); }
            var fr = new Frame { MinZ = minZ, Scale = 1.85f / Mathf.Max(1e-6f, maxZ - minZ), Front = 1f };
            float toeY = 0f, footY = 0f; int toes = 0, feet = 0;
            for (int i = 0; i < v.Length; i++)
            {
                string bn = BoneName(bones, w[i].boneIndex0);
                if (bn.Contains("Toe")) { toeY += v[i].y; toes++; }
                else if (bn.Contains("Foot")) { footY += v[i].y; feet++; }
            }
            if (toes > 0 && feet > 0 && toeY / toes < footY / feet) fr.Front = -1f;
            for (int i = 0; i < bones.Length && i < bind.Length; i++)
                if (bones[i] != null) fr.Joints[bones[i].name] = fr.P(bind[i].inverse.MultiplyPoint3x4(Vector3.zero));

            // the skin's shading (muscles, face) shows through a little, as a suit over a body
            Texture skinTex = body.sharedMaterial != null ? body.sharedMaterial.GetTexture("_MainTex") : null;
            Texture2D skin = skinTex != null ? SuitProbe.Read(skinTex, Size) : null;
            Color32[] skinPx = skin != null ? skin.GetPixels32() : null;
            if (skin != null) Object.Destroy(skin);

            var col = new Color[Size * Size];
            var filled = new bool[Size * Size];
            int[] tris = mesh.GetTriangles(0);
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                Part pa = PartOf(BoneName(bones, w[a].boneIndex0)), pb = PartOf(BoneName(bones, w[b].boneIndex0)), pc = PartOf(BoneName(bones, w[c].boneIndex0));
                string ba = BoneName(bones, w[a].boneIndex0), bb = BoneName(bones, w[b].boneIndex0), bc = BoneName(bones, w[c].boneIndex0);
                Vector3 Pa = fr.P(v[a]), Pb = fr.P(v[b]), Pc = fr.P(v[c]);
                Vector3 Na = fr.N(nrm[a]), Nb = fr.N(nrm[b]), Nc = fr.N(nrm[c]);
                Raster(uv[a], uv[b], uv[c], (x, y, w0, w1, w2) =>
                {
                    Vector3 P = Pa * w0 + Pb * w1 + Pc * w2;
                    Vector3 N = (Na * w0 + Nb * w1 + Nc * w2).normalized;
                    bool first = w0 >= w1 && w0 >= w2, second = w1 >= w2;
                    Part part = first ? pa : second ? pb : pc;
                    string bone = first ? ba : second ? bb : bc;
                    int k = y * Size + x;
                    col[k] = style == Style.Venom ? VenomColour(fr, part, bone, P, N) : Colour(fr, part, bone, P, N);
                    filled[k] = true;
                });
            }

            // shading and a little weave, then grow the edges a few texels (no skin showing at the seams)
            float avg = 0f; int n = 0;
            if (skinPx != null)
            {
                for (int k = 0; k < col.Length; k++) if (filled[k]) { avg += Lum(skinPx[k]); n++; }
                avg = n > 0 ? avg / n : 1f;
            }
            for (int k = 0; k < col.Length; k++)
            {
                if (!filled[k]) continue;
                float shade = skinPx != null && avg > 0.01f ? Mathf.Clamp(Lum(skinPx[k]) / avg, 0.7f, 1.25f) : 1f;
                float weave = 0.96f + 0.08f * Hash(k);
                Color c = col[k] * Mathf.Lerp(1f, shade, 0.55f) * weave;
                c.a = 1f;
                col[k] = c;
            }
            for (int pass = 0; pass < 4; pass++) Grow(col, filled);

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = ChestNames[(int)style], wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            tex.SetPixels(col);
            tex.Apply(true);
            Made.Add(tex);
            if (BodyTex[(int)style] != null && Made.Contains(BodyTex[(int)style])) { Object.Destroy(BodyTex[(int)style]); Made.Remove(BodyTex[(int)style]); }
            BodyTex[(int)style] = tex;
            Painted = fr;
            Plugin.Log.LogInfo("painted the " + style + " suit");
        }

        internal static object Painted;   // (for web suit status)

        private static string BoneName(Transform[] bones, int i) => i >= 0 && i < bones.Length && bones[i] != null ? bones[i].name : "";

        private static Part PartOf(string n)
        {
            if (n.Contains("Head") || n.Contains("Jaw") || n.Contains("Neck")) return Part.Head;
            if (n.Contains("Hand")) return Part.Hand;
            if (n.Contains("ForeArm")) return Part.ForeArm;
            if (n.Contains("Arm")) return Part.UpperArm;
            if (n.Contains("Shoulder") || n.Contains("Spine")) return Part.Torso;
            if (n.Contains("UpLeg")) return Part.Thigh;
            if (n.Contains("Foot") || n.Contains("Toe")) return Part.Foot;
            if (n.Contains("Leg")) return Part.Shin;
            return Part.Hips;
        }

        /// <summary>The suit's colour at a point of the body.</summary>
        private static Color Colour(Frame fr, Part part, string bone, Vector3 P, Vector3 N)
        {
            Vector3 neck = fr.J("Neck"), chest = fr.J("Spine2"), spine = fr.J("Spine"), hips = fr.J("Hips");
            float x = Mathf.Abs(P.x), f = P.y - chest.y, h = P.z;   // (f: forward of the spine)
            switch (part)
            {
                case Part.Head:
                {
                    if (bone.Contains("Neck") && h < neck.z + 0.02f) return Web2D(new Vector2(P.x, h - chest.z - 0.06f), Red, 9, 0.07f);
                    // the mask: web lines radiating from the face's middle
                    Vector3 head = fr.J("Head");
                    Vector3 centre = new Vector3(0f, head.y + 0.02f, head.z + 0.09f);
                    Vector3 d = P - centre;
                    // the eyes: white lenses rimmed in black, tilted up at their outer corners
                    if (N.y > 0.2f && P.y > head.y)
                    {
                        Vector2 e = new Vector2(x - EyeSide, h - (head.z + EyeUp));
                        e = Quaternion.Euler(0f, 0f, -EyeTilt) * e;
                        float q = e.x * e.x / (EyeWide * EyeWide) + e.y * e.y / (EyeTall * EyeTall);
                        if (q <= 1f) return new Color(0.97f, 0.97f, 0.99f);
                        float rim = (e.x * e.x) / ((EyeWide + 0.005f) * (EyeWide + 0.005f)) + (e.y * e.y) / ((EyeTall + 0.005f) * (EyeTall + 0.005f));
                        if (rim <= 1f) return Line;
                    }
                    float theta = Vector3.Angle(new Vector3(0f, 1f, 0f), d);   // from straight ahead (y is forward in this frame)
                    float psi = Mathf.Atan2(d.z, d.x);
                    return Spherical(theta, psi, Red);
                }
                case Part.Torso:
                case Part.Hips:
                {
                    float belt = hips.z + 0.06f;
                    if (Mathf.Abs(h - belt) < 0.028f)
                    {
                        // a leather belt with a gold buckle
                        if (f > 0f && x < 0.04f && Mathf.Abs(h - belt) < 0.024f) return Mathf.Abs(h - belt) > 0.016f || x > 0.03f ? Gold : Gold * 0.55f;
                        return Mathf.Abs(Mathf.Abs(h - belt) - 0.022f) < 0.003f ? DarkLeather : Leather;
                    }
                    if (h < belt) return Blue;
                    // red down the middle, front and back, widening to the shoulders; blue sides under the arms
                    float wide = Mathf.Lerp(0.075f, 0.17f, Mathf.InverseLerp(spine.z, chest.z + 0.08f, h));
                    bool red = x < wide || h > neck.z - 0.07f || bone.Contains("Shoulder");
                    if (!red) return Blue;
                    Vector2 c = new Vector2(P.x, h - (chest.z + 0.03f));
                    if (f > 0f && Spider(c, SpiderFront)) return Line;
                    if (f < 0f && Spider(c - new Vector2(0f, 0.04f), SpiderBack)) return Line;
                    return Web2D(c, Red, 12, 0.07f);
                }
                case Part.UpperArm:
                {
                    Limb(fr, bone, "ForeArm", P, out float t, out float around);
                    bool top = Vector3.Dot(N, Vector3.forward) > -0.15f;   // (z is up in this frame)
                    if (!top) return Blue;
                    return WebLimb(t, around, Red);
                }
                case Part.ForeArm:
                {
                    // a leather bracer, laced, under the web shooter
                    Limb(fr, bone, "Hand", P, out float t, out float around);
                    if (Mathf.Abs(t - 0.06f) < 0.012f || Mathf.Abs(t - 0.2f) < 0.012f) return DarkLeather;
                    if (Mathf.Abs(Mathf.Repeat(around + t * 9f, 0.5f) - 0.25f) < 0.02f && Mathf.Cos(around * Mathf.PI * 2f) > 0.6f) return DarkLeather;
                    return Leather;
                }
                case Part.Hand:
                {
                    Limb(fr, bone.StartsWith("Left") ? "LeftHand" : "RightHand", "Middle1", P, out float t, out float around);
                    return Mathf.Abs(Mathf.Repeat(t, 0.035f) - 0.0175f) < 0.0022f ? Line : Red;
                }
                case Part.Thigh:
                    return Blue;
                case Part.Shin:
                {
                    // Viking leg wraps crossing over the blue
                    Limb(fr, bone, "Foot", P, out float t, out float around);
                    float a = Mathf.Repeat(t * 7f + around, 1f), b = Mathf.Repeat(t * 7f - around, 1f);
                    if (t > 0.08f && (a < 0.09f || b < 0.09f)) return Mathf.Min(a, b) < 0.015f ? DarkLeather : Leather;
                    return Blue;
                }
                case Part.Foot:
                {
                    // fur-topped leather boots
                    Vector3 ankle = fr.J(bone.Contains("Left") ? "LeftFoot" : "RightFoot");
                    if (h > ankle.z - 0.01f) return Fur * (0.85f + 0.3f * Mathf.PerlinNoise(P.x * 160f, h * 160f + P.y * 80f));
                    if (Mathf.Abs(h - 0.035f) < 0.006f) return DarkLeather;
                    return Leather * 1.1f;
                }
            }
            return Red;
        }

        /// <summary>
        /// The Venom suit's colour at a point: symbiote black with a blue sheen in wandering veins, a huge white spider whose legs reach round
        /// the chest and back, jagged white eyes and a grin of fangs; the Viking parts in black leather, silver and grey fur.
        /// </summary>
        private static Color VenomColour(Frame fr, Part part, string bone, Vector3 P, Vector3 N)
        {
            Vector3 chest = fr.J("Spine2"), hips = fr.J("Hips");
            float x = Mathf.Abs(P.x), f = P.y - chest.y, h = P.z;
            // the symbiote: black, with veins of blue sheen (ridges of noise over the body)
            float n = Mathf.PerlinNoise(P.x * 9f + 31f, h * 7f + P.y * 9f);
            float vein = Mathf.Pow(1f - Mathf.Abs(2f * n - 1f), 7f);
            Color skin = Symbiote + Sheen * (0.25f + 0.9f * vein);
            switch (part)
            {
                case Part.Head:
                {
                    Vector3 head = fr.J("Head");
                    if (N.y > 0.2f && P.y > head.y)
                    {
                        // the eyes: big white teardrops, pointed toward the back of the head
                        Vector2 e = new Vector2(x - EyeSide - 0.004f, h - (head.z + EyeUp + 0.002f));
                        e = Quaternion.Euler(0f, 0f, -(EyeTilt + 12f)) * e;
                        float rx = EyeWide * 1.25f * (e.x > 0f ? 1.45f : 1f);
                        float ry = EyeTall * 1.3f * (1f - 0.55f * Mathf.Clamp01(e.x / rx));
                        if (e.x * e.x / (rx * rx) + e.y * e.y / (ry * ry) <= 1f) return White;
                        // the grin: a wide mouth curving up to the cheeks, white fangs from above and below
                        float mx = x / 0.055f;
                        if (mx < 1f)
                        {
                            float line = head.z + EyeUp - 0.075f + 0.022f * mx * mx;
                            float half = 0.016f * (1f - 0.55f * mx * mx), yy = h - line;
                            if (Mathf.Abs(yy) < half)
                            {
                                float tooth = 0.0075f, fr1 = Mathf.Repeat(x / tooth, 1f), fr2 = Mathf.Repeat(x / tooth + 0.5f, 1f);
                                float topLen = half * 1.25f * (1f - Mathf.Abs(2f * fr1 - 1f)), botLen = half * 1.1f * (1f - Mathf.Abs(2f * fr2 - 1f));
                                if (yy > half - topLen || yy < -half + botLen) return White;
                                return new Color(0.16f, 0.02f, 0.03f);
                            }
                        }
                    }
                    return skin;
                }
                case Part.Torso:
                case Part.Hips:
                {
                    float belt = hips.z + 0.06f;
                    if (Mathf.Abs(h - belt) < 0.028f)
                    {
                        // a black leather belt with a silver buckle
                        if (f > 0f && x < 0.04f && Mathf.Abs(h - belt) < 0.024f) return Mathf.Abs(h - belt) > 0.016f || x > 0.03f ? Silver : Silver * 0.45f;
                        return Mathf.Abs(Mathf.Abs(h - belt) - 0.022f) < 0.003f ? Symbiote : BlackLeather;
                    }
                    Vector2 c = new Vector2(P.x, h - (chest.z + 0.02f));
                    if (f > 0f && Spider(c, 3.1f)) return White;
                    if (f < 0f && Spider(c - new Vector2(0f, 0.03f), 3.5f)) return White;
                    return skin;
                }
                case Part.ForeArm:
                {
                    // a black leather bracer with silver studs, under the web shooter
                    Limb(fr, bone, "Hand", P, out float t, out float around);
                    if (Mathf.Abs(t - 0.06f) < 0.012f || Mathf.Abs(t - 0.2f) < 0.012f)
                        return Mathf.Abs(Mathf.Repeat(around * 8f, 1f) - 0.5f) < 0.12f ? Silver : Symbiote;
                    return BlackLeather;
                }
                case Part.Shin:
                {
                    // the leg wraps, in grey wool over the black
                    Limb(fr, bone, "Foot", P, out float t, out float around);
                    float a = Mathf.Repeat(t * 7f + around, 1f), b = Mathf.Repeat(t * 7f - around, 1f);
                    if (t > 0.08f && (a < 0.09f || b < 0.09f)) return Mathf.Min(a, b) < 0.015f ? Symbiote : new Color(0.22f, 0.22f, 0.24f);
                    return skin;
                }
                case Part.Foot:
                {
                    Vector3 ankle = fr.J(bone.Contains("Left") ? "LeftFoot" : "RightFoot");
                    if (h > ankle.z - 0.01f) return DarkFur * (0.8f + 0.4f * Mathf.PerlinNoise(P.x * 160f, h * 160f + P.y * 80f));
                    if (Mathf.Abs(h - 0.035f) < 0.006f) return Silver * 0.6f;
                    return BlackLeather;
                }
            }
            return skin;
        }

        /// <summary>Position along a limb (metres from its joint) and round it (turns, 0-1).</summary>
        private static void Limb(Frame fr, string from, string to, Vector3 P, out float t, out float around)
        {
            string side = from.StartsWith("Left") ? "Left" : from.StartsWith("Right") ? "Right" : (P.x < 0f ? "Left" : "Right");
            string a = from.StartsWith("Left") || from.StartsWith("Right") ? from : side + from;
            Vector3 A = fr.J(a), B = fr.J(to.StartsWith("Left") || to.StartsWith("Right") ? to : side + (to == "Middle1" ? "HandMiddle1" : to));
            Vector3 axis = B - A;
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.forward;
            Vector3 dir = axis.normalized;
            t = Vector3.Dot(P - A, dir);
            Vector3 radial = P - A - dir * t;
            Vector3 refUp = Vector3.ProjectOnPlane(Vector3.forward, dir);   // (z is up in this frame)
            if (refUp.sqrMagnitude < 0.01f) refUp = Vector3.ProjectOnPlane(Vector3.up, dir);
            refUp.Normalize();
            Vector3 refSide = Vector3.Cross(dir, refUp);
            around = Mathf.Repeat(Mathf.Atan2(Vector3.Dot(radial, refSide), Vector3.Dot(radial, refUp)) / (Mathf.PI * 2f), 1f);
        }

        /// <summary>Web lines on a limb: lines along it and sagging rings round it.</summary>
        private static Color WebLimb(float t, float around, Color under)
        {
            float lines = 6f;
            float u = around * lines, du = Mathf.Abs(u - Mathf.Round(u));
            if (du < 0.045f) return Line;
            float sag = 0.012f * Mathf.Sin(Mathf.PI * Mathf.Repeat(u, 1f));
            float ring = Mathf.Repeat(t + sag, 0.065f);
            if (Mathf.Min(ring, 0.065f - ring) < 0.0028f) return Line;
            return under;
        }

        /// <summary>Web lines on a flat-ish part: spokes from a centre, crossed by arcs that sag toward it between the spokes.</summary>
        private static Color Web2D(Vector2 p, Color under, int spokes, float spacing)
        {
            float r = p.magnitude;
            if (r < 0.004f) return Line;
            float ang = Mathf.Atan2(p.y, p.x) / (Mathf.PI * 2f) * spokes;
            float frac = Mathf.Repeat(ang, 1f);
            float toSpoke = Mathf.Min(frac, 1f - frac) / spokes * Mathf.PI * 2f * r;
            if (toSpoke < 0.0028f) return Line;
            float sag = spacing * 0.22f * Mathf.Sin(Mathf.PI * frac);
            float rr = Mathf.Repeat(r + sag, spacing);
            if (Mathf.Min(rr, spacing - rr) < 0.0028f) return Line;
            return under;
        }

        /// <summary>The mask's web: spokes from the face's middle round the head, rings at equal angles out from it.</summary>
        private static Color Spherical(float thetaDeg, float psi, Color under)
        {
            float spokes = 12f;
            float u = (psi / (Mathf.PI * 2f) + 1f) * spokes, frac = Mathf.Repeat(u, 1f);
            if (Mathf.Min(frac, 1f - frac) < 0.05f * Mathf.Clamp(40f / Mathf.Max(thetaDeg, 4f), 0.6f, 4f) && thetaDeg > 6f) return Line;
            float sag = 6f * Mathf.Sin(Mathf.PI * frac);
            float ring = Mathf.Repeat(thetaDeg + sag, 22f);
            if (Mathf.Min(ring, 22f - ring) < 1.6f) return Line;
            return under;
        }

        /// <summary>The spider: a round body and a longer back, eight legs bent at the knee (p in metres from its middle).</summary>
        private static bool Spider(Vector2 p, float scale)
        {
            p /= scale;
            if (Ellipse(p, new Vector2(0f, 0.012f), 0.014f, 0.016f)) return true;    // the front body
            if (Ellipse(p, new Vector2(0f, -0.022f), 0.012f, 0.026f)) return true;   // the back
            if (Ellipse(p, new Vector2(0f, 0.032f), 0.007f, 0.007f)) return true;    // the head
            float s = Mathf.Sign(p.x == 0f ? 1f : p.x);
            Vector2 q = new Vector2(Mathf.Abs(p.x), p.y);
            Vector2[] hips = { new Vector2(0.01f, 0.02f), new Vector2(0.012f, 0.013f), new Vector2(0.012f, 0.006f), new Vector2(0.01f, -0.002f) };
            Vector2[] knees = { new Vector2(0.04f, 0.045f), new Vector2(0.05f, 0.025f), new Vector2(0.05f, -0.005f), new Vector2(0.04f, -0.03f) };
            Vector2[] feet = { new Vector2(0.045f, 0.085f), new Vector2(0.07f, 0.055f), new Vector2(0.065f, -0.045f), new Vector2(0.045f, -0.075f) };
            for (int i = 0; i < 4; i++)
                if (Segment(q, hips[i], knees[i]) < 0.0032f || Segment(q, knees[i], feet[i]) < 0.0026f) return true;
            return false;
        }

        private static bool Ellipse(Vector2 p, Vector2 c, float rx, float ry)
        {
            Vector2 d = p - c;
            return d.x * d.x / (rx * rx) + d.y * d.y / (ry * ry) <= 1f;
        }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-9f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        private delegate void TexelFn(int x, int y, float w0, float w1, float w2);

        private static void Raster(Vector2 a, Vector2 b, Vector2 c, TexelFn fn) => RasterAt(Size, Size, a, b, c, fn);

        private static void RasterAt(int W, int H, Vector2 a, Vector2 b, Vector2 c, TexelFn fn)
        {
            a = new Vector2(Mathf.Repeat(a.x, 1f), Mathf.Repeat(a.y, 1f)) + new Vector2(a.x - Mathf.Repeat(a.x, 1f), a.y - Mathf.Repeat(a.y, 1f)) * 0f;
            Vector2 A = new Vector2(a.x * W, a.y * H), B = new Vector2(b.x * W, b.y * H), C = new Vector2(c.x * W, c.y * H);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(A.x, Mathf.Min(B.x, C.x))), 0, W - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(A.x, Mathf.Max(B.x, C.x))), 0, W - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(A.y, Mathf.Min(B.y, C.y))), 0, H - 1), y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(A.y, Mathf.Max(B.y, C.y))), 0, H - 1);
            float area = (B.x - A.x) * (C.y - A.y) - (C.x - A.x) * (B.y - A.y);
            if (Mathf.Abs(area) < 1e-6f) return;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float w0 = ((B.x - px) * (C.y - py) - (C.x - px) * (B.y - py)) / area;
                    float w1 = ((C.x - px) * (A.y - py) - (A.x - px) * (C.y - py)) / area;
                    float w2 = 1f - w0 - w1;
                    if (w0 < -0.02f || w1 < -0.02f || w2 < -0.02f) continue;
                    fn(x, y, w0, w1, w2);
                }
        }

        private static void Grow(Color[] col, bool[] filled)
        {
            var add = new List<int>();
            for (int y = 1; y < Size - 1; y++)
                for (int x = 1; x < Size - 1; x++)
                {
                    int k = y * Size + x;
                    if (filled[k]) continue;
                    int from = filled[k - 1] ? k - 1 : filled[k + 1] ? k + 1 : filled[k - Size] ? k - Size : filled[k + Size] ? k + Size : -1;
                    if (from < 0) continue;
                    col[k] = col[from];
                    add.Add(k);
                }
            foreach (int k in add) filled[k] = true;
        }

        private static float Lum(Color32 c) => (c.r * 0.3f + c.g * 0.55f + c.b * 0.15f) / 255f;

        private static float Hash(int k)
        {
            unchecked
            {
                uint h = (uint)k * 2654435761u;
                h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        /// <summary>For web suit status.</summary>
        public static string Describe()
        {
            if (!(Painted is Frame fr)) return "not painted";
            return $"scale {fr.Scale:0.0} front {fr.Front} neck {fr.J("Neck")} chest {fr.J("Spine2")} hips {fr.J("Hips")} head {fr.J("Head")} lfoot {fr.J("LeftFoot")} larm {fr.J("LeftArm")} lfore {fr.J("LeftForeArm")} lhand {fr.J("LeftHand")}";
        }

        /// <summary>For trying a look before the suit exists in the bag: the local player shows it (or not) without wearing it.</summary>
        internal static Style? Preview;

        [HarmonyPatch(typeof(Humanoid), "SetupVisEquipment")]
        private static class Humanoid_SetupVisEquipment_Preview
        {
            private static void Postfix(Humanoid __instance, VisEquipment visEq)
            {
                if (Preview == null || visEq == null || !ReferenceEquals(__instance, Player.m_localPlayer) || Wears(__instance) || !Items.ContainsKey(ChestNames[(int)Preview])) return;
                visEq.SetChestItem(ChestHashes[(int)Preview]);
                visEq.SetLegItem(LegsHash);
            }
        }
    }
}
