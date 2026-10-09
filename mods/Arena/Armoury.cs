using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Arena
{
    /// <summary>
    /// Upgrading the arena's steel as you go (the Long Road and today's trial). You keep what you wear from round to round and land to land;
    /// each piece (weapon, shield, helmet, chest, legs) can be raised to the next land's, never more than one land ahead of the fight. An
    /// upgrade comes three ways: one at random in the chest that rises after every round, from the crowd now and then, or bought from the
    /// Armourer, who stands in the ring between rounds with fresh food, meads and arrows too. Coins are what the fighters drop.
    /// </summary>
    internal static class Armoury
    {
        internal enum Slot { Weapon, Shield, Helmet, Chest, Legs }
        internal static readonly string[] SlotNames = { "Weapon", "Shield", "Helmet", "Chest", "Legs" };

        // what an upgrade to a land's piece costs, by land, steeper and steeper; a shield is cheaper, armour in between
        private static readonly int[] Base = { 20, 80, 200, 400, 700, 1100, 1600 };

        private static string[] List(Slot slot, int style)
        {
            switch (slot)
            {
                case Slot.Weapon: return Kit.Weapons[Mathf.Clamp(style, 0, Kit.Styles.Length - 1)];
                case Slot.Shield: return Kit.Shields;
                default: return Kit.Armour.Select(a => a[(int)slot - 2]).ToArray();
            }
        }

        private static string PrefabOf(string entry) { int c = entry.IndexOf(':'); return c > 0 ? entry.Substring(0, c) : entry; }
        private static int QualityOf(string entry) { int c = entry.IndexOf(':'); return c > 0 && int.TryParse(entry.Substring(c + 1), out int q) ? q : 2; }

        /// <summary>The pieces a fighter of this style wears (no shield with the bow; armour only with bare fists).</summary>
        internal static List<Slot> Slots(int style, bool fists)
        {
            if (fists) return new List<Slot> { Slot.Helmet, Slot.Chest, Slot.Legs };
            var slots = new List<Slot> { Slot.Weapon, Slot.Shield, Slot.Helmet, Slot.Chest, Slot.Legs };
            if (style == 4) slots.Remove(Slot.Shield);
            return slots;
        }

        /// <summary>The best land's piece of a slot the player has (-1: none).</summary>
        internal static int TierOf(Player player, Slot slot, int style)
        {
            string[] list = List(slot, style);
            int best = -1;
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            {
                string name = item.m_dropPrefab != null ? item.m_dropPrefab.name : "";
                for (int i = list.Length - 1; i > best; i--)
                    if (PrefabOf(list[i]) == name && item.m_quality >= QualityOf(list[i])) { best = i; break; }
            }
            return best;
        }

        /// <summary>How far a free upgrade (the reward chest's, the crowd's) may go: one land ahead of the fight. The Armourer sells any, for coin.</summary>
        internal static int Cap => Mathf.Min(Contest.Tier + 1, Roster.TierNames.Length - 1);

        internal static int Price(Slot slot, int tier) => Mathf.RoundToInt(Base[Mathf.Clamp(tier, 0, Base.Length - 1)] * (slot == Slot.Weapon ? 1f : slot == Slot.Shield ? 0.4f : 0.6f));

        /// <summary>The next piece for a slot (with <paramref name="capped"/>, only if it is no more than a land ahead of the fight).</summary>
        internal static bool Next(Player player, Slot slot, int style, out int tier, out string prefab, out int quality, bool capped = false)
        {
            tier = TierOf(player, slot, style) + 1;
            prefab = null; quality = 2;
            if (capped && tier > Cap || tier >= Roster.TierNames.Length) return false;
            string[] list = List(slot, style);
            // (a list can name the same piece twice, at a better quality: skip to the next one that is really better)
            prefab = PrefabOf(list[tier]); quality = QualityOf(list[tier]);
            return Roster.Has(prefab);
        }

        internal static string Name(Player player, Slot slot, int style)
        {
            int t = TierOf(player, slot, style);
            if (t < 0) return "none";
            string[] list = List(slot, style);
            return Contest.ItemName(PrefabOf(list[t])) + (QualityOf(list[t]) > 2 ? " ★" + QualityOf(list[t]) : "");
        }

        /// <summary>One upgrade picked at random from the slots that can take one (null: everything is as good as it may be yet).</summary>
        internal static (string Prefab, int Amount, int Quality, string Loan)? Random(Player player, int style, bool fists, string loan)
        {
            var open = Slots(style, fists).Where(s => Next(player, s, style, out _, out _, out _, true)).ToList();
            if (open.Count == 0) return null;
            Slot pick = open[UnityEngine.Random.Range(0, open.Count)];
            Next(player, pick, style, out _, out string prefab, out int quality, true);
            return (prefab, 1, quality, loan);
        }

        /// <summary>
        /// An upgrade just bought in the store (which adds it plain, at the prefab's quality): it is made the arena's piece for its land, at
        /// its quality, and put on; what it replaces goes back.
        /// </summary>
        internal static void Bought(Player player, Slot slot, string prefab, int style)
        {
            if (!Next(player, slot, style, out int tier, out string want, out int quality) || want != prefab) return;
            ItemDrop.ItemData item = player.GetInventory().GetAllItems().LastOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab && !Kit.IsLoan(i));
            if (item == null) return;
            item.m_quality = Mathf.Clamp(quality, 1, item.m_shared.m_maxQuality);
            item.m_durability = item.GetMaxDurability();
            item.m_customData[Kit.LoanKey] = tier.ToString();
            player.EquipItem(item, false);
            Tidy(player, style);
        }

        /// <summary>The lent pieces you have outgrown (a better one of the same slot in the bag, and this one not worn) go back.</summary>
        internal static void Tidy(Player player, int style)
        {
            if (player == null) return;
            Inventory inv = player.GetInventory();
            foreach (Slot slot in new[] { Slot.Weapon, Slot.Shield, Slot.Helmet, Slot.Chest, Slot.Legs })
            {
                string[] list = List(slot, style);
                int best = TierOf(player, slot, style);
                if (best < 0) continue;
                foreach (ItemDrop.ItemData item in inv.GetAllItems().ToList())
                {
                    if (!Kit.IsLoan(item) || item.m_equipped || item.m_dropPrefab == null) continue;
                    string name = item.m_dropPrefab.name;
                    int at = System.Array.FindLastIndex(list, e => PrefabOf(e) == name && item.m_quality >= QualityOf(e));
                    if (at >= 0 && at < best) inv.RemoveItem(item);
                }
            }
        }

        // ---- prices of the rest ---------------------------------------------------------------------------------------------------

        // a fresh meal costs about what two fighters of its land drop; a mead a little more; arrows a little less
        internal static int MealPrice(int land) => 10 + 10 * land;
        internal static int HealingPrice(int land) => 15 + 12 * land;
        internal static int StaminaPrice(int land) => 12 + 8 * land;
        internal static int ArrowsPrice(int land) => 10 + 8 * land;

        /// <summary>Another kind of weapon, as good as the one you have: half what an upgrade to it would cost.</summary>
        internal static int SwitchPrice(int tier) => Mathf.Max(20, Price(Slot.Weapon, Mathf.Max(1, tier)) / 2);

        /// <summary>A weapon of another kind bought: from now on the upgrades are of that kind (and a bow comes with arrows and a knife).</summary>
        internal static void Switched(Player player, int style, string prefab, int tier, int quality)
        {
            ItemDrop.ItemData item = player.GetInventory().GetAllItems().LastOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab && !Kit.IsLoan(i));
            if (item == null) return;
            item.m_quality = Mathf.Clamp(quality, 1, item.m_shared.m_maxQuality);
            item.m_durability = item.GetMaxDurability();
            item.m_customData[Kit.LoanKey] = tier.ToString();
            Contest.Style = style;
            if (style == 4)
            {
                foreach (var extra in new[] { (Kit.ArrowFor(tier), 60), (Kit.Knives[Mathf.Clamp(tier, 0, Kit.Knives.Length - 1)].Split(':')[0], 1) })
                    if (extra.Item1 != null && Roster.Has(extra.Item1))
                    {
                        ItemDrop.ItemData x = player.GetInventory().AddItem(extra.Item1, extra.Item2, 2, 0, 0L, "The Arena", false);
                        if (x != null) x.m_customData[Kit.LoanKey] = tier.ToString();
                    }
            }
            player.EquipItem(item, false);
        }

        // ---- what the fighters drop -----------------------------------------------------------------------------------------------

        private static readonly int[] CoinsByLand = { 6, 12, 20, 30, 42, 56, 72 };

        /// <summary>A fighter of the arena falls: coins spill where it fell (a champion's, a pile, with its trophy and the land's metal).</summary>
        internal static void Spill(Vector3 at, int land, int level, bool champion)
        {
            float mult = Rules.Multiplier * (1f + Crowd.Favour / 200f) * Plugin.Rewards.Value / 100f;
            int coins = Mathf.Max(1, Mathf.RoundToInt(CoinsByLand[Mathf.Clamp(land, 0, CoinsByLand.Length - 1)] * UnityEngine.Random.Range(0.8f, 1.2f) * (1f + 0.5f * (level - 1)) * (champion ? 5f : 1f) * mult));
            Drop("Coins", coins, at);
        }

        internal static void Drop(string prefab, int amount, Vector3 at)
        {
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            ItemDrop item = go != null ? go.GetComponent<ItemDrop>() : null;
            if (item == null || amount <= 0) return;
            int max = Mathf.Max(1, item.m_itemData.m_shared.m_maxStackSize);
            while (amount > 0)
            {
                int n = Mathf.Min(amount, max);
                amount -= n;
                GameObject made = Object.Instantiate(go, at + Vector3.up * 0.8f + Random3(), Quaternion.identity);
                made.GetComponent<ItemDrop>()?.SetStack(n);
                made.GetComponent<Rigidbody>()?.AddForce(Vector3.up * 3f + Random3() * 2f, ForceMode.VelocityChange);
            }
        }

        private static Vector3 Random3() => new Vector3(UnityEngine.Random.Range(-0.4f, 0.4f), 0f, UnityEngine.Random.Range(-0.4f, 0.4f));
    }

    /// <summary>
    /// The Armourer, in the ring between lands by the gate. Press E for his wares, in the game's own store (Haldor's): each upgrade for what
    /// you wear (put on at once; what it replaces goes back to him), fresh food, meads and arrows. Shift+E: bring on the next round.
    /// The store needs a Trader: one is kept switched off on a child of the figure (so the game's trader talk and looking about never run),
    /// its wares refilled each time the store opens and after every purchase.
    /// </summary>
    internal class Armourer : MonoBehaviour, Hoverable, Interactable
    {
        private Trader _trader;
        internal static readonly Dictionary<Trader.TradeItem, Armoury.Slot> Upgrades = new Dictionary<Trader.TradeItem, Armoury.Slot>();
        internal static readonly Dictionary<Trader.TradeItem, (int Style, int Tier, int Quality)> Switches = new Dictionary<Trader.TradeItem, (int, int, int)>();

        public string GetHoverText() => Localization.instance.Localize("The Armourer\n[<color=yellow><b>$KEY_Use</b></color>] Upgrades, fresh food and meads\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Bring them on!");
        public string GetHoverName() => "The Armourer";
        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer) return false;
            if (alt) { Contest.ReadyEarly(); return true; }
            if (_trader == null)
            {
                var go = new GameObject("ArmourerStore");
                go.transform.SetParent(transform, false);
                _trader = go.AddComponent<Trader>();
                _trader.enabled = false;
                _trader.m_name = "The Armourer";
            }
            Restock(_trader);
            StoreGui.instance?.Show(_trader);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        internal static bool Ours(Trader trader) => trader != null && trader.transform.parent != null && trader.transform.parent.GetComponent<Armourer>() != null;

        /// <summary>What the Armourer has for you now.</summary>
        internal static void Restock(Trader trader)
        {
            Player me = Player.m_localPlayer;
            trader.m_items.Clear();
            Upgrades.Clear();
            Switches.Clear();
            if (me == null) return;
            int land = Contest.Tier, style = Contest.Style;
            foreach (Armoury.Slot slot in Armoury.Slots(style, Rules.Fists))
            {
                if (!Armoury.Next(me, slot, style, out int tier, out string prefab, out int quality)) continue;
                ItemDrop drop = ZNetScene.instance.GetPrefab(prefab)?.GetComponent<ItemDrop>();
                if (drop == null) continue;
                ItemDrop.ItemData shown = drop.m_itemData.Clone();
                shown.m_quality = Mathf.Clamp(quality, 1, shown.m_shared.m_maxQuality);
                var item = Item(drop, 1, Armoury.Price(slot, tier),
                    $"<color=orange>Upgrade, {Armoury.SlotNames[(int)slot].ToLowerInvariant()}</color>: replaces your {Armoury.Name(me, slot, style)}, and is put on at once. Lent, like all the arena's gear.\n\n" + shown.GetTooltip(1));
                trader.m_items.Add(item);
                Upgrades[item] = slot;
            }
            // another kind of weapon, as good as yours (your upgrades follow it from then on)
            if (!Rules.Fists)
            {
                int now = Mathf.Max(0, Armoury.TierOf(me, Armoury.Slot.Weapon, style));
                for (int other = 0; other < Kit.Styles.Length; other++)
                {
                    if (other == style) continue;
                    string entry = Kit.Weapons[other][now];
                    string prefab = entry.Split(':')[0];
                    int quality = entry.Contains(":") && int.TryParse(entry.Split(':')[1], out int q) ? q : 2;
                    ItemDrop drop = ZNetScene.instance.GetPrefab(prefab)?.GetComponent<ItemDrop>();
                    if (drop == null) continue;
                    ItemDrop.ItemData shown = drop.m_itemData.Clone();
                    shown.m_quality = Mathf.Clamp(quality, 1, shown.m_shared.m_maxQuality);
                    var item = Item(drop, 1, Armoury.SwitchPrice(now),
                        $"<color=orange>Fight with {(other == 4 ? "a bow (with arrows and a knife)" : "a " + Kit.Styles[other].ToLowerInvariant())}</color>: as good as your {Armoury.Name(me, Armoury.Slot.Weapon, style)}, and from now on your weapon upgrades are of this kind.\n\n" + shown.GetTooltip(1));
                    trader.m_items.Add(item);
                    Switches[item] = (other, now, quality);
                }
            }
            if (!Rules.NoFood)
            {
                foreach (var meal in Kit.Menu(land)) Add(trader, meal.Prefab, 1, Armoury.MealPrice(land));
                Add(trader, Kit.Meads[Mathf.Clamp(land, 0, Kit.Meads.Length - 1)], 1, Armoury.HealingPrice(land));
                Add(trader, land < 3 ? "MeadStaminaMinor" : land < 6 ? "MeadStaminaMedium" : "MeadStaminaLingering", 1, Armoury.StaminaPrice(land));
            }
            if (style == 4 && !Rules.Fists && Kit.ArrowFor(land) != null) Add(trader, Kit.ArrowFor(land), 30, Armoury.ArrowsPrice(land));
        }

        private static void Add(Trader trader, string prefab, int stack, int price)
        {
            ItemDrop drop = ZNetScene.instance.GetPrefab(prefab)?.GetComponent<ItemDrop>();
            if (drop != null) trader.m_items.Add(Item(drop, stack, price, ""));
        }

        private static Trader.TradeItem Item(ItemDrop drop, int stack, int price, string tooltip) =>
            new Trader.TradeItem { m_prefab = drop, m_stack = stack, m_price = price, m_tooltip = tooltip ?? "", m_buyPlayerEffects = new EffectList(), m_name = "" };
    }

    /// <summary>A purchase from the Armourer: an upgrade is made the arena's (lent, at its quality) and put on, the old one goes back.</summary>
    [HarmonyLib.HarmonyPatch(typeof(Trader), nameof(Trader.OnBought))]
    internal static class Trader_OnBought_Armourer
    {
        private static bool Prefix(Trader __instance, Trader.TradeItem item)
        {
            if (!Armourer.Ours(__instance)) return true;
            Player me = Player.m_localPlayer;
            if (me != null && Armourer.Upgrades.TryGetValue(item, out Armoury.Slot slot)) Armoury.Bought(me, slot, item.m_prefab.gameObject.name, Contest.Style);
            else if (me != null && Armourer.Switches.TryGetValue(item, out var sw)) Armoury.Switched(me, sw.Style, item.m_prefab.gameObject.name, sw.Tier, sw.Quality);
            Net.Sound("cheer");
            Armourer.Restock(__instance);   // (the store lists them again right after)
            return false;                   // (no trader talk: the game's would need a trader's body)
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Trader), nameof(Trader.OnSold))]
    internal static class Trader_OnSold_Armourer
    {
        private static bool Prefix(Trader __instance)
        {
            if (!Armourer.Ours(__instance)) return true;
            Armourer.Restock(__instance);
            return false;
        }
    }
}
