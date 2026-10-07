using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// The companion's menu (J, or E on it), in tabs:
    ///   Overview  - health, what it is doing and against whom, how sure Jev was, quick orders, its record (kills, fights, potions);
    ///   Orders    - follow / stay / guard, fighting style, when to fall back, potions, protecting you;
    ///   Inventory - what it wears, its bag (click to take), and the useful things in yours (click to give);
    ///   Brain     - the Jev key, test, cost and speed, how often to ask and how sure Jev must be;
    ///   Debug     - every decision, with Jev's odds for each answer and the exact JSON sent and received.
    /// With no companion of yours nearby it offers to summon one.
    /// </summary>
    public partial class Plugin
    {
        private enum Tab { Overview, Orders, Inventory, Brain, Debug }

        internal static bool MenuOpen;
        private Humanoid _shown;          // null: the summon panel
        private Tab _tab = Tab.Overview;
        private Rect _rect;
        private bool _placed;
        private Action _pending;          // button actions run in Update, not in the middle of drawing
        private string _nameField = "", _note = "", _testResult = "", _hover = "";
        private Vector2 _scroll, _logScroll, _jsonScroll;
        private DecisionRecord _picked;
        private int _filter;              // Debug: 0 all, 1 Jev, 2 built-in, 3 failed
        private bool _showJson;
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private GUIStyle _title, _text, _dim, _button, _buttonOn, _good, _warn, _bad, _field, _row, _rowOn, _small, _mono;
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

        // ---- the window ------------------------------------------------------------------------------------

        private void OnGUI()
        {
            if (!MenuOpen) return;
            FreeTheMouse();
            EnsureStyles();
            Matrix4x4 previous = GUI.matrix;
            float s = Mathf.Max(0.8f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float sw = Screen.width / s, sh = Screen.height / s;
            float w = Mathf.Min(580f, sw - 40f), h = Mathf.Min(_shown != null ? 760f : 300f, sh - 60f);
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
            GUILayout.Label("You have no companion here. Summon one: a viking who follows you and fights beside you. Give it weapons, armour and potions in its Inventory tab.", _dim);
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
                    if (c != null) { OpenMenuFor(p, c); _tab = Tab.Inventory; }
                };
            GUILayout.Space(6);
            GUILayout.Label(string.IsNullOrEmpty(ApiKey.Value) ? "No Jev key yet: it fights with the built-in brain until you add one (Brain tab)." : "Jev key set: Jev decides how it fights.", _dim);
        }

        private void DrawCompanion()
        {
            Humanoid c = _shown;
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

            Bar($"Health {c.GetHealth():0}/{c.GetMaxHealth():0}", c.GetHealth() / Mathf.Max(1f, c.GetMaxHealth()), new Color(0.75f, 0.2f, 0.15f));
            Bar($"Stamina {Stamina.Get(c):0}/{Stamina.Max(c):0}", Stamina.Get(c) / Mathf.Max(1f, Stamina.Max(c)), new Color(0.85f, 0.68f, 0.18f), 14f);
            string status = Companion.StatusOf(c);
            GUILayout.Label(string.IsNullOrEmpty(status) ? "idle" : status, _good);
            if (!string.IsNullOrEmpty(_note)) GUILayout.Label(_note, _warn);

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            foreach (Tab t in (Tab[])Enum.GetValues(typeof(Tab)))
                if (GUILayout.Button(t.ToString(), _tab == t ? _buttonOn : _button, GUILayout.Height(30))) { _tab = t; _hover = ""; }
            GUILayout.EndHorizontal();
            Rect line = GUILayoutUtility.GetRect(10f, 2f, GUILayout.ExpandWidth(true));
            Rounded(line, new Color(0.62f, 0.47f, 0.22f, 0.6f), 1f);
            GUILayout.Space(4);

            if (_tab == Tab.Debug) { DrawDebug(c); return; }
            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            switch (_tab)
            {
                case Tab.Overview: DrawOverview(c); break;
                case Tab.Orders: DrawOrders(c); break;
                case Tab.Inventory: DrawInventory(c); break;
                case Tab.Brain: DrawBrain(c); break;
            }
            GUILayout.EndScrollView();
        }

        // ---- Overview ----------------------------------------------------------------------------------------

        private void DrawOverview(Humanoid c)
        {
            BrainState st = Brain.Get(c);
            Decision d = st.Current;
            bool owner = c.GetComponent<ZNetView>().IsOwner();

            GUILayout.BeginHorizontal();
            Stat("Armour", $"{Companion.Armor(c):0}");
            Stat("Kills", Companion.Zdo(c).GetInt(Keys.Kills, 0).ToString());
            Stat("Fights", owner ? st.Fights.ToString() : "-");
            Stat("Potions drunk", owner ? st.Potions.ToString() : "-");
            GUILayout.EndHorizontal();

            Section("In a fight");
            if (!owner) GUILayout.Label("Its brain runs on the game of the player nearest to it, so the details are there.", _dim);
            else if (!st.InCombat) GUILayout.Label($"Not fighting. It decides when an enemy comes within {EngageRange.Value:0} m of it or you.", _dim);
            else
            {
                GUILayout.Label($"Enemies: {string.Join(", ", st.Enemies.Where(e => e != null).Select(st.Label))}", _text);
                GUILayout.Label($"Doing: {d.Describe(st.Label)}", _good);
                if (d.FromJev) Bar($"Jev is {d.Confidence * 100f:0}% sure", d.Confidence, new Color(0.3f, 0.65f, 0.55f));
                if (!string.IsNullOrEmpty(d.Note)) GUILayout.Label(d.Note, _dim);
            }

            Section("Effects");
            var effects = owner ? c.GetSEMan().GetStatusEffects().Where(se => se != null && se.m_icon != null).ToList() : new List<StatusEffect>();
            if (!owner) GUILayout.Label("Known on the game that runs it.", _dim);
            else if (effects.Count == 0) GUILayout.Label("None. Your boss power reaches it too when it is within 10 m of you.", _dim);
            else
            {
                GUILayout.BeginHorizontal();
                foreach (StatusEffect se in effects.Take(8))
                {
                    GUILayout.BeginVertical(GUILayout.Width(64));
                    Rect r = GUILayoutUtility.GetRect(32f, 32f, GUILayout.Width(32), GUILayout.Height(32));
                    DrawIcon(r, se.m_icon);
                    float left = se.GetRemaningTime();
                    GUILayout.Label(left > 0f ? $"{Mathf.FloorToInt(left / 60f)}:{Mathf.FloorToInt(left % 60f):00}" : "", _dim);
                    GUILayout.EndVertical();
                }
                GUILayout.EndHorizontal();
                GUILayout.Label(string.Join(", ", effects.Select(se => Localization.instance.Localize(se.m_name))), _dim);
            }

            Section("Quick orders");
            GUILayout.BeginHorizontal();
            Order order = Companion.OrderOf(c);
            if (Toggle("Follow me", order == Order.Follow)) _pending = () => Change(z => z.Set(Keys.Order, (int)Order.Follow));
            if (Toggle("Stay here", order == Order.Stay)) _pending = () => Change(z => z.Set(Keys.Order, (int)Order.Stay));
            if (GUILayout.Button("Come here", _button, GUILayout.Height(28))) _pending = ComeHere;
            GUILayout.EndHorizontal();

            Section("Brain");
            DrawBrainLight(c);
            GUILayout.Label($"This session: Jev decided {st.JevCalls} times, the built-in brain {st.BuiltInCalls} times.", _dim);

            Section("Recently");
            if (st.History.Count == 0) GUILayout.Label("Nothing yet.", _dim);
            foreach (string h in st.History.Take(6)) GUILayout.Label("  " + h, _dim);
        }

        private void ComeHere()
        {
            Player p = Player.m_localPlayer;
            if (p == null || _shown == null) return;
            Change(z => z.Set(Keys.Order, (int)Order.Follow));
            if (Mine && Vector3.Distance(p.transform.position, _shown.transform.position) > 15f && _shown.GetComponent<ZNetView>().IsOwner()) Brain.TeleportBehind(_shown, p);
        }

        // ---- Orders ------------------------------------------------------------------------------------------

        private void DrawOrders(Humanoid c)
        {
            Section("Orders");
            GUILayout.BeginHorizontal();
            Order order = Companion.OrderOf(c);
            if (Toggle("Follow me", order == Order.Follow)) _pending = () => Change(z => z.Set(Keys.Order, (int)Order.Follow));
            if (Toggle("Stay here", order == Order.Stay)) _pending = () => Change(z => z.Set(Keys.Order, (int)Order.Stay));
            if (Toggle("Guard this spot", order == Order.Guard)) _pending = () => Change(z => { z.Set(Keys.Order, (int)Order.Guard); z.Set(Keys.Post, c.transform.position); });
            GUILayout.EndHorizontal();
            GUILayout.Label(order switch
            {
                Order.Stay => "Stays where it is, and fights what comes near.",
                Order.Guard => "Stays by the spot it was given, and goes back to it after a fight.",
                _ => "Follows you, and catches up if it falls far behind (after a portal or a boat ride).",
            }, _dim);

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
            GUILayout.Label("These go to Jev with every question, so its choices follow them; the built-in brain follows them too.", _dim);

            GUILayout.Space(12);
            if (Mine && GUILayout.Button("Send home", _button, GUILayout.Width(120), GUILayout.Height(30)))
                _pending = () => { if (Companion.Dismiss(c, out string why)) CloseMenu(); else _note = why; };
            GUILayout.Label("Sends it away for good (only with its bag empty, so nothing is lost). Summon another with " + MenuKey.Value + ".", _dim);
        }

        // ---- Inventory ---------------------------------------------------------------------------------------

        private void DrawInventory(Humanoid c)
        {
            Player p = Player.m_localPlayer;
            if (Event.current.type == EventType.Repaint) _hover = "";
            Section("Wearing");
            GUILayout.BeginHorizontal();
            foreach (var slot in Companion.Slots(c))
            {
                GUILayout.BeginVertical(GUILayout.Width(66));
                Rect r = GUILayoutUtility.GetRect(52f, 52f, GUILayout.Width(52), GUILayout.Height(52));
                ItemCell(r, slot.Value, true, null);
                GUILayout.Label(slot.Key, _small, GUILayout.Width(56));
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("It wears the best armour it has and holds the weapon Jev picks (with a shield beside a one-handed one).", _dim);

            Inventory its = c.GetInventory();
            Section($"Its bag ({its.NrOfItems()}/{its.GetWidth() * its.GetHeight()})" + (Mine ? "  ·  click an item to take it" : ""));
            Grid(its, its.GetWidth(), its.GetHeight(), item => { if (Mine) _pending = () => { if (!Companion.Take(_shown, Player.m_localPlayer, item, out string why) && why != null) _note = why; }; });

            if (Mine && p != null)
            {
                var giveable = p.GetInventory().GetAllItems().Where(i => !p.IsItemEquiped(i) && Companion.Useful(c, i)).ToList();
                Section("From your inventory  ·  click to give");
                if (giveable.Count == 0) GUILayout.Label("Nothing to give: weapons, armour, shields, arrows and healing potions you are not wearing show here.", _dim);
                else
                {
                    const int per = 9;
                    int rows = Mathf.CeilToInt(giveable.Count / (float)per);
                    for (int row = 0; row < rows; row++)
                    {
                        GUILayout.BeginHorizontal();
                        foreach (ItemDrop.ItemData item in giveable.Skip(row * per).Take(per))
                        {
                            Rect r = GUILayoutUtility.GetRect(52f, 52f, GUILayout.Width(52), GUILayout.Height(52));
                            ItemDrop.ItemData it = item;
                            ItemCell(r, it, false, () => _pending = () => { if (!Companion.Give(_shown, Player.m_localPlayer, it, out string why) && why != null) _note = why; });
                        }
                        GUILayout.EndHorizontal();
                    }
                }
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Open the full chest view", _button, GUILayout.Width(200), GUILayout.Height(28))) _pending = OpenGear;
                GUILayout.Label("(the game's own screen, to drag items around)", _dim);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(4);
            GUILayout.Label(string.IsNullOrEmpty(_hover) ? "Point at an item to see what it is." : _hover, string.IsNullOrEmpty(_hover) ? _dim : _text);
        }

        private void Grid(Inventory inv, int width, int height, Action<ItemDrop.ItemData> click)
        {
            for (int y = 0; y < height; y++)
            {
                GUILayout.BeginHorizontal();
                for (int x = 0; x < width; x++)
                {
                    Rect r = GUILayoutUtility.GetRect(52f, 52f, GUILayout.Width(52), GUILayout.Height(52));
                    ItemDrop.ItemData item = inv.GetItemAt(x, y);
                    ItemCell(r, item, _shown != null && item != null && _shown.IsItemEquiped(item), item != null ? () => click(item) : (Action)null);
                }
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>One square: its icon, stack count and wear; a gold edge when worn; a click runs the action; pointing at it names it below.</summary>
        private void ItemCell(Rect r, ItemDrop.ItemData item, bool worn, Action click)
        {
            Rounded(r, new Color(0.13f, 0.11f, 0.09f, 1f), 4f);
            Outline(r, worn && item != null ? Gold : new Color(0.35f, 0.28f, 0.18f, 1f), 4f);
            if (item == null) return;
            DrawIcon(new Rect(r.x + 5f, r.y + 5f, r.width - 10f, r.height - 10f), item.GetIcon());
            if (item.m_stack > 1) GUI.Label(new Rect(r.x, r.yMax - 18f, r.width - 4f, 16f), item.m_stack.ToString(), _small);
            if (item.m_shared.m_useDurability && item.GetMaxDurability() > 0f)
            {
                float f = Mathf.Clamp01(item.m_durability / item.GetMaxDurability());
                Rounded(new Rect(r.x + 4f, r.yMax - 5f, (r.width - 8f) * f, 3f), Color.Lerp(new Color(0.9f, 0.3f, 0.2f), new Color(0.4f, 0.85f, 0.4f), f), 1f);
            }
            if (r.Contains(Event.current.mousePosition))
            {
                _hover = Describe(item);
                Outline(r, Color.white, 4f);
            }
            if (click != null && GUI.Button(r, GUIContent.none, GUIStyle.none)) click();
        }

        private static string Describe(ItemDrop.ItemData item)
        {
            string name = Localization.instance.Localize(item.m_shared.m_name) + (item.m_quality > 1 ? $" ★{item.m_quality}" : "");
            if (item.IsWeapon()) name += $"  ·  {item.GetDamage().GetTotalDamage():0} damage";
            if (item.GetArmor() > 0f) name += $"  ·  {item.GetArmor():0} armour";
            if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield) name += $"  ·  {item.m_shared.m_blockPower:0} block";
            if (item.m_shared.m_useDurability && item.GetMaxDurability() > 0f) name += $"  ·  {item.m_durability / item.GetMaxDurability() * 100f:0}% wear left";
            if (item.m_stack > 1) name += $"  ·  {item.m_stack}";
            return name;
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

        // ---- Brain -------------------------------------------------------------------------------------------

        private void DrawBrainLight(Humanoid c)
        {
            bool jevOn = UseJev.Value && Companion.UsesJev(c);
            string key = ApiKey.Value;
            string light = !jevOn ? "Built-in brain (Jev is off)" : string.IsNullOrEmpty(key) ? "No Jev key: the built-in brain fights (Brain tab)" :
                !string.IsNullOrEmpty(Jev.LastError) ? "Jev: " + Jev.LastError : Jev.Decisions > 0 ? $"● Jev connected  ·  last answer {Jev.LastMs:0} ms" : "Jev key set (not asked yet)";
            GUILayout.Label(light, !jevOn || string.IsNullOrEmpty(key) || !string.IsNullOrEmpty(Jev.LastError) ? _warn : _good);
        }

        private void DrawBrain(Humanoid c)
        {
            Section("Jev");
            DrawBrainLight(c);
            GUILayout.Label($"Today: {Jev.Decisions} answers, {Jev.Failures} failed, {Jev.Tokens:N0} tokens, about ${Jev.Cost:0.0000}", _dim);
            string key = ApiKey.Value;
            GUILayout.BeginHorizontal();
            GUILayout.Label("API key: " + Mask(key), _text, GUILayout.Width(230));
            if (GUILayout.Button("Paste", _button, GUILayout.Width(70), GUILayout.Height(26))) _pending = PasteKey;
            if (GUILayout.Button("Test", _button, GUILayout.Width(60), GUILayout.Height(26))) _pending = () => { _testResult = "Testing…"; StartCoroutine(Jev.Test(r => _testResult = r)); };
            if (!string.IsNullOrEmpty(key) && GUILayout.Button("Clear", _button, GUILayout.Width(60), GUILayout.Height(26))) _pending = () => { ApiKey.Value = ""; Config.Save(); _testResult = "Key removed"; };
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_testResult)) GUILayout.Label(_testResult, _testResult.StartsWith("Connected") ? _good : _dim);
            GUILayout.Label("Copy your key from console.typesafe.ai, then press Paste. It is kept in your own settings file, never shared.", _dim);

            Section("Who decides");
            bool usesJev = Companion.UsesJev(c);
            if (Toggle(usesJev ? "☑ This companion asks Jev" : "☐ This companion asks Jev (off: the built-in brain)", usesJev)) _pending = () => Change(z => z.Set(Keys.UseJev, !usesJev));
            if (Toggle(UseJev.Value ? "☑ Jev on for all my companions" : "☐ Jev on for all my companions", UseJev.Value)) _pending = () => { UseJev.Value = !UseJev.Value; Config.Save(); };
            if (Toggle(ShowDecisions.Value ? "☑ Show decisions above its head" : "☐ Show decisions above its head", ShowDecisions.Value)) _pending = () => { ShowDecisions.Value = !ShowDecisions.Value; Config.Save(); };

            Section("Tuning");
            Stepper("Ask Jev every", $"{DecisionSeconds.Value:0.0} s", () => { DecisionSeconds.Value = Mathf.Clamp(DecisionSeconds.Value - 0.5f, 0.5f, 10f); Config.Save(); },
                    () => { DecisionSeconds.Value = Mathf.Clamp(DecisionSeconds.Value + 0.5f, 0.5f, 10f); Config.Save(); });
            GUILayout.Label("…and at once when something big happens (a new enemy, its target dead, a big hit). Shorter is sharper and costs a little more.", _dim);
            Stepper("Trust Jev from", $"{MinConfidence.Value * 100f:0}% sure", () => { MinConfidence.Value = Mathf.Clamp01(MinConfidence.Value - 0.05f); Config.Save(); },
                    () => { MinConfidence.Value = Mathf.Clamp01(MinConfidence.Value + 0.05f); Config.Save(); });
            GUILayout.Label("When Jev is less sure than this about what to do, the built-in brain decides that round.", _dim);
            Stepper("Fight enemies within", $"{EngageRange.Value:0} m", () => { EngageRange.Value = Mathf.Clamp(EngageRange.Value - 5f, 5f, 50f); Config.Save(); },
                    () => { EngageRange.Value = Mathf.Clamp(EngageRange.Value + 5f, 5f, 50f); Config.Save(); });
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

        // ---- Debug -------------------------------------------------------------------------------------------

        private void DrawDebug(Humanoid c)
        {
            BrainState st = Brain.Get(c);
            GUILayout.Label(DebugLog.Summary(), _dim);
            GUILayout.BeginHorizontal();
            string[] filters = { "All", "Jev", "Built-in", "Failed" };
            for (int i = 0; i < filters.Length; i++) if (Toggle(filters[i], _filter == i)) { int f = i; _pending = () => _filter = f; }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Ask Jev now", st.InCombat ? _buttonOn : _button, GUILayout.Width(110), GUILayout.Height(28)))
                _pending = () => { if (st.InCombat) st.NextAsk = 0f; else _note = "It only asks during a fight (no enemies near it now)."; };
            if (GUILayout.Button("Clear", _button, GUILayout.Width(60), GUILayout.Height(28))) _pending = () => { DebugLog.Records.Clear(); _picked = null; };
            GUILayout.EndHorizontal();
            if (Toggle(LogToFile.Value ? "☑ Also write every Jev request to BepInEx/AICompanion/jev-decisions.jsonl" : "☐ Also write every Jev request to a file (for tuning)", LogToFile.Value))
                _pending = () => { LogToFile.Value = !LogToFile.Value; Config.Save(); };

            IEnumerable<DecisionRecord> shown = DebugLog.Records;
            if (_filter == 1) shown = shown.Where(r => r.FromJev);
            else if (_filter == 2) shown = shown.Where(r => !r.FromJev);
            else if (_filter == 3) shown = shown.Where(r => r.Failed);
            var list = shown.Take(100).ToList();

            _logScroll = GUILayout.BeginScrollView(_logScroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.Height(_picked != null ? 170f : 440f));
            if (list.Count == 0) GUILayout.Label("No decisions yet: they appear here during a fight.", _dim);
            foreach (DecisionRecord r in list)
            {
                string src = r.FromJev ? $"Jev {r.Ms:0} ms" : r.Failed ? "FAILED: built-in" : r.AskedJev ? "unsure: built-in" : "built-in";
                if (GUILayout.Button($"{r.When:HH:mm:ss}   {src}   {r.Outcome}", r == _picked ? _rowOn : _row, GUILayout.Height(22)))
                { DecisionRecord pick = r; _pending = () => { _picked = _picked == pick ? null : pick; _jsonScroll = Vector2.zero; }; }
            }
            GUILayout.EndScrollView();

            if (_picked == null) { GUILayout.Label("Click a decision to see Jev's answers and the exact request.", _dim); return; }
            DecisionRecord p = _picked;
            GUILayout.Space(4);
            GUILayout.Label($"{p.When:HH:mm:ss}  {p.Companion}:  {p.Outcome}", _text);
            if (!string.IsNullOrEmpty(p.Enemies)) GUILayout.Label("Enemies: " + p.Enemies, _dim);
            if (!string.IsNullOrEmpty(p.Note)) GUILayout.Label("Why: " + p.Note, _warn);
            if (p.Failed) GUILayout.Label("Error: " + p.Error, _bad);
            foreach (string a in p.Answers) GUILayout.Label(a, _mono);
            if (!p.AskedJev) { GUILayout.Label("Decided by the built-in brain without asking Jev (no key, Jev off, or its target had just died).", _dim); return; }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_showJson ? "Hide JSON" : "Show JSON", _button, GUILayout.Width(110), GUILayout.Height(26))) _pending = () => _showJson = !_showJson;
            if (GUILayout.Button("Copy request", _button, GUILayout.Width(120), GUILayout.Height(26))) _pending = () => { GUIUtility.systemCopyBuffer = p.Request; _note = "Request copied."; };
            if (!string.IsNullOrEmpty(p.Response) && GUILayout.Button("Copy answer", _button, GUILayout.Width(110), GUILayout.Height(26))) _pending = () => { GUIUtility.systemCopyBuffer = p.Response; _note = "Answer copied."; };
            GUILayout.EndHorizontal();
            if (_showJson)
            {
                _jsonScroll = GUILayout.BeginScrollView(_jsonScroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar);
                GUILayout.Label("Sent to Jev", _text);
                GUILayout.Label(p.Request, _mono);
                if (!string.IsNullOrEmpty(p.Response)) { GUILayout.Label("Jev's answer", _text); GUILayout.Label(p.Response, _mono); }
                GUILayout.EndScrollView();
            }
        }

        // ---- pieces ------------------------------------------------------------------------------------------

        private void Section(string title)
        {
            GUILayout.Space(8);
            GUILayout.Label(title, _text);
        }

        private void Stat(string label, string value)
        {
            GUILayout.BeginVertical(GUILayout.Width(120));
            GUILayout.Label(value, _title);
            GUILayout.Label(label, _dim);
            GUILayout.EndVertical();
        }

        private void Stepper(string label, string value, Action down, Action up)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _text, GUILayout.Width(170));
            if (GUILayout.Button("-", _button, GUILayout.Width(32), GUILayout.Height(24))) _pending = down;
            GUILayout.Label(value, _text, GUILayout.Width(110));
            if (GUILayout.Button("+", _button, GUILayout.Width(32), GUILayout.Height(24))) _pending = up;
            GUILayout.EndHorizontal();
        }

        private bool Toggle(string label, bool on) => GUILayout.Button(label, on ? _buttonOn : _button, GUILayout.Height(28));

        private void Bar(string label, float fraction, Color color, float height = 22f)
        {
            Rect r = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true));
            Rounded(r, new Color(0.18f, 0.15f, 0.12f), 4f);
            Rounded(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fraction), r.height), color, 4f);
            GUI.Label(new Rect(r.x + 8, r.y + 1, r.width, r.height), label, _text);
        }

        private static void DrawIcon(Rect r, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null || Event.current.type != EventType.Repaint) return;
            Texture2D tex = sprite.texture;
            Rect t = sprite.textureRect;
            GUI.DrawTextureWithTexCoords(r, tex, new Rect(t.x / tex.width, t.y / tex.height, t.width / tex.width, t.height / tex.height));
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

        private GUIStyle RowLook(Color fill, Color hover, Color border)
        {
            GUIStyle s = ButtonLook(fill, hover, border);
            s.alignment = TextAnchor.MiddleLeft; s.fontStyle = FontStyle.Normal; s.fontSize = 12; s.margin = new RectOffset(0, 0, 1, 1);
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
            _small = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.LowerRight, fontStyle = FontStyle.Bold };
            _small.normal.textColor = new Color(0.95f, 0.92f, 0.86f);
            _mono = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            try { _mono.font = Font.CreateDynamicFontFromOSFont("Consolas", 12); } catch (Exception) { }
            _mono.normal.textColor = new Color(0.8f, 0.9f, 0.85f);
            _good = new GUIStyle(_dim); _good.normal.textColor = new Color(0.6f, 0.9f, 0.6f);
            _warn = new GUIStyle(_dim); _warn.normal.textColor = new Color(1f, 0.75f, 0.35f);
            _bad = new GUIStyle(_dim); _bad.normal.textColor = new Color(1f, 0.55f, 0.4f);
            _button = ButtonLook(new Color(0.22f, 0.19f, 0.15f), new Color(0.33f, 0.27f, 0.18f), new Color(0.45f, 0.36f, 0.2f));
            _buttonOn = ButtonLook(new Color(0.55f, 0.42f, 0.16f), new Color(0.62f, 0.48f, 0.2f), new Color(0.95f, 0.78f, 0.35f));
            _row = RowLook(new Color(0.1f, 0.09f, 0.07f), new Color(0.2f, 0.17f, 0.12f), new Color(0.16f, 0.13f, 0.09f));
            _rowOn = RowLook(new Color(0.3f, 0.24f, 0.12f), new Color(0.36f, 0.28f, 0.14f), Gold);
            _field = new GUIStyle(GUI.skin.textField) { fontSize = 15, padding = new RectOffset(6, 6, 4, 4) };
            _field.normal.background = _field.focused.background = _field.hover.background = MakeBox(new Color(0.12f, 0.1f, 0.08f), new Color(0.45f, 0.36f, 0.2f));
            _field.normal.textColor = _field.focused.textColor = _field.hover.textColor = new Color(0.95f, 0.92f, 0.86f);
        }

        private void DestroyMenuResources()
        {
            foreach (Texture2D t in _textures) if (t != null) Destroy(t);
            _textures.Clear();
            _title = _text = _dim = _button = _buttonOn = _good = _warn = _bad = _field = _row = _rowOn = _small = _mono = null;
        }
    }
}
