using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
using static DHack.Shared.Translations;

namespace Arena
{
    /// <summary>
    /// A contest, run by the game of the player who entered it. Each costs an entry fee (the first fight of a character is on the house).
    ///
    /// Fought with the arena's own steel (Kit.cs: your things are held by the Arena Master while you fight, and you are armed and fed by the
    /// arena): the Long Road, from the Meadows to the Ashlands, three rounds in each land with that land's champion in the third, the
    /// armourer and the kitchen bringing the next land's steel and meals as you go, as far as you can get; and Today's Trial, one land picked
    /// for the day with its weapon and rules, worth half as much again the first time you win it.
    /// Fought with what you carry: a Champion Bout, one named champion of a land you have reached; the Endless Horde, wave after wave of one
    /// land, harder each time, until you fall or take your purse.
    ///
    /// Fighters come in through the gates round the ring. Each round you clear adds to your purse (more with the rules and the crowd's favour).
    /// Between rounds you can take the purse and leave. Win and it is paid in full with a bonus, your stake doubled and the champions' trophies;
    /// give up mid-round, run out of time, leave the ring or leave the game and you get half; die and you get nothing, and your tombstone is carried out of the
    /// ring for you to collect.
    /// </summary>
    internal static class Contest
    {
        internal enum PhaseKind { None, Ready, Arming, Countdown, Spawning, Fighting, Break, Victory }
        internal enum KindOf { Road, Champion, Endless, Trial }
        // (Aborted: the fighter's game left or the mod unloaded; CalledOff: the arena had no one to send in)
        private enum Outcome { Win, CashOut, Yield, Died, TimeUp, LeftRing, Carried, Aborted, CalledOff }

        internal static PhaseKind Phase;
        internal static KindOf Kind;
        internal static string Title = "";   // (in English, for the log; TitleLine, in Words, is what other players are told)
        internal static string TitleLine = "";
        internal static int Round, Rounds, Tier, Stake, Fee;
        internal static int Style;   // (the kind of weapon on the arena's steel: Kit.Styles; it changes if you buy another kind)
        internal static bool Daily;
        private static bool _stowed;
        private static string _look = "";
        private static readonly List<string> Trophies = new List<string>();

        private struct Item { public string Prefab, Name; public int Level; public bool Champion; }
        private sealed class Foe { public GameObject Go; public Character Char; public bool Champion, Counted; public string Prefab; public Vector3 LastPos; public int Level; }

        private static readonly List<Foe> Foes = new List<Foe>();
        private static readonly Queue<Item> Queue = new Queue<Item>();
        private const float ReadySeconds = 90f;
        private static int _nextPen;
        private static float _spawnedAt, _timer, _spawnTimer, _lastKill, _outside, _closeAt, _yieldAt, _roundLeft, _lastWarn, _lastParryShout, _peak;
        private static float _purseCoins, _victoryAt, _offFloor, _nextChant;
        private static bool _backingOut;
        private static float _nextWear;   // (leaving before the first round has begun: the fee and the stake come back)

        /// <summary>Before any fighter has come out (walking in, or arming): leaving then is backing out, not giving up.</summary>
        internal static bool BeforeTheFight => Phase == PhaseKind.Ready || Phase == PhaseKind.Arming && Round == 0;
        private static int _victoryStep;
        private static bool _crowned;
        private static string _held = "";   // (the prize just won, in words: the Arena Master holds it)
        private static readonly Dictionary<string, float> Mats = new Dictionary<string, float>();
        private static int _combo, _kills, _count;
        private static int _cleared, _championLevel;   // (the last round cleared; the stars of the last champion sent in, 1 for none)
        private static bool _knockedOut, _hurtThisRound;

        // where a death in the ring happened, for the tombstone (see Patches)
        private static float _tombUntil;
        private static Vector3 _tombCentre;
        private static float _tombRadius;

        internal static bool Active => Phase != PhaseKind.None;
        internal static bool Fighting => Phase == PhaseKind.Spawning || Phase == PhaseKind.Fighting;
        internal static bool Waiting => Phase == PhaseKind.Ready;

        /// <summary>The floor between lands and at a win: the platform in the middle, where the chests stand.</summary>
        internal const string Stage = "Platform";

        /// <summary>Cover for a fight: one of the sets (not the platform: that is for between lands), or now and then the bare floor.</summary>
        private static string FightCover()
        {
            string[] sets = Layout.Props.Keys.Where(k => k != Stage).ToArray();
            return sets.Length > 0 && UnityEngine.Random.value < 0.85f ? sets[UnityEngine.Random.Range(0, sets.Length)] : null;
        }

        /// <summary>The clock on the screen between the fights, and what it is for (seconds below zero: none shown).</summary>
        internal static string TimerLabel(out float seconds)
        {
            string chant = Favours.DemandLabel(out seconds);
            if (seconds >= 0f) return chant;
            seconds = _timer;
            // (what comes next and what to do now; the keys to start now or leave are the HUD's, beside it)
            switch (Phase)
            {
                case PhaseKind.Ready: return T("arena_walk_in");
                case PhaseKind.Arming: return Show.ChestEmptied ? T("arena_timer_arming_done") : T("arena_hud_arming");
                case PhaseKind.Break:
                    string what = Lent ? T(Show.ArmourerStanding ? "arena_timer_break_armourer" : Show.ChestStanding ? "arena_timer_break_reward" : "arena_timer_break_coins") : "";
                    return T("arena_timer_next", NextRound()) + (what.Length > 0 ? "\n<color=#bdb7a9>" + what + "</color>" : "");
                default: seconds = -1f; return "";
            }
        }

        /// <summary>The next round, in words: "Black Forest, round 1 of 3", "Wave 6", "Round 2 of 3".</summary>
        private static string NextRound()
        {
            int next = Round + 1;
            if (Kind == KindOf.Road)
            {
                int land = Mathf.Min((next - 1) / 3, Roster.TierNames.Length - 1), step = (next - 1) % 3 + 1;
                return T("arena_hud_land_round", Roster.LandName(land), step) + (step == 3 ? "  ·  " + T("arena_timer_champion") : "");
            }
            if (Kind == KindOf.Endless) return T("arena_wave", next) + (next % 5 == 0 ? "  ·  " + T("arena_timer_champion") : "");
            return T("arena_round_of", next, Rounds) + (Rounds > 0 && next == Rounds && Lands ? "  ·  " + T("arena_timer_champion") : "");
        }
        internal static int FoesLeft { get { int n = Queue.Count; foreach (Foe f in Foes) if (!f.Counted) n++; return n; } }
        private static bool AllDown() { foreach (Foe f in Foes) if (!f.Counted) return false; return true; }
        private static int Out() { int n = 0; foreach (Foe f in Foes) if (!f.Counted) n++; return n; }

        /// <summary>
        /// How many fighters may be on the floor at once: two on the arena's steel (three with Hard), three in a Champion Bout, and in the
        /// Endless Horde three, rising a wave in five to five. The rest wait behind their grates for one to fall.
        /// </summary>
        private static int MaxOut => Lands ? (Rules.Hard ? 3 : 2) : Kind == KindOf.Endless ? Mathf.Min(5, 3 + Round / 8) : 3;

        /// <summary>
        /// The stars of one fighter of the Endless Horde's nth wave, by chance: a first one from wave 4 (two in five by wave 8, every one by
        /// wave 16), a second from wave 11 (every one by wave 25).
        /// </summary>
        private static int StarsFor(int wave)
        {
            int stars = UnityEngine.Random.value < Mathf.Clamp01((wave - 3) * 0.08f) ? 1 : 0;
            if (stars == 1 && UnityEngine.Random.value < Mathf.Clamp01((wave - 10) * 0.07f)) stars = 2;
            return stars;
        }
        internal static Vector3 Centre => Site.Centre;
        internal static int Kills => _kills;
        internal static float LastKillTime => _lastKill;

        /// <summary>A live foe within this far of a point.</summary>
        internal static bool FoeWithin(Vector3 at, float range) => Foes.Any(f => !f.Counted && f.Go != null && (f.Go.transform.position - at).sqrMagnitude < range * range);
        internal static float RoundLeft => _roundLeft;
        internal static bool Endless => Kind == KindOf.Endless;
        internal static int PurseCoins => Mathf.RoundToInt(_purseCoins);
        /// <summary>Fought with the arena's steel (your own things held by the Arena Master until it is over).</summary>
        internal static bool Lent => Kind == KindOf.Road || Kind == KindOf.Trial;
        internal static bool Lands => Kind == KindOf.Road || Kind == KindOf.Trial;
        /// <summary>The round within the land (1 to 3) on the Long Road or in a trial.</summary>
        internal static int LandRound => (Mathf.Max(1, Round) - 1) % 3 + 1;

        internal static string KindName(KindOf kind) => T(KindKey(kind));
        internal static string KindKey(KindOf kind) => "arena_kind_" + KindWord(kind);
        /// <summary>The contest's word, as the Hall keeps it ("road", "champion"...).</summary>
        internal static string KindWord(KindOf kind) => kind == KindOf.Road ? "road" : kind == KindOf.Champion ? "champion" : kind == KindOf.Endless ? "endless" : "trial";

        /// <summary>What it costs to enter (nothing for a character's first fight).</summary>
        internal static int FeeFor(KindOf kind, int tier)
        {
            if (Ladder.Get("fights") == 0) return 0;
            // about what the first land (or a win at the lowest) pays back, so a contest is a gamble: on the Long Road you must get into the Black Forest to come out ahead
            float fee = kind == KindOf.Road ? 150f : kind == KindOf.Trial ? 60f + 30f * tier : kind == KindOf.Champion ? 50f + 40f * tier : 40f + 30f * tier;
            return Mathf.RoundToInt(fee * Plugin.EntryFee.Value / 100f);
        }

        // ---- today's challenge -------------------------------------------------------------------------------------------------

        /// <summary>The trial picked for today (the same all day): its land, weapon and rules.</summary>
        internal static void TodaysPlan(out int tier, out int style, out bool fists, out bool noFood, out bool hard, out bool timed)
        {
            var rng = new System.Random(Ladder.Today * 31 + 7);
            style = rng.Next(Kit.Styles.Length);
            int stage = Roster.Beaten();   // (the lands this world has reached: not opened by AllTiers)
            tier = Mathf.Max(0, stage - rng.Next(Mathf.Min(2, stage + 1)));
            // one or two of bare fists, hard and the clock (never no food: on the arena's gear that leaves you 25 health, and nobody lives)
            bool[] rules = new bool[3];
            int count = 1 + rng.Next(2);
            for (int n = 0; n < count; n++) rules[rng.Next(3)] = true;
            fists = rules[0]; noFood = false; hard = rules[1]; timed = rules[2];
        }

        // ---- starting ------------------------------------------------------------------------------------------------------

        /// <summary>Begins a contest. Returns why not, or null when it has begun.</summary>
        internal static string Start(KindOf kind, int tier, int style, bool fists, bool noFood, bool hard, bool timed, int stake, bool daily)
        {
            Player player = Player.m_localPlayer;
            if (player == null || !Site.Known || !Scenery.Built) return T("arena_not_ready");
            if (Active) return T("arena_contest_on");
            if (Duel.Active) return T("arena_in_duel");
            if (Net.RemoteFight) return T("arena_busy_wait", Net.BusyText);
            if (kind == KindOf.Road) tier = 0;
            tier = Mathf.Clamp(tier, 0, Roster.TierNames.Length - 1);
            if (Roster.Pool(tier).Count == 0) return T("arena_no_one");
            if (kind == KindOf.Endless || kind == KindOf.Road) stake = 0;
            bool lent = kind == KindOf.Road || kind == KindOf.Trial;
            if (lent && Kit.Stowed(player)) return T("arena_still_held");
            Inventory inv = player.GetInventory();
            int fee = FeeFor(kind, tier);
            if (inv.CountItems("$item_coins") < fee + stake) return stake > 0 ? T("arena_no_coins_stake", fee, stake) : T("arena_no_coins", fee);

            if (fee + stake > 0) inv.RemoveItem("$item_coins", fee + stake);
            Stake = stake; Fee = fee; Daily = daily;
            Kind = kind; Tier = tier;
            Style = style < 0 ? UnityEngine.Random.Range(0, Kit.Styles.Length) : Mathf.Clamp(style, 0, Kit.Styles.Length - 1);   // (below 0: the armourer's pick)
            Rules.Set(fists, noFood, hard, timed);
            Rounds = kind == KindOf.Road ? 3 * Roster.TierNames.Length : kind == KindOf.Trial ? 3 : kind == KindOf.Champion ? 1 : 0;
            Title = kind == KindOf.Road ? "The Long Road" : kind + " - " + Roster.TierNames[Tier];
            TitleLine = kind == KindOf.Road ? Words.Line(KindKey(kind)) : Words.Line("arena_title", Words.Word(KindKey(kind)), Words.Land(Tier));
            Round = 0; _kills = 0; _combo = 0; _outside = 0f; _knockedOut = false; _closeAt = 0f; _yieldAt = 0f; _cleared = 0; _championLevel = 0;
            _purseCoins = 0f; _peak = 0f; _stowed = false; _look = "";
            Mats.Clear(); Trophies.Clear();
            Foes.Clear(); Queue.Clear();

            // the floor is dressed for the fight, the crowd comes in, and the grate rises: walk in
            Net.Props(FightCover());
            Show.ClearFloor();   // (the last fight's leavings)
            Crowd.Open();
            Favours.Reset();
            Net.Gate(Scenery.MainGrate, ReadySeconds);
            Net.Door(ReadySeconds);   // (the main gate opens for you)
            Net.Shout(Words.Line("arena_call_to_ring"), Words.Line("arena_call_to_ring_sub", player.GetPlayerName()), 6f,
                      Words.Line("arena_call_to_ring"), Words.Line("arena_watch_to_ring_sub", player.GetPlayerName()));
            Phase = PhaseKind.Ready;
            _timer = ReadySeconds; _count = 4;
            if (lent) Hud.Say(T("arena_say_lent"));
            Plugin.Log.LogInfo($"Contest started: {Title}, rules {Rules.Names()}, fee {fee}, stake {stake}" + (lent ? $", {Kit.Styles[Style]}" : ""));
            return null;
        }

        // ---- every frame -----------------------------------------------------------------------------------------------------

        internal static void Tick(float dt)
        {
            if (_closeAt > 0f && Time.time >= _closeAt)
            {
                // the crowd stays until you have walked out of the ring (or a while longer)
                Player p = Player.m_localPlayer;
                if (p == null || !Site.OnFloor(p.transform.position, 2f) || Time.time > _closeAt + 45f)
                { _closeAt = 0f; if (!Duel.Active && !Net.RemoteFight) Crowd.Close(true); Net.Props(null); }
            }
            if (!Active) return;
            Player player = Player.m_localPlayer;
            if (player == null) { Abort("You left the world."); return; }
            if (Phase == PhaseKind.Victory) { TickVictory(player, dt); return; }
            if (player.IsDead()) { End(Outcome.Died); return; }
            if (_knockedOut) { End(Outcome.Carried); return; }
            _peak = Mathf.Max(_peak, Crowd.Favour);
            if (Lent && _stowed && Time.time > _nextWear) { _nextWear = Time.time + 0.25f; Armoury.WearNew(player, Style, Rules.Fists); }

            // giving up: twice, so a stray key does not end it. Between rounds it takes the purse; in a round it is half.
            if (Plugin.YieldKey.Value.IsDown() && !Window.IsOpen)
            {
                bool between = Phase == PhaseKind.Break || Phase == PhaseKind.Arming;
                if (_yieldAt > 0f && Time.time - _yieldAt < 3f)
                {
                    if (BeforeTheFight) { _backingOut = true; End(Outcome.Aborted); } else End(between ? Outcome.CashOut : Outcome.Yield);
                    return;
                }
                _yieldAt = Time.time;
                Hud.Say(T(BeforeTheFight ? "arena_say_back_out" : between ? (Lent ? "arena_say_yield_coins" : "arena_say_yield_purse") : "arena_say_yield_half"));
            }
            if (Plugin.ReadyKey.Value.MainKey != KeyCode.None && Plugin.ReadyKey.Value.IsDown() && !Window.IsOpen) ReadyEarly();

            if (Phase == PhaseKind.Ready)
            {
                _timer -= dt;
                if (Site.OnFloor(player.transform.position, -1f))
                {
                    Net.Gate(Scenery.MainGrate, 0f);   // shut behind you
                    Net.Door(0f);
                    if (Lent)
                    {
                        // your things to the Arena Master; the arena's steel and meals to you
                        if (!Kit.Stow(player)) { End(Outcome.Aborted); Hud.Say(T("arena_say_stow_failed")); return; }
                        _stowed = true;
                        player.ClearFood();   // (what you ate outside went with your things: you fight on the arena's meals)
                    }
                    else Rules.Strip(player);
                    player.Heal(player.GetMaxHealth(), true);
                    player.AddStamina(player.GetMaxStamina());
                    _look = Figures.LookOf(player);
                    string intro = Words.Line("arena_call_intro", Words.Word(KindKey(Kind)));
                    Net.Sound("gong");
                    if (Lent)
                    {
                        // the armourer's chest rises in the middle of the ring: get your gear on and eat before the clock runs out
                        Show.PopChest(Show.Armoury, Kit.Outfit(Tier, Style, Rules.Fists, !Rules.NoFood));
                        string welcome = Lines.Welcome(player.GetPlayerName());
                        Net.Shout(intro, Words.Then(welcome, Words.Line("arena_call_arm")), 5f, intro, welcome);
                        Phase = PhaseKind.Arming;
                        _timer = 30f;
                    }
                    else
                    {
                        string rules = Words.Rules(Rules.Fists, Rules.NoFood, Rules.Hard, Rules.Timed);
                        Net.Shout(intro, Words.Then(Lines.Welcome(player.GetPlayerName()), Stake > 0 ? Words.Line("arena_call_rules_stake", rules, Stake) : Words.Line("arena_call_rules", rules)), 5f);
                        Phase = PhaseKind.Countdown;
                        _timer = 5f; _count = 4;
                    }
                }
                else if (_timer <= 0f) { End(Outcome.Aborted); Hud.Say(T("arena_say_no_show")); }
                return;
            }

            // staying in the ring
            if (!Site.OnFloor(player.transform.position, 1.5f))
            {
                _outside += dt;
                if (Time.time - _lastWarn > 1f) { _lastWarn = Time.time; Hud.Say(T("arena_say_get_back", Mathf.CeilToInt(8f - _outside))); }
                if (_outside > 8f) { End(Outcome.LeftRing); return; }
            }
            else _outside = Mathf.Max(0f, _outside - dt * 2f);

            switch (Phase)
            {
                case PhaseKind.Arming:
                    _timer -= dt;
                    if (Show.ChestEmptied && _timer > 6f) _timer = 6f;   // (all taken: no need to wait)
                    if (_timer <= 0f) { Show.TakeChest(); Phase = PhaseKind.Countdown; _timer = 3.5f; _count = 4; }
                    break;
                case PhaseKind.Countdown:
                    _timer -= dt;
                    int n = Mathf.CeilToInt(_timer);
                    if (n < _count && n >= 1 && n <= 3) { _count = n; Hud.Count(n.ToString()); }
                    if (_timer <= 0f) { Hud.Count(T("arena_fight")); Net.Sound("cheer"); BeginRound(1); }
                    break;
                case PhaseKind.Spawning:
                case PhaseKind.Fighting:
                    if (Phase == PhaseKind.Spawning)
                    {
                        _spawnTimer -= dt;
                        // (the next comes out only while fewer than MaxOut are on the floor; when one falls, the next a moment later)
                        if (_spawnTimer <= 0f && Queue.Count > 0)
                        {
                            if (Out() < MaxOut) { Spawn(Queue.Dequeue(), player); _spawnTimer = 1.1f; }
                            else _spawnTimer = 1.5f;
                        }
                        if (Queue.Count == 0) Phase = PhaseKind.Fighting;
                    }
                    CheckFoes(player);
                    if (Rules.Timed)
                    {
                        float before = _roundLeft;
                        _roundLeft -= dt;
                        if (before > 10f && _roundLeft <= 10f) Net.Shout(Words.Line("arena_call_ten_seconds"), "", 1.5f);
                        if (_roundLeft <= 0f) { End(Outcome.TimeUp); return; }
                    }
                    if (Phase == PhaseKind.Fighting && Queue.Count == 0 && AllDown()) RoundCleared(player);
                    break;
                case PhaseKind.Break:
                    _timer -= dt;
                    if (Show.ChestEmptied && !Show.ArmourerStanding && _timer > 5f) _timer = 5f;   // (its chest emptied, and no Armourer to see: on)
                    if (_timer <= 0f)
                    {
                        Show.TakeChest();
                        Show.Armourer(false);
                        if (Lent) Armoury.Tidy(player, Style);   // (outgrown pieces back to the armourer)
                        BeginRound(Round + 1);
                    }
                    break;
            }
        }

        // ---- rounds ------------------------------------------------------------------------------------------------------------

        private static void BeginRound(int n)
        {
            Round = n;
            Queue.Clear();
            Foes.Clear();
            _hurtThisRound = false;
            bool champion;
            int fodder, level;
            float lean;
            int step = (n - 1) % 3;
            if (Kind == KindOf.Road && step == 0 && n > 1) { Show.TakeChest(); Show.ClearGifts(); Net.Props(FightCover()); }   // (a new land: its own cover, the stage taken down, the last land's leftovers gone)
            if (Lands)
            {
                // three rounds a land, its champion in the third; on the Long Road each three takes you to the next land. Two fighters, then
                // three, then the champion with one at its side (Hard: one more each time), and never more than two out at once (MaxOut)
                if (Kind == KindOf.Road) Tier = Mathf.Min((n - 1) / 3, Roster.TierNames.Length - 1);
                champion = step == 2;
                fodder = (champion ? 1 : 2 + step) + (Rules.Hard ? 1 : 0);
                lean = step / 2f;
                level = 1 + (Rules.Hard ? 1 : 0);
            }
            else if (Kind == KindOf.Champion)
            {
                champion = true;
                fodder = Rules.Hard ? 2 : 0;
                lean = 0.5f;
                level = 1 + (Rules.Hard ? 1 : 0);
            }
            else
            {
                // the Endless Horde: one more fighter every second wave (to ten), a champion every fifth with two fewer at its side, and stars
                // creeping in (StarsFor), never a wall where the whole wave turns starred at once (it was at waves 6 and 11)
                champion = n % 5 == 0;
                fodder = Mathf.Min(2 + (n + 1) / 2, 10) + (Rules.Hard ? 1 : 0) - (champion ? 2 : 0);
                lean = Mathf.Min(1f, n / 8f);
                level = 1 + (Rules.Hard ? 1 : 0);
            }
            for (int i = 0; i < fodder; i++)
            {
                string prefab = Roster.Pick(Tier, lean);
                if (prefab == null) continue;
                // (on the arena's steel a star now and then only at a land's champion's side, and not before the Swamp; in the Horde, StarsFor)
                int lv = level + (Lands ? (champion && Tier >= 2 && UnityEngine.Random.value < 0.25f ? 1 : 0) : Kind == KindOf.Endless ? StarsFor(n) : 0);
                Queue.Enqueue(new Item { Prefab = prefab, Level = Mathf.Clamp(lv, 1, 3) });
            }
            if (champion)
            {
                string prefab = Roster.Champion(Tier);
                // (its stars by what its land's gear can take: Roster.ChampionLevel)
                if (prefab != null)
                {
                    _championLevel = Roster.ChampionLevel(Tier, prefab, Kind, n, Rules.Hard);
                    Queue.Enqueue(new Item { Prefab = prefab, Name = Roster.ChampionName(), Level = _championLevel, Champion = true });
                }
            }
            if (Queue.Count == 0) { End(Outcome.CalledOff); return; }   // (not your doing: paid as if you had taken your purse)
            // (time for how many there are: they come out a few at a time now, so a big wave needs longer than a small one; three as before)
            _roundLeft = Rules.RoundSeconds + 15f * Mathf.Max(0, Queue.Count - 3) + (champion ? 30f : 0f);
            Phase = PhaseKind.Spawning;
            _spawnTimer = 1.4f;
            string land = Words.Land(Tier);
            string head = Kind == KindOf.Champion ? Words.Line("arena_call_the_champion") : Kind == KindOf.Endless ? Words.Line("arena_call_wave", n)
                        : champion ? (Kind == KindOf.Road ? Words.Line("arena_call_champion_of", land) : Words.Line("arena_call_final_round"))
                        : (Kind == KindOf.Road ? Words.Line("arena_call_land_round", land, step + 1) : Words.Line("arena_call_round", n));
            string sub = champion && Kind != KindOf.Champion ? Words.Line("arena_call_champion_comes") : Lines.RoundCall(Queue.Count);
            Net.Shout(head, sub, 3.5f);
            Net.Sound("gong");
            if (n > 1) Net.Sound("cheer");
        }

        private static void RoundCleared(Player player)
        {
            int step = (Round - 1) % 3;
            _cleared = Round;
            float units = Lands ? 0.7f + 0.4f * step : Kind == KindOf.Endless ? 0.5f + 0.15f * Round : 3f;
            if (!_hurtThisRound) { Crowd.Gain(12f, false); Net.Shout(Words.Line("arena_call_flawless"), Words.Line("arena_call_flawless_sub"), 2.2f, Words.Line("arena_call_flawless"), Words.Line("arena_watch_flawless_sub", player.GetPlayerName())); }
            if (!Lent) AddToPurse(units);   // (on the arena's steel the fighters drop coins instead)
            if (Kind == KindOf.Endless) Ladder.Best("bestwave" + Tier, Round);
            else if (Kind == KindOf.Road) Ladder.Best("road", Round);
            else Ladder.Best("bestround" + Tier, Round);
            _look = Figures.LookOf(player);
            if (Rounds > 0 && Round >= Rounds) { StartVictory(player); return; }
            Crowd.Gain(8f, false);
            Net.Sound("applause");
            Phase = PhaseKind.Break;
            _timer = 12f;
            player.AddStamina(player.GetMaxStamina());
            if (Kind == KindOf.Road && step == 2)
            {
                // a land beaten: on to the next, with new cover on the floor; you keep your gear, and upgrade it as you can
                int next = Mathf.Min(Tier + 1, Roster.TierNames.Length - 1);
                player.Heal(player.GetMaxHealth(), true);
                // between lands the floor is always the same: the platform in the middle, the reward chest on it (the next land's own cover
                // goes up when its first round begins)
                Net.Props(Stage);
                if (Lent)
                {
                    // between lands on the Long Road: a chest with one upgrade (and a leftover meal), and the Armourer by the gate
                    var reward = new List<(string Prefab, int Amount, int Quality, string Loan)>();
                    var up = Armoury.Random(player, Style, Rules.Fists, Tier.ToString());
                    if (up != null) reward.Add(up.Value);
                    else reward.Add(("Coins", Mathf.RoundToInt(60f * (Tier + 1) * Plugin.Rewards.Value / 100f), 1, null));   // (nothing left to raise: coins instead)
                    // (a fresh meal of the next land's every time, and now and then a mead: the rest of the food is the Armourer's to sell)
                    if (!Rules.NoFood)
                    {
                        foreach (string meal in Kit.Plate(Tier + 1, new[] { Kit.Role.Health }).Take(1)) reward.Add((meal, 1, 1, Tier.ToString()));
                        if (UnityEngine.Random.value < 0.3f) reward.Add((Kit.Meads[Mathf.Clamp(Tier + 1, 0, Kit.Meads.Length - 1)], 1, 1, Tier.ToString()));
                    }
                    if (reward.Count > 0) Show.PopChest(Show.Reward, reward);
                    Show.Armourer(true);
                }
                Net.Sound("roar");
                Net.Shout(Words.Line("arena_call_land_beaten", Words.Land(Tier)),
                          Words.Then(Words.Line("arena_call_on_to", Words.Land(next)), Words.Dash, Words.Line("arena_call_leave_coins", Plugin.YieldKey.Value.ToString())), 7f,
                          Words.Line("arena_call_land_beaten", Words.Land(Tier)), Words.Line("arena_watch_on_to", Words.Land(next)));
                _timer = 50f;
                return;
            }
            if (Lent)
            {
                // after a land's first and second rounds: a chest with one piece raised to this land's (whatever you have not yet got), so
                // by its champion you are dressed for the land; nothing left to raise, a few coins instead
                if (Kind == KindOf.Road)
                {
                    var catchUp = Armoury.Random(player, Style, Rules.Fists, Tier.ToString(), false);
                    Show.PopChest(Show.Reward, new[] { catchUp ?? ("Coins", Mathf.RoundToInt(30f * (Tier + 1) * Plugin.Rewards.Value / 100f), 1, (string)null) });
                }
                Net.Shout(Words.Line("arena_call_round_cleared", Round), Words.Then(Words.Line("arena_call_pick_up_coins"), Words.Dash, Words.Line("arena_call_leave_them", Plugin.YieldKey.Value.ToString())), 4.5f,
                          Words.Line("arena_call_round_cleared", Round), "");
                player.Heal(player.GetMaxHealth() * 0.3f, true);
                return;
            }
            string cleared = Words.Line(Kind == KindOf.Endless ? "arena_call_wave_cleared" : "arena_call_round_cleared", Round);
            Net.Shout(cleared, Words.Then(Words.Line("arena_call_your_purse", Words.Purse(PurseData())), Words.Dash, Words.Line("arena_call_leave_it", Plugin.YieldKey.Value.ToString())), 4.5f,
                      cleared, Words.Line("arena_watch_purse", player.GetPlayerName(), Words.Purse(PurseData())));
            player.Heal(player.GetMaxHealth() * 0.3f, true);
        }

        private static void AddToPurse(float units)
        {
            // (half what it was: coins only pile up; the value is in the land's spoils, paid at the end: Spoils)
            float mult = Rules.Multiplier * (1f + Crowd.Favour / 200f) * Plugin.Rewards.Value / 100f;
            _purseCoins += (10 + 15 * Tier) * units * mult;
        }

        internal static string PurseText()
        {
            if (Lent) return T("arena_purse_bag", Player.m_localPlayer != null ? Player.m_localPlayer.GetInventory().CountItems("$item_coins") : 0);
            return PurseText(PurseData());
        }

        /// <summary>The purse as data, "coins;Prefab*n,...", for the watchers to put into words (PurseText).</summary>
        private static string PurseData() =>
            PurseCoins.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";"
            + string.Join(",", Mats.Where(m => Mathf.FloorToInt(m.Value) > 0).Select(m => m.Key + "*" + Mathf.FloorToInt(m.Value).ToString(System.Globalization.CultureInfo.InvariantCulture)));

        internal static string PurseText(string data)
        {
            string[] f = data.Split(';');
            string s = T("arena_coins", f[0]);
            if (f.Length > 1) foreach (var m in Counted(f[1])) s += ", " + T("arena_prize_item", m.Amount, ItemName(m.Prefab));
            return s;
        }

        /// <summary>"Prefab*n,Prefab" (no count: one).</summary>
        private static IEnumerable<(string Prefab, int Amount)> Counted(string list)
        {
            foreach (string part in list.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int star = part.IndexOf('*');
                if (star < 0) { yield return (part, 1); continue; }
                int.TryParse(part.Substring(star + 1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n);
                yield return (part.Substring(0, star), n);
            }
        }

        internal static string ItemName(string prefab)
        {
            ItemDrop drop = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab)?.GetComponent<ItemDrop>() : null;
            return drop != null && Localization.instance != null ? Localization.instance.Localize(drop.m_itemData.m_shared.m_name) : prefab;
        }

        // ---- the fighters --------------------------------------------------------------------------------------------------------

        private static readonly System.Reflection.MethodInfo SetTarget = HarmonyLib.AccessTools.Method(typeof(MonsterAI), "SetTarget", new[] { typeof(Character) });

        private static void Spawn(Item item, Player player)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(item.Prefab) : null;
            if (prefab == null) return;
            Vector3 at = Site.Centre;
            if (Layout.Pens.Count > 0)
            {
                // the next pen round the ring (not the one nearest you), its grate rising
                Layout.Mark pen = null;
                for (int k = 0; k < Layout.Pens.Count; k++)
                {
                    Layout.Mark p = Layout.Pens[(_nextPen + k) % Layout.Pens.Count];
                    if ((Site.Point(p) - player.transform.position).magnitude > 8f || k == Layout.Pens.Count - 1) { pen = p; _nextPen = (_nextPen + k + 1) % Layout.Pens.Count; break; }
                }
                at = Site.Point(pen, 0.2f);
                Net.Gate(pen.Gate, 4f);
            }
            GameObject go = Object.Instantiate(prefab, at, Quaternion.LookRotation(new Vector3(Site.Centre.x - at.x, 0f, Site.Centre.z - at.z)));
            var foe = new Foe { Go = go, Char = go.GetComponent<Character>(), Champion = item.Champion, Prefab = item.Prefab, LastPos = at, Level = item.Level };
            if (foe.Char != null)
            {
                foe.Char.SetLevel(item.Level);
                if (!string.IsNullOrEmpty(item.Name)) foe.Char.m_name = Words.Show(item.Name);
            }
            go.GetComponent<CharacterDrop>()?.SetDropsEnabled(false);          // the prize is the arena's, not the creature's
            MonsterAI ai = go.GetComponent<MonsterAI>();
            if (ai != null)
            {
                // a fighter of the arena: never runs (not from fire, not when hurt, not when it cannot reach you), and comes for you at once
                ai.SetDespawnInDay(false); ai.SetHuntPlayer(true);
                ai.m_afraidOfFire = false; ai.m_avoidFire = false;
                ai.m_fleeIfLowHealth = 0f; ai.m_fleeIfHurtWhenTargetCantBeReached = false; ai.m_fleeIfNotAlerted = false;
                ai.Alert();
                SetTarget?.Invoke(ai, new object[] { player });
            }
            else go.GetComponent<BaseAI>()?.SetHuntPlayer(true);
            ZNetView view = go.GetComponent<ZNetView>();
            if (view != null && view.IsValid())
            {
                view.GetZDO().Set("dh_arena", true);
                if (item.Champion) view.SetLocalScale(Vector3.one * 1.18f);   // a champion stands a head taller
            }
            Foes.Add(foe);
            _spawnedAt = Time.time;
            if (item.Champion)
            {
                // the champion's entrance: a fanfare, a burst of fire at its gate, and the crowd on its feet
                Net.Shout(item.Name, Words.Line(Roster.Origin(Tier)), 4f);
                Net.Sound("horn"); Net.Sound("roar");
                Net.Effect("fx_fireball_staff_explosion", at + Vector3.up * 1.2f);
                Net.Effect("fx_fireskeleton_nova", at);
            }
        }

        private static void CheckFoes(Player player)
        {
            foreach (Foe f in Foes)
            {
                if (f.Counted) continue;
                // (a creature that dies is taken out of the world at once, often before this sees it dead: gone is killed)
                if (f.Go == null || f.Char == null || f.Char.IsDead())
                {
                    f.Counted = true;
                    OnKill(f, player);
                    continue;
                }
                f.LastPos = f.Go.transform.position;
                // one that wandered far from the ring is put back at its edge
                if (!Site.OnFloor(f.Go.transform.position, 6f) && Time.time - _spawnedAt > 8f)
                {
                    Vector3 back = Site.World(new Vector3(0f, 0f, 0f)) + (f.Go.transform.position - Site.Centre).normalized * (Layout.Floor - 3f);
                    back.y = Site.Ground(back, Site.Centre.y) + 0.3f;
                    f.Go.transform.position = back;
                }
            }
        }

        private static void OnKill(Foe f, Player player)
        {
            _kills++;
            _combo = Time.time - _lastKill < 4f ? _combo + 1 : 1;
            _lastKill = Time.time;
            Favours.OnKill();
            bool close = player.GetHealth() < player.GetMaxHealth() * 0.25f;
            Crowd.Gain((f.Champion ? 30f : 4f + _combo * 3f) + (close ? 8f : 0f), true);
            if (Lent)
            {
                // on the arena's steel the fighters drop coins (a champion a pile, with its trophy and the land's metal)
                Armoury.Spill(f.LastPos, Tier, f.Level, f.Champion);
                if (f.Champion && Tier <= Roster.Beaten())
                {
                    // (its trophy; the land's metal is in its spoils now, paid when you walk out: Spoils)
                    string t = Roster.Trophy(f.Prefab);
                    if (t != null) Armoury.Drop(t, 1, f.LastPos);
                }
            }
            if (f.Champion)
            {
                string trophy = !Lent && Tier <= Roster.Beaten() ? Roster.Trophy(f.Prefab) : null;
                if (trophy != null) Trophies.Add(trophy);
                Net.Shout(Words.Line("arena_call_champion_falls"), Lines.ChampionDown(), 3f);
                Net.Sound("roar");
            }
            else if (close) Net.Shout(Words.Line("arena_call_close"), Words.Line("arena_call_close_sub"), 1.8f);
            else if (_combo == 2) Net.Shout(Words.Line("arena_call_double"), "", 1.5f);
            else if (_combo == 3) Net.Shout(Words.Line("arena_call_triple"), "", 1.5f);
            else if (_combo >= 5) Net.Shout(Words.Line("arena_call_unstoppable"), Words.Line("arena_call_in_a_row", _combo), 1.8f);
        }

        /// <summary>The local player blocked a blow in a contest (a perfect parry is worth more to the crowd).</summary>
        internal static void OnBlock(bool perfect)
        {
            if (!Fighting) return;
            Favours.OnParry(perfect);
            Crowd.Gain(perfect ? 6f : 1f, perfect);
            if (perfect && Time.time - _lastParryShout > 4f) { _lastParryShout = Time.time; Net.Shout(Words.Line("arena_call_parry"), "", 1.2f); }
        }

        /// <summary>The local player was hurt in a contest: the crowd likes it less, and the round is not flawless.</summary>
        internal static void OnHurt(float fraction)
        {
            if (!Fighting || fraction <= 0f) return;
            _hurtThisRound = true;
            Favours.OnHurt();
            Crowd.Hurt(Mathf.Min(3f, fraction * 15f));
        }

        // ---- the end ----------------------------------------------------------------------------------------------------------------

        /// <summary>The local player's health reached nothing with dying in the ring switched off: they are carried out (the health patch calls this).</summary>
        internal static void Knockout() => _knockedOut = true;

        /// <summary>The local player is dying in the ring: remember the ring, so the tombstone can be carried out of it.</summary>
        internal static void NoteDeath()
        {
            if (!Active) return;
            _tombUntil = Time.time + 5f;
            _tombCentre = Site.Centre;
            _tombRadius = Layout.Floor;
        }

        internal static bool TombPending => Time.time < _tombUntil;

        /// <summary>Where the tombstone goes: just outside the ring, on the side where you fell.</summary>
        internal static Vector3 TombSpot(Vector3 died)
        {
            _tombUntil = 0f;
            // in the forecourt, beside the Hall of Fame, where you can walk in and pick it up
            Vector3 spot = Site.World(Layout.Arrival.Pos + new Vector3(UnityEngine.Random.Range(-4f, 4f), 0f, 5f));
            spot.y = Site.Ground(spot, Site.Origin.y) + 0.6f;
            return spot;
        }

        private static void ClearFoes()
        {
            foreach (Foe f in Foes)
            {
                if (f.Go == null || f.Counted) continue;
                ZNetView view = f.Go.GetComponent<ZNetView>();
                if (view != null && view.IsValid()) view.Destroy(); else Object.Destroy(f.Go);
            }
            Foes.Clear(); Queue.Clear();
        }

        /// <summary>
        /// Ends the contest without a result (the mod unloaded, the player logged out). Before the fight began the fee and the stake come back;
        /// once it has begun it counts as giving up (half the purse, the stake lost), so leaving the game never pays better than losing.
        /// </summary>
        internal static void Abort(string why)
        {
            if (!Active) return;
            if (Phase == PhaseKind.Victory) { FinishVictory(); return; }
            End(Outcome.Aborted);
        }

        // ---- the victory ---------------------------------------------------------------------------------------------------------

        /// <summary>
        /// The whole contest won: the crowd on its feet, fireworks over the stands, your name called, and the main gate opening for you to walk
        /// out when you are ready (or after a while). The prize is the Arena Master's to hand over, at his desk in the forecourt: it is held
        /// for you from this moment (Prize), so nothing that happens during the show loses it.
        /// </summary>
        private static void StartVictory(Player player)
        {
            Phase = PhaseKind.Victory;
            _victoryAt = Time.time; _victoryStep = 0; _offFloor = 0f; _nextChant = Time.time + 5f;
            _timer = 90f;
            ClearFoes();
            Crowd.Set(100f);
            Pay(player, Outcome.Win, false, Sweep());
            Net.Sound("roar"); Net.Sound("horn"); Net.Sound("applause");
            Net.Props(Stage);   // (the platform rises in the middle, a stage for the winner)
            Net.Fireworks(18, 12f);
            Net.Celebrate();
        }

        private static void TickVictory(Player player, float dt)
        {
            _timer -= dt;
            float t = Time.time - _victoryAt;
            string name = player.GetPlayerName();
            if (_victoryStep == 0 && t > 4f)
            {
                _victoryStep = 1;
                string head = _crowned ? Words.Line("arena_call_crowned_" + KindWord(Kind)) : Words.Line("arena_call_triumphs", name);
                Net.Shout(head, Words.Line(_crowned ? "arena_call_chants_name_statue" : "arena_call_chants_name", name), 5f,
                          head, Words.Line(_crowned ? "arena_watch_chants_name_statue" : "arena_watch_chants_name", name));
                Net.Sound("horn");
            }
            if (_victoryStep == 1 && t > 7f)
            {
                _victoryStep = 2;
                Net.Sound("roar");
                if (_held.Length > 0) Hud.Say(T("arena_prize_waits"));
            }
            if (_victoryStep == 2 && t > 11f)
            {
                _victoryStep = 3;
                Net.Gate(Scenery.MainGrate, 120f);
                Net.Door(120f);
                Net.Shout(Words.Line("arena_call_take_a_bow"), Words.Line("arena_call_take_a_bow_sub"), 3f, null, null);   // (for the winner alone)
                Net.Fireworks(12, 9f);
            }
            if (Time.time > _nextChant) { _nextChant = Time.time + UnityEngine.Random.Range(7f, 10f); Net.Sound(UnityEngine.Random.value < 0.5f ? "horn" : "cheer"); }
            // done: walked out of the ring (once the gate is open), fell, or the show has gone on long enough
            if (!Site.OnFloor(player.transform.position, 2f)) _offFloor += dt; else _offFloor = 0f;
            if (player.IsDead() || _timer <= 0f || _victoryStep >= 3 && _offFloor > 1.5f) FinishVictory();
        }

        /// <summary>"Bring them on!" (the challenge emote, the ready key, Shift+E at the Armourer): the break ends now (well, in 3 seconds).</summary>
        internal static bool ReadyEarly()
        {
            if ((Phase != PhaseKind.Break && Phase != PhaseKind.Arming) || _timer <= 3f) return false;
            _timer = 3f;
            Net.Sound("roar");
            Hud.Say(T("arena_say_ready"));
            return true;
        }

        private static void FinishVictory()
        {
            Player player = Player.m_localPlayer;
            Phase = PhaseKind.None;
            Show.Armourer(false);
            Show.TakeChest();
            Show.ClearGifts();
            if (_stowed && player != null && !player.IsDead()) Kit.Return(player);
            _stowed = false;
            Rules.Restore(player);
            Rules.Clear();
            Net.Gate(Scenery.MainGrate, 30f);
            Net.Door(45f);
            Guard.Grace(60f);
            _closeAt = Time.time + 6f;
        }

        private static void End(Outcome outcome)
        {
            Player player = Player.m_localPlayer;
            bool wasReady = Phase == PhaseKind.Ready, refund = wasReady || _backingOut;
            _backingOut = false;
            Phase = PhaseKind.None;
            ClearFoes();
            Show.TakeChest();
            Show.Armourer(false);
            if (player != null && !player.IsDead() && !wasReady && _look.Length == 0) _look = Figures.LookOf(player);
            // what the fighters dropped and nobody picked up is gathered off the floor (for the prize; a fall loses it)
            var swept = refund ? new Dictionary<string, int>() : Sweep();
            if (outcome == Outcome.Died) swept.Clear();
            // (paid before your own things come back: on the arena's steel the coins in your bag are what you picked up)
            if (player != null) Pay(player, outcome, refund, swept);
            Show.ClearGifts();   // (the crowd's gifts nobody picked up: gone with the fight)
            // your own things back (if you fell, they come back when you rise: Kit.Tick)
            if (_stowed && player != null && !player.IsDead()) Kit.Return(player);
            _stowed = false;
            Rules.Restore(player);
            Rules.Clear();
            if (player != null && !player.IsDead() && outcome == Outcome.Carried) player.SetHealth(player.GetMaxHealth() * 0.35f);
            _knockedOut = false;
            Net.Gate(Scenery.MainGrate, wasReady ? 0f : 30f);
            Net.Door(wasReady ? 0f : 45f);   // (open again for you to walk out)
            Guard.Grace(60f);
            bool won = outcome == Outcome.Win || outcome == Outcome.CashOut;
            if (refund) Hud.Say(T("arena_say_backed_out"));
            else if (won) { Net.Sound("roar"); Net.Sound("horn"); Net.Sound("applause"); } else { Net.Sound("boo"); }
            _closeAt = Time.time + (won ? 8f : 5f);
        }

        // ---- the prize -------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// What the contest pays, into the Arena Master's keeping (Prize): collected at his desk, and only by this player. (The fee and stake
        /// of a fight never begun come straight back.)
        /// </summary>
        private static void Pay(Player player, Outcome outcome, bool beforeTheFight, Dictionary<string, int> swept)
        {
            _held = "";
            bool win = outcome == Outcome.Win;
            if (beforeTheFight)
            {
                // never came into the ring: the fee and the stake are back, and it does not count
                if (Fee + Stake > 0) Give("Coins", Fee + Stake, player);
                return;
            }
            Ladder.Add("kills", _kills);
            Ladder.Add("fights", 1);
            Ladder.Best("favour", Mathf.RoundToInt(_peak));
            if (outcome == Outcome.Died) Ladder.Add("deaths", 1);
            // a win: the whole contest, or on the Long Road walking out with your purse after beating a land's champion
            bool counts = win || Kind == KindOf.Road && outcome == Outcome.CashOut && Round >= 3;
            if (counts) { Ladder.Add("wins", 1); Ladder.Add(Kind == KindOf.Champion ? "champions" : Kind == KindOf.Road ? "roads" : Kind == KindOf.Trial ? "trials" : "hordes", 1); }

            // on the arena's steel the purse is the coins you picked up (you keep them unless you fell); a win adds a bonus
            int carried = player.GetInventory().CountItems("$item_coins");
            // (the bonus: on the Long Road about a land's coins more for going all the way; a trial, a third of that for its three rounds)
            if (Lent) { _purseCoins = win ? (Kind == KindOf.Road ? 125f : 40f) * (Tier + 1) * Rules.Multiplier * Plugin.Rewards.Value / 100f : 0f; Mats.Clear(); }
            // (leaving mid-fight, Aborted, is paid as giving up: never better than losing)
            float share = outcome == Outcome.Win ? 1.25f : outcome == Outcome.CashOut || outcome == Outcome.CalledOff ? 1f : outcome == Outcome.Died ? 0f : 0.5f;
            bool dailyBonus = win && Daily && !Ladder.DailyDone;
            if (dailyBonus) { share *= 1.5f; Ladder.Set("daily", Ladder.Today); }
            int coins = Mathf.RoundToInt(_purseCoins * share);
            int stakeBack = win ? Stake * 2 : outcome == Outcome.CalledOff ? Stake : 0;
            List<string> trophies = outcome != Outcome.Died ? Trophies.Distinct().ToList() : new List<string>();

            var held = new Dictionary<string, int>();
            void Hand(string prefab, int amount)
            {
                if (amount <= 0) return;
                Prize.Hold(player, prefab, amount);
                held[prefab] = (held.TryGetValue(prefab, out int had) ? had : 0) + amount;
            }
            if (coins + stakeBack > 0) Hand("Coins", coins + stakeBack);
            var things = new List<string>();   // (materials "Prefab*n", trophies "Prefab")
            foreach (var m in Mats)
            {
                int n = Mathf.FloorToInt(m.Value * share);
                if (n <= 0) continue;
                Hand(m.Key, n);
                things.Add(m.Key + "*" + n.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            foreach (string trophy in trophies) { Hand(trophy, 1); things.Add(trophy); }
            foreach (var s in swept) { Hand(s.Key, s.Value); things.Add(s.Key + "*" + s.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            if (outcome != Outcome.Died)
                foreach (var s in SpoilsFor(player, outcome, win, dailyBonus))
                {
                    Hand(s.Prefab, s.Amount);
                    things.Add(s.Prefab + "*" + s.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            _held = Prize.Text(held);
            // the prize as data, for each game to put into words (PrizeText): coins;on the arena's steel;coins picked up;stake back;won;today's bonus;things
            string data = string.Join(";", new[] { coins, Lent ? 1 : 0, Lent && outcome != Outcome.Died ? carried : 0, stakeBack, win ? 1 : 0, dailyBonus ? 1 : 0 }
                                               .Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture))) + ";" + string.Join(",", things);
            // (what was won is in the Arena Master's Prize tab: the calls just say so, a list would run off the screen)
            string prize = held.Count > 0 ? Words.Line("arena_call_prize_master") : Words.Prize(data);

            // the Hall of Champions at this arena
            // (the run itself, never the coins it paid: how far, the kills, the crowd at its loudest, a win, the rules taken; a deeper land counts
            // for more outside the Long Road, which goes through them all)
            float landWorth = Kind == KindOf.Road ? 1f : 1f + 0.25f * Tier;
            int score = Mathf.RoundToInt((Round * 100 + _kills * 10 + Mathf.RoundToInt(_peak) + (win ? 500 : 0)) * Rules.Multiplier * landWorth);
            // (the run as Words: the Hall shows it in each reader's language)
            string what = Kind == KindOf.Road ? (win ? Words.Line("arena_run_road_end") : Words.Line("arena_run_road", Words.Land(Tier), Round, Rounds))
                        : Words.Line(Kind == KindOf.Endless ? "arena_run_endless" : Kind == KindOf.Trial ? "arena_run_trial" : "arena_run_champion", Words.Land(Tier), Round);
            bool crowned = _crowned = outcome != Outcome.Aborted && outcome != Outcome.CalledOff && Ladder.Enter(KindWord(Kind), player.GetPlayerName(), what, score, _look);   // (as they fought: their statue wears it)
            string hall = crowned ? Words.Then(Words.Dash, Words.Line("arena_call_new_champion_" + KindWord(Kind))) : "";
            if (crowned) Net.Sound("roar");
            Net.Result(player.GetPlayerName(), Api.Pack(KindWord(Kind), outcome.ToString().ToLowerInvariant(), crowned, coins));   // (for other mods: Api)

            string half = held.Count > 0 ? Words.Line("arena_call_half_master") : Words.Line("arena_call_half_purse", prize);
            // (the stands are told it of the fighter, by name: "you" is the fighter's alone)
            string who = player.GetPlayerName(), halfOf = Words.Line("arena_watch_half_purse", who);
            switch (outcome)
            {
                case Outcome.Win: Net.Shout(Words.Line("arena_call_victory"), Words.Then(Lines.Victory(), prize, hall), 7f, Words.Line("arena_call_victory"), Words.Then(Words.Line("arena_watch_won", who), hall)); break;
                case Outcome.CashOut: Net.Shout(Words.Line("arena_call_cash_out"), Words.Then(prize, hall), 6f, Words.Line("arena_watch_cash_out", who), hall); break;
                case Outcome.Died: Net.Shout(Words.Line("arena_call_fallen"), Words.Line(Lent ? "arena_call_fallen_lent" : "arena_call_fallen_own"), 7f, Words.Line("arena_watch_fallen", who), ""); break;
                case Outcome.TimeUp: Net.Shout(Words.Line("arena_call_time"), half, 5f, Words.Line("arena_call_time"), halfOf); break;
                case Outcome.LeftRing: Net.Shout(Words.Line("arena_call_left_ring"), half, 5f, Words.Line("arena_watch_left_ring", who), halfOf); break;
                case Outcome.Yield: Net.Shout(Words.Line("arena_call_yield"), half, 5f, Words.Line("arena_watch_yield", who), halfOf); break;
                case Outcome.Carried: Net.Shout(Words.Line("arena_call_carried"), half, 5f, Words.Line("arena_call_carried"), halfOf); break;
                case Outcome.CalledOff: Net.Shout(Words.Line("arena_call_called_off"), held.Count > 0 ? Words.Line("arena_call_no_one_master") : Words.Line("arena_call_no_one", prize), 5f, Words.Line("arena_call_called_off"), ""); break;
                default: break;
            }
            // (a win is told in the victory show)
            if (outcome != Outcome.Aborted && outcome != Outcome.Died && outcome != Outcome.Win)
                player.Message(MessageHud.MessageType.Center, held.Count > 0 ? T("arena_prize_waits") : T("arena_prize", PrizeText(data)));
            History.Add(player, KindWord(Kind), Tier, Round, Rounds, outcome.ToString().ToLowerInvariant(), counts, data);
            Plugin.Log.LogInfo($"Contest ended: {outcome}, {_kills} kills, prize {data}, score {score}");
        }

        /// <summary>
        /// The spoils a contest's end pays (Spoils), and on the Long Road the Arena's Blessing: the lands beaten (each in full the first time
        /// this week, a tenth after), today's trial's first win of the day (double, and a set of the land's best meads), a Champion Bout won and
        /// every fifth wave of the Endless Horde (in full for three paid wins a day, then a quarter; a starred champion half as much again).
        /// </summary>
        private static List<(string Prefab, int Amount)> SpoilsFor(Player player, Outcome outcome, bool win, bool daily, bool pay = true)
        {
            var all = new List<(string Prefab, int Amount)>();
            if (Kind == KindOf.Road)
            {
                int lands = _cleared / 3, best = Spoils.RoadBest;
                for (int land = 0; land < lands; land++) all.AddRange(Spoils.Of(land, land >= best ? 1f : 0.1f));
                if (!pay) return all;
                if (lands > 0 && best > 0) Hud.Say(T(lands > best ? "arena_spoils_record" : "arena_spoils_repeat"));
                Spoils.RoadReached(lands);
                if (outcome != Outcome.Aborted) Blessing.Give(player, lands);
            }
            else if (Kind == KindOf.Trial)
            {
                if (!daily) return all;
                all.AddRange(Spoils.Of(Tier, 2f));
                if (!Rules.NoFood)
                {
                    all.Add((Kit.Meads[Mathf.Clamp(Tier, 0, Kit.Meads.Length - 1)], 3));
                    all.Add((Tier < 3 ? "MeadStaminaMinor" : Tier < 6 ? "MeadStaminaMedium" : "MeadStaminaLingering", 3));
                }
            }
            else if (Kind == KindOf.Champion)
            {
                if (!win) return all;
                float share = pay ? Spoils.TakeDailyShare() : Spoils.NextShare;
                if (pay && share < 1f) Hud.Say(T("arena_spoils_tired", Spoils.FullPerDay));
                all.AddRange(Spoils.Of(Tier, share * (_championLevel >= 2 ? 1.5f : 1f)));
            }
            else
            {
                int bundles = _cleared / 5;
                if (bundles <= 0) return all;
                float share = pay ? Spoils.TakeDailyShare() : Spoils.NextShare;
                if (pay && share < 1f) Hud.Say(T("arena_spoils_tired", Spoils.FullPerDay));
                bool whole = outcome == Outcome.CashOut || outcome == Outcome.CalledOff;   // (given up mid-wave: half, as the purse)
                all.AddRange(Spoils.Of(Tier, bundles * share * (whole ? 1f : 0.5f)));
            }
            all.RemoveAll(s => !Roster.Has(s.Prefab) || s.Amount <= 0);
            return all;
        }

        /// <summary>
        /// What you would walk out with if you left between rounds now (for the HUD, which shows it growing): the coins (picked up, on the
        /// arena's steel; else your purse), the spoils earned so far and the champions' trophies. Nothing is paid by asking.
        /// </summary>
        internal static List<(string Prefab, int Amount)> Winnings()
        {
            var list = new List<(string Prefab, int Amount)>();
            Player p = Player.m_localPlayer;
            if (!Active || p == null) return list;
            // (on the arena's steel, the coins in your bag once your own things are with the Arena Master: before, they are yours)
            int coins = Lent ? (_stowed ? p.GetInventory().CountItems("$item_coins") : 0) : PurseCoins;
            if (coins > 0) list.Add(("Coins", coins));
            if (!Lent) foreach (string t in Trophies.Distinct()) list.Add((t, 1));
            foreach (var s in SpoilsFor(p, Outcome.CashOut, false, false, false))
            {
                int at = list.FindIndex(x => x.Prefab == s.Prefab);
                if (at >= 0) list[at] = (s.Prefab, list[at].Amount + s.Amount); else list.Add(s);
            }
            return list;
        }

        /// <summary>What a win would add to it (for the HUD), in words: the bonus, the stake back doubled, the spoils a win pays.</summary>
        internal static string OnWin()
        {
            Player p = Player.m_localPlayer;
            if (!Active || p == null || Kind == KindOf.Endless || Phase == PhaseKind.Victory) return "";
            var add = new Dictionary<string, int>();
            bool daily = Daily && !Ladder.DailyDone;
            float bonus = Lent ? (Kind == KindOf.Road ? 125f : 40f) * (Kind == KindOf.Road ? Roster.TierNames.Length : Tier + 1) * Rules.Multiplier * Plugin.Rewards.Value / 100f
                               : _purseCoins * 0.25f;
            int coins = Mathf.RoundToInt(bonus * (Lent ? 1.25f : 1f) * (daily ? 1.5f : 1f)) + Stake * 2;
            if (coins > 0) add["Coins"] = coins;
            if (Kind != KindOf.Road)
                foreach (var s in SpoilsFor(p, Outcome.Win, true, daily, false)) add[s.Prefab] = (add.TryGetValue(s.Prefab, out int had) ? had : 0) + s.Amount;
            return add.Count > 0 ? T("arena_hud_on_win", Prize.Text(add)) : "";
        }

        /// <summary>The champion now on the floor, if one is (for its bar at the top of the screen).</summary>
        internal static Character ChampionOut
        {
            get { foreach (Foe f in Foes) if (f.Champion && !f.Counted && f.Char != null && !f.Char.IsDead()) return f.Char; return null; }
        }

        /// <summary>The prize in words, from its data (Pay).</summary>
        internal static string PrizeText(string data)
        {
            string[] f = data.Split(';');
            if (f.Length < 7) return data;
            var n = new int[6];
            for (int i = 0; i < 6; i++) int.TryParse(f[i], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out n[i]);
            bool lent = n[1] == 1, win = n[4] == 1, daily = n[5] == 1;
            var lines = new List<string>();
            if (n[0] > 0) lines.Add(T(lent ? "arena_prize_coins_win" : "arena_coins", n[0]));
            if (n[2] > 0) lines.Add(T("arena_prize_picked_up", n[2]));
            foreach (var thing in Counted(f[6])) lines.Add(thing.Prefab.StartsWith("Trophy") ? ItemName(thing.Prefab) : T("arena_prize_item", thing.Amount, ItemName(thing.Prefab)));
            if (n[3] > 0) lines.Add(T(win ? "arena_prize_stake_doubled" : "arena_prize_stake_back", n[3]));
            if (daily) lines.Add(T("arena_prize_daily"));
            return lines.Count > 0 ? string.Join(", ", lines) : T("arena_prize_nothing");
        }

        /// <summary>
        /// Gathers off the fighting floor what the arena's fighters dropped and nobody picked up (coins, a champion's trophy and metal: marked
        /// by Armoury.Drop), and takes it away. What was gathered, by prefab.
        /// </summary>
        private static Dictionary<string, int> Sweep()
        {
            var swept = new Dictionary<string, int>();
            if (ZNetScene.instance == null) return swept;
            var found = new List<ItemDrop>();   // (gathered first: taking one away changes the list being walked)
            foreach (ZNetView view in Ground.Instances(ZNetScene.instance).Values)
            {
                if (view == null || !view.IsValid() || !view.GetZDO().GetBool(Armoury.DropKey, false)) continue;
                ItemDrop drop = view.GetComponent<ItemDrop>();
                if (drop != null && Site.OnFloor(drop.transform.position, 2f)) found.Add(drop);
            }
            foreach (ItemDrop drop in found)
            {
                string prefab = Utils.GetPrefabName(drop.gameObject);
                swept[prefab] = (swept.TryGetValue(prefab, out int had) ? had : 0) + drop.m_itemData.m_stack;
                ZNetView view = drop.GetComponent<ZNetView>();
                view.ClaimOwnership();
                view.Destroy();
            }
            if (swept.Count > 0) Plugin.Log.LogInfo($"Gathered off the floor: {string.Join(", ", swept.Select(s => s.Key + " x" + s.Value))}");
            return swept;
        }

        internal static void Give(string prefab, int amount, Player player)
        {
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop == null || amount <= 0) return;
            int max = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
            Inventory inv = player.GetInventory();
            while (amount > 0)
            {
                int n = Mathf.Min(amount, max);
                amount -= n;
                if (inv.CanAddItem(go, n)) inv.AddItem(go, n);
                else Object.Instantiate(go, player.transform.position + Vector3.up, Quaternion.identity).GetComponent<ItemDrop>().SetStack(n);
            }
        }
    }

    /// <summary>
    /// The ring is for those who have taken up a challenge. Anyone else found on the fighting floor (climbed in, or slipped through the
    /// grate behind a fighter) is shown out by the Arena Master's guards: back to the forecourt by the waystone. A fighter just finished
    /// has a while to walk out.
    /// </summary>
    internal static class Guard
    {
        private static float _graceUntil, _next;

        internal static void Grace(float seconds) => _graceUntil = Mathf.Max(_graceUntil, Time.time + seconds);

        internal static void Tick()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            Player p = Player.m_localPlayer;
            if (p == null || p.IsDead() || p.IsTeleporting() || !Site.Known || !Scenery.Built) return;
            if (Contest.Active || Duel.Active || Time.time < _graceUntil) return;
            if (!Site.OnFloor(p.transform.position, -0.5f)) return;
            Vector3 to = Site.World(Layout.Arrival.Pos + new Vector3(0f, 0.3f, 3f));
            to.y = Mathf.Max(to.y, Site.Ground(to, Site.Origin.y) + 0.3f);
            p.TeleportTo(to, Site.Turn * Quaternion.Euler(0f, 180f, 0f), false);
            p.Message(MessageHud.MessageType.Center, T("arena_guards"));
            Plugin.Log.LogInfo("Someone on the fighting floor without a challenge was shown out to the forecourt");
        }
    }

    /// <summary>What the announcer says, a few ways each so it does not repeat itself: one picked here, as Words (each game in its own language).</summary>
    internal static class Lines
    {
        private static string One(string key, params object[] args) => Words.Line(key + "_" + UnityEngine.Random.Range(1, 5), args);

        internal static string Welcome(string name) => One("arena_welcome", name);
        internal static string RoundCall(int count) => One("arena_round_call", count);
        internal static string ChampionDown() => One("arena_champion_down");
        internal static string Victory() => One("arena_victory_line");
    }
}
