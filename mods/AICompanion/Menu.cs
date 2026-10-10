using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AICompanion
{
    /// <summary>
    /// The companion menu for other mods and tools: Claude Tools' "shoot close" closes it by reflection ("AICompanion.Menu:Close"), as
    /// Escape does. (Inside this mod the game's own Menu class is global::Menu.)
    /// </summary>
    public static class Menu
    {
        public static bool IsOpen => Plugin.MenuOpen;
        public static void Close() => Plugin.CloseFromEscape();
    }

    /// <summary>
    /// The companion's menu (J, or E on it), in the game's own look (GameUi.cs: the crafting panel's wood, its buttons and tabs, the
    /// inventory's item cells and tooltips, the game's fonts), in seven tabs:
    ///   Status - what it is doing, its health, food and rest, effects and recent events;
    ///   Stats  - all its numbers: health, stamina, healing, carry weight, armour, block, weapon damage, resistances, record, every skill;
    ///   Orders - come with me or go home (and stay, guard, come here), errands, how it fights, its habits, sending it away;
    ///   Home   - its bed and chests, missions, living at home, home duties (in order), its food, jobs, what it gathered, while you are away;
    ///   Gear   - what it wears, its bag, giving it things, and what it picks up off the ground;
    ///   Looks  - body, hair, beard, skin and hair colour;
    ///   Journal - its life so far: days with you, kills, parries, bosses, deaths, gear made, and the moments worth remembering.
    /// With no companion of yours nearby it shows your companions (also those far away) and the summon panel.
    ///
    /// How it is drawn: the same drawing code runs in two ways. A few times a second it only writes down what the window would show (a
    /// signature); when that differs from what is on screen, it runs again and builds the window. Numbers that change all the time (health,
    /// stamina, timers, skill bars, the portrait) are not in the signature: they are changed in place ten times a second.
    /// </summary>
    public partial class Plugin
    {
        private enum Tab { Status, Stats, Orders, Home, Gear, Looks, Journal }

        internal static bool MenuOpen;
        internal static float MenuClosedAt = -1f;   // (the E that closes it must not open it again at once: Container_Interact)
        private Humanoid _shown;          // null: your companions and the summon panel
        private long _rebind;             // a companion being remade (new body): show it again when it is back
        private float _rebindUntil;
        private Tab _tab = Tab.Status;
        private bool _renaming, _focusField, _menuWarned;
        private Action _pending;          // button actions run in Update, never while the window is being built or clicked
        private string _nameField = "", _note = "";
        private long _forgetArmed;        // "Forget" pressed once (a second press forgets)

        private const float WindowW = 720f, Pad = 26f, CardPad = 14f, CardGap = 10f;
        private static readonly Color HealthRed = new Color(0.9f, 0.25f, 0.18f), StaminaYellow = new Color(1f, 0.78f, 0.22f), EitrPurple = new Color(0.62f, 0.46f, 1f);

        private RectTransform _window, _list, _card;
        private TMP_InputField _field;
        private string _drawn = "", _scrollKey = "";
        private float _nextCheck, _nextLive, _openedAt;
        private readonly List<Action> _live = new List<Action>();   // things changed in place
        // while drawing: making the window (true) or only writing its signature (false), where things go and the next free line there
        private bool _make;
        private readonly StringBuilder _sig = new StringBuilder();
        private Transform _in;
        private float _y, _left, _width, _listY, _cardW;

        internal void OpenMenuFor(Player player, Humanoid companion)
        {
            if (!GameUi.Ready()) return;   // (the window is made from the inventory screen's parts: not there outside a world)
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            if (!MenuOpen) _openedAt = Time.unscaledTime;
            _shown = companion;
            _nameField = companion != null ? Companion.NameOf(companion) : (player.m_customData.TryGetValue("dhc_name", out string n) ? n : "Rádvar");
            _note = companion == null ? "" : (Companion.IsMine(companion, player) ? "" : $"This is {Companion.Zdo(companion).GetString(Keys.MasterName, "someone")}'s companion.");
            _renaming = false;
            MenuOpen = true;
            GameUi.IsOpen = () => MenuOpen;            // the input patches in GameUi.cs: no walking, attacking or zooming under it
            GameUi_EscapeCloses.Close = CloseFromEscape;
            try { Rebuild(); }
            catch (Exception e) { MenuFailed(e); }   // (never into the game's Interact, which called this)
        }

        private void MenuFailed(Exception e)
        {
            if (!_menuWarned) { _menuWarned = true; Warn("Companion menu: " + e); }   // (once: not every frame)
            CloseMenu();
        }

        internal static void CloseFromEscape() => Instance?.CloseMenu();

        private void CloseMenu()
        {
            bool was = MenuOpen;
            MenuOpen = false;
            _shown = null;
            _pending = null;
            _renaming = false;
            if (_window != null) Object.Destroy(_window.gameObject);
            _window = _list = _card = null;
            _field = null;
            _drawn = "";
            _live.Clear();
            if (was) { MenuClosedAt = Time.unscaledTime; UITooltip.HideTooltip(); }
        }

        private void UpdateMenu(Player player)
        {
            if (!MenuOpen) return;
            Action run = _pending;
            _pending = null;
            run?.Invoke();
            if (!MenuOpen) return;
            if (_window == null) { CloseMenu(); return; }   // the game rebuilt its interface (a world change)
            if (_rebind != 0L && _shown == null)
            {
                Humanoid back = Companion.All().FirstOrDefault(h => Companion.IdOf(h) == _rebind);
                if (back != null) { _shown = back; _rebind = 0L; }
                else if (Time.time > _rebindUntil) _rebind = 0L;
            }
            else if (_shown != null && (_shown.IsDead() || Vector3.Distance(_shown.transform.position, player.transform.position) > 100f)) { CloseMenu(); return; }
            else if (global::Menu.IsVisible() || InventoryGui.IsVisible()) { CloseMenu(); return; }
            // E closes it again, as it does a chest (not the E that opened it, nor one typed into a name)
            if (!GameUi.Typing && Time.unscaledTime > _openedAt + 0.2f && ZInput.GetButtonDown("Use")) { CloseMenu(); return; }
            TickMenu();
        }

        private bool Mine => _shown != null && Companion.IsMine(_shown, Player.m_localPlayer);
        private bool Commandable => _shown != null && Companion.CanCommand(_shown, Player.m_localPlayer);

        private void Change(Action<ZDO> change)
        {
            if (!Commandable) { _note = "Only its owner can give it orders (unless they let friends do so)."; return; }
            if (!Companion.Write(_shown, change)) _note = "Someone has its things open. Try again in a moment.";
        }

        /// <summary>A name field in the menu has the keyboard: the game's keys (M for the map, Tab for the inventory) must not act on what you type.</summary>
        internal static bool TypingInMenu => MenuOpen && GameUi.Typing;

        // ==== keeping it up to date ========================================================================

        private void TickMenu()
        {
            float now = Time.unscaledTime;
            // not while a name is being typed, a slider dragged or the mouse button held (a click half done): building again would take the
            // field, slider or button from under the keyboard or the mouse
            if (now >= _nextCheck && !GameUi.Typing && !GameUi.Sliding && !Input.GetMouseButton(0))
            {
                _nextCheck = now + 0.25f;
                try
                {
                    Draw(false);
                    if (_sig.ToString() != _drawn) Rebuild();
                }
                catch (Exception e) { MenuFailed(e); return; }
            }
            if (now >= _nextLive)
            {
                _nextLive = now + 0.1f;
                try { foreach (Action a in _live) a(); }
                catch (Exception) { _nextCheck = 0f; }   // (its companion just went: the window is built again without it)
            }
        }

        private void Rebuild()
        {
            Draw(false);
            string sig = _sig.ToString();
            Draw(true);
            _drawn = sig;
        }

        /// <summary>The whole window: built when <paramref name="make"/>, else only its signature written.</summary>
        private void Draw(bool make)
        {
            _make = make;
            _sig.Length = 0;
            Humanoid c = _shown;   // (Unity's null: a companion just remade or unloaded shows your companions instead)
            bool companion = c != null;
            Vector2 screen = GameUi.ScreenSize;
            float w = Mathf.Min(WindowW, screen.x - 20f), h = Mathf.Min(companion ? 900f : 680f, screen.y - 40f);
            string key = (companion ? Companion.IdOf(c).ToString() : "list") + "/" + _tab;
            _sig.Append(key).Append('|').Append((int)w).Append('x').Append((int)h).Append('|').Append(!companion && _rebind != 0L);
            float scrolled = 0f;
            if (make)
            {
                if (_list != null && key == _scrollKey) scrolled = _list.anchoredPosition.y;   // (kept, unless the tab or companion changed)
                _scrollKey = key;
                if (_window != null) Object.Destroy(_window.gameObject);
                UITooltip.HideTooltip();
                _live.Clear();
                _field = null;
                _list = _card = null;
                _window = GameUi.Window("AICompanionMenu", new Vector2(w, h));
            }
            _in = _window; _left = Pad; _width = w - Pad * 2f;

            if (!companion && _rebind != 0L)
            {
                Title("Changing its body...", w);
                return;
            }
            float top = companion ? Header(c, w) : ListHeader(w);
            if (make)
            {
                RectTransform box = GameUi.Box(_window, new Vector2(Pad - 6f, -top), new Vector2(w - Pad * 2f + 12f, h - top - Pad + 8f));
                _list = GameUi.Scroll(box, 8f);
                _cardW = _list.sizeDelta.x - 2f;
            }
            _listY = 0f;
            if (!companion) DrawCompanions();
            else
                switch (_tab)
                {
                    case Tab.Status: DrawStatus(c); break;
                    case Tab.Stats: DrawStats(c); break;
                    case Tab.Orders: DrawOrders(c); break;
                    case Tab.Home: DrawHome(c); break;
                    case Tab.Gear: DrawGear(c); break;
                    case Tab.Looks: DrawLooks(c); break;
                    case Tab.Journal: DrawJournal(c); break;
                }
            if (make)
            {
                float content = Mathf.Max(0f, _listY - CardGap);   // (no gap after the last card: it would scroll for nothing)
                _list.sizeDelta = new Vector2(_list.sizeDelta.x, content);
                float view = ((RectTransform)_list.parent).sizeDelta.y;
                _list.anchoredPosition = new Vector2(0f, Mathf.Clamp(scrolled, 0f, Mathf.Max(0f, content - view)));
                if (_focusField && _field != null) { _field.Select(); _field.ActivateInputField(); }
                _focusField = false;
            }
        }

        private void Title(string text, float w)
        {
            _sig.Append("|T").Append(text);
            if (_make) GameUi.Label(_window, text, new Vector2(Pad, -20f), new Vector2(w - Pad * 2f - 130f, 44f), 34f, GameUi.Warm, GameUi.Norse);
            Btn("Close", w - Pad - 110f, 22f, 110f, 36f, () => _pending = CloseMenu);
            if (_make) GameUi.Braid(_window, new Vector2(0f, -76f), w - Pad * 2f);
        }

        // ==== your companions, and summoning =================================================================

        private float ListHeader(float w)
        {
            Title("Companions", w);
            _y = 92f;
            if (!string.IsNullOrEmpty(_note)) Note(_note, GameUi.Warn);
            return _y + 6f;
        }

        private void DrawCompanions()
        {
            Player me = Player.m_localPlayer;
            List<Profile> mine = me != null ? Profile.Here(me) : new List<Profile>();

            foreach (Profile prof in mine)
            {
                BeginCard();
                Humanoid here = Companion.All().FirstOrDefault(h => Companion.IdOf(h) == prof.Id);
                if (here != null)
                {
                    float d = me != null ? Vector3.Distance(me.transform.position, here.transform.position) : 0f;
                    var buttons = new List<(string, bool, Action)>();
                    if (d > 15f) buttons.Add(("Call to me", false, () => { Humanoid h2 = here; _pending = () => { if (Home.Follow(h2)) Brain.TeleportBehind(h2, Player.m_localPlayer, "was called to"); }; }));
                    buttons.Add(("Open", true, () => { Humanoid h2 = here; _pending = () => OpenMenuFor(Player.m_localPlayer, h2); }));
                    HeadingRow(prof.Name, buttons.ToArray());
                    string doing = Companion.StatusOf(here);
                    Note((d <= 15f ? "Here with you" : Remote.Where(here.transform.position, me.transform.position)) + (string.IsNullOrEmpty(doing) ? "." : $": {doing}."), GameUi.Good);
                }
                else if (prof.Dead)
                {
                    Heading(prof.Name);
                    Profile p0 = prof;
                    LiveNote(() => $"Has fallen. Wakes {(p0.HasBed ? "in their bed" : "beside you")} in {Mathf.Max(0f, (float)(p0.DiedAt + RespawnSeconds.Value - ZNet.instance.GetTimeSeconds())):0} s.", GameUi.Warn);
                    Note("Their things are in their tombstone (the skull on your map).", GameUi.Warn);
                    WideBtn($"Wake {prof.Name} now", true, () => { Profile p2 = prof; _pending = () => { Humanoid c = Home.Respawn(Player.m_localPlayer, p2); if (c != null) OpenMenuFor(Player.m_localPlayer, c); else if (p2.Dead || Remote.InWorld(p2.Id) == null) _note = $"Asking the host whether {p2.Name} is up somewhere: try again in a moment."; }; });
                }
                else
                {
                    // Away (its area not loaded here): what the world knows of it, and calling it to you.
                    ZDO z = Remote.Find(prof.Id);
                    if (z != null) HeadingRow(prof.Name, ("Call to me", true, () => { ZDO z2 = z; _pending = () => { if (Remote.Call(z2, Player.m_localPlayer)) _note = $"{prof.Name} is on the way: they appear beside you in a moment."; }; }));
                    else Heading(prof.Name);
                    if (z != null)
                    {
                        Vector3 at = Remote.Position(z);
                        string doing = Remote.Status(z);
                        string where = (me != null ? Remote.Where(at, me.transform.position) : "") + (Remote.AtHome(z) ? ", at home" : "");
                        string order = Remote.OrderOf(z) == AICompanion.Order.Gather ? "living at home" : Remote.OrderOf(z) == AICompanion.Order.Follow ? "with you (left behind)" : Remote.OrderOf(z) == AICompanion.Order.Guard ? "guarding a spot" : "staying put";
                        Note($"{Capital(where)} ({order}).", GameUi.Text);
                        if (!string.IsNullOrEmpty(doing)) Note($"Last doing: {doing}.", GameUi.Good);
                        foreach (string line in Remote.Journal(z, 2)) Note("· " + line, GameUi.Dim);
                    }
                    else if (Remote.InWorld(prof.Id) == false)
                    {
                        // Lost: nothing of them is in the world, yet they never woke (their fall was not kept). They come back as they were.
                        Vector3? tomb = Remote.TombOf(prof.Id);
                        Note($"{prof.Name} is nowhere in the world: they fell and never woke up. Bring them back: they wake {(prof.HasBed ? "in their bed" : "beside you")}, with their skills, looks and settings. "
                             + (tomb != null && me != null ? $"Their things are in their tombstone, {Remote.Where(tomb.Value, me.transform.position)}: near it they put their gear back on." : "Their things are in their tombstone where they fell."), GameUi.Warn);
                        WideBtn($"Bring {prof.Name} back", true, () => { Profile p2 = prof; _pending = () => { Humanoid c = Home.Respawn(Player.m_localPlayer, p2); if (c != null) OpenMenuFor(Player.m_localPlayer, c); else if (p2.Dead || Remote.InWorld(p2.Id) == null) _note = $"Asking the host whether {p2.Name} is up somewhere: try again in a moment."; }; });
                        Note("Or, if they are gone for good, forget them:", GameUi.Dim);
                        WideBtn(_forgetArmed == prof.Id ? $"Click again to forget {prof.Name}" : $"Forget {prof.Name}", false, () =>
                        {
                            long id = prof.Id;
                            if (_forgetArmed == id) _pending = () => { Profile.Forget(Player.m_localPlayer, id); _forgetArmed = 0L; };
                            else _forgetArmed = id;
                        }, 32f, _forgetArmed == prof.Id ? GameUi.Warn : (Color?)null);
                    }
                    else
                        Note($"Out in the world, last seen {Remote.Where(prof.LastSeen, me != null ? me.transform.position : prof.LastSeen)} (far from you, so this game does not see them; the host's does).", GameUi.Text);
                }
                EndCard();
            }

            BeginCard();
            if (mine.Count >= MaxCompanions.Value)
            {
                Note($"You have {mine.Count} companions, the most allowed (settings: MaxCompanions).", GameUi.Dim);
                EndCard();
                return;
            }
            Heading(mine.Count == 0 ? "Summon a companion" : "Summon another companion");
            Note("A viking who comes on adventures with you and, when you send them home, lives a life of their own at your base. They take a free bed and empty chests near them by themselves. You choose how they look next.", GameUi.Dim);
            _y += 4f;
            NameField(null);
            if (mine.Count > 0) Note("This makes a new companion, starting from nothing. To see one you already have, go to where they are.", GameUi.Warn);
            WideBtn(mine.Count == 0 ? "Summon" : "Summon a new companion", true, () =>
                _pending = () =>
                {
                    // Not a second one by the same name: someone looking for theirs pressed this, and got a new one called the same.
                    string wanted = string.IsNullOrEmpty(_nameField?.Trim()) ? "Rádvar" : _nameField.Trim();
                    Profile same = Profile.Here(Player.m_localPlayer).FirstOrDefault(pr => string.Equals(pr.Name, wanted, StringComparison.OrdinalIgnoreCase));
                    if (same != null) { _note = $"You already have a companion called {same.Name}. Give the new one another name, or go to {same.Name}."; return; }
                    Player p = Player.m_localPlayer;
                    Humanoid c = p != null ? Companion.Summon(p, (_nameField ?? "").Trim()) : null;
                    if (c == null) return;
                    _tab = Tab.Looks;
                    OpenMenuFor(p, c);
                    string took = Home.SetUp(c);
                    Talk.Tell(c, took != null ? $"Hello! {took}" : "Hello! Give me a bed and a chest and I'll make my home here.");
                }, 38f);
            EndCard();
        }

        /// <summary>The name field of the summon panel: "Name" and the game's own text field.</summary>
        private void NameField(Action submit)
        {
            _sig.Append("|f");   // (not what is typed: the window isn't built again while typing anyway)
            if (_make)
            {
                GameUi.Label(_in, "Name", new Vector2(_left, -_y - 8f), new Vector2(64f, 24f), 17f, GameUi.Text).fontStyle = FontStyles.Bold;
                _field = GameUi.TextField(_in, new Vector2(_left + 70f, -_y), new Vector2(Mathf.Min(320f, _width - 70f), 38f), _nameField, 24, v => _nameField = v,
                    submit == null ? (Action<string>)null : _ => submit());
            }
            _y += 46f;
        }

        // ==== the companion: the top of the window ==========================================================

        private float Header(Humanoid c, float w)
        {
            Player pl = Player.m_localPlayer;
            long id = Companion.IdOf(c);
            string name = Companion.NameOf(c);
            bool owner = c.GetComponent<ZNetView>().IsOwner();
            float x = Pad + 94f, closeX = w - Pad - 110f;
            _sig.Append("|H").Append(name).Append(_renaming).Append(Mine).Append(owner);

            // its face (its initial until there is a picture), name (or renaming it), what it is doing, close
            if (_make)
            {
                RawImage face = GameUi.Picture(_window, new Vector2(Pad, -18f), new Vector2(78f, 78f), Portraits.Get(id));
                TMP_Text initial = GameUi.Label(face.transform.parent, name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?", Vector2.zero, new Vector2(78f, 78f), 40f,
                    GameUi.Dim, GameUi.Norse, TextAlignmentOptions.Center);
                initial.transform.SetSiblingIndex(0);
                initial.enabled = face.texture == null;
                _live.Add(() =>
                {
                    Texture t = Portraits.Get(id);
                    if (face.texture != t) face.texture = t;
                    face.enabled = t != null;
                    initial.enabled = t == null;
                });
            }
            Btn("Close", closeX, 18f, 110f, 36f, () => _pending = CloseMenu);
            if (_renaming)
            {
                if (_make) _field = GameUi.TextField(_window, new Vector2(x, -20f), new Vector2(240f, 38f), _nameField, 24, v => _nameField = v, _ => SaveName(), 20f);
                Btn("Save", x + 248f, 20f, 80f, 38f, SaveName, true);
                Btn("Cancel", x + 334f, 20f, 90f, 38f, () => { _renaming = false; _nameField = Companion.NameOf(_shown); });
            }
            else
            {
                float nameW = 0f, room = closeX - x - 120f;
                if (_make)
                {
                    TMP_Text t = GameUi.Label(_window, name, new Vector2(x, -14f), new Vector2(room, 44f), 32f, GameUi.Warm, GameUi.Norse);
                    t.enableWordWrapping = false;
                    t.overflowMode = TextOverflowModes.Ellipsis;
                    nameW = Mathf.Min(t.GetPreferredValues(name, 2000f, 0f).x, room);
                }
                if (Mine) Btn("Rename", x + nameW + 14f, 22f, 96f, 30f, () => { _renaming = true; _focusField = true; _nameField = Companion.NameOf(_shown); }, size: 16f);
            }
            if (_make)
            {
                TMP_Text status = Line(_window, x, 62f, closeX - x - 8f, 18f, GameUi.Good);
                _live.Add(() => { string s = Companion.StatusOf(c); Set(status, string.IsNullOrEmpty(s) ? "Idle" : Capital(s)); });
            }
            if (owner)
            {
                BrainState js = Brain.Get(c);
                Work.JobText(js, out float jobDone);   // (only whether it has a progress bar: the words change in place)
                bool stoppable = js.Area != null || js.PickQueue.Count > 0 || js.Task != null && js.Task.Ordered;
                _sig.Append("|J").Append(jobDone >= 0f).Append(stoppable && Mine);
                float jobW = closeX - x - (stoppable && Mine ? 150f : 8f) + 110f;
                if (_make)
                {
                    TMP_Text job = Line(_window, x, 88f, jobW, 16f, GameUi.Text);
                    RectTransform bar = jobDone >= 0f ? GameUi.Bar(_window, new Vector2(x, -110f), new Vector2(Mathf.Min(jobW, 400f), 10f), jobDone, GameUi.Good, null) : null;
                    _live.Add(() =>
                    {
                        Set(job, "Job: " + Work.JobText(Brain.Get(c), out float done));
                        if (bar != null) GameUi.SetBar(bar, done);
                    });
                }
                if (stoppable && Mine) Btn("Stop this job", w - Pad - 140f, 84f, 140f, 30f, () => _pending = () => Work.StopJob(Brain.Get(_shown)), size: 15f);
            }

            // health, stamina, eitr
            _y = 128f;
            LiveBar(() => c.GetHealth() / Mathf.Max(1f, c.GetMaxHealth()), () => $"Health  {c.GetHealth():0} / {c.GetMaxHealth():0}", HealthRed);
            LiveBar(() => Stamina.Get(c) / Mathf.Max(1f, Stamina.Max(c)), () => $"Stamina  {Stamina.Get(c):0} / {Stamina.Max(c):0}", StaminaYellow);
            bool eitr = Eitr.Max(c) > 0f;
            _sig.Append(eitr);
            if (eitr) LiveBar(() => Eitr.Get(c) / Mathf.Max(1f, Eitr.Max(c)), () => $"Eitr  {Eitr.Get(c):0} / {Eitr.Max(c):0}", EitrPurple);
            if (!string.IsNullOrEmpty(_note)) Note(_note, GameUi.Warn);

            // your other companions near you
            var yours = Companion.All().Where(h => Companion.IsMine(h, pl) && Vector3.Distance(h.transform.position, pl.transform.position) < 100f).ToList();
            if (yours.Count > 1)
            {
                float bw = Mathf.Min(170f, (_width - 6f * (yours.Count - 1)) / yours.Count);
                for (int i = 0; i < yours.Count; i++)
                {
                    Humanoid pick = yours[i];
                    TabBtn(Companion.NameOf(pick), _left + i * (bw + 6f), _y, bw, 32f, pick == c, () => _pending = () => OpenMenuFor(pl, pick), 16f);
                }
                _y += 38f;
            }

            // the tabs, and the braid under them
            _y += 4f;
            var tabs = (Tab[])Enum.GetValues(typeof(Tab));
            float tw = (_width - 6f * (tabs.Length - 1)) / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
            {
                Tab t = tabs[i];
                TabBtn(t.ToString(), _left + i * (tw + 6f), _y, tw, 38f, _tab == t, () => _tab = t, 17f);
            }
            _y += 42f;
            if (_make) GameUi.Braid(_window, new Vector2(0f, -_y - 4f), _width);
            return _y + 14f;
        }

        private void SaveName()
        {
            string n = (_nameField ?? "").Trim();
            if (n.Length == 0) return;
            _pending = () => { Change(z => z.Set(Keys.Name, n)); Player.m_localPlayer.m_customData["dhc_name"] = n; _renaming = false; };
        }

        // ==== Status =======================================================================================

        private void DrawStatus(Humanoid c)
        {
            BrainState st = Brain.Get(c);
            Decision d = st.Current;
            bool here = c.GetComponent<ZNetView>().IsOwner(); // its brain runs on this game, so the details are here

            BeginCard("Now");
            if (!here) Note("Its brain runs on the game of the player nearest to it; the details show there.", GameUi.Dim);
            else if (st.InCombat)
            {
                Note("Fighting: " + string.Join(", ", st.Enemies.Where(e => e != null).Select(st.Label)), GameUi.Text, bold: true);
                Note(Capital(d.Describe(st.Label)), GameUi.Good);
                if (!string.IsNullOrEmpty(d.Note)) Note(Capital(d.Note), GameUi.Dim);
            }
            else Note($"Not fighting. It acts when an enemy comes within {EngageRange.Value:0} m of it or you.", GameUi.Dim);
            _y += 4f;
            OrderButtons(c);
            EndCard();

            BeginCard("Food and rest");
            if (!here) Note("Shows on the game that runs it.", GameUi.Dim);
            else
            {
                // its three meals in the inventory's own cells: minutes left, and the bar how much of it is left
                List<Food.Meal> meals = Food.Meals(c);
                float s = GameUi.CellSpace, top = _y;
                for (int i = 0; i < 3; i++)
                {
                    if (i < meals.Count) AppendItem(meals[i].Item).Append(Mathf.CeilToInt(meals[i].Time / 60f));
                    if (!_make) continue;
                    var pos = new Vector2(_left + i * s, -top);
                    if (i >= meals.Count) { GameUi.EmptyCell(_in, pos); continue; }
                    InventoryElement e = GameUi.Cell(_in, pos, meals[i].Item, $"{Mathf.CeilToInt(meals[i].Time / 60f)}m", false, null);
                    GuiBar left = e.m_durability;
                    left.gameObject.SetActive(true);
                    int k = i;
                    void Show() { List<Food.Meal> now = Food.Meals(c); if (k < now.Count) { left.SetValue(now[k].Fraction); left.SetColor(now[k].Fraction < 0.2f ? GameUi.Warn : GameUi.Good); } }
                    Show();
                    _live.Add(Show);
                }
                if (meals.Count == 0) Note($"Hungry: only {BaseHealth.Value:0} health and {BaseStamina.Value:0} stamina, and it does not heal. Give it food (Gear tab); it eats by itself.", GameUi.Warn, indent: 3f * s + 10f);
                else Note(string.Join(", ", meals.Select(m => $"{Loc(m.Item.m_shared.m_name)} ({Mathf.CeilToInt(m.Time / 60f)} min)")) + ". It eats again when a food is half gone.", GameUi.Text, indent: 3f * s + 10f);
                _y = Mathf.Max(_y, top + 64f) + 8f;
                if (Rest.IsRested(c)) Note($"Rested for {Mathf.CeilToInt(Rest.Left(c) / 60f)} more min: heals and gets its breath back faster.", GameUi.Good);
                else Note("Not rested. A while under a roof by a fire makes it rested.", GameUi.Dim);
            }
            EndCard();

            BeginCard("Effects");
            var effects = here ? c.GetSEMan().GetStatusEffects().Where(se => se != null && se.m_icon != null).ToList() : new List<StatusEffect>();
            if (!here) Note("Shows on the game that runs it.", GameUi.Dim);
            else if (effects.Count == 0) Note("None. Your boss power reaches it too when it is within 10 m of you.", GameUi.Dim);
            else
            {
                foreach (StatusEffect se in effects.Take(9)) _sig.Append("|e").Append(se.m_name);
                if (_make)
                {
                    int i = 0;
                    foreach (StatusEffect se in effects.Take(9))
                    {
                        float ex = _left + i++ * 62f;
                        GameUi.Icon(_in, new Vector2(ex + 9f, -_y), new Vector2(40f, 40f), se.m_icon);
                        TMP_Text time = Line(_in, ex, _y + 42f, 58f, 14f, GameUi.Text, TextAlignmentOptions.Center);
                        StatusEffect effect = se;
                        void Show() { float left = effect.GetRemaningTime(); Set(time, left > 0f ? $"{Mathf.FloorToInt(left / 60f)}:{Mathf.FloorToInt(left % 60f):00}" : ""); }
                        Show();
                        _live.Add(Show);
                    }
                    _y += 66f;
                }
                Note(string.Join(", ", effects.Select(se => Loc(se.m_name))), GameUi.Dim);
            }
            EndCard();

            BeginCard("Recently");
            if (st.History.Count == 0) Note("Nothing yet.", GameUi.Dim);
            foreach (string line in st.History.Take(8)) Note(Capital(line), GameUi.Text);
            EndCard();
        }

        /// <summary>The two orders that matter (come with me, go home), big; stay, guard and come here smaller beneath them.</summary>
        private void OrderButtons(Humanoid c)
        {
            Order order = Companion.OrderOf(c);
            float half = (_width - 8f) / 2f;
            Btn("Come with me", _left, _y, half, 42f, () => _pending = () => GiveOrder(h => Home.Follow(h)), order == Order.Follow, size: 19f);
            Btn("Go home", _left + half + 8f, _y, half, 42f, () => _pending = () => GiveOrder(h => Home.GoHome(h)), order == Order.Gather, size: 19f);
            _y += 48f;
            if (_make) GameUi.Label(_in, "More", new Vector2(_left, -_y - 6f), new Vector2(56f, 24f), 16f, GameUi.Dim);
            float third = (_width - 62f - 12f) / 3f, bx = _left + 62f;
            Btn("Stay here", bx, _y, third, 32f, () => _pending = () => Change(z => z.Set(Keys.Order, (int)AICompanion.Order.Stay)), order == Order.Stay, size: 16f);
            Btn("Guard this spot", bx + third + 6f, _y, third, 32f,
                () => _pending = () => Change(z => { z.Set(Keys.Order, (int)AICompanion.Order.Guard); z.Set(Keys.Post, c.transform.position); }), order == Order.Guard, size: 16f);
            Btn("Come here", bx + (third + 6f) * 2f, _y, third, 32f, () => _pending = ComeHere, size: 16f);
            _y += 38f;
            Note($"Or hold {MenuKey.Value} anywhere: all your companions near you come with you, or go home.", GameUi.Dim);
        }

        private void GiveOrder(Func<Humanoid, bool> order)
        {
            if (!Commandable) { _note = "Only its owner can give it orders (unless they let friends do so)."; return; }
            if (!order(_shown)) _note = "Someone has its things open. Try again in a moment.";
        }

        // ==== Stats ========================================================================================

        private void DrawStats(Humanoid c)
        {
            BrainState st = Brain.Get(c);
            bool here = c.GetComponent<ZNetView>().IsOwner();
            List<Food.Meal> meals = Food.Meals(c);

            BeginCard("Body");
            Stats(("Health", null, () => $"{c.GetHealth():0}/{c.GetMaxHealth():0}"),
                  ("Stamina", null, () => $"{Stamina.Get(c):0}/{Stamina.Max(c):0}"),
                  ("Eitr", null, () => Eitr.Max(c) > 0f ? $"{Eitr.Get(c):0}/{Eitr.Max(c):0}" : "-"),
                  ("Carrying", null, () => $"{Carry.Weight(c):0}/{Carry.Max(c):0}"));
            float regen = meals.Sum(m => m.Item.m_shared.m_foodRegen) * (Rest.IsRested(c) ? 1.5f : 1f);
            Note($"Before food: {BaseHealth.Value:0} health and {BaseStamina.Value:0} stamina, as a player's. Food adds the rest.", GameUi.Dim);
            Note(regen > 0f ? $"Heals {regen:0.#} health every 10 s from its food{(Rest.IsRested(c) ? " (rested: 50% more)" : "")}." : "Heals nothing: it has not eaten (a player only heals from food).", regen > 0f ? GameUi.Text : GameUi.Warn);
            EndCard();

            BeginCard("Food");
            if (!here) Note("Shows on the game that runs it.", GameUi.Dim);
            else if (meals.Count == 0) Note("Nothing eaten.", GameUi.Warn);
            foreach (Food.Meal m in meals)
                Note($"{Loc(m.Item.m_shared.m_name)}:  +{m.Item.m_shared.m_food:0} health, +{m.Item.m_shared.m_foodStamina:0} stamina{(m.Item.m_shared.m_foodEitr > 0f ? $", +{m.Item.m_shared.m_foodEitr:0} eitr" : "")}, heals {m.Item.m_shared.m_foodRegen:0.#}  ·  {Mathf.CeilToInt(m.Time / 60f)} min left", GameUi.Text);
            EndCard();

            BeginCard("Fighting");
            ItemDrop.ItemData melee = Companion.BestMelee(c), bow = Companion.BestRanged(c), shield = Companion.Shield(c);
            Stats(("Armour", $"{Companion.Armor(c):0}", null),
                  ("Block", shield != null ? $"{shield.GetBlockPower(Skill.Get(c, Skills.SkillType.Blocking) / 100f):0}" : melee != null ? $"{melee.GetBlockPower(Skill.Get(c, Skills.SkillType.Blocking) / 100f):0}" : "-", null),
                  ("Kills", Companion.Zdo(c).GetInt(Keys.Kills, 0).ToString(), null),
                  ("Fights", here ? st.Fights.ToString() : "-", null));
            if (melee != null) Note($"{Loc(melee.m_shared.m_name)}: {Damage(c, melee)} damage a hit (with its {Loc("$skill_" + melee.m_shared.m_skillType.ToString().ToLower())} skill)", GameUi.Text);
            else Note("No weapon: it fights with its fists.", GameUi.Warn);
            if (bow != null) Note($"{Loc(bow.m_shared.m_name)}: {Damage(c, bow)} damage a shot", GameUi.Text);
            Note(shield != null ? $"Blocks with its {Loc(shield.m_shared.m_name)} just before a swing lands (a parry, once it has seen that creature swing), then hits back; steps aside from sweeps, shots and hits too hard to block." : melee != null ? "Blocks with its weapon when something swings at it (a shield blocks far better); steps aside from what it cannot block." : "Nothing to block with: it steps aside instead.", GameUi.Dim);
            if (here && st.Parries + st.Blocks > 0) Note($"This session: {st.Parries} parries, {st.Blocks} blocks.", GameUi.Text);
            var tally = Journal.Tally(c);
            var feared = tally.Where(kv => kv.Key.StartsWith("killedby:")).Select(kv => kv.Key.Substring(9).ToLowerInvariant()).ToList();
            if (feared.Count > 0) Note("Careful with: " + string.Join(", ", feared) + " (they have killed it before: it uses its bow on them and keeps its distance).", GameUi.Dim);
            EndCard();

            BeginCard("Its last fights");
            if (!here || st.FightLog.Count == 0) Note(here ? "None yet this session." : "Shows on the game that runs it.", GameUi.Dim);
            else foreach (string f in st.FightLog) Note(f, GameUi.Text);
            EndCard();

            BeginCard("Resistances");
            HitData.DamageModifiers mods = Gear.Modifiers(c);
            var lines = new List<string>();
            foreach (HitData.DamageType t in new[] { HitData.DamageType.Blunt, HitData.DamageType.Slash, HitData.DamageType.Pierce, HitData.DamageType.Fire, HitData.DamageType.Frost, HitData.DamageType.Lightning, HitData.DamageType.Poison, HitData.DamageType.Spirit })
            {
                HitData.DamageModifier mod = mods.GetModifier(t);
                if (mod != HitData.DamageModifier.Normal) lines.Add($"{t}: {mod.ToString().Replace("Very", "very ").ToLowerInvariant()}");
            }
            Note(lines.Count > 0 ? string.Join("   ", lines) : "None: its gear gives no resistances (and no weaknesses).", lines.Count > 0 ? GameUi.Text : GameUi.Dim);
            EndCard();

            BeginCard("Skills");
            Note("As a player's: each rises as it uses it and makes it better at it. It loses 5% of every skill when it falls.", GameUi.Dim);
            var skills = Skill.Types().Select(t => new KeyValuePair<Skills.SkillType, float>(t, Skill.Get(c, t)))
                .OrderByDescending(kv => kv.Value).ThenBy(kv => Loc("$skill_" + kv.Key.ToString().ToLower())).ToList();
            foreach (var kv in skills) _sig.Append('|').Append((int)kv.Key).Append(kv.Value >= 1f ? '+' : '-');   // (the order and which are known: the numbers change in place)
            if (_make)
            {
                const float rowH = 44f, gap = 24f;
                float colW = (_width - gap) / 2f;
                for (int i = 0; i < skills.Count; i++)
                {
                    float sx = _left + (i % 2) * (colW + gap), sy = _y + (i / 2) * rowH;
                    Skills.SkillType type = skills[i].Key;
                    TMP_Text name = Line(_in, sx, sy, colW - 50f, 16f, skills[i].Value >= 1f ? GameUi.Text : GameUi.Dim);
                    name.text = Loc("$skill_" + type.ToString().ToLower());
                    TMP_Text level = Line(_in, sx + colW - 48f, sy, 48f, 17f, GameUi.Gold, TextAlignmentOptions.TopRight);
                    RectTransform bar = GameUi.Bar(_in, new Vector2(sx, -sy - 23f), new Vector2(colW, 12f), skills[i].Value / 100f, GameUi.Gold, null);
                    RectTransform next = GameUi.Rect(_in, "Next", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(sx + 2f, -sy - 37f), new Vector2(0f, 3f));
                    Image nextImage = next.gameObject.AddComponent<Image>();
                    nextImage.color = GameUi.Good;
                    nextImage.raycastTarget = false;
                    void Show()
                    {
                        float v = Skill.Get(c, type), progress = here ? Skill.Progress(c, type) : 0f;
                        Set(level, v.ToString("0"));
                        GameUi.SetBar(bar, v / 100f);
                        var size = new Vector2((colW - 4f) * Mathf.Clamp01(progress), 3f);
                        if (next.sizeDelta != size) next.sizeDelta = size;
                    }
                    Show();
                    _live.Add(Show);
                }
                _y += (skills.Count + 1) / 2 * rowH + 4f;
            }
            Note("Gold: its level out of 100. Green: on its way to the next level.", GameUi.Dim);
            EndCard();
        }

        /// <summary>"about 14": a hit's damage with its skill (40% of the weapon's at skill 0, all of it at 100, the player's rule).</summary>
        private static string Damage(Humanoid c, ItemDrop.ItemData w) =>
            $"about {w.GetDamage().GetTotalDamage() * Mathf.Lerp(0.4f, 1f, Skill.Get(c, w.m_shared.m_skillType) / 100f):0}";

        private void ComeHere()
        {
            Player p = Player.m_localPlayer;
            if (p == null || _shown == null) return;
            GiveOrder(h => Home.Follow(h));
            if (Commandable && Vector3.Distance(p.transform.position, _shown.transform.position) > 15f && _shown.GetComponent<ZNetView>().IsOwner()) Brain.TeleportBehind(_shown, p);
        }

        // ==== Orders =======================================================================================

        private void DrawOrders(Humanoid c)
        {
            Order order = Companion.OrderOf(c);
            BeginCard("What it does");
            OrderButtons(c);
            Note(order switch
            {
                AICompanion.Order.Gather => "Lives its own life at home: works toward better gear (gathers what it needs, makes and upgrades it at your workbench and forge), stores what it finds in its chests, eats, cooks, repairs and defends the place.",
                AICompanion.Order.Stay => "Stays where it is and fights what comes near.",
                AICompanion.Order.Guard => "Stays by this spot, fights what comes near, and goes back to it after a fight.",
                _ => "Goes on adventures with you: follows you through portals and on boats, fights beside you, and picks up the loot. When its bag is full, its gear wears out or its food runs out, it tells you (and goes home by itself when home is near).",
            }, GameUi.Text);
            EndCard();

            if (Commandable && c.GetComponent<ZNetView>().IsOwner())
            {
                BeginCard("Errands");
                Note("One click: it goes and does it now, then carries on with what it was doing (near its home). Or say it in chat: \"" + Companion.NameOf(c) + ", restock\", \"gear up\", \"repair the base\"...", GameUi.Dim);
                float bw = (_width - 12f) / 3f;
                for (int k = 0; k < Errands.All.Length; k++)
                {
                    Errand pick = Errands.All[k].What;
                    Btn(Errands.All[k].Label, _left + (k % 3) * (bw + 6f), _y, bw, 32f, () => _pending = () => _note = Errands.Run(Brain.Get(_shown), pick) ?? "", size: 16f);
                    if (k % 3 == 2 || k == Errands.All.Length - 1) _y += 38f;
                }
                EndCard();
            }

            BeginCard("How it fights");
            Style style = Companion.Chosen(c);
            TabBtn("Let it decide (recommended)", _left, _y, _width, 36f, style == Style.Auto, () => _pending = () => Change(z => z.Set(Keys.Style, (int)Style.Auto)));
            _y += 42f;
            Style[] styles = { Style.Aggressive, Style.Balanced, Style.Defensive, Style.Passive };
            Choices(styles.Select(s => s.ToString()).ToArray(), Array.IndexOf(styles, style), i => { Style pick = styles[i]; _pending = () => Change(z => z.Set(Keys.Style, (int)pick)); });
            Note(style switch
            {
                Style.Aggressive => "Presses the attack and falls back only when badly hurt.",
                Style.Defensive => "Stays close to you, fights what comes near, and falls back early.",
                Style.Passive => "Never starts a fight; keeps by your side and keeps safe.",
                Style.Balanced => "Fights what threatens you or it, and falls back when hurt.",
                _ => "Reads what you are doing: fights beside you, helps you mine or chop, keeps close and only defends you while you travel, and where a place is too dangerous for its gear it tells you and only defends.",
            }, GameUi.Text);
            if (style == Style.Auto && !string.IsNullOrEmpty(Brain.Get(c)?.AutoNote)) Note("Right now: " + Brain.Get(c).AutoNote + ".", GameUi.Good);
            _y += 4f;
            int retreat = Companion.RetreatOf(c);
            Stepper("Falls back below", $"{retreat}% health", () => Change(z => z.Set(Keys.Retreat, Mathf.Clamp(retreat - 5, 0, 90))), () => Change(z => z.Set(Keys.Retreat, Mathf.Clamp(retreat + 5, 0, 90))));
            Note("Below half of that it runs.", GameUi.Dim);
            Stepper("Fights enemies within", $"{EngageRange.Value:0} m", () => { EngageRange.Value = Mathf.Clamp(EngageRange.Value - 2f, 4f, 50f); SaveSettings(); },
                    () => { EngageRange.Value = Mathf.Clamp(EngageRange.Value + 2f, 4f, 50f); SaveSettings(); });
            Note("Of it or you (all your companions). Living at home it also fights anything that comes into its home.", GameUi.Dim);
            bool potions = Companion.Potions(c), protect = Companion.Protect(c);
            Check("Drinks healing potions when hurt", potions, () => _pending = () => Change(z => z.Set(Keys.Potions, !potions)));
            Check("Protects you first (goes for what attacks you)", protect, () => _pending = () => Change(z => z.Set(Keys.Protect, !protect)));
            EndCard();

            BeginCard("Habits");
            bool loot = Loot.On(c), friends = Companion.Zdo(c).GetBool(Keys.Friends, false);
            Check("Picks things up off the ground (what, in the Gear tab)", loot, () => _pending = () => Change(z => z.Set(Loot.Key, !loot)));
            bool autoHome = Following.AutoHome(c);
            Check("Comes along when you head out, and lives at home again when you're back (with a bed)", autoHome, () => _pending = () => Change(z => z.Set(Following.AutoHomeKey, !autoHome)));
            Check("Shows what it decides in a fight above its head", ShowDecisions.Value, () => _pending = () => { ShowDecisions.Value = !ShowDecisions.Value; SaveSettings(); });
            bool chatty = Talk.Chatty(c);
            Check("Tells you in chat what it is up to (what it makes, what it needs)", chatty, () => _pending = () => Change(z => z.Set(Talk.ChattyKey, !chatty)));
            if (Mine) Check("Friends can give it orders", friends, () => _pending = () => Companion.Write(c, z => z.Set(Keys.Friends, !friends)));
            EndCard();

            BeginCard("Talking to it");
            string n = Companion.NameOf(c);
            Note($"Start a chat message with its name: \"{n}, follow me\", \"{n} go home\", \"{n}, be careful\", \"{n}, get your things\", and the errands: \"{n}, put away\", \"restock\", \"gear up\", \"repair your gear\", \"repair the base\", \"feed the fires\", \"cook\". It answers above its head.", GameUi.Text);
            EndCard();

            if (Mine)
            {
                BeginCard("Send it away");
                Note("Sends it away for good. Only with its bag empty, so nothing is lost.", GameUi.Dim);
                Btn("Send home", _left, _y, 160f, 32f, () => _pending = () => { if (Companion.Dismiss(c, out string why)) CloseMenu(); else _note = why; });
                _y += 38f;
                EndCard();
            }
        }

        // ==== Home =========================================================================================

        private void DrawHome(Humanoid c)
        {
            ZDO z = Companion.Zdo(c);
            Player p = Player.m_localPlayer;
            BrainState st = Brain.Get(c);
            bool bed = z.GetBool(Keys.HasBed, false);

            BeginCard("Bed and chests");
            if (bed)
            {
                Vector3 spot = z.GetVec3(Keys.BedPos, Vector3.zero);
                Note($"Its bed is {Vector3.Distance(spot, p.transform.position):0} m from you. It wakes there after falling and lives around it.", GameUi.Good);
            }
            else Note("No bed yet: it wakes beside you after falling. Give it a bed nobody sleeps in.", GameUi.Warn);
            List<Container> chests = Home.Chests(c);
            if (chests.Count == 0) Note("No chests nearby. It stores what it finds in its chests and takes better gear, food, arrows and potions from them.", GameUi.Dim);
            foreach (Container ch in chests)
            {
                Inventory inv = ch.GetInventory();
                Note($"{Loc(ch.m_name)}: {inv.NrOfItems()} of {inv.GetWidth() * inv.GetHeight()} slots used", GameUi.Text);
            }
            _y += 4f;
            if (Mine && (!bed || chests.Count == 0))
                WideBtn(bed ? "Take the empty chests beside its bed" : "Find it a free bed (and empty chests beside it)", true,
                    () => _pending = () => { string took = Home.SetUp(c); if (took != null) Talk.Tell(c, took); else _note = bed ? "No empty chest within 8 m of its bed." : "No free bed within 40 m of it (nobody sleeps in it, no companion has it)."; });
            if (Mine)
            {
                float letGo = bed ? 170f : 0f;
                Btn("Choose bed and chests", _left, _y, _width - (bed ? letGo + 6f : 0f), 34f, () => _pending = () => { Home.StartAssign(c); CloseMenu(); });
                if (bed) Btn("Let its bed go", _left + _width - letGo, _y, letGo, 34f, () => _pending = () => { Bed b = Home.BedOf(c); if (b != null) Home.ToggleBed(b, c); else Change(cz => cz.Set(Keys.HasBed, false)); });
                _y += 40f;
                Note($"Choosing closes this menu: then E on a bed or a chest gives it to {Companion.NameOf(c)}, E again takes it back. {MenuKey.Value} or Esc when done. Going home takes a free bed and empty chests near it by itself.", GameUi.Dim);
            }
            EndCard();

            BeginCard("Your chests");
            Note("The chests at home that are not a companion's.", GameUi.Dim);
            bool stow = Work.Stows(c), pantry = Work.UsesPantry(c), armory = Armory.Allowed(c);
            Check("Puts what it gathers into your chests when its own are full (or it has none). It never takes anything out.", stow, () => _pending = () => Change(cz => cz.Set(Work.StowKey, !stow)));
            Check("Takes food from your chests to keep its food slots filled (food only, never anything else)", pantry, () => _pending = () => Change(cz => cz.Set(Work.PantryKey, !pantry)));
            Check("Takes better weapons, armour and tools from your chests and wears them (its old ones go back in the chest)", armory, () => _pending = () => Change(cz => cz.Set(Armory.Key, !armory)));
            Note(pantry ? "On: when its food slots run low it takes food from your chests at home (and says what it took). Materials for its gear it only ever takes from its own chests."
                        : "Off: it eats from its own chests, forages, hunts and cooks, and asks you in chat when it runs out. It never takes anything from your chests.", GameUi.Dim);
            EndCard();

            bool living = Companion.OrderOf(c) == AICompanion.Order.Gather;
            BeginCard("Missions");
            Goal goal = st.Goal;
            List<Mission> missions = Missions.All(c);
            Mission current = missions.LastOrDefault(m => m.Status == "active");
            if (!c.GetComponent<ZNetView>().IsOwner()) Note("Shows on the game that runs it.", GameUi.Dim);
            else if (goal == null) Note(living ? "No mission right now: nothing better to make or upgrade at the stations near its home. Build a workbench (and a forge) near its home, or give it better tools." : "It sets itself a mission when it goes home.", GameUi.Dim);
            else
            {
                if (current != null && current.StartCost > 0)
                {
                    float done = Mathf.Clamp01(1f - current.Cost / (float)current.StartCost);
                    Note($"Mission since day {current.Day}: {Mathf.RoundToInt(done * 100f)}% of the way", GameUi.Dim);
                    if (_make) GameUi.Bar(_in, new Vector2(_left, -_y), new Vector2(_width, 12f), done, GameUi.Good, null);
                    _y += 18f;
                }
                Note(Capital(goal.What) + (goal.Station != null ? $", at the {Loc(goal.Station.m_name).ToLowerInvariant()}" : ""), GameUi.Text, bold: true);
                if (goal.Raw.Count > 0) Note("Gathering: " + goal.RawText(), GameUi.Text);
                if (goal.Steps.Count > 0) Note("Then makes: " + string.Join(", ", goal.Steps.Select(s => Loc(s.Key.m_item.m_itemData.m_shared.m_name).ToLowerInvariant())), GameUi.Text);
                if (goal.Smelt.Count > 0) Note($"{Capital(string.Join(" and ", goal.Smelt))} need{(goal.Smelt.Count == 1 ? "s" : "")} smelting: it puts the ore in its chest for you (or FeedFromChests) to smelt.", GameUi.Dim);
                if (goal.Ask.Count > 0) Note("Needs from you: " + string.Join(", ", goal.Ask) + ". Put it in its chest.", GameUi.Warn);
                var next = st.Candidates.Where(g => g != goal).Take(2).Select(g => g.What).ToList();
                if (next.Count > 0) Note("After that: " + string.Join(", then ", next) + ".", GameUi.Dim);
            }
            var past = missions.Where(m => m.Status != "active").Reverse().Take(5).ToList();
            if (past.Count > 0)
            {
                _y += 4f;
                Note("Lately:", GameUi.Dim);
                foreach (Mission m in past)
                    Note(m.Status == "done" ? $"Done: {m.What} ({m.Why})" : $"Gave up: {m.What}: {m.Why}", m.Status == "done" ? GameUi.Good : GameUi.Text);
            }
            Note("It sets its own missions (its next piece of better gear) and sticks to them; it decides for itself when to give one up, and says why.", GameUi.Dim);
            EndCard();

            BeginCard("Living at home");
            if (!living)
            {
                Note("It is not living at home now.", GameUi.Dim);
                Btn("Go home", _left, _y, 180f, 32f, () => _pending = () => GiveOrder(h => Home.GoHome(h)), true);
                _y += 38f;
            }
            Vector3 center = Work.Center(c);
            bool homeSet = z.GetBool(Work.HomeSetKey, false);
            Note(homeSet ? $"Its home is the area you set, {Vector3.Distance(center, p.transform.position):0} m from you: it stores its things, works, sorts into your chests and comes home there. Its bed is where it sleeps."
                : bed ? "Its home is around its bed. Set it to your base instead if its bed is off to one side."
                : $"With no bed it lives around where it was told to ({center.x:0}, {center.z:0}).", GameUi.Dim);
            if (Commandable)
            {
                float clear = homeSet ? 210f : 0f;
                Btn("Make this its home (where I stand)", _left, _y, _width - (homeSet ? clear + 6f : 0f), 32f,
                    () => _pending = () => { Vector3 at = Player.m_localPlayer.transform.position; Change(cz => { cz.Set(Work.HomeSpotKey, at); cz.Set(Work.HomeSetKey, true); cz.Set(Keys.Post, at); }); }, true);
                if (homeSet) Btn(bed ? "Back to around its bed" : "Clear", _left + _width - clear, _y, clear, 32f, () => _pending = () => Change(cz => cz.Set(Work.HomeSetKey, false)));
                _y += 38f;
            }
            int radius = Work.RadiusOf(c);
            Stepper("Goes as far as", $"{radius} m", () => Change(cz => cz.Set(Keys.Radius, Mathf.Clamp(radius - 5, 10, 80))), () => Change(cz => cz.Set(Keys.Radius, Mathf.Clamp(radius + 5, 10, 80))));
            EndCard();

            // Home duties: a short list in the order it does them, each with a stockpile it keeps in its chest.
            List<Duties.Entry> duties = Duties.Read(z);
            Duties.Entry working = Duties.Active(c);
            BeginCard("Home duties");
            Note("What it does at home, top to bottom. It works the first duty that is on until its stockpile holds the amount you set (or your plans are built), then the next one. When all are done it goes back to living its own life.", GameUi.Text);
            bool toYours = Duties.ToYours(c);
            Choices(new[] { "Put it in your chests", "Put it in its own chest" }, toYours ? 0 : 1, i => { if (Commandable) _pending = () => Change(cz => cz.Set(Duties.DestKey, i)); });
            if (duties.Any(x => x.On && x.Duty != Duty.Build) && !Duties.HasPlace(c))
                Note(toYours ? (Work.Stows(c) ? "No chest of yours near its home that it can use: duties wait for one." : "Putting things in your chests is off (Your chests, below): duties wait until you switch it on.")
                             : "It has no chest of its own: duties wait. Give it one in Bed and chests above.", GameUi.Warn);
            else if (toYours) Note("What is in your chests at home counts: with 300 wood there and 100 asked for, it rests.", GameUi.Dim);
            const float sub = 30f;   // (what belongs to a duty is set in under it)
            for (int di = 0; di < duties.Count; di++)
            {
                Duties.Entry e = duties[di];
                Duty dd = e.Duty;
                // its place in the list, on or off, and moving it up or down
                _sig.Append("|#").Append(di);
                if (_make) GameUi.Label(_in, (di + 1).ToString(), new Vector2(_left, -_y - 4f), new Vector2(24f, 24f), 18f, GameUi.Gold);
                float rowH = CheckAt(Duties.Label(dd), e.On, () => { if (Commandable) _pending = () => Change(cz => { var l = Duties.Read(cz); var x = l.First(a => a.Duty == dd); x.On = !x.On; Duties.Write(cz, l); }); },
                    _left + sub, _y, _width - sub - 120f);
                Btn("Up", _left + _width - 114f, _y, 52f, 30f,
                    () => _pending = () => Change(cz => { var l = Duties.Read(cz); int k = l.FindIndex(a => a.Duty == dd); if (k > 0) { var t = l[k]; l[k] = l[k - 1]; l[k - 1] = t; Duties.Write(cz, l); } }), enabled: di > 0, size: 15f);
                Btn("Down", _left + _width - 58f, _y, 58f, 30f,
                    () => _pending = () => Change(cz => { var l = Duties.Read(cz); int k = l.FindIndex(a => a.Duty == dd); if (k >= 0 && k < l.Count - 1) { var t = l[k]; l[k] = l[k + 1]; l[k + 1] = t; Duties.Write(cz, l); } }), enabled: di < duties.Count - 1, size: 15f);
                _y += Mathf.Max(rowH, 30f) + 6f;
                if (!e.On) continue;
                if (dd == Duty.Build)
                {
                    Note(Duties.Status(c, e, working), e == working ? GameUi.Good : GameUi.Dim, indent: sub);
                    if (Duties.Status(c, e, working).StartsWith("Needs")) Note("Install BuildOrders, then plan pieces at your base (hammer out, Left Alt): it builds them, paying from its bag and your chests.", GameUi.Warn, indent: sub);
                    continue;
                }
                int step = Duties.Step(dd), max = Duties.Max(dd);
                Stepper("Keep in " + (toYours ? "your chests" : "its chest"), $"{e.Target} {Duties.Unit(dd)}",
                    () => Change(cz => { var l = Duties.Read(cz); var x = l.First(a => a.Duty == dd); x.Target = Mathf.Clamp(x.Target - step, step, max); Duties.Write(cz, l); }),
                    () => Change(cz => { var l = Duties.Read(cz); var x = l.First(a => a.Duty == dd); x.Target = Mathf.Clamp(x.Target + step, step, max); Duties.Write(cz, l); }), sub);
                Note(Duties.Status(c, e, working), e == working ? GameUi.Good : GameUi.Dim, indent: sub);
                if (dd == Duty.Wood && Work.Axe(c) == null) Note("It needs an axe (put one in its chest).", GameUi.Warn, indent: sub);
                if (dd == Duty.Mining && Work.Pickaxe(c) == null) Note("It needs a pickaxe (put one in its chest).", GameUi.Warn, indent: sub);
                if (dd == Duty.Food && Companion.BestRanged(c) == null && Companion.BestMelee(c) == null) Note("It needs a weapon to hunt; it can still forage.", GameUi.Warn, indent: sub);
            }
            Note("A cooking station near home lets it cook what it hunts. Hungry, it feeds itself first whatever the duty.", GameUi.Dim);
            EndCard();

            BeginCard("Its food");
            bool frugal = AICompanion.Food.Frugal(c);
            Check("Eats only what it needs when it is calm at home (the cheapest food that will do)", frugal, () => _pending = () => Change(cz => cz.Set(AICompanion.Food.FrugalKey, !frugal)));
            Note(frugal ? "Out with you, in a fight or hurt it eats its three best meals as a player does. Calm at home it keeps one or two meals going (none while asleep and well), and uses berries, mushrooms and the like before your good meat."
                        : "Off: it keeps three meals going all the time, the best first. That is a lot of food.", GameUi.Dim);
            int ration = Work.RationOf(c);
            Note($"Today: ate {AICompanion.Food.AteToday(c)} meals" + (Work.UsesPantry(c) ? $", took {Work.TakenToday(c)} of your food (up to {ration} a day)." : "."), GameUi.Text);
            if (Work.UsesPantry(c))
                Stepper("Most it takes from yours a day", $"{ration} food", () => Change(cz => cz.Set(Work.RationKey, Mathf.Clamp(ration - 2, 2, 60))), () => Change(cz => cz.Set(Work.RationKey, Mathf.Clamp(ration + 2, 2, 60))));
            EndCard();

            Job jobs = Work.JobsOf(c);
            BeginCard(duties.Any(x => x.On && x.Duty != Duty.Build) ? "Other jobs (not used while a duty is on)" : "Jobs (optional)");
            Job auto = Work.AutoJobs(c);
            Note(jobs == Job.None
                ? "Nothing switched on: it decides for itself, gathering what its goal needs and what its tools allow. Right now: " + (auto == Job.None && goal == null ? "nothing (no tools, and enough food)." : JobNames(auto | Goals.JobsFor(goal, out _)) + ".")
                : "It does only what is switched on (still preferring what its goal needs). Switch everything off to let it decide for itself.", GameUi.Text);
            foreach (Job job in new[] { Job.Wood, Job.Stone, Job.Ore, Job.Forage, Job.Hunt, Job.Cook, Job.Loot })
            {
                bool on = (jobs & job) != 0;
                string tool = Work.ToolFor(c, job);
                string label = job switch
                {
                    Job.Wood => "Wood: logs, stumps and trees",
                    Job.Stone => "Stone: rocks",
                    Job.Ore => "Ore: copper, tin, iron, silver deposits",
                    Job.Forage => "Forage: wild berries, mushrooms, flowers (never your crops)",
                    Job.Hunt => "Hunt: deer, boar, necks and hares",
                    Job.Cook => "Cook its raw food on a cooking station",
                    _ => "Pick up everything on the ground (also what players drop)",
                };
                Job pick = job;
                Check(label, on, () => _pending = () => Change(cz => cz.Set(Keys.Jobs, cz.GetInt(Keys.Jobs, 0) ^ (int)pick)));
                if (job == Job.Wood || job == Job.Stone || job == Job.Ore) Note("Tool: " + tool, tool.StartsWith("no ") ? GameUi.Warn : GameUi.Dim, indent: 74f);
            }
            Note("A better axe or pickaxe works harder trees and ores (the game's tool tiers). At its workbench it also upgrades and makes gear when it has the materials.", GameUi.Dim);
            EndCard();

            BeginCard("Gathered this session");
            if (st.Gathered.Count == 0) Note("Nothing yet.", GameUi.Dim);
            else Note(string.Join(", ", st.Gathered.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {kv.Key}")), GameUi.Text);
            EndCard();

            BeginCard("While you are away");
            Note(WhileAway.Value switch
            {
                AwayMode.Off => "When you come back it catches up on the work it would have done: trees felled, rocks mined, berries picked, into its chests.",
                AwayMode.Real => "When you come back it catches up on its work, and on fights with the creatures around its home, which can go badly: it can fall (its things in a tombstone nearby, which it goes back for).",
                _ => "When you come back it catches up on the work it would have done (trees felled, rocks mined, berries picked, into its chests), and fights off a few creatures, keeping what they drop. It never falls while you are away.",
            }, GameUi.Text);
            Note($"Setting: WhileAway ({WhileAway.Value}).", GameUi.Dim);
            EndCard();

            BeginCard("If it falls");
            Note($"As a player: its things go into its tombstone (a skull on everyone's map), it wakes {RespawnSeconds.Value:0} s later in its bed (or beside you), and goes back for its things. E on its tombstone gives them back to it.", GameUi.Text);
            EndCard();
        }

        private static string JobNames(Job j) => string.Join(", ", ((Job[])Enum.GetValues(typeof(Job))).Where(x => x != Job.None && (j & x) != 0).Select(x => x.ToString().ToLower()));

        // ==== Gear =========================================================================================

        private void DrawGear(Humanoid c)
        {
            Player p = Player.m_localPlayer;
            float space = GameUi.CellSpace;

            // Its gear: slots of their own (Gear), kept when it falls; changed from here, wherever it is. The game's tooltip tells what each is.
            BeginCard("Its gear  (kept when it falls)");
            bool mine = Mine;
            foreach (int row in new[] { 4, 5 })
            {
                Gear.Slot[] slots = Gear.All.Where(s => s.Y == row).ToArray();
                for (int j = 0; j < slots.Length; j++)
                {
                    ItemDrop.ItemData inSlot = Gear.In(c, slots[j]);
                    AppendItem(inSlot);
                    if (!_make) continue;
                    var pos = new Vector2(_left + j * space, -_y);
                    Gear.Slot slot = slots[j];
                    if (inSlot == null) GameUi.EmptyCell(_in, pos);
                    else GameUi.Cell(_in, pos, inSlot, null, false, mine ? () =>
                    {
                        // (what is in the slot when clicked: on a game that doesn't run it, its items are fresh copies every moment)
                        _pending = () => { ItemDrop.ItemData now = _shown != null ? Gear.In(_shown, slot) : null; if (now != null && !Companion.Take(_shown, Player.m_localPlayer, now, out string why) && why != null) _note = why; };
                        _nextCheck = 0f;
                    } : (Action)null);
                    Line(_in, pos.x - (space - 64f) / 2f, _y + 66f, space, 14f, GameUi.Dim, TextAlignmentOptions.Top).text = slots[j].Label;
                }
                _y += 64f + 24f;
            }
            Note(mine ? "Click a piece of gear to take it back. It moves the best it has into these slots by itself." : "Point at a piece to see what it is.", GameUi.Dim);
            EndCard();

            Inventory its = c.GetInventory();
            BeginCard($"Its bag  ({Gear.BagCount(its)} of {its.GetWidth() * Gear.BagRows} slots, weight {Carry.Weight(c):0} of {Carry.Max(c):0})");
            if (Carry.Over(c)) Note("Too heavy: it cannot run until it puts something down.", GameUi.Warn);
            int width = its.GetWidth(), height = Mathf.Min(Gear.BagRows, its.GetHeight());
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    ItemDrop.ItemData item = its.GetItemAt(x, y);   // (look only: its bag is changed beside it)
                    AppendItem(item);
                    if (!_make) continue;
                    var pos = new Vector2(_left + x * space, -_y - y * space);
                    if (item == null) GameUi.EmptyCell(_in, pos); else GameUi.Cell(_in, pos, item, null, false, null);
                }
            _y += height * space + 4f;
            Note("What it carries goes into its tombstone when it falls. To give or take from its bag, open your inventory (Tab) next to it: its bag shows beside yours.", GameUi.Dim);
            EndCard();

            if (mine && p != null)
            {
                var yours = AppDomain.CurrentDomain.GetData("DHack.GearSlots.IsGear") as Func<ItemDrop.ItemData, bool>; // your own gear slots (GearSlots)
                var giveable = p.GetInventory().GetAllItems().Where(i => !p.IsItemEquiped(i) && !i.m_equipped && !(yours?.Invoke(i) ?? false) && Gear.IsGear(i)).ToList();
                BeginCard("Give it gear from your inventory");
                if (giveable.Count == 0) Note("Weapons, shields, bows, arrows, axes, pickaxes, hammers and armour you are not wearing show here.", GameUi.Dim);
                else
                {
                    int per = Mathf.Max(1, Mathf.FloorToInt((_width + space - 64f) / space));
                    for (int i = 0; i < giveable.Count; i++)
                    {
                        ItemDrop.ItemData it = giveable[i];
                        AppendItem(it);
                        if (_make)
                            GameUi.Cell(_in, new Vector2(_left + i % per * space, -_y - i / per * space), it, null, false,
                                () => { _pending = () => { if (!Companion.Give(_shown, Player.m_localPlayer, it, out string why) && why != null) _note = why; }; _nextCheck = 0f; });
                    }
                    _y += (giveable.Count + per - 1) / per * space + 4f;
                    Note("Click to give: it goes into its gear slot (what was there goes into its bag).", GameUi.Dim);
                }
                EndCard();
            }

            BeginCard("What it picks up");
            Note("As it walks by, and fresh drops near it (what you mine and chop, what creatures drop). Never what a player dropped.", GameUi.Dim);
            Loot.Kinds list = Loot.List(c);
            var kinds = new[] { Loot.Kinds.Ore, Loot.Kinds.Wood, Loot.Kinds.Stone, Loot.Kinds.Food, Loot.Kinds.Creature, Loot.Kinds.Gear, Loot.Kinds.Other };
            float colW = (_width - 16f) / 2f;
            for (int i = 0; i < kinds.Length; i += 2)
            {
                float rowH = 0f;
                for (int j = i; j < Mathf.Min(i + 2, kinds.Length); j++)
                {
                    Loot.Kinds k = kinds[j];
                    string label = k == Loot.Kinds.Ore ? "Ore and metal" : k == Loot.Kinds.Creature ? "Creature drops" : k == Loot.Kinds.Other ? "Everything else" : k == Loot.Kinds.Food ? "Food and raw meat" : k.ToString();
                    rowH = Mathf.Max(rowH, CheckAt(label, (list & k) != 0, () => _pending = () => Change(z => z.Set(Loot.ListKey, z.GetInt(Loot.ListKey, (int)Loot.Default) ^ (int)k)),
                        _left + (j - i) * (colW + 16f), _y, colW));
                }
                _y += rowH + 6f;
            }
            if (!Loot.On(c)) Note("Picking up is switched off (Orders tab).", GameUi.Warn);
            EndCard();
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

        // ==== Looks ========================================================================================

        private void Rebind(Humanoid c) { _rebind = Companion.IdOf(c); _rebindUntil = Time.time + 5f; }

        private void LooksChange(Action<Humanoid> change)
        {
            if (!Mine) { _note = "Only its owner can change how it looks."; return; }
            Humanoid c = _shown;
            if (!Companion.Write(c, _ => change(c))) _note = "Someone has its things open. Try again in a moment.";
        }

        private void DrawLooks(Humanoid c)
        {
            long id = Companion.IdOf(c);
            BeginCard();
            float top = _y, face = 150f, beside = face + 16f;
            if (_make)
            {
                RawImage picture = GameUi.Picture(_in, new Vector2(_left, -top), new Vector2(face, face), Portraits.Get(id));
                _live.Add(() => { Texture t = Portraits.Get(id); if (picture.texture != t) picture.texture = t; picture.enabled = t != null; });
            }
            Heading("How it looks", beside);
            Note("As when you make a character. Everyone sees the same, and it wakes looking the same after it falls. It changes as you click: turn round to watch.", GameUi.Text, indent: beside);
            _y += 6f;
            float bw = (_width - beside - 8f) / 2f;
            Btn("Randomise", _left + beside, _y, bw, 34f, () => _pending = () => { Rebind(c); LooksChange(Looks.Randomise); });
            Btn("Look like me", _left + beside + bw + 8f, _y, bw, 34f, () => _pending = () => { Rebind(c); LooksChange(h => Looks.CopyFrom(h, Player.m_localPlayer)); });
            _y = Mathf.Max(_y + 40f, top + face + 4f);
            EndCard();

            int model = Looks.Model(c);
            BeginCard("Body");
            Choices(new[] { "Body 1", "Body 2" }, model, i => _pending = () => { Rebind(c); LooksChange(h => Looks.SetModel(h, i)); });
            EndCard();

            List<ItemDrop> hairs = Looks.Hairs(), beards = Looks.Beards();
            BeginCard("Hair");
            Picker("Style", Looks.StyleName(Looks.Hair(c), hairs), () => LooksChange(h => Looks.SetHair(h, Looks.Step(Looks.Hair(h), hairs, -1))), () => LooksChange(h => Looks.SetHair(h, Looks.Step(Looks.Hair(h), hairs, 1))));
            SliderRow("Colour", () => Looks.HairTone(c), v => _pending = () => LooksChange(h => Looks.SetHairColor(h, v, Looks.HairShade(h))));
            SliderRow("Shade", () => Looks.HairShade(c), v => _pending = () => LooksChange(h => Looks.SetHairColor(h, Looks.HairTone(h), v)));
            EndCard();

            if (model == 0)
            {
                BeginCard("Beard");
                Picker("Style", Looks.StyleName(Looks.Beard(c), beards), () => LooksChange(h => Looks.SetBeard(h, Looks.Step(Looks.Beard(h), beards, -1))), () => LooksChange(h => Looks.SetBeard(h, Looks.Step(Looks.Beard(h), beards, 1))));
                EndCard();
            }

            BeginCard("Skin");
            SliderRow("Tone", () => Looks.SkinTone(c), v => _pending = () => LooksChange(h => Looks.SetSkin(h, v)));
            EndCard();
        }

        private void Picker(string label, string value, Action prev, Action next)
        {
            _sig.Append("|p").Append(label).Append(value);
            if (_make) Line(_in, _left, _y + 6f, 90f, 17f, GameUi.Text).text = label;
            Btn("<", _left + 96f, _y, 44f, 32f, () => _pending = prev);
            if (_make) Line(_in, _left + 146f, _y + 6f, 180f, 17f, GameUi.Gold, TextAlignmentOptions.Top).text = value;
            Btn(">", _left + 332f, _y, 44f, 32f, () => _pending = next);
            _y += 40f;
        }

        /// <summary>A slider (0 to 1) with its name. It is set from the companion while nobody drags it (someone else may change it).</summary>
        private void SliderRow(string label, Func<float> value, Action<float> set)
        {
            _sig.Append("|v").Append(label);
            if (_make)
            {
                Line(_in, _left, _y + 4f, 90f, 17f, GameUi.Text).text = label;
                Slider s = GameUi.Slider(_in, new Vector2(_left + 96f, -_y), new Vector2(_width - 96f, 28f), value(), set);
                _live.Add(() => { if (GameUi.Sliding) return; float now = value(); if (Mathf.Abs(s.value - now) > 0.005f) s.SetValueWithoutNotify(now); });
            }
            _y += 36f;
        }

        // ==== Journal ======================================================================================

        private void DrawJournal(Humanoid c)
        {
            var tally = Journal.Tally(c);
            int T(string k) => tally.TryGetValue(k, out int n) ? n : 0;
            BeginCard("Its saga");
            Stats(("Days with you", Journal.DaysWithYou(c).ToString(), null), ("Kills", T("kills").ToString(), null), ("Parries", T("parries").ToString(), null), ("Falls", T("deaths").ToString(), null));
            var kills = tally.Where(kv => kv.Key.StartsWith("kill:")).OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Value} {kv.Key.Substring(5).ToLowerInvariant()}").ToList();
            if (kills.Count > 0) Note("Most slain: " + string.Join(", ", kills), GameUi.Text);
            var bosses = tally.Where(kv => kv.Key.StartsWith("boss:")).Select(kv => kv.Key.Substring(5)).ToList();
            Note(bosses.Count > 0 ? "Bosses: " + string.Join(", ", bosses) : "No boss yet.", bosses.Count > 0 ? GameUi.Good : GameUi.Dim);
            Note($"Gear made: {T("made")}   Upgrades: {T("upgraded")}   Trips: {T("trips")}", GameUi.Text);
            var lands = tally.Where(kv => kv.Key.StartsWith("place:") && kv.Key != "place:dungeon").Select(kv => kv.Key.Substring(6)).ToList();
            if (lands.Count > 0) Note("Has seen: " + string.Join(", ", lands) + (T("place:dungeon") > 0 ? $", and {T("place:dungeon")} dungeon{(T("place:dungeon") == 1 ? "" : "s")}" : ""), GameUi.Dim);
            EndCard();

            BeginCard("Moments");
            var entries = Journal.Entries(c);
            if (entries.Count == 0) Note("Nothing written yet: its first kills, parries, trips and the gear it makes will show here.", GameUi.Dim);
            foreach (string e in Enumerable.Reverse(entries).Take(40))
            {
                int bar = e.IndexOf('|');
                string day = bar > 0 ? e.Substring(0, bar) : "?", text = bar > 0 ? e.Substring(bar + 1) : e;
                if (_make) Line(_in, _left, _y, 76f, 16f, GameUi.Dim).text = $"Day {day}";
                Note(text, GameUi.Text, indent: 82f);
            }
            EndCard();
        }

        // ==== pieces =======================================================================================
        // Each writes what it shows into the signature, and makes it only when the window is being made. Positions: x from the left of where
        // things go now (the window or a card), y downward from its top.

        private void BeginCard(string title = null)
        {
            _sig.Append("\n[").Append(title);
            _y = CardPad;
            if (_make)
            {
                _card = GameUi.Box(_list, new Vector2(0f, -_listY), new Vector2(_cardW, 40f));
                _in = _card;
                _left = CardPad;
                _width = _cardW - CardPad * 2f;
            }
            if (title != null) Heading(title);
        }

        private void EndCard()
        {
            _sig.Append(']');
            float h = _y + CardPad - 5f;
            if (_make) _card.sizeDelta = new Vector2(_cardW, h);
            _listY += h + CardGap;
        }

        /// <summary>A card's title, in the game's title font (Averia when it has numbers: Norse draws 0 as a rune).</summary>
        private void Heading(string title, float indent = 0f, bool advance = true, float width = -1f)
        {
            _sig.Append("|h").Append(title);
            if (_make)
            {
                TMP_FontAsset font = title.Any(char.IsDigit) ? GameUi.Serif : GameUi.Norse;
                TMP_Text t = GameUi.Label(_in, title, new Vector2(_left + indent, -_y), new Vector2(width > 0f ? width : _width - indent, 30f), 22f, GameUi.Warm, font);
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Ellipsis;
            }
            if (advance) _y += 32f;
        }

        /// <summary>A title with buttons on the right of it (a companion in the list: "Call to me", "Open").</summary>
        private void HeadingRow(string title, params (string label, bool lit, Action click)[] buttons)
        {
            float x = _left + _width;
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                x -= 130f;
                Btn(buttons[i].label, x, _y, 130f, 32f, buttons[i].click, buttons[i].lit, size: 16f);
                x -= 8f;
            }
            Heading(title, 0f, false, x - _left - 8f);
            _y += 38f;
        }

        /// <summary>Text that wraps to the width it has; what comes next goes under it.</summary>
        private void Note(string text, Color color, float size = 17f, float indent = 0f, bool bold = false)
        {
            _sig.Append("|n").Append(color.GetHashCode()).Append(text);
            if (!_make) return;
            float w = _width - indent;
            TMP_Text t = GameUi.Label(_in, text, new Vector2(_left + indent, -_y), new Vector2(w, 24f), size, color);
            if (bold) t.fontStyle = FontStyles.Bold;
            float h = t.GetPreferredValues(text, w, 0f).y;
            ((RectTransform)t.transform).sizeDelta = new Vector2(w, h);
            _y += h + 5f;
        }

        /// <summary>One line whose words change all the time (a countdown): changed in place, not in the signature.</summary>
        private void LiveNote(Func<string> text, Color color, float size = 17f)
        {
            _sig.Append("|l");
            if (!_make) return;
            TMP_Text t = Line(_in, _left, _y, _width, size, color);
            void Show() => Set(t, text());
            Show();
            _live.Add(Show);
            _y += size * 1.35f + 5f;
        }

        /// <summary>A text on one line (cut short with "..." if it doesn't fit), its words set later.</summary>
        private static TMP_Text Line(Transform parent, float x, float y, float w, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            TMP_Text t = GameUi.Label(parent, "", new Vector2(x, -y), new Vector2(w, size * 1.35f), size, color, null, align);
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        private static void Set(TMP_Text t, string text) { if (t.text != text) t.text = text; }

        private Button Btn(string label, float x, float y, float w, float h, Action click, bool lit = false, bool enabled = true, float size = 17f, Color? color = null)
        {
            _sig.Append("|b").Append(label).Append(lit ? '*' : '.').Append(enabled ? '1' : '0');
            if (!_make) return null;
            Button b = GameUi.Button(_in, label, new Vector2(x, -y), new Vector2(w, h), () => { click(); _nextCheck = 0f; }, enabled, size);
            if (enabled && (lit || color != null)) GameUi.Label(b).color = color ?? GameUi.Gold;
            return b;
        }

        /// <summary>A button as wide as where it is.</summary>
        private void WideBtn(string label, bool lit, Action click, float h = 34f, Color? color = null)
        {
            Btn(label, _left, _y, _width, h, click, lit, color: color);
            _y += h + 6f;
        }

        /// <summary>The game's tab button: the chosen one pressed in (not clickable), its label gold.</summary>
        private void TabBtn(string label, float x, float y, float w, float h, bool chosen, Action click, float size = 17f)
        {
            _sig.Append("|t").Append(label).Append(chosen ? '*' : '.');
            if (_make) GameUi.Tab(_in, label, new Vector2(x, -y), new Vector2(w, h), chosen, () => { click(); _nextCheck = 0f; }, size);
        }

        /// <summary>One of a row of choices (sharing the width), as tabs: the chosen one pressed in.</summary>
        private void Choices(string[] labels, int chosen, Action<int> pick, float h = 34f)
        {
            float bw = (_width - 6f * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                int k = i;
                TabBtn(labels[i], _left + i * (bw + 6f), _y, bw, h, i == chosen, () => pick(k));
            }
            _y += h + 6f;
        }

        /// <summary>
        /// A setting that is on or off: a button saying which (gold when on: the inventory screen has no tick box to clone), its words beside
        /// it (clicking them switches it too). Returns the height it took.
        /// </summary>
        private float CheckAt(string label, bool on, Action toggle, float x, float y, float w)
        {
            _sig.Append("|k").Append(on ? '1' : '0').Append(label);
            if (!_make) return 0f;
            const float bw = 62f, bh = 30f;
            Button b = GameUi.Button(_in, on ? "On" : "Off", new Vector2(x, -y), new Vector2(bw, bh), () => { toggle(); _nextCheck = 0f; }, true, 16f);
            if (on) GameUi.Label(b).color = GameUi.Gold;
            float tw = w - bw - 10f;
            TMP_Text t = GameUi.Label(_in, label, new Vector2(x + bw + 10f, -y), new Vector2(tw, bh), 17f, GameUi.Text);
            float th = t.GetPreferredValues(label, tw, 0f).y;
            var tr = (RectTransform)t.transform;
            tr.sizeDelta = new Vector2(tw, th);
            if (th < bh) tr.anchoredPosition = new Vector2(x + bw + 10f, -y - (bh - th) / 2f);
            t.raycastTarget = true;
            Button words = t.gameObject.AddComponent<Button>();
            words.transition = Selectable.Transition.None;
            words.navigation = new Navigation { mode = Navigation.Mode.None };
            words.onClick.AddListener(() => { toggle(); _nextCheck = 0f; });
            return Mathf.Max(bh, th);
        }

        private void Check(string label, bool on, Action toggle, float indent = 0f)
        {
            float h = CheckAt(label, on, toggle, _left + indent, _y, _width - indent);
            _y += h + 6f;
        }

        /// <summary>A number with - and + beside it. The buttons' actions run in Update.</summary>
        private void Stepper(string label, string value, Action down, Action up, float indent = 0f)
        {
            _sig.Append("|s").Append(label).Append(value);
            float x = _left + indent, lw = Mathf.Max(120f, Mathf.Min(280f, _width - indent - 220f));
            if (_make)
            {
                TMP_Text t = Line(_in, x, _y + 5f, lw, 17f, GameUi.Text);
                t.text = label;
                t.fontStyle = FontStyles.Bold;
            }
            Btn("-", x + lw + 8f, _y, 40f, 30f, () => _pending = down, size: 20f);
            if (_make) Line(_in, x + lw + 52f, _y + 5f, 116f, 17f, GameUi.Gold, TextAlignmentOptions.Top).text = value;
            Btn("+", x + lw + 172f, _y, 40f, 30f, () => _pending = up, size: 20f);
            _y += 36f;
        }

        /// <summary>A row of big numbers with their names under them; a live one is read again ten times a second.</summary>
        private void Stats(params (string label, string value, Func<string> live)[] stats)
        {
            foreach (var s in stats) _sig.Append("|=").Append(s.label).Append(s.live == null ? s.value : "");
            if (!_make) return;
            float cw = _width / stats.Length;
            for (int i = 0; i < stats.Length; i++)
            {
                var s = stats[i];
                TMP_Text value = Line(_in, _left + i * cw, _y, cw - 6f, 26f, GameUi.Gold);
                value.fontStyle = FontStyles.Bold;
                if (s.live != null) { Func<string> f = s.live; void Show() => Set(value, f()); Show(); _live.Add(Show); }
                else value.text = s.value;
                Line(_in, _left + i * cw, _y + 36f, cw - 6f, 15f, GameUi.Dim).text = s.label;
            }
            _y += 62f;
        }

        /// <summary>A bar (health, stamina) whose fill and words change in place.</summary>
        private void LiveBar(Func<float> fraction, Func<string> text, Color color, float height = 24f)
        {
            _sig.Append("|r");
            if (_make)
            {
                RectTransform bar = GameUi.Bar(_in, new Vector2(_left, -_y), new Vector2(_width, height), fraction(), color, text(), 15f);
                _live.Add(() => GameUi.SetBar(bar, fraction(), text()));
            }
            _y += height + 5f;
        }

        /// <summary>
        /// An item (or an empty place) in the signature: what it is, how many, how worn, whether worn. Not which object: on a game that
        /// doesn't run the companion its items are fresh copies every time its ZDO changes (several times a second).
        /// </summary>
        private StringBuilder AppendItem(ItemDrop.ItemData i)
        {
            if (i == null) return _sig.Append("|-");
            return _sig.Append('|').Append(i.m_shared.m_name).Append(',').Append(i.m_stack).Append(',').Append(i.m_quality)
                .Append(',').Append((int)(i.GetDurabilityPercentage() * 20f)).Append(',').Append(i.m_equipped);
        }

        private static string Loc(string s) => Localization.instance.Localize(s);
        private static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        /// <summary>The menu's interface, at unload (a hot reload): the window and its canvas.</summary>
        private void DestroyMenuResources() => GameUi.Destroy();
    }
}
