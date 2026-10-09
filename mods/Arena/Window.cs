using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
            if (!Ui.Ready()) { Hud.Say("The game's interface is not ready yet."); return; }
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
            string title = _context == Context.Master ? "The Arena Master" : "Arena Waystone";
            Ui.Label(w, title, new Vector2(0f, -22f), new Vector2(W, 44f), 34f, Ui.Warm, Ui.Norse, TextAlignmentOptions.Center, new Vector2(0.5f, 1f)).rectTransform.pivot = new Vector2(0.5f, 1f);
            Ui.Braid(w, new Vector2(0f, -72f), 360f);
            Ui.Label(w, $"{Ladder.Title()}  ·  {Ladder.Wins} wins", new Vector2(-Pad - 44f, -30f), new Vector2(240f, 24f), 15f, Ui.Gold, Ui.Serif, TextAlignmentOptions.TopRight, new Vector2(1f, 1f)).rectTransform.pivot = new Vector2(1f, 1f);
            Ui.Button(w, "X", new Vector2(W - 54f, -16f), new Vector2(38f, 34f), Close);

            if (_context == Context.Master)
            {
                Ui.Label(w, "<i>\"Step up, challenger! Blood, glory and gold await. The crowd is hungry.\"</i>", new Vector2(Pad, -86f), new Vector2(W - 2 * Pad, 24f), 16f, Ui.Dim, Ui.Serif, TextAlignmentOptions.Center);
                string[] tabs = { "Contests", "Duel", "Standing" };
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
            Ui.Label(w, text, new Vector2(Pad, y), new Vector2(W - 2 * Pad, 24f), 19f, Ui.Warm, Ui.Serif);
            return y - 28f;
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

            y = Heading(w, "The contest", y);
            string[] kinds = { "The Long Road", "Champion Bout", "Endless Horde", Ladder.DailyDone ? "Today's Trial (won)" : "Today's Trial" };
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
                y = Line(w, "From the Meadows to the Ashlands: three rounds in each land, its champion in the third, and on to the next, as far as you can get.", y, Ui.Text, 15f);
                y = Line(w, "You start in the plainest Meadows gear from a chest, with whatever weapon the armourer hands you, and keep it, building it up as you go: " +
                            "the fighters drop coins; between lands a reward chest holds a free upgrade, and the Armourer sells upgrades (or another kind of weapon), fresh food and meads. " +
                            "The crowd throws the rest when you please them, and the kitchen's meals are leftovers, so you will need them.", y, Ui.Dim, 14f);
            }
            else if (daily)
            {
                Contest.TodaysPlan(out tier, out style, out fists, out noFood, out hard, out timed);
                y = Line(w, $"Today: three rounds in the <color=#ffd27a>{Roster.TierNames[tier]}</color>, its champion in the third, with the arena's <color=#ffd27a>{Kit.Styles[style].ToLowerInvariant()}</color>, under {Rules.TextOf(fists, noFood, hard, timed)}. " +
                            (Ladder.DailyDone ? "You have won it today: it pays as usual." : "Win it and the prize is half as big again."), y, Ui.Text, 15f);
            }
            else
            {
                y = Line(w, kind == Contest.KindOf.Champion ? "One named champion, a head taller, with as many stars as it can bear. You fight with what you carry." : "Wave after wave, harder each time, until you fall or take your purse. You fight with what you carry.", y, Ui.Dim);
                y = Heading(w, "Who you fight  <size=70%><color=#bdb7a9>(each land opens as your world beats the boss before it)</color></size>", y);
                y = Choices(w, y, Enumerable.Range(0, stage + 1).ToList(), t => Roster.TierNames[t], t => t == _tier, t => _tier = t);
                tier = Mathf.Clamp(_tier, 0, stage);
                y = Line(w, $"{Roster.PoolText(tier)}. The champion: {ChampionName(tier)}.", y, Ui.Dim, 14f);
            }

            if (!daily)
            {
                y = Heading(w, "Rules  <size=70%><color=#bdb7a9>(each makes the prize bigger)</color></size>", y);
                var rules = new List<string> { "fists", "food", "hard", "timed" };
                y = Choices(w, y, rules, r => r == "fists" ? "Bare fists +60%" : r == "food" ? "No food +30%" : r == "hard" ? "Hard +50%" : "Timed rounds +40%",
                            r => r == "fists" ? _fists : r == "food" ? _noFood : r == "hard" ? _hard : _timed,
                            r => { if (r == "fists") _fists = !_fists; else if (r == "food") _noFood = !_noFood; else if (r == "hard") _hard = !_hard; else _timed = !_timed; });
            }

            int coins = player != null ? player.GetInventory().CountItems("$item_coins") : 0;
            int fee = Contest.FeeFor(kind, tier);
            bool staking = kind == Contest.KindOf.Champion || daily;
            if (staking)
            {
                y = Heading(w, $"Stake on yourself  <size=70%><color=#bdb7a9>(paid double if you win · you have {coins} coins)</color></size>", y);
                if (_stake > coins - fee) _stake = 0;
                y = Choices(w, y, Stakes, s => s == 0 ? "None" : s.ToString(), s => s == _stake, s => _stake = s, s => s <= coins - fee);
            }

            float mult = Rules.MultiplierOf(fists, noFood, hard, timed) * 1.15f * Plugin.Rewards.Value / 100f;
            string mat = Roster.Material(tier);
            string prize = kind == Contest.KindOf.Endless
                ? $"Each wave adds to your purse (about {Mathf.RoundToInt((20 + 30 * tier) * 0.8f * mult)} coins for the first, more after)."
                : kind == Contest.KindOf.Road
                ? "The fighters drop coins, more in each land and with the crowd's favour: spend them on upgrades, or walk out with them. Champions drop their trophy and the land's metal (in the lands your world has reached)."
                : $"A win pays about <color=#ffd27a>{Mathf.RoundToInt((20 + 30 * tier) * (daily ? 3.3f : 3f) * mult * 1.25f * (daily && !Ladder.DailyDone ? 1.5f : 1f))} coins</color>" + (mat != null && tier <= stage ? " and " + Contest.ItemName(mat) : "") + ", more with the crowd's favour, and the champion's trophy.";
            string entry = fee == 0 ? (Ladder.Get("fights") == 0 ? "<color=#a8e88a>Your first fight is on the house.</color> " : "") : $"Entry <color=#ffd27a>{fee} coins</color> (you have {coins}). ";
            y = Line(w, entry + prize, y - 2f, Ui.Text, 15f);
            y = Line(w, (lent ? "<color=#ffd27a>The Arena Master holds everything you carry while you fight</color>, and gives it back when you come out (or when you rise again). " : "")
                        + "<color=#ff9a6a>Death is real in the ring:</color> fall and your purse is lost" + (lent ? "." : " and your tombstone waits in the forecourt."), y, Ui.Dim, 14f);
            if (Net.RemoteFight) y = Line(w, Net.BusyText + ". Wait your turn, or watch from the stands.", y, Ui.Warn);
            if (_message.Length > 0) y = Line(w, _message, y, Ui.Warn);

            float bottom = -_window.sizeDelta.y + 30f + 48f;
            bool can = !Net.RemoteFight && Scenery.Built && coins >= fee;
            Contest.KindOf k2 = kind; int t2 = tier, s2 = style; bool f2 = fists, n2 = noFood, h2 = hard, ti2 = timed;
            Ui.Button(w, fee > 0 ? $"Enter the arena  ({fee} coins)" : "Enter the arena", new Vector2(W / 2f - 170f, bottom), new Vector2(340f, 46f), () =>
            {
                string why = Contest.Start(k2, t2, s2, f2, n2, h2, ti2, staking ? _stake : 0, daily);
                if (why == null) Close(); else { _message = why; Changed(); }
            }, can);
            Ui.Label(w, "The grate in the tunnel beyond the main gate rises: walk through it into the ring and the fight begins.", new Vector2(Pad, bottom - 52f), new Vector2(W - 2 * Pad, 20f), 13f, Ui.Dim, null, TextAlignmentOptions.Center);
        }

        private static string ChampionName(int tier) => Roster.ChampionText(tier);

        // ---- duels ------------------------------------------------------------------------------------------------------------------

        private static void Duels(Transform w, float y)
        {
            Player me = Player.m_localPlayer;
            y = Heading(w, "Duel another player", y);
            y = Line(w, "You both walk into the ring and fight with what you carry. The first to drop to a fifth of their health loses; nobody dies in a duel. The winner takes both wagers, less a tenth to the arena. Leaving the ring forfeits.", y, Ui.Dim);

            if (Duel.HasIncoming)
            {
                y = Line(w, $"<color=#a8e88a>{Duel.IncomingName} challenges you" + (Duel.IncomingWager > 0 ? $" for {Duel.IncomingWager} coins each!" : "!") + "</color>", y - 6f, Ui.Text, 18f);
                Ui.Button(w, "Accept", new Vector2(Pad, y), new Vector2(200f, 40f), () => { string why = Duel.Accept(); if (why != null) { _message = why; Changed(); } else Close(); });
                Ui.Button(w, "Decline", new Vector2(Pad + 210f, y), new Vector2(200f, 40f), () => { Duel.Decline(); Changed(); });
                y -= 52f;
            }
            if (Duel.Active) { Line(w, "A duel is on.", y, Ui.Good); return; }

            int coins = me != null ? me.GetInventory().CountItems("$item_coins") : 0;
            if (_wager > coins) _wager = 0;
            y = Heading(w, $"Wager  <size=70%><color=#bdb7a9>(each · you have {coins} coins)</color></size>", y);
            y = Choices(w, y, Wagers, v => v == 0 ? "None" : v.ToString(), v => v == _wager, v => _wager = v, v => v <= coins);

            y = Heading(w, "At the arena", y);
            var others = Player.GetAllPlayers().Where(p => p != null && p != me && Travel.Distance(p.transform.position) < 90f).ToList();
            if (others.Count == 0) y = Line(w, "No other player is at the arena. A duel needs a second player (with this mod).", y, Ui.Dim);
            foreach (Player other in others.Take(6))
            {
                Ui.Label(w, other.GetPlayerName(), new Vector2(Pad, y - 8f), new Vector2(360f, 28f), 18f, Ui.Text, Ui.Serif);
                Player target = other;
                Ui.Button(w, "Challenge", new Vector2(W - Pad - 180f, y), new Vector2(180f, 38f), () => { string why = Duel.Challenge(target, _wager, Site.Centre); _message = why ?? "Challenge sent: waiting for an answer."; Changed(); });
                y -= 46f;
            }
            if (_message.Length > 0) Line(w, _message, y, Ui.Good);
        }

        // ---- standing ----------------------------------------------------------------------------------------------------------------

        private static void Standing(Transform w, float y)
        {
            y = Heading(w, "Your standing", y);
            string next = Ladder.Next();
            y = Line(w, $"<color=#ffd27a>{Ladder.Title()}</color>  ·  {Ladder.Wins} wins ({Ladder.Get("roads")} on the Long Road, {Ladder.Get("champions")} champion bouts, {Ladder.Get("trials")} trials)" + (next != null ? $"\nNext title: {next}" : "\nYou hold every title."), y, Ui.Text);
            y = Line(w, $"Fights {Ladder.Get("fights")}  ·  creatures beaten {Ladder.Get("kills")}  ·  deaths in the ring {Ladder.Get("deaths")}  ·  best crowd favour {Ladder.Get("favour")}  ·  duels won {Ladder.Get("duelwins")}, lost {Ladder.Get("duellosses")}", y, Ui.Dim, 14f);
            int road = Ladder.Get("road");
            var best = new List<string> { road > 0 ? $"the Long Road to the {Roster.TierNames[Mathf.Min((road - 1) / 3, Roster.TierNames.Length - 1)]} (round {road})" : "the Long Road not yet walked" };
            for (int t = 0; t <= Roster.Stage(); t++) if (Ladder.Get("bestwave" + t) > 0) best.Add($"{Roster.TierNames[t]} horde: wave {Ladder.Get("bestwave" + t)}");
            y = Line(w, "Your best: " + string.Join("  ·  ", best), y, Ui.Dim, 14f);

            y = Heading(w, "The Hall of Champions  <size=70%><color=#bdb7a9>(one for each contest: their statues stand in the forecourt)</color></size>", y - 6f);
            RectTransform box = Ui.Box(w, new Vector2(Pad, y), new Vector2(W - 2 * Pad, Ladder.Kinds.Length * 52f + 16f));
            for (int i = 0; i < Ladder.Kinds.Length; i++)
            {
                float ry = -10f - i * 52f;
                Ui.Label(box, Ladder.KindTitles[i], new Vector2(14f, ry), new Vector2(220f, 26f), 17f, Ui.Gold, Ui.Serif);
                if (Ladder.Champion(Ladder.Kinds[i], out Ladder.Entry champ))
                {
                    Ui.Label(box, champ.Name, new Vector2(240f, ry), new Vector2(300f, 26f), 17f, Ui.Text, Ui.Serif);
                    Ui.Label(box, champ.Score.ToString(), new Vector2(W - 2 * Pad - 110f, ry), new Vector2(96f, 26f), 17f, Ui.Text, Ui.Serif, TextAlignmentOptions.TopRight);
                    Ui.Label(box, champ.What, new Vector2(240f, ry - 24f), new Vector2(W - 2 * Pad - 260f, 22f), 14f, Ui.Dim);
                }
                else Ui.Label(box, "Unclaimed: be the first", new Vector2(240f, ry), new Vector2(300f, 26f), 16f, Ui.Dim);
            }
        }

        // ---- the waystones ------------------------------------------------------------------------------------------------------------

        private static void Waystone(Transform w, float y)
        {
            Player me = Player.m_localPlayer;
            if (!Site.Known) { Line(w, "The arena has not found its ground in this world yet (the game hosting the world picks it).", y, Ui.Warn); return; }
            float km = me != null ? Travel.Distance(me.transform.position) / 1000f : 0f;
            y = Line(w, $"The Arena stands {km:0.0} km from here. The waystone takes you to its forecourt, where the Hall of Fame stands and the Arena Master takes your name. The waystone you arrive by brings you back here.", y, Ui.Text, 16f);
            if (Net.RemoteFight) y = Line(w, Net.BusyText + ": come and watch from the stands.", y, Ui.Good);
            var champs = new List<string>();
            for (int i = 0; i < Ladder.Kinds.Length; i++) if (Ladder.Champion(Ladder.Kinds[i], out Ladder.Entry champ)) champs.Add($"{Ladder.KindTitles[i]}: <color=#ffd27a>{champ.Name}</color>");
            if (champs.Count > 0) y = Line(w, "Champions of the arena: " + string.Join("  ·  ", champs), y, Ui.Text);
            bool ok = me != null && me.IsTeleportable(false);
            if (!ok) y = Line(w, "You are carrying something that cannot travel by waystone (ore, metal...), as with portals.", y, Ui.Warn);
            Ui.Button(w, "Travel to the Arena", new Vector2(W / 2f - 160f, -_window.sizeDelta.y + 30f + 48f), new Vector2(320f, 46f), () => { Stand from = _stone; Close(); if (from != null) Travel.ToArena(from); }, ok);
        }

        private static void Arrival(Transform w, float y)
        {
            y = Line(w, "The forecourt of the Arena. Speak to the Arena Master at his desk to fight. Climb the stairs inside the main gate, or go straight up from here, to watch.", y, Ui.Text, 16f);
            if (Net.RemoteFight) y = Line(w, Net.BusyText, y, Ui.Good);
            bool home = Travel.HasReturn(out _);
            float b = -_window.sizeDelta.y + 30f + 48f;
            Ui.Button(w, "Up to the stands", new Vector2(Pad + 20f, b), new Vector2(300f, 46f), () => { Close(); Travel.ToStands(); });
            Ui.Button(w, home ? "Travel home" : "Travel home (you came on foot)", new Vector2(W - Pad - 320f, b), new Vector2(300f, 46f), () => { Close(); Travel.Home(); }, home);
        }
    }
}
