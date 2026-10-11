using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static DHack.Shared.Translations;

namespace QualityOfLife
{
    /// <summary>
    /// The menu for telling a chest what it should receive. Two tabs: whole categories (high level) and individual items
    /// (granular, searchable). Opens when you look at a chest and press the assign key, or from the button beside an open chest.
    /// Built from the game's own interface (GameUi.cs). Opened from an open chest it takes the crafting panel's place beside the chest, which
    /// stays open; opened from the world it is a window of its own in the middle of the screen.
    /// </summary>
    public partial class Plugin
    {
        /// <summary>True while the menu is open (the input patches in GameUi.cs use this to keep the game from reacting to your clicks and typing).</summary>
        internal static bool RulesWindowOpen;

        private Container _rulesChest;
        private ChestRules _rules;
        private int _rulesTab;
        private string _search = "";

        private class ItemEntry { public string Name, Display, Category; public Sprite Icon; public int Rank; }
        private List<ItemEntry> _items = new List<ItemEntry>();

        private static readonly System.Reflection.FieldInfo CurrentContainerField = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");

        // ---- opening and closing ------------------------------------------------------------------

        internal static Container OpenChest() => InventoryGui.instance != null ? CurrentContainerField?.GetValue(InventoryGui.instance) as Container : null;

        /// <summary>A chest that can be assigned things: not the LedgerChest mod's Ledger Chest, which keeps nothing (it sends all on).</summary>
        private static bool Assignable(Container c) => c != null && !c.gameObject.name.StartsWith("piece_ledgerchest", StringComparison.Ordinal);
        private static Container LookedAtChest(Player p) => p.GetHoverObject()?.GetComponentInParent<Container>();

        private void HandleAssignKey(Player player)
        {
            Container chest = InventoryGui.IsVisible() ? OpenChest() : LookedAtChest(player);
            if (chest == null) { Tell(player, T("qualityoflife_assign_look", _assignKey.Value)); return; }
            if (!Assignable(chest)) { Tell(player, T("qualityoflife_assign_ledger")); return; }
            if (!Usable(chest)) { Tell(player, T("qualityoflife_assign_cant_use")); return; }
            if (chest != OpenChest() && ContainerRegistry.InUse(chest)) { Tell(player, T("qualityoflife_assign_in_use")); return; }
            OpenRules(chest, player);
        }

        /// <summary>Opens the menu for a chest (Claude Tools' "shoot open qolrules" calls it by reflection: keep its name and parameters).</summary>
        private void OpenRules(Container chest, Player player)
        {
            if (chest == null || player == null || !GameUi.Ready()) return;
            if (RulesWindowOpen) CloseRules();
            InventoryGui gui = InventoryGui.instance;

            // From the chest you have open (its button, or the key): the menu takes the crafting panel's place beside it and the chest stays
            // open, so you see what is in it while you choose. From the world: a window of its own, and the inventory goes away so clicks
            // in the menu can't reach it.
            bool docked = InventoryGui.IsVisible() && chest == OpenChest() && gui.m_crafting != null;
            if (!docked && InventoryGui.IsVisible()) gui.Hide();

            _rulesChest = chest;
            _rules = ChestRules.Read(chest);
            _rulesTab = 0;
            _search = "";
            _rulesDocked = docked;
            _rulesScroll[0] = _rulesScroll[1] = 0f;
            BuildItemList(player);
            RulesWindowOpen = true;
            _rulesOpenedAt = Time.unscaledTime;
            BuildRulesWindow();
        }

        /// <summary>Closes the menu, as Escape does (Claude Tools' "shoot close" calls it by reflection: keep its name).</summary>
        public static void CloseFromEscape() => Instance?.CloseRules();

        private void CloseRules()
        {
            RulesWindowOpen = false;
            _rulesChest = null;
            if (_rulesWindow != null) Destroy(_rulesWindow.gameObject);
            _rulesWindow = null; _rulesList = null; _rulesAway = null; _searchField = null; _noItems = null;
            _rows.Clear();
            UITooltip.HideTooltip();
        }

        /// <summary>The search box has the keyboard (other keys are left alone meanwhile).</summary>
        private bool Typing => RulesWindowOpen && _searchField != null && _searchField.isFocused;

        /// <summary>Called every frame while the menu is open. Returns true if it's open (so other keys should be ignored).</summary>
        private bool UpdateRulesWindow(Player player)
        {
            if (!RulesWindowOpen) return false;
            bool settled = Time.unscaledTime > _rulesOpenedAt + 0.3f;
            bool close = _rulesChest == null || _rulesWindow == null      // the chest is gone, or the game rebuilt its interface (a world change)
                         || !_stackEnabled.Value || Menu.IsVisible()
                         || (_rulesChest.transform.position - player.transform.position).sqrMagnitude > 15f * 15f
                         || (settled && !Typing && Input.GetKeyDown(_assignKey.Value));           // the key again
            if (!close && _rulesDocked) close = !InventoryGui.IsVisible() || OpenChest() != _rulesChest;   // the chest was closed
            else if (!close) close = settled && InventoryGui.IsVisible();   // the inventory was opened (Tab) over the window
            if (close) { CloseRules(); return true; }

            int away = Mathf.RoundToInt(Vector3.Distance(_rulesChest.transform.position, player.transform.position));
            if (_rulesAway != null && away != _awayShown)
            {
                _awayShown = away;
                _rulesAway.text = Localization.instance.Localize(_rulesChest.m_name) + "  ·  " + T("qualityoflife_m_away", away);
            }
            if (_rulesDirty) BuildRulesWindow();
            else
            {
                if (_rulesList != null) _rulesScroll[_rulesTab] = _rulesList.anchoredPosition.y;
                if (_rulesTab == 1) UpdateItemRows();
            }
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
            if (_rulesChest == null || ChestRules.Write(_rulesChest, _rules)) return;
            _rules = ChestRules.Read(_rulesChest); // not saved: show what the chest really has
            if (Player.m_localPlayer != null) Tell(Player.m_localPlayer, T("qualityoflife_save_in_use"));
        }

        // What the menu's clicks do: run from Update (through _pending), never while the menu is being drawn, as they save to the chest.

        private void ToggleCategory(string category)
        {
            if (!RulesWindowOpen || _rules == null) return;
            if (!_rules.Categories.Remove(category)) _rules.Categories.Add(category);
            SaveRules();
            _rulesDirty = true;
        }

        private void ToggleItem(string itemName)
        {
            if (!RulesWindowOpen || _rules == null) return;
            if (!_rules.Items.Remove(itemName)) _rules.Items.Add(itemName);
            SaveRules();
            _rulesDirty = true;
        }

        private void ClearRules()
        {
            if (!RulesWindowOpen || _rules == null) return;
            _rules.Categories.Clear();
            _rules.Items.Clear();
            SaveRules();
            _rulesDirty = true;
        }

        // ---- the button beside an open chest ------------------------------------------------------

        private const string AssignName = "QoLAssignChest";
        private RectTransform _assignRow;
        private string _assignShown = "";

        private Container _summaryChest;
        private List<KeyValuePair<string, Color>> _summaryChips;
        private string _summarySig = "";
        private float _nextSummary;

        /// <summary>
        /// The "Assign items to this chest" button under the open chest's panel, with what the chest receives as little tags under it. Part of
        /// the inventory's own interface, so it moves, scales and hides with it. Built again only when what it shows changes.
        /// </summary>
        private void UpdateAssignButton(InventoryGui gui)
        {
            Container chest = _stackEnabled.Value && gui.m_container != null ? OpenChest() : null;
            if (chest == null || !Assignable(chest)) { DestroyAssignRow(); return; }

            if (chest != _summaryChest || Time.unscaledTime >= _nextSummary)
            {
                // what this chest receives, read twice a second (not every frame)
                _summaryChest = chest;
                _summaryChips = _showChestLabels.Value ? BuildChips(ChestRules.Read(chest)) : null;
                _summarySig = _summaryChips == null ? "-" : string.Join("|", _summaryChips.Select(c => c.Key).ToArray());
                _nextSummary = Time.unscaledTime + 0.5f;
            }
            float width = Mathf.Max(220f, gui.m_container.rect.width) - 16f;
            string signature = chest.GetInstanceID() + "|" + Mathf.RoundToInt(width) + "|" + _summarySig + "|" + Localization.instance.GetSelectedLanguage();
            if (_assignRow != null && signature == _assignShown) return;

            DestroyAssignRow();
            foreach (Transform old in gui.m_container.Cast<Transform>().Where(t => t.name == AssignName).ToList()) Destroy(old.gameObject);   // (a hot reload's)
            _assignShown = signature;
            _assignRow = GameUi.Rect(gui.m_container, AssignName, Vector2.zero, new Vector2(0f, 1f), new Vector2(8f, -6f), new Vector2(width, 30f));
            Container target = chest;
            string assignText = T("qualityoflife_assign_title");
            var assign = GameUi.Button(_assignRow, assignText, Vector2.zero, new Vector2(220f, 30f), () =>
            {
                Player p = Player.m_localPlayer;
                _pending = () => OpenRules(target, p);
            }, true, 15f);
            TMP_Text assignLabel = GameUi.Label(assign);   // (a longer title in another language: the button grows to fit it, up to the panel's width)
            float assignW = assignLabel != null ? Mathf.Min(width, Mathf.Ceil(assignLabel.GetPreferredValues(assignText).x) + 24f) : 0f;
            if (assignW > 220f) ((RectTransform)assign.transform).sizeDelta = new Vector2(assignW, 30f);

            if (_summaryChips == null) return;
            if (_summaryChips.Count == 0)
            {
                TMP_Text none = GameUi.Label(_assignRow, T("qualityoflife_receives_nothing"), new Vector2(0f, -36f), new Vector2(width, 20f), 13f, GameUi.Dim);
                none.enableWordWrapping = false;
                return;
            }
            // What this chest receives, as little tags, so you can see its job at a glance: wraps to fit the width.
            const float h = 20f, gap = 4f;
            float x = 0f, y = 36f;
            foreach (KeyValuePair<string, Color> chip in _summaryChips)
            {
                RectTransform tag = GameUi.Tag(_assignRow, chip.Key, new Vector2(x, -y), h, 13f, chip.Value, GameUi.Text);
                float w = tag.sizeDelta.x;
                if (x > 0f && x + w > width) { x = 0f; y += h + gap; tag.anchoredPosition = new Vector2(x, -y); }
                x += w + gap;
            }
        }

        private void DestroyAssignRow()
        {
            if (_assignRow != null) Destroy(_assignRow.gameObject);
            _assignRow = null;
            _assignShown = "";
        }

        /// <summary>The tags for a chest: its categories first, then a few item names, then "+N more".</summary>
        private static List<KeyValuePair<string, Color>> BuildChips(ChestRules rules)
        {
            var chips = new List<KeyValuePair<string, Color>>();
            foreach (string c in Categories.All.Where(rules.Categories.Contains)) chips.Add(new KeyValuePair<string, Color>(Categories.Display(c), CategoryChip));

            // Individual items: show a few by name, then "+N more" so a long list can't take over the screen.
            List<string> names = rules.Items.Select(ItemDisplayName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string n in names.Take(6)) chips.Add(new KeyValuePair<string, Color>(n, ItemChip));
            if (names.Count > 6) chips.Add(new KeyValuePair<string, Color>(T("qualityoflife_n_more", names.Count - 6), ItemChip));
            return chips;
        }

        private static readonly Color CategoryChip = new Color(0.55f, 0.42f, 0.16f, 0.97f);
        private static readonly Color ItemChip = new Color(0.2f, 0.32f, 0.45f, 0.97f);

        /// <summary>The extra line shown when you look at a chest (added by a hook on the game's chest hover text).</summary>
        internal static string HoverSuffix(Container chest)
        {
            Plugin plugin = Instance;
            if (plugin == null || !plugin._stackEnabled.Value || !plugin._showChestLabels.Value) return "";
            if (!Assignable(chest)) return "";

            // The game asks for hover text every frame while you look at a chest. Work it out once a second and reuse it.
            if (chest == plugin._hoverChest && Time.unscaledTime < plugin._nextHover) return plugin._hoverText;
            try
            {
                ChestRules rules = ChestRules.Read(chest);
                string text = "";
                if (!rules.IsEmpty)
                {
                    // Its categories, then its items by name: two on the first line, up to three more on the next, then "+N more".
                    var parts = new List<string>(Categories.All.Where(rules.Categories.Contains).Select(Categories.Display));
                    parts.AddRange(rules.Items.Select(ItemDisplayName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
                    const int firstLine = 2, shown = 5;
                    text = "\n<color=#F2C75A>" + T("qualityoflife_receives") + "</color> " + string.Join(", ", parts.Take(firstLine).ToArray());
                    if (parts.Count > firstLine)
                    {
                        text += ",\n" + string.Join(", ", parts.Skip(firstLine).Take(shown - firstLine).ToArray());
                        if (parts.Count > shown) text += " <color=#BBBBBB>" + T("qualityoflife_n_more", parts.Count - shown) + "</color>";
                    }
                }
                plugin._hoverChest = chest;
                plugin._hoverText = text;
                plugin._nextHover = Time.unscaledTime + 1f;
                return text;
            }
            catch (Exception) { return ""; }
        }

        /// <summary>An item's name as the game shows it; an item the game has no name for (not in this version) gets a tidied form of its key.</summary>
        private static string ItemDisplayName(string itemName)
        {
            string shown = Localization.instance.Localize(itemName);
            if (!string.IsNullOrEmpty(shown) && !shown.Contains("[")) return shown;
            string plain = (itemName ?? "").Replace("$item_", "").Replace("$", "").Replace('_', ' ').Trim();
            return plain.Length > 0 ? char.ToUpperInvariant(plain[0]) + plain.Substring(1) : itemName;
        }

        private Container _hoverChest;
        private string _hoverText = "";
        private float _nextHover;

        // ---- the window ---------------------------------------------------------------------------

        private const string RulesWindowName = "QoLAssignMenu";
        private const float RulesPad = 26f, RowH = 32f;

        private RectTransform _rulesWindow, _rulesList;
        private TMP_Text _rulesAway, _noItems;
        private TMP_InputField _searchField;
        private bool _rulesDocked, _rulesDirty;
        private float _rulesOpenedAt;
        private int _awayShown = -1, _rowsFirst = -1;
        private readonly float[] _rulesScroll = new float[2];   // each tab keeps its own scroll position

        /// <summary>
        /// Builds the whole menu (again): when it opens and after anything it shows changed (a tab, an assignment). Only the search and the
        /// scrolling of the item list are handled without building it again (UpdateItemRows).
        /// </summary>
        private void BuildRulesWindow()
        {
            _rulesDirty = false;
            if (_rulesWindow != null) Destroy(_rulesWindow.gameObject);
            _rulesWindow = null; _rulesList = null; _rulesAway = null; _searchField = null; _noItems = null;
            _rows.Clear();
            _rowsFirst = -1; _awayShown = -1;
            UITooltip.HideTooltip();
            InventoryGui gui = InventoryGui.instance;
            if (_rulesChest == null || _rules == null || gui == null || !GameUi.Ready()) { CloseRules(); return; }

            if (_rulesDocked) _rulesWindow = GameUi.Over(gui.m_crafting, RulesWindowName, new Vector2(520f, 600f));
            else
            {
                var screen = (RectTransform)GameUi.Canvas;
                Vector2 room = screen != null && screen.rect.height > 200f ? screen.rect.size - new Vector2(40f, 40f) : new Vector2(600f, 700f);
                _rulesWindow = GameUi.Window(RulesWindowName, new Vector2(Mathf.Min(600f, room.x), Mathf.Min(700f, room.y)));
            }
            float W = _rulesWindow.sizeDelta.x, H = _rulesWindow.sizeDelta.y, inner = W - RulesPad * 2f;
            Transform w = _rulesWindow;

            // header: title, Close, the braid, which chest and how far
            TMP_Text title = GameUi.Label(w, T("qualityoflife_assign_title"), new Vector2(RulesPad, -18f), new Vector2(inner - 120f, 40f), 30f, GameUi.Warm, GameUi.Norse);
            title.enableWordWrapping = false;
            title.enableAutoSizing = true; title.fontSizeMin = 18f; title.fontSizeMax = 30f;
            GameUi.Button(w, Localization.instance.Localize("$menu_close"), new Vector2(W - RulesPad - 110f, -20f), new Vector2(110f, 36f), CloseRules, true, 18f);
            GameUi.Braid(w, new Vector2(0f, -64f), inner);
            float y = 76f;
            _rulesAway = GameUi.Label(w, "", new Vector2(RulesPad, -y), new Vector2(inner, 24f), 17f, GameUi.Dim);
            _rulesAway.enableWordWrapping = false;
            y += 26f;
            string note = T("qualityoflife_assign_note");
            TMP_Text info = GameUi.Label(w, note, new Vector2(RulesPad, -y), new Vector2(inner, 24f), 16f, GameUi.Dim);
            float noteH = info.GetPreferredValues(note, inner, 0f).y;
            ((RectTransform)info.transform).sizeDelta = new Vector2(inner, noteH);
            y += noteH + 12f;

            // tabs
            float tabW = (inner - 8f) / 2f;
            string[] tabs = { T("qualityoflife_tab_categories", _rules.Categories.Count), T("qualityoflife_tab_items", _rules.Items.Count) };
            for (int i = 0; i < 2; i++)
            {
                int tab = i;
                GameUi.Tab(w, tabs[i], new Vector2(RulesPad + i * (tabW + 8f), -y), new Vector2(tabW, 38f), _rulesTab == i, () =>
                {
                    if (_rulesList != null) _rulesScroll[_rulesTab] = _rulesList.anchoredPosition.y;   // (the tab you leave keeps its place)
                    _rulesTab = tab;
                    _rulesDirty = true;
                });
            }
            y += 48f;

            // the Items tab's search
            if (_rulesTab == 1)
            {
                TMP_Text searchLabel = GameUi.Label(w, T("qualityoflife_search"), new Vector2(RulesPad, -y - 6f), new Vector2(80f, 26f), 18f, GameUi.Text);
                searchLabel.enableAutoSizing = true; searchLabel.fontSizeMin = 12f; searchLabel.fontSizeMax = 18f;   // (a longer word in another language)
                _searchField = GameUi.Field(w, _search, T("qualityoflife_search_hint"), new Vector2(RulesPad + 84f, -y), new Vector2(inner - 84f - 100f, 34f));
                _searchField.onValueChanged.AddListener(text => _search = text ?? "");   // (the list follows in UpdateItemRows)
                GameUi.Button(w, T("qualityoflife_clear"), new Vector2(W - RulesPad - 92f, -y), new Vector2(92f, 34f), () => { if (_searchField != null) _searchField.text = ""; }, true, 16f);
                y += 44f;
            }

            // the list, and the footer under it
            float footer = H - RulesPad - 40f;
            RectTransform box = GameUi.Box(w, new Vector2(RulesPad - 6f, -y), new Vector2(inner + 12f, footer - y - 10f));
            _rulesList = GameUi.Scroll(box, 8f);
            if (_rulesTab == 0) BuildCategoryRows(); else BuildItemRows();

            string status = _rules.IsEmpty ? T("qualityoflife_status_empty") : T("qualityoflife_status_saved");
            TMP_Text statusText = GameUi.Label(w, status, new Vector2(RulesPad, -footer), new Vector2(inner - 196f, 40f), 16f, GameUi.Dim, GameUi.Sans, TextAlignmentOptions.MidlineLeft);
            statusText.enableAutoSizing = true; statusText.fontSizeMin = 12f; statusText.fontSizeMax = 16f;   // (longer in some languages)
            GameUi.Button(w, T("qualityoflife_clear_all"), new Vector2(W - RulesPad - 180f, -footer), new Vector2(180f, 40f), () => _pending = ClearRules, !_rules.IsEmpty, 17f);
        }

        /// <summary>Puts the list back where it was scrolled to (each tab keeps its own), within what there is to scroll.</summary>
        private void RestoreScroll(float height)
        {
            float view = ((RectTransform)_rulesList.parent).sizeDelta.y;
            _rulesList.sizeDelta = new Vector2(_rulesList.sizeDelta.x, height);
            _rulesList.anchoredPosition = new Vector2(0f, Mathf.Clamp(_rulesScroll[_rulesTab], 0f, Mathf.Max(0f, height - view)));
        }

        /// <summary>The Categories tab: each category with a switch that says whether the chest receives it, and what it holds.</summary>
        private void BuildCategoryRows()
        {
            const float rowH = 60f;
            float width = _rulesList.sizeDelta.x, y = 0f;
            foreach (string category in Categories.All)
            {
                bool on = _rules.Categories.Contains(category);
                string c = category;
                Button toggle = Squeeze(GameUi.Button(_rulesList, OnOff(on), new Vector2(4f, -y - 12f), new Vector2(64f, 32f), () => _pending = () => ToggleCategory(c), true, 16f));
                if (on) GameUi.Label(toggle).color = GameUi.Gold;
                TMP_Text name = GameUi.Label(_rulesList, Categories.Display(category), new Vector2(80f, -y - 4f), new Vector2(width - 84f, 36f), 20f, on ? GameUi.Gold : GameUi.Text);
                name.enableWordWrapping = false;
                string help = Categories.HelpOf(category);
                TMP_Text helpText = GameUi.Label(_rulesList, help, new Vector2(80f, -y - 32f), new Vector2(width - 84f, 24f), 15f, GameUi.Dim);
                helpText.enableWordWrapping = false;
                helpText.overflowMode = TextOverflowModes.Ellipsis;
                y += rowH;
            }
            RestoreScroll(y - 4f);
        }

        // ---- the Items tab: hundreds of items, so only the rows you can see exist (moved and refilled as you scroll) ----

        private class ItemRow { public RectTransform Root; public TMP_Text Header, Name; public Button Toggle; public Image Icon; public int Index = -1; }
        private readonly List<ItemRow> _rows = new List<ItemRow>();

        private string NoItemsText => _search.Length > 0 ? T("qualityoflife_no_match") : T("qualityoflife_no_items");

        /// <summary>A small button whose word may be longer in another language ("Désactivé"): the word shrinks to fit.</summary>
        private static Button Squeeze(Button b)
        {
            TMP_Text label = GameUi.Label(b);
            if (label != null) { label.fontSizeMax = label.fontSize; label.fontSizeMin = 10f; label.enableAutoSizing = true; }
            return b;
        }

        private static string OnOff(bool on) => Localization.instance.Localize(on ? "$hud_on" : "$hud_off");

        private void BuildItemRows()
        {
            if (_displayFor != _search) RebuildDisplay();
            float width = _rulesList.sizeDelta.x;
            _noItems = GameUi.Label(_rulesList, NoItemsText, new Vector2(8f, -8f), new Vector2(width - 16f, 60f), 17f, GameUi.Dim);
            _noItems.gameObject.SetActive(_display.Count == 0);
            float view = ((RectTransform)_rulesList.parent).sizeDelta.y;
            int pool = Mathf.CeilToInt(view / RowH) + 2;
            for (int k = 0; k < pool; k++) _rows.Add(MakeRow(width));
            RestoreScroll(_display.Count * RowH);
            _rowsFirst = -1;
            UpdateItemRows();
        }

        /// <summary>Every frame on the Items tab: follows the search text and the scrolling (cheap when neither changed).</summary>
        private void UpdateItemRows()
        {
            if (_rulesList == null) return;
            if (_displayFor != _search)
            {
                RebuildDisplay();
                _rulesScroll[1] = 0f;
                RestoreScroll(_display.Count * RowH);
                _rowsFirst = -1;
                if (_noItems != null)
                {
                    _noItems.text = NoItemsText;
                    _noItems.gameObject.SetActive(_display.Count == 0);
                }
            }
            int first = Mathf.Max(0, Mathf.FloorToInt(_rulesList.anchoredPosition.y / RowH));
            if (first == _rowsFirst) return;
            _rowsFirst = first;
            for (int k = 0; k < _rows.Count; k++) ShowRow(_rows[k], first + k);
        }

        private ItemRow MakeRow(float width)
        {
            var row = new ItemRow();
            row.Root = GameUi.Rect(_rulesList, "Row", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(width, RowH));
            row.Header = GameUi.Label(row.Root, "", new Vector2(8f, -9f), new Vector2(width - 16f, RowH - 9f), 15f, GameUi.Warm, GameUi.Serif);
            row.Header.enableWordWrapping = false;
            row.Toggle = GameUi.Button(row.Root, OnOff(false), new Vector2(4f, -2f), new Vector2(60f, RowH - 4f), () =>
            {
                ItemEntry item = row.Index >= 0 && row.Index < _display.Count ? _display[row.Index].Item : null;
                if (item != null) { string itemName = item.Name; _pending = () => ToggleItem(itemName); }
            }, true, 15f);
            Squeeze(row.Toggle);
            row.Icon = GameUi.Icon(row.Root, new Vector2(72f, -2f), new Vector2(RowH - 4f, RowH - 4f), null);
            row.Name = GameUi.Label(row.Root, "", new Vector2(72f + RowH + 2f, -5f), new Vector2(width - 110f, RowH - 6f), 18f, GameUi.Text);
            row.Name.enableWordWrapping = false;
            row.Name.overflowMode = TextOverflowModes.Ellipsis;
            return row;
        }

        private void ShowRow(ItemRow row, int index)
        {
            row.Index = index;
            bool there = index < _display.Count;
            row.Root.gameObject.SetActive(there);
            if (!there) return;
            row.Root.anchoredPosition = new Vector2(0f, -index * RowH);
            DisplayRow shown = _display[index];
            bool header = shown.Header != null;
            row.Header.gameObject.SetActive(header);
            row.Toggle.gameObject.SetActive(!header);
            row.Icon.gameObject.SetActive(!header);
            row.Name.gameObject.SetActive(!header);
            if (header) { row.Header.text = shown.Header; return; }

            bool on = _rules.Items.Contains(shown.Item.Name);
            TMP_Text label = GameUi.Label(row.Toggle);
            if (label != null) { label.text = OnOff(on); label.color = on ? GameUi.Gold : GameUi.Text; }
            row.Icon.sprite = shown.Item.Icon;
            row.Icon.enabled = shown.Item.Icon != null;
            row.Name.text = shown.Item.Display;
            row.Name.color = on ? GameUi.Gold : GameUi.Text;
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
                    rows.Add(new DisplayRow { Header = Categories.Display(item.Category).ToUpperInvariant() });
                }
                rows.Add(new DisplayRow { Item = item });
            }
            _display = rows;
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
}
