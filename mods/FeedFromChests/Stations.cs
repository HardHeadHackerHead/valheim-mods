using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FeedFromChests
{
    internal enum Purpose { Items, Fuel }

    /// <summary>What happened when we asked a station to take something.</summary>
    internal enum Outcome
    {
        Failed,
        /// <summary>It took the item, and the game already used one from your inventory/chests (through the "fuel" route).</summary>
        Added,
        /// <summary>It took the item, but we handed it a stand-in, so we still have to take one real item from a chest or your inventory.</summary>
        AddedTakeOne,
    }

    /// <summary>One kind of station: what it accepts and how to put things into it using the game's own code.</summary>
    internal class StationInfo
    {
        public MonoBehaviour Component;
        public string Title;
        public Vector3 Position;
        public List<ItemDrop> Inputs = new List<ItemDrop>();   // things it processes or cooks
        public ItemDrop Fuel;                                  // wood, coal and so on (null if none)
        public Func<Player, ItemDrop.ItemData, Outcome> AddInput;
        public Func<Player, ItemDrop.ItemData, Outcome> AddFuel;

        public bool Alive => Component != null;
    }

    /// <summary>Works out which kind of station an object is. Every kind just calls the game's own "add" code.</summary>
    internal static class Stations
    {
        private static readonly System.Reflection.MethodInfo HaveDoneItem = AccessTools.Method(typeof(CookingStation), "HaveDoneItem");

        /// <summary>A stand-in item for the game to "use": it's never in any inventory, so the game's own removal is harmless.</summary>
        public static ItemDrop.ItemData StandIn(ItemDrop drop)
        {
            ItemDrop.ItemData item = drop.m_itemData.Clone();
            item.m_dropPrefab = drop.gameObject;
            item.m_stack = 1;
            item.m_customData[Feed.StandInTag] = "1";
            return item;
        }

        /// <summary>
        /// Identify the station you're looking at. <paramref name="purpose"/> is what pressing E on this exact spot would do
        /// (add items or add fuel), or null if E here does something else that we should leave alone.
        /// </summary>
        public static bool TryGet(GameObject go, out StationInfo info, out Purpose? purpose)
        {
            info = null;
            purpose = null;
            if (go == null) return false;
            Switch hoveredSwitch = go.GetComponentInParent<Switch>();

            // Smelters: kilns, furnaces, blast furnaces, the windmill, spinning wheel, Eitr refinery...
            Smelter smelter = go.GetComponentInParent<Smelter>();
            if (smelter != null)
            {
                info = new StationInfo { Component = smelter, Title = smelter.m_name, Position = smelter.transform.position };
                info.Inputs.AddRange(smelter.m_conversion.Where(c => c.m_from != null).Select(c => c.m_from));
                if (smelter.m_maxFuel > 0 && smelter.m_fuelItem != null) info.Fuel = smelter.m_fuelItem;
                info.AddInput = (p, item) => smelter.m_addOreSwitch != null && smelter.m_addOreSwitch.UseItem(p, item) ? Outcome.AddedTakeOne : Outcome.Failed;
                info.AddFuel = (p, item) => smelter.m_addWoodSwitch != null && smelter.m_addWoodSwitch.Interact(p, false, false) ? Outcome.Added : Outcome.Failed;

                if (hoveredSwitch != null)
                    purpose = hoveredSwitch == smelter.m_addOreSwitch ? Purpose.Items
                            : hoveredSwitch == smelter.m_addWoodSwitch ? Purpose.Fuel
                            : (Purpose?)null;
                return true;
            }

            // Cooking racks and cooking stations.
            CookingStation cooking = go.GetComponentInParent<CookingStation>();
            if (cooking != null)
            {
                info = new StationInfo { Component = cooking, Title = cooking.m_name, Position = cooking.transform.position };
                info.Inputs.AddRange(cooking.m_conversion.Where(c => c.m_from != null).Select(c => c.m_from));
                if (cooking.m_addFuelSwitch != null && cooking.m_fuelItem != null) info.Fuel = cooking.m_fuelItem;
                info.AddInput = (p, item) =>
                    (cooking.m_addFoodSwitch != null ? cooking.m_addFoodSwitch.UseItem(p, item) : cooking.UseItem(p, item)) ? Outcome.AddedTakeOne : Outcome.Failed;
                info.AddFuel = (p, item) => cooking.m_addFuelSwitch != null && cooking.m_addFuelSwitch.Interact(p, false, false) ? Outcome.Added : Outcome.Failed;

                bool haveDone = HaveDoneItem != null && (bool)HaveDoneItem.Invoke(cooking, null); // E would collect finished food: leave that alone
                if (!haveDone)
                {
                    if (cooking.m_addFuelSwitch != null && hoveredSwitch == cooking.m_addFuelSwitch) purpose = Purpose.Fuel;
                    else if (cooking.m_addFoodSwitch != null ? hoveredSwitch == cooking.m_addFoodSwitch : hoveredSwitch == null) purpose = Purpose.Items;
                }
                return true;
            }

            // Fires: campfires, hearths, braziers, sconces.
            Fireplace fire = go.GetComponentInParent<Fireplace>();
            if (fire != null)
            {
                info = new StationInfo { Component = fire, Title = fire.m_name, Position = fire.transform.position };
                if (fire.m_canRefill && !fire.m_infiniteFuel && fire.m_fuelItem != null) info.Fuel = fire.m_fuelItem;
                info.AddFuel = (p, item) => fire.UseItem(p, item) ? Outcome.AddedTakeOne : Outcome.Failed;

                // Where E would switch the fire on/off instead of refuelling, we stay out of the way.
                float fuel = fire.GetComponent<ZNetView>()?.GetZDO()?.GetFloat(ZDOVars.s_fuel) ?? 0f;
                if (info.Fuel != null && !(fire.m_canTurnOff && fuel > 0f)) purpose = Purpose.Fuel;
                return info.Fuel != null;
            }

            // The fermenter (mead base).
            Fermenter fermenter = go.GetComponentInParent<Fermenter>();
            if (fermenter != null)
            {
                info = new StationInfo { Component = fermenter, Title = fermenter.m_name, Position = fermenter.transform.position };
                info.Inputs.AddRange(fermenter.m_conversion.Where(c => c.m_from != null).Select(c => c.m_from));
                info.AddInput = (p, item) => fermenter.UseItem(p, item) ? Outcome.AddedTakeOne : Outcome.Failed;

                bool empty = (fermenter.GetComponent<ZNetView>()?.GetZDO()?.GetInt(ZDOVars.s_content) ?? 0) == 0; // only an empty one takes ingredients
                if (empty) purpose = Purpose.Items;
                return true;
            }

            return false;
        }
    }
}
