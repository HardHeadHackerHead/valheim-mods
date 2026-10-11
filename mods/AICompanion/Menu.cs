using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static DHack.Shared.Translations;

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
            _note = companion == null ? "" : (Companion.IsMine(companion, player) ? "" : T("aicompanion_menu_others", Companion.Zdo(companion).GetString(Keys.MasterName, "") is string m && m.Length > 0 ? m : T("aicompanion_menu_someone")));
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
            if (!Commandable) { _note = T("aicompanion_menu_owner_orders"); return; }
            if (!Companion.Write(_shown, change)) _note = T("aicompanion_menu_busy");
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
                Title(T("aicompanion_menu_changing_body"), w);
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
            text = Words.Show(text);
            _sig.Append("|T").Append(text);
            if (_make) GameUi.Label(_window, text, new Vector2(Pad, -20f), new Vector2(w - Pad * 2f - 130f, 44f), 34f, GameUi.Warm, GameUi.Norse);
            Btn(Loc("$menu_close"), w - Pad - 110f, 22f, 110f, 36f, () => _pending = CloseMenu);
            if (_make) GameUi.Braid(_window, new Vector2(0f, -76f), w - Pad * 2f);
        }

        // ==== your companions, and summoning =================================================================

        private float ListHeader(float w)
        {
            Title(T("aicompanion_menu_companions"), w);
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
                    if (d > 15f) buttons.Add((T("aicompanion_menu_call"), false, () => { Humanoid h2 = here; _pending = () => { if (Home.Follow(h2)) Brain.TeleportBehind(h2, Player.m_localPlayer, "aicompanion_brain_was_called_to"); }; }));
                    buttons.Add((T("aicompanion_menu_open"), true, () => { Humanoid h2 = here; _pending = () => OpenMenuFor(Player.m_localPlayer, h2); }));
                    HeadingRow(prof.Name, buttons.ToArray());
                    string doing = Companion.StatusOf(here);
                    string whereNow = d <= 15f ? T("aicompanion_menu_here_with_you") : Words.Show(Remote.Where(here.transform.position, me.transform.position));
                    Note(string.IsNullOrEmpty(doing) ? whereNow + "." : T("aicompanion_menu_where_doing", whereNow, Words.Show(doing)), GameUi.Good);
                }
                else if (prof.Dead)
                {
                    Heading(prof.Name);
                    Profile p0 = prof;
                    LiveNote(() => T(p0.HasBed ? "aicompanion_menu_fallen_bed" : "aicompanion_menu_fallen_you", Mathf.Max(0f, (float)(p0.DiedAt + RespawnSeconds.Value - ZNet.instance.GetTimeSeconds())).ToString("0")), GameUi.Warn);
                    Note(T("aicompanion_menu_things_in_tomb"), GameUi.Warn);
                    WideBtn(T("aicompanion_menu_wake_now", prof.Name), true, () => { Profile p2 = prof; _pending = () => { Humanoid c = Home.Respawn(Player.m_localPlayer, p2); if (c != null) OpenMenuFor(Player.m_localPlayer, c); else if (p2.Dead || Remote.InWorld(p2.Id) == null) _note = T("aicompanion_menu_asking_host", p2.Name); }; });
                }
                else
                {
                    // Away (its area not loaded here): what the world knows of it, and calling it to you.
                    ZDO z = Remote.Find(prof.Id);
                    if (z != null) HeadingRow(prof.Name, (T("aicompanion_menu_call"), true, () => { ZDO z2 = z; _pending = () => { if (Remote.Call(z2, Player.m_localPlayer)) _note = T("aicompanion_menu_on_the_way", prof.Name); }; }));
                    else Heading(prof.Name);
                    if (z != null)
                    {
                        Vector3 at = Remote.Position(z);
                        string doing = Remote.Status(z);
                        string where = me != null ? Words.Show(Remote.Where(at, me.transform.position)) : "";
                        if (Remote.AtHome(z)) where = where.Length > 0 ? T("aicompanion_menu_where_at_home", where) : T("aicompanion_menu_at_home");
                        string order = T(Remote.OrderOf(z) == AICompanion.Order.Gather ? "aicompanion_menu_away_living" : Remote.OrderOf(z) == AICompanion.Order.Follow ? "aicompanion_menu_away_left_behind" : Remote.OrderOf(z) == AICompanion.Order.Guard ? "aicompanion_menu_away_guarding" : "aicompanion_menu_away_staying");
                        Note($"{Capital(where)} ({order}).", GameUi.Text);
                        if (!string.IsNullOrEmpty(doing)) Note(T("aicompanion_menu_last_doing", Words.Show(doing)), GameUi.Good);
                        foreach (string line in Remote.Journal(z, 2)) Note("· " + Words.Show(line), GameUi.Dim);
                    }
                    else if (Remote.InWorld(prof.Id) == false)
                    {
                        // Lost: nothing of them is in the world, yet they never woke (their fall was not kept). They come back as they were.
                        Vector3? tomb = Remote.TombOf(prof.Id);
                        Note(T(prof.HasBed ? "aicompanion_menu_lost_bed" : "aicompanion_menu_lost_you", prof.Name) + " "
                             + (tomb != null && me != null ? T("aicompanion_menu_lost_tomb_at", Words.Show(Remote.Where(tomb.Value, me.transform.position))) : T("aicompanion_menu_lost_tomb")), GameUi.Warn);
                        WideBtn(T("aicompanion_menu_bring_back", prof.Name), true, () => { Profile p2 = prof; _pending = () => { Humanoid c = Home.Respawn(Player.m_localPlayer, p2); if (c != null) OpenMenuFor(Player.m_localPlayer, c); else if (p2.Dead || Remote.InWorld(p2.Id) == null) _note = T("aicompanion_menu_asking_host", p2.Name); }; });
                        Note(T("aicompanion_menu_forget_hint"), GameUi.Dim);
                        WideBtn(_forgetArmed == prof.Id ? T("aicompanion_menu_forget_again", prof.Name) : T("aicompanion_menu_forget", prof.Name), false, () =>
                        {
                            long id = prof.Id;
                            if (_forgetArmed == id) _pending = () => { Profile.Forget(Player.m_localPlayer, id); _forgetArmed = 0L; };
                            else _forgetArmed = id;
                        }, 32f, _forgetArmed == prof.Id ? GameUi.Warn : (Color?)null);
                    }
                    else
                        Note(T("aicompanion_menu_last_seen", Words.Show(Remote.Where(prof.LastSeen, me != null ? me.transform.position : prof.LastSeen))), GameUi.Text);
                }
                EndCard();
            }

            BeginCard();
            if (mine.Count >= MaxCompanions.Value)
            {
                Note(T("aicompanion_menu_most_allowed", mine.Count), GameUi.Dim);
                EndCard();
                return;
            }
            Heading(T(mine.Count == 0 ? "aicompanion_menu_summon_title" : "aicompanion_menu_summon_another_title"));
            Note(T("aicompanion_menu_summon_about"), GameUi.Dim);
            _y += 4f;
            NameField(null);
            if (mine.Count > 0) Note(T("aicompanion_menu_summon_new_warn"), GameUi.Warn);
            WideBtn(T(mine.Count == 0 ? "aicompanion_menu_summon" : "aicompanion_menu_summon_new"), true, () =>
                _pending = () =>
                {
                    // Not a second one by the same name: someone looking for theirs pressed this, and got a new one called the same.
                    string wanted = string.IsNullOrEmpty(_nameField?.Trim()) ? "Rádvar" : _nameField.Trim();
                    Profile same = Profile.Here(Player.m_localPlayer).FirstOrDefault(pr => string.Equals(pr.Name, wanted, StringComparison.OrdinalIgnoreCase));
                    if (same != null) { _note = T("aicompanion_menu_same_name", same.Name); return; }
                    Player p = Player.m_localPlayer;
                    Humanoid c = p != null ? Companion.Summon(p, (_nameField ?? "").Trim()) : null;
                    if (c == null) return;
                    _tab = Tab.Looks;
                    OpenMenuFor(p, c);
                    string took = Home.SetUp(c);
                    Talk.Tell(c, took != null ? Words.P("aicompanion_menu_hello_took", took) : Words.P("aicompanion_menu_hello"));
                }, 38f);
            EndCard();
        }

        /// <summary>The name field of the summon panel: "Name" and the game's own text field.</summary>
        private void NameField(Action submit)
        {
            _sig.Append("|f");   // (not what is typed: the window isn't built again while typing anyway)
            if (_make)
            {
                GameUi.Label(_in, Loc("$menu_name"), new Vector2(_left, -_y - 8f), new Vector2(84f, 24f), 17f, GameUi.Text).fontStyle = FontStyles.Bold;
                _field = GameUi.TextField(_in, new Vector2(_left + 90f, -_y), new Vector2(Mathf.Min(320f, _width - 90f), 38f), _nameField, 24, v => _nameField = v,
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
            Btn(Loc("$menu_close"), closeX, 18f, 110f, 36f, () => _pending = CloseMenu);
            if (_renaming)
            {
                if (_make) _field = GameUi.TextField(_window, new Vector2(x, -20f), new Vector2(240f, 38f), _nameField, 24, v => _nameField = v, _ => SaveName(), 20f);
                Btn(Loc("$menu_manualsave"), x + 248f, 20f, 96f, 38f, SaveName, true);
                Btn(Loc("$menu_cancel"), x + 350f, 20f, 110f, 38f, () => { _renaming = false; _nameField = Companion.NameOf(_shown); });
            }
            else
            {
                float nameW = 0f, room = closeX - x - 144f;
                if (_make)
                {
                    TMP_Text t = GameUi.Label(_window, name, new Vector2(x, -14f), new Vector2(room, 58f), 32f, GameUi.Warm, GameUi.Norse);
                    t.enableWordWrapping = false;
                    t.overflowMode = TextOverflowModes.Ellipsis;
                    nameW = Mathf.Min(t.GetPreferredValues(name, 2000f, 0f).x, room);
                }
                if (Mine) Btn(Loc("$hud_rename"), x + nameW + 14f, 22f, 120f, 30f, () => { _renaming = true; _focusField = true; _nameField = Companion.NameOf(_shown); }, size: 16f);
            }
            if (_make)
            {
                TMP_Text status = Line(_window, x, 62f, closeX - x - 8f, 18f, GameUi.Good);
                _live.Add(() => { string s = Companion.StatusOf(c); Set(status, string.IsNullOrEmpty(s) ? T("aicompanion_menu_idle") : Capital(Words.Show(s))); });
            }
            if (owner)
            {
                BrainState js = Brain.Get(c);
                Work.JobText(js, out float jobDone);   // (only whether it has a progress bar: the words change in place)
                bool stoppable = js.Area != null || js.PickQueue.Count > 0 || js.Task != null && js.Task.Ordered;
                _sig.Append("|J").Append(jobDone >= 0f).Append(stoppable && Mine);
                float jobW = closeX - x - (stoppable && Mine ? 180f : 8f) + 110f;
                if (_make)
                {
                    TMP_Text job = Line(_window, x, 88f, jobW, 16f, GameUi.Text);
                    RectTransform bar = jobDone >= 0f ? GameUi.Bar(_window, new Vector2(x, -110f), new Vector2(Mathf.Min(jobW, 400f), 10f), jobDone, GameUi.Good, null) : null;
                    _live.Add(() =>
                    {
                        Set(job, T("aicompanion_menu_job", Words.Show(Work.JobText(Brain.Get(c), out float done))));
                        if (bar != null) GameUi.SetBar(bar, done);
                    });
                }
                if (stoppable && Mine) Btn(T("aicompanion_menu_stop_job"), w - Pad - 170f, 84f, 170f, 30f, () => _pending = () => Work.StopJob(Brain.Get(_shown)), size: 15f);
            }

            // health, stamina, eitr
            _y = 128f;
            LiveBar(() => c.GetHealth() / Mathf.Max(1f, c.GetMaxHealth()), () => $"{Loc("$se_health")}  {c.GetHealth():0} / {c.GetMaxHealth():0}", HealthRed);
            LiveBar(() => Stamina.Get(c) / Mathf.Max(1f, Stamina.Max(c)), () => $"{Loc("$se_stamina")}  {Stamina.Get(c):0} / {Stamina.Max(c):0}", StaminaYellow);
            bool eitr = Eitr.Max(c) > 0f;
            _sig.Append(eitr);
            if (eitr) LiveBar(() => Eitr.Get(c) / Mathf.Max(1f, Eitr.Max(c)), () => $"{Loc("$se_eitr")}  {Eitr.Get(c):0} / {Eitr.Max(c):0}", EitrPurple);
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
                TabBtn(Named("aicompanion_menu_tab_", t), _left + i * (tw + 6f), _y, tw, 38f, _tab == t, () => _tab = t, 17f);
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

            BeginCard(T("aicompanion_menu_now"));
            if (!here) Note(T("aicompanion_menu_brain_elsewhere"), GameUi.Dim);
            else if (st.InCombat)
            {
                Note(T("aicompanion_menu_fighting", string.Join(", ", st.Enemies.Where(e => e != null).Select(e => Thing(st.Label(e))))), GameUi.Text, bold: true);
                Note(Capital(Words.Show(d.Describe(st.Label))), GameUi.Good);
                if (!string.IsNullOrEmpty(d.Note)) Note(Capital(Words.Show(d.Note)), GameUi.Dim);
            }
            else Note(T("aicompanion_menu_not_fighting", EngageRange.Value.ToString("0")), GameUi.Dim);
            _y += 4f;
            OrderButtons(c);
            EndCard();

            BeginCard(T("aicompanion_menu_food_rest"));
            if (!here) Note(T("aicompanion_menu_shows_elsewhere"), GameUi.Dim);
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
                    InventoryElement e = GameUi.Cell(_in, pos, meals[i].Item, T("aicompanion_menu_minutes_short", Mathf.CeilToInt(meals[i].Time / 60f)), false, null);
                    GuiBar left = e.m_durability;
                    left.gameObject.SetActive(true);
                    int k = i;
                    void Show() { List<Food.Meal> now = Food.Meals(c); if (k < now.Count) { left.SetValue(now[k].Fraction); left.SetColor(now[k].Fraction < 0.2f ? GameUi.Warn : GameUi.Good); } }
                    Show();
                    _live.Add(Show);
                }
                if (meals.Count == 0) Note(T("aicompanion_menu_hungry", BaseHealth.Value.ToString("0"), BaseStamina.Value.ToString("0")), GameUi.Warn, indent: 3f * s + 10f);
                else Note(T("aicompanion_menu_meals", string.Join(", ", meals.Select(m => T("aicompanion_menu_meal", Loc(m.Item.m_shared.m_name), Mathf.CeilToInt(m.Time / 60f))))), GameUi.Text, indent: 3f * s + 10f);
                _y = Mathf.Max(_y, top + 64f) + 8f;
                if (Rest.IsRested(c)) Note(T("aicompanion_menu_rested", Mathf.CeilToInt(Rest.Left(c) / 60f)), GameUi.Good);
                else Note(T("aicompanion_menu_not_rested"), GameUi.Dim);
            }
            EndCard();

            BeginCard(T("aicompanion_menu_effects"));
            var effects = here ? c.GetSEMan().GetStatusEffects().Where(se => se != null && se.m_icon != null).ToList() : new List<StatusEffect>();
            if (!here) Note(T("aicompanion_menu_shows_elsewhere"), GameUi.Dim);
            else if (effects.Count == 0) Note(T("aicompanion_menu_no_effects"), GameUi.Dim);
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

            BeginCard(T("aicompanion_menu_recently"));
            if (st.History.Count == 0) Note(T("aicompanion_menu_nothing_yet"), GameUi.Dim);
            foreach (string line in st.History.Take(8)) Note(Capital(Words.Show(line)), GameUi.Text);
            EndCard();
        }

        /// <summary>The two orders that matter (come with me, go home), big; stay, guard and come here smaller beneath them.</summary>
        private void OrderButtons(Humanoid c)
        {
            Order order = Companion.OrderOf(c);
            float half = (_width - 8f) / 2f;
            Btn(T("aicompanion_menu_come_with_me"), _left, _y, half, 42f, () => _pending = () => GiveOrder(h => Home.Follow(h)), order == Order.Follow, size: 19f);
            Btn(T("aicompanion_menu_go_home"), _left + half + 8f, _y, half, 42f, () => _pending = () => GiveOrder(h => Home.GoHome(h)), order == Order.Gather, size: 19f);
            _y += 48f;
            if (_make) GameUi.Label(_in, T("aicompanion_menu_more"), new Vector2(_left, -_y - 6f), new Vector2(76f, 24f), 16f, GameUi.Dim);
            float third = (_width - 82f - 12f) / 3f, bx = _left + 82f;
            Btn(T("aicompanion_menu_stay_here"), bx, _y, third, 32f, () => _pending = () => Change(z => z.Set(Keys.Order, (int)AICompanion.Order.Stay)), order == Order.Stay, size: 16f);
            Btn(T("aicompanion_menu_guard_spot"), bx + third + 6f, _y, third, 32f,
                () => _pending = () => Change(z => { z.Set(Keys.Order, (int)AICompanion.Order.Guard); z.Set(Keys.Post, c.transform.position); }), order == Order.Guard, size: 16f);
            Btn(T("aicompanion_menu_come_here"), bx + (third + 6f) * 2f, _y, third, 32f, () => _pending = ComeHere, size: 16f);
            _y += 38f;
            Note(T("aicompanion_menu_hold_key", MenuKey.Value), GameUi.Dim);
        }

        private void GiveOrder(Func<Humanoid, bool> order)
        {
            if (!Commandable) { _note = T("aicompanion_menu_owner_orders"); return; }
            if (!order(_shown)) _note = T("aicompanion_menu_busy");
        }

        // ==== Stats ========================================================================================

        private void DrawStats(Humanoid c)
        {
            BrainState st = Brain.Get(c);
            bool here = c.GetComponent<ZNetView>().IsOwner();
            List<Food.Meal> meals = Food.Meals(c);

            BeginCard(T("aicompanion_menu_body"));
            Stats((Loc("$se_health"), null, () => $"{c.GetHealth():0}/{c.GetMaxHealth():0}"),
                  (Loc("$se_stamina"), null, () => $"{Stamina.Get(c):0}/{Stamina.Max(c):0}"),
                  (Loc("$se_eitr"), null, () => Eitr.Max(c) > 0f ? $"{Eitr.Get(c):0}/{Eitr.Max(c):0}" : "-"),
                  (T("aicompanion_menu_carrying"), null, () => $"{Carry.Weight(c):0}/{Carry.Max(c):0}"));
            float regen = meals.Sum(m => m.Item.m_shared.m_foodRegen) * (Rest.IsRested(c) ? 1.5f : 1f);
            Note(T("aicompanion_menu_before_food", BaseHealth.Value.ToString("0"), BaseStamina.Value.ToString("0")), GameUi.Dim);
            Note(regen > 0f ? T(Rest.IsRested(c) ? "aicompanion_menu_heals_rested" : "aicompanion_menu_heals", regen.ToString("0.#")) : T("aicompanion_menu_heals_nothing"), regen > 0f ? GameUi.Text : GameUi.Warn);
            EndCard();

            BeginCard(Loc("$hud_food"));
            if (!here) Note(T("aicompanion_menu_shows_elsewhere"), GameUi.Dim);
            else if (meals.Count == 0) Note(T("aicompanion_menu_nothing_eaten"), GameUi.Warn);
            foreach (Food.Meal m in meals)
            {
                ItemDrop.ItemData.SharedData f = m.Item.m_shared;
                string gives = f.m_foodEitr > 0f ? T("aicompanion_menu_food_gives_eitr", f.m_food.ToString("0"), f.m_foodStamina.ToString("0"), f.m_foodEitr.ToString("0"), f.m_foodRegen.ToString("0.#"))
                                                 : T("aicompanion_menu_food_gives", f.m_food.ToString("0"), f.m_foodStamina.ToString("0"), f.m_foodRegen.ToString("0.#"));
                Note($"{Loc(f.m_name)}:  {gives}  ·  {T("aicompanion_menu_min_left", Mathf.CeilToInt(m.Time / 60f))}", GameUi.Text);
            }
            EndCard();

            BeginCard(T("aicompanion_menu_fighting_card"));
            ItemDrop.ItemData melee = Companion.BestMelee(c), bow = Companion.BestRanged(c), shield = Companion.Shield(c);
            Stats((Loc("$item_armor"), $"{Companion.Armor(c):0}", null),
                  (T("aicompanion_menu_block"), shield != null ? $"{shield.GetBlockPower(Skill.Get(c, Skills.SkillType.Blocking) / 100f):0}" : melee != null ? $"{melee.GetBlockPower(Skill.Get(c, Skills.SkillType.Blocking) / 100f):0}" : "-", null),
                  (T("aicompanion_menu_kills"), Companion.Zdo(c).GetInt(Keys.Kills, 0).ToString(), null),
                  (T("aicompanion_menu_fights"), here ? st.Fights.ToString() : "-", null));
            if (melee != null) Note(T("aicompanion_menu_damage_hit", Loc(melee.m_shared.m_name), Damage(c, melee), Words.Mid(Loc("$skill_" + melee.m_shared.m_skillType.ToString().ToLower()))), GameUi.Text);
            else Note(T("aicompanion_menu_no_weapon"), GameUi.Warn);
            if (bow != null) Note(T("aicompanion_menu_damage_shot", Loc(bow.m_shared.m_name), Damage(c, bow)), GameUi.Text);
            Note(shield != null ? T("aicompanion_menu_blocks_shield", Words.Mid(Loc(shield.m_shared.m_name))) : melee != null ? T("aicompanion_menu_blocks_weapon") : T("aicompanion_menu_blocks_nothing"), GameUi.Dim);
            if (here && st.Parries + st.Blocks > 0) Note(T("aicompanion_menu_session_parries", st.Parries, st.Blocks), GameUi.Text);
            var tally = Journal.Tally(c);
            var feared = tally.Where(kv => kv.Key.StartsWith("killedby:")).Select(kv => Words.Mid(Thing(kv.Key.Substring(9)))).Distinct().ToList();
            if (feared.Count > 0) Note(T("aicompanion_menu_careful_with", string.Join(", ", feared)), GameUi.Dim);
            EndCard();

            BeginCard(T("aicompanion_menu_last_fights"));
            if (!here || st.FightLog.Count == 0) Note(here ? T("aicompanion_menu_none_this_session") : T("aicompanion_menu_shows_elsewhere"), GameUi.Dim);
            else foreach (string f in st.FightLog) Note(f, GameUi.Text);
            EndCard();

            BeginCard(T("aicompanion_menu_resistances"));
            HitData.DamageModifiers mods = Gear.Modifiers(c);
            var lines = new List<string>();
            foreach (HitData.DamageType t in new[] { HitData.DamageType.Blunt, HitData.DamageType.Slash, HitData.DamageType.Pierce, HitData.DamageType.Fire, HitData.DamageType.Frost, HitData.DamageType.Lightning, HitData.DamageType.Poison, HitData.DamageType.Spirit })
            {
                HitData.DamageModifier mod = mods.GetModifier(t);
                if (mod != HitData.DamageModifier.Normal)
                    lines.Add($"{Loc("$inventory_" + t.ToString().ToLowerInvariant())}: {Words.Mid(mod == HitData.DamageModifier.Ignore ? T("aicompanion_menu_mod_ignore") : Loc("$inventory_" + mod.ToString().ToLowerInvariant()))}");
            }
            Note(lines.Count > 0 ? string.Join("   ", lines) : T("aicompanion_menu_no_resistances"), lines.Count > 0 ? GameUi.Text : GameUi.Dim);
            EndCard();

            BeginCard(Loc("$inventory_skills"));
            Note(T("aicompanion_menu_skills_about"), GameUi.Dim);
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
            Note(T("aicompanion_menu_skills_legend"), GameUi.Dim);
            EndCard();
        }

        /// <summary>"14": a hit's damage with its skill (40% of the weapon's at skill 0, all of it at 100, the player's rule).</summary>
        private static string Damage(Humanoid c, ItemDrop.ItemData w) =>
            $"{w.GetDamage().GetTotalDamage() * Mathf.Lerp(0.4f, 1f, Skill.Get(c, w.m_shared.m_skillType) / 100f):0}";

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
            BeginCard(T("aicompanion_menu_what_it_does"));
            OrderButtons(c);
            Note(order switch
            {
                AICompanion.Order.Gather => T("aicompanion_menu_order_gather"),
                AICompanion.Order.Stay => T("aicompanion_menu_order_stay"),
                AICompanion.Order.Guard => T("aicompanion_menu_order_guard"),
                _ => T("aicompanion_menu_order_follow"),
            }, GameUi.Text);
            EndCard();

            if (Commandable && c.GetComponent<ZNetView>().IsOwner())
            {
                BeginCard(T("aicompanion_menu_errands"));
                Note(T("aicompanion_menu_errands_about", Companion.NameOf(c)), GameUi.Dim);
                float bw = (_width - 12f) / 3f;
                for (int k = 0; k < Errands.All.Length; k++)
                {
                    Errand pick = Errands.All[k].What;
                    Btn(Errands.All[k].Label, _left + (k % 3) * (bw + 6f), _y, bw, 32f, () => _pending = () => _note = Errands.Run(Brain.Get(_shown), pick) ?? "", size: 16f);
                    if (k % 3 == 2 || k == Errands.All.Length - 1) _y += 38f;
                }
                EndCard();
            }

            BeginCard(T("aicompanion_menu_how_it_fights"));
            Style style = Companion.Chosen(c);
            TabBtn(T("aicompanion_menu_style_auto"), _left, _y, _width, 36f, style == Style.Auto, () => _pending = () => Change(z => z.Set(Keys.Style, (int)Style.Auto)));
            _y += 42f;
            Style[] styles = { Style.Aggressive, Style.Balanced, Style.Defensive, Style.Passive };
            Choices(styles.Select(s => Named("aicompanion_menu_style_", s)).ToArray(), Array.IndexOf(styles, style), i => { Style pick = styles[i]; _pending = () => Change(z => z.Set(Keys.Style, (int)pick)); });
            Note(style switch
            {
                Style.Aggressive => T("aicompanion_menu_style_aggressive_about"),
                Style.Defensive => T("aicompanion_menu_style_defensive_about"),
                Style.Passive => T("aicompanion_menu_style_passive_about"),
                Style.Balanced => T("aicompanion_menu_style_balanced_about"),
                _ => T("aicompanion_menu_style_auto_about"),
            }, GameUi.Text);
            if (style == Style.Auto && !string.IsNullOrEmpty(Brain.Get(c)?.AutoNote)) Note(T("aicompanion_menu_right_now", Words.Show(Brain.Get(c).AutoNote)), GameUi.Good);
            _y += 4f;
            int retreat = Companion.RetreatOf(c);
            Stepper(T("aicompanion_menu_falls_back"), T("aicompanion_menu_percent_health", retreat), () => Change(z => z.Set(Keys.Retreat, Mathf.Clamp(retreat - 5, 0, 90))), () => Change(z => z.Set(Keys.Retreat, Mathf.Clamp(retreat + 5, 0, 90))));
            Note(T("aicompanion_menu_runs_below_half"), GameUi.Dim);
            Stepper(T("aicompanion_menu_fights_within"), T("aicompanion_menu_metres", EngageRange.Value.ToString("0")), () => { EngageRange.Value = Mathf.Clamp(EngageRange.Value - 2f, 4f, 50f); SaveSettings(); },
                    () => { EngageRange.Value = Mathf.Clamp(EngageRange.Value + 2f, 4f, 50f); SaveSettings(); });
            Note(T("aicompanion_menu_fights_within_about"), GameUi.Dim);
            bool potions = Companion.Potions(c), protect = Companion.Protect(c);
            Check(T("aicompanion_menu_check_potions"), potions, () => _pending = () => Change(z => z.Set(Keys.Potions, !potions)));
            Check(T("aicompanion_menu_check_protect"), protect, () => _pending = () => Change(z => z.Set(Keys.Protect, !protect)));
            EndCard();

            BeginCard(T("aicompanion_menu_habits"));
            bool loot = Loot.On(c), friends = Companion.Zdo(c).GetBool(Keys.Friends, false);
            Check(T("aicompanion_menu_check_loot"), loot, () => _pending = () => Change(z => z.Set(Loot.Key, !loot)));
            bool autoHome = Following.AutoHome(c);
            Check(T("aicompanion_menu_check_autohome"), autoHome, () => _pending = () => Change(z => z.Set(Following.AutoHomeKey, !autoHome)));
            Check(T("aicompanion_menu_check_decisions"), ShowDecisions.Value, () => _pending = () => { ShowDecisions.Value = !ShowDecisions.Value; SaveSettings(); });
            bool chatty = Talk.Chatty(c);
            Check(T("aicompanion_menu_check_chatty"), chatty, () => _pending = () => Change(z => z.Set(Talk.ChattyKey, !chatty)));
            if (Mine) Check(T("aicompanion_menu_check_friends"), friends, () => _pending = () => Companion.Write(c, z => z.Set(Keys.Friends, !friends)));
            EndCard();

            BeginCard(T("aicompanion_menu_talking"));
            Note(T("aicompanion_menu_talking_about", Companion.NameOf(c)), GameUi.Text);
            EndCard();

            if (Mine)
            {
                BeginCard(T("aicompanion_menu_send_away"));
                Note(T("aicompanion_menu_send_away_about"), GameUi.Dim);
                Btn(T("aicompanion_menu_send_home"), _left, _y, 200f, 32f, () => _pending = () => { if (Companion.Dismiss(c, out string why)) CloseMenu(); else _note = Words.Show(why); });
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

            BeginCard(T("aicompanion_menu_bed_chests"));
            if (bed)
            {
                Vector3 spot = z.GetVec3(Keys.BedPos, Vector3.zero);
                Note(T("aicompanion_menu_bed_is", Vector3.Distance(spot, p.transform.position).ToString("0")), GameUi.Good);
            }
            else Note(T("aicompanion_menu_no_bed"), GameUi.Warn);
            List<Container> chests = Home.Chests(c);
            if (chests.Count == 0) Note(T("aicompanion_menu_no_chests"), GameUi.Dim);
            foreach (Container ch in chests)
            {
                Inventory inv = ch.GetInventory();
                Note(T("aicompanion_menu_chest_slots", Loc(ch.m_name), inv.NrOfItems(), inv.GetWidth() * inv.GetHeight()), GameUi.Text);
            }
            _y += 4f;
            if (Mine && (!bed || chests.Count == 0))
                WideBtn(T(bed ? "aicompanion_menu_take_chests" : "aicompanion_menu_find_bed"), true,
                    () => _pending = () => { string took = Home.SetUp(c); if (took != null) Talk.Tell(c, took); else _note = T(bed ? "aicompanion_menu_no_empty_chest" : "aicompanion_menu_no_free_bed"); });
            if (Mine)
            {
                float letGo = bed ? 200f : 0f;
                Btn(T("aicompanion_menu_choose_bed"), _left, _y, _width - (bed ? letGo + 6f : 0f), 34f, () => _pending = () => { Home.StartAssign(c); CloseMenu(); });
                if (bed) Btn(T("aicompanion_menu_let_bed_go"), _left + _width - letGo, _y, letGo, 34f, () => _pending = () => { Bed b = Home.BedOf(c); if (b != null) Home.ToggleBed(b, c); else Change(cz => cz.Set(Keys.HasBed, false)); });
                _y += 40f;
                Note(T("aicompanion_menu_choosing_about", Companion.NameOf(c), MenuKey.Value), GameUi.Dim);
            }
            EndCard();

            BeginCard(T("aicompanion_menu_your_chests"));
            Note(T("aicompanion_menu_your_chests_about"), GameUi.Dim);
            bool stow = Work.Stows(c), pantry = Work.UsesPantry(c), armory = Armory.Allowed(c);
            Check(T("aicompanion_menu_check_stow"), stow, () => _pending = () => Change(cz => cz.Set(Work.StowKey, !stow)));
            Check(T("aicompanion_menu_check_pantry"), pantry, () => _pending = () => Change(cz => cz.Set(Work.PantryKey, !pantry)));
            Check(T("aicompanion_menu_check_armory"), armory, () => _pending = () => Change(cz => cz.Set(Armory.Key, !armory)));
            Note(T(pantry ? "aicompanion_menu_pantry_on" : "aicompanion_menu_pantry_off"), GameUi.Dim);
            EndCard();

            bool living = Companion.OrderOf(c) == AICompanion.Order.Gather;
            BeginCard(T("aicompanion_menu_missions"));
            Goal goal = st.Goal;
            List<Mission> missions = Missions.All(c);
            Mission current = missions.LastOrDefault(m => m.Status == "active");
            if (!c.GetComponent<ZNetView>().IsOwner()) Note(T("aicompanion_menu_shows_elsewhere"), GameUi.Dim);
            else if (goal == null) Note(T(living ? "aicompanion_menu_no_mission" : "aicompanion_menu_mission_at_home"), GameUi.Dim);
            else
            {
                if (current != null && current.StartCost > 0)
                {
                    float done = Mathf.Clamp01(1f - current.Cost / (float)current.StartCost);
                    Note(T("aicompanion_menu_mission_since", current.Day, Mathf.RoundToInt(done * 100f)), GameUi.Dim);
                    if (_make) GameUi.Bar(_in, new Vector2(_left, -_y), new Vector2(_width, 12f), done, GameUi.Good, null);
                    _y += 18f;
                }
                string what = Capital(Words.Show(goal.What));
                Note(goal.Station != null ? T("aicompanion_menu_goal_at", what, Words.Mid(Loc(goal.Station.m_name))) : what, GameUi.Text, bold: true);
                if (goal.Raw.Count > 0) Note(T("aicompanion_menu_gathering", Words.Show(goal.RawText())), GameUi.Text);
                if (goal.Steps.Count > 0) Note(T("aicompanion_menu_then_makes", string.Join(", ", goal.Steps.Select(s => Words.Mid(Loc(s.Key.m_item.m_itemData.m_shared.m_name))))), GameUi.Text);
                if (goal.Smelt.Count > 0) Note(T(goal.Smelt.Count == 1 ? "aicompanion_menu_smelt_one" : "aicompanion_menu_smelt_many", Capital(And(goal.Smelt.Select(Thing).ToList()))), GameUi.Dim);
                if (goal.Ask.Count > 0) Note(T("aicompanion_menu_needs_from_you", string.Join(", ", goal.Ask.Select(Thing))), GameUi.Warn);
                var next = st.Candidates.Where(g => g != goal).Take(2).Select(g => Words.Show(g.What)).ToList();
                if (next.Count == 1) Note(T("aicompanion_menu_after_that", next[0]), GameUi.Dim);
                else if (next.Count > 1) Note(T("aicompanion_menu_after_that_two", next[0], next[1]), GameUi.Dim);
            }
            var past = missions.Where(m => m.Status != "active").Reverse().Take(5).ToList();
            if (past.Count > 0)
            {
                _y += 4f;
                Note(T("aicompanion_menu_lately"), GameUi.Dim);
                foreach (Mission m in past)
                    Note(m.Status == "done" ? T("aicompanion_menu_mission_done", Words.Show(m.What), Words.Show(m.Why)) : T("aicompanion_menu_mission_dropped", Words.Show(m.What), Words.Show(m.Why)), m.Status == "done" ? GameUi.Good : GameUi.Text);
            }
            Note(T("aicompanion_menu_missions_about"), GameUi.Dim);
            EndCard();

            BeginCard(T("aicompanion_menu_living"));
            if (!living)
            {
                Note(T("aicompanion_menu_not_living"), GameUi.Dim);
                Btn(T("aicompanion_menu_go_home"), _left, _y, 200f, 32f, () => _pending = () => GiveOrder(h => Home.GoHome(h)), true);
                _y += 38f;
            }
            Vector3 center = Work.Center(c);
            bool homeSet = z.GetBool(Work.HomeSetKey, false);
            Note(homeSet ? T("aicompanion_menu_home_set", Vector3.Distance(center, p.transform.position).ToString("0"))
                : bed ? T("aicompanion_menu_home_bed")
                : T("aicompanion_menu_home_none", center.x.ToString("0"), center.z.ToString("0")), GameUi.Dim);
            if (Commandable)
            {
                float clear = homeSet ? 210f : 0f;
                Btn(T("aicompanion_menu_make_home"), _left, _y, _width - (homeSet ? clear + 6f : 0f), 32f,
                    () => _pending = () => { Vector3 at = Player.m_localPlayer.transform.position; Change(cz => { cz.Set(Work.HomeSpotKey, at); cz.Set(Work.HomeSetKey, true); cz.Set(Keys.Post, at); }); }, true);
                if (homeSet) Btn(T(bed ? "aicompanion_menu_back_to_bed" : "aicompanion_menu_clear"), _left + _width - clear, _y, clear, 32f, () => _pending = () => Change(cz => cz.Set(Work.HomeSetKey, false)));
                _y += 38f;
            }
            int radius = Work.RadiusOf(c);
            Stepper(T("aicompanion_menu_goes_as_far"), T("aicompanion_menu_metres", radius), () => Change(cz => cz.Set(Keys.Radius, Mathf.Clamp(radius - 5, 10, 80))), () => Change(cz => cz.Set(Keys.Radius, Mathf.Clamp(radius + 5, 10, 80))));
            EndCard();

            // Home duties: a short list in the order it does them, each with a stockpile it keeps in its chest.
            List<Duties.Entry> duties = Duties.Read(z);
            Duties.Entry working = Duties.Active(c);
            BeginCard(T("aicompanion_menu_duties"));
            Note(T("aicompanion_menu_duties_about"), GameUi.Text);
            bool toYours = Duties.ToYours(c);
            Choices(new[] { T("aicompanion_menu_put_yours"), T("aicompanion_menu_put_its_own") }, toYours ? 0 : 1, i => { if (Commandable) _pending = () => Change(cz => cz.Set(Duties.DestKey, i)); });
            if (duties.Any(x => x.On && x.Duty != Duty.Build) && !Duties.HasPlace(c))
                Note(T(toYours ? (Work.Stows(c) ? "aicompanion_menu_duties_no_chest" : "aicompanion_menu_duties_stow_off") : "aicompanion_menu_duties_no_own_chest"), GameUi.Warn);
            else if (toYours) Note(T("aicompanion_menu_duties_counts"), GameUi.Dim);
            const float sub = 30f;   // (what belongs to a duty is set in under it)
            for (int di = 0; di < duties.Count; di++)
            {
                Duties.Entry e = duties[di];
                Duty dd = e.Duty;
                // its place in the list, on or off, and moving it up or down
                _sig.Append("|#").Append(di);
                if (_make) GameUi.Label(_in, (di + 1).ToString(), new Vector2(_left, -_y - 4f), new Vector2(24f, 24f), 18f, GameUi.Gold);
                float rowH = CheckAt(Duties.Label(dd), e.On, () => { if (Commandable) _pending = () => Change(cz => { var l = Duties.Read(cz); var x = l.First(a => a.Duty == dd); x.On = !x.On; Duties.Write(cz, l); }); },
                    _left + sub, _y, _width - sub - 160f);
                Btn(T("aicompanion_menu_up"), _left + _width - 154f, _y, 72f, 30f,
                    () => _pending = () => Change(cz => { var l = Duties.Read(cz); int k = l.FindIndex(a => a.Duty == dd); if (k > 0) { var t = l[k]; l[k] = l[k - 1]; l[k - 1] = t; Duties.Write(cz, l); } }), enabled: di > 0, size: 15f);
                Btn(T("aicompanion_menu_down"), _left + _width - 78f, _y, 78f, 30f,
                    () => _pending = () => Change(cz => { var l = Duties.Read(cz); int k = l.FindIndex(a => a.Duty == dd); if (k >= 0 && k < l.Count - 1) { var t = l[k]; l[k] = l[k + 1]; l[k + 1] = t; Duties.Write(cz, l); } }), enabled: di < duties.Count - 1, size: 15f);
                _y += Mathf.Max(rowH, 30f) + 6f;
                if (!e.On) continue;
                if (dd == Duty.Build)
                {
                    Note(Duties.Status(c, e, working), e == working ? GameUi.Good : GameUi.Dim, indent: sub);
                    if (!Building.Available) Note(T("aicompanion_menu_install_buildorders"), GameUi.Warn, indent: sub);
                    continue;
                }
                int step = Duties.Step(dd), max = Duties.Max(dd);
                Stepper(T(toYours ? "aicompanion_menu_keep_in_yours" : "aicompanion_menu_keep_in_its"), Duties.Amount(dd, e.Target),
                    () => Change(cz => { var l = Duties.Read(cz); var x = l.First(a => a.Duty == dd); x.Target = Mathf.Clamp(x.Target - step, step, max); Duties.Write(cz, l); }),
                    () => Change(cz => { var l = Duties.Read(cz); var x = l.First(a => a.Duty == dd); x.Target = Mathf.Clamp(x.Target + step, step, max); Duties.Write(cz, l); }), sub);
                Note(Duties.Status(c, e, working), e == working ? GameUi.Good : GameUi.Dim, indent: sub);
                if (dd == Duty.Wood && Work.Axe(c) == null) Note(T("aicompanion_menu_needs_axe"), GameUi.Warn, indent: sub);
                if (dd == Duty.Mining && Work.Pickaxe(c) == null) Note(T("aicompanion_menu_needs_pickaxe"), GameUi.Warn, indent: sub);
                if (dd == Duty.Food && Companion.BestRanged(c) == null && Companion.BestMelee(c) == null) Note(T("aicompanion_menu_needs_weapon"), GameUi.Warn, indent: sub);
            }
            Note(T("aicompanion_menu_duties_cook"), GameUi.Dim);
            EndCard();

            BeginCard(T("aicompanion_menu_its_food"));
            bool frugal = AICompanion.Food.Frugal(c);
            Check(T("aicompanion_menu_check_frugal"), frugal, () => _pending = () => Change(cz => cz.Set(AICompanion.Food.FrugalKey, !frugal)));
            Note(T(frugal ? "aicompanion_menu_frugal_on" : "aicompanion_menu_frugal_off"), GameUi.Dim);
            int ration = Work.RationOf(c);
            Note(Work.UsesPantry(c) ? T("aicompanion_menu_today_took", AICompanion.Food.AteToday(c), Work.TakenToday(c), ration) : T("aicompanion_menu_today", AICompanion.Food.AteToday(c)), GameUi.Text);
            if (Work.UsesPantry(c))
                Stepper(T("aicompanion_menu_most_a_day"), T("aicompanion_menu_n_food", ration), () => Change(cz => cz.Set(Work.RationKey, Mathf.Clamp(ration - 2, 2, 60))), () => Change(cz => cz.Set(Work.RationKey, Mathf.Clamp(ration + 2, 2, 60))));
            EndCard();

            Job jobs = Work.JobsOf(c);
            BeginCard(T(duties.Any(x => x.On && x.Duty != Duty.Build) ? "aicompanion_menu_other_jobs" : "aicompanion_menu_jobs"));
            Job auto = Work.AutoJobs(c);
            Note(jobs == Job.None
                ? (auto == Job.None && goal == null ? T("aicompanion_menu_jobs_auto_nothing") : T("aicompanion_menu_jobs_auto", JobNames(auto | Goals.JobsFor(goal, out _))))
                : T("aicompanion_menu_jobs_chosen"), GameUi.Text);
            foreach (Job job in new[] { Job.Wood, Job.Stone, Job.Ore, Job.Forage, Job.Hunt, Job.Cook, Job.Loot })
            {
                bool on = (jobs & job) != 0;
                string tool = Work.ToolFor(c, job);
                string label = job switch
                {
                    Job.Wood => T("aicompanion_menu_job_wood"),
                    Job.Stone => T("aicompanion_menu_job_stone"),
                    Job.Ore => T("aicompanion_menu_job_ore"),
                    Job.Forage => T("aicompanion_menu_job_forage"),
                    Job.Hunt => T("aicompanion_menu_job_hunt"),
                    Job.Cook => T("aicompanion_menu_job_cook"),
                    _ => T("aicompanion_menu_job_loot"),
                };
                Job pick = job;
                Check(label, on, () => _pending = () => Change(cz => cz.Set(Keys.Jobs, cz.GetInt(Keys.Jobs, 0) ^ (int)pick)));
                if (job == Job.Wood || job == Job.Stone || job == Job.Ore) Note(T("aicompanion_menu_tool", Words.Show(tool)), Work.ToolMissing(c, job) ? GameUi.Warn : GameUi.Dim, indent: 74f);
            }
            Note(T("aicompanion_menu_tools_about"), GameUi.Dim);
            EndCard();

            BeginCard(T("aicompanion_menu_gathered"));
            if (st.Gathered.Count == 0) Note(T("aicompanion_menu_nothing_yet"), GameUi.Dim);
            else Note(string.Join(", ", st.Gathered.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {Words.Mid(Thing(kv.Key))}")), GameUi.Text);
            EndCard();

            BeginCard(T("aicompanion_menu_away"));
            Note(WhileAway.Value switch
            {
                AwayMode.Off => T("aicompanion_menu_away_off"),
                AwayMode.Real => T("aicompanion_menu_away_real"),
                _ => T("aicompanion_menu_away_mild"),
            }, GameUi.Text);
            Note(T("aicompanion_menu_away_setting", WhileAway.Value), GameUi.Dim);
            EndCard();

            BeginCard(T("aicompanion_menu_if_falls"));
            Note(T("aicompanion_menu_if_falls_about", RespawnSeconds.Value.ToString("0")), GameUi.Text);
            EndCard();
        }

        private static string JobNames(Job j) => string.Join(", ", ((Job[])Enum.GetValues(typeof(Job))).Where(x => x != Job.None && (j & x) != 0).Select(x => Named("aicompanion_menu_jobname_", x)));

        // ==== Gear =========================================================================================

        private void DrawGear(Humanoid c)
        {
            Player p = Player.m_localPlayer;
            float space = GameUi.CellSpace;

            // Its gear: slots of their own (Gear), kept when it falls; changed from here, wherever it is. The game's tooltip tells what each is.
            BeginCard(T("aicompanion_menu_its_gear"));
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
                        _pending = () => { ItemDrop.ItemData now = _shown != null ? Gear.In(_shown, slot) : null; if (now != null && !Companion.Take(_shown, Player.m_localPlayer, now, out string why) && why != null) _note = Words.Show(why); };
                        _nextCheck = 0f;
                    } : (Action)null);
                    Line(_in, pos.x - (space - 64f) / 2f, _y + 66f, space, 14f, GameUi.Dim, TextAlignmentOptions.Top).text = Words.Show(slots[j].Label);
                }
                _y += 64f + 24f;
            }
            Note(T(mine ? "aicompanion_menu_gear_take_back" : "aicompanion_menu_gear_point"), GameUi.Dim);
            EndCard();

            Inventory its = c.GetInventory();
            BeginCard(T("aicompanion_menu_its_bag", Gear.BagCount(its), its.GetWidth() * Gear.BagRows, Carry.Weight(c).ToString("0"), Carry.Max(c).ToString("0")));
            if (Carry.Over(c)) Note(T("aicompanion_menu_too_heavy"), GameUi.Warn);
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
            Note(T("aicompanion_menu_bag_about"), GameUi.Dim);
            EndCard();

            if (mine && p != null)
            {
                var yours = AppDomain.CurrentDomain.GetData("DHack.GearSlots.IsGear") as Func<ItemDrop.ItemData, bool>; // your own gear slots (GearSlots)
                var giveable = p.GetInventory().GetAllItems().Where(i => !p.IsItemEquiped(i) && !i.m_equipped && !(yours?.Invoke(i) ?? false) && Gear.IsGear(i)).ToList();
                BeginCard(T("aicompanion_menu_give_gear"));
                if (giveable.Count == 0) Note(T("aicompanion_menu_give_gear_none"), GameUi.Dim);
                else
                {
                    int per = Mathf.Max(1, Mathf.FloorToInt((_width + space - 64f) / space));
                    for (int i = 0; i < giveable.Count; i++)
                    {
                        ItemDrop.ItemData it = giveable[i];
                        AppendItem(it);
                        if (_make)
                            GameUi.Cell(_in, new Vector2(_left + i % per * space, -_y - i / per * space), it, null, false,
                                () => { _pending = () => { if (!Companion.Give(_shown, Player.m_localPlayer, it, out string why) && why != null) _note = Words.Show(why); }; _nextCheck = 0f; });
                    }
                    _y += (giveable.Count + per - 1) / per * space + 4f;
                    Note(T("aicompanion_menu_click_to_give"), GameUi.Dim);
                }
                EndCard();
            }

            BeginCard(T("aicompanion_menu_picks_up"));
            Note(T("aicompanion_menu_picks_up_about"), GameUi.Dim);
            Loot.Kinds list = Loot.List(c);
            var kinds = new[] { Loot.Kinds.Ore, Loot.Kinds.Wood, Loot.Kinds.Stone, Loot.Kinds.Food, Loot.Kinds.Creature, Loot.Kinds.Gear, Loot.Kinds.Other };
            float colW = (_width - 16f) / 2f;
            for (int i = 0; i < kinds.Length; i += 2)
            {
                float rowH = 0f;
                for (int j = i; j < Mathf.Min(i + 2, kinds.Length); j++)
                {
                    Loot.Kinds k = kinds[j];
                    string label = Named("aicompanion_menu_loot_", k);
                    rowH = Mathf.Max(rowH, CheckAt(label, (list & k) != 0, () => _pending = () => Change(z => z.Set(Loot.ListKey, z.GetInt(Loot.ListKey, (int)Loot.Default) ^ (int)k)),
                        _left + (j - i) * (colW + 16f), _y, colW));
                }
                _y += rowH + 6f;
            }
            if (!Loot.On(c)) Note(T("aicompanion_menu_loot_off"), GameUi.Warn);
            EndCard();
        }

        private void OpenGear()
        {
            Player p = Player.m_localPlayer;
            if (_shown == null || p == null) return;
            if (!Mine) { _note = T("aicompanion_menu_owner_gear"); return; }
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
            if (!Mine) { _note = T("aicompanion_menu_owner_looks"); return; }
            Humanoid c = _shown;
            if (!Companion.Write(c, _ => change(c))) _note = T("aicompanion_menu_busy");
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
            Heading(T("aicompanion_menu_how_it_looks"), beside);
            Note(T("aicompanion_menu_how_it_looks_about"), GameUi.Text, indent: beside);
            _y += 6f;
            float bw = (_width - beside - 8f) / 2f;
            Btn(T("aicompanion_menu_randomise"), _left + beside, _y, bw, 34f, () => _pending = () => { Rebind(c); LooksChange(Looks.Randomise); });
            Btn(T("aicompanion_menu_look_like_me"), _left + beside + bw + 8f, _y, bw, 34f, () => _pending = () => { Rebind(c); LooksChange(h => Looks.CopyFrom(h, Player.m_localPlayer)); });
            _y = Mathf.Max(_y + 40f, top + face + 4f);
            EndCard();

            int model = Looks.Model(c);
            BeginCard(T("aicompanion_menu_body"));
            Choices(new[] { T("aicompanion_menu_body_n", 1), T("aicompanion_menu_body_n", 2) }, model, i => _pending = () => { Rebind(c); LooksChange(h => Looks.SetModel(h, i)); });
            EndCard();

            List<ItemDrop> hairs = Looks.Hairs(), beards = Looks.Beards();
            BeginCard(Loc("$menu_hair"));
            Picker(Loc("$inventory_style"), Looks.StyleName(Looks.Hair(c), hairs), () => LooksChange(h => Looks.SetHair(h, Looks.Step(Looks.Hair(h), hairs, -1))), () => LooksChange(h => Looks.SetHair(h, Looks.Step(Looks.Hair(h), hairs, 1))));
            SliderRow(T("aicompanion_menu_colour"), () => Looks.HairTone(c), v => _pending = () => LooksChange(h => Looks.SetHairColor(h, v, Looks.HairShade(h))));
            SliderRow(T("aicompanion_menu_shade"), () => Looks.HairShade(c), v => _pending = () => LooksChange(h => Looks.SetHairColor(h, Looks.HairTone(h), v)));
            EndCard();

            if (model == 0)
            {
                BeginCard(Loc("$menu_beard"));
                Picker(Loc("$inventory_style"), Looks.StyleName(Looks.Beard(c), beards), () => LooksChange(h => Looks.SetBeard(h, Looks.Step(Looks.Beard(h), beards, -1))), () => LooksChange(h => Looks.SetBeard(h, Looks.Step(Looks.Beard(h), beards, 1))));
                EndCard();
            }

            BeginCard(T("aicompanion_menu_skin"));
            SliderRow(T("aicompanion_menu_tone"), () => Looks.SkinTone(c), v => _pending = () => LooksChange(h => Looks.SetSkin(h, v)));
            EndCard();
        }

        private void Picker(string label, string value, Action prev, Action next)
        {
            label = Words.Show(label); value = Words.Show(value);
            _sig.Append("|p").Append(label).Append(value);
            if (_make) Line(_in, _left, _y + 6f, 120f, 17f, GameUi.Text).text = label;
            Btn("<", _left + 126f, _y, 44f, 32f, () => _pending = prev);
            if (_make) Line(_in, _left + 176f, _y + 6f, 180f, 17f, GameUi.Gold, TextAlignmentOptions.Top).text = value;
            Btn(">", _left + 362f, _y, 44f, 32f, () => _pending = next);
            _y += 40f;
        }

        /// <summary>A slider (0 to 1) with its name. It is set from the companion while nobody drags it (someone else may change it).</summary>
        private void SliderRow(string label, Func<float> value, Action<float> set)
        {
            label = Words.Show(label);
            _sig.Append("|v").Append(label);
            if (_make)
            {
                Line(_in, _left, _y + 4f, 120f, 17f, GameUi.Text).text = label;
                Slider s = GameUi.Slider(_in, new Vector2(_left + 126f, -_y), new Vector2(_width - 126f, 28f), value(), set);
                _live.Add(() => { if (GameUi.Sliding) return; float now = value(); if (Mathf.Abs(s.value - now) > 0.005f) s.SetValueWithoutNotify(now); });
            }
            _y += 36f;
        }

        // ==== Journal ======================================================================================

        private void DrawJournal(Humanoid c)
        {
            var tally = Journal.Tally(c);
            int Tally(string k) => tally.TryGetValue(k, out int n) ? n : 0;
            BeginCard(T("aicompanion_menu_saga"));
            Stats((T("aicompanion_menu_days_with_you"), Journal.DaysWithYou(c).ToString(), null), (T("aicompanion_menu_kills"), Tally("kills").ToString(), null), (T("aicompanion_menu_parries"), Tally("parries").ToString(), null), (T("aicompanion_menu_falls"), Tally("deaths").ToString(), null));
            // (the names in the tally: old saves have the name in the language it was played in, newer ones the game's "$enemy_..." token)
            var slain = new Dictionary<string, int>();
            foreach (var kv in tally.Where(x => x.Key.StartsWith("kill:"))) { string who = Words.Mid(Thing(OldCreature(kv.Key.Substring(5)))); slain[who] = (slain.TryGetValue(who, out int had) ? had : 0) + kv.Value; }
            var kills = slain.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Value} {kv.Key}").ToList();
            if (kills.Count > 0) Note(T("aicompanion_menu_most_slain", string.Join(", ", kills)), GameUi.Text);
            var bosses = tally.Where(kv => kv.Key.StartsWith("boss:")).Select(kv => Thing(OldCreature(kv.Key.Substring(5)))).Distinct().ToList();
            Note(bosses.Count > 0 ? T("aicompanion_menu_bosses", string.Join(", ", bosses)) : T("aicompanion_menu_no_boss"), bosses.Count > 0 ? GameUi.Good : GameUi.Dim);
            Note(T("aicompanion_menu_made_counts", Tally("made"), Tally("upgraded"), Tally("trips")), GameUi.Text);
            var lands = tally.Where(kv => kv.Key.StartsWith("place:") && kv.Key != "place:dungeon").Select(kv => Thing(OldLand(kv.Key.Substring(6)))).Distinct().ToList();
            int dungeons = Tally("place:dungeon");
            if (lands.Count > 0) Note(dungeons == 0 ? T("aicompanion_menu_has_seen", string.Join(", ", lands)) : T(dungeons == 1 ? "aicompanion_menu_has_seen_dungeon" : "aicompanion_menu_has_seen_dungeons", string.Join(", ", lands), dungeons), GameUi.Dim);
            EndCard();

            BeginCard(T("aicompanion_menu_moments"));
            var entries = Journal.Entries(c);
            if (entries.Count == 0) Note(T("aicompanion_menu_no_moments"), GameUi.Dim);
            foreach (string e in Enumerable.Reverse(entries).Take(40))
            {
                int bar = e.IndexOf('|');
                string day = bar > 0 ? e.Substring(0, bar) : "?", text = bar > 0 ? e.Substring(bar + 1) : e;
                if (_make) Line(_in, _left, _y, 76f, 16f, GameUi.Dim).text = T("aicompanion_menu_day", day);
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
            title = Words.Show(title);
            _sig.Append("|h").Append(title);
            if (_make)
            {
                TMP_FontAsset font = title.Any(char.IsDigit) ? GameUi.Serif : GameUi.Norse;
                TMP_Text t = GameUi.Label(_in, title, new Vector2(_left + indent, -_y), new Vector2(width > 0f ? width : _width - indent, 40f), 22f, GameUi.Warm, font);   // (40: room for a Chinese heading, see GameUi.Line)
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
            text = Words.Show(text);
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
            TMP_Text t = GameUi.Label(parent, "", new Vector2(x, -y), new Vector2(w, size * 1.8f), size, color, null, align);   // (taller than the line: see GameUi.Line)
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        private static void Set(TMP_Text t, string text) { text = Words.Show(text); if (t.text != text) t.text = text; }

        private Button Btn(string label, float x, float y, float w, float h, Action click, bool lit = false, bool enabled = true, float size = 17f, Color? color = null)
        {
            label = Words.Show(label);
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
            label = Words.Show(label);
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
            label = Words.Show(label);
            _sig.Append("|k").Append(on ? '1' : '0').Append(label);
            if (!_make) return 0f;
            const float bw = 62f, bh = 30f;
            Button b = GameUi.Button(_in, Loc(on ? "$hud_on" : "$hud_off"), new Vector2(x, -y), new Vector2(bw, bh), () => { toggle(); _nextCheck = 0f; }, true, 16f);
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
            label = Words.Show(label); value = Words.Show(value);
            _sig.Append("|s").Append(label).Append(value);
            float x = _left + indent, lw = Mathf.Max(120f, Mathf.Min(300f, _width - indent - 250f));
            if (_make)
            {
                TMP_Text t = Line(_in, x, _y + 5f, lw, 17f, GameUi.Text);
                t.text = label;
                t.fontStyle = FontStyles.Bold;
            }
            Btn("-", x + lw + 8f, _y, 40f, 30f, () => _pending = down, size: 20f);
            if (_make) Line(_in, x + lw + 52f, _y + 5f, 146f, 17f, GameUi.Gold, TextAlignmentOptions.Top).text = value;
            Btn("+", x + lw + 202f, _y, 40f, 30f, () => _pending = up, size: 20f);
            _y += 36f;
        }

        /// <summary>A row of big numbers with their names under them; a live one is read again ten times a second.</summary>
        private void Stats(params (string label, string value, Func<string> live)[] stats)
        {
            for (int i = 0; i < stats.Length; i++) stats[i] = (Words.Show(stats[i].label), Words.Show(stats[i].value), stats[i].live);
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
                RectTransform bar = GameUi.Bar(_in, new Vector2(_left, -_y), new Vector2(_width, height), fraction(), color, Words.Show(text()), 15f);
                _live.Add(() => GameUi.SetBar(bar, fraction(), Words.Show(text())));
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

        /// <summary>The words for one of a set of choices: the key is the prefix and the choice's name in lower case ("aicompanion_menu_tab_" + "orders").</summary>
        private static string Named(string prefix, Enum value) => T(prefix + value.ToString().ToLowerInvariant());

        private static string Loc(string s) => Localization.instance.Localize(s);

        /// <summary>A name that may be packed text, a game "$token" (a creature's, with a number: "$enemy_greyling 2") or plain (an old save's): in the player's language.</summary>
        /// <summary>A creature counted before 0.24 under its English name ("Boar"): the game's token when it has one ("$enemy_boar"), so it
        /// shows in the player's language and joins the newer count. Other names (another language then) are shown as they were saved.</summary>
        private static string OldCreature(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('$') >= 0 || Words.Packed(s) || Localization.instance == null) return s;
            string token = "$enemy_" + s.ToLowerInvariant().Replace(" ", "");
            string shown = Localization.instance.Localize(token);
            return shown.StartsWith("[") ? s : token;
        }

        /// <summary>A land counted before 0.24 under the word the journal used then ("forest"): the game's token for it.</summary>
        private static string OldLand(string s) => s switch
        {
            "place" => "$biome_meadows", "forest" => "$biome_blackforest", "swamp" => "$biome_swamp", "mountain" => "$biome_mountain",
            "plains" => "$biome_plains", "mist" => "$biome_mistlands", "ash land" => "$biome_ashlands", "frozen north" => "$biome_deepnorth",
            "sea" => "$biome_ocean", _ => s,
        };

        private static string Thing(string s) { string t = Words.Show(s); return t != null && t.IndexOf('$') >= 0 ? Loc(t) : t; }
        /// <summary>"copper and tin", "copper, tin and iron": the words of a list in the player's language.</summary>
        private static string And(IList<string> items) =>
            items.Count == 0 ? "" : items.Count == 1 ? items[0] : T("aicompanion_menu_and", string.Join(", ", items.Take(items.Count - 1)), items[items.Count - 1]);

        private static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        /// <summary>The menu's interface, at unload (a hot reload): the window and its canvas.</summary>
        private void DestroyMenuResources() => GameUi.Destroy();
    }
}
