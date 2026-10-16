using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Arena
{
    /// <summary>
    /// What the arena owes a player, held by the Arena Master until they come to his desk for it. It is kept on the character (its saved
    /// custom data, which the game keeps through death and logging out), so it is only ever that player's: nobody else can take it, and it is
    /// not lost when they leave the game, die or the mod reloads before collecting. What does not fit in their bag stays with him.
    /// </summary>
    internal static class Prize
    {
        internal const string Key = "dh_arena_prize";

        /// <summary>What is held for the player: prefab name and how many.</summary>
        internal static Dictionary<string, int> Held(Player p)
        {
            var held = new Dictionary<string, int>();
            if (p == null || !p.m_customData.TryGetValue(Key, out string data) || string.IsNullOrEmpty(data)) return held;
            foreach (string part in data.Split(','))
            {
                int star = part.LastIndexOf('*');
                if (star <= 0 || !int.TryParse(part.Substring(star + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n <= 0) continue;
                string prefab = part.Substring(0, star);
                held[prefab] = (held.TryGetValue(prefab, out int had) ? had : 0) + n;
            }
            return held;
        }

        internal static bool Waiting(Player p) => Held(p).Count > 0;

        private static void Save(Player p, Dictionary<string, int> held)
        {
            if (held.Count == 0) { p.m_customData.Remove(Key); return; }
            p.m_customData[Key] = string.Join(",", held.Where(h => h.Value > 0).Select(h => h.Key + "*" + h.Value.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>The Arena Master holds this for the player, with whatever he holds already.</summary>
        internal static void Hold(Player p, string prefab, int amount)
        {
            if (p == null || string.IsNullOrEmpty(prefab) || amount <= 0) return;
            var held = Held(p);
            held[prefab] = (held.TryGetValue(prefab, out int had) ? had : 0) + amount;
            Save(p, held);
        }

        internal static string Text(Dictionary<string, int> things) =>
            string.Join(", ", things.Select(t => t.Key == "Coins" ? T("arena_coins", t.Value)
                                               : t.Key.StartsWith("Trophy") && t.Value == 1 ? Contest.ItemName(t.Key)
                                               : T("arena_prize_item", t.Value, Contest.ItemName(t.Key))));

        /// <summary>
        /// At the Arena Master's desk (his Prize tab, a chest only you can open and only take from): everything he holds, as much as your bag
        /// takes; the rest stays with him. Not while a contest of yours is on (your own bag is with him then).
        /// </summary>
        internal static void Collect(Player p)
        {
            if (!CanCollect(p)) return;
            bool full = false;
            foreach (string prefab in new List<string>(Held(p).Keys)) full |= !Take(p, prefab, false);
            if (full) p.Message(MessageHud.MessageType.Center, T("arena_prize_bag_full"));
        }

        /// <summary>One thing he holds, as much of it as your bag takes. False when some of it stays with him (no room).</summary>
        internal static bool Take(Player p, string prefab, bool say = true)
        {
            if (!CanCollect(p)) return true;
            var held = Held(p);
            if (!held.TryGetValue(prefab, out int amount) || amount <= 0) return true;
            GameObject go = ZNetScene.instance.GetPrefab(prefab);
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop == null) return false;   // (a thing the game no longer has: kept, should it come back)
            Inventory inv = p.GetInventory();
            int max = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize), rest = amount;
            while (rest > 0)
            {
                int n = Mathf.Min(rest, max);
                if (!inv.CanAddItem(go, n)) n = Mathf.Min(n, inv.FindFreeStackSpace(drop.m_itemData.m_shared.m_name, Game.m_worldLevel));
                if (n <= 0 || !inv.CanAddItem(go, n) || !inv.AddItem(go, n)) break;
                rest -= n;
            }
            if (rest > 0) held[prefab] = rest; else held.Remove(prefab);
            Save(p, held);
            if (amount - rest > 0) Plugin.Log.LogInfo($"Took {prefab} x{amount - rest} from the Arena Master" + (rest > 0 ? $" ({rest} still held: no room)" : ""));
            if (rest > 0 && say) p.Message(MessageHud.MessageType.Center, T("arena_prize_bag_full"));
            return rest == 0;
        }

        private static bool CanCollect(Player p) => p != null && !Contest.Active && !Kit.Stowed(p) && ZNetScene.instance != null;
    }
}
