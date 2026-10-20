using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static DHack.Shared.Translations;

namespace AICompanion
{
    /// <summary>
    /// Its bag beside your inventory: whenever your inventory is open and one of your companions is within 10 m, its bag shows to the right
    /// of yours, in a copy of the game's own chest panel and grid (so dragging, splitting stacks and tooltips are the game's), titled with its
    /// name and weight. Drag between the two as with a chest; right-click one of its items to take it. It is not "opened" like a chest, so
    /// the companion carries on meanwhile. Where it goes: right of your inventory (GearSlots' gear panel is on the left, QualityOfLife's
    /// buttons and the chest panel below it, the crafting panel on the right), or right of ExtraSlots' equipment panel when that is there;
    /// with no room there, under the crafting panel (or under ExtraSlots' panel).
    ///
    /// With a controller it is a panel of its own (LB/RB to it, as to a chest): the chest panel it is copied from brings the chest's
    /// gamepad group, which the game never switched off for a copy, so its grid took every A press along with yours and dropped what you
    /// picked up straight into its bag (0.24.7 and older). It has its own group now, in the game's list while it shows, off until you tab to it.
    /// </summary>
    internal static class BagPanel
    {
        private static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> ContainerGrid = AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");
        private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> DragItem = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem");
        private static readonly AccessTools.FieldRef<InventoryGui, Container> CurrentContainer = AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");

        private static RectTransform _panel;
        private static InventoryGrid _grid;
        private static TMP_Text _name, _weight;
        private static Humanoid _shown;
        private static (Vector2 screen, Rect beside, bool crafting) _placedFor;
        private static UIGroupHandler _group;   // its gamepad group (Join, Leave)
        private static InventoryGui _gui;
        private static readonly AccessTools.FieldRef<InventoryGui, int> ActiveGroup = AccessTools.FieldRefAccess<InventoryGui, int>("m_activeGroup");
        private static readonly System.Reflection.MethodInfo SetActiveGroup = AccessTools.Method(typeof(InventoryGui), "SetActiveGroup", new[] { typeof(int), typeof(bool) });
        public const string ExtraSlotsPanel = "ExtraSlotsEquipmentPanel";   // (shudnal's ExtraSlots: its equipment and quick slots, right of your inventory)
        private static bool _warned;

        public static void Tick(InventoryGui gui)
        {
            try
            {
                Humanoid c = Pick(gui);
                if (c == null) { Hide(); return; }
                if (_panel == null && !Build(gui)) return;
                if (!_panel.gameObject.activeSelf) _panel.gameObject.SetActive(true);
                _gui = gui;
                Join(gui);
                if (_shown != c) { _shown = c; _placedFor = default; }
                _grid.UpdateInventory(c.GetInventory(), null, DragItem(gui));
                if (_name != null) _name.text = T("aicompanion_bagpanel_title", Companion.NameOf(c));
                if (_weight != null) _weight.text = $"{Carry.Weight(c):0}/{Carry.Max(c):0}";
                // Placed again when the screen size changes, the crafting panel comes or goes, or ExtraSlots' panel moves (it can be dragged).
                var now = (new Vector2(Screen.width, Screen.height), Beside(gui) ?? default, gui.m_crafting != null && gui.m_crafting.gameObject.activeInHierarchy);
                if (_placedFor != now) { Place(gui); _placedFor = now; }
            }
            catch (Exception e)
            {
                if (!_warned) { _warned = true; Plugin.Instance?.Warn("Companion bag panel: " + e); } // (once, with where: not every frame)
                Hide();
            }
        }

        /// <summary>Your nearest companion within 10 m that runs on your game (only then are changes to its bag kept), unless its bag is the chest you have open.</summary>
        private static Humanoid Pick(InventoryGui gui)
        {
            Player p = Player.m_localPlayer;
            if (p == null || !InventoryGui.IsVisible()) return null;
            Container open = CurrentContainer(gui);
            return Companion.All().Where(c => Companion.IsMine(c, p) && !c.IsDead() && c.GetComponent<ZNetView>().IsOwner() && Vector3.Distance(c.transform.position, p.transform.position) < 10f
                                             && (open == null || open.gameObject != c.gameObject))
                .OrderBy(c => Vector3.Distance(c.transform.position, p.transform.position)).FirstOrDefault();
        }

        /// <summary>A copy of the game's chest panel (background, title, weight, grid), without its buttons, wired to the game's own drag and drop.</summary>
        private static bool Build(InventoryGui gui)
        {
            InventoryGrid source = ContainerGrid(gui);
            if (gui.m_container == null || source == null) return false;
            GameObject copy = UnityEngine.Object.Instantiate(gui.m_container.gameObject, gui.m_container.parent, false);
            copy.name = "DHack_CompanionBag";
            _panel = copy.GetComponent<RectTransform>();
            foreach (Button b in copy.GetComponentsInChildren<Button>(true)) UnityEngine.Object.Destroy(b.gameObject); // Take all, Stack...: not for its bag
            _grid = copy.GetComponentInChildren<InventoryGrid>(true);
            if (_grid == null) { UnityEngine.Object.Destroy(copy); _panel = null; return false; }
            if (_grid.m_gridRoot != null) foreach (Transform cell in _grid.m_gridRoot) UnityEngine.Object.Destroy(cell.gameObject); // the chest's cells come along: the grid makes its own
            // The game wires its chest grid to the inventory screen in code (InventoryGui.Awake), and a copy keeps none of that: wired here the
            // same way. Without the drop check the game's grid update throws for every item in the bag (the panel hid itself at once), and
            // without the release nothing dropped on it would land.
            _grid.m_onSelected = Wire<Action<InventoryGrid, ItemDrop.ItemData, Vector2i, InventoryGrid.Modifier>>(gui, "OnSelectedItem");
            _grid.m_onReleased = Wire<Action<InventoryGrid, ItemDrop.ItemData, Vector2i>>(gui, "OnReleasedItem");
            _grid.m_onEnter = Wire<Action<InventoryGrid, Vector2i>>(gui, "OnEnterElement");
            _grid.OnSetTouchSelection = Wire<Action<InventoryGrid>>(gui, "SetTouchSelection");
            _grid.CanDropDragOntoItem = Wire<Func<ItemDrop.ItemData, bool>>(gui, "CanDropDragOntoItem") ?? (item => true);
            _grid.m_onRightClick = TakeToYou;
            // Its own gamepad group (the chest's, copied with it, or a new one), off until you tab to it: see the summary.
            UIGroupHandler group = _grid.m_uiGroup;
            if (group == null || !group.transform.IsChildOf(copy.transform))
            {
                group = copy.GetComponent<UIGroupHandler>() ?? copy.AddComponent<UIGroupHandler>();
                group.m_groupPriority = source.m_uiGroup != null ? source.m_uiGroup.m_groupPriority : 0;
                _grid.m_uiGroup = group;
            }
            group.SetActive(false);
            _group = group;
            _name = Find(copy, gui.m_container, gui.m_containerName);
            _weight = Find(copy, gui.m_container, gui.m_containerWeight);
            // Room for its two gear rows (and the gap above them) under the bag: the chest panel is made for four rows.
            float extra = (Gear.Rows - Gear.BagRows) * _grid.m_elementSpace + InventoryGrid_UpdateGui_Bag.Gap;
            _panel.sizeDelta += new Vector2(0f, extra);
            InventoryGrid_UpdateGui_Bag.Forget();
            copy.SetActive(true);
            Plugin.Instance?.Note("Companion bag panel made beside the inventory");
            return true;
        }

        /// <summary>The inventory screen's own handler, as the game hooks it to its chest grid (null when this game version has no such method).</summary>
        private static T Wire<T>(InventoryGui gui, string method) where T : Delegate
        {
            System.Reflection.MethodInfo m = AccessTools.Method(typeof(InventoryGui), method);
            return m != null ? (T)Delegate.CreateDelegate(typeof(T), gui, m, false) : null;
        }

        /// <summary>The copy's counterpart of a text in the game's panel (by its path under the panel).</summary>
        private static TMP_Text Find(GameObject copy, RectTransform original, TMP_Text text)
        {
            if (text == null) return null;
            string path = "";
            for (Transform t = text.transform; t != null && t != original.transform; t = t.parent) path = path.Length == 0 ? t.name : t.name + "/" + path;
            Transform found = copy.transform.Find(path);
            return found != null ? found.GetComponent<TMP_Text>() : null;
        }

        private static void TakeToYou(InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos)
        {
            Player p = Player.m_localPlayer;
            if (item == null || p == null || _shown == null) return;
            if (!Companion.Take(_shown, p, item, out string why) && why != null) p.Message(MessageHud.MessageType.Center, Words.Show(why));
        }

        /// <summary>
        /// Right of your inventory (top aligned, past its armour and weight readout), or under the crafting panel when that would overlap it,
        /// as always. With ExtraSlots' panel there (right of your inventory too), the first place that covers neither it nor the crafting
        /// panel and stays on screen: right of it, under the crafting panel, under it.
        /// </summary>
        private static void Place(InventoryGui gui)
        {
            var player = new Vector3[4];
            gui.m_player.GetWorldCorners(player);           // 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
            var mine = new Vector3[4];
            _panel.GetWorldCorners(mine);
            float width = mine[2].x - mine[1].x, height = mine[1].y - mine[0].y;
            float gap = (player[2].x - player[1].x) * 0.13f;  // room for the armour and weight readout beside your inventory
            Vector2 topLeft = new Vector2(player[2].x + gap, player[2].y);
            Rect? craft = gui.m_crafting != null && gui.m_crafting.gameObject.activeInHierarchy ? WorldRect(gui.m_crafting) : (Rect?)null;
            Vector2 underCraft = craft is Rect cr ? new Vector2(cr.xMin, cr.yMin - gap * 0.3f) : topLeft;
            if (craft is Rect c && topLeft.x + width > c.xMin - gap * 0.3f && topLeft.y - height < c.yMax) topLeft = underCraft; // (the old rule)
            if (Beside(gui) is Rect extra)
            {
                Canvas canvas = gui.GetComponentInParent<Canvas>();
                Rect? screen = canvas != null ? WorldRect((RectTransform)canvas.rootCanvas.transform) : (Rect?)null;
                var tries = new List<Vector2> { new Vector2(extra.xMax + gap * 0.3f, player[2].y) };
                if (craft != null) tries.Add(underCraft);
                tries.Add(new Vector2(extra.xMin, extra.yMin - gap * 0.3f));
                topLeft = underCraft;
                foreach (Vector2 at in tries)
                {
                    var box = new Rect(at.x, at.y - height, width, height);
                    if (box.Overlaps(extra) || craft is Rect k && box.Overlaps(k)) continue;
                    if (screen is Rect s && (box.xMin < s.xMin || box.xMax > s.xMax || box.yMin < s.yMin || box.yMax > s.yMax)) continue;
                    topLeft = at;
                    break;
                }
            }
            _panel.position += new Vector3(topLeft.x, topLeft.y, mine[1].z) - mine[1];
        }

        /// <summary>ExtraSlots' equipment panel when it shows (it sits right of your inventory, where its bag would go), in world space.</summary>
        private static Rect? Beside(InventoryGui gui)
        {
            Transform t = gui.m_player != null ? gui.m_player.Find(ExtraSlotsPanel) : null;
            return t is RectTransform r && r.gameObject.activeInHierarchy ? WorldRect(r) : (Rect?)null;
        }

        private static Rect WorldRect(RectTransform r)
        {
            var c = new Vector3[4];
            r.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        /// <summary>Its gamepad group into the game's list (LB/RB reach it) while it shows, off until you tab to it.</summary>
        private static void Join(InventoryGui gui)
        {
            if (_group == null || Array.IndexOf(gui.m_uiGroups, _group) >= 0) return;
            gui.m_uiGroups = gui.m_uiGroups.Concat(new[] { _group }).ToArray();
            _group.SetActive(false);
        }

        /// <summary>Out of the list when it hides; if it was the one in use, back to your inventory (the game's group 1).</summary>
        private static void Leave(InventoryGui gui)
        {
            if (gui == null || ReferenceEquals(_group, null)) return;
            int i = Array.IndexOf(gui.m_uiGroups, _group);
            if (i < 0) return;
            int active = ActiveGroup(gui);
            gui.m_uiGroups = gui.m_uiGroups.Where((g, n) => n != i).ToArray();
            if (_group != null) _group.SetActive(false);
            if (active == i) SetActiveGroup?.Invoke(gui, new object[] { 1, false });
            else if (active > i) ActiveGroup(gui) = active - 1;   // (a group another mod put after it moved down one)
        }

        private static void Hide()
        {
            _shown = null;
            Leave(_gui);
            if (_panel != null && _panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);
        }

        public static bool IsBagGrid(InventoryGrid grid) => grid != null && grid == _grid;
        public static Humanoid Shown => _shown;

        public static void Destroy()
        {
            try { Leave(_gui); } catch { }   // (a reload: never leave a destroyed group in the game's list)
            if (_panel != null) UnityEngine.Object.Destroy(_panel.gameObject);
            _panel = null; _grid = null; _name = _weight = null; _shown = null; _group = null; _gui = null;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Update")]
    internal static class InventoryGui_Update_Bag
    {
        private static void Postfix(InventoryGui __instance) => BagPanel.Tick(__instance);
    }
}

namespace AICompanion
{
    /// <summary>
    /// Ctrl-click (the game's quick move) with its bag beside your inventory: from its bag into yours, from yours to it (gear into its slot,
    /// the rest into its bag), as with an open chest. Without its panel showing, the game's own (drop on the ground).
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
    internal static class InventoryGui_OnSelectedItem_QuickMove
    {
        private static readonly AccessTools.FieldRef<InventoryGui, GameObject> DragGo = AccessTools.FieldRefAccess<InventoryGui, GameObject>("m_dragGo");
        private static readonly AccessTools.FieldRef<InventoryGui, Container> Open = AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");

        private static bool Prefix(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item, InventoryGrid.Modifier mod)
        {
            Humanoid c = BagPanel.Shown;
            Player p = Player.m_localPlayer;
            if (mod != InventoryGrid.Modifier.Move || item == null || c == null || p == null || Open(__instance) != null || DragGo(__instance) != null) return true;
            string why;
            if (BagPanel.IsBagGrid(grid)) { if (!Companion.Take(c, p, item, out why) && why != null) p.Message(MessageHud.MessageType.Center, Words.Show(why)); return false; }
            if (grid == __instance.m_playerGrid) { if (!Companion.Give(c, p, item, out why) && why != null) p.Message(MessageHud.MessageType.Center, Words.Show(why)); return false; }
            return true;
        }
    }
}

namespace AICompanion
{
    /// <summary>
    /// Dragging out of its bag: the inventory screen cancels, every frame, any drag that is not from your own inventory when no chest is open
    /// (InventoryGui.UpdateContainer), so an item picked up in its bag panel was dropped again at once and nothing could be taken out. That one
    /// check is let off for a drag from its bag while its panel shows; closing the inventory, a right-click and the rest still cancel.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), "UpdateContainer")]
    internal static class InventoryGui_UpdateContainer_Bag
    {
        internal static bool Inside;
        private static void Prefix() => Inside = true;
        private static void Finalizer() => Inside = false;
    }

    [HarmonyPatch(typeof(InventoryGui), "SetupDragItem")]
    internal static class InventoryGui_SetupDragItem_Bag
    {
        private static readonly AccessTools.FieldRef<InventoryGui, Inventory> DragFrom = AccessTools.FieldRefAccess<InventoryGui, Inventory>("m_dragInventory");

        private static bool Prefix(InventoryGui __instance, ItemDrop.ItemData item)
        {
            if (item != null || !InventoryGui_UpdateContainer_Bag.Inside) return true;
            Inventory from = DragFrom(__instance);
            return !(BagPanel.Shown != null && from != null && from == BagPanel.Shown.GetInventory()); // (its bag, its panel showing: keep the drag)
        }
    }
}
