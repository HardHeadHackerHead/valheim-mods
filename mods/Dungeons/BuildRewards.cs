using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    // Items only dungeons give: copies of the game's own item prefabs (their models, icons and the way they lie on the ground and hang on
    // an item stand) under our names. Worth nothing at the trader (m_value 0). Ours:
    // - quaddungeon_trophy_warden: a giant warden's trophy (the troll's trophy, bigger and heavier), from the Warden or a boss's chest;
    // - quaddungeon_giantcog: a giant's cog (the charred cogwheel), a material kept for crafting later: 1-3 in every vault chest;
    // and any other part's (the packs' key items, BlockKeys.cs) through DefineItem: one way to make and register them all.
    // Registered as docs/modding-pitfalls.md says: in ZNetScene (saved and dropped items load by prefab name) and ObjectDB (bags, chests,
    // stands), from both Awake hooks whichever comes first, ObjectDB.CopyOtherDB (the main menu's copy), and the plugin's Awake for a reload
    // in a world (Plugin calls RegisterItems; OnDestroy calls UnregisterItems, which only takes out entries still pointing at ours).
    internal static partial class Builder
    {
        internal const string TrophyItem = "quaddungeon_trophy_warden";
        internal const string CogItem = "quaddungeon_giantcog";

        /// <summary>One of our items: a copy of the game's item Look, named Name (the prefab), with words Display and Description ($ keys
        /// or plain), its weight, its size against the game's, a stack limit (0: the game item's), and whether portals let it through.</summary>
        internal class ItemSpec
        {
            public string Name, Look, Display, Description;
            public float Weight = 1f, Size = 1f;
            public int MaxStack;
            public bool Teleportable = true;
        }

        private static readonly List<ItemSpec> Specs = new List<ItemSpec>
        {
            new ItemSpec { Name = TrophyItem, Look = "TrophyTroll", Display = "$dungeons_item_trophy_warden",
                           Description = "$dungeons_item_trophy_warden_desc", Weight = 8f, Size = 1.4f },
            new ItemSpec { Name = CogItem, Look = "CharredCogwheel", Display = "$dungeons_item_giantcog", Description = "$dungeons_item_giantcog_desc",
                           Weight = 4f },
        };

        private static GameObject _rewardHolder;
        private static readonly List<GameObject> RewardItems = new List<GameObject>();

        /// <summary>One of our items' prefabs, by name (null before it's made).</summary>
        internal static GameObject RewardItem(string name) => RewardItems.Find(i => i != null && i.name == name);

        /// <summary>Adds (or replaces, by name) an item to make and register with ours. Call it as early as you know it (the plugin's
        /// Awake): the items must be in ZNetScene and ObjectDB before a world loads, or saved ones are lost. Called later, it's made and
        /// registered at once where it can be. (A spec replaced after its item was made keeps the item made first.)</summary>
        internal static void DefineItem(ItemSpec spec)
        {
            if (spec == null || string.IsNullOrEmpty(spec.Name) || string.IsNullOrEmpty(spec.Look)) return;
            Specs.RemoveAll(s => s.Name == spec.Name);
            Specs.Add(spec);
            RegisterItems();
        }

        /// <summary>Makes the items not made yet (from whichever of ZNetScene and ObjectDB is there) and puts them all in both. Safe to call
        /// again and again. db: the ObjectDB to add them to (CopyOtherDB's own); null for ObjectDB.instance.</summary>
        internal static void RegisterItems(ObjectDB db = null)
        {
            db = db != null ? db : ObjectDB.instance;
            ZNetScene scene = ZNetScene.instance;
            if (db == null && scene == null) return;
            MakeItems(db, scene);
            ItemReload.Replace(scene, db, RewardItems);   // (in both, an older copy's replaced by name: a reload in a world, ItemReload.cs)
        }

        /// <summary>Takes the items out of the game's lists (only entries that are still ours) and destroys them: the plugin unloading.</summary>
        internal static void UnregisterItems()
        {
            // a reload in a world: left registered and alive (items in bags and chests hold these prefabs; saved and dropped ones would be
            // lost loading in the gap); the next copy replaces them by name and points the items it finds at its own (ItemReload.cs)
            if (ItemReload.KeepOnUnload) return;
            ZNetScene scene = ZNetScene.instance;
            if (scene != null)
            {
                Dictionary<int, GameObject> named = NamedPrefabs(scene);
                foreach (GameObject item in RewardItems)
                {
                    scene.m_prefabs.Remove(item);
                    int hash = item.name.GetStableHashCode();
                    if (named.TryGetValue(hash, out GameObject current) && current == item) named.Remove(hash);
                }
            }
            ObjectDB db = ObjectDB.instance;
            if (db != null)
            {
                db.m_items.RemoveAll(i => i == null || RewardItems.Contains(i));
                AccessTools.Method(typeof(ObjectDB), "UpdateRegisters")?.Invoke(db, null);
            }
            RewardItems.Clear();
            if (_rewardHolder != null) Object.Destroy(_rewardHolder);
            _rewardHolder = null;
        }

        private static Dictionary<int, GameObject> NamedPrefabs(ZNetScene scene) =>
            (Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene);

        /// <summary>Every spec not made yet whose game item can be found now (the next hook tries the others again).</summary>
        private static void MakeItems(ObjectDB db, ZNetScene scene)
        {
            var made = new List<string>();
            foreach (ItemSpec spec in Specs)
            {
                if (RewardItem(spec.Name) != null) continue;
                GameObject look = GameItem(spec.Look, db, scene);
                if (look == null) continue;
                if (_rewardHolder == null)
                {
                    _rewardHolder = new GameObject("quaddungeon_items");
                    _rewardHolder.SetActive(false);   // the copies never wake as objects in the world
                    Object.DontDestroyOnLoad(_rewardHolder);
                }
                RewardItems.Add(CopyItem(look, spec));
                made.Add($"{spec.Name} (from {spec.Look})");
            }
            if (made.Count > 0) Plugin.Log.LogInfo("Dungeon items made: " + string.Join(", ", made));
        }

        private static GameObject GameItem(string name, ObjectDB db, ZNetScene scene)
        {
            GameObject go = scene != null ? scene.GetPrefab(name) : null;
            if (go == null && db != null) go = db.GetItemPrefab(name);
            return go != null && go.GetComponent<ItemDrop>() != null ? go : null;
        }

        /// <summary>A copy of one of the game's items as the spec says; worth nothing. (A trophy's line in the trophies list is its name's
        /// key with "_lore".)</summary>
        private static GameObject CopyItem(GameObject source, ItemSpec spec)
        {
            GameObject go = Object.Instantiate(source, _rewardHolder.transform);
            go.name = spec.Name;
            go.transform.localScale = source.transform.localScale * spec.Size;
            ItemDrop.ItemData.SharedData shared = go.GetComponent<ItemDrop>().m_itemData.m_shared;   // (a copy: the game's item is untouched)
            shared.m_name = spec.Display ?? spec.Name;
            shared.m_description = spec.Description ?? "";
            shared.m_weight = spec.Weight;
            shared.m_value = 0;
            shared.m_teleportable = spec.Teleportable;
            if (spec.MaxStack > 0) shared.m_maxStackSize = spec.MaxStack;
            return go;
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class Dungeons_ZNetScene_Awake_Rewards
    {
        private static void Postfix() => Builder.RegisterItems();
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class Dungeons_ObjectDB_Awake_Rewards
    {
        private static void Postfix(ObjectDB __instance) => Builder.RegisterItems(__instance);
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    internal static class Dungeons_ObjectDB_CopyOtherDB_Rewards
    {
        private static void Postfix(ObjectDB __instance) => Builder.RegisterItems(__instance);
    }
}
