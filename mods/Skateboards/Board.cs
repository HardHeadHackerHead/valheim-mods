using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Skateboards
{
    /// <summary>
    /// The skateboard: a one-handed item made at the workbench (the server's cost). Held in the hand, the attack button drops it and you
    /// hop on (Controls.cs, Skate.cs); stepping off puts it back in the hand. It never leaves the bag while you ride, so nothing is
    /// placed in the world and nothing can be lost there.
    ///
    /// The item is a copy of the Club (a plain one-handed item) with its own model (a wooden deck with grip tape, iron trucks and resin
    /// wheels, from simple shapes with the game's own wood), drawn for its icon (Shared/Icon.cs), and no damage: attacks with it never
    /// happen (Controls.cs takes the button). It goes in through ItemReload.cs, so the mod still reloads in a world, and its recipe
    /// through Shared/Content.cs; the recipe is hidden on a server without the mod (a dropped board would be deleted there).
    /// The same model is what rides under the feet of every skating player (Look.cs).
    /// </summary>
    internal static class Board
    {
        public const string PrefabName = "skateboards_board";
        public const string NameKey = "$skateboards_board";
        private static readonly int Hash = PrefabName.GetStableHashCode();

        private static GameObject _holder, _item;
        private static Recipe _recipe;

        private static readonly AccessTools.FieldRef<Character, ZNetView> NView = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");

        public static bool IsBoard(ItemDrop.ItemData item) =>
            item != null && ((item.m_dropPrefab != null && item.m_dropPrefab.name == PrefabName) || item.m_shared?.m_name == NameKey);

        /// <summary>The local player holds a skateboard in the right hand.</summary>
        public static bool InHand(Player p) => p != null && IsBoard(RightItem(p));

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> RightItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_rightItem");

        /// <summary>Any player (another one's from their ZDO: their equipment fields are only set on their own game) holds a skateboard.</summary>
        public static bool InHandOf(Player p)
        {
            if (p == null) return false;
            if (ReferenceEquals(p, Player.m_localPlayer)) return InHand(p);
            ZNetView nview = NView(p);
            ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            return zdo != null && zdo.GetInt(ZDOVars.s_rightItem) == Hash;
        }

        // ---- the item ----

        public static void Register()
        {
            ZNetScene scene = ZNetScene.instance;
            ObjectDB db = ObjectDB.instance;
            if (scene == null) return;   // copied from the Club, which the scene knows: ZNetScene.Awake calls this again
            if (_item == null)
            {
                if (_holder == null)
                {
                    _holder = new GameObject("SkateboardsPrefabs");
                    _holder.SetActive(false);   // keeps the copy from waking up as a real object
                    Object.DontDestroyOnLoad(_holder);
                }
                try { _item = Build(scene); }
                catch (System.Exception e) { Plugin.Log.LogError("could not build the skateboard: " + e); return; }
                if (_item == null) return;
            }
            ItemReload.Replace(scene, db, new[] { _item });
            if (db == null) return;
            GameObject bench = scene.GetPrefab("piece_workbench");
            CraftingStation station = bench != null ? bench.GetComponent<CraftingStation>() : null;
            _recipe = DHack.Shared.Content.Recipe(_item, station, 1, 1, null, Plugin.BoardCost.Value, Plugin.Log, db: db);
            Plugin.Synced.NeedsServer(_recipe);
        }

        public static void ApplyCost()
        {
            if (_recipe != null) DHack.Shared.Costs.Apply(_recipe, Plugin.BoardCost.Value, Plugin.Log);
        }

        public static GameObject Current(string name) => name == PrefabName ? _item : null;

        public static void Unregister()
        {
            bool keep = ItemReload.KeepOnUnload;   // inside a world: the item stays registered and alive, the next copy replaces it
            DHack.Shared.Content.RemoveAll(keepItems: true);   // the recipe (only while still this copy's)
            _recipe = null;
            if (!keep && _item != null)
            {
                ZNetScene scene = ZNetScene.instance;
                ObjectDB db = ObjectDB.instance;
                if (scene != null)
                {
                    scene.m_prefabs.Remove(_item);
                    var named = (Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene);
                    if (named.TryGetValue(Hash, out GameObject current) && current == _item) named.Remove(Hash);
                }
                if (db != null)
                {
                    db.m_items.RemoveAll(i => i == null || i == _item);
                    AccessTools.Method(typeof(ObjectDB), "UpdateRegisters")?.Invoke(db, null);
                }
            }
            if (!keep && _holder != null) Object.Destroy(_holder);
            _holder = null;
            _item = null;
            _wood = _grip = _iron = _wheel = _paint = null;
            _base = null;
        }

        /// <summary>Every skateboard in the local player's bag gets this copy's prefab and data (one the give command made has its own copy).</summary>
        public static void Refresh()
        {
            if (_item == null || Player.m_localPlayer == null) return;
            ItemDrop.ItemData.SharedData shared = _item.GetComponent<ItemDrop>().m_itemData.m_shared;
            foreach (ItemDrop.ItemData item in Player.m_localPlayer.GetInventory().GetAllItems())
                if (IsBoard(item)) { item.m_dropPrefab = _item; item.m_shared = shared; }
        }

        private static GameObject Build(ZNetScene scene)
        {
            GameObject source = scene.GetPrefab("Club");
            if (source == null) { Plugin.Log.LogWarning("the Club prefab was not found, so there is no skateboard"); return null; }
            GameObject go = Object.Instantiate(source, _holder.transform);
            go.name = PrefabName;
            Transform attach = go.transform.Find("attach");
            if (attach == null) { Plugin.Log.LogWarning("the Club has no attach part, so there is no skateboard"); Object.DestroyImmediate(go); return null; }
            MeshRenderer clubModel = attach.Find("model")?.GetComponent<MeshRenderer>();
            if (clubModel != null && clubModel.sharedMaterial != null) _base = clubModel.sharedMaterial;   // the game's wood, for the deck
            for (int i = attach.childCount - 1; i >= 0; i--) Object.DestroyImmediate(attach.GetChild(i).gameObject);   // (the club, its glow, trail and collider)

            // in the hand: carried by its side, the deck upright along the arm, the nose ahead (the attach part's z runs out of the fist)
            GameObject held = Model(go.layer);
            held.name = "model";
            held.transform.SetParent(attach, false);
            held.transform.localPosition = new Vector3(0f, -0.02f, 0f);
            held.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var box = new GameObject("collider") { layer = go.layer };
            box.transform.SetParent(held.transform, false);
            var col = box.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.07f, 0f);
            col.size = new Vector3(0.21f, 0.12f, 0.8f);

            ItemDrop drop = go.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
            s.m_name = NameKey;
            s.m_description = NameKey + "_desc";
            s.m_itemType = ItemDrop.ItemData.ItemType.OneHandedWeapon;
            s.m_damages = new HitData.DamageTypes();
            s.m_damagesPerLevel = new HitData.DamageTypes();
            s.m_attackForce = 0f;
            s.m_blockPower = 0f;
            s.m_blockPowerPerLevel = 0f;
            s.m_deflectionForce = 0f;
            s.m_timedBlockBonus = 1f;
            s.m_equipStatusEffect = null;
            s.m_setStatusEffect = null;
            s.m_setName = "";
            s.m_setSize = 0;
            s.m_weight = 3f;
            s.m_value = 0;                  // never sold: the trader buys anything worth more than 0
            s.m_maxStackSize = 1;
            s.m_maxQuality = 1;
            s.m_variants = 0;
            s.m_useDurability = false;
            s.m_teleportable = true;
            s.m_questItem = false;
            s.m_dlc = "";
            s.m_movementModifier = 0f;
            drop.m_itemData.m_dropPrefab = go;
            drop.m_itemData.m_stack = 1;
            DHack.Shared.Icon.MakeItem(go, setIcon: true, look: Quaternion.LookRotation(new Vector3(-0.55f, -0.6f, 0.58f).normalized, Vector3.up));
            return go;
        }

        // ---- the model: nose at +z, deck facing +y, its origin where the wheels touch the ground (the middle between them) ----

        /// <summary>How high the deck's top is over the ground the wheels stand on.</summary>
        public const float DeckTop = 0.104f;
        /// <summary>Where the trucks are along the deck (from the middle), where the feet stand.</summary>
        public const float TruckZ = 0.21f;

        private static Material _base, _wood, _grip, _iron, _wheel, _paint;

        private static Material Mat(Color c, float metal, float gloss, bool keepTexture)
        {
            Material m = _base != null ? new Material(_base) : new Material(Shader.Find("Standard"));
            m.color = c;
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            if (!keepTexture)
            {
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
                if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            }
            return m;
        }

        public static GameObject Model(int layer)
        {
            if (_wood == null)
            {
                _wood = Mat(new Color(0.95f, 0.82f, 0.66f), 0f, 0.2f, keepTexture: true);
                _grip = Mat(new Color(0.07f, 0.07f, 0.075f), 0f, 0.05f, keepTexture: false);
                _iron = Mat(new Color(0.52f, 0.53f, 0.55f), 0.8f, 0.55f, keepTexture: false);
                _wheel = Mat(new Color(0.85f, 0.52f, 0.14f), 0f, 0.6f, keepTexture: false);
                _paint = Mat(new Color(0.55f, 0.09f, 0.07f), 0f, 0.3f, keepTexture: false);
            }
            var root = new GameObject("skateboards_board_model") { layer = layer };
            const float t = 0.014f, deckY = DeckTop - t / 2f - 0.002f, kick = 18f, kickLen = 0.14f;
            float kickRise = kickLen / 2f * Mathf.Sin(kick * Mathf.Deg2Rad), kickRun = kickLen / 2f * Mathf.Cos(kick * Mathf.Deg2Rad);
            Part(root, PrimitiveType.Cube, _wood, new Vector3(0f, deckY, 0f), Vector3.zero, new Vector3(0.205f, t, 0.56f));                    // the deck
            Part(root, PrimitiveType.Cube, _wood, new Vector3(0f, deckY + kickRise, 0.28f + kickRun), new Vector3(-kick, 0f, 0f), new Vector3(0.205f, t, kickLen));    // the nose
            Part(root, PrimitiveType.Cube, _wood, new Vector3(0f, deckY + kickRise, -0.28f - kickRun), new Vector3(kick, 0f, 0f), new Vector3(0.205f, t, kickLen));   // the tail
            Part(root, PrimitiveType.Cube, _grip, new Vector3(0f, deckY + t / 2f + 0.002f, 0f), Vector3.zero, new Vector3(0.195f, 0.004f, 0.555f));                 // grip tape
            Part(root, PrimitiveType.Cube, _paint, new Vector3(0f, deckY - t / 2f - 0.002f, 0f), Vector3.zero, new Vector3(0.207f, 0.004f, 0.09f));                // a red band under it
            Part(root, PrimitiveType.Cube, _paint, new Vector3(0f, deckY + kickRise - 0.009f, 0.28f + kickRun * 1.2f), new Vector3(-kick, 0f, 0f), new Vector3(0.207f, 0.004f, 0.05f));
            Part(root, PrimitiveType.Cube, _paint, new Vector3(0f, deckY + kickRise - 0.009f, -0.28f - kickRun * 1.2f), new Vector3(kick, 0f, 0f), new Vector3(0.207f, 0.004f, 0.05f));
            for (int side = -1; side <= 1; side += 2)
            {
                float z = side * TruckZ;
                Part(root, PrimitiveType.Cube, _iron, new Vector3(0f, deckY - t / 2f - 0.008f, z), Vector3.zero, new Vector3(0.06f, 0.012f, 0.08f));             // baseplate
                Part(root, PrimitiveType.Cube, _iron, new Vector3(0f, 0.072f, z), new Vector3(0f, 0f, 0f), new Vector3(0.034f, 0.03f, 0.034f));                  // kingpin and bushing
                Part(root, PrimitiveType.Cylinder, _iron, new Vector3(0f, 0.027f + 0.01f, z), new Vector3(0f, 0f, 90f), new Vector3(0.022f, 0.09f, 0.022f));     // hanger and axle
                for (int w = -1; w <= 1; w += 2)
                    Part(root, PrimitiveType.Cylinder, _wheel, new Vector3(w * 0.09f, 0.027f, z), new Vector3(0f, 0f, 90f), new Vector3(0.054f, 0.016f, 0.054f));   // wheels
            }
            return root;
        }

        private static void Part(GameObject root, PrimitiveType shape, Material mat, Vector3 pos, Vector3 euler, Vector3 scale)
        {
            GameObject g = GameObject.CreatePrimitive(shape);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.layer = root.layer;
            g.transform.SetParent(root.transform, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(euler);
            g.transform.localScale = scale;
            g.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        public static bool Registered => _item != null;
    }
}
