using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace LedgerChest
{
    /// <summary>
    /// The ledger: while a Ledger Chest is open, the game's own chest window (a copy of it, in its place) shows every kind of item in the
    /// chests around it, one slot each with how many there are in all. It works like a chest:
    ///   - click an item: all of it on your cursor; put it down on a slot of your inventory and as much as fits comes out of the chests;
    ///   - Shift-click: the game's split slider, how many; Ctrl-click: all that fits, straight in; right-click: a stack, straight in;
    ///   - drop something from your inventory onto it: it goes to the chest assigned it (else an unassigned chest), as anything put in it.
    /// A search above it narrows it down; under it, which chests hold what is under your mouse, and what was moved.
    /// The slots it shows are a picture of the chests: copies, in a list of its own that is never saved, never the items themselves.
    /// </summary>
    internal class Ledger
    {
        private static readonly AccessTools.FieldRef<InventoryGui, Container> Current = AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");
        private static readonly AccessTools.FieldRef<InventoryGui, GameObject> DragGo = AccessTools.FieldRefAccess<InventoryGui, GameObject>("m_dragGo");
        private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> DragItem = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_dragItem");
        private static readonly AccessTools.FieldRef<InventoryGui, Inventory> DragInventory = AccessTools.FieldRefAccess<InventoryGui, Inventory>("m_dragInventory");
        private static readonly AccessTools.FieldRef<InventoryGui, int> DragAmount = AccessTools.FieldRefAccess<InventoryGui, int>("m_dragAmount");
        private static readonly MethodInfo SetupDrag = AccessTools.Method(typeof(InventoryGui), "SetupDragItem");
        private static readonly AccessTools.FieldRef<Inventory, List<ItemDrop.ItemData>> Items = AccessTools.FieldRefAccess<Inventory, List<ItemDrop.ItemData>>("m_inventory");
        private static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>> Elements = AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");
        private static readonly MethodInfo Hovered = AccessTools.Method(typeof(InventoryGrid), "GetHoveredElement");

        private const int Width = 8;
        /// <summary>The name of the ledger's own list of slots (a picture of the chests, never saved): how the patches below know it.</summary>
        internal const string DisplayName = "LedgerChest.Ledger";
        internal static bool IsDisplay(Inventory inv) => inv != null && inv.GetName() == DisplayName;
        private static readonly MethodInfo SplitDialog = AccessTools.Method(typeof(InventoryGui), "ShowSplitDialog");
        private static readonly PropertyInfo SplitDropping = AccessTools.Property(typeof(InventoryGui), "IsSplitDropping");
        private Line _carry;                                         // what is on your cursor from the ledger (all of it, or the amount chosen)

        private Container _open;
        private List<Container> _chests = new List<Container>();
        private List<Line> _lines = new List<Line>();
        private readonly List<Line> _shown = new List<Line>();      // in slot order
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(); // how many things under each button (after the search)
        private string _search = "", _built = null, _note = "";
        private float _noteUntil, _nextScan, _nextSend, _nextError;
        private bool _focused, _dirty = true;
        private int _order;                                          // 0 by kind, 1 A to Z, 2 most first
        private string _kind = Kinds.All;                            // the category button that's down
        private static readonly string[] OrderNames = { "By kind", "A to Z", "Most first" };

        private GameObject _panel;
        private InventoryGrid _grid;
        private TMP_Text _title, _weight;
        private Inventory _display;
        private Rect _screen;                                        // the panel on screen (for the search above it and the notes under it)
        private GUIStyle _field, _button, _text, _dim, _chip, _chipOn;
        private Texture2D _fieldTex, _buttonTex, _buttonHover, _backTex, _chipOnTex;

        public bool Typing => _open != null && _focused;

        private static Container OpenLedger(InventoryGui gui)
        {
            if (gui == null || !InventoryGui.IsVisible()) return null;
            Container c = Current(gui);
            return c != null && Plugin.IsLedger(c) ? c : null;
        }

        // ---- every frame (Update) -------------------------------------------------------------------------------------------------------

        public void Tick()
        {
            Container now = OpenLedger(InventoryGui.instance);
            if (now != _open) { _open = now; _search = ""; _built = null; _nextScan = 0f; _focused = false; }
            if (_open == null || Player.m_localPlayer == null) return;
            // Anything put in the Ledger Chest itself (Ctrl-click from your inventory, Stack to chests) goes on to its chest.
            if (Time.unscaledTime >= _nextSend && _open.GetInventory().NrOfItems() > 0 && DragGo(InventoryGui.instance) == null)
            {
                _nextSend = Time.unscaledTime + 0.15f;
                Send();
            }
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.5f;
                _chests = Stores.Around(_open, Player.m_localPlayer);
                _lines = Stores.Lines(_chests);
                _dirty = true;
            }
        }

        /// <summary>After the game updated its inventory window: our window in place of the chest's.</summary>
        public void AfterGui(InventoryGui gui)
        {
            if (_open == null || gui == null) { if (_panel != null && _panel.activeSelf) _panel.SetActive(false); return; }
            if (!Build(gui)) return;
            gui.m_container.gameObject.SetActive(false);   // the Ledger Chest's own slots: it keeps nothing, the ledger stands in their place
            if (!_panel.activeSelf) { _panel.SetActive(true); _grid.ResetView(); }
            _panel.transform.SetAsLastSibling();

            if (!Carrying && (_dirty || _built != _search)) Fill();   // (not while something from it is on your cursor: that is one of its slots)
            try { _grid.UpdateInventory(_display, null, null); Amounts(); }
            catch (Exception ex) { if (Time.unscaledTime >= _nextError) { _nextError = Time.unscaledTime + 10f; Debug.LogWarning($"[{Plugin.Name}] the ledger window: {ex}"); Diagnose(); } }
            _title.text = "";                              // (the search sits in the title bar)
            if (_weight != null) _weight.text = $"{_chests.Count}";
            var corners = new Vector3[4];
            ((RectTransform)_panel.transform).GetWorldCorners(corners);
            Canvas canvas = _panel.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 lo = RectTransformUtility.WorldToScreenPoint(cam, corners[0]), hi = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            _screen = new Rect(lo.x, Screen.height - hi.y, hi.x - lo.x, hi.y - lo.y);
        }

        public void Close()
        {
            if (_open != null && Player.m_localPlayer != null) Send(); // (put in just before closing)
            _open = null;
            _focused = false;
            if (_panel != null) _panel.SetActive(false);
        }

        public void Destroy()
        {
            if (_panel != null) UnityEngine.Object.Destroy(_panel);
            _panel = null;
        }

        // ---- the window: a copy of the game's chest window ------------------------------------------------------------------------------

        private bool Build(InventoryGui gui)
        {
            if (_panel != null) return true;
            if (gui.m_container == null) return false;
            foreach (Transform old in gui.m_container.parent) if (old.name == "LedgerChestPanel") UnityEngine.Object.Destroy(old.gameObject);
            _panel = UnityEngine.Object.Instantiate(gui.m_container.gameObject, gui.m_container.parent);
            _panel.name = "LedgerChestPanel";
            _grid = _panel.GetComponentInChildren<InventoryGrid>(true);
            if (_grid == null) { UnityEngine.Object.Destroy(_panel); _panel = null; return false; }
            if (_grid.m_gridRoot != null) foreach (Transform cell in _grid.m_gridRoot) UnityEngine.Object.Destroy(cell.gameObject); // (the chest's own cells come along: the grid makes its own)
            _grid.m_onSelected = OnSelected;
            _grid.m_onRightClick = OnRightClick;
            _grid.m_onReleased = (g, i, p) => { };      // the grid calls these without asking whether anyone listens
            _grid.m_onEnter = (g, p) => { };
            _grid.OnSetTouchSelection = g => { };
            _grid.CanDropDragOntoItem = i => false;    // (asked for every slot: the whole window takes a drop, not a slot)
            _title = Twin(gui.m_container, _panel.transform, gui.m_containerName) as TMP_Text;
            _weight = Twin(gui.m_container, _panel.transform, gui.m_containerWeight) as TMP_Text;
            foreach (Component b in new Component[] { Twin(gui.m_container, _panel.transform, gui.m_takeAllButton), Twin(gui.m_container, _panel.transform, gui.m_stackAllButton) })
                if (b != null) b.gameObject.SetActive(false);   // (nothing to take all of, or stack, here)
            _display = new Inventory(DisplayName, null, Width, 4);
            return true;
        }

        /// <summary>The copy's counterpart of something inside the game's chest window (found by its path in the window).</summary>
        private static Component Twin(Transform original, Transform copy, Component part)
        {
            if (part == null) return null;
            var path = new List<string>();
            for (Transform t = part.transform; t != null && t != original; t = t.parent) path.Insert(0, t.name);
            Transform found = copy.Find(string.Join("/", path.ToArray()));
            return found != null ? found.GetComponent(part.GetType()) : null;
        }

        /// <summary>The slots: one per kind of item (filtered by the search), copies with their totals, in a list that is never saved.</summary>
        private void Fill()
        {
            _dirty = false;
            _built = _search;
            string[] words = (_search ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            IEnumerable<Line> q = _lines.Where(l => words.All(w => l.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0));
            _counts.Clear();
            foreach (Line l in q) _counts[l.Kind] = (_counts.TryGetValue(l.Kind, out int n) ? n : 0) + 1;
            _counts[Kinds.All] = q.Count();
            if (_kind != Kinds.All) q = q.Where(l => l.Kind == _kind);
            _shown.Clear();
            _shown.AddRange(_order == 2 ? q.OrderByDescending(l => l.Total).ThenBy(l => l.Name)
                          : _order == 1 ? q.OrderBy(l => l.Name)
                          : q.OrderBy(l => Kinds.RankOf(l.Kind)).ThenBy(l => l.Name));
            int rows = Mathf.Max(4, Mathf.CeilToInt(_shown.Count / (float)Width));
            if (_display.GetHeight() != rows) _display = new Inventory(DisplayName, null, Width, rows);
            List<ItemDrop.ItemData> list = Items(_display);
            list.Clear();
            for (int i = 0; i < _shown.Count; i++)
            {
                ItemDrop.ItemData sample = Sample(_shown[i]);
                if (sample == null) continue;
                ItemDrop.ItemData copy = sample.Clone();
                copy.m_stack = Mathf.Max(1, _shown[i].Total);   // all of it: what you pick up (or split) is up to the total
                copy.m_equipped = false;
                copy.m_gridPos = new Vector2i(i % Width, i / Width);
                list.Add(copy);
            }
        }

        /// <summary>What in the slots trips the game's grid (logged, for finding it).</summary>
        private void Diagnose()
        {
            var elements = Elements(_grid);
            Debug.LogWarning($"[{Plugin.Name}] grid: elements {elements?.Count}, uiGroup {(_grid.m_uiGroup != null)}, tooltipAnchor {(_grid.m_tooltipAnchor != null)}, gridRoot {(_grid.m_gridRoot != null)}, prefab {(_grid.m_elementPrefab != null)}");
            if (elements != null && elements.Count > 0)
            {
                InventoryElement e = elements[1];
                Debug.LogWarning($"[{Plugin.Name}] element: icon {(e.m_icon != null)}, durability {(e.m_durability != null)}, equiped {(e.m_equiped != null)}, queued {(e.m_queued != null)}, noteleport {(e.m_noteleport != null)}, food {(e.m_food != null)}, quality {(e.m_quality != null)}, amount {(e.m_amount != null)}, tooltip {(e.m_tooltip != null)}, dropFocus {(e.m_dropFocus != null)}, selected {(e.m_selected != null)}");
            }
            foreach (ItemDrop.ItemData i in _display.GetAllItems().Take(4))
            {
                string bad = "";
                try { i.GetIcon(); } catch { bad += " icon"; }
                try { i.GetMaxDurability(); i.GetDurabilityPercentage(); } catch { bad += " durability"; }
                try { bool t = !i.m_shared.m_teleportable && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.TeleportAll); } catch { bad += " teleport"; }
                Debug.LogWarning($"[{Plugin.Name}] item {Localization.instance.Localize(i.m_shared.m_name)} at {i.m_gridPos}: {(bad.Length == 0 ? "ok" : bad)}");
            }
        }

        private static ItemDrop.ItemData Sample(Line line)
        {
            foreach (var kv in line.Where)
                if (kv.Key != null)
                    foreach (ItemDrop.ItemData i in kv.Key.GetInventory().GetAllItems())
                        if (i.m_shared.m_name + (i.m_shared.m_maxQuality > 1 ? "#" + i.m_quality : "") == line.Key) return i;
            return null;
        }

        /// <summary>Each slot shows the total in all the chests (not "50/50").</summary>
        private void Amounts()
        {
            List<InventoryElement> elements = Elements(_grid);
            if (elements == null) return;
            for (int i = 0; i < _shown.Count && i < elements.Count; i++)
            {
                InventoryElement e = elements[i];
                if (e == null || e.m_amount == null) continue;
                int total = _shown[i].Total;
                e.m_amount.enabled = total > 1 || _shown[i].MaxStack > 1;
                e.m_amount.text = total >= 10000 ? $"{total / 1000f:0.#}k" : total.ToString();
            }
        }

        private Line LineAt(Vector2i pos)
        {
            int i = pos.y * Width + pos.x;
            return i >= 0 && i < _shown.Count ? _shown[i] : null;
        }

        // ---- clicks -----------------------------------------------------------------------------------------------------------------------

        private void OnSelected(InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos, InventoryGrid.Modifier mod)
        {
            InventoryGui gui = InventoryGui.instance;
            Player p = Player.m_localPlayer;
            if (gui == null || p == null || _open == null) return;

            // Something from the ledger itself on your cursor, put back: nothing happens.
            if (DragGo(gui) != null && IsDisplay(DragInventory(gui))) { Drop(); return; }

            // Something on your cursor (picked up from your inventory): dropped on the ledger, it goes to its chest.
            if (DragGo(gui) != null)
            {
                ItemDrop.ItemData dragged = DragItem(gui);
                Inventory from = DragInventory(gui);
                if (dragged != null && from != null && (from == p.GetInventory() || from == _open.GetInventory()) && from.ContainsItem(dragged))
                {
                    if (p.IsItemEquiped(dragged)) p.UnequipItem(dragged, false);
                    var sent = new List<string>();
                    int want = Mathf.Clamp(DragAmount(gui), 1, dragged.m_stack);
                    int moved = Stores.Route(_open, p, dragged, from, want, sent);
                    Note(moved > 0 ? "Sent " + string.Join(", ", sent.Take(3).ToArray()) + (moved < want ? $". No room for the other {want - moved}." : ".")
                                   : "No chest around here has room for it.");
                    if (moved < want) p.Message(MessageHud.MessageType.Center, "No chest has room for " + Localization.instance.Localize(dragged.m_shared.m_name));
                }
                SetupDrag.Invoke(gui, new object[] { null, null, 1 });
                _nextScan = 0f;
                return;
            }

            Line line = LineAt(pos);
            if (line == null || item == null) return;
            if (mod == InventoryGrid.Modifier.Move) { Take(line, line.Total, null); return; }            // Ctrl-click: all that fits, straight in
            _carry = line;
            if (mod == InventoryGrid.Modifier.Split && line.Total > 1) SplitDialog.Invoke(gui, new object[] { item, _display }); // Shift-click: how many
            else SetupDrag.Invoke(gui, new object[] { item, _display, line.Total });                                            // click: all of it, on your cursor
        }

        private void OnRightClick(InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos)
        {
            Line line = LineAt(pos);
            if (line != null && DragGo(InventoryGui.instance) == null) Take(line, line.MaxStack, null);  // a stack, straight in
        }

        /// <summary>Something from the ledger is on your cursor.</summary>
        public bool Carrying => InventoryGui.instance != null && DragGo(InventoryGui.instance) != null && IsDisplay(DragInventory(InventoryGui.instance));

        /// <summary>
        /// What is on your cursor from the ledger, put down on a slot of some window: in your inventory, as much as fits comes out of the chests,
        /// into that slot first and then wherever there's room; anywhere else (another chest, a companion's bag) nothing moves.
        /// </summary>
        public void DropOnto(InventoryGrid grid, Vector2i pos)
        {
            InventoryGui gui = InventoryGui.instance;
            Player p = Player.m_localPlayer;
            int amount = gui != null ? DragAmount(gui) : 0;
            Line line = _carry == null ? null : _lines.FirstOrDefault(l => l.Key == _carry.Key) ?? _carry;
            Drop();
            if (line == null || p == null || amount <= 0) return;
            if (grid == null || grid.GetInventory() != p.GetInventory()) { Note("Take it into your own inventory: from there you can put it anywhere."); return; }
            Take(line, amount, pos);
        }

        /// <summary>Whatever is on your cursor from the ledger goes back (it never left the chests).</summary>
        public void Drop()
        {
            if (Carrying) SetupDrag.Invoke(InventoryGui.instance, new object[] { null, null, 1 });
            if (InventoryGui.instance != null) SplitDropping?.SetValue(InventoryGui.instance, false);   // (after a split: the slots' drop highlight off)
            _carry = null;
            _dirty = true;
        }

        private void Take(Line line, int want, Vector2i? at)
        {
            Player p = Player.m_localPlayer;
            Inventory yours = p.GetInventory();
            int moved = Stores.Take(line, want, yours, at, out int fromChests, out bool busy, out _);
            Note(moved > 0 ? $"Took {moved} {line.Name}{(fromChests > 1 ? $" from {fromChests} chests" : "")}." + (moved < want ? busy ? " (Some is in a chest someone has open.)" : $" No room for the other {want - moved}." : "")
                           : busy ? "That is in a chest someone has open." : "No room in your inventory.");
            if (moved < want && !busy) p.Message(MessageHud.MessageType.Center, moved > 0 ? $"Room for only {moved}" : "No room in your inventory");
            _nextScan = 0f;
        }

        private void Send()
        {
            var refused = new List<string>();
            List<string> sent = Stores.SendOut(_open, Player.m_localPlayer, refused);
            if (sent.Count == 0 && refused.Count == 0) return;
            Note((sent.Count > 0 ? "Sent " + string.Join(", ", sent.Take(3).ToArray()) + (sent.Count > 3 ? $" and {sent.Count - 3} more" : "") + ". " : "")
                 + (refused.Count > 0 ? "No chest has room for " + string.Join(", ", refused.Take(3).ToArray()) + ": back to you." : ""));
            if (refused.Count > 0) Player.m_localPlayer.Message(MessageHud.MessageType.Center, "No chest has room for " + string.Join(", ", refused.Take(2).ToArray()));
            _nextScan = 0f;
        }

        private void Note(string text)
        {
            _note = text;
            _noteUntil = Time.unscaledTime + 6f;
            Debug.Log($"[{Plugin.Name}] {text}");
        }

        // ---- the search in the window's title bar, and the notes under it (OnGUI) -----------------------------------------------------------------

        public void Draw()
        {
            if (_open == null || _panel == null || !_panel.activeSelf || _screen.width < 10f) return;
            Styles();
            float s = Mathf.Max(0.7f, Screen.height / 1080f) * Plugin.Scale.Value;
            float h = 30f * s;
            var bar = new Rect(_screen.x + 14f * s, _screen.y + 9f * s, _screen.width - 28f * s - 64f * s, h);   // in the window's title bar, clear of the chest count at the right
            float sortW = 92f * s, clearW = 56f * s;
            GUI.SetNextControlName("LedgerSearch");
            _field.fontSize = Mathf.RoundToInt(15f * s);
            _button.fontSize = Mathf.RoundToInt(13f * s);
            _dim.fontSize = Mathf.RoundToInt(15f * s);
            _search = GUI.TextField(new Rect(bar.x, bar.y, bar.width - sortW - clearW - 8f * s, h), _search ?? "", 60, _field);
            if (string.IsNullOrEmpty(_search) && GUI.GetNameOfFocusedControl() != "LedgerSearch")
                GUI.Label(new Rect(bar.x + 8f * s, bar.y, bar.width, h), "Search the chests around…", _dim);
            if (GUI.Button(new Rect(bar.xMax - sortW - clearW - 4f * s, bar.y, clearW, h), "Clear", _button)) { _search = ""; GUI.FocusControl(null); }
            if (GUI.Button(new Rect(bar.xMax - sortW, bar.y, sortW, h), OrderNames[_order], _button)) { _order = (_order + 1) % OrderNames.Length; _dirty = true; }
            if (Event.current.type == EventType.Repaint) _focused = GUI.GetNameOfFocusedControl() == "LedgerSearch";

            // under the window: where the item under the mouse is, or what just moved
            string under = null;
            if (Hovered?.Invoke(_grid, null) is InventoryElement e && e != null)
            {
                Line line = LineAt(e.Position);
                if (line != null)
                {
                    Vector3 from = _open.transform.position;
                    under = $"{line.Name}: {line.Total} in all. " + string.Join(", ", line.Where.Take(4).Select(kv => $"{kv.Value} in a chest {Vector3.Distance(kv.Key.transform.position, from):0} m away").ToArray())
                            + (line.Where.Count > 4 ? $" and {line.Where.Count - 4} more chests" : "");
                }
            }
            if (under == null && Time.unscaledTime < _noteUntil) under = _note;
            if (under == null) under = $"{_chests.Count} chests within {Plugin.Radius.Value:0} m   ·   Click: pick it all up   ·   Shift-click: how many   ·   Ctrl-click: all into your bag   ·   Right-click: a stack   ·   Drop things here to put them away";
            float below = Chips(s);
            _text.fontSize = Mathf.RoundToInt(14f * s);
            var info = new Rect(_screen.x, below + 4f * s, _screen.width, 40f * s);
            GUI.DrawTexture(new Rect(info.x - 4f, info.y - 2f, info.width + 8f, info.height + 4f), _backTex);
            GUI.Label(info, under, _text);
        }

        /// <summary>The category buttons under the window: one down at a time, each with how many things it holds. Where they end (y).</summary>
        private float Chips(float s)
        {
            _chip.fontSize = _chipOn.fontSize = Mathf.RoundToInt(13f * s);
            float h = 26f * s, gap = 4f * s, x = _screen.x, y = _screen.yMax + 6f * s, right = _screen.xMax;
            var kinds = new List<string> { Kinds.All };
            kinds.AddRange(Kinds.Order.Where(k => k == _kind || (_counts.TryGetValue(k, out int n) && n > 0)));
            float top = y;
            foreach (string k in kinds)
            {
                string label = $"{k}  {(_counts.TryGetValue(k, out int n) ? n : 0)}";
                float w = _chip.CalcSize(new GUIContent(label)).x + 14f * s;
                if (x + w > right && x > _screen.x) { x = _screen.x; y += h + gap; }
                var r = new Rect(x, y, w, h);
                if (Event.current.type == EventType.Repaint && x == _screen.x) GUI.DrawTexture(new Rect(_screen.x - 4f, y - 2f, _screen.width + 8f, h + 4f), _backTex);
                if (GUI.Button(r, label, k == _kind ? _chipOn : _chip) && k != _kind) { _kind = k; _dirty = true; _grid.ResetView(); }
                x += w + gap;
            }
            return y + h;
        }

        private void Styles()
        {
            if (_field != null) return;
            Texture2D Tex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }
            _fieldTex = Tex(new Color(0f, 0f, 0f, 0.6f));
            _buttonTex = Tex(new Color(0.24f, 0.19f, 0.13f, 1f));
            _buttonHover = Tex(new Color(0.38f, 0.30f, 0.19f, 1f));
            _backTex = Tex(new Color(0.08f, 0.07f, 0.05f, 0.82f));
            Color text = new Color(0.95f, 0.91f, 0.82f), gold = new Color(0.95f, 0.78f, 0.38f);
            _field = new GUIStyle(GUI.skin.textField) { padding = new RectOffset(8, 8, 5, 5), alignment = TextAnchor.MiddleLeft, normal = { background = _fieldTex, textColor = text }, focused = { background = _fieldTex, textColor = Color.white }, hover = { background = _fieldTex, textColor = text } };
            _button = new GUIStyle(GUI.skin.button) { normal = { background = _buttonTex, textColor = text }, hover = { background = _buttonHover, textColor = Color.white }, active = { background = _buttonHover, textColor = gold } };
            _text = new GUIStyle(GUI.skin.label) { wordWrap = true, normal = { textColor = text } };
            _chipOnTex = Tex(new Color(0.62f, 0.45f, 0.18f, 1f));
            _chip = new GUIStyle(_button) { padding = new RectOffset(6, 6, 2, 2) };
            _chipOn = new GUIStyle(_chip) { fontStyle = FontStyle.Bold, normal = { background = _chipOnTex, textColor = Color.white }, hover = { background = _chipOnTex, textColor = Color.white } };
            _dim = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(text.r, text.g, text.b, 0.45f) } };
        }
    }

    // After the game's inventory window updates (it shows the chest's own slots): the ledger in their place.
    [HarmonyPatch(typeof(InventoryGui), "Update")]
    internal static class InventoryGui_Update_Ledger
    {
        private static void Postfix(InventoryGui __instance) => Plugin.Instance?.Window.AfterGui(__instance);
    }

    // The ledger closes with the chest.
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    internal static class InventoryGui_Hide
    {
        private static void Postfix() => Plugin.Instance?.Window.Close();
    }

    // Something from the ledger on your cursor, put down on a slot of the game's windows (your inventory): ours to do (DropOnto).
    [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
    internal static class InventoryGui_OnSelectedItem_Ledger
    {
        private static bool Prefix(InventoryGrid grid, Vector2i pos)
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || !Ledger.IsDisplay(AccessTools.FieldRefAccess<InventoryGui, Inventory>(gui, "m_dragInventory"))) return true;
            Plugin.Instance?.Window.DropOnto(grid, pos);
            return false;
        }
    }

    // ... and on any other mod's slots (they put down with DropItem): the same, so a picture can never become an item.
    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
    internal static class InventoryGrid_DropItem_Ledger
    {
        private static bool Prefix(InventoryGrid __instance, Inventory fromInventory, Vector2i pos, ref bool __result)
        {
            if (!Ledger.IsDisplay(fromInventory) || Ledger.IsDisplay(__instance.GetInventory())) return true;
            __result = true;
            Plugin.Instance?.Window.DropOnto(__instance, pos);
            return false;
        }
    }

    // Put down outside the windows (which drops a thing on the ground): it just goes back.
    [HarmonyPatch(typeof(InventoryGui), "OnDropOutside")]
    internal static class InventoryGui_OnDropOutside_Ledger
    {
        private static bool Prefix()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || !Ledger.IsDisplay(AccessTools.FieldRefAccess<InventoryGui, Inventory>(gui, "m_dragInventory"))) return true;
            Plugin.Instance?.Window.Drop();
            return false;
        }
    }

    // Never out of the ledger's list by any other way (dropped, moved into another inventory).
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem))]
    internal static class Humanoid_DropItem_Ledger
    {
        private static bool Prefix(Inventory inventory, ref bool __result) { if (!Ledger.IsDisplay(inventory)) return true; __result = false; return false; }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) })]
    internal static class Inventory_MoveItemToThis_Ledger
    {
        private static bool Prefix(Inventory __instance, Inventory fromInventory, ref bool __result) { if (!Ledger.IsDisplay(fromInventory) || __instance == fromInventory) return true; __result = false; return false; }
    }
}
