using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static DHack.Shared.Translations;

namespace Arena
{
    /// <summary>
    /// The menus, in the game's own look (Ui.cs). At the Arena Master: pick a contest (the Long Road, a Champion Bout, the Endless Horde, or
    /// today's trial), your weapon and meals or the land you fight, its rules and a stake; challenge a player to a duel; see your standing
    /// and the Hall of Champions.
    /// At a waystone: travel to the arena; at the forecourt's waystone: travel home, or go up to the stands to watch.
    /// </summary>
    internal static class Window
    {
        internal enum Context { Master, Waystone, Arrival }


        internal static bool IsOpen => _window != null;
        private static RectTransform _window;
        private static Context _context;
        private static Stand _stone;
        private static int _tab, _tier, _stake, _wager, _kind;
        internal static int ContestKind { set { _kind = value; } }   // (for testing)
        private static bool _fists, _noFood, _hard, _timed, _tierSet, _dirty;
        private static string _message = "";
        private static Vector3 _openedAt;
        private static float _nextRefresh;
        private static readonly int[] Stakes = { 0, 10, 50, 100, 250, 500 };
        private static readonly int[] Wagers = { 0, 10, 50, 100, 250 };

        private const float W = 760f, Pad = 34f;

        internal static void Open(Context context, Stand stone = null, int tab = 0)
        {
            if (!Ui.Ready()) { Hud.Say(T("arena_ui_not_ready")); return; }
            _context = context; _stone = stone; _tab = tab; _message = "";
            _openedAt = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            if (!_tierSet) { _tier = Roster.Stage(); _tierSet = true; }
            _tier = Mathf.Min(_tier, Roster.Stage());
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            Build();
        }

        internal static void Close()
        {
            if (_window != null) Object.Destroy(_window.gameObject);
            _window = null; _stone = null;
        }

        internal static void Tick()
        {
            if (!IsOpen) return;
            Player p = Player.m_localPlayer;
            if (p == null || Contest.Active && !Contest.Waiting) { Close(); return; }
            if ((p.transform.position - _openedAt).sqrMagnitude > 12f * 12f) { Close(); return; }
            if (_dirty || (Time.unscaledTime > _nextRefresh && Signature() != _signature)) Build();   // (a challenge coming in, the arena's state)
            if (Time.unscaledTime > _nextRefresh) _nextRefresh = Time.unscaledTime + 1f;
        }

        internal static void Draw() { }   // (drawn by the game's interface now)

        private static void Changed() => _dirty = true;

        private static string _signature = "";
        private static string Signature()
        {
            Player p = Player.m_localPlayer;
            return $"{Net.RemoteFight}{Duel.HasIncoming}{Duel.Active}{Scenery.Built}{(p != null ? p.GetInventory().CountItems("$item_coins") + "/" + p.GetInventory().NrOfItems() : "")}{Ladder.Hall().Count}";
        }

        // ---- the window --------------------------------------------------------------------------------------------------------

        private static void Build()
        {
            _dirty = false;
            _signature = Signature();
            _nextRefresh = Time.unscaledTime + 1f;
            if (_window != null) Object.Destroy(_window.gameObject);
            float h = _context == Context.Master ? 720f : 380f;
            _window = Ui.Window("ArenaWindow", new Vector2(W, h));
            Transform w = _window;
            string title = T(_context == Context.Master ? "arena_master" : "arena_waystone");
            Ui.Label(w, title, new Vector2(0f, -22f), new Vector2(W, 44f), 34f, Ui.Warm, Ui.Norse, TextAlignmentOptions.Center, new Vector2(0.5f, 1f)).rectTransform.pivot = new Vector2(0.5f, 1f);
            Ui.Braid(w, new Vector2(0f, -72f), 360f);
            Ui.Fit(Ui.Label(w, T("arena_rank_wins", Ladder.Title(), Ladder.Wins), new Vector2(-Pad - 44f, -30f), new Vector2(240f, 24f), 15f, Ui.Gold, Ui.Serif, TextAlignmentOptions.TopRight, new Vector2(1f, 1f))).rectTransform.pivot = new Vector2(1f, 1f);
            Ui.Button(w, "X", new Vector2(W - 54f, -16f), new Vector2(38f, 34f), Close);

            if (_context == Context.Master)
            {
                Ui.Fit(Ui.Label(w, "<i>" + T("arena_master_quote") + "</i>", new Vector2(Pad, -86f), new Vector2(W - 2 * Pad, 24f), 16f, Ui.Dim, Ui.Serif, TextAlignmentOptions.Center));
                string[] tabs = { T("arena_tab_contests"), T("arena_tab_duel"), T("arena_tab_standing") };
                float tw = (W - 2 * Pad) / tabs.Length;
                for (int i = 0; i < tabs.Length; i++) { int k = i; Ui.Tab(w, tabs[i], new Vector2(Pad + i * tw, -116f), new Vector2(tw - 4f, 38f), i == _tab, () => { _tab = k; _message = ""; Changed(); }, true, 18f); }
                if (_tab == 0) Contests(w, -166f);
                else if (_tab == 1) Duels(w, -166f);
                else Standing(w, -166f);
            }
            else if (_context == Context.Waystone) Waystone(w, -92f);
            else Arrival(w, -92f);
        }

        private static float Heading(Transform w, string text, float y)
        {
            // (as tall as it needs: a longer language may take two lines)
            TMP_Text t = Ui.Label(w, text, new Vector2(Pad, y), new Vector2(W - 2 * Pad, 24f), 19f, Ui.Warm, Ui.Serif);
            return y - Mathf.Max(24f, t.GetPreferredValues(text, W - 2 * Pad, 0f).y + 2f) - 4f;
        }

        private static float Line(Transform w, string text, float y, Color color, float size = 15f, float height = 0f)
        {
            TMP_Text t = Ui.Label(w, text, new Vector2(Pad, y), new Vector2(W - 2 * Pad, height > 0f ? height : 22f), size, color);
            float used = height > 0f ? height : Mathf.Max(20f, t.GetPreferredValues(text, W - 2 * Pad, 0f).y + 2f);
            return y - used - 4f;
        }

        private static float Choices<T>(Transform w, float y, IList<T> values, System.Func<T, string> label, System.Func<T, bool> on, System.Action<T> pick, System.Func<T, bool> allowed = null, float height = 34f)
        {
            float gap = 4f, width = (W - 2 * Pad - gap * (values.Count - 1)) / values.Count;
            for (int i = 0; i < values.Count; i++)
            {
                T v = values[i];
                Ui.Tab(w, label(v), new Vector2(Pad + i * (width + gap), y), new Vector2(width, height), on(v), () => { pick(v); Changed(); }, allowed == null || allowed(v), values.Count >= 6 ? 13.5f : 15f);
            }
            return y - height - 8f;
        }

        // ---- the contests --------------------------------------------------------------------------------------------------------

        private static void Contests(Transform w, float y)
        {
            Player player = Player.m_localPlayer;
            int stage = Roster.Stage();

            y = Heading(w, T("arena_head_contest"), y);
            string[] kinds = { T("arena_kind_road"), T("arena_kind_champion"), T("arena_choice_endless"), T(Ladder.DailyDone ? "arena_choice_trial_won" : "arena_kind_trial") };
            y = Choices(w, y, Enumerable.Range(0, 4).ToList(), k => kinds[k], k => k == _kind, k => _kind = k, null, 38f);

            Contest.KindOf kind = _kind == 0 ? Contest.KindOf.Road : _kind == 1 ? Contest.KindOf.Champion : _kind == 2 ? Contest.KindOf.Endless : Contest.KindOf.Trial;
            bool daily = kind == Contest.KindOf.Trial;
            bool lent = kind == Contest.KindOf.Road || daily;
            int tier, style = 0;
            bool fists = _fists, noFood = _noFood, hard = _hard, timed = _timed;
            if (kind == Contest.KindOf.Road)
            {
                tier = 0;
                style = -1;   // (the armourer's pick: a random Meadows weapon)
                y = Line(w, T("arena_road_desc"), y, Ui.Text, 15f);
                y = Line(w, T("arena_road_desc_more"), y, Ui.Dim, 14f);
            }
            else if (daily)
            {
                Contest.TodaysPlan(out tier, out style, out fists, out noFood, out hard, out timed);
                y = Line(w, T("arena_daily_desc", Gold(Roster.LandName(tier)), Gold(T($"arena_style_{style}")), Rules.TextOf(fists, noFood, hard, timed)) + " " +
                            T(Ladder.DailyDone ? "arena_daily_done" : "arena_daily_bonus"), y, Ui.Text, 15f);
            }
            else
            {
                y = Line(w, T(kind == Contest.KindOf.Champion ? "arena_champion_desc" : "arena_endless_desc"), y, Ui.Dim);
                y = Heading(w, T("arena_head_who") + "  " + Hint(T("arena_head_who_hint")), y);
                y = Choices(w, y, Enumerable.Range(0, stage + 1).ToList(), t => Roster.LandName(t), t => t == _tier, t => _tier = t);
                tier = Mathf.Clamp(_tier, 0, stage);
                y = Line(w, T("arena_pool_line", Roster.PoolText(tier), ChampionName(tier)), y, Ui.Dim, 14f);
            }

            if (!daily)
            {
                y = Heading(w, T("arena_head_rules") + "  " + Hint(T("arena_head_rules_hint")), y);
                var rules = new List<string> { "fists", "food", "hard", "timed" };
                y = Choices(w, y, rules, r => r == "fists" ? T("arena_btn_fists", 60) : r == "food" ? T("arena_btn_nofood", 30) : r == "hard" ? T("arena_btn_hard", 50) : T("arena_btn_timed", 40),
                            r => r == "fists" ? _fists : r == "food" ? _noFood : r == "hard" ? _hard : _timed,
                            r => { if (r == "fists") _fists = !_fists; else if (r == "food") _noFood = !_noFood; else if (r == "hard") _hard = !_hard; else _timed = !_timed; });
            }

            int coins = player != null ? player.GetInventory().CountItems("$item_coins") : 0;
            int fee = Contest.FeeFor(kind, tier);
            bool staking = kind == Contest.KindOf.Champion || daily;
            if (staking)
            {
                y = Heading(w, T("arena_head_stake") + "  " + Hint(T("arena_head_stake_hint", coins)), y);
                if (_stake > coins - fee) _stake = 0;
                y = Choices(w, y, Stakes, s => s == 0 ? T("arena_none") : s.ToString(), s => s == _stake, s => _stake = s, s => s <= coins - fee);
            }

            float mult = Rules.MultiplierOf(fists, noFood, hard, timed) * 1.15f * Plugin.Rewards.Value / 100f;
            string mat = Roster.Material(tier);
            string winCoins = Gold(T("arena_coins", Mathf.RoundToInt((20 + 30 * tier) * (daily ? 3.3f : 3f) * mult * 1.25f * (daily && !Ladder.DailyDone ? 1.5f : 1f))));
            string prize = kind == Contest.KindOf.Endless ? T("arena_prize_endless", Mathf.RoundToInt((20 + 30 * tier) * 0.8f * mult))
                : kind == Contest.KindOf.Road ? T("arena_prize_road")
                : mat != null && tier <= Roster.Beaten() ? T("arena_prize_win_mat", winCoins, Contest.ItemName(mat)) : T("arena_prize_win", winCoins);
            string entry = fee == 0 ? (Ladder.Get("fights") == 0 ? "<color=#a8e88a>" + T("arena_first_free") + "</color> " : "") : T("arena_entry", Gold(T("arena_coins", fee)), coins) + " ";
            y = Line(w, entry + prize, y - 2f, Ui.Text, 15f);
            y = Line(w, (lent ? T("arena_lent_note") + " " : "") + T(lent ? "arena_death_lent" : "arena_death_own"), y, Ui.Dim, 14f);
            if (Net.RemoteFight) y = Line(w, T("arena_busy_wait", Net.BusyText), y, Ui.Warn);
            if (_message.Length > 0) y = Line(w, _message, y, Ui.Warn);

            float bottom = -_window.sizeDelta.y + 30f + 48f;
            bool can = !Net.RemoteFight && Scenery.Built && coins >= fee;
            Contest.KindOf k2 = kind; int t2 = tier, s2 = style; bool f2 = fists, n2 = noFood, h2 = hard, ti2 = timed;
            Ui.Button(w, fee > 0 ? T("arena_enter_fee", fee) : T("arena_enter"), new Vector2(W / 2f - 170f, bottom), new Vector2(340f, 46f), () =>
            {
                string why = Contest.Start(k2, t2, s2, f2, n2, h2, ti2, staking ? _stake : 0, daily);
                if (why == null) Close(); else { _message = why; Changed(); }
            }, can);
            Ui.Fit(Ui.Label(w, T("arena_enter_hint"), new Vector2(Pad, bottom - 52f), new Vector2(W - 2 * Pad, 20f), 13f, Ui.Dim, null, TextAlignmentOptions.Center));
        }

        private static string ChampionName(int tier) => Roster.ChampionText(tier);

        /// <summary>A number or a name in gold, inside a translated line.</summary>
        private static string Gold(string text) => "<color=#ffd27a>" + text + "</color>";

        /// <summary>The small grey note after a heading, in brackets.</summary>
        private static string Hint(string text) => "<size=70%><color=#bdb7a9>(" + text + ")</color></size>";

        // ---- duels ------------------------------------------------------------------------------------------------------------------

        private static void Duels(Transform w, float y)
        {
            Player me = Player.m_localPlayer;
            y = Heading(w, T("arena_head_duel"), y);
            y = Line(w, T("arena_duel_desc"), y, Ui.Dim);

            if (Duel.HasIncoming)
            {
                y = Line(w, "<color=#a8e88a>" + (Duel.IncomingWager > 0 ? T("arena_challenged_wager", Duel.IncomingName, Duel.IncomingWager) : T("arena_challenged", Duel.IncomingName)) + "</color>", y - 6f, Ui.Text, 18f);
                Ui.Button(w, T("arena_accept"), new Vector2(Pad, y), new Vector2(200f, 40f), () => { string why = Duel.Accept(); if (why != null) { _message = why; Changed(); } else Close(); });
                Ui.Button(w, T("arena_decline"), new Vector2(Pad + 210f, y), new Vector2(200f, 40f), () => { Duel.Decline(); Changed(); });
                y -= 52f;
            }
            if (Duel.Active) { Line(w, T("arena_duel_on"), y, Ui.Good); return; }

            int coins = me != null ? me.GetInventory().CountItems("$item_coins") : 0;
            if (_wager > coins) _wager = 0;
            y = Heading(w, T("arena_head_wager") + "  " + Hint(T("arena_head_wager_hint", coins)), y);
            y = Choices(w, y, Wagers, v => v == 0 ? T("arena_none") : v.ToString(), v => v == _wager, v => _wager = v, v => v <= coins);

            y = Heading(w, T("arena_head_at_arena"), y);
            var others = Player.GetAllPlayers().Where(p => p != null && p != me && Travel.Distance(p.transform.position) < 90f).ToList();
            if (others.Count == 0) y = Line(w, T("arena_no_players"), y, Ui.Dim);
            foreach (Player other in others.Take(6))
            {
                Ui.Label(w, other.GetPlayerName(), new Vector2(Pad, y - 8f), new Vector2(360f, 28f), 18f, Ui.Text, Ui.Serif);
                Player target = other;
                Ui.Button(w, T("arena_challenge"), new Vector2(W - Pad - 180f, y), new Vector2(180f, 38f), () => { string why = Duel.Challenge(target, _wager, Site.Centre); _message = why ?? T("arena_challenge_sent"); Changed(); });
                y -= 46f;
            }
            if (_message.Length > 0) Line(w, _message, y, Ui.Good);
        }

        // ---- standing ----------------------------------------------------------------------------------------------------------------

        private static void Standing(Transform w, float y)
        {
            y = Heading(w, T("arena_head_standing"), y);
            string next = Ladder.Next();
            y = Line(w, Gold(Ladder.Title()) + "  ·  " + T("arena_standing_wins", Ladder.Wins, Ladder.Get("roads"), Ladder.Get("champions"), Ladder.Get("trials"))
                        + "\n" + (next != null ? T("arena_next_title", next) : T("arena_every_title")), y, Ui.Text);
            y = Line(w, T("arena_stats", Ladder.Get("fights"), Ladder.Get("kills"), Ladder.Get("deaths"), Ladder.Get("favour"), Ladder.Get("duelwins"), Ladder.Get("duellosses")), y, Ui.Dim, 14f);
            int road = Ladder.Get("road");
            var best = new List<string> { road > 0 ? T("arena_best_road", Roster.LandName((road - 1) / 3), road) : T("arena_best_road_none") };
            for (int t = 0; t <= Roster.Stage(); t++) if (Ladder.Get("bestwave" + t) > 0) best.Add(T("arena_best_horde", Roster.LandName(t), Ladder.Get("bestwave" + t)));
            y = Line(w, T("arena_your_best", string.Join("  ·  ", best)), y, Ui.Dim, 14f);

            y = Heading(w, T("arena_head_hall") + "  " + Hint(T("arena_head_hall_hint")), y - 6f);
            RectTransform box = Ui.Box(w, new Vector2(Pad, y), new Vector2(W - 2 * Pad, Ladder.Kinds.Length * 52f + 16f));
            for (int i = 0; i < Ladder.Kinds.Length; i++)
            {
                float ry = -10f - i * 52f;
                Ui.Fit(Ui.Label(box, Ladder.KindTitle(i), new Vector2(14f, ry), new Vector2(220f, 26f), 17f, Ui.Gold, Ui.Serif));
                if (Ladder.Champion(Ladder.Kinds[i], out Ladder.Entry champ))
                {
                    Ui.Label(box, champ.Name, new Vector2(240f, ry), new Vector2(300f, 26f), 17f, Ui.Text, Ui.Serif);
                    Ui.Label(box, champ.Score.ToString(), new Vector2(W - 2 * Pad - 110f, ry), new Vector2(96f, 26f), 17f, Ui.Text, Ui.Serif, TextAlignmentOptions.TopRight);
                    Ui.Fit(Ui.Label(box, champ.Run, new Vector2(240f, ry - 24f), new Vector2(W - 2 * Pad - 260f, 22f), 14f, Ui.Dim));
                }
                else Ui.Label(box, T("arena_unclaimed_first"), new Vector2(240f, ry), new Vector2(300f, 26f), 16f, Ui.Dim);
            }
        }

        // ---- the waystones ------------------------------------------------------------------------------------------------------------

        private static void Waystone(Transform w, float y)
        {
            Player me = Player.m_localPlayer;
            if (!Site.Known) { Line(w, T("arena_no_site"), y, Ui.Warn); return; }
            float km = me != null ? Travel.Distance(me.transform.position) / 1000f : 0f;
            y = Line(w, T("arena_waystone_window", km.ToString("0.0")), y, Ui.Text, 16f);
            if (Net.RemoteFight) y = Line(w, T("arena_busy_watch", Net.BusyText), y, Ui.Good);
            var champs = new List<string>();
            for (int i = 0; i < Ladder.Kinds.Length; i++) if (Ladder.Champion(Ladder.Kinds[i], out Ladder.Entry champ)) champs.Add(T("arena_kind_champ", Ladder.KindTitle(i), Gold(champ.Name)));
            if (champs.Count > 0) y = Line(w, T("arena_champions_list", string.Join("  ·  ", champs)), y, Ui.Text);
            bool ok = me != null && me.IsTeleportable(false);
            if (!ok) y = Line(w, T("arena_not_teleportable"), y, Ui.Warn);
            Ui.Button(w, T("arena_travel_to"), new Vector2(W / 2f - 160f, -_window.sizeDelta.y + 30f + 48f), new Vector2(320f, 46f), () => { Stand from = _stone; Close(); if (from != null) Travel.ToArena(from); }, ok);
        }

        private static void Arrival(Transform w, float y)
        {
            y = Line(w, T("arena_arrival_desc"), y, Ui.Text, 16f);
            if (Net.RemoteFight) y = Line(w, Net.BusyText, y, Ui.Good);
            bool home = Travel.HasReturn(out _);
            float b = -_window.sizeDelta.y + 30f + 48f;
            Ui.Button(w, T("arena_up_stands"), new Vector2(Pad + 20f, b), new Vector2(300f, 46f), () => { Close(); Travel.ToStands(); });
            Ui.Button(w, T(home ? "arena_travel_home" : "arena_travel_home_foot"), new Vector2(W - Pad - 320f, b), new Vector2(300f, 46f), () => { Close(); Travel.Home(); }, home);
        }
    }
}
