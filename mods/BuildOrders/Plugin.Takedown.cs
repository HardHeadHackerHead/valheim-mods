using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// Taking down what was built of a plan. Every placed blueprint keeps the list of its pieces (BepInEx/blueprints/_plans); a real piece
    /// standing where one of them was planned, of the same kind, was built from the plan. Taking the plan down removes those pieces the way
    /// the hammer does (wards respected, chests with things in them left alone) and puts what they were made of into your inventory (what does
    /// not fit drops at your feet): the same materials the game gives back when you take a piece down by hand.
    /// The list goes to the other players with the orders (and to anyone who joins later), so whoever removes the plan can take it down.
    /// </summary>
    public partial class Plugin
    {
        private static string PlanRecordDir => Path.Combine(BlueprintDir, "_plans");

        private string PlanRecordFile(string key) => Path.Combine(PlanRecordDir, Safe((_loadedWorld ?? "world") + "__" + key) + ".json");

        /// <summary>Remember a plan's pieces as placed (called when a blueprint's ghosts are placed), and tell the others.</summary>
        private void RecordPlan(string key)
        {
            try
            {
                Directory.CreateDirectory(PlanRecordDir);
                var doc = new JObject
                {
                    ["plan"] = key,
                    ["time"] = DateTime.UtcNow.Ticks,                     // the newest list for a plan name wins (names are used again)
                    ["by"] = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "",
                    ["levelled"] = File.Exists(UndoFile(key)),          // only this player's game can put its ground back
                    ["pieces"] = new JArray(_orders.Values.Where(o => o.By == key).Select(o => new JArray(o.Prefab, Math.Round(o.Pos.x, 2), Math.Round(o.Pos.y, 2), Math.Round(o.Pos.z, 2)))),
                };
                string text = doc.ToString(Newtonsoft.Json.Formatting.None);
                File.WriteAllText(PlanRecordFile(key), text);
                Send("P|" + text);
            }
            catch (Exception e) { Logger.LogWarning("Could not record the plan's pieces: " + e.Message); }
        }

        private void ForgetPlan(string key)
        {
            try { File.Delete(PlanRecordFile(key)); } catch (Exception) { }
        }

        private JObject ReadPlanRecord(string key)
        {
            try { string path = PlanRecordFile(key); return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null; }
            catch (Exception) { return null; }
        }

        /// <summary>Someone placed a plan (or answered a newcomer): keep its piece list, unless ours is newer.</summary>
        private void OnPlanRecord(string text)
        {
            JObject doc = JObject.Parse(text);
            string key = (string)doc["plan"];
            if (string.IsNullOrEmpty(key) || !(doc["pieces"] is JArray)) return;
            JObject have = ReadPlanRecord(key);
            if (have != null && (long?)have["time"] >= (long?)doc["time"]) return;
            Directory.CreateDirectory(PlanRecordDir);
            File.WriteAllText(PlanRecordFile(key), text);
        }

        /// <summary>Send a newcomer the piece lists of the plans that still have ghosts.</summary>
        private void SendPlanRecords(long target)
        {
            foreach (string key in _orders.Values.Select(o => o.By ?? "").Where(b => b.StartsWith(BlueprintPrefix)).Distinct().ToList())
            {
                string path = PlanRecordFile(key);
                try { if (File.Exists(path)) Send("P|" + File.ReadAllText(path), target); }
                catch (Exception) { }
            }
        }

        /// <summary>The real pieces built from a plan (those it planned that are no longer ghosts and now stand there).</summary>
        internal List<Piece> BuiltPieces(string key)
        {
            var built = new List<Piece>();
            string path = PlanRecordFile(key);
            if (!File.Exists(path)) return built;
            try
            {
                var near = new List<Piece>();
                foreach (JArray p in ((JArray)JObject.Parse(File.ReadAllText(path))["pieces"]).OfType<JArray>())
                {
                    string prefab = (string)p[0];
                    var pos = new Vector3((float)p[1], (float)p[2], (float)p[3]);
                    if (_orders.Values.Any(o => o.By == key && o.Prefab == prefab && (o.Pos - pos).sqrMagnitude < 0.04f)) continue; // still a ghost
                    near.Clear();
                    Piece.GetAllPiecesInRadius(pos, 0.3f, near);
                    Piece piece = near.FirstOrDefault(pc => pc != null && Utils.GetPrefabName(pc.gameObject) == prefab && !built.Contains(pc));
                    if (piece != null) built.Add(piece);
                }
            }
            catch (Exception e) { Logger.LogWarning("Could not read the plan's pieces: " + e.Message); }
            return built;
        }

        /// <summary>Take down what was built of a plan and give its materials back. Returns how many pieces came down, and how many were left.</summary>
        internal int TakeDownBuilt(Player player, string key, out int left)
        {
            left = 0;
            int down = 0;
            bool free = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildCost);
            var refund = new Dictionary<GameObject, int>();
            foreach (Piece piece in BuiltPieces(key))
            {
                if (piece == null) continue;
                WearNTear wear = piece.GetComponent<WearNTear>();
                ZNetView view = piece.GetComponent<ZNetView>();
                bool chestWithThings = piece.TryGetComponent(out Container chest) && chest.GetInventory() != null && chest.GetInventory().NrOfItems() > 0;
                if (!piece.m_canBeRemoved || chestWithThings || view == null || !view.IsValid() || !PrivateArea.CheckAccess(piece.transform.position, 0f, false, false))
                {
                    left++;
                    continue;
                }
                if (!free)
                    foreach (Piece.Requirement req in piece.m_resources)
                    {
                        if (req.m_resItem == null || !req.m_recover || req.m_amount <= 0) continue;
                        GameObject item = ObjectDB.instance.GetItemPrefab(Utils.GetPrefabName(req.m_resItem.gameObject));
                        if (item != null) refund[item] = (refund.TryGetValue(item, out int n) ? n : 0) + req.m_amount;
                    }
                if (wear != null) wear.Remove(blockDrop: true);    // the game's own removal, without dropping the materials (they go to you instead)
                else { view.ClaimOwnership(); ZNetScene.instance.Destroy(piece.gameObject); }
                down++;
            }

            // the materials: into your inventory, the rest at your feet
            Inventory inv = player.GetInventory();
            int dropped = 0;
            foreach (var kv in refund)
            {
                ItemDrop drop = kv.Key.GetComponent<ItemDrop>();
                if (drop == null) continue;
                int amount = kv.Value, max = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
                while (amount > 0)
                {
                    int stack = Mathf.Min(amount, max);
                    if (inv.CanAddItem(kv.Key, stack)) inv.AddItem(kv.Key, stack);
                    else
                    {
                        // a fresh copy of the item: the prefab's own ItemData has no drop prefab set, so ItemDrop.DropItem would throw
                        GameObject loose = Instantiate(kv.Key, player.transform.position + Vector3.up, Quaternion.identity);
                        loose.GetComponent<ItemDrop>().SetStack(stack);
                        dropped += stack;
                    }
                    amount -= stack;
                }
            }
            if (down > 0)
            {
                string got = string.Join(", ", refund.Select(kv => $"{kv.Value} {Localization.instance.Localize(kv.Key.GetComponent<ItemDrop>().m_itemData.m_shared.m_name)}").ToArray());
                player.Message(MessageHud.MessageType.Center, $"Took down {down} built piece(s)" + (got.Length > 0 ? $": got back {got}" : "") + (dropped > 0 ? " (some at your feet: your bags are full)" : ""));
                Logger.LogInfo($"Plan '{key}': took down {down} built pieces, {left} left standing; refunded {got}");
            }
            return down;
        }
    }
}
