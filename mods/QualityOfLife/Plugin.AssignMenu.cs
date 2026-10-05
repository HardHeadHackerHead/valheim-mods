using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace QualityOfLife
{
    /// <summary>
    /// The menu for telling a chest what it should receive. Two tabs: whole categories (high level) and individual items
    /// (granular, searchable). Opens when you look at a chest and press the assign key, or from the button beside an open chest.
    /// </summary>
    public partial class Plugin
    {
        /// <summary>True while the menu is open (the input patches use this to keep the game from reacting to your clicks and typing).</summary>
        internal static bool RulesWindowOpen;

        private Container _rulesChest;
        private ChestRules _rules;
        private int _rulesTab;
        private string _search = "";
        private Vector2 _scrollCategories, _scrollItems;
        private Rect _rulesRect;
        private bool _rulesPlaced;

        private class ItemEntry { public string Name, Display, Category; public Sprite Icon; public int Rank; }
        private List<ItemEntry> _items = new List<ItemEntry>();

        private static readonly System.Reflection.FieldInfo CurrentContainerField = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");

        private readonly List<Texture2D> _menuTextures = new List<Texture2D>();
        private GUIStyle _rTitle, _rText, _rDim, _rBtn, _rBtnOn, _rToggle, _rField;

        private static readonly Color Gold = new Color(0.95f, 0.78f, 0.35f);
        private static readonly Color Dim = new Color(0.68f, 0.66f, 0.62f);

        // ---- opening and closing ------------------------------------------------------------------

        private static Container OpenChest() => InventoryGui.instance != null ? CurrentContainerField?.GetValue(InventoryGui.instance) as Container : null;
        private static Container LookedAtChest(Player p) => p.GetHoverObject()?.GetComponentInParent<Container>();

        private void HandleAssignKey(Player player)
        {
            Container chest = InventoryGui.IsVisible() ? OpenChest() : LookedAtChest(player);
            if (chest == null) { Tell(player, $"Look at a chest (or open one) and press {_assignKey.Value} to choose what it receives."); return; }
            if (!Usable(chest)) { Tell(player, "You can't use that chest."); return; }
            OpenRules(chest, player);
        }

        private void OpenRules(Container chest, Player player)
        {
            if (chest == null) return;
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide(); // so clicks in the menu can't reach the inventory beneath it

            _rulesChest = chest;
            _rules = ChestRules.Read(chest);
            _rulesTab = 0;
            _search = "";
            BuildItemList(player);
            RulesWindowOpen = true;
        }

        private void CloseRules()
        {
            RulesWindowOpen = false;
            _rulesChest = null;
        }

        /// <summary>Called every frame while the menu is open. Returns true if it's open (so other keys should be ignored).</summary>
        private bool UpdateRulesWindow(Player player)
        {
            if (!RulesWindowOpen) return false;
            if (_rulesChest == null || Menu.IsVisible() || (_rulesChest.transform.position - player.transform.position).sqrMagnitude > 15f * 15f)
                CloseRules();
            return true;
        }

        /// <summary>The items you can pick from: ones you've discovered, are carrying, or have already assigned.</summary>
        private void BuildItemList(Player player)
        {
            var list = new List<ItemEntry>();
            if (ObjectDB.instance != null)
            {
                var seen = new HashSet<string>();
                Inventory inventory = player.GetInventory();
                foreach (GameObject go in ObjectDB.instance.m_items)
                {
                    ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
                    if (drop == null) continue;
                    ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
                    if (shared.m_itemType == ItemDrop.ItemData.ItemType.None || shared.m_itemType == ItemDrop.ItemData.ItemType.Customization) continue;
                    if (!seen.Add(shared.m_name)) continue;

                    bool known = player.IsMaterialKnown(shared.m_name) || inventory.ContainsItemByName(shared.m_name) || _rules.Items.Contains(shared.m_name);
                    if (!known) continue;

                    list.Add(new ItemEntry
                    {
                        Name = shared.m_name,
                        Display = Localization.instance.Localize(shared.m_name),
                        Category = Categories.Of(shared),
                        Icon = drop.m_itemData.GetIcon(),
                        Rank = Tiers.Rank(shared),
                    });
                }
            }
            // Grouped by category; within each, from the lesser item to the greater (wood, fine wood, core wood...).
            _items = list.OrderBy(i => Array.IndexOf(Categories.All, i.Category)).ThenBy(i => i.Rank)
                         .ThenBy(i => i.Display, StringComparer.OrdinalIgnoreCase).ToList();
            _displayFor = null; // the Items tab rebuilds its rows from this new list
        }

        private void SaveRules()
        {
            if (_rulesChest != null) ChestRules.Write(_rulesChest, _rules);
        }

        // ---- the button beside an open chest ------------------------------------------------------

        private void DrawAssignButton()
        {
            if (!ShowStackUi(out Player player, out InventoryGui gui) || gui.m_container == null) return;
            Container chest = OpenChest();
            if (chest == null) return;
            EnsureStackStyle();

            var corners = new Vector3[4];
            gui.m_container.GetWorldCorners(corners);
            Canvas canvas = gui.m_container.GetComponentInParent<Canvas>();
            Camera cam = canvas != null ? canvas.worldCamera : null;
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            float scale = canvas != null ? Mathf.Max(0.6f, canvas.scaleFactor) : 1f;

            var rect = new Rect(bottomLeft.x + 8f * scale, Screen.height - bottomLeft.y + 6f * scale, 200f * scale, 30f * scale);
            if (StackButton(rect, "Assign items to this chest")) _pending = () => OpenRules(chest, player);

            if (_showChestLabels.Value && Event.current.type == EventType.Repaint)
            {
                // What this chest receives, as little tags under the button, so you can see its job at a glance.
                if (chest != _summaryChest || Time.unscaledTime >= _nextSummary)
                {
                    _summaryChest = chest;
                    _summaryChips = BuildChips(ChestRules.Read(chest)); // built once here, not on every redraw
                    _nextSummary = Time.unscaledTime + 0.5f;
                }
                float panelWidth = Mathf.Max(220f * scale, corners[3].x - corners[0].x);
                DrawChips(_summaryChips, rect.x, rect.yMax + 6f * scale, panelWidth - 8f * scale, scale);
            }
        }

        private Container _summaryChest;
        private List<KeyValuePair<string, Color>> _summaryChips = new List<KeyValuePair<string, Color>>();
        private float _nextSummary;

        /// <summary>The tags for a chest: its categories first, then a few item names, then "+N more".</summary>
        private static List<KeyValuePair<string, Color>> BuildChips(ChestRules rules)
        {
            var chips = new List<KeyValuePair<string, Color>>();
            foreach (string c in Categories.All.Where(rules.Categories.Contains)) chips.Add(new KeyValuePair<string, Color>(c, CategoryChip));

            // Individual items: show a few by name, then "+N more" so a long list can't take over the screen.
            List<string> names = rules.Items.Select(n => Localization.instance.Localize(n)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string n in names.Take(6)) chips.Add(new KeyValuePair<string, Color>(n, ItemChip));
            if (names.Count > 6) chips.Add(new KeyValuePair<string, Color>($"+{names.Count - 6} more", ItemChip));
            return chips;
        }

        private static readonly Color CategoryChip = new Color(0.55f, 0.42f, 0.16f, 0.97f);
        private static readonly Color ItemChip = new Color(0.2f, 0.32f, 0.45f, 0.97f);

        /// <summary>Little rounded tags: gold for categories, blue for individual items. Wraps to fit the width.</summary>
        private void DrawChips(List<KeyValuePair<string, Color>> chips, float x, float y, float maxWidth, float scale)
        {
            EnsureStackStyle();
            int size = _stackLabel.fontSize;
            _stackLabel.fontSize = Mathf.RoundToInt(11f * scale);

            if (chips.Count == 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                GUI.Label(new Rect(x, y, maxWidth, 16f * scale), "Receives: nothing assigned (only items it already holds)", _stackLabel);
                GUI.color = Color.white;
                _stackLabel.fontSize = size;
                return;
            }

            float cx = x, cy = y, h = 18f * scale, pad = 8f * scale, gap = 4f * scale;
            foreach (var chip in chips)
            {
                float w = _stackLabel.CalcSize(new GUIContent(chip.Key)).x + pad * 2f;
                if (cx + w > x + maxWidth && cx > x) { cx = x; cy += h + gap; } // wrap to the next line
                var r = new Rect(cx, cy, w, h);
                Rounded(r, chip.Value, 5f * scale);
                GUI.Label(r, chip.Key, _stackLabel);
                cx += w + gap;
            }
            _stackLabel.fontSize = size;
        }

        /// <summary>The extra line shown when you look at a chest (added by a hook on the game's chest hover text).</summary>
        internal static string HoverSuffix(Container chest)
        {
            Plugin plugin = Instance;
            if (plugin == null || !plugin._stackEnabled.Value || !plugin._showChestLabels.Value) return "";

            // The game asks for hover text every frame while you look at a chest. Work it out once a second and reuse it.
            if (chest == plugin._hoverChest && Time.unscaledTime < plugin._nextHover) return plugin._hoverText;
            try
            {
                ChestRules rules = ChestRules.Read(chest);
                string text = "";
                if (!rules.IsEmpty)
                {
                    var parts = new List<string>(Categories.All.Where(rules.Categories.Contains));
                    if (rules.Items.Count > 0) parts.Add(rules.Items.Count == 1 ? "1 item" : $"{rules.Items.Count} items");
                    text = "\n<color=#F2C75A>Receives:</color> " + string.Join(", ", parts.ToArray());
                }
                plugin._hoverChest = chest;
                plugin._hoverText = text;
                plugin._nextHover = Time.unscaledTime + 1f;
                return text;
            }
            catch (Exception) { return ""; }
        }

        private Container _hoverChest;
        private string _hoverText = "";
        private float _nextHover;

        // ---- the window ---------------------------------------------------------------------------

        private void DrawRulesWindow()
        {
            if (!RulesWindowOpen || _rulesChest == null) return;
            FreeTheMouse();
            EnsureMenuStyles();

            Matrix4x4 previous = GUI.matrix;
            float s = Mathf.Max(0.8f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float sw = Screen.width / s, sh = Screen.height / s;

            float w = Mathf.Min(560f, sw - 40f), h = Mathf.Min(660f, sh - 60f);
            if (!_rulesPlaced) { _rulesRect = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h); _rulesPlaced = true; }
            _rulesRect.width = w; _rulesRect.height = h;

            _rulesRect = GUI.Window(8841, _rulesRect, DrawRulesContents, GUIContent.none, GUIStyle.none);
            _rulesRect.x = Mathf.Clamp(_rulesRect.x, 0f, Mathf.Max(0f, sw - w));
            _rulesRect.y = Mathf.Clamp(_rulesRect.y, 0f, Mathf.Max(0f, sh - h));
            GUI.matrix = previous;
        }

        private void DrawRulesContents(int id)
        {
            float w = _rulesRect.width, h = _rulesRect.height;
            Rounded(new Rect(0, 0, w, h), new Color(0.07f, 0.06f, 0.05f, 0.98f), 9f);
            Outline(new Rect(0, 0, w, h), new Color(0.62f, 0.47f, 0.22f, 1f), 9f);

            GUILayout.BeginArea(new Rect(16f, 12f, w - 32f, h - 24f));

            // header
            GUILayout.BeginHorizontal();
            GUILayout.Label("Assign items to this chest", _rTitle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", _rBtn, GUILayout.Width(80), GUILayout.Height(28))) CloseRules();
            GUILayout.EndHorizontal();

            Player player = Player.m_localPlayer;
            float away = player != null && _rulesChest != null ? (_rulesChest.transform.position - player.transform.position).magnitude : 0f;
            GUILayout.Label($"{(_rulesChest != null ? _rulesChest.m_name : "")}  ·  {away:0} m away", _rDim);
            GUILayout.Label("When you press \"Stack to chests\", matching items go here, even if the chest is empty.", _rDim);
            GUILayout.Space(6);

            // tabs
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"Categories ({_rules.Categories.Count})", _rulesTab == 0 ? _rBtnOn : _rBtn, GUILayout.Height(30))) _rulesTab = 0;
            if (GUILayout.Button($"Individual items ({_rules.Items.Count})", _rulesTab == 1 ? _rBtnOn : _rBtn, GUILayout.Height(30))) _rulesTab = 1;
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            if (_rulesTab == 0) DrawCategoriesTab(); else DrawItemsTab();

            // footer
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label(_rules.IsEmpty ? "Nothing assigned: this chest only gets items it already holds." : "Changes are saved straight away.", _rDim);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear everything", _rBtn, GUILayout.Width(150), GUILayout.Height(26)))
            {
                _rules.Categories.Clear();
                _rules.Items.Clear();
                SaveRules();
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, w, 40));
        }

        private void DrawCategoriesTab()
        {
            _scrollCategories = GUILayout.BeginScrollView(_scrollCategories, GUILayout.ExpandHeight(true));
            foreach (string category in Categories.All)
            {
                bool on = _rules.Categories.Contains(category);
                bool now = GUILayout.Toggle(on, "  " + category, _rToggle);
                GUILayout.Label("      " + Categories.Help[category], _rDim);
                GUILayout.Space(4);
                if (now != on)
                {
                    if (now) _rules.Categories.Add(category); else _rules.Categories.Remove(category);
                    SaveRules();
                }
            }
            GUILayout.EndScrollView();
        }

        private void DrawItemsTab()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", _rText, GUILayout.Width(60));
            _search = GUILayout.TextField(_search, _rField, GUILayout.Height(26));
            if (_search.Length > 0 && GUILayout.Button("x", _rBtn, GUILayout.Width(28), GUILayout.Height(26))) _search = "";
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            if (_displayFor != _search) RebuildDisplay(); // the list only changes when the search text does

            if (_display.Count == 0)
            {
                GUILayout.Label(_search.Length > 0 ? "No match." : "No items yet. Items appear once you've found or picked them up.", _rDim);
                return;
            }

            // A long list is drawn by hand so that only the rows you can actually see cost anything (there can be hundreds of items).
            const float rowHeight = 26f;
            Rect area = GUILayoutUtility.GetRect(0f, 100000f, 0f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var content = new Rect(0f, 0f, area.width - 20f, _display.Count * rowHeight);
            _scrollItems = GUI.BeginScrollView(area, _scrollItems, content);

            int first = Mathf.Max(0, Mathf.FloorToInt(_scrollItems.y / rowHeight));
            int last = Mathf.Min(_display.Count - 1, first + Mathf.CeilToInt(area.height / rowHeight) + 1);
            for (int i = first; i <= last; i++)
            {
                DisplayRow row = _display[i];
                float y = i * rowHeight;

                if (row.Header != null)
                {
                    GUI.Label(new Rect(0f, y, content.width, rowHeight), row.Header, _rDim);
                    continue;
                }

                ItemEntry item = row.Item;
                bool on = _rules.Items.Contains(item.Name);
                bool now = GUI.Toggle(new Rect(0f, y + 1f, 22f, 24f), on, GUIContent.none, _rToggle);
                DrawIcon(new Rect(28f, y + 1f, 24f, 24f), item.Icon);
                GUI.Label(new Rect(58f, y + 1f, content.width - 58f, 24f), item.Display, _rText);

                if (now != on)
                {
                    if (now) _rules.Items.Add(item.Name); else _rules.Items.Remove(item.Name);
                    SaveRules();
                }
            }
            GUI.EndScrollView();
        }

        // The rows actually shown on the Items tab: the filtered items, with a heading each time the category changes.
        private class DisplayRow { public ItemEntry Item; public string Header; }
        private List<DisplayRow> _display = new List<DisplayRow>();
        private string _displayFor;

        private void RebuildDisplay()
        {
            _displayFor = _search;
            var rows = new List<DisplayRow>();
            string lastCategory = null;
            foreach (ItemEntry item in _items)
            {
                if (_search.Length > 0 && item.Display.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (item.Category != lastCategory)
                {
                    lastCategory = item.Category;
                    rows.Add(new DisplayRow { Header = item.Category.ToUpperInvariant() });
                }
                rows.Add(new DisplayRow { Item = item });
            }
            _display = rows;
        }

        /// <summary>
        /// Make sure the mouse can move. The game only frees the cursor when it thinks the mouse is the active input device, which on
        /// some setups (Linux, Steam Deck/Steam Input, a controller plugged in) it doesn't, leaving the cursor stuck in the middle of
        /// the screen. So while our window is open we set Unity's cursor directly, every time we draw.
        /// </summary>
        private static void FreeTheMouse()
        {
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        private static void DrawIcon(Rect r, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null || Event.current.type != EventType.Repaint) return;
            Texture2D tex = sprite.texture;
            Rect t = sprite.textureRect;
            GUI.DrawTextureWithTexCoords(r, tex, new Rect(t.x / tex.width, t.y / tex.height, t.width / tex.width, t.height / tex.height));
        }

        // ---- styles ---------------------------------------------------------------------------------

        private Texture2D MakeBox(Color fill, Color border)
        {
            const int size = 6;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    t.SetPixel(x, y, (x < 2 || y < 2 || x >= size - 2 || y >= size - 2) ? border : fill);
            t.Apply();
            _menuTextures.Add(t);
            return t;
        }

        private GUIStyle ButtonLook(Color fill, Color hover, Color border)
        {
            var s = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(8, 8, 3, 3), margin = new RectOffset(3, 3, 3, 3),
            };
            s.normal.background = MakeBox(fill, border);
            s.hover.background = s.active.background = s.focused.background = MakeBox(hover, border);
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = new Color(0.95f, 0.9f, 0.8f);
            return s;
        }

        private void EnsureMenuStyles()
        {
            if (_rTitle != null) return;

            _rTitle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            _rTitle.normal.textColor = Gold;
            _rText = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            _rText.normal.textColor = new Color(0.93f, 0.9f, 0.85f);
            _rDim = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            _rDim.normal.textColor = Dim;

            _rBtn = ButtonLook(new Color(0.22f, 0.19f, 0.15f), new Color(0.33f, 0.27f, 0.18f), new Color(0.45f, 0.36f, 0.2f));
            _rBtnOn = ButtonLook(new Color(0.55f, 0.42f, 0.16f), new Color(0.62f, 0.48f, 0.2f), new Color(0.95f, 0.78f, 0.35f));

            _rToggle = new GUIStyle(GUI.skin.toggle) { fontSize = 14, fontStyle = FontStyle.Bold };
            _rToggle.normal.textColor = _rToggle.onNormal.textColor = _rToggle.hover.textColor = _rToggle.onHover.textColor =
                _rToggle.active.textColor = _rToggle.onActive.textColor = new Color(0.95f, 0.9f, 0.8f);

            _rField = new GUIStyle(GUI.skin.textField) { fontSize = 13 };
        }

        private void DestroyMenuResources()
        {
            foreach (Texture2D t in _menuTextures) if (t != null) Destroy(t);
            _menuTextures.Clear();
            _rTitle = _rText = _rDim = _rBtn = _rBtnOn = _rToggle = _rField = null;
        }
    }

    // Looking at a chest: add a line saying what it has been assigned to receive.
    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    internal static class Container_GetHoverText
    {
        private static void Postfix(Container __instance, ref string __result)
        {
            if (!string.IsNullOrEmpty(__result)) __result += Plugin.HoverSuffix(__instance);
        }
    }

    // While the menu is open: keep the game from reacting to your clicks and typing, and show the mouse.
    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class PlayerController_TakeInput
    {
        private static void Postfix(ref bool __result) { if (Plugin.RulesWindowOpen) __result = false; }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class Player_TakeInput
    {
        private static void Postfix(ref bool __result) { if (Plugin.RulesWindowOpen) __result = false; }
    }

    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    internal static class GameCamera_UpdateMouseCapture
    {
        // While our window is open, skip the game's own cursor handling entirely. If we let it run and then undo it, the game locks the
        // cursor every frame, and on Linux locking physically snaps the pointer to the middle of the screen, so it looks stuck there.
        private static bool Prefix()
        {
            if (!Plugin.RulesWindowOpen) return true;
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
            return false;
        }
    }
}

namespace QualityOfLife
{
    // While one of our windows is open, the mouse wheel should scroll the window, not zoom the camera (or cycle the hotbar).
    [HarmonyLib.HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ZInput_GetMouseScrollWheel
    {
        private static void Postfix(ref float __result) { if (Plugin.RulesWindowOpen) __result = 0f; }
    }
}
