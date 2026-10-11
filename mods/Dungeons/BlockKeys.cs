using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
using static DHack.Shared.Translations;

namespace Dungeons
{
    /// <summary>
    /// Key items: a pack's dungeon-only items ("items": {"smallkey": {"look": "CryptKey", "name": "$...", "description": "$..."}}), each a
    /// copy of a game item's look under our name quaddungeon_key_&lt;pack&gt;_&lt;name&gt; (TriggerReader.KeyPrefab), worth nothing at the trader.
    /// - Made and registered with the reward items (Builder.DefineItem, BuildRewards.cs: ZNetScene, ObjectDB and the main menu's, only
    ///   entries still ours taken out on unload), defined from the packs' raw JSON in the plugin's Awake (Define: no DungeonDB needed). A
    ///   pack added later (the config folder, a server) calls Add.
    /// - Bound to the dungeon that made them, as it is now (BoundKey: "&lt;dungeon id&gt;:&lt;resets&gt;" in the item's custom data): a lock
    ///   only takes its own dungeon's, and a reset makes the old ones worthless.
    /// - Taken from a player's inventory when they're outside the dungeon they belong to (once a second, never while they're held, dead or
    ///   teleporting): they can't leave, and a key from before a reset is gone.
    /// </summary>
    internal static class BlockKeys
    {
        public const string Prefix = "quaddungeon_key_";
        public const string BoundKey = "quaddungeon_keyof";

        public static bool IsKey(string prefab) => prefab != null && prefab.StartsWith(Prefix);

        // ---- binding

        /// <summary>What a dungeon's keys carry now: its id and how many times it was reset.</summary>
        public static string Binding(Site site) => site?.Zdo == null ? null : site.Id + ":" + site.Zdo.GetInt(Keys.Resets);

        public static void Bind(ItemDrop.ItemData item, Site site)
        {
            string b = Binding(site);
            if (item != null && b != null) item.m_customData[BoundKey] = b;
        }

        /// <summary>A key of this dungeon as it is now (one with no binding, given by a command, counts as any dungeon's).</summary>
        public static bool BoundTo(ItemDrop.ItemData item, Site site)
        {
            if (item == null) return false;
            if (!item.m_customData.TryGetValue(BoundKey, out string b)) return true;
            return b == Binding(site);
        }

        public static bool Has(Player p, string prefab, Site site) =>
            p != null && prefab != null && p.GetInventory().GetAllItems().Any(i => i?.m_dropPrefab != null && i.m_dropPrefab.name == prefab && BoundTo(i, site));

        /// <summary>One of this dungeon's keys taken from the player (a lock opened with it).</summary>
        public static void TakeOne(Player p, string prefab, Site site)
        {
            ItemDrop.ItemData key = p?.GetInventory().GetAllItems().FirstOrDefault(i => i?.m_dropPrefab != null && i.m_dropPrefab.name == prefab && BoundTo(i, site));
            if (key == null) return;
            p.GetInventory().RemoveItem(key, 1);
            Plugin.Log.LogInfo($"Dungeon {site.Id}: a lock took {prefab} from {p.GetPlayerName()}");
        }

        // ---- leaving

        private static float _next;

        /// <summary>The local player, once a second: keys outside their dungeon (or from before its reset) taken away; an item stuck on its
        /// way to a pedestal whose dungeon went given back; the checkpoint kept in step.</summary>
        public static void Tick(Player p)
        {
            if (p == null || p != Player.m_localPlayer || Time.time < _next) return;
            _next = Time.time + 1f;
            if (HotReload.Settling) return;   // (just reloaded: the dungeons aren't known and built here again yet, so "outside" means nothing)
            MechSet.SettleOrphan(p);
            Checkpoints.Tick(p);
            if (p.IsDead() || p.IsTeleporting() || Safety.Holding) return;
            Site site = Dungeons.Covering(p.transform.position);
            if (site != null && site.Zdo == null) return;   // (its state not at hand for a moment)
            string here = Binding(site);
            foreach (ItemDrop.ItemData item in p.GetInventory().GetAllItems().ToList())
            {
                if (item?.m_dropPrefab == null || !IsKey(item.m_dropPrefab.name)) continue;
                bool bound = item.m_customData.TryGetValue(BoundKey, out string b);
                if (site != null && (!bound || b == here)) continue;
                p.GetInventory().RemoveItem(item);
                p.Message(MessageHud.MessageType.Center, T("dungeons_key_gone", Localization.instance.Localize(item.m_shared.m_name)));
                Plugin.Log.LogInfo($"{item.m_dropPrefab.name} taken from {p.GetPlayerName()}: {(site == null ? "out of its dungeon" : "from before its dungeon's reset, or another dungeon's")}");
            }
        }

        // ---- registering (Builder.DefineItem, BuildRewards.cs: made and registered with the reward items)

        private static bool _definedBuiltIn;

        /// <summary>A pack's key items, to be made and registered (a pack added after the built-in ones: from the config folder or a
        /// server, dungeon-sync calls it). Safe to call again.</summary>
        public static void Add(JObject packJson)
        {
            string pack = (string)packJson?["name"];
            if (string.IsNullOrEmpty(pack) || !(packJson["items"] is JObject items)) return;
            foreach (JProperty p in items.Properties())
            {
                if (!(p.Value is JObject o) || o["look"]?.Type != JTokenType.String) continue;
                Builder.DefineItem(new Builder.ItemSpec
                {
                    Name = TriggerReader.KeyPrefab(pack, p.Name),
                    Look = (string)o["look"],
                    Display = o["name"]?.Type == JTokenType.String ? (string)o["name"] : p.Name,
                    Description = o["description"]?.Type == JTokenType.String ? (string)o["description"] : "",
                    Weight = 0.1f,
                    MaxStack = 10,
                    Teleportable = true,   // (it never leaves its dungeon anyway; a teleporter inside must take it along)
                });
            }
        }

        /// <summary>The built-in packs' key items (embedded packs/*.json), defined once, from the plugin's Awake: before any world loads, so
        /// keys saved in bags and on floors find their prefab.</summary>
        public static void Define()
        {
            if (_definedBuiltIn) return;
            _definedBuiltIn = true;
            var asm = typeof(BlockKeys).Assembly;
            foreach (string res in asm.GetManifestResourceNames().Where(r => r.StartsWith("packs/") && r.EndsWith(".json")))
            {
                try
                {
                    using (Stream s = asm.GetManifestResourceStream(res))
                    using (var reader = new StreamReader(s))
                        Add(JObject.Parse(reader.ReadToEnd()));
                }
                catch (Exception e) { Plugin.Log.LogWarning($"{res}: its key items can't be read: {e.Message}"); }
            }
        }
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class Dungeons_Player_Update_Keys
    {
        private static void Postfix(Player __instance)
        {
            if (!ReferenceEquals(__instance, Player.m_localPlayer)) return;
            try { BlockKeys.Tick(__instance); }
            catch (Exception e) { Plugin.Log.LogWarning("Dungeon keys and checkpoints: " + e.Message); }
        }
    }

    // A creature dying inside a dungeon, on its owner's game: told to the dungeon's owner (triggers' "kills").
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class Dungeons_Character_OnDeath_Kills
    {
        private static void Postfix(Character __instance)
        {
            try
            {
                if (__instance == null || __instance is Player || __instance.transform.position.y < 3000f) return;
                ZNetView v = __instance.GetComponent<ZNetView>();
                if (v == null || !v.IsValid() || !v.IsOwner()) return;
                Site site = Dungeons.Containing(__instance.transform.position);
                if (site?.Mechs == null || !site.Mechs.HasTriggers) return;
                site.Act("blk:kill:" + MechSet.Vec(__instance.transform.position));
            }
            catch (Exception) { }
        }
    }
}
