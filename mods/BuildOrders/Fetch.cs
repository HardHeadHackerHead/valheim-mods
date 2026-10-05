using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// With the hammer out and a piece selected: a hotkey that takes one stack of each material that piece needs out of the chests around you and
    /// puts it in your inventory. The chest access is BuildFromChests' (same range, same rules about protected and busy chests), reached through two
    /// small functions it leaves in the app domain's shared data, so nothing is fetched if that mod is not installed.
    /// </summary>
    public partial class Plugin
    {
        private ConfigEntry<KeyCode> _fetchKey;

        private const string TakeKey = "DHack.BuildFromChests.Take";
        private const string CountKey = "DHack.BuildFromChests.Count";

        internal class FetchLine
        {
            public string Item, Name;
            public Sprite Icon;
            public GameObject Prefab;
            public int Stack, InInventory, InChests;

            /// <summary>What one press takes: a full stack of it, or what the chests have if that is less.</summary>
            public int Take => Mathf.Min(Stack, InChests);

            /// <summary>The chests have some and you hold less than a full stack.</summary>
            public bool Short => InChests > 0 && InInventory < Stack;
        }

        internal readonly List<FetchLine> FetchLines = new List<FetchLine>();
        internal bool FetchAvailable;
        internal string FetchPieceName = "";
        private float _fetchAt;

        private static Func<string, int, int> ChestTake => AppDomain.CurrentDomain.GetData(TakeKey) as Func<string, int, int>;
        private static Func<string, int> ChestCount => AppDomain.CurrentDomain.GetData(CountKey) as Func<string, int>;

        private void BindFetch()
        {
            _fetchKey = Config.Bind("Keys", "FetchKey", KeyCode.Y,
                "With the hammer out and a piece selected: press this to take one stack of each material it needs out of the chests around you and put it in your inventory (needs BuildFromChests).");
        }

        private void UpdateFetch(Player player)
        {
            FetchAvailable = false;
            if (!_enabled.Value || !player.InPlaceMode() || PlanKeyHeld || InventoryGui.IsVisible() || TypingOrMenuOpen()) return;
            Func<string, int> count = ChestCount;
            if (count == null) return; // BuildFromChests is not loaded

            Piece piece = player.GetSelectedPiece();
            if (piece == null || piece.m_resources == null || piece.m_repairPiece || piece.m_removePiece) return;

            if (Time.unscaledTime - _fetchAt >= 0.4f)
            {
                _fetchAt = Time.unscaledTime;
                FetchPieceName = Localization.instance.Localize(piece.m_name);
                FetchLines.Clear();
                Inventory inventory = player.GetInventory();
                foreach (Piece.Requirement req in piece.m_resources)
                {
                    if (req.m_resItem == null || req.GetAmount(1) <= 0) continue;
                    string item = req.m_resItem.m_itemData.m_shared.m_name;
                    FetchLines.Add(new FetchLine
                    {
                        Item = item, Name = Localization.instance.Localize(item), Icon = req.m_resItem.m_itemData.GetIcon(), Prefab = req.m_resItem.gameObject,
                        Stack = Mathf.Max(1, req.m_resItem.m_itemData.m_shared.m_maxStackSize),
                        InInventory = inventory.GetAllItems().Where(i => i.m_shared.m_name == item).Sum(i => i.m_stack), // yours only (not the chests' count)
                        InChests = count(item),
                    });
                }
            }
            FetchAvailable = FetchLines.Any(l => l.Short);

            if (FetchAvailable && Input.GetKeyDown(_fetchKey.Value)) Fetch(player);
        }

        private void Fetch(Player player)
        {
            Func<string, int, int> take = ChestTake;
            if (take == null) return;
            Inventory inventory = player.GetInventory();
            var took = new List<string>();
            bool noRoom = false;

            foreach (FetchLine line in FetchLines.Where(l => l.Short).ToList())
            {
                int want = line.Take;
                if (!inventory.CanAddItem(line.Prefab, want)) { noRoom = true; continue; } // check before taking, so nothing is ever taken that has no place to go
                int got = take(line.Item, want);
                if (got <= 0) continue;
                if (!inventory.AddItem(line.Prefab, got))
                {
                    // should not happen after the check above; if it does, the items must not vanish
                    GameObject drop = Instantiate(line.Prefab, player.transform.position + player.transform.forward + Vector3.up, Quaternion.identity);
                    drop.GetComponent<ItemDrop>().SetStack(got);
                }
                took.Add($"{got} {line.Name}");
            }

            _fetchAt = 0f; // recount straight away
            if (took.Count > 0) player.Message(MessageHud.MessageType.TopLeft, "Took from the chests: " + string.Join(", ", took.ToArray()) + (noRoom ? "  (no room for the rest)" : ""));
            else player.Message(MessageHud.MessageType.Center, noRoom ? "No room in your inventory" : "Nothing to take from the chests");
        }
    }
}
