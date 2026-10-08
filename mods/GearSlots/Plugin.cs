using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace GearSlots
{
    /// <summary>
    /// Extra inventory slots for what you wear and eat: Head, Chest, Legs, Cape, Belt, Trinket, Ammo and Shield slots (an item dropped in is
    /// worn), three Food slots, and five Quick slots with hotkeys for weapons, tools or potions.
    ///
    /// The slots are extra rows of your real inventory shown in their own panel, so nothing is stored anywhere new: if you
    /// remove the mod, the game drops whatever is in those rows at your feet the next time you spawn (move it into the bag first).
    ///
    /// Split across files: Plugin.cs (setup, input), Slots.cs (what each slot accepts), Patches.cs (game hooks), Panel.cs (the look).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dhack.gearslots";
        public const string Name = "GearSlots";
        public const string Version = "1.1.0";

        internal static Plugin Instance;

        private ConfigEntry<bool> _showPanel, _autoEat, _showMessages, _autoFill, _shieldFollows, _showQuickBar;
        private ConfigEntry<int> _eatBelow;
        private ConfigEntry<float> _panelGap, _offsetX, _offsetY;
        private ConfigEntry<KeyboardShortcut>[] _quickKeys;
        private Harmony _harmony;

        internal bool ShowQuickBar => _showQuickBar.Value;
        internal bool ShowPanel => _showPanel.Value;
        internal float PanelGap => _panelGap.Value;
        internal Vector2 PanelOffset => new Vector2(_offsetX.Value, _offsetY.Value);

        private void Awake()
        {
            Instance = this;
            _showPanel = Config.Bind("General", "ShowPanel", true, "Show the Gear panel next to your inventory. (Turn off and your gear slots hide; the items stay where they are.)");
            _showMessages = Config.Bind("General", "ShowMessages", true, "Short messages when something happens (auto-eat, a quick slot is empty).");
            _autoEat = Config.Bind("Food", "AutoEat", false,
                "Eat from your Food slots by yourself: when a food you have in a slot is not active (and you have a free food slot), or its effect has dropped below half. Off by default.");
            _eatBelow = Config.Bind("Food", "EatBelowPercent", 20,
                new ConfigDescription("Auto-eat a food you are already under only once its time left drops below this percent (the game allows it from 50). Lower saves food: a meal then lasts 80% of its time instead of 50%, and its effect weakens a little towards the end.", new AcceptableValueRange<int>(1, 50)));
            _shieldFollows = Config.Bind("General", "ShieldFollowsWeapon", true,
                "When you switch to a one-handed weapon, put on the shield from your Shield slot too. (Two-handed weapons and bows take the shield off, as in the game.)");
            _showQuickBar = Config.Bind("Quick slots", "ShowUnderHotbar", true,
                "Show what is in your Quick slots, with their keys, in a row under the hotbar (the 1-8 on screen) while the inventory is closed.");
            _autoFill = Config.Bind("General", "AutoFill", true,
                "When you put on a piece of gear (or the game loads with it on), move it into its matching gear slot if that slot is empty.");
            _panelGap = Config.Bind("Layout", "Gap", 6f, "Space between the Gear panel and the inventory (UI pixels).");
            _offsetX = Config.Bind("Layout", "OffsetX", 0f, "Move the Gear panel right (negative = left).");
            _offsetY = Config.Bind("Layout", "OffsetY", 0f, "Move the Gear panel up (negative = down).");

            KeyCode[] defaults = { KeyCode.Z, KeyCode.X, KeyCode.C, KeyCode.V, KeyCode.None };
            _quickKeys = new ConfigEntry<KeyboardShortcut>[Layout.QuickCount];
            for (int i = 0; i < Layout.QuickCount; i++)
                _quickKeys[i] = Config.Bind("Quick slots", "Key" + (i + 1), new KeyboardShortcut(defaults[i]),
                    "Press to use what is in quick slot " + (i + 1) + ": equips or unequips a weapon, tool or shield, or drinks a potion. Set to None to turn it off. " +
                    "If the game uses the same key for something (V: auto-pickup, X: sit, C: walk), the game's key is unbound once, so only this one works; bind it again in the game's Settings, Controls.");
            foreach (var entry in _quickKeys) entry.SettingChanged += (s, e) => _keysChecked = false;

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            if (Player.m_localPlayer != null) Patches.Extend(Player.m_localPlayer.GetInventory()); // hot reload with a player already in the world

            // Other mods (stacking into chests, for example) can ask whether an item is sitting in a gear slot.
            AppDomain.CurrentDomain.SetData("DHack.GearSlots.IsGear", new Func<ItemDrop.ItemData, bool>(IsInGearSlot));

            Logger.LogInfo($"{Name} {Version} loaded");
            if (Player.m_localPlayer != null && Chat.instance != null)
                Chat.instance.AddString("[Mod]", $"{Name} v{Version} reloaded", Talker.Type.Normal);
        }

        // ScriptEngine destroys this copy on a reload. The inventory stays tall so nothing in a gear slot is disturbed;
        // the new copy picks it up again.
        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Panel.Dispose(restoreCells: true);
            _hotbar.Destroy();
            AppDomain.CurrentDomain.SetData("DHack.GearSlots.IsGear", null);
            if (Instance == this) Instance = null;
        }

        internal string QuickKeyName(int quick)
        {
            KeyCode key = _quickKeys[quick].Value.MainKey;
            return key == KeyCode.None ? "" : key.ToString();
        }

        private static bool IsInGearSlot(ItemDrop.ItemData item) =>
            item != null && Player.m_localPlayer != null && Layout.InExtraRows(item.m_gridPos) &&
            Player.m_localPlayer.GetInventory().GetAllItems().Contains(item);

        // ---- each frame ----

        private readonly Hotbar _hotbar = new Hotbar();
        private Inventory _watched;
        private readonly Dictionary<Slot, ItemDrop.ItemData> _last = new Dictionary<Slot, ItemDrop.ItemData>();
        private float _nextEat, _nextFill;

        // ---- our keys win over the game's (see GameKeys) ----

        private bool _keysChecked;
        private readonly List<string> _keyNotes = new List<string>();

        private void FreeGameKeys()
        {
            if (_keysChecked || ZInput.instance == null) return;
            _keysChecked = true;
            var keys = new List<KeyValuePair<KeyCode, string>>();
            for (int i = 0; i < _quickKeys.Length; i++)
            {
                KeyboardShortcut k = _quickKeys[i].Value;
                if (k.MainKey != KeyCode.None && !k.Modifiers.Any()) keys.Add(new KeyValuePair<KeyCode, string>(k.MainKey, "quick slot " + (i + 1)));
            }
            _keyNotes.AddRange(GameKeys.Free(Name, keys));

            // With its key unbound nothing can switch auto-pickup back on, so make sure it is on (the game's default).
            var autoPickup = AccessTools.Field(typeof(Player), "m_enableAutoPickup");
            if (autoPickup != null && !(bool)autoPickup.GetValue(null) && string.IsNullOrEmpty(ZInput.instance.GetButtonDef("AutoPickup")?.GetActionPath()))
            {
                autoPickup.SetValue(null, true);
                _keyNotes.Add($"{Name}: auto-pickup was off and its key is unbound, so it is switched back on");
                Logger.LogInfo("Auto-pickup was off and its key is unbound: switched it back on");
            }
        }

        private void Update()
        {
            FreeGameKeys();
            if (_keyNotes.Count > 0 && Chat.instance != null && Player.m_localPlayer != null)
            {
                foreach (string note in _keyNotes) Chat.instance.AddString("[Mod]", note, Talker.Type.Normal);
                _keyNotes.Clear();
            }

            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead()) return;
            Inventory inv = player.GetInventory();
            Patches.Extend(inv);
            _hotbar.Update(player, inv);

            if (_autoFill.Value && Time.time >= _nextFill) { _nextFill = Time.time + 0.5f; FillSlotsWithWornGear(player, inv); }
            WearWhatArrived(player, inv);
            if (_shieldFollows.Value) ShieldFollowsWeapon(player, inv);
            if (_autoEat.Value && Time.time >= _nextEat) { _nextEat = Time.time + 1f; AutoEat(player, inv); }

            if (InventoryGui.IsVisible() || TypingOrMenuOpen()) return;
            for (int i = 0; i < Layout.QuickCount; i++)
                if (Pressed(_quickKeys[i].Value)) UseQuick(player, inv, i);
        }

        /// <summary>An item newly dropped into an armor slot gets put on. (Items already there when the game loads keep the state they were saved in.)</summary>
        private void WearWhatArrived(Player player, Inventory inv)
        {
            bool first = _watched != inv;
            _watched = inv;
            foreach (Slot slot in Layout.All)
            {
                if (!Layout.IsEquipment(slot)) continue;
                ItemDrop.ItemData now = Layout.ItemIn(inv, slot);
                _last.TryGetValue(slot, out ItemDrop.ItemData before);
                if (!first && now != null && now != before && !player.IsItemEquiped(now))
                {
                    // A shield dropped into its slot while you hold a two-handed weapon waits: putting it on would take the weapon off.
                    if (slot.Kind == Kind.Shield && !OneHandFree(player)) { _last[slot] = now; continue; }
                    player.EquipItem(now);
                }
                _last[slot] = now;
            }
        }

        /// <summary>Gear you are wearing that sits in the ordinary part of the bag moves into its own slot, so armor stops taking room.</summary>
        private void FillSlotsWithWornGear(Player player, Inventory inv)
        {
            foreach (ItemDrop.ItemData item in inv.GetAllItems().ToList())
            {
                if (item.m_gridPos.y >= Layout.NormalRows || !player.IsItemEquiped(item)) continue;
                Slot slot = Layout.All.FirstOrDefault(s => Layout.IsEquipment(s) && Layout.Fits(s, item) && Layout.ItemIn(inv, s) == null);
                if (slot == null) continue;
                item.m_gridPos = Layout.CellOf(slot);
                Logger.LogInfo("Moved worn " + item.m_shared.m_name + " into the " + slot.Label + " slot");
            }
        }

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> LeftItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_leftItem");
        private ItemDrop.ItemData _lastWeapon;

        /// <summary>True when what you hold leaves the other hand free for a shield (a one-handed weapon, or nothing).</summary>
        private static bool OneHandFree(Player player)
        {
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (weapon == null) return true;
            var type = weapon.m_shared.m_itemType;
            return type == ItemDrop.ItemData.ItemType.OneHandedWeapon;
        }

        /// <summary>The moment you change to a one-handed weapon, the shield from the Shield slot comes out with it.</summary>
        private void ShieldFollowsWeapon(Player player, Inventory inv)
        {
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (weapon == _lastWeapon) return;
            _lastWeapon = weapon;
            if (weapon == null || weapon.m_shared.m_itemType != ItemDrop.ItemData.ItemType.OneHandedWeapon || !inv.ContainsItem(weapon)) return;
            if (LeftItem(player) != null) return; // a shield or a torch is already in your other hand

            Slot slot = Layout.All.First(s => s.Kind == Kind.Shield);
            ItemDrop.ItemData shield = Layout.ItemIn(inv, slot);
            if (shield != null && !player.IsItemEquiped(shield)) player.EquipItem(shield);
        }

        private void UseQuick(Player player, Inventory inv, int quick)
        {
            Slot slot = Layout.All.First(s => s.Quick == quick);
            ItemDrop.ItemData item = Layout.ItemIn(inv, slot);
            if (item == null) { Tell(player, "Quick slot " + (quick + 1) + " is empty"); return; }
            player.UseItem(inv, item, fromInventoryGui: true); // true: never "use on" whatever you are looking at
        }

        private void AutoEat(Player player, Inventory inv)
        {
            List<Player.Food> active = player.GetFoods();
            foreach (Slot slot in Layout.All)
            {
                if (slot.Kind != Kind.Food) continue;
                ItemDrop.ItemData item = Layout.ItemIn(inv, slot);
                if (item == null) continue;

                Player.Food same = active.FirstOrDefault(f => f.m_item.m_shared.m_name == item.m_shared.m_name);
                bool wanted = same != null ? same.CanEatAgain() && same.m_time < same.m_item.m_shared.m_foodBurnTime * _eatBelow.Value / 100f : active.Count < 3;
                if (!wanted || !player.CanEat(item, false)) continue;

                player.UseItem(inv, item, fromInventoryGui: true);
                Tell(player, "Ate " + Localization.instance.Localize(item.m_shared.m_name));
                return; // one at a time; the next tick looks again
            }
        }

        // The same helpers the other mods use, so keys behave alike (extra held keys like the run key don't cancel a press).
        private static bool Pressed(KeyboardShortcut shortcut) =>
            shortcut.MainKey != KeyCode.None && Input.GetKeyDown(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);

        private static bool TypingOrMenuOpen() =>
            (Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() || TextInput.IsVisible() ||
            Minimap.InTextInput() || Menu.IsVisible() || Minimap.IsOpen() || StoreGui.IsVisible();

        private void Tell(Player player, string message)
        {
            if (_showMessages.Value) player.Message(MessageHud.MessageType.TopLeft, message);
        }
    }
}
