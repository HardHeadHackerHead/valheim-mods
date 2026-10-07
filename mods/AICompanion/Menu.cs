using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// The companion's menu (J, or E on it): health, what it is doing and why, orders, how it fights, its gear, and the brain (Jev key,
    /// test, cost, its last decisions). With no companion of yours nearby it offers to summon one.
    /// </summary>
    public partial class Plugin
    {
        internal static bool MenuOpen;
        private Humanoid _shown;          // null: the summon panel
        private Rect _rect;
        private bool _placed;
        private Action _pending;          // button actions run in Update, not in the middle of drawing
        private string _nameField = "", _note = "", _testResult = "";
        private Vector2 _scroll;
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private GUIStyle _title, _text, _dim, _button, _buttonOn, _good, _warn, _bad, _field;
        private static readonly Color Gold = new Color(0.95f, 0.78f, 0.35f), Dim = new Color(0.72f, 0.68f, 0.6f);

        internal void OpenMenuFor(Player player, Humanoid companion)
        {
            _shown = companion;
            _nameField = companion != null ? Companion.NameOf(companion) : (player.m_customData.TryGetValue("dhc_name", out string n) ? n : "Rádvar");
            _note = companion == null ? "" : (Companion.IsMine(companion, player) ? "" : $"This is {Companion.Zdo(companion).GetString(Keys.MasterName, "someone")}'s companion: only they can change it.");
            _testResult = "";
            MenuOpen = true;
        }

        internal static void CloseFromEscape() => Instance?.CloseMenu();

        private void CloseMenu()
        {
            MenuOpen = false;
            _shown = null;
            _pending = null;
        }

        private void UpdateMenu(Player player)
        {
            if (!MenuOpen) return;
            Action run = _pending;
            _pending = null;
            run?.Invoke();
            if (!MenuOpen) return;
            if (_shown != null && (_shown.IsDead() || Vector3.Distance(_shown.transform.position, player.transform.position) > 100f)) CloseMenu();
            else if (Menu.IsVisible() || InventoryGui.IsVisible()) CloseMenu();
        }

        private bool Mine => _shown != null && Companion.IsMine(_shown, Player.m_localPlayer);

        private void Change(Action<ZDO> change)
        {
            if (!Mine) { _note = "Only its owner can change it."; return; }
            if (!Companion.Write(_shown, change)) _note = "Someone has its gear open; try again in a moment.";
        }

        // ---- drawing -------------------------------------------------------------------------------------

        private void OnGUI()
        {
            if (!MenuOpen) return;
            FreeTheMouse();
            EnsureStyles();
            Matrix4x4 previous = GUI.matrix;
            float s = Mathf.Max(0.8f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float sw = Screen.width / s, sh = Screen.height / s;
            float w = Mathf.Min(520f, sw - 40f), h = Mathf.Min(_shown != null ? 860f : 300f, sh - 60f);
            if (!_placed) { _rect = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h); _placed = true; }
            _rect.width = w; _rect.height = h;
            _rect = GUI.Window(8871, _rect, DrawWindow, GUIContent.none, GUIStyle.none);
            _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, sw - w));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, sh - h));
            GUI.matrix = previous;
        }

        private void DrawWindow(int id)
        {
            float w = _rect.width, h = _rect.height;
            Rounded(new Rect(0, 0, w, h), new Color(0.07f, 0.06f, 0.05f, 0.98f), 9f);
            Outline(new Rect(0, 0, w, h), new Color(0.62f, 0.47f, 0.22f, 1f), 9f);
            GUILayout.BeginArea(new Rect(16f, 12f, w - 32f, h - 24f));
            if (_shown == null) DrawSummon(); else DrawCompanion();
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, w, 40f));
        }

        private void DrawSummon()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Companion", _title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", _button, GUILayout.Width(80), GUILayout.Height(28))) CloseMenu();
            GUILayout.EndHorizontal();
            GUILayout.Label("You have no companion here. Summon one: a viking who follows you and fights beside you. Give it weapons, armour and potions through its menu.", _dim);
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _text, GUILayout.Width(60));
            _nameField = GUILayout.TextField(_nameField ?? "", 24, _field, GUILayout.Height(26));
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            if (GUILayout.Button("Summon companion", _buttonOn, GUILayout.Height(34)))
                _pending = () =>
                {
                    Player p = Player.m_localPlayer;
                    Humanoid c = p != null ? Companion.Summon(p, _nameField.Trim()) : null;
                    if (c != null) OpenMenuFor(p, c);
                };
            GUILayout.Space(6);
            GUILayout.Label(string.IsNullOrEmpty(ApiKey.Value) ? "No Jev key yet: it fights with the built-in brain until you add one (in its menu)." : "Jev key set: Jev decides how it fights.", _dim);
        }

        private void DrawCompanion()
        {
            Humanoid c = _shown;
            BrainState st = Brain.Get(c);
            bool owner = c.GetComponent<ZNetView>().IsOwner();

            GUILayout.BeginHorizontal();
            if (Mine)
            {
                _nameField = GUILayout.TextField(_nameField ?? "", 24, _field, GUILayout.Width(200), GUILayout.Height(28));
                if (_nameField.Trim() != Companion.NameOf(c) && _nameField.Trim().Length > 0 && GUILayout.Button("Rename", _button, GUILayout.Width(80), GUILayout.Height(28)))
                { string n = _nameField.Trim(); _pending = () => { Change(z => z.Set(Keys.Name, n)); Player.m_localPlayer.m_customData["dhc_name"] = n; }; }
            }
            else GUILayout.Label(Companion.NameOf(c), _title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", _button, GUILayout.Width(80), GUILayout.Height(28))) CloseMenu();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_note)) GUILayout.Label(_note, _warn);

            Bar("Health", c.GetHealth(), c.GetMaxHealth(), new Color(0.75f, 0.2f, 0.15f));
            GUILayout.Label($"Armour {Companion.Armor(c):0}   ·   " + (string.IsNullOrEmpty(Companion.StatusOf(c)) ? "idle" : "Now: " + Companion.StatusOf(c)), _good);

            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);

            Section("Orders");
            GUILayout.BeginHorizontal();
            Order order = Companion.OrderOf(c);
            if (Toggle("Follow me", order == Order.Follow)) _pending = () => Change(z => z.Set(Keys.Order, (int)Order.Follow));
            if (Toggle("Stay here", order == Order.Stay)) _pending = () => Change(z => z.Set(Keys.Order, (int)Order.Stay));
            if (Toggle("Guard this spot", order == Order.Guard)) _pending = () => Change(z => { z.Set(Keys.Order, (int)Order.Guard); z.Set(Keys.Post, c.transform.position); });
            GUILayout.EndHorizontal();

            Section("How it fights");
            GUILayout.BeginHorizontal();
            Style style = Companion.StyleOf(c);
            foreach (Style s in (Style[])Enum.GetValues(typeof(Style)))
                if (Toggle(s.ToString(), style == s)) { Style pick = s; _pending = () => Change(z => z.Set(Keys.Style, (int)pick)); }
            GUILayout.EndHorizontal();
            GUILayout.Label(style switch
            {
                Style.Aggressive => "Presses the attack; falls back only when badly hurt.",
                Style.Defensive => "Stays close to you and fights what comes near; falls back early.",
                Style.Passive => "Never starts a fight: keeps by your side and keeps safe.",
                _ => "Fights what threatens you or it; falls back when hurt.",
            }, _dim);
            GUILayout.BeginHorizontal();
            int retreat = Companion.RetreatOf(c);
            GUILayout.Label("Fall back below", _text, GUILayout.Width(140));
            if (GUILayout.Button("-", _button, GUILayout.Width(32), GUILayout.Height(24))) _pending = () => Change(z => z.Set(Keys.Retreat, Mathf.Clamp(retreat - 5, 0, 90)));
            GUILayout.Label($"{retreat}% health", _text, GUILayout.Width(100));
            if (GUILayout.Button("+", _button, GUILayout.Width(32), GUILayout.Height(24))) _pending = () => Change(z => z.Set(Keys.Retreat, Mathf.Clamp(retreat + 5, 0, 90)));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            bool potions = Companion.Potions(c), protect = Companion.Protect(c);
            if (Toggle(potions ? "☑ Drinks potions" : "☐ Drinks potions", potions)) _pending = () => Change(z => z.Set(Keys.Potions, !potions));
            if (Toggle(protect ? "☑ Protects you first" : "☐ Protects you first", protect)) _pending = () => Change(z => z.Set(Keys.Protect, !protect));
            GUILayout.EndHorizontal();

            Section("Gear");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Open its inventory", _buttonOn, GUILayout.Width(180), GUILayout.Height(30))) _pending = OpenGear;
            GUILayout.Label("Weapons, armour, arrows and healing potions. It wears the best and uses what Jev picks.", _dim);
            GUILayout.EndHorizontal();
            string worn = string.Join(", ", Companion.Worn(c).Select(i => Localization.instance.Localize(i.m_shared.m_name)).Distinct());
            GUILayout.Label("Wearing: " + (worn.Length > 0 ? worn : "nothing yet"), _dim);

            Section("Brain");
            bool jevOn = UseJev.Value && Companion.UsesJev(c);
            string key = ApiKey.Value;
            string light = !jevOn ? "Built-in brain (Jev off)" : string.IsNullOrEmpty(key) ? "No Jev key: the built-in brain fights" :
                !string.IsNullOrEmpty(Jev.LastError) ? "Jev: " + Jev.LastError : Jev.Decisions > 0 ? $"● Jev connected · {Jev.LastMs:0} ms" : "Jev key set (not asked yet)";
            GUILayout.Label(light, !jevOn || string.IsNullOrEmpty(key) || !string.IsNullOrEmpty(Jev.LastError) ? _warn : _good);
            GUILayout.Label($"Today: {Jev.Decisions} decisions, {Jev.Failures} failed, about ${Jev.Cost:0.0000}", _dim);
            GUILayout.BeginHorizontal();
            GUILayout.Label("API key: " + Mask(key), _text, GUILayout.Width(220));
            if (GUILayout.Button("Paste", _button, GUILayout.Width(70), GUILayout.Height(26))) _pending = PasteKey;
            if (GUILayout.Button("Test", _button, GUILayout.Width(60), GUILayout.Height(26))) _pending = () => { _testResult = "Testing…"; StartCoroutine(Jev.Test(r => _testResult = r)); };
            if (!string.IsNullOrEmpty(key) && GUILayout.Button("Clear", _button, GUILayout.Width(60), GUILayout.Height(26))) _pending = () => { ApiKey.Value = ""; Config.Save(); _testResult = "Key removed"; };
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_testResult)) GUILayout.Label(_testResult, _testResult.StartsWith("Connected") ? _good : _dim);
            GUILayout.Label("Copy your key from console.typesafe.ai, then press Paste. It is kept in your own settings file, never shared.", _dim);
            bool usesJev = Companion.UsesJev(c);
            if (Toggle(usesJev ? "☑ Use Jev (off = the simple built-in brain)" : "☐ Use Jev (off = the simple built-in brain)", usesJev)) _pending = () => Change(z => z.Set(Keys.UseJev, !usesJev));

            GUILayout.Space(4);
            GUILayout.Label("Last decisions", _text);
            if (!owner) GUILayout.Label("(made on the game of whoever is nearest to it right now)", _dim);
            else if (st.History.Count == 0) GUILayout.Label("None yet: it decides when a fight starts.", _dim);
            foreach (string line in st.History) GUILayout.Label("  " + line, _dim);
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (Mine && GUILayout.Button("Send home", _button, GUILayout.Width(110), GUILayout.Height(30)))
                _pending = () => { if (Companion.Dismiss(c, out string why)) CloseMenu(); else _note = why; };
            GUILayout.EndHorizontal();
        }

        private void OpenGear()
        {
            Player p = Player.m_localPlayer;
            if (_shown == null || p == null) return;
            if (!Mine) { _note = "Only its owner can change its gear."; return; }
            Container gear = _shown.GetComponent<Container>();
            if (gear == null) return;
            Patches.OpeningGear = true;
            try { gear.Interact(p, false, false); }
            finally { Patches.OpeningGear = false; }
            CloseMenu();
        }

        private void PasteKey()
        {
            string text = (GUIUtility.systemCopyBuffer ?? "").Trim();
            if (text.Length < 10 || text.Contains(" ") || text.Contains("\n")) { _testResult = "The clipboard does not hold a key (copy it from console.typesafe.ai first)."; return; }
            ApiKey.Value = text;
            Config.Save();
            Jev.BlockedUntil = 0f; Jev.InRow = 0; Jev.LastError = "";
            _testResult = "Key saved (" + Mask(text) + "). Press Test to check it.";
        }

        // ---- pieces --------------------------------------------------------------------------------------

        private void Section(string title)
        {
            GUILayout.Space(8);
            GUILayout.Label(title, _text);
        }

        private bool Toggle(string label, bool on) => GUILayout.Button(label, on ? _buttonOn : _button, GUILayout.Height(28));

        private void Bar(string label, float value, float max, Color color)
        {
            Rect r = GUILayoutUtility.GetRect(10f, 22f, GUILayout.ExpandWidth(true));
            Rounded(r, new Color(0.18f, 0.15f, 0.12f), 4f);
            Rounded(new Rect(r.x, r.y, r.width * Mathf.Clamp01(max > 0 ? value / max : 0), r.height), color, 4f);
            GUI.Label(new Rect(r.x + 8, r.y + 1, r.width, r.height), $"{label} {value:0}/{max:0}", _text);
        }

        private static void FreeTheMouse()
        {
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        private static void Rounded(Rect r, Color c, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.zero, new Vector4(radius, radius, radius, radius));

        private static void Outline(Rect r, Color c, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.one, new Vector4(radius, radius, radius, radius));

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
            _dim = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            _dim.normal.textColor = Dim;
            _good = new GUIStyle(_dim); _good.normal.textColor = new Color(0.6f, 0.9f, 0.6f);
            _warn = new GUIStyle(_dim); _warn.normal.textColor = new Color(1f, 0.75f, 0.35f);
            _bad = new GUIStyle(_dim); _bad.normal.textColor = new Color(1f, 0.55f, 0.4f);
            _button = ButtonLook(new Color(0.22f, 0.19f, 0.15f), new Color(0.33f, 0.27f, 0.18f), new Color(0.45f, 0.36f, 0.2f));
            _buttonOn = ButtonLook(new Color(0.55f, 0.42f, 0.16f), new Color(0.62f, 0.48f, 0.2f), new Color(0.95f, 0.78f, 0.35f));
            _field = new GUIStyle(GUI.skin.textField) { fontSize = 15, padding = new RectOffset(6, 6, 4, 4) };
            _field.normal.background = _field.focused.background = _field.hover.background = MakeBox(new Color(0.12f, 0.1f, 0.08f), new Color(0.45f, 0.36f, 0.2f));
            _field.normal.textColor = _field.focused.textColor = _field.hover.textColor = new Color(0.95f, 0.92f, 0.86f);
        }

        private void DestroyMenuResources()
        {
            foreach (Texture2D t in _textures) if (t != null) Destroy(t);
            _textures.Clear();
            _title = _text = _dim = _button = _buttonOn = _good = _warn = _bad = _field = null;
        }
    }
}
