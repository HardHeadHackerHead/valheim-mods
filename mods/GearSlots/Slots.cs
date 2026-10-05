using System;
using System.Collections.Generic;
using UnityEngine;

namespace GearSlots
{
    internal enum Kind { Head, Chest, Legs, Shoulder, Utility, Trinket, Ammo, Shield, Food, Quick }

    /// <summary>One extra slot. The slot is a normal cell of your real inventory, in rows below the ones you normally see.</summary>
    internal class Slot
    {
        public string Label;
        public Kind Kind;
        public int Col;   // column in the gear panel and in the inventory grid
        public int Row;   // 0 = first extra row, 1 = second
        public int Quick; // 0-based number among the quick slots (-1 for the others)
        public string Icon; // an item whose picture is shown faintly in the empty slot
    }

    internal static class Layout
    {
        public const int ExtraRows = 2;
        public const int Columns = 8;
        private const string RowsKey = "DHack.GearSlots.NormalRows";

        public static readonly Slot[] All =
        {
            new Slot { Label = "Head",    Kind = Kind.Head,     Col = 0, Row = 0, Quick = -1, Icon = "HelmetLeather" },
            new Slot { Label = "Chest",   Kind = Kind.Chest,    Col = 1, Row = 0, Quick = -1, Icon = "ArmorLeatherChest" },
            new Slot { Label = "Legs",    Kind = Kind.Legs,     Col = 2, Row = 0, Quick = -1, Icon = "ArmorLeatherLegs" },
            new Slot { Label = "Cape",    Kind = Kind.Shoulder, Col = 3, Row = 0, Quick = -1, Icon = "CapeDeerHide" },
            new Slot { Label = "Belt",    Kind = Kind.Utility,  Col = 4, Row = 0, Quick = -1, Icon = "BeltStrength" },
            new Slot { Label = "Trinket", Kind = Kind.Trinket,  Col = 5, Row = 0, Quick = -1 },
            new Slot { Label = "Ammo",    Kind = Kind.Ammo,     Col = 6, Row = 0, Quick = -1, Icon = "ArrowWood" },
            new Slot { Label = "Shield",  Kind = Kind.Shield,   Col = 7, Row = 0, Quick = -1, Icon = "ShieldWood" },
            new Slot { Label = "Food",    Kind = Kind.Food,     Col = 0, Row = 1, Quick = -1, Icon = "CookedMeat" },
            new Slot { Label = "Food",    Kind = Kind.Food,     Col = 1, Row = 1, Quick = -1, Icon = "CookedMeat" },
            new Slot { Label = "Food",    Kind = Kind.Food,     Col = 2, Row = 1, Quick = -1, Icon = "CookedMeat" },
            new Slot { Label = "Quick",   Kind = Kind.Quick,    Col = 3, Row = 1, Quick = 0 },
            new Slot { Label = "Quick",   Kind = Kind.Quick,    Col = 4, Row = 1, Quick = 1 },
            new Slot { Label = "Quick",   Kind = Kind.Quick,    Col = 5, Row = 1, Quick = 2 },
            new Slot { Label = "Quick",   Kind = Kind.Quick,    Col = 6, Row = 1, Quick = 3 },
            new Slot { Label = "Quick",   Kind = Kind.Quick,    Col = 7, Row = 1, Quick = 4 },
        };

        public const int QuickCount = 5;

        /// <summary>How many rows your inventory normally has (4 in vanilla). Remembered across hot reloads.</summary>
        public static int NormalRows
        {
            get
            {
                object saved = AppDomain.CurrentDomain.GetData(RowsKey);
                if (saved is int rows) return rows;
                Inventory inv = Player.m_localPlayer != null ? Player.m_localPlayer.GetInventory() : null;
                return inv != null ? inv.GetHeight() : 4;
            }
        }

        public static void RememberNormalRows(int rows) => AppDomain.CurrentDomain.SetData(RowsKey, rows);
        public static void ForgetNormalRows() => AppDomain.CurrentDomain.SetData(RowsKey, null);

        public static Vector2i CellOf(Slot s) => new Vector2i(s.Col, NormalRows + s.Row);

        /// <summary>The slot that sits at this inventory cell, or null for an ordinary cell.</summary>
        public static Slot At(int x, int y)
        {
            int row = y - NormalRows;
            if (row < 0 || row >= ExtraRows) return null;
            foreach (Slot s in All) if (s.Col == x && s.Row == row) return s;
            return null;
        }

        public static bool InExtraRows(Vector2i pos) => pos.y >= NormalRows;

        public static bool Fits(Slot s, ItemDrop.ItemData item)
        {
            if (s == null || item == null) return true;
            var shared = item.m_shared;
            switch (s.Kind)
            {
                case Kind.Head: return shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet;
                case Kind.Chest: return shared.m_itemType == ItemDrop.ItemData.ItemType.Chest;
                case Kind.Legs: return shared.m_itemType == ItemDrop.ItemData.ItemType.Legs;
                case Kind.Shoulder: return shared.m_itemType == ItemDrop.ItemData.ItemType.Shoulder;
                case Kind.Utility: return shared.m_itemType == ItemDrop.ItemData.ItemType.Utility;
                case Kind.Trinket: return shared.m_itemType == ItemDrop.ItemData.ItemType.Trinket;
                case Kind.Shield: return shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;
                case Kind.Ammo: return shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo || shared.m_itemType == ItemDrop.ItemData.ItemType.AmmoNonEquipable;
                case Kind.Food: return IsFood(item);
                default: return !shared.m_questItem;
            }
        }

        public static bool IsFood(ItemDrop.ItemData item) =>
            item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable &&
            (item.m_shared.m_food > 0f || item.m_shared.m_foodStamina > 0f || item.m_shared.m_foodEitr > 0f);

        public static Slot QuickSlot(int quick) { foreach (Slot s in All) if (s.Quick == quick) return s; return null; }

        public static bool IsEquipment(Slot s) => s != null && s.Kind != Kind.Food && s.Kind != Kind.Quick;

        public static ItemDrop.ItemData ItemIn(Inventory inv, Slot s)
        {
            Vector2i cell = CellOf(s);
            return inv.GetItemAt(cell.x, cell.y);
        }

        public static bool IsPlayerInventory(Inventory inv) =>
            inv != null && Player.m_localPlayer != null && inv == Player.m_localPlayer.GetInventory();

        /// <summary>Items in the ordinary part of the inventory (the extra rows do not count toward "full").</summary>
        public static int NormalItemCount(Inventory inv)
        {
            int rows = NormalRows, n = 0;
            foreach (ItemDrop.ItemData item in inv.GetAllItems()) if (item.m_gridPos.y < rows) n++;
            return n;
        }

        public static Vector2i FindFreeNormalCell(Inventory inv, bool topFirst)
        {
            int rows = NormalRows, width = inv.GetWidth();
            for (int i = 0; i < rows; i++)
            {
                int y = topFirst ? i : rows - 1 - i;
                for (int x = 0; x < width; x++)
                    if (inv.GetItemAt(x, y) == null) return new Vector2i(x, y);
            }
            return new Vector2i(-1, -1);
        }
    }
}
