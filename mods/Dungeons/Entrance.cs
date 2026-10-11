using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using static DHack.Shared.Translations;

namespace Dungeons
{
    /// <summary>The keys of an entrance's ZDO: everything saved about its dungeon (the rooms themselves never are).</summary>
    internal static class Keys
    {
        public static readonly int Pack = "quaddungeon_pack".GetStableHashCode();         // string: the pack's name
        public static readonly int Seed = "quaddungeon_seed".GetStableHashCode();         // int
        public static readonly int Id = "quaddungeon_id".GetStableHashCode();             // string: our own id (a ZDOID changes every load)
        public static readonly int Origin = "quaddungeon_origin".GetStableHashCode();     // Vector3: where the layout's origin stands (set once, by the owner)
        public static readonly int Slot = "quaddungeon_slot".GetStableHashCode();         // int: which height step above the entrance (for the record)
        public static readonly int Done = "quaddungeon_done".GetStableHashCode();         // string: '1' at index i when room i's networked parts were made
        public static readonly int Entered = "quaddungeon_entered".GetStableHashCode();   // int: the day it was first entered since new (-1: not yet)
        public static readonly int Resets = "quaddungeon_resets".GetStableHashCode();     // int: how many times it came back new
        public static readonly int ResetDay = "quaddungeon_resetday".GetStableHashCode(); // int: the day it last did
        public static readonly int Mechs = "quaddungeon_mechs".GetStableHashCode();       // string: the mechanisms that ever moved (MechRecords)
        /// <summary>On every saved object the builder makes for a dungeon (its doors, chests, spawners): that dungeon's Id, so a reset can
        /// find and remove them before it makes them again.</summary>
        public const string PartOf = "quaddungeon_partof";
    }

    /// <summary>
    /// The entrance: the one saved object per dungeon, holding the pack, the seed and the state in its ZDO. One prefab per look
    /// (EntranceLooks, read from packs/looks/*.json: quaddungeon_entrance is the crypt, quaddungeon_entrance_&lt;look&gt; the others), each our own root with a ZNetView
    /// and a stand-in: the forest crypt's door (or the plain wooden door, if a game update renames it) with everything that would make it a
    /// door, a piece or breakable taken off. The look itself is built on each game when the entrance appears (EntranceLooks.Dress), and the
    /// stand-in hidden. The way out inside each dungeon is a local copy of the stand-in (no ZNetView).
    /// Registered as the pitfalls doc says: from ZNetScene.Awake and ObjectDB.Awake, and at our Awake for a reload.
    /// </summary>
    internal static class Entrance
    {
        public const string PrefabName = "quaddungeon_entrance";
        public static readonly int Hash = PrefabName.GetStableHashCode();
        private static readonly string[] Looks = { "dungeon_forestcrypt_door", "wood_door" };

        private static GameObject _holder, _prefab, _look;
        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();   // look -> its prefab
        private static HashSet<int> _hashes;
        private static HashSet<int> Hashes => _hashes ?? (_hashes = new HashSet<int>(EntranceLooks.All.Select(l => l.Prefab.GetStableHashCode())));
        public static GameObject Prefab => _prefab;
        public static string LookFrom = "";

        /// <summary>The entrance prefab for a look (a pack's entranceLook; unknown or none: the crypt).</summary>
        public static GameObject PrefabFor(string look) => Prefabs.TryGetValue(EntranceLooks.Get(look).Name, out GameObject p) ? p : _prefab;

        /// <summary>Every entrance prefab's name (one per look).</summary>
        public static IEnumerable<string> PrefabNames => EntranceLooks.All.Select(l => l.Prefab);

        /// <summary>A saved object of one of the entrance prefabs.</summary>
        public static bool IsEntrance(int prefabHash) => Hashes.Contains(prefabHash);

        public static void Register(ZNetScene scene)
        {
            if (scene == null) return;
            if (_prefab == null)
            {
                _holder = new GameObject("Dungeons_Prefabs");
                _holder.SetActive(false);   // keeps the copies from waking up as real objects
                Object.DontDestroyOnLoad(_holder);
                _look = MakeLookTemplate(scene);
                foreach (EntranceLooks.Look look in EntranceLooks.All)
                {
                    var prefab = new GameObject(look.Prefab);
                    prefab.transform.SetParent(_holder.transform, false);
                    prefab.layer = _look.layer;
                    Object.Instantiate(_look, prefab.transform, false).name = "look";
                    ZNetView view = prefab.AddComponent<ZNetView>();
                    view.m_persistent = true;
                    view.m_type = ZDO.ObjectType.Default;
                    prefab.AddComponent<DungeonEntrance>().Look = look.Name;
                    Prefabs[look.Name] = prefab;
                }
                _prefab = Prefabs[EntranceLooks.Default];
            }
            var named = (Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene);
            foreach (GameObject prefab in Prefabs.Values)
            {
                if (!scene.m_prefabs.Contains(prefab)) scene.m_prefabs.Add(prefab);
                named[prefab.name.GetStableHashCode()] = prefab;   // lets it be made and loaded from saves
            }
        }

        public static void Unregister()
        {
            if (_prefab == null) return;
            if (ZNetScene.instance != null)
            {
                var named = (Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(ZNetScene.instance);
                foreach (GameObject prefab in Prefabs.Values)
                {
                    ZNetScene.instance.m_prefabs.Remove(prefab);
                    int hash = prefab.name.GetStableHashCode();
                    if (named.TryGetValue(hash, out GameObject current) && current == prefab) named.Remove(hash);   // only our own entry
                }
            }
            Object.Destroy(_holder);
            Prefabs.Clear();
            _holder = _prefab = _look = null;
        }

        /// <summary>A local copy of the entrance's look (no ZNetView, nothing saved), active, at a place: the way out inside.</summary>
        public static GameObject MakeLook(string name, Vector3 at, Quaternion rot)
        {
            if (_look == null) return null;
            GameObject go = Object.Instantiate(_look, at, rot);
            go.name = name;
            return go;
        }

        /// <summary>The look: the first of the game's doors we find, stripped to its model and colliders. Never fails: without them, a plain
        /// stone-coloured slab (the prefab must be registered whatever happens, or the host deletes every placed entrance).</summary>
        private static GameObject MakeLookTemplate(ZNetScene scene)
        {
            GameObject look = null;
            foreach (string name in Looks)
            {
                GameObject source = scene.GetPrefab(name);
                if (source == null) continue;
                try
                {
                    look = Object.Instantiate(source, _holder.transform, false);
                    // the game's prefab keeps where it stood when it was made (dungeon_forestcrypt_door: 50 m up): ours stands on the entrance
                    look.transform.localPosition = Vector3.zero;
                    look.transform.localRotation = Quaternion.identity;
                    Strip(look);
                    LookFrom = name;
                    break;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Could not use {name} for the dungeon entrance: {e.Message}");
                    if (look != null) Object.DestroyImmediate(look);
                    look = null;
                }
            }
            if (look == null)
            {
                Plugin.Log.LogWarning("None of the game's doors were found for the dungeon entrance's look: it is a plain slab");
                look = GameObject.CreatePrimitive(PrimitiveType.Cube);
                look.transform.SetParent(_holder.transform, false);
                look.transform.localScale = new Vector3(2f, 3f, 0.4f);
                look.transform.localPosition = new Vector3(0f, 1.5f, 0f);
                LookFrom = "(slab)";
                GameObject root = new GameObject();
                root.transform.SetParent(_holder.transform, false);
                look.transform.SetParent(root.transform, false);
                look = root;
            }
            look.name = "quaddungeon_look";
            return look;
        }

        /// <summary>Everything that isn't the model or a collider: the ZNetView, Door, WearNTear, Piece and the rest (scripts), and physics.
        /// Removed in a few rounds, since one script can require another.</summary>
        private static void Strip(GameObject go)
        {
            for (int round = 0; round < 4; round++)
            {
                bool left = false;
                MonoBehaviour[] scripts = go.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = scripts.Length - 1; i >= 0; i--)
                {
                    try { Object.DestroyImmediate(scripts[i]); }
                    catch (Exception) { left = true; }
                }
                if (!left) break;
            }
            foreach (Rigidbody body in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
        }
    }

    /// <summary>The entrance in the world: "Enter (the pack's title)", its look (EntranceLooks), and the RPC through which players tell its
    /// owner things (Dungeons_Act: "entered", "use:&lt;mechanism key&gt;" for a lever).</summary>
    internal class DungeonEntrance : MonoBehaviour, Hoverable, Interactable
    {
        public string Look = EntranceLooks.Default;   // set on each look's prefab (copied with it)

        public ZNetView View { get; private set; }
        public Site Site { get; private set; }

        private void Awake()
        {
            View = GetComponent<ZNetView>();
            if (View == null || View.GetZDO() == null) return;
            View.Register<string>("Dungeons_Act", RPC_Act);
            ZDO zdo = View.GetZDO();
            if (View.IsOwner() && string.IsNullOrEmpty(zdo.GetString(Keys.Id))) zdo.Set(Keys.Id, Dungeons.NewId());
            Site = Dungeons.Track(this);
        }

        private void Start()
        {
            if (View == null || View.GetZDO() == null) return;
            // (on the plugin, so a location loaded for it is always let go, even if the entrance goes while it loads)
            if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(EntranceLooks.Dress(this, transform.Find("look")?.gameObject));
        }

        private void OnDestroy()
        {
            if (Site != null) Dungeons.Lost(Site);
            Site = null;
        }

        public string GetHoverText()
        {
            if (Site == null) return "";
            string title = Site.Title;
            string text = $"{title}\n[<color=yellow><b>$KEY_Use</b></color>] {T("dungeons_enter", title)}";
            if (Site.Problem != null) text += $"\n<color=#e8a060>{T("dungeons_broken", Site.Problem)}</color>";
            else if (!Site.Ready) text += $"\n<color=#a0a0a0>{T("dungeons_forming")}</color>";
            else if (Site.RenewsOnDay(out int day)) text += $"\n<color=#a0a0a0>{T("dungeons_renews_on", day)}</color>";
            return Localization.instance.Localize(text);
        }

        public string GetHoverName() => Site != null ? Site.Title : T("dungeons_entrance");

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || Site == null || user != Player.m_localPlayer) return false;
            Dungeons.Enter(Site);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        /// <summary>The owner hears what players did. The sender is the RPC's, never something in the message.</summary>
        private void RPC_Act(long sender, string act)
        {
            if (Site == null || !View.IsOwner()) return;
            if (act == "entered") Dungeons.RecordEntry(Site);
            else if (act != null && act.StartsWith("use:")) Site.Mechs?.UseFrom(sender, act.Substring(4));
            else Plugin.Log.LogInfo($"Dungeons_Act from {sender}: \"{act}\" isn't known (a newer version?)");
        }
    }

    /// <summary>The way out: the entrance's look, local, at the entrance room's doorway. "Leave".</summary>
    internal class DungeonExit : MonoBehaviour, Hoverable, Interactable
    {
        public Site Site;

        public string GetHoverText()
        {
            if (Site == null) return "";
            return Localization.instance.Localize($"{T("dungeons_way_out")}\n[<color=yellow><b>$KEY_Use</b></color>] {T("dungeons_leave", Site.Title)}");
        }

        public string GetHoverName() => T("dungeons_way_out");

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || Site == null || user != Player.m_localPlayer) return false;
            Dungeons.Leave(Site);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake
    {
        private static void Postfix(ZNetScene __instance) => Entrance.Register(__instance);
    }

    // Whichever of ZNetScene and ObjectDB wakes second, the entrance is registered before any saved one is loaded.
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake
    {
        private static void Postfix() { if (ZNetScene.instance != null) Entrance.Register(ZNetScene.instance); }
    }
}
