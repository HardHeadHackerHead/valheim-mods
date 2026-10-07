using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// The companion's menu (J, or E on it), styled like the game's own windows (its Averia font, dark wood, gold headings), in six tabs:
    ///   Status - what it is doing, its health, food and rest, effects and recent events;
    ///   Stats  - all its numbers: health, stamina, healing, carry weight, armour, block, weapon damage, resistances, record, every skill;
    ///   Orders - come with me or go home (and stay, guard, come here), how it fights, its habits, sending it away;
    ///   Home   - its bed and chests, living at home (where and what it gathers), what it gathered, what happens while you are away;
    ///   Gear   - what it wears, its bag, giving it things, and what it picks up off the ground;
    ///   Looks  - body, hair, beard, skin and hair colour;
    ///   Journal - its life so far: days with you, kills, parries, bosses, deaths, gear made, and the moments worth remembering.
    /// With no companion of yours nearby it shows the summon panel.
    /// </summary>
    public partial class Plugin
    {
        private enum Tab { Status, Stats, Orders, Home, Gear, Looks, Journal }

        internal static bool MenuOpen;
        private Humanoid _shown;          // null: the summon panel
        private long _rebind;             // a companion being remade (new body): show it again when it is back
        private float _rebindUntil;
        private Tab _tab = Tab.Status;
        private Rect _rect;
        private bool _placed, _renaming, _showLog, _showJson, _showAdvanced;
        private Action _pending;          // button actions run in Update, not in the middle of drawing
        private string _nameField = "", _note = "", _testResult = "", _hover = "";
        private Vector2 _scroll, _logScroll, _jsonScroll;
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private GUIStyle _statNum, _title, _h2, _text, _bold, _dim, _small, _good, _warn, _bad, _mono, _button, _buttonOn, _tab0, _tab1, _check, _field, _card, _row, _rowOn, _num;

        // Valheim's colours: dark wood, a warm brass edge, gold headings, parchment text.
        private static readonly Color Panel = new Color(0.045f, 0.032f, 0.022f, 0.97f), Edge = new Color(0.62f, 0.45f, 0.21f, 1f);
        private static readonly Color Gold = new Color(1f, 0.78f, 0.4f), Text = new Color(0.94f, 0.89f, 0.79f), Dim = new Color(0.71f, 0.65f, 0.56f);
        private static readonly Color ColGood = new Color(0.62f, 0.88f, 0.55f), ColWarn = new Color(1f, 0.72f, 0.38f), ColBad = new Color(1f, 0.52f, 0.42f);

        private const float WindowW = 660f, Pad = 18f;
        private float Inner => _rect.width - Pad * 2f - 22f; // the width content can use (inside the scroll bar)

        internal void OpenMenuFor(Player player, Humanoid companion)
        {
            _shown = companion;
            _nameField = companion != null ? Companion.NameOf(companion) : (player.m_customData.TryGetValue("dhc_name", out string n) ? n : "Rádvar");
            _note = companion == null ? "" : (Companion.IsMine(companion, player) ? "" : $"This is {Companion.Zdo(companion).GetString(Keys.MasterName, "someone")}'s companion.");
            _testResult = "";
            _renaming = false;
            MenuOpen = true;
        }

        internal static void CloseFromEscape() => Instance?.CloseMenu();

        private void CloseMenu()
        {
            MenuOpen = false;
            _shown = null;
            _pending = null;
            _renaming = false;
        }

        private void UpdateMenu(Player player)
        {
            if (!MenuOpen) return;
            Action run = _pending;
            _pending = null;
            run?.Invoke();
            if (!MenuOpen) return;
            if (_rebind != 0L && _shown == null)
            {
                Humanoid back = Companion.All().FirstOrDefault(h => Companion.IdOf(h) == _rebind);
                if (back != null) { _shown = back; _rebind = 0L; }
                else if (Time.time > _rebindUntil) _rebind = 0L;
                return;
            }
            if (_shown != null && (_shown.IsDead() || Vector3.Distance(_shown.transform.position, player.transform.position) > 100f)) CloseMenu();
            else if (Menu.IsVisible() || InventoryGui.IsVisible()) CloseMenu();
        }

        private bool Mine => _shown != null && Companion.IsMine(_shown, Player.m_localPlayer);
        private bool Commandable => _shown != null && Companion.CanCommand(_shown, Player.m_localPlayer);

        private void Change(Action<ZDO> change)
        {
            if (!Commandable) { _note = "Only its owner can give it orders (unless they let friends do so)."; return; }
            if (!Companion.Write(_shown, change)) _note = "Someone has its things open. Try again in a moment.";
        }

        // ==== the window ===================================================================================

        private void OnGUI()
        {
            if (!MenuOpen) return;
            FreeTheMouse();
            EnsureStyles();
            Matrix4x4 previous = GUI.matrix;
            float s = Mathf.Max(0.8f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float sw = Screen.width / s, sh = Screen.height / s;
            float w = Mathf.Min(WindowW, sw - 40f), h = Mathf.Min(_shown != null ? 840f : 520f, sh - 60f);
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
            // A dark wooden board with a brass double edge, as the game's own windows have.
            Rounded(new Rect(0, 0, w, h), Panel, 6f);
            Outline(new Rect(0, 0, w, h), Edge, 6f, 2f);
            Outline(new Rect(4, 4, w - 8, h - 8), new Color(Edge.r, Edge.g, Edge.b, 0.4f), 4f, 1f);
            GUILayout.BeginArea(new Rect(Pad, 14f, w - Pad * 2f, h - 26f));
            if (_shown == null && _rebind != 0L) GUILayout.Label("Changing its body…", _title);
            else if (_shown == null) DrawSummon(); else DrawCompanion();
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, w, 36f));
        }

        // ==== summoning ====================================================================================

        private void DrawSummon()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Companions", _title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", _button, GUILayout.Width(90), GUILayout.Height(30))) CloseMenu();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            Player me = Player.m_localPlayer;
            List<Profile> mine = me != null ? Profile.Here(me) : new List<Profile>();

            foreach (Profile prof in mine)
            {
                BeginCard();
                Humanoid here = Companion.All().FirstOrDefault(h => Companion.IdOf(h) == prof.Id);
                if (here != null)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(prof.Name, _h2);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Open", _buttonOn, GUILayout.Width(100), GUILayout.Height(28))) { Humanoid h2 = here; _pending = () => OpenMenuFor(Player.m_localPlayer, h2); }
                    GUILayout.EndHorizontal();
                    Note("Here with you.", _good);
                }
                else if (prof.Dead)
                {
                    double left = Mathf.Max(0f, (float)(prof.DiedAt + RespawnSeconds.Value - ZNet.instance.GetTimeSeconds()));
                    GUILayout.Label(prof.Name, _h2);
                    Note($"Has fallen. Wakes {(prof.HasBed ? "in their bed" : "beside you")} in {left:0} s. Their things are in their tombstone (the skull on your map).", _warn);
                    if (GUILayout.Button($"Wake {prof.Name} now", _buttonOn, GUILayout.Height(30))) { Profile p2 = prof; _pending = () => { Humanoid c = Home.Respawn(Player.m_localPlayer, p2); if (c != null) OpenMenuFor(Player.m_localPlayer, c); }; }
                }
                else
                {
                    float far = me != null ? Vector3.Distance(me.transform.position, prof.LastSeen) : 0f;
                    GUILayout.Label(prof.Name, _h2);
                    Note($"Out in the world, last seen {far:0} m from here. Go to them to see them again.", _text);
                    if (GUILayout.Button($"Let {prof.Name} go for good", _button, GUILayout.Height(28))) { long id = prof.Id; _pending = () => Profile.Forget(Player.m_localPlayer, id); }
                }
                EndCard();
            }

            BeginCard();
            if (mine.Count >= MaxCompanions.Value)
            {
                Note($"You have {mine.Count} companions, the most allowed (settings: MaxCompanions).", _dim);
                EndCard();
                return;
            }
            GUILayout.Label(mine.Count == 0 ? "Summon a companion" : "Summon another companion", _h2);
            Note("A viking who comes on adventures with you and, when you send them home, lives a life of their own at your base. They take a free bed and empty chests near them by themselves. You choose how they look next.", _dim);
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", _bold, GUILayout.Width(60));
            _nameField = GUILayout.TextField(_nameField ?? "", 24, _field, GUILayout.Height(30));
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            if (GUILayout.Button("Summon", _buttonOn, GUILayout.Height(36)))
                _pending = () =>
                {
                    Player p = Player.m_localPlayer;
                    Humanoid c = p != null ? Companion.Summon(p, _nameField.Trim()) : null;
                    if (c == null) return;
                    OpenMenuFor(p, c);
                    _tab = Tab.Looks;
                    string took = Home.SetUp(c);
                    Talk.Tell(c, took != null ? $"Hello! {took}" : "Hello! Give me a bed and a chest and I'll make my home here.");
                };
            EndCard();
        }

        // ==== the companion ================================================================================

        private void DrawCompanion()
        {
            Humanoid c = _shown;
            Player pl = Player.m_localPlayer;

            // Header: face, name, what it is doing, close.
            GUILayout.BeginHorizontal();
            Face(GUILayoutUtility.GetRect(64f, 64f, GUILayout.Width(64), GUILayout.Height(64)), c, 6f);
            GUILayout.Space(10);
            GUILayout.BeginVertical();
            if (_renaming)
            {
                GUILayout.BeginHorizontal();
                _nameField = GUILayout.TextField(_nameField ?? "", 24, _field, GUILayout.Width(220), GUILayout.Height(30));
                if (GUILayout.Button("Save", _buttonOn, GUILayout.Width(70), GUILayout.Height(30)) && _nameField.Trim().Length > 0)
                { string n = _nameField.Trim(); _pending = () => { Change(z => z.Set(Keys.Name, n)); Player.m_localPlayer.m_customData["dhc_name"] = n; _renaming = false; }; }
                if (GUILayout.Button("Cancel", _button, GUILayout.Width(80), GUILayout.Height(30))) { _renaming = false; _nameField = Companion.NameOf(c); }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Companion.NameOf(c), _title);
                if (Mine && GUILayout.Button("Rename", _button, GUILayout.Width(80), GUILayout.Height(26))) { _renaming = true; _nameField = Companion.NameOf(c); }
                GUILayout.EndHorizontal();
            }
            string status = Companion.StatusOf(c);
            GUILayout.Label(string.IsNullOrEmpty(status) ? "Idle" : Capital(status), _good);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", _button, GUILayout.Width(90), GUILayout.Height(30))) CloseMenu();
            GUILayout.EndHorizontal();

            // Vitals.
            GUILayout.Space(6);
            Bar($"Health  {c.GetHealth():0} / {c.GetMaxHealth():0}", c.GetHealth() / Mathf.Max(1f, c.GetMaxHealth()), new Color(0.72f, 0.18f, 0.14f));
            Bar($"Stamina  {Stamina.Get(c):0} / {Stamina.Max(c):0}", Stamina.Get(c) / Mathf.Max(1f, Stamina.Max(c)), new Color(0.8f, 0.62f, 0.16f));
            if (Eitr.Max(c) > 0f) Bar($"Eitr  {Eitr.Get(c):0} / {Eitr.Max(c):0}", Eitr.Get(c) / Eitr.Max(c), new Color(0.5f, 0.36f, 0.85f));
            if (!string.IsNullOrEmpty(_note)) Note(_note, _warn);

            // Your other companions near you.
            var yours = Companion.All().Where(h => Companion.IsMine(h, pl) && Vector3.Distance(h.transform.position, pl.transform.position) < 100f).ToList();
            if (yours.Count > 1)
            {
                GUILayout.BeginHorizontal();
                foreach (Humanoid h in yours)
                    if (GUILayout.Button(Companion.NameOf(h), h == c ? _buttonOn : _button, GUILayout.Height(28))) { Humanoid pick = h; _pending = () => OpenMenuFor(pl, pick); }
                GUILayout.EndHorizontal();
            }

            // Tabs.
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            foreach (Tab t in (Tab[])Enum.GetValues(typeof(Tab)))
                if (GUILayout.Button(t.ToString(), _tab == t ? _tab1 : _tab0, GUILayout.Height(32))) { _tab = t; _hover = ""; _scroll = Vector2.zero; }
            GUILayout.EndHorizontal();
            Rounded(GUILayoutUtility.GetRect(10f, 2f, GUILayout.ExpandWidth(true)), Edge, 1f);
            GUILayout.Space(6);

            _scroll = GUILayout.BeginScrollView(_scroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginVertical(GUILayout.Width(Inner));
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
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        // ==== Status =======================================================================================

        private void DrawStatus(Humanoid c)
        {
            BrainState st = Brain.Get(c);
            Decision d = st.Current;
            bool here = c.GetComponent<ZNetView>().IsOwner(); // its brain runs on this game, so the details are here

            BeginCard("Now");
            if (!here) Note("Its brain runs on the game of the player nearest to it; the details show there.", _dim);
            else if (st.InCombat)
            {
                Note("Fighting: " + string.Join(", ", st.Enemies.Where(e => e != null).Select(st.Label)), _bold);
                Note(Capital(d.Describe(st.Label)), _good);
                if (!string.IsNullOrEmpty(d.Note)) Note(Capital(d.Note), _dim);
            }
            else Note($"Not fighting. It acts when an enemy comes within {EngageRange.Value:0} m of it or you.", _dim);
            GUILayout.Space(4);
            OrderButtons(c);
            EndCard();

            BeginCard("Food and rest");
            if (!here) Note("Shows on the game that runs it.", _dim);
            else
            {
                List<Food.Meal> meals = Food.Meals(c);
                GUILayout.BeginHorizontal();
                for (int i = 0; i < 3; i++)
                {
                    Rect r = GUILayoutUtility.GetRect(46f, 46f, GUILayout.Width(46), GUILayout.Height(46));
                    Slot(r, false);
                    if (i >= meals.Count) continue;
                    DrawIcon(new Rect(r.x + 5f, r.y + 5f, r.width - 10f, r.height - 10f), meals[i].Item.GetIcon());
                    Rounded(new Rect(r.x + 3f, r.yMax - 5f, (r.width - 6f) * meals[i].Fraction, 3f), meals[i].Fraction < 0.2f ? ColBad : ColGood, 1f);
                }
                GUILayout.Space(10);
                GUILayout.BeginVertical();
                if (meals.Count == 0) Note($"Hungry: only {BaseHealth.Value:0} health and {BaseStamina.Value:0} stamina, and it does not heal. Give it food (Gear tab); it eats by itself.", _warn);
                else Note(string.Join(", ", meals.Select(m => $"{Loc(m.Item.m_shared.m_name)} ({Mathf.CeilToInt(m.Time / 60f)} min)")) + ". It eats again when a food is half gone.", _text);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
                if (Rest.IsRested(c)) Note($"Rested for {Mathf.CeilToInt(Rest.Left(c) / 60f)} more min: heals and gets its breath back faster.", _good);
                else Note("Not rested. A while under a roof by a fire makes it rested.", _dim);
            }
            EndCard();

            BeginCard("Effects");
            var effects = here ? c.GetSEMan().GetStatusEffects().Where(se => se != null && se.m_icon != null).ToList() : new List<StatusEffect>();
            if (!here) Note("Shows on the game that runs it.", _dim);
            else if (effects.Count == 0) Note("None. Your boss power reaches it too when it is within 10 m of you.", _dim);
            else
            {
                GUILayout.BeginHorizontal();
                foreach (StatusEffect se in effects.Take(9))
                {
                    GUILayout.BeginVertical(GUILayout.Width(56));
                    DrawIcon(GUILayoutUtility.GetRect(34f, 34f, GUILayout.Width(34), GUILayout.Height(34)), se.m_icon);
                    float left = se.GetRemaningTime();
                    GUILayout.Label(left > 0f ? $"{Mathf.FloorToInt(left / 60f)}:{Mathf.FloorToInt(left % 60f):00}" : " ", _small);
                    GUILayout.EndVertical();
                }
                GUILayout.EndHorizontal();
                Note(string.Join(", ", effects.Select(se => Loc(se.m_name))), _dim);
            }
            EndCard();

            BeginCard("Recently");
            if (st.History.Count == 0) Note("Nothing yet.", _dim);
            foreach (string line in st.History.Take(8)) Note(Capital(line), _text);
            EndCard();
        }

        /// <summary>The two orders that matter (come with me, go home), big; stay, guard and come here small beneath them.</summary>
        private void OrderButtons(Humanoid c)
        {
            Order order = Companion.OrderOf(c);
            float half = (Inner - 28f) / 2f - 6f;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Come with me", order == Order.Follow ? _buttonOn : _button, GUILayout.Width(half), GUILayout.Height(40))) _pending = () => GiveOrder(h => Home.Follow(h));
            if (GUILayout.Button("Go home", order == Order.Gather ? _buttonOn : _button, GUILayout.Width(half), GUILayout.Height(40))) _pending = () => GiveOrder(h => Home.GoHome(h));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("More", _dim, GUILayout.Width(52), GUILayout.Height(28));
            float third = (Inner - 28f - 58f) / 3f - 6f;
            if (GUILayout.Button("Stay here", order == Order.Stay ? _buttonOn : _button, GUILayout.Width(third), GUILayout.Height(28))) _pending = () => Change(z => z.Set(Keys.Order, (int)AICompanion.Order.Stay));
            if (GUILayout.Button("Guard this spot", order == Order.Guard ? _buttonOn : _button, GUILayout.Width(third), GUILayout.Height(28)))
                _pending = () => Change(z => { z.Set(Keys.Order, (int)AICompanion.Order.Guard); z.Set(Keys.Post, c.transform.position); });
            if (GUILayout.Button("Come here", _button, GUILayout.Width(third), GUILayout.Height(28))) _pending = ComeHere;
            GUILayout.EndHorizontal();
            Note($"Or hold {MenuKey.Value} anywhere: all your companions near you come with you, or go home.", _dim);
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
            GUILayout.BeginHorizontal();
            Stat("Health", $"{c.GetHealth():0}/{c.GetMaxHealth():0}");
            Stat("Stamina", $"{Stamina.Get(c):0}/{Stamina.Max(c):0}");
            Stat("Eitr", Eitr.Max(c) > 0f ? $"{Eitr.Get(c):0}/{Eitr.Max(c):0}" : "-");
            Stat("Carrying", $"{Carry.Weight(c):0}/{Carry.Max(c):0}");
            GUILayout.EndHorizontal();
            float regen = meals.Sum(m => m.Item.m_shared.m_foodRegen) * (Rest.IsRested(c) ? 1.5f : 1f);
            Note($"Before food: {BaseHealth.Value:0} health and {BaseStamina.Value:0} stamina, as a player's. Food adds the rest.", _dim);
            Note(regen > 0f ? $"Heals {regen:0.#} health every 10 s from its food{(Rest.IsRested(c) ? " (rested: 50% more)" : "")}." : "Heals nothing: it has not eaten (a player only heals from food).", regen > 0f ? _text : _warn);
            EndCard();

            BeginCard("Food");
            if (!here) Note("Shows on the game that runs it.", _dim);
            else if (meals.Count == 0) Note("Nothing eaten.", _warn);
            foreach (Food.Meal m in meals)
                Note($"{Loc(m.Item.m_shared.m_name)}:  +{m.Item.m_shared.m_food:0} health, +{m.Item.m_shared.m_foodStamina:0} stamina{(m.Item.m_shared.m_foodEitr > 0f ? $", +{m.Item.m_shared.m_foodEitr:0} eitr" : "")}, heals {m.Item.m_shared.m_foodRegen:0.#}  ·  {Mathf.CeilToInt(m.Time / 60f)} min left", _text);
            EndCard();

            BeginCard("Fighting");
            ItemDrop.ItemData melee = Companion.BestMelee(c), bow = Companion.BestRanged(c), shield = Companion.Shield(c);
            GUILayout.BeginHorizontal();
            Stat("Armour", $"{Companion.Armor(c):0}");
            Stat("Block", shield != null ? $"{shield.GetBlockPower(Skill.Get(c, Skills.SkillType.Blocking) / 100f):0}" : melee != null ? $"{melee.GetBlockPower(Skill.Get(c, Skills.SkillType.Blocking) / 100f):0}" : "-");
            Stat("Kills", Companion.Zdo(c).GetInt(Keys.Kills, 0).ToString());
            Stat("Fights", here ? st.Fights.ToString() : "-");
            GUILayout.EndHorizontal();
            if (melee != null) Note($"{Loc(melee.m_shared.m_name)}: {Damage(c, melee)} damage a hit (with its {Loc("$skill_" + melee.m_shared.m_skillType.ToString().ToLower())} skill)", _text);
            else Note("No weapon: it fights with its fists.", _warn);
            if (bow != null) Note($"{Loc(bow.m_shared.m_name)}: {Damage(c, bow)} damage a shot", _text);
            Note(shield != null ? $"Blocks with its {Loc(shield.m_shared.m_name)} just before a swing lands (a parry, once it has seen that creature swing), then hits back; steps aside from sweeps, shots and hits too hard to block." : melee != null ? "Blocks with its weapon when something swings at it (a shield blocks far better); steps aside from what it cannot block." : "Nothing to block with: it steps aside instead.", _dim);
            if (here && st.Parries + st.Blocks > 0) Note($"This session: {st.Parries} parries, {st.Blocks} blocks.", _text);
            var tally = Journal.Tally(c);
            var feared = tally.Where(kv => kv.Key.StartsWith("killedby:")).Select(kv => kv.Key.Substring(9).ToLowerInvariant()).ToList();
            if (feared.Count > 0) Note("Careful with: " + string.Join(", ", feared) + " (they have killed it before: it uses its bow on them and keeps its distance).", _dim);
            EndCard();

            BeginCard("Its last fights");
            if (!here || st.FightLog.Count == 0) Note(here ? "None yet this session." : "Shows on the game that runs it.", _dim);
            else foreach (string f in st.FightLog) Note(f, _text);
            EndCard();

            BeginCard("Resistances");
            HitData.DamageModifiers mods = Gear.Modifiers(c);
            var lines = new List<string>();
            foreach (HitData.DamageType t in new[] { HitData.DamageType.Blunt, HitData.DamageType.Slash, HitData.DamageType.Pierce, HitData.DamageType.Fire, HitData.DamageType.Frost, HitData.DamageType.Lightning, HitData.DamageType.Poison, HitData.DamageType.Spirit })
            {
                HitData.DamageModifier mod = mods.GetModifier(t);
                if (mod != HitData.DamageModifier.Normal) lines.Add($"{t}: {mod.ToString().Replace("Very", "very ").ToLowerInvariant()}");
            }
            Note(lines.Count > 0 ? string.Join("   ", lines) : "None: its gear gives no resistances (and no weaknesses).", lines.Count > 0 ? _text : _dim);
            EndCard();

            BeginCard("Skills");
            Note("As a player's: each rises as it uses it and makes it better at it. It loses 5% of every skill when it falls.", _dim);
            var skills = Skill.Types().Select(t => new KeyValuePair<Skills.SkillType, float>(t, Skill.Get(c, t)))
                .OrderByDescending(kv => kv.Value).ThenBy(kv => Loc("$skill_" + kv.Key.ToString().ToLower())).ToList();
            float col = (Inner - 40f) / 2f;
            for (int i = 0; i < skills.Count; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 2, skills.Count); j++)
                {
                    GUILayout.BeginVertical(GUILayout.Width(col));
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(Loc("$skill_" + skills[j].Key.ToString().ToLower()), skills[j].Value >= 1f ? _text : _dim, GUILayout.Width(col - 50f));
                    GUILayout.Label(skills[j].Value.ToString("0"), _num, GUILayout.Width(44));
                    GUILayout.EndHorizontal();
                    Rect r = GUILayoutUtility.GetRect(col - 10f, 5f, GUILayout.Width(col - 10f), GUILayout.Height(5));
                    Rounded(r, new Color(0.06f, 0.045f, 0.035f, 1f), 2f);
                    if (skills[j].Value > 0f) Rounded(new Rect(r.x, r.y, r.width * Mathf.Clamp01(skills[j].Value / 100f), r.height), Gold, 2f);
                    float next = here ? Skill.Progress(c, skills[j].Key) : 0f;
                    if (next > 0f) Rounded(new Rect(r.x, r.yMax + 1f, r.width * next, 2f), ColGood, 1f);
                    GUILayout.Space(5);
                    GUILayout.EndVertical();
                }
                GUILayout.EndHorizontal();
            }
            Note("Gold: its level out of 100. Green: on its way to the next level.", _dim);
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
            }, _text);
            EndCard();

            BeginCard("How it fights");
            Style style = Companion.Chosen(c);
            if (GUILayout.Button("Let it decide (recommended)", style == Style.Auto ? _buttonOn : _button, GUILayout.Width(Inner - 34f), GUILayout.Height(34)))
                _pending = () => Change(z => z.Set(Keys.Style, (int)Style.Auto));
            GUILayout.BeginHorizontal();
            foreach (Style s in new[] { Style.Aggressive, Style.Balanced, Style.Defensive, Style.Passive })
                if (Choice(s.ToString(), style == s, 4)) { Style pick = s; _pending = () => Change(z => z.Set(Keys.Style, (int)pick)); }
            GUILayout.EndHorizontal();
            Note(style switch
            {
                Style.Aggressive => "Presses the attack and falls back only when badly hurt.",
                Style.Defensive => "Stays close to you, fights what comes near, and falls back early.",
                Style.Passive => "Never starts a fight; keeps by your side and keeps safe.",
                Style.Balanced => "Fights what threatens you or it, and falls back when hurt.",
                _ => "Reads what you are doing: fights beside you, helps you mine or chop, keeps close and only defends you while you travel, and where a place is too dangerous for its gear it tells you and only defends.",
            }, _text);
            if (style == Style.Auto && !string.IsNullOrEmpty(Brain.Get(c)?.AutoNote)) Note("Right now: " + Brain.Get(c).AutoNote + ".", _good);
            GUILayout.Space(4);
            int retreat = Companion.RetreatOf(c);
            Stepper("Falls back below", $"{retreat}% health", () => Change(z => z.Set(Keys.Retreat, Mathf.Clamp(retreat - 5, 0, 90))), () => Change(z => z.Set(Keys.Retreat, Mathf.Clamp(retreat + 5, 0, 90))));
            Note("Below half of that it runs.", _dim);
            Stepper("Fights enemies within", $"{EngageRange.Value:0} m", () => { EngageRange.Value = Mathf.Clamp(EngageRange.Value - 2f, 4f, 50f); SaveSettings(); },
                    () => { EngageRange.Value = Mathf.Clamp(EngageRange.Value + 2f, 4f, 50f); SaveSettings(); });
            Note("Of it or you (all your companions). Living at home it also fights anything that comes into its home.", _dim);
            bool potions = Companion.Potions(c), protect = Companion.Protect(c);
            if (Check("Drinks healing potions when hurt", potions)) _pending = () => Change(z => z.Set(Keys.Potions, !potions));
            if (Check("Protects you first (goes for what attacks you)", protect)) _pending = () => Change(z => z.Set(Keys.Protect, !protect));
            EndCard();

            BeginCard("Habits");
            bool loot = Loot.On(c), friends = Companion.Zdo(c).GetBool(Keys.Friends, false);
            if (Check("Picks things up off the ground (what, in the Gear tab)", loot)) _pending = () => Change(z => z.Set(Loot.Key, !loot));
            bool autoHome = Following.AutoHome(c);
            if (Check("Comes along when you head out, and lives at home again when you're back (with a bed)", autoHome)) _pending = () => Change(z => z.Set(Following.AutoHomeKey, !autoHome));
            if (Check("Shows what it decides in a fight above its head", ShowDecisions.Value)) _pending = () => { ShowDecisions.Value = !ShowDecisions.Value; SaveSettings(); };
            bool chatty = Talk.Chatty(c);
            if (Check("Tells you in chat what it is up to (what it makes, what it needs)", chatty)) _pending = () => Change(z => z.Set(Talk.ChattyKey, !chatty));
            if (Mine && Check("Friends can give it orders", friends)) _pending = () => Companion.Write(c, z => z.Set(Keys.Friends, !friends));
            EndCard();

            BeginCard("Talking to it");
            string n = Companion.NameOf(c);
            Note($"Start a chat message with its name: \"{n}, follow me\", \"{n} go home\", \"{n}, be careful\", \"{n}, get your things\". It answers above its head.", _text);
            EndCard();

            if (Mine)
            {
                BeginCard("Send it away");
                Note("Sends it away for good. Only with its bag empty, so nothing is lost.", _dim);
                if (GUILayout.Button("Send home", _button, GUILayout.Width(140), GUILayout.Height(30)))
                    _pending = () => { if (Companion.Dismiss(c, out string why)) CloseMenu(); else _note = why; };
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
                Note($"Its bed is {Vector3.Distance(spot, p.transform.position):0} m from you. It wakes there after falling and lives around it.", _good);
            }
            else Note("No bed yet: it wakes beside you after falling. Give it a bed nobody sleeps in.", _warn);
            List<Container> chests = Home.Chests(c);
            if (chests.Count == 0) Note("No chests nearby. It stores what it finds in its chests and takes better gear, food, arrows and potions from them.", _dim);
            foreach (Container ch in chests)
            {
                Inventory inv = ch.GetInventory();
                Note($"{Loc(ch.m_name)}: {inv.NrOfItems()} of {inv.GetWidth() * inv.GetHeight()} slots used", _text);
            }
            GUILayout.Space(4);
            if (Mine && (!bed || chests.Count == 0) && GUILayout.Button(bed ? "Take the empty chests beside its bed" : "Find it a free bed (and empty chests beside it)", _buttonOn, GUILayout.Height(32)))
                _pending = () => { string took = Home.SetUp(c); if (took != null) Talk.Tell(c, took); else _note = bed ? "No empty chest within 8 m of its bed." : "No free bed within 40 m of it (nobody sleeps in it, no companion has it)."; };
            if (Mine)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Choose bed and chests", _button, GUILayout.Height(32))) _pending = () => { Home.StartAssign(c); CloseMenu(); };
                if (bed && GUILayout.Button("Let its bed go", _button, GUILayout.Width(150), GUILayout.Height(32)))
                    _pending = () => { Bed b = Home.BedOf(c); if (b != null) Home.ToggleBed(b, c); else Change(cz => cz.Set(Keys.HasBed, false)); };
                GUILayout.EndHorizontal();
                Note($"Choosing closes this menu: then E on a bed or a chest gives it to {Companion.NameOf(c)}, E again takes it back. {MenuKey.Value} or Esc when done. Going home takes a free bed and empty chests near it by itself.", _dim);
            }
            EndCard();

            BeginCard("Your chests");
            Note("The chests at home that are not a companion's.", _dim);
            bool stow = Work.Stows(c), pantry = Work.UsesPantry(c);
            if (Check("Puts what it gathers into your chests when its own are full (or it has none). It never takes anything out.", stow)) _pending = () => Change(z => z.Set(Work.StowKey, !stow));
            if (Check("Takes a little food from your chests when it has nothing to eat (and tells you what it took)", pantry)) _pending = () => Change(z => z.Set(Work.PantryKey, !pantry));
            if (!pantry) Note("Off: when it runs out of food it forages, hunts and asks you in chat.", _dim);
            EndCard();

            bool living = Companion.OrderOf(c) == AICompanion.Order.Gather;
            BeginCard("Working toward");
            Goal goal = st.Goal;
            if (!c.GetComponent<ZNetView>().IsOwner()) Note("Shows on the game that runs it.", _dim);
            else if (goal == null) Note(living ? "Nothing to work toward right now: nothing better to make or upgrade at the stations near its home. Build a workbench (and a forge) near its bed, or give it better tools." : "It picks something to work toward when it goes home.", _dim);
            else
            {
                Note(Capital(goal.What) + (goal.Station != null ? $", at the {Loc(goal.Station.m_name).ToLowerInvariant()}" : ""), _bold);
                if (goal.Raw.Count > 0) Note("Gathering: " + goal.RawText(), _text);
                if (goal.Steps.Count > 0) Note("Then makes: " + string.Join(", ", goal.Steps.Select(s => Loc(s.Key.m_item.m_itemData.m_shared.m_name).ToLowerInvariant())), _text);
                if (goal.Smelt.Count > 0) Note($"{Capital(string.Join(" and ", goal.Smelt))} need{(goal.Smelt.Count == 1 ? "s" : "")} smelting: it puts the ore in its chest for you (or FeedFromChests) to smelt.", _dim);
                if (goal.Ask.Count > 0) Note("Needs from you: " + string.Join(", ", goal.Ask) + ". Put it in its chest.", _warn);
            }
            EndCard();

            BeginCard("Living at home");
            if (!living)
            {
                Note("It is not living at home now.", _dim);
                if (GUILayout.Button("Go home", _buttonOn, GUILayout.Width(170), GUILayout.Height(30))) _pending = () => GiveOrder(h => Home.GoHome(h));
            }
            Vector3 center = Work.Center(c);
            Note(bed ? "It lives around its bed." : $"With no bed it lives around where it was told to ({center.x:0}, {center.z:0}).", _dim);
            if (!bed && Commandable && GUILayout.Button("Live around where I stand", _button, GUILayout.Width(230), GUILayout.Height(28)))
                _pending = () => { Vector3 at = Player.m_localPlayer.transform.position; Change(cz => cz.Set(Keys.Post, at)); };
            int radius = Work.RadiusOf(c);
            Stepper("Goes as far as", $"{radius} m", () => Change(cz => cz.Set(Keys.Radius, Mathf.Clamp(radius - 5, 10, 80))), () => Change(cz => cz.Set(Keys.Radius, Mathf.Clamp(radius + 5, 10, 80))));
            EndCard();

            Job jobs = Work.JobsOf(c);
            BeginCard("Jobs (optional)");
            Job auto = Work.AutoJobs(c);
            Note(jobs == Job.None
                ? "Nothing ticked: it decides for itself, gathering what its goal needs and what its tools allow. Right now: " + (auto == Job.None && goal == null ? "nothing (no tools, and enough food)." : JobNames(auto | Goals.JobsFor(goal, out _)) + ".")
                : "It does only what is ticked (still preferring what its goal needs). Untick everything to let it decide for itself.", _text);
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
                if (Check(label, on)) { Job pick = job; _pending = () => Change(cz => cz.Set(Keys.Jobs, cz.GetInt(Keys.Jobs, 0) ^ (int)pick)); }
                if (job == Job.Wood || job == Job.Stone || job == Job.Ore) Note("      Tool: " + tool, tool.StartsWith("no ") ? _warn : _dim);
            }
            Note("A better axe or pickaxe works harder trees and ores (the game's tool tiers). At its workbench it also upgrades and makes gear when it has the materials.", _dim);
            EndCard();

            BeginCard("Gathered this session");
            if (st.Gathered.Count == 0) Note("Nothing yet.", _dim);
            else Note(string.Join(", ", st.Gathered.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {kv.Key}")), _text);
            EndCard();

            BeginCard("While you are away");
            Note(WhileAway.Value switch
            {
                AwayMode.Off => "When you come back it catches up on the work it would have done: trees felled, rocks mined, berries picked, into its chests.",
                AwayMode.Real => "When you come back it catches up on its work, and on fights with the creatures around its home, which can go badly: it can fall (its things in a tombstone nearby, which it goes back for).",
                _ => "When you come back it catches up on the work it would have done (trees felled, rocks mined, berries picked, into its chests), and fights off a few creatures, keeping what they drop. It never falls while you are away.",
            }, _text);
            Note($"Setting: WhileAway ({WhileAway.Value}).", _dim);
            EndCard();

            BeginCard("If it falls");
            Note($"As a player: its things go into its tombstone (a skull on everyone's map), it wakes {RespawnSeconds.Value:0} s later in its bed (or beside you), and goes back for its things. E on its tombstone gives them back to it.", _text);
            EndCard();
        }

        private static string JobNames(Job j) => string.Join(", ", ((Job[])Enum.GetValues(typeof(Job))).Where(x => x != Job.None && (j & x) != 0).Select(x => x.ToString().ToLower()));

        // ==== Gear =========================================================================================

        private void DrawGear(Humanoid c)
        {
            Player p = Player.m_localPlayer;
            if (Event.current.type == EventType.Repaint) _hover = "";

            // Its gear: slots of their own (Gear), kept when it falls; changed from here, wherever it is.
            BeginCard("Its gear  (kept when it falls)");
            foreach (int row in new[] { 4, 5 })
            {
                GUILayout.BeginHorizontal();
                foreach (Gear.Slot slot in Gear.All.Where(s => s.Y == row))
                {
                    GUILayout.BeginVertical(GUILayout.Width(60));
                    ItemDrop.ItemData inSlot = Gear.In(c, slot);
                    Rect cell = GUILayoutUtility.GetRect(52f, 52f, GUILayout.Width(52), GUILayout.Height(52));
                    ItemCell(cell, inSlot, inSlot != null && c.IsItemEquiped(inSlot),
                        inSlot != null && Mine ? () => _pending = () => { if (!Companion.Take(_shown, Player.m_localPlayer, inSlot, out string why) && why != null) _note = why; } : (Action)null);
                    if (inSlot == null) GUI.Label(new Rect(cell.x, cell.y + 17f, cell.width, 18f), "empty", _small);
                    GUILayout.Label(slot.Label, _small, GUILayout.Width(58));
                    GUILayout.EndVertical();
                }
                GUILayout.EndHorizontal();
            }
            Note(string.IsNullOrEmpty(_hover) ? (Mine ? "Click a piece of gear to take it back. It moves the best it has into these slots by itself." : "Point at a piece to see what it is.") : _hover, string.IsNullOrEmpty(_hover) ? _dim : _bold);
            EndCard();

            Inventory its = c.GetInventory();
            BeginCard($"Its bag  ({Gear.BagCount(its)} of {its.GetWidth() * Gear.BagRows} slots, weight {Carry.Weight(c):0} of {Carry.Max(c):0})");
            if (Carry.Over(c)) Note("Too heavy: it cannot run until it puts something down.", _warn);
            Grid(its, its.GetWidth(), Mathf.Min(Gear.BagRows, its.GetHeight()), null); // (look only: its bag is changed beside it)
            Note("What it carries goes into its tombstone when it falls. To give or take from its bag, open your inventory (Tab) next to it: its bag shows beside yours.", _dim);
            EndCard();

            if (Mine && p != null)
            {
                var giveable = p.GetInventory().GetAllItems().Where(i => !p.IsItemEquiped(i) && Gear.IsGear(i)).ToList();
                BeginCard("Give it gear from your inventory");
                if (giveable.Count == 0) Note("Weapons, shields, bows, arrows, axes, pickaxes, hammers and armour you are not wearing show here.", _dim);
                else
                {
                    int per = Mathf.Max(1, Mathf.FloorToInt((Inner - 30f) / 58f));
                    for (int row = 0; row * per < giveable.Count; row++)
                    {
                        GUILayout.BeginHorizontal();
                        foreach (ItemDrop.ItemData item in giveable.Skip(row * per).Take(per))
                        {
                            ItemDrop.ItemData it = item;
                            ItemCell(GUILayoutUtility.GetRect(52f, 52f, GUILayout.Width(52), GUILayout.Height(52)), it, false,
                                () => _pending = () => { if (!Companion.Give(_shown, Player.m_localPlayer, it, out string why) && why != null) _note = why; });
                        }
                        GUILayout.EndHorizontal();
                    }
                    Note("Click to give: it goes into its gear slot (what was there goes into its bag).", _dim);
                }
                EndCard();
            }

            BeginCard("What it picks up");
            Note("As it walks by, and fresh drops near it (what you mine and chop, what creatures drop). Never what a player dropped.", _dim);
            Loot.Kinds list = Loot.List(c);
            var kinds = new[] { Loot.Kinds.Ore, Loot.Kinds.Wood, Loot.Kinds.Stone, Loot.Kinds.Food, Loot.Kinds.Creature, Loot.Kinds.Gear, Loot.Kinds.Other };
            for (int i = 0; i < kinds.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 2, kinds.Length); j++)
                {
                    Loot.Kinds k = kinds[j];
                    string label = k == Loot.Kinds.Ore ? "Ore and metal" : k == Loot.Kinds.Creature ? "Creature drops" : k == Loot.Kinds.Other ? "Everything else" : k == Loot.Kinds.Food ? "Food and raw meat" : k.ToString();
                    if (Check(label, (list & k) != 0, Inner / 2f - 8f)) { Loot.Kinds pick = k; _pending = () => Change(z => z.Set(Loot.ListKey, z.GetInt(Loot.ListKey, (int)Loot.Default) ^ (int)pick)); }
                }
                GUILayout.EndHorizontal();
            }
            if (!Loot.On(c)) Note("Picking up is switched off (Orders tab).", _warn);
            EndCard();
        }

        private void Grid(Inventory inv, int width, int height, Action<ItemDrop.ItemData> click)
        {
            for (int y = 0; y < height; y++)
            {
                GUILayout.BeginHorizontal();
                for (int x = 0; x < width; x++)
                {
                    ItemDrop.ItemData item = inv.GetItemAt(x, y);
                    ItemCell(GUILayoutUtility.GetRect(52f, 52f, GUILayout.Width(52), GUILayout.Height(52)), item, _shown != null && item != null && _shown.IsItemEquiped(item), item != null && click != null ? () => click(item) : (Action)null);
                }
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>One square: its icon, stack count and wear; a gold edge when worn; a click runs the action; pointing at it describes it.</summary>
        private void ItemCell(Rect r, ItemDrop.ItemData item, bool worn, Action click)
        {
            Slot(r, worn && item != null);
            if (item == null) return;
            DrawIcon(new Rect(r.x + 5f, r.y + 5f, r.width - 10f, r.height - 10f), item.GetIcon());
            if (item.m_stack > 1) GUI.Label(new Rect(r.x, r.yMax - 19f, r.width - 4f, 17f), item.m_stack.ToString(), _small);
            if (item.m_shared.m_useDurability && item.GetMaxDurability() > 0f)
            {
                float f = Mathf.Clamp01(item.m_durability / item.GetMaxDurability());
                Rounded(new Rect(r.x + 4f, r.yMax - 5f, (r.width - 8f) * f, 3f), Color.Lerp(ColBad, ColGood, f), 1f);
            }
            if (r.Contains(Event.current.mousePosition))
            {
                _hover = Describe(item);
                Outline(r, Text, 4f, 1f);
            }
            if (click != null && GUI.Button(r, GUIContent.none, GUIStyle.none)) click();
        }

        private static string Describe(ItemDrop.ItemData item)
        {
            string name = Loc(item.m_shared.m_name) + (item.m_quality > 1 ? $" (level {item.m_quality})" : "");
            var parts = new List<string> { name };
            if (item.IsWeapon()) parts.Add($"{item.GetDamage().GetTotalDamage():0} damage");
            if (item.GetArmor() > 0f) parts.Add($"{item.GetArmor():0} armour");
            if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield) parts.Add($"{item.m_shared.m_blockPower:0} block");
            if (item.m_shared.m_useDurability && item.GetMaxDurability() > 0f) parts.Add($"{item.m_durability / item.GetMaxDurability() * 100f:0}% condition");
            if (item.m_stack > 1) parts.Add($"{item.m_stack} of them");
            return string.Join("  ·  ", parts);
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

        private void Face(Rect r, Humanoid c, float radius)
        {
            Rounded(r, new Color(0.16f, 0.19f, 0.17f), radius);
            Texture face = Portraits.Get(Companion.IdOf(c));
            if (face != null && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(r, face, ScaleMode.ScaleAndCrop, true, 0f, Color.white, Vector4.zero, new Vector4(radius, radius, radius, radius));
            Outline(r, Edge, radius, 2f);
        }

        private void Rebind(Humanoid c) { _rebind = Companion.IdOf(c); _rebindUntil = Time.time + 5f; }

        private void LooksChange(Action<Humanoid> change)
        {
            if (!Mine) { _note = "Only its owner can change how it looks."; return; }
            Humanoid c = _shown;
            if (!Companion.Write(c, _ => change(c))) _note = "Someone has its things open. Try again in a moment.";
        }

        private void DrawLooks(Humanoid c)
        {
            BeginCard();
            GUILayout.BeginHorizontal();
            Face(GUILayoutUtility.GetRect(150f, 150f, GUILayout.Width(150), GUILayout.Height(150)), c, 8f);
            GUILayout.Space(12);
            GUILayout.BeginVertical();
            GUILayout.Label("How it looks", _h2);
            Note("As when you make a character. Everyone sees the same, and it wakes looking the same after it falls. It changes as you click: turn round to watch.", _text);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Randomise", _button, GUILayout.Height(30))) _pending = () => { Rebind(c); LooksChange(Looks.Randomise); };
            if (GUILayout.Button("Look like me", _button, GUILayout.Height(30))) _pending = () => { Rebind(c); LooksChange(h => Looks.CopyFrom(h, Player.m_localPlayer)); };
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            EndCard();

            int model = Looks.Model(c);
            BeginCard("Body");
            GUILayout.BeginHorizontal();
            if (Choice("Body 1", model == 0, 2)) _pending = () => { Rebind(c); LooksChange(h => Looks.SetModel(h, 0)); };
            if (Choice("Body 2", model == 1, 2)) _pending = () => { Rebind(c); LooksChange(h => Looks.SetModel(h, 1)); };
            GUILayout.EndHorizontal();
            EndCard();

            List<ItemDrop> hairs = Looks.Hairs(), beards = Looks.Beards();
            BeginCard("Hair");
            Picker("Style", Looks.StyleName(Looks.Hair(c), hairs), () => LooksChange(h => Looks.SetHair(h, Looks.Step(Looks.Hair(h), hairs, -1))), () => LooksChange(h => Looks.SetHair(h, Looks.Step(Looks.Hair(h), hairs, 1))));
            float tone = Looks.HairTone(c), shade = Looks.HairShade(c);
            float newTone = Slider("Colour", tone), newShade = Slider("Shade", shade);
            if (Mathf.Abs(newTone - tone) > 0.005f || Mathf.Abs(newShade - shade) > 0.005f) { float t = newTone, l = newShade; _pending = () => LooksChange(h => Looks.SetHairColor(h, t, l)); }
            EndCard();

            if (model == 0)
            {
                BeginCard("Beard");
                Picker("Style", Looks.StyleName(Looks.Beard(c), beards), () => LooksChange(h => Looks.SetBeard(h, Looks.Step(Looks.Beard(h), beards, -1))), () => LooksChange(h => Looks.SetBeard(h, Looks.Step(Looks.Beard(h), beards, 1))));
                EndCard();
            }

            BeginCard("Skin");
            float skin = Looks.SkinTone(c), newSkin = Slider("Tone", skin);
            if (Mathf.Abs(newSkin - skin) > 0.005f) { float t = newSkin; _pending = () => LooksChange(h => Looks.SetSkin(h, t)); }
            EndCard();
        }

        private void Picker(string label, string value, Action prev, Action next)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _bold, GUILayout.Width(90));
            if (GUILayout.Button("<", _button, GUILayout.Width(40), GUILayout.Height(28))) _pending = prev;
            GUILayout.Label(value, _num, GUILayout.Width(110));
            if (GUILayout.Button(">", _button, GUILayout.Width(40), GUILayout.Height(28))) _pending = next;
            GUILayout.EndHorizontal();
        }

        private float Slider(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _bold, GUILayout.Width(90));
            GUILayout.BeginVertical();
            GUILayout.Space(8);
            float v = GUILayout.HorizontalSlider(value, 0f, 1f, GUILayout.Width(Inner - 130f));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            return v;
        }

        // ==== Journal ======================================================================================

        private void DrawJournal(Humanoid c)
        {
            var tally = Journal.Tally(c);
            int T(string k) => tally.TryGetValue(k, out int n) ? n : 0;
            BeginCard("Its saga");
            GUILayout.BeginHorizontal();
            Stat("Days with you", Journal.DaysWithYou(c).ToString());
            Stat("Kills", T("kills").ToString());
            Stat("Parries", T("parries").ToString());
            Stat("Falls", T("deaths").ToString());
            GUILayout.EndHorizontal();
            var kills = tally.Where(kv => kv.Key.StartsWith("kill:")).OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Value} {kv.Key.Substring(5).ToLowerInvariant()}").ToList();
            if (kills.Count > 0) Note("Most slain: " + string.Join(", ", kills), _text);
            var bosses = tally.Where(kv => kv.Key.StartsWith("boss:")).Select(kv => kv.Key.Substring(5)).ToList();
            Note(bosses.Count > 0 ? "Bosses: " + string.Join(", ", bosses) : "No boss yet.", bosses.Count > 0 ? _good : _dim);
            Note($"Gear made: {T("made")}   Upgrades: {T("upgraded")}   Trips: {T("trips")}", _text);
            var lands = tally.Where(kv => kv.Key.StartsWith("place:") && kv.Key != "place:dungeon").Select(kv => kv.Key.Substring(6)).ToList();
            if (lands.Count > 0) Note("Has seen: " + string.Join(", ", lands) + (T("place:dungeon") > 0 ? $", and {T("place:dungeon")} dungeon{(T("place:dungeon") == 1 ? "" : "s")}" : ""), _dim);
            EndCard();

            BeginCard("Moments");
            var entries = Journal.Entries(c);
            if (entries.Count == 0) Note("Nothing written yet: its first kills, parries, trips and the gear it makes will show here.", _dim);
            foreach (string e in Enumerable.Reverse(entries).Take(40))
            {
                int bar = e.IndexOf('|');
                string day = bar > 0 ? e.Substring(0, bar) : "?", text = bar > 0 ? e.Substring(bar + 1) : e;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Day {day}", _dim, GUILayout.Width(70));
                GUILayout.Label(text, _text, GUILayout.MaxWidth(Inner - 110f));
                GUILayout.EndHorizontal();
            }
            EndCard();
        }

        // ==== pieces =======================================================================================

        private void BeginCard(string title = null)
        {
            GUILayout.BeginVertical(_card, GUILayout.Width(Inner));
            if (title != null) { GUILayout.Label(title, _h2); GUILayout.Space(2); }
        }

        private void EndCard()
        {
            GUILayout.EndVertical();
            GUILayout.Space(8);
        }

        /// <summary>Text that wraps to the width it has (never cut off).</summary>
        private void Note(string text, GUIStyle style) => GUILayout.Label(text, style, GUILayout.MaxWidth(Inner - 28f));

        private void Stat(string label, string value)
        {
            GUILayout.BeginVertical(GUILayout.Width((Inner - 40f) / 4f));
            GUILayout.Label(value, _statNum);
            GUILayout.Label(label, _dim);
            GUILayout.EndVertical();
        }

        private void Stepper(string label, string value, Action down, Action up)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _bold, GUILayout.Width(200));
            if (GUILayout.Button("−", _button, GUILayout.Width(36), GUILayout.Height(28))) _pending = down;
            GUILayout.Label(value, _num, GUILayout.Width(120));
            if (GUILayout.Button("+", _button, GUILayout.Width(36), GUILayout.Height(28))) _pending = up;
            GUILayout.EndHorizontal();
        }

        /// <summary>One of a row of choices (n in the row share its width); the chosen one is lit.</summary>
        private bool Choice(string label, bool on, int n) => GUILayout.Button(label, on ? _buttonOn : _button, GUILayout.Width((Inner - 28f) / n - 6f), GUILayout.Height(30));

        /// <summary>A checkbox row: a box (ticked or not) and its label, which wraps. Clicking anywhere on it toggles it.</summary>
        private bool Check(string label, bool on, float width = 0f)
        {
            float w = width > 0f ? width : Inner - 30f;
            bool clicked = GUILayout.Button(label, _check, GUILayout.Width(w));
            Rect r = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint)
            {
                var box = new Rect(r.x + 4f, r.y + (r.height - 18f) / 2f, 18f, 18f);
                Rounded(box, new Color(0.08f, 0.06f, 0.05f, 1f), 3f);
                Outline(box, on ? Gold : Edge, 3f, 1.5f);
                if (on) Rounded(new Rect(box.x + 4f, box.y + 4f, 10f, 10f), Gold, 2f);
            }
            return clicked;
        }

        private void Bar(string label, float fraction, Color color, float height = 22f)
        {
            Rect r = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            Rounded(r, new Color(0.06f, 0.045f, 0.035f, 1f), 4f);
            if (fraction > 0.001f) Rounded(new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * Mathf.Clamp01(fraction), r.height - 2f), color, 3f);
            Outline(r, new Color(0f, 0f, 0f, 0.8f), 4f, 1f);
            GUIStyle t = BarText();
            Color keep = t.normal.textColor;
            t.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), label, t);
            t.normal.textColor = keep;
            GUI.Label(r, label, t);
            GUILayout.Space(3);
        }

        private GUIStyle _barText;
        private GUIStyle BarText()
        {
            if (_barText != null) return _barText;
            _barText = new GUIStyle(_bold) { alignment = TextAnchor.MiddleCenter, fontSize = 14, wordWrap = false };
            _barText.normal.textColor = new Color(1f, 0.97f, 0.9f);
            return _barText;
        }

        private void Slot(Rect r, bool lit)
        {
            Rounded(r, new Color(0.07f, 0.055f, 0.045f, 1f), 4f);
            Outline(r, lit ? Gold : new Color(0.36f, 0.27f, 0.16f, 1f), 4f, lit ? 2f : 1f);
        }

        private static string Loc(string s) => Localization.instance.Localize(s);
        private static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

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

        private static void Outline(Rect r, Color c, float radius, float width = 1f) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, new Vector4(width, width, width, width), new Vector4(radius, radius, radius, radius));

        // ==== styles =======================================================================================

        private Texture2D MakeBox(Color fill, Color border)
        {
            const int size = 6;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    t.SetPixel(x, y, (x < 2 || y < 2 || x >= size - 2 || y >= size - 2) ? border : fill);
            t.Apply();
            _textures.Add(t);
            return t;
        }

        private GUIStyle ButtonLook(Font font, Color fill, Color hover, Color border, Color text, int size = 14)
        {
            var s = new GUIStyle(GUI.skin.button)
            {
                fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true,
                border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(8, 8, 4, 4), margin = new RectOffset(3, 3, 3, 3),
            };
            if (font != null) s.font = font;
            s.normal.background = MakeBox(fill, border);
            s.hover.background = s.active.background = s.focused.background = MakeBox(hover, border);
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = text;
            return s;
        }

        private static GUIStyle Label(Font font, int size, Color color, FontStyle style = FontStyle.Normal, bool wrap = true)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = style, wordWrap = wrap, richText = false, margin = new RectOffset(2, 2, 2, 2) };
            if (font != null) s.font = font;
            s.normal.textColor = color;
            return s;
        }

        /// <summary>The game's fonts: Averia (its text; the upright one, not the italic) and Norse (its headings).</summary>
        private static Font FindGameFont(string family)
        {
            try
            {
                var fonts = Resources.FindObjectsOfTypeAll<Font>().Where(f => f != null && f.name.IndexOf(family, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                return fonts.FirstOrDefault(f => f.name.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) < 0 && f.name.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? fonts.FirstOrDefault(f => f.name.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) < 0)
                    ?? fonts.FirstOrDefault();
            }
            catch (Exception) { return null; }
        }

        private void EnsureStyles()
        {
            if (_title != null) return;
            Font font = FindGameFont("Averia"), heading = FindGameFont("Norse") ?? font;
            _title = Label(heading, 28, Gold, FontStyle.Normal, false);
            _h2 = Label(heading, 20, Gold);
            _text = Label(font, 15, Text);
            _bold = Label(font, 15, Text, FontStyle.Bold);
            _dim = Label(font, 14, Dim);
            _small = Label(font, 13, Text, FontStyle.Bold, false);
            _small.alignment = TextAnchor.LowerRight;
            _num = Label(font, 17, Gold, FontStyle.Bold, false); // (numbers in Averia: Norse draws 0 as a rune)
            _statNum = Label(font, 26, Gold, FontStyle.Bold, false);
            _num.alignment = TextAnchor.MiddleCenter;
            _good = Label(font, 15, ColGood);
            _warn = Label(font, 15, ColWarn);
            _bad = Label(font, 15, ColBad);
            _mono = Label(null, 12, new Color(0.8f, 0.88f, 0.82f));
            try { _mono.font = Font.CreateDynamicFontFromOSFont("Consolas", 12); } catch (Exception) { }
            _button = ButtonLook(heading, new Color(0.13f, 0.095f, 0.06f), new Color(0.22f, 0.16f, 0.1f), new Color(0.45f, 0.33f, 0.17f), Text);
            _buttonOn = ButtonLook(heading, new Color(0.38f, 0.24f, 0.08f), new Color(0.45f, 0.29f, 0.1f), Gold, new Color(1f, 0.95f, 0.85f));
            _tab0 = ButtonLook(heading, new Color(0.075f, 0.055f, 0.035f), new Color(0.16f, 0.115f, 0.07f), new Color(0.34f, 0.24f, 0.12f), Dim, 16);
            _tab1 = ButtonLook(heading, new Color(0.36f, 0.22f, 0.07f), new Color(0.42f, 0.26f, 0.09f), Gold, new Color(1f, 0.93f, 0.78f), 16);
            _check = ButtonLook(font, new Color(0f, 0f, 0f, 0f), new Color(0.2f, 0.14f, 0.08f, 0.6f), new Color(0f, 0f, 0f, 0f), Text);
            _check.alignment = TextAnchor.MiddleLeft;
            _check.fontStyle = FontStyle.Normal;
            _check.fontSize = 15;
            _check.padding = new RectOffset(30, 6, 5, 5);
            _row = ButtonLook(font, new Color(0.05f, 0.037f, 0.025f), new Color(0.14f, 0.1f, 0.06f), new Color(0.13f, 0.09f, 0.05f), Text, 13);
            _row.alignment = TextAnchor.MiddleLeft; _row.fontStyle = FontStyle.Normal; _row.margin = new RectOffset(0, 0, 1, 1);
            _rowOn = ButtonLook(font, new Color(0.3f, 0.21f, 0.1f), new Color(0.36f, 0.25f, 0.12f), Gold, Text, 13);
            _rowOn.alignment = TextAnchor.MiddleLeft; _rowOn.fontStyle = FontStyle.Normal; _rowOn.margin = new RectOffset(0, 0, 1, 1);
            _card = new GUIStyle(GUI.skin.box) { border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(12, 12, 10, 10), margin = new RectOffset(0, 0, 0, 0) };
            _card.normal.background = MakeBox(new Color(0.075f, 0.055f, 0.038f, 0.97f), new Color(0.27f, 0.19f, 0.1f, 1f));
            _field = new GUIStyle(GUI.skin.textField) { fontSize = 16, padding = new RectOffset(8, 8, 5, 5) };
            if (font != null) _field.font = font;
            _field.normal.background = _field.focused.background = _field.hover.background = MakeBox(new Color(0.07f, 0.055f, 0.045f), Edge);
            _field.normal.textColor = _field.focused.textColor = _field.hover.textColor = Text;
        }

        private void DestroyMenuResources()
        {
            foreach (Texture2D t in _textures) if (t != null) Destroy(t);
            _textures.Clear();
            _title = _h2 = _text = _bold = _dim = _small = _good = _warn = _bad = _mono = _button = _buttonOn = _tab0 = _tab1 = _check = _field = _card = _row = _rowOn = _num = _barText = _statNum = null;
        }
    }
}
