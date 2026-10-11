using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using static DHack.Shared.Translations;

namespace FeedFromChests
{
    /// <summary>
    /// The station menu, in the game's own look (GameUi.cs): on the left what you can put in by hand (each usable item, how many you and
    /// the chests have, Add 1 / Fill, Fill station); on the right, for smelters, kilns, cooking stations and fermenters, what is inside and
    /// the station's auto-feed settings (saved on the station). Built again only when what it shows changes; counts update in place.
    /// </summary>
    public partial class Plugin
    {
        /// <summary>True while the menu is on screen.</summary>
        internal static bool MenuOpen => Instance != null && Instance._window != null;

        /// <summary>When the menu last closed: the E that closed it must not open it again (see Player_Interact).</summary>
        internal static float MenuClosedAt = -1f;

        internal void Log(string message) => Logger.LogWarning(message);

        private class Row
        {
            public ItemDrop Drop;
            public string Name, Display;
            public bool IsFuel;
            public int InInventory, InChests;
            public string Title, CountsText; // the text shown, built once per refresh (building strings on every redraw creates garbage)
        }

        private string _stationTitle = "", _subtitle = "";

        // automatic feeding (smelters, kilns, furnaces)
        private class AutoItem { public string Name, Display; public ItemDrop Drop; public bool IsFuel; }
        private bool _supportsAuto;
        private AutoFeed.Inside _inside = new AutoFeed.Inside();
        private List<string> _cookLines = new List<string>();   // a cooking station: what is on each slot
        private bool IsCooking => _station != null && _station.Component is CookingStation;
        private bool IsFermenter => _station != null && _station.Component is Fermenter;
        private LimitPlan _plan = new LimitPlan();
        private readonly List<KeyValuePair<string, bool>> _limitLines = new List<KeyValuePair<string, bool>>(); // per ticked item: why it is or is not fed
        private string _fermentLine = "";
        private readonly Dictionary<string, int> _autoStock = new Dictionary<string, int>(); // item -> how many the chests in auto-feed range hold
        private AutoSetting _auto = new AutoSetting();
        private List<AutoItem> _autoItems = new List<AutoItem>();

        private StationInfo _station;
        private List<Row> _rows = new List<Row>();
        private float _nextRefresh;
        private string _status = "";

        // ---- opening, closing, refreshing -------------------------------------------------------------

        private void OpenMenu(Player player, StationInfo info)
        {
            _station = info;
            _status = "";
            Mark("opened the menu");
            _stationTitle = info.Component is CookingStation ? Localization.instance.Localize(info.Title) : T("feedfromchests_add_to", Localization.instance.Localize(info.Title));
            _subtitle = T("feedfromchests_subtitle", _radius.Value.ToString("0"));
            _supportsAuto = _stationAuto.Value && AutoFeed.Supported(info);
            _autoItems = BuildAutoItems(info);
            _auto = _supportsAuto ? AutoFeed.Read(info.Component) : new AutoSetting();
            _plan = Limits.For(info);
            _autoEverSet = _supportsAuto && (_auto.On || _auto.Allowed.Count > 0);
            RefreshRows(player);
            Logger.LogInfo($"Menu for '{info.Title}': {_rows.Count} item(s) available, {_station.Inputs.Count} input(s), fuel={(_station.Fuel != null ? "yes" : "no")}, auto-feed={(_supportsAuto ? (_auto.On ? "on" : "off") : "n/a")}");
            if (_rows.Count == 0 && !_supportsAuto)
            {
                player.Message(MessageHud.MessageType.Center, T("feedfromchests_nothing_to_add"));
                _station = null;
                return;
            }
            if (!GameUi.Ready()) { _station = null; return; }   // (it logs why: a game update moved the inventory's parts)
            GameUi.IsOpen = () => MenuOpen;                    // the game stops taking input while it is open
            GameUi_EscapeCloses.Close = CloseFromEscape;
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            _openedAt = Time.unscaledTime;
            BuildWindow(true);
        }

        /// <summary>Closes the menu, as Escape does (static: Claude Tools' "shoot close" can call it before a screenshot).</summary>
        internal static void CloseFromEscape() => Instance?.CloseMenu();

        private void CloseMenu()
        {
            bool wasOpen = _station != null || _window != null;
            if (_window != null) Destroy(_window.gameObject);
            _window = null; _addList = null; _autoList = null; _shown = "";
            _live.Clear();
            _station = null;
            if (!wasOpen) return;
            MenuClosedAt = Time.unscaledTime;
            UITooltip.HideTooltip();
        }

        private void UpdateMenu(Player player)
        {
            bool useAgain = Time.unscaledTime > _openedAt + 0.2f && ZInput.GetButtonDown("Use"); // E closes it again, as it does a chest
            // (_window null: the game rebuilt its interface, a world change)
            if (_window == null || player == null || player.IsDead() || _station == null || !_station.Alive || Menu.IsVisible() || useAgain ||
                (_station.Position - player.transform.position).sqrMagnitude > 12f * 12f)
            {
                CloseMenu();
                return;
            }
            if (Time.unscaledTime >= _nextRefresh) RefreshRows(player);
            if (Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 0.2f;
                if (Signature() != _shown) BuildWindow(false);
                else UpdateLive();
            }
        }

        /// <summary>Everything this station takes that you or the chests have, with counts.</summary>
        private void RefreshRows(Player player)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            RefreshRowsCore(player);
            if (clock.ElapsedMilliseconds >= 12) Logger.LogInfo($"Slow menu refresh: {clock.ElapsedMilliseconds} ms");
        }

        /// <summary>Every item this station can use (whether or not any is in stock), lesser to greater, fuel last.</summary>
        private static List<AutoItem> BuildAutoItems(StationInfo info)
        {
            var seen = new HashSet<string>();
            var items = new List<AutoItem>();
            foreach (ItemDrop drop in info.Inputs.Concat(info.Fuel != null ? new[] { info.Fuel } : new ItemDrop[0]))
            {
                string name = drop.m_itemData.m_shared.m_name;
                if (!seen.Add(name)) continue;
                items.Add(new AutoItem { Name = name, Display = Localization.instance.Localize(name), Drop = drop, IsFuel = info.Fuel != null && drop == info.Fuel });
            }
            return items.OrderBy(i => i.IsFuel).ThenBy(i => Tiers.Rank(i.Drop.m_itemData.m_shared)).ToList();
        }

        /// <summary>Turn automatic feeding on or off. The first time, tick sensible items: only the plain wood for a kiln, everything otherwise.</summary>
        private void ToggleAuto()
        {
            if (_station == null || !_station.Alive || !_supportsAuto) return;
            AutoSetting setting = AutoFeed.Read(_station.Component);
            setting.On = !setting.On;
            if (setting.On && setting.Allowed.Count == 0)
            {
                bool onlyWood = _autoItems.Count > 0 && _autoItems.All(i => i.IsFuel || Tiers.IsWood(i.Drop.m_itemData.m_shared));
                foreach (AutoItem item in _autoItems)
                {
                    if (onlyWood && !item.IsFuel && item != _autoItems.First(i => !i.IsFuel)) continue; // a kiln: just the first (plain) wood
                    setting.Allowed.Add(item.Name);
                }
            }
            if (setting.On && !_autoEverSet)        // a sensible start, for this kind of station
            {
                setting.Output = true;
                setting.Target = _plan.DefaultTarget;
                setting.Reserve = _plan.DefaultReserve;
                setting.FuelReserve = _plan.DefaultFuelReserve;
            }
            AutoFeed.Write(_station.Component, setting);
            _auto = setting;
            _status = setting.On ? T("feedfromchests_auto_is_on") : T("feedfromchests_auto_is_off");
        }

        private bool _autoEverSet; // this station already has saved settings (so we do not overwrite what someone chose)

        private void AdjustReserve(int delta)
        {
            if (_station == null || !_station.Alive || !_supportsAuto) return;
            AutoSetting setting = AutoFeed.Read(_station.Component);
            setting.Reserve = Mathf.Clamp(setting.Reserve + delta, 0, 9999);
            AutoFeed.Write(_station.Component, setting);
            _auto = setting;
        }

        private void AdjustTarget(int delta)
        {
            if (_station == null || !_station.Alive || !_supportsAuto) return;
            AutoSetting setting = AutoFeed.Read(_station.Component);
            setting.Target = Mathf.Clamp(setting.Target + delta, 0, 99999);
            AutoFeed.Write(_station.Component, setting);
            _auto = setting;
        }

        private void AdjustFuelReserve(int delta)
        {
            if (_station == null || !_station.Alive || !_supportsAuto) return;
            AutoSetting setting = AutoFeed.Read(_station.Component);
            setting.FuelReserve = Mathf.Clamp(setting.FuelReserve + delta, 0, 9999);
            AutoFeed.Write(_station.Component, setting);
            _auto = setting;
        }

        private void ToggleOutput()
        {
            if (_station == null || !_station.Alive || !_supportsAuto) return;
            AutoSetting setting = AutoFeed.Read(_station.Component);
            setting.Output = !setting.Output;
            AutoFeed.Write(_station.Component, setting);
            _auto = setting;
            _status = setting.Output ? T("feedfromchests_output_chests") : T("feedfromchests_output_ground");
        }

        private void ToggleTakeOff()
        {
            if (_station == null || !_station.Alive || !_supportsAuto) return;
            AutoSetting setting = AutoFeed.Read(_station.Component);
            setting.TakeOff = !setting.TakeOff;
            AutoFeed.Write(_station.Component, setting);
            _auto = setting;
            _status = setting.TakeOff ? T("feedfromchests_takeoff_on") : T("feedfromchests_takeoff_off");
        }

        private void ToggleAutoItem(string name)
        {
            if (_station == null || !_station.Alive || !_supportsAuto) return;
            AutoSetting setting = AutoFeed.Read(_station.Component);
            if (!setting.Allowed.Remove(name)) setting.Allowed.Add(name);
            AutoFeed.Write(_station.Component, setting);
            _auto = setting;
        }

        private void RefreshRowsCore(Player player)
        {
            _nextRefresh = Time.unscaledTime + 0.5f;
            if (_station == null || player == null) return;
            if (_supportsAuto && _station != null && _station.Alive)
            {
                _auto = AutoFeed.Read(_station.Component); // others may have changed it
                if (_station.Component is Smelter smelter) _inside = AutoFeed.Look(smelter);
                else if (_station.Component is CookingStation cooking) _cookLines = Cooking.Lines(cooking);
                else if (_station.Component is Fermenter fermenter) _fermentLine = Ferment.Line(fermenter);
                _autoStock.Clear();
                List<Container> autoChests = Chests.Near(_station.Position, _autoRadius.Value);
                foreach (AutoItem item in _autoItems) _autoStock[item.Name] = Chests.Count(autoChests, item.Name);
                _limitLines.Clear();
                if (_auto.On)
                    foreach (AutoItem item in _autoItems)
                    {
                        if (!_auto.Allowed.Contains(item.Name)) continue;
                        bool ok = Limits.MayFeed(_station, _auto, _plan, item.Drop, item.IsFuel, _autoStock[item.Name], _outputRadius.Value, out string line);
                        _limitLines.Add(new KeyValuePair<string, bool>(line, ok));
                    }
            }
            List<Container> chests = Chests.Near(_station.Position, _radius.Value);
            Inventory inventory = player.GetInventory();

            var rows = new List<Row>();
            var seen = new HashSet<string>();
            foreach (ItemDrop drop in _station.Inputs.Concat(_station.Fuel != null ? new[] { _station.Fuel } : new ItemDrop[0]))
            {
                string name = drop.m_itemData.m_shared.m_name;
                if (!seen.Add(name)) continue;

                var row = new Row
                {
                    Drop = drop, Name = name, Display = Localization.instance.Localize(name),
                    IsFuel = _station.Fuel != null && drop == _station.Fuel,
                    InInventory = inventory.CountItems(name), InChests = Chests.Count(chests, name),
                };
                row.Title = row.Display + (row.IsFuel ? "  <size=80%><color=#bdb7a9>" + T("feedfromchests_fuel_tag") + "</color></size>" : "");
                row.CountsText = T("feedfromchests_in_inventory", row.InInventory) + "\n" + T("feedfromchests_in_chests", row.InChests);
                if (row.InInventory + row.InChests > 0) rows.Add(row);
            }
            // Things to add first, fuel after; each group from the lesser item to the greater (wood, fine wood, core wood...).
            _rows = rows.OrderBy(r => r.IsFuel).ThenBy(r => Tiers.Rank(r.Drop.m_itemData.m_shared))
                        .ThenBy(r => r.Display, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Put in one of an item.</summary>
        private void AddOneFromMenu(Player player, Row row)
        {
            if (_station == null || !_station.Alive || _filling) return;

            Mark($"Add 1 {row.Display}");
            bool ok = AddOne(player, _station, row.Drop, row.IsFuel);
            _status = ok ? T("feedfromchests_added_one", row.Display) : T("feedfromchests_couldnt_add", row.Display);
            RefreshRows(player);
            if (_rows.Count == 0) CloseMenu();
        }

        /// <summary>Fill one item (fills until the station is full or you run out).</summary>
        private void FillFromMenu(Player player, Row row)
        {
            if (_station == null || !_station.Alive || _filling) return;
            Mark($"Fill {row.Display}");
            StartCoroutine(FillRoutine(player, _station, new List<Row> { row }));
        }

        /// <summary>Fill everything the station takes: the fuel first (so it can run), then each item in the list.</summary>
        private void FillStation(Player player)
        {
            if (_station == null || !_station.Alive || _filling) return;
            Mark("Fill station");
            StartCoroutine(FillRoutine(player, _station, _rows.OrderByDescending(r => r.IsFuel).ToList()));
        }

        // ---- the window ----------------------------------------------------------------------------------

        private const string WindowName = "FeedFromChestsMenu";
        private const float Pad = 30f, ColW = 480f, ColGap = 20f, Below = 90f;   // (Below: where the columns start, under the title and braid)

        private RectTransform _window, _addList, _autoList;
        private float _openedAt, _nextCheck;
        private string _shown = "";

        /// <summary>A text that changes while the menu is open (counts, cooking progress, the status line): updated in place, no rebuild.</summary>
        private class LiveText { public TMP_Text Label; public Func<string> Text; public bool Fit; }
        private readonly List<LiveText> _live = new List<LiveText>();

        /// <summary>Keep <paramref name="label"/> showing <paramref name="text"/>. With fit, a text that no longer fits its height builds the window again.</summary>
        private TMP_Text Live(TMP_Text label, Func<string> text, bool fit = false)
        {
            _live.Add(new LiveText { Label = label, Text = text, Fit = fit });
            return label;
        }

        private void UpdateLive()
        {
            foreach (LiveText l in _live)
            {
                if (l.Label == null) continue;
                string text = l.Text() ?? "";
                if (l.Label.text == text) continue;
                l.Label.text = text;
                if (!l.Fit) continue;
                var r = (RectTransform)l.Label.transform;
                if (Mathf.Abs(Mathf.Ceil(l.Label.GetPreferredValues(text, r.sizeDelta.x, 0f).y) - r.sizeDelta.y) > 1f) _shown = ""; // (wraps differently now)
            }
        }

        /// <summary>A small button whose word may be longer in another language: the word shrinks to fit rather than run over the edge.</summary>
        private static UnityEngine.UI.Button Squeeze(UnityEngine.UI.Button b)
        {
            TMP_Text label = GameUi.Label(b);
            if (label != null) { label.fontSizeMax = label.fontSize; label.fontSizeMin = 11f; label.enableAutoSizing = true; }
            return b;
        }

        /// <summary>Sizes a wrapping text to the height it needs and returns that height (so what comes under it starts below it).</summary>
        private static float Fit(TMP_Text t, float width)
        {
            float h = Mathf.Ceil(t.GetPreferredValues(t.text, width, 0f).y);
            ((RectTransform)t.transform).sizeDelta = new Vector2(width, h);
            return h;
        }

        private bool FuelSwitchedOff => _supportsAuto && _auto.On && _station != null && _station.Fuel != null &&
                                        !_auto.Allowed.Contains(_station.Fuel.m_itemData.m_shared.m_name);

        private Row RowNamed(string name) => _rows.FirstOrDefault(r => r.Name == name);
        private static int Total(Row row) => row != null ? row.InInventory + row.InChests : 0;
        private static string Shown(int value, bool offWhenZero) => value == 0 && offWhenZero ? Localization.instance.Localize("$hud_off") : value.ToString();

        /// <summary>A click's action, run in Update with the local player (never from the click itself).</summary>
        private void Later(Action<Player> act) => _pending = () => { Player p = Player.m_localPlayer; if (p != null) act(p); };

        /// <summary>What decides the window's layout (the live texts are left out: they update in place), so it is built again only when this changes.</summary>
        private string Signature()
        {
            if (_station == null) return "";
            var sb = new System.Text.StringBuilder();
            sb.Append(_station.Component != null ? _station.Component.GetInstanceID() : 0).Append('|').Append(_supportsAuto).Append(_filling)
              .Append(_takeOffCooked.Value).Append('|').Append(_autoRadius.Value).Append('|').Append(_subtitle);
            foreach (Row row in _rows) sb.Append("|r").Append(row.Name);
            if (_supportsAuto)
            {
                sb.Append("|a").Append(_auto.On).Append(_auto.Output).Append(_auto.TakeOff).Append(FuelSwitchedOff).Append(':')
                  .Append(string.Join(",", _auto.Allowed.OrderBy(n => n).ToArray()));
                foreach (KeyValuePair<string, int> q in _inside.Queue) sb.Append("|q").Append(q.Key);
                sb.Append("|c").Append(_cookLines.Count);
                foreach (KeyValuePair<string, bool> line in _limitLines) sb.Append("|l").Append(line.Value);
            }
            return sb.ToString();
        }

        private void BuildWindow(bool fresh)
        {
            if (!GameUi.Ready() || _station == null) { CloseMenu(); return; }
            float addScrolled = !fresh && _addList != null ? _addList.anchoredPosition.y : 0f;
            float autoScrolled = !fresh && _autoList != null ? _autoList.anchoredPosition.y : 0f;
            if (_window != null) Destroy(_window.gameObject);
            _addList = _autoList = null;
            UITooltip.HideTooltip();
            _live.Clear();
            _shown = Signature();

            // two columns for a station with auto-feed (by hand | auto-feed), one for the rest (fires, torches...)
            float w = _supportsAuto ? Pad * 2f + ColW * 2f + ColGap : Pad * 2f + ColW, h = _supportsAuto ? 740f : 600f;
            _window = GameUi.Window(WindowName, new Vector2(w, h));
            Transform win = _window;
            TMP_Text title = GameUi.Label(win, _stationTitle, new Vector2(Pad, -20f), new Vector2(w - Pad * 2f - 130f, 60f), 34f, GameUi.Warm, GameUi.Norse);   // (60: "..." hides a taller Chinese line)
            title.enableWordWrapping = false;
            title.overflowMode = TextOverflowModes.Ellipsis;
            GameUi.Button(win, Localization.instance.Localize("$menu_close"), new Vector2(w - Pad - 110f, -24f), new Vector2(110f, 40f), () => _pending = CloseMenu);
            GameUi.Braid(win, new Vector2(0f, -76f), w - Pad * 2f);

            DrawAddColumn(win, Pad, h);
            if (_supportsAuto) DrawAutoColumn(win, Pad + ColW + ColGap, h);

            if (_addList != null) _addList.anchoredPosition = new Vector2(0f, Mathf.Clamp(addScrolled, 0f, Mathf.Max(0f, _addList.sizeDelta.y)));
            if (_autoList != null) _autoList.anchoredPosition = new Vector2(0f, Mathf.Clamp(autoScrolled, 0f, Mathf.Max(0f, _autoList.sizeDelta.y)));
        }

        /// <summary>The left side: what you can put in by hand (your inventory first, then the chests), and the status line with Fill station.</summary>
        private void DrawAddColumn(Transform win, float x, float h)
        {
            float y = Below;
            y += Fit(GameUi.Label(win, _subtitle, new Vector2(x, -y), new Vector2(ColW, 40f), 16f, GameUi.Dim), ColW) + 8f;
            RectTransform box = GameUi.Box(win, new Vector2(x - 6f, -y), new Vector2(ColW + 12f, h - Pad - 62f - y));
            _addList = GameUi.Scroll(box, 8f);
            float cw = _addList.sizeDelta.x;

            if (_rows.Count == 0)
            {
                TMP_Text none = GameUi.Label(_addList, T("feedfromchests_nothing_by_hand"),
                    new Vector2(8f, -8f), new Vector2(cw - 16f, 40f), 17f, GameUi.Dim);
                _addList.sizeDelta = new Vector2(cw, Fit(none, cw - 16f) + 16f);
            }
            else
            {
                // one line per item: the inventory's own cell (the game's tooltip; the number is how many you and the chests have), its name
                // and counts, Add 1 and Fill
                float space = GameUi.CellSpace, bw = 84f, ry = 4f, tx = 4f + 64f + 12f, textW = cw - tx - bw * 2f - 14f;
                foreach (Row row in _rows)
                {
                    Row r = row;
                    string name = row.Name;
                    InventoryElement cell = GameUi.Cell(_addList, new Vector2(4f, -ry), row.Drop.m_itemData, Total(row).ToString(), false, null);
                    Live(cell.m_amount, () => Total(RowNamed(name)).ToString());
                    TMP_Text t = GameUi.Label(_addList, row.Title, new Vector2(tx, -ry - 4f), new Vector2(textW, 34f), 19f, GameUi.Text);
                    t.enableWordWrapping = false;
                    t.overflowMode = TextOverflowModes.Ellipsis;
                    Live(GameUi.Label(_addList, row.CountsText, new Vector2(tx, -ry - 28f), new Vector2(textW, 38f), 14f, GameUi.Dim), () => RowNamed(name)?.CountsText ?? "");
                    Squeeze(GameUi.Button(_addList, T("feedfromchests_add_one"), new Vector2(cw - bw * 2f - 6f, -ry - 12f), new Vector2(bw, 40f), () => Later(p => AddOneFromMenu(p, r)), !_filling, 17f));
                    var fill = Squeeze(GameUi.Button(_addList, T("feedfromchests_fill"), new Vector2(cw - bw, -ry - 12f), new Vector2(bw, 40f), () => Later(p => FillFromMenu(p, r)), !_filling, 17f));
                    if (!_filling) GameUi.Label(fill).color = GameUi.Gold;
                    ry += space;
                }
                _addList.sizeDelta = new Vector2(cw, ry - space + 64f + 4f);   // (exactly: a few pixels more and a scrollbar shows for nothing)
            }

            // the bottom line: what just happened, and Fill station (the fuel first, then each item in the list)
            float by = h - Pad - 50f;
            Live(GameUi.Label(win, _status, new Vector2(x, -by), new Vector2(ColW - 196f, 50f), 15f, GameUi.Dim, null, TextAlignmentOptions.MidlineLeft), () => _status);
            bool any = !_filling && _rows.Count > 0;
            var all = GameUi.Button(win, T("feedfromchests_fill_station"), new Vector2(x + ColW - 184f, -by), new Vector2(184f, 48f), () => Later(FillStation), any, 19f);
            if (any) GameUi.Label(all).color = GameUi.Gold;
        }

        /// <summary>
        /// The right side, for stations that can feed themselves: what is inside, the auto-feed switch, the limits that make sense for this
        /// station, its other switches, the items it may use, and why each is or isn't being fed. Every change is saved on the station.
        /// </summary>
        private void DrawAutoColumn(Transform win, float x, float h)
        {
            RectTransform box = GameUi.Box(win, new Vector2(x - 6f, -Below), new Vector2(ColW + 12f, h - Below - Pad));
            _autoList = GameUi.Scroll(box, 8f);
            RectTransform c = _autoList;
            float cw = c.sizeDelta.x - 8f, y = 6f;   // (usable width, from x = 4)

            TMP_Text Line(string text, float size, Color color, float gap, TMP_FontAsset font = null)
            {
                TMP_Text t = GameUi.Label(c, text, new Vector2(4f, -y), new Vector2(cw, 30f), size, color, font);
                y += Fit(t, cw) + gap;
                return t;
            }
            void Heading(string text) => Line(text, 20f, GameUi.Warm, 6f, GameUi.Serif);
            void OnOff(string text, bool on, Action act)
            {
                GameUi.Toggle(c, text, on, new Vector2(4f, -y), new Vector2(cw, 40f), () => _pending = act, 17f);
                y += 46f;
            }
            void Number(string label, Func<int> value, bool offWhenZero, string help, Action<int> adjust)
            {
                Line(label, 17f, GameUi.Text, 6f);
                TMP_Text v = GameUi.Stepper(c, new Vector2(4f, -y), 34f, Shown(value(), offWhenZero), new[] { 10, 1 }, d => _pending = () => adjust(d));
                Live(v, () => Shown(value(), offWhenZero));
                y += 40f;
                if (!string.IsNullOrEmpty(help)) Line(help, 15f, GameUi.Dim, 12f);
                else y += 8f;
            }

            // what is in it right now
            if (IsFermenter)
            {
                Heading(T("feedfromchests_in_barrel"));
                Live(Line(_fermentLine, 16f, GameUi.Text, 10f), () => _fermentLine, true);
            }
            else if (IsCooking)
            {
                Heading(T("feedfromchests_on_station"));
                for (int i = 0; i < _cookLines.Count; i++)
                {
                    int k = i;
                    Live(Line(_cookLines[i], 16f, GameUi.Text, 2f), () => k < _cookLines.Count ? _cookLines[k] : "", true);
                }
                y += 8f;
            }
            else
            {
                Heading(T("feedfromchests_inside"));
                if (_inside.Queue.Count == 0) Line(T("feedfromchests_nothing_loaded_menu"), 16f, GameUi.Dim, 4f);
                else
                {
                    // each ore waiting: its icon and "4 Copper ore", as many on a line as fit
                    float qx = 4f;
                    foreach (KeyValuePair<string, int> entry in _inside.Queue)
                    {
                        string key = entry.Key;
                        string text = QueueText(key);
                        TMP_Text t = GameUi.Label(c, text, Vector2.zero, new Vector2(cw, 28f), 16f, GameUi.Text);
                        t.enableWordWrapping = false;
                        Live(t, () => QueueText(key));
                        float tw = t.GetPreferredValues(text, cw, 0f).x + 20f;   // (room for the count to grow)
                        if (qx > 4f && qx + 32f + tw > cw) { qx = 4f; y += 34f; }
                        GameUi.Icon(c, new Vector2(qx, -y), new Vector2(28f, 28f), AutoFeed.IconOf(key));
                        var tr = (RectTransform)t.transform;
                        tr.anchoredPosition = new Vector2(qx + 32f, -y - 3f);
                        tr.sizeDelta = new Vector2(tw, 28f);
                        qx += 32f + tw + 6f;
                    }
                    y += 34f;
                }
                Live(Line(InsideStatus(), 15f, GameUi.Dim, 6f), InsideStatus, true);
            }
            GameUi.Braid(c, new Vector2(0f, -y - 8f), cw - 40f);
            y += 20f;

            // auto-feed itself
            OnOff(T("feedfromchests_auto_toggle"), _auto.On, ToggleAuto);
            Line(_auto.On
                ? T("feedfromchests_auto_on_help", _autoRadius.Value.ToString("0"))
                : T("feedfromchests_auto_off_help"), 15f, GameUi.Dim, 12f);

            if (_plan.Target) Number(_plan.TargetLabel, () => _auto.Target, true, _plan.TargetHelp, AdjustTarget);
            if (_plan.Reserve) Number(_plan.ReserveLabel, () => _auto.Reserve, false, _plan.ReserveHelp, AdjustReserve);
            if (_plan.FuelReserve) Number(_plan.FuelLabel, () => _auto.FuelReserve, false, _plan.FuelHelp, AdjustFuelReserve);

            if (IsFermenter)
            {
                OnOff(T("feedfromchests_taps_toggle"), _auto.TakeOff, ToggleTakeOff);
                Line(T("feedfromchests_taps_help"), 15f, GameUi.Dim, 12f);
            }
            if (IsCooking)
            {
                OnOff(T("feedfromchests_takeoff_on"), _auto.TakeOff, ToggleTakeOff);
                Line(_takeOffCooked.Value
                    ? T("feedfromchests_takeoff_help")
                    : T("feedfromchests_takeoff_config_off"), 15f, GameUi.Dim, 12f);
            }
            OnOff(IsCooking ? T("feedfromchests_output_food") : IsFermenter ? T("feedfromchests_output_mead") : T("feedfromchests_output_chests"), _auto.Output, ToggleOutput);
            Line(IsFermenter
                ? T("feedfromchests_output_mead_help")
                : IsCooking
                ? T("feedfromchests_output_food_help")
                : T("feedfromchests_output_help"), 15f, GameUi.Dim, 12f);

            // the items it may use: one switch each, two to a line
            Line(T("feedfromchests_items_may_use"), 17f, GameUi.Text, 6f);
            float iw = (cw - 6f) / 2f;
            for (int i = 0; i < _autoItems.Count; i++)
            {
                AutoItem item = _autoItems[i];
                string name = item.Name;
                GameUi.Toggle(c, item.Display + (item.IsFuel ? " " + T("feedfromchests_fuel_tag") : ""), _auto.Allowed.Contains(name), new Vector2(4f + i % 2 * (iw + 6f), -y - i / 2 * 44f),
                    new Vector2(iw, 40f), () => _pending = () => ToggleAutoItem(name), 15f, item.Drop.m_itemData.GetIcon());
            }
            y += (_autoItems.Count + 1) / 2 * 44f + 6f;

            // for each item switched on: how many the chests hold against the limits, so it is clear why it is or is not being fed; and a
            // warning if the fuel is off (the smelter would run out and stop)
            if (FuelSwitchedOff)
                Line(T("feedfromchests_fuel_off_warn", Localization.instance.Localize(_station.Fuel.m_itemData.m_shared.m_name)), 15f, GameUi.Warn, 4f);
            for (int i = 0; i < _limitLines.Count; i++)
            {
                int k = i;
                Live(Line(_limitLines[i].Key, 15f, _limitLines[i].Value ? GameUi.Good : GameUi.Gold, 4f), () => k < _limitLines.Count ? _limitLines[k].Key : "", true);
            }
            c.sizeDelta = new Vector2(c.sizeDelta.x, y + 2f);
        }

        private string QueueText(string prefab)
        {
            foreach (KeyValuePair<string, int> q in _inside.Queue)
                if (q.Key == prefab) return T("feedfromchests_amount", q.Value, AutoFeed.NameOf(q.Key));
            return "";
        }

        /// <summary>"Loaded 4/10 · Fuel 8/20 · Ready to collect: 3 Copper".</summary>
        private string InsideStatus()
        {
            AutoFeed.Inside i = _inside;
            string status = T("feedfromchests_loaded", i.QueueSize, i.MaxOre);
            if (i.MaxFuel > 0) status += "   ·   " + T("feedfromchests_fuel_level", Mathf.Floor(i.Fuel).ToString("0"), i.MaxFuel);
            if (i.Ready > 0) status += "   ·   " + T("feedfromchests_ready_collect", i.Ready, i.ReadyName);
            return status;
        }
    }
}
