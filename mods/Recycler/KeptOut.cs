using System;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace Recycler
{
    /// <summary>
    /// Items the window leaves out so they aren't recycled by mistake: the hotbar, other mods' dedicated slots (equipment, quick, quiver),
    /// and items marked favourite. The other mods are reached by reflection (their own checks, so we agree with them about which cells
    /// are theirs), looked up the first time the window opens in a world: mods loaded after this one aren't listed before that.
    /// </summary>
    internal static class KeptOut
    {
        // Slot mods with no check to ask: their slots are the rows under the bag (the game's rows, bought ones included).
        private static readonly string[] RowSlotMods =
        {
            "randyknapp.mods.equipmentandquickslots", "aedenthorn.ExtendedPlayerInventory", "com.bruce.valheim.comfyquickslots",
            "odinplusqol.OdinsExtendedInventory", "com.odinplusqol.mod",
        };

        private static bool _looked, _rowSlots;
        private static MethodInfo _extraSlotsInSlot;              // ExtraSlots.API.IsItemInSlot(ItemData)
        private static PropertyInfo _azuNormalRows;               // AzuEPI's Layout.NormalInventoryRows (its slots are below them)
        private static MethodInfo _azuFavConfig, _qssFavConfig;   // UserConfig.GetPlayerConfig(long) of AzuEPI and Quick Stack Store
        private static MethodInfo _azuFavorited, _qssFavorited;   // UserConfig.IsItemNameOrSlotFavorited(ItemData)

        private static void Look()
        {
            if (_looked) return;
            _looked = true;
            try
            {
                if (Chainloader.PluginInfos.ContainsKey("shudnal.ExtraSlots"))
                    _extraSlotsInSlot = AccessTools.Method("ExtraSlots.API:IsItemInSlot", new[] { typeof(ItemDrop.ItemData) });
                if (Chainloader.PluginInfos.ContainsKey("Azumatt.AzuExtendedPlayerInventory"))
                {
                    _azuNormalRows = AccessTools.Property(AccessTools.TypeByName("AzuEPI.Core.InventoryHandlers.Layout"), "NormalInventoryRows");
                    Favourites("AzuEPI.Game.Favoriting.UserConfig", out _azuFavConfig, out _azuFavorited);
                    if (_azuNormalRows == null) _rowSlots = true; // (an older AzuEPI: fall back to the rows under the bag)
                }
                if (Chainloader.PluginInfos.ContainsKey("goldenrevolver.quick_stack_store"))
                    Favourites("QuickStackStore.UserConfig", out _qssFavConfig, out _qssFavorited);
                foreach (string guid in RowSlotMods) if (Chainloader.PluginInfos.ContainsKey(guid)) _rowSlots = true;
                if (Chainloader.PluginInfos.TryGetValue("ishid4.mods.betterarchery", out BepInEx.PluginInfo ba))
                    _rowSlots |= ba.Instance == null || !ba.Instance.Config.TryGetEntry("Quiver", "Enable Quiver", out BepInEx.Configuration.ConfigEntry<bool> quiver) || quiver.Value;
            }
            catch (Exception e) { Plugin.Log?.LogWarning($"Couldn't look up other mods' slots and favourites: {e.Message}"); }
        }

        private static void Favourites(string type, out MethodInfo config, out MethodInfo favorited)
        {
            Type t = AccessTools.TypeByName(type);
            config = t != null ? AccessTools.Method(t, "GetPlayerConfig", new[] { typeof(long) }) : null;
            favorited = t != null ? AccessTools.Method(t, "IsItemNameOrSlotFavorited", new[] { typeof(ItemDrop.ItemData) }) : null;
            if (config == null || favorited == null) Plugin.Log?.LogWarning($"{type} has changed: its favourites aren't kept out of the Recycler");
        }

        /// <summary>Is this item left out of the Recycler's list by the hotbar, slots or favourites settings?</summary>
        internal static bool Item(Player player, ItemDrop.ItemData item)
        {
            if (Plugin.SkipHotbar.Value && item.m_gridPos.y == 0) return true;
            if (Plugin.SkipSlots.Value && InSlot(player, item)) return true;
            if (Plugin.SkipFavorites.Value && Favourite(player, item)) return true;
            return false;
        }

        private static bool InSlot(Player player, ItemDrop.ItemData item)
        {
            Look();
            if (AppDomain.CurrentDomain.GetData("DHack.GearSlots.IsGear") is Func<ItemDrop.ItemData, bool> gear && gear(item)) return true;
            if (_extraSlotsInSlot != null && Ask(() => (bool)_extraSlotsInSlot.Invoke(null, new object[] { item }))) return true;
            if (_azuNormalRows != null && Ask(() => item.m_gridPos.y >= (int)_azuNormalRows.GetValue(null))) return true;
            if (_rowSlots && item.m_gridPos.y >= BagRows(player)) return true;
            return false;
        }

        /// <summary>The bag's rows: the game's 4, or more bought from the trader (the same count the game sets the height from).</summary>
        private static int BagRows(Player player) =>
            player.TryGetUniqueKeyValue(Player.InventoryRowsKey, out string value) && int.TryParse(value, out int rows) && rows > 0 ? rows : 4;

        private static bool Favourite(Player player, ItemDrop.ItemData item)
        {
            if (item.m_customData != null && item.m_customData.ContainsKey(Calc.QuickTag)) return true; // in QualityOfLife's quick set
            Look();
            long id = player.GetPlayerID();
            if (_azuFavConfig != null && _azuFavorited != null && Ask(() => (bool)_azuFavorited.Invoke(_azuFavConfig.Invoke(null, new object[] { id }), new object[] { item }))) return true;
            if (_qssFavConfig != null && _qssFavorited != null && Ask(() => (bool)_qssFavorited.Invoke(_qssFavConfig.Invoke(null, new object[] { id }), new object[] { item }))) return true;
            return false;
        }

        private static bool _complained;

        /// <summary>Another mod's answer; if it throws, the item is kept out (the safe side) and the log says so once.</summary>
        private static bool Ask(Func<bool> question)
        {
            try { return question(); }
            catch (Exception e)
            {
                if (!_complained) { _complained = true; Plugin.Log?.LogWarning($"Another mod's slot or favourite check failed, so its items are kept out: {e.GetBaseException().Message}"); }
                return true;
            }
        }
    }
}
