using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Webslinger
{
    /// <summary>
    /// The Web Shooters: a pair of wrist gadgets worn in the utility slot (where Megingjord and the Wishbone go). With them on, the moves
    /// work; without, Webslinger does nothing (the server's NeedsWebShooters can turn that off). Made at the workbench (the server's cost).
    ///
    /// The item is a copy of the Wishbone (a utility item that shows nothing on the body) with its own model, drawn for its icon
    /// (Shared/Icon.cs). It goes in through ItemReload.cs, so the mod still reloads in a world, and its recipe through Shared/Content.cs;
    /// the recipe is hidden on a server without the mod (a dropped pair would be deleted there).
    /// Worn, a shooter shows on each wrist, for everyone with the mod: read from the player's ZDO (the utility item's hash), since another
    /// player's equipment fields are only set on their own game (docs/modding-pitfalls.md).
    /// </summary>
    internal static class Shooters
    {
        public const string PrefabName = "webslinger_shooters";
        public const string NameKey = "$webslinger_shooters";
        private static readonly int Hash = PrefabName.GetStableHashCode();

        private static GameObject _holder, _item;
        private static Recipe _recipe;

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> UtilityItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_utilityItem");
        private static readonly AccessTools.FieldRef<Character, ZNetView> NView = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");

        public static bool IsShooters(ItemDrop.ItemData item) =>
            item != null && ((item.m_dropPrefab != null && item.m_dropPrefab.name == PrefabName) || item.m_shared?.m_name == NameKey);

        /// <summary>The local player can use the moves: wearing the shooters, or the server doesn't ask for them.</summary>
        public static bool Worn(Player p) => p != null && (!Plugin.NeedsShooters.Value || IsShooters(UtilityItem(p)));

        /// <summary>Any player (another one's from their ZDO) wears the shooters.</summary>
        public static bool WornBy(Player p)
        {
            if (p == null) return false;
            if (ReferenceEquals(p, Player.m_localPlayer)) return IsShooters(UtilityItem(p));
            ZNetView nview = NView(p);
            ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            return zdo != null && zdo.GetInt(ZDOVars.s_utilityItem) == Hash;
        }

        // ---- the item ----

        public static void Register()
        {
            ZNetScene scene = ZNetScene.instance;
            ObjectDB db = ObjectDB.instance;
            if (scene == null) return;   // copied from the Wishbone, which the scene knows: ZNetScene.Awake calls this again
            if (_item == null)
            {
                if (_holder == null)
                {
                    _holder = new GameObject("WebslingerPrefabs");
                    _holder.SetActive(false);   // keeps the copy from waking up as a real object
                    Object.DontDestroyOnLoad(_holder);
                }
                try { _item = Build(scene); }
                catch (System.Exception e) { Plugin.Log.LogError("could not build the Web Shooters: " + e); return; }
                if (_item == null) return;
            }
            ItemReload.Replace(scene, db, new[] { _item });
            if (db == null) return;
            GameObject bench = scene.GetPrefab("piece_workbench");
            CraftingStation station = bench != null ? bench.GetComponent<CraftingStation>() : null;
            _recipe = DHack.Shared.Content.Recipe(_item, station, 1, 1, null, Plugin.ShootersCost.Value, Plugin.Log, db: db);
            Plugin.Synced.NeedsServer(_recipe);
        }

        public static void ApplyCost()
        {
            if (_recipe != null) DHack.Shared.Costs.Apply(_recipe, Plugin.ShootersCost.Value, Plugin.Log);
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
            foreach (GameObject[] pair in Worn_.Values) foreach (GameObject g in pair) if (g != null) Object.Destroy(g);
            Worn_.Clear();
            _mat = null;
        }

        /// <summary>Every Web Shooters in the local player's bag gets this copy's prefab and data (one the give command made has its own copy).</summary>
        public static void Refresh()
        {
            if (_item == null || Player.m_localPlayer == null) return;
            ItemDrop.ItemData.SharedData shared = _item.GetComponent<ItemDrop>().m_itemData.m_shared;
            foreach (ItemDrop.ItemData item in Player.m_localPlayer.GetInventory().GetAllItems())
                if (IsShooters(item)) { item.m_dropPrefab = _item; item.m_shared = shared; }
        }

        private static GameObject Build(ZNetScene scene)
        {
            GameObject source = scene.GetPrefab("Wishbone");
            if (source == null) { Plugin.Log.LogWarning("the Wishbone prefab was not found, so there are no Web Shooters"); return null; }
            GameObject go = Object.Instantiate(source, _holder.transform);
            go.name = PrefabName;
            Shader shader = null;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                if (r is MeshRenderer && r.sharedMaterial != null) { shader = r.sharedMaterial.shader; break; }
            if (shader != null) Shader = shader;
            for (int i = go.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);   // (the wishbone, its glow and light)
            foreach (Collider c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);

            // two shooters side by side, as they lie on the ground and in the icon
            var attach = new GameObject("attach") { layer = go.layer };
            attach.transform.SetParent(go.transform, false);
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject one = Model(go.layer);
                one.transform.SetParent(attach.transform, false);
                one.transform.localPosition = new Vector3(side * 0.07f, 0.045f, 0f);
                one.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // the band's axis lying along z, the cartridge on top
            }
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.058f, 0f);
            box.size = new Vector3(0.24f, 0.12f, 0.1f);

            ItemDrop drop = go.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
            s.m_name = NameKey;
            s.m_description = NameKey + "_desc";
            s.m_itemType = ItemDrop.ItemData.ItemType.Utility;
            s.m_equipStatusEffect = null;   // (the Wishbone's ore finder)
            s.m_setStatusEffect = null;
            s.m_setName = "";
            s.m_setSize = 0;
            s.m_armor = 0f;
            s.m_armorPerLevel = 0f;
            s.m_weight = 1f;
            s.m_value = 0;                  // never sold: the trader buys anything worth more than 0
            s.m_maxStackSize = 1;
            s.m_maxQuality = 1;
            s.m_variants = 0;
            s.m_useDurability = false;
            s.m_teleportable = true;
            s.m_questItem = false;
            s.m_dlc = "";
            drop.m_itemData.m_dropPrefab = go;
            drop.m_itemData.m_stack = 1;
            DHack.Shared.Icon.MakeItem(go, setIcon: true, look: Quaternion.LookRotation(Vector3.down, Vector3.forward) * Quaternion.Euler(0f, 0f, 30f));
            return go;
        }

        // ---- the model: a leather band round the wrist, a steel cartridge on top, a brass nozzle toward the hand ----

        private static Shader Shader;
        private static Material _mat;

        private static Material Mat(Color c, float metal, float gloss)
        {
            if (_mat == null)
            {
                Shader sh = Shader != null ? Shader : UnityEngine.Shader.Find("Standard");
                _mat = new Material(sh);
            }
            var m = new Material(_mat) { color = c };
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            return m;
        }

        private static Material _leather, _steel, _brass, _web;

        /// <summary>One shooter, its band's axis along local y (the arm runs along y, the nozzle at +y), the cartridge on +z.</summary>
        private static GameObject Model(int layer)
        {
            if (_leather == null)
            {
                _leather = Mat(new Color(0.24f, 0.13f, 0.07f), 0f, 0.25f);
                _steel = Mat(new Color(0.55f, 0.57f, 0.6f), 0.85f, 0.6f);
                _brass = Mat(new Color(0.75f, 0.55f, 0.22f), 0.9f, 0.55f);
                _web = Mat(new Color(0.95f, 0.95f, 0.97f), 0f, 0.1f);
            }
            var root = new GameObject("webslinger_shooter") { layer = layer };
            Part(root, PrimitiveType.Cylinder, _leather, Vector3.zero, Vector3.zero, new Vector3(0.085f, 0.032f, 0.085f));        // the band
            Part(root, PrimitiveType.Cylinder, _steel, new Vector3(0f, -0.022f, 0f), Vector3.zero, new Vector3(0.09f, 0.005f, 0.09f));   // its rims
            Part(root, PrimitiveType.Cylinder, _steel, new Vector3(0f, 0.022f, 0f), Vector3.zero, new Vector3(0.09f, 0.005f, 0.09f));
            Part(root, PrimitiveType.Cylinder, _steel, new Vector3(0f, 0.004f, 0.054f), Vector3.zero, new Vector3(0.032f, 0.036f, 0.032f));   // the cartridge
            Part(root, PrimitiveType.Cylinder, _brass, new Vector3(0f, 0.046f, 0.054f), Vector3.zero, new Vector3(0.014f, 0.008f, 0.014f));   // the nozzle
            Part(root, PrimitiveType.Cube, _brass, new Vector3(0f, -0.01f, 0.072f), Vector3.zero, new Vector3(0.012f, 0.03f, 0.008f));        // the trigger plate
            Part(root, PrimitiveType.Sphere, _web, new Vector3(0f, 0.058f, 0.054f), Vector3.zero, new Vector3(0.012f, 0.012f, 0.012f));      // a wisp of web
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

        // ---- worn: a shooter on each wrist ----

        private static readonly Dictionary<Player, GameObject[]> Worn_ = new Dictionary<Player, GameObject[]>();
        private static readonly List<Player> Gone = new List<Player>();
        private static float _checkAt;

        /// <summary>After the animation: each player wearing the shooters has one on each wrist, and those who took them off none.</summary>
        public static void Late()
        {
            if (Time.time < _checkAt) return;
            _checkAt = Time.time + 0.25f;
            Gone.Clear();
            foreach (KeyValuePair<Player, GameObject[]> kv in Worn_)
                if (kv.Key == null || !WornBy(kv.Key) || kv.Value[0] == null || kv.Value[1] == null) Gone.Add(kv.Key);
            foreach (Player p in Gone)
            {
                foreach (GameObject g in Worn_[p]) if (g != null) Object.Destroy(g);
                Worn_.Remove(p);
            }
            foreach (Player p in Player.GetAllPlayers())
                if (p != null && !Worn_.ContainsKey(p) && WornBy(p)) Dress(p);
        }

        private static void Dress(Player p)
        {
            Animator anim = p.GetComponentInChildren<Animator>();
            if (anim == null || !anim.isHuman) return;
            var pair = new GameObject[2];
            for (int i = 0; i < 2; i++)
            {
                Transform fore = anim.GetBoneTransform(i == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                Transform hand = anim.GetBoneTransform(i == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (fore == null || hand == null) { foreach (GameObject g in pair) if (g != null) Object.Destroy(g); return; }
                GameObject one = Model(p.gameObject.layer);
                foreach (Renderer r in one.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // round the wrist, a little back from the hand, the cartridge on the back of the forearm
                Vector3 along = hand.position - fore.position;
                Vector3 back = Vector3.ProjectOnPlane(hand.up, along.normalized);
                if (back.sqrMagnitude < 0.0001f) back = Vector3.ProjectOnPlane(Vector3.up, along.normalized);
                one.transform.position = fore.position + along * 0.78f;
                one.transform.rotation = Quaternion.LookRotation(back.normalized, along.normalized);
                one.transform.SetParent(fore, true);
                pair[i] = one;
            }
            Worn_[p] = pair;
        }

        /// <summary>For the status command.</summary>
        public static int Dressed => Worn_.Count;
        public static bool Registered => _item != null;
    }
}
