using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// With the hammer out and a piece selected: a hotkey that takes exactly what that piece needs out of the chests around you and puts it in
    /// your inventory. Holding less than one piece's worth, it tops you up to one; holding enough, each press brings another piece's worth. The chest access is BuildFromChests' (same range, same rules about protected and busy chests), reached through two
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
            public int Need, InInventory, InChests;

            /// <summary>What one press takes (worked out with the other materials: see FetchTopUp), no more than the chests have.</summary>
            public int Take;

            /// <summary>One press takes some of it.</summary>
            public bool Short => Take > 0;
        }

        /// <summary>You hold less than one piece's worth of something: a press tops you up to one. Otherwise a press brings another piece's worth.</summary>
        internal bool FetchTopUp;

        internal readonly List<FetchLine> FetchLines = new List<FetchLine>();
        internal bool FetchAvailable;
        internal string FetchPieceName = "";
        private float _fetchAt;

        private static Func<string, int, int> ChestTake => AppDomain.CurrentDomain.GetData(TakeKey) as Func<string, int, int>;
        private static Func<string, int> ChestCount => AppDomain.CurrentDomain.GetData(CountKey) as Func<string, int>;

        private void BindFetch()
        {
            _fetchKey = Config.Bind("Keys", "FetchKey", KeyCode.Y,
                "With the hammer out and a piece selected: press this to take exactly what it needs out of the chests around you (topping you up to one piece's worth, or another piece's worth when you have enough) and put it in your inventory (needs BuildFromChests).");
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
                        Need = req.GetAmount(1),
                        InInventory = inventory.GetAllItems().Where(i => i.m_shared.m_name == item).Sum(i => i.m_stack), // yours only (not the chests' count)
                        InChests = count(item),
                    });
                }
                FetchTopUp = FetchLines.Any(l => l.InInventory < l.Need);
                foreach (FetchLine l in FetchLines)
                    l.Take = Mathf.Min(l.InChests, FetchTopUp ? Mathf.Max(0, l.Need - l.InInventory) : l.Need);
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
