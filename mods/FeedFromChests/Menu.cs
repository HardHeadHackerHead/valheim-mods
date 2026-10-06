using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FeedFromChests
{
    /// <summary>The window for choosing what to put into a station: each usable item, how many you have, and Add 1 / Add all buttons.</summary>
    public partial class Plugin
    {
        internal static bool MenuOpen;

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
        private Vector2 _scroll;
        private Rect _rect;
        private bool _placed;
        private string _status = "";

        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private GUIStyle _title, _text, _dim, _button, _buttonOn, _good, _warn, _bad;

        private static readonly Color Gold = new Color(0.95f, 0.78f, 0.35f);
        private static readonly Color Dim = new Color(0.68f, 0.66f, 0.62f);

        // ---- opening, closing, refreshing -------------------------------------------------------------

        private void OpenMenu(Player player, StationInfo info)
        {
            _station = info;
            _status = "";
            Mark("opened the menu");
            _stationTitle = (info.Component is CookingStation ? "" : "Add to ") + Localization.instance.Localize(info.Title);
            _subtitle = $"Uses your inventory first, then chests within {_radius.Value:0} m. The station's own limits still apply.";
            _supportsAuto = _stationAuto.Value && AutoFeed.Supported(info);
            _autoItems = BuildAutoItems(info);
            _auto = _supportsAuto ? AutoFeed.Read(info.Component) : new AutoSetting();
            _plan = Limits.For(info);
            _autoEverSet = _supportsAuto && (_auto.On || _auto.Allowed.Count > 0);
            RefreshRows(player);
            Logger.LogInfo($"Menu for '{info.Title}': {_rows.Count} item(s) available, {_station.Inputs.Count} input(s), fuel={(_station.Fuel != null ? "yes" : "no")}, auto-feed={(_supportsAuto ? (_auto.On ? "on" : "off") : "n/a")}");
            if (_rows.Count == 0 && !_supportsAuto)
            {
                player.Message(MessageHud.MessageType.Center, "Nothing to add: no item this takes in your inventory or nearby chests");
                _station = null;
                return;
            }
            MenuOpen = true;
        }

        internal static void CloseFromEscape() => Instance?.CloseMenu();

        private void CloseMenu()
        {
            MenuOpen = false;
            _station = null;
            if (!_filling) Feed.Reserved = null; // never leave a stale "reserved" count behind: it would make stock look lower than it is
        }

        private void UpdateMenu(Player player)
        {
            if (_station == null || !_station.Alive || Menu.IsVisible() ||
                (_station.Position - player.transform.position).sqrMagnitude > 12f * 12f)
            {
                CloseMenu();
                return;
            }
            if (Time.unscaledTime >= _nextRefresh) RefreshRows(player);
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
            _status = setting.On ? "Auto-feed is on" : "Auto-feed is off";
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
            _status = setting.Output ? "What it makes goes into chests" : "What it makes drops on the ground";
        }

        private void ToggleTakeOff()
        {
            if (_station == null || !_station.Alive || !_supportsAuto) return;
            AutoSetting setting = AutoFeed.Read(_station.Component);
            setting.TakeOff = !setting.TakeOff;
            AutoFeed.Write(_station.Component, setting);
            _auto = setting;
            _status = setting.TakeOff ? "Done food comes off by itself" : "Done food stays on (and can burn)";
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
                row.Title = row.Display + (row.IsFuel ? "   (fuel)" : "");
                row.CountsText = $"In your inventory: {row.InInventory}     In chests: {row.InChests}";
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
            _status = ok ? $"Added 1 {row.Display}" : $"Couldn't add {row.Display} (full, or not accepted right now)";
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

        // ---- drawing -----------------------------------------------------------------------------------

        private void OnGUI()
        {
            if (!MenuOpen || _station == null) return;
            FreeTheMouse();
            EnsureStyles();

            Matrix4x4 previous = GUI.matrix;
            float s = Mathf.Max(0.8f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float sw = Screen.width / s, sh = Screen.height / s;

            float w = Mathf.Min(520f, sw - 40f), h = Mathf.Min(_supportsAuto ? 900f : 520f, sh - 60f);
            if (!_placed) { _rect = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h); _placed = true; }
            _rect.width = w; _rect.height = h;

            _rect = GUI.Window(8851, _rect, DrawContents, GUIContent.none, GUIStyle.none);
            _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, sw - w));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, sh - h));
            GUI.matrix = previous;
        }

        private void DrawContents(int id)
        {
            float w = _rect.width, h = _rect.height;
            Rounded(new Rect(0, 0, w, h), new Color(0.07f, 0.06f, 0.05f, 0.98f), 9f);
            Outline(new Rect(0, 0, w, h), new Color(0.62f, 0.47f, 0.22f, 1f), 9f);

            GUILayout.BeginArea(new Rect(16f, 12f, w - 32f, h - 24f));

            GUILayout.BeginHorizontal();
            GUILayout.Label(_stationTitle, _title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", _button, GUILayout.Width(80), GUILayout.Height(28))) CloseMenu();
            GUILayout.EndHorizontal();
            GUILayout.Label(_subtitle, _dim);
            GUILayout.Space(6);

            if (_supportsAuto) { DrawInsidePanel(); DrawAutoPanel(); }

            Player player = Player.m_localPlayer;
            if (_rows.Count == 0) GUILayout.Label("Nothing to add by hand right now: none of what this takes is in your inventory or the nearby chests.", _dim);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            foreach (Row row in _rows)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(40));

                Rect icon = GUILayoutUtility.GetRect(34f, 34f, GUILayout.Width(34), GUILayout.Height(34));
                DrawIcon(icon, row.Drop.m_itemData.GetIcon());

                GUILayout.BeginVertical();
                GUILayout.Label(row.Title, _text);
                GUILayout.Label(row.CountsText, _dim);
                GUILayout.EndVertical();

                GUILayout.FlexibleSpace();
                Row r = row;
                if (GUILayout.Button("Add 1", _button, GUILayout.Width(70), GUILayout.Height(30))) _pending = () => AddOneFromMenu(player, r);
                if (GUILayout.Button("Fill", _buttonOn, GUILayout.Width(70), GUILayout.Height(30))) _pending = () => FillFromMenu(player, r);
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label(_status, _dim);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Fill station", _buttonOn, GUILayout.Width(120), GUILayout.Height(30))) _pending = () => FillStation(player);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, w, 40));
        }

        /// <summary>What is loaded into the station right now: the ore waiting to be processed, the fuel, and what is ready to collect.</summary>
        private void DrawInsidePanel()
        {
            if (IsFermenter)
            {
                GUILayout.Label("In the barrel", _text);
                GUILayout.Label(_fermentLine, _dim);
                GUILayout.Space(8);
                return;
            }
            if (IsCooking)
            {
                GUILayout.Label("On the station", _text);
                foreach (string line in _cookLines) GUILayout.Label(line, _dim);
                GUILayout.Space(8);
                return;
            }
            AutoFeed.Inside i = _inside;
            GUILayout.Label("Inside", _text);
            GUILayout.BeginHorizontal(GUILayout.Height(30));
            if (i.Queue.Count == 0) GUILayout.Label("Nothing is loaded.", _dim);
            foreach (var entry in i.Queue)
            {
                Rect icon = GUILayoutUtility.GetRect(28f, 28f, GUILayout.Width(28), GUILayout.Height(28));
                DrawIcon(icon, AutoFeed.IconOf(entry.Key));
                GUILayout.Label($"{entry.Value} {AutoFeed.NameOf(entry.Key)}", _text, GUILayout.ExpandWidth(false));
                GUILayout.Space(10);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            string status = $"Loaded {i.QueueSize}/{i.MaxOre}";
            if (i.MaxFuel > 0) status += $"     Fuel {Mathf.Floor(i.Fuel):0}/{i.MaxFuel}";
            if (i.Ready > 0) status += $"     Ready to collect: {i.Ready} {i.ReadyName}";
            GUILayout.Label(status, _dim);
            GUILayout.Space(8);
        }

        /// <summary>The auto-feed switch and the list of items it may use.</summary>
        private void DrawAutoPanel()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Auto-feed from chests", _text);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_auto.On ? "ON" : "OFF", _auto.On ? _buttonOn : _button, GUILayout.Width(80), GUILayout.Height(28))) _pending = ToggleAuto;
            GUILayout.EndHorizontal();
            GUILayout.Label(_auto.On
                ? $"Keeps this stocked from chests within {_autoRadius.Value:0} m, by itself. It only uses the items ticked below, plain ones first."
                : "Turn on to keep this stocked from nearby chests without pressing anything. You choose which items it may use.", _dim);

            DrawLimits();

            if (IsFermenter)
            {
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Taps itself when ready", _text);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(_auto.TakeOff ? "ON" : "OFF", _auto.TakeOff ? _buttonOn : _button, GUILayout.Width(80), GUILayout.Height(26))) _pending = ToggleTakeOff;
                GUILayout.EndHorizontal();
                GUILayout.Label("When the mead is ready it comes out by itself (and with auto-load on, the next base goes straight in).", _dim);
            }
            if (IsCooking)
            {
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Done food comes off by itself", _text);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(_auto.TakeOff ? "ON" : "OFF", _auto.TakeOff ? _buttonOn : _button, GUILayout.Width(80), GUILayout.Height(26))) _pending = ToggleTakeOff;
                GUILayout.EndHorizontal();
                GUILayout.Label(_takeOffCooked.Value
                    ? "The moment something is done it comes off, so it never burns: into a chest (below), or it slides off the side of the spit towards you."
                    : "Switched off for every station in the config (Cooking, TakeOffCooked).", _dim);
            }

            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            GUILayout.Label(IsCooking ? "Done food goes into chests" : IsFermenter ? "Mead goes into chests" : "What it makes goes into chests", _text);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_auto.Output ? "ON" : "OFF", _auto.Output ? _buttonOn : _button, GUILayout.Width(80), GUILayout.Height(26))) _pending = ToggleOutput;
            GUILayout.EndHorizontal();
            GUILayout.Label(IsFermenter
                ? "Goes to a chest assigned to that mead or to Potions (K), or one that already holds it. With none, it drops as usual."
                : IsCooking
                ? "Goes to the chests assigned to that food or to Food (the chest assign menu, K), nearest first. With none, it slides off the spit."
                : "Goes to the chests assigned to that item (the chest assign menu, K), nearest first. With none assigned it drops on the ground as usual.", _dim);

            GUILayout.Space(2);
            for (int i = 0; i < _autoItems.Count; i += 3)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 3, _autoItems.Count); j++)
                {
                    AutoItem item = _autoItems[j];
                    bool on = _auto.Allowed.Contains(item.Name);
                    string label = (on ? "[x] " : "[  ] ") + item.Display + (item.IsFuel ? " (fuel)" : "");
                    if (GUILayout.Button(label, on ? _buttonOn : _button, GUILayout.Height(26))) { string name = item.Name; _pending = () => ToggleAutoItem(name); }
                }
                GUILayout.EndHorizontal();
            }
            // For each ticked item: how many the chests hold against the minimum, so it is clear why it is or is not being fed.
            // And a warning if the fuel is not ticked (the smelter would run out and stop).
            if (_station != null && _station.Fuel != null && !_auto.Allowed.Contains(_station.Fuel.m_itemData.m_shared.m_name) && _auto.On)
                GUILayout.Label($"{Localization.instance.Localize(_station.Fuel.m_itemData.m_shared.m_name)} is not ticked, so this will run out of fuel and stop.", _bad);
            foreach (var line in _limitLines) GUILayout.Label(line.Key, line.Value ? _good : _warn);
            GUILayout.Space(8);
        }

        /// <summary>The limits that make sense for this station (see Limits), each as one row: label, -/+ buttons, value.</summary>
        private void DrawLimits()
        {
            if (_plan.Target) NumberRow(_plan.TargetLabel, _auto.Target, true, _plan.TargetHelp, AdjustTarget);
            if (_plan.Reserve) NumberRow(_plan.ReserveLabel, _auto.Reserve, false, _plan.ReserveHelp, AdjustReserve);
            if (_plan.FuelReserve) NumberRow(_plan.FuelLabel, _auto.FuelReserve, false, _plan.FuelHelp, AdjustFuelReserve);
        }

        private void NumberRow(string label, int value, bool offWhenZero, string help, Action<int> adjust)
        {
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _text, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("-10", _button, GUILayout.Width(44), GUILayout.Height(24))) _pending = () => adjust(-10);
            if (GUILayout.Button("-1", _button, GUILayout.Width(36), GUILayout.Height(24))) _pending = () => adjust(-1);
            GUILayout.Label(value == 0 && offWhenZero ? "off" : value.ToString(), _text, GUILayout.Width(48));
            if (GUILayout.Button("+1", _button, GUILayout.Width(36), GUILayout.Height(24))) _pending = () => adjust(1);
            if (GUILayout.Button("+10", _button, GUILayout.Width(44), GUILayout.Height(24))) _pending = () => adjust(10);
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(help)) GUILayout.Label(help, _dim);
        }

        /// <summary>
        /// Make sure the mouse can move. The game only frees the cursor when it thinks the mouse is the active input device, which on
        /// some setups (Linux, Steam Deck/Steam Input, a controller plugged in) it doesn't, leaving the cursor stuck in the middle of
        /// the screen. So while our window is open we set Unity's cursor directly, every time we draw (which happens after the game's own
        /// per-frame cursor handling, so ours wins).
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

        private static void Rounded(Rect r, Color c, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.zero, new Vector4(radius, radius, radius, radius));

        private static void Outline(Rect r, Color c, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.one, new Vector4(radius, radius, radius, radius));

        // ---- styles ---------------------------------------------------------------------------------------

        private Texture2D MakeBox(Color fill, Color border)
        {
            const int size = 6;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    t.SetPixel(x, y, (x < 2 || y < 2 || x >= size - 2 || y >= size - 2) ? border : fill);
            t.Apply();
            _textures.Add(t);
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

        private void EnsureStyles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            _title.normal.textColor = Gold;
            _text = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            _text.normal.textColor = new Color(0.95f, 0.92f, 0.86f);
            _dim = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            _dim.normal.textColor = Dim;
            _good = new GUIStyle(_dim); _good.normal.textColor = new Color(0.6f, 0.9f, 0.6f);
            _warn = new GUIStyle(_dim); _warn.normal.textColor = new Color(1f, 0.75f, 0.35f);
            _bad = new GUIStyle(_dim); _bad.normal.textColor = new Color(1f, 0.55f, 0.4f);
            _button = ButtonLook(new Color(0.22f, 0.19f, 0.15f), new Color(0.33f, 0.27f, 0.18f), new Color(0.45f, 0.36f, 0.2f));
            _buttonOn = ButtonLook(new Color(0.55f, 0.42f, 0.16f), new Color(0.62f, 0.48f, 0.2f), new Color(0.95f, 0.78f, 0.35f));
        }

        private void DestroyMenuResources()
        {
            foreach (Texture2D t in _textures) if (t != null) Destroy(t);
            _textures.Clear();
            _title = _text = _dim = _button = _buttonOn = _good = _warn = _bad = null;
        }
    }

    // While the menu is open: keep the game from reacting to your clicks and typing, and show the mouse.
    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class PlayerController_TakeInput
    {
        private static void Postfix(ref bool __result) { if (Plugin.MenuOpen) __result = false; }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class Player_TakeInput
    {
        private static void Postfix(ref bool __result) { if (Plugin.MenuOpen) __result = false; }
    }

    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    internal static class GameCamera_UpdateMouseCapture
    {
        // While our window is open, skip the game's own cursor handling entirely. If we let it run and then undo it, the game locks the
        // cursor every frame, and on Linux locking physically snaps the pointer to the middle of the screen, so it looks stuck there.
        private static bool Prefix()
        {
            if (!Plugin.MenuOpen) return true;
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
            return false;
        }
    }
}

namespace FeedFromChests
{
    // While one of our windows is open, the mouse wheel should scroll the window, not zoom the camera (or cycle the hotbar).
    [HarmonyLib.HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ZInput_GetMouseScrollWheel
    {
        private static void Postfix(ref float __result) { if (Plugin.MenuOpen) __result = 0f; }
    }
}

namespace FeedFromChests
{
    // Escape closes our window, and only that: the game's own menu does not open (so the game is not paused by it).
    [HarmonyLib.HarmonyPatch(typeof(Menu), "Update")]
    internal static class Menu_Update_EscapeCloses
    {
        private static bool Prefix()
        {
            if (!(Plugin.MenuOpen)) return true;
            if (!(ZInput.GetKeyDown(UnityEngine.KeyCode.Escape) || ZInput.GetButtonDown("JoyMenu"))) return true;
            Plugin.CloseFromEscape();
            return false; // skip the game's menu handling for this frame
        }
    }
}
