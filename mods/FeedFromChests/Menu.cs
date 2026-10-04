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

        private StationInfo _station;
        private List<Row> _rows = new List<Row>();
        private float _nextRefresh;
        private Vector2 _scroll;
        private Rect _rect;
        private bool _placed;
        private string _status = "";

        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private GUIStyle _title, _text, _dim, _button, _buttonOn;

        private static readonly Color Gold = new Color(0.95f, 0.78f, 0.35f);
        private static readonly Color Dim = new Color(0.68f, 0.66f, 0.62f);

        // ---- opening, closing, refreshing -------------------------------------------------------------

        private void OpenMenu(Player player, StationInfo info)
        {
            _station = info;
            _status = "";
            Mark("opened the menu");
            _stationTitle = "Add to " + Localization.instance.Localize(info.Title);
            _subtitle = $"Uses your inventory first, then chests within {_radius.Value:0} m. The station's own limits still apply.";
            RefreshRows(player);
            Logger.LogInfo($"Menu for '{info.Title}': {_rows.Count} item(s) available, {_station.Inputs.Count} input(s), fuel={(_station.Fuel != null ? "yes" : "no")}");
            if (_rows.Count == 0)
            {
                player.Message(MessageHud.MessageType.Center, "Nothing to add: no item this takes in your inventory or nearby chests");
                _station = null;
                return;
            }
            MenuOpen = true;
        }

        private void CloseMenu()
        {
            MenuOpen = false;
            _station = null;
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

        private void RefreshRowsCore(Player player)
        {
            _nextRefresh = Time.unscaledTime + 0.5f;
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

            float w = Mathf.Min(520f, sw - 40f), h = Mathf.Min(520f, sh - 60f);
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

            Player player = Player.m_localPlayer;
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
            _button = ButtonLook(new Color(0.22f, 0.19f, 0.15f), new Color(0.33f, 0.27f, 0.18f), new Color(0.45f, 0.36f, 0.2f));
            _buttonOn = ButtonLook(new Color(0.55f, 0.42f, 0.16f), new Color(0.62f, 0.48f, 0.2f), new Color(0.95f, 0.78f, 0.35f));
        }

        private void DestroyMenuResources()
        {
            foreach (Texture2D t in _textures) if (t != null) Destroy(t);
            _textures.Clear();
            _title = _text = _dim = _button = _buttonOn = null;
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
        private static void Postfix()
        {
            if (!Plugin.MenuOpen) return;
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
        }
    }
}
