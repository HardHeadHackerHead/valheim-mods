using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;

namespace SkalTavern
{
    /// <summary>
    /// A mug holds four swigs. Using a drink (from the bag or the hotbar, the game's own way) takes one swig: the game's drinking animation and
    /// sound, a quarter of the drink, and a toast with whoever is near (Toast.Raise). The mug leaves the bag only with its last swig.
    ///
    /// How many swigs are left is kept on the stack, for its top mug (m_customData "SkalTavern.swigs", which the game saves with the item):
    /// when they run out the stack loses one mug and the next one starts full. A stack without the key (brewed, or from before swigs) is full.
    /// The counter belongs to the stack: split one and both halves carry it, put two together and the one you add to keeps its own.
    /// </summary>
    internal static class Swigs
    {
        public const int PerMug = 4;
        public const string Key = "SkalTavern.swigs";

        /// <summary>One of our drinks (by its name, which stays the same across a reload of the mod, unlike its type).</summary>
        public static bool IsDrink(ItemDrop.ItemData item) =>
            item?.m_shared != null && Drinks.All.Any(d => d.Display == item.m_shared.m_name);

        /// <summary>The swigs left in the top mug of this stack: a full mug when it has never been drunk from.</summary>
        public static int Left(ItemDrop.ItemData item)
        {
            if (item?.m_customData != null && item.m_customData.TryGetValue(Key, out string text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int left) && left >= 1 && left <= PerMug)
                return left;
            return PerMug;
        }

        /// <summary>One swig from the top mug; its last one takes the mug from the stack, and the next starts full.</summary>
        internal static void TakeOne(Inventory inventory, ItemDrop.ItemData item)
        {
            int left = Left(item) - 1;
            if (left > 0)
            {
                if (item.m_customData == null) item.m_customData = new Dictionary<string, string>();
                item.m_customData[Key] = left.ToString(CultureInfo.InvariantCulture);   // (only our own key: other mods keep theirs here too)
                inventory.m_onChanged?.Invoke();   // (a chest saves only when told: an item changed in place is not saved by itself)
            }
            else
            {
                item.m_customData?.Remove(Key);
                inventory.RemoveOneItem(item);
            }
        }
    }

    /// <summary>
    /// The game drinks the whole mug in Player.ConsumeItem (its effect, then RemoveOneItem). For our drinks this does it instead, a swig at a
    /// time: the game's own checks first (CanConsumeItem, with its own "you can't" messages), then the drink's effect (one swig's worth: see
    /// Drinks.MakeItem), then the swig, then the toast. Returning true lets Humanoid.UseItem play the drinking animation and sound as for any drink.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
    internal static class Player_ConsumeItem_Swig
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(Player __instance, Inventory inventory, ItemDrop.ItemData item, bool checkWorldLevel, ref bool __result, bool __runOriginal)
        {
            if (!__runOriginal) return false;   // (another mod has dealt with it: every prefix still runs, so never drink twice)
            if (!Swigs.IsDrink(item)) return true;
            __result = false;
            if (inventory == null) inventory = __instance.GetInventory();
            if (!inventory.ContainsItem(item) || !__instance.CanConsumeItem(item, checkWorldLevel)) return false;
            if (item.m_shared.m_consumeStatusEffect != null) __instance.GetSEMan().AddStatusEffect(item.m_shared.m_consumeStatusEffect, true);
            Swigs.TakeOne(inventory, item);
            Toast.Raise(__instance);
            __result = true;
            return false;
        }
    }

    /// <summary>The swigs left, at the bottom of the drink's tooltip (in the bag, a chest, and at the cauldron: a full mug there).</summary>
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
        new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
    internal static class ItemData_GetTooltip_Swigs
    {
        private static void Postfix(ItemDrop.ItemData item, bool appending, ref string __result)
        {
            if (appending || !Swigs.IsDrink(item)) return;
            __result += $"\n$skaltavern_swigs_left: <color=orange>{Swigs.Left(item)}/{Swigs.PerMug}</color>";
        }
    }
}
