using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

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
    /// give up mid-round, run out of time or leave the ring and you get half; die and you get nothing, and your tombstone is carried out of the
    /// ring for you to collect.
    /// </summary>
    internal static class Contest
    {
        internal enum PhaseKind { None, Ready, Arming, Countdown, Spawning, Fighting, Break, Victory }
        internal enum KindOf { Road, Champion, Endless, Trial }
        private enum Outcome { Win, CashOut, Yield, Died, TimeUp, LeftRing, Carried, Aborted }

        internal static PhaseKind Phase;
        internal static KindOf Kind;
        internal static string Title = "";
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
        private static int _victoryStep;
        private static bool _toChest, _crowned;
        private static readonly List<(string Prefab, int Amount, int Quality, string Loan)> PrizeItems = new List<(string, int, int, string)>();
        private static readonly Dictionary<string, float> Mats = new Dictionary<string, float>();
        private static int _combo, _kills, _count;
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
            string key = Plugin.YieldKey.Value.ToString();
            switch (Phase)
            {
                case PhaseKind.Ready: return "Walk through the gate into the ring";
                case PhaseKind.Arming: return Show.ChestEmptied ? "Put it on and eat: here they come" : "Your gear and meals are in the chest in the middle of the ring: take them, put them on, eat";
                case PhaseKind.Break: return Lent ? (Show.ArmourerStanding ? "Open your reward, see the Armourer by the gate" : "Pick up your coins: the next round") + $"  ·  or {key} twice to take your coins and leave"
                                                  : $"The next round  ·  or {key} twice to take your purse and leave";
                default: seconds = -1f; return "";
            }
        }
        internal static int FoesLeft => Queue.Count + Foes.Count(f => !f.Counted);
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

        internal static string KindName(KindOf kind) => kind == KindOf.Road ? "The Long Road" : kind == KindOf.Champion ? "Champion Bout" : kind == KindOf.Endless ? "The Endless Horde" : "Today's Trial";

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
            if (player == null || !Site.Known || !Scenery.Built) return "The arena is not ready yet.";
            if (Active) return "A contest is already on.";
            if (Duel.Active) return "You are in a duel.";
            if (Net.RemoteFight) return Net.BusyText + ". Wait your turn, or watch from the stands.";
            if (kind == KindOf.Road) tier = 0;
            tier = Mathf.Clamp(tier, 0, Roster.TierNames.Length - 1);
            if (Roster.Pool(tier).Count == 0) return "The Arena Master has no one for that land in this game.";
            if (kind == KindOf.Endless || kind == KindOf.Road) stake = 0;
            bool lent = kind == KindOf.Road || kind == KindOf.Trial;
            if (lent && Kit.Stowed(player)) return "The Arena Master still holds your things from your last fight: they come back in a moment.";
            Inventory inv = player.GetInventory();
            int fee = FeeFor(kind, tier);
            if (inv.CountItems("$item_coins") < fee + stake) return stake > 0 ? $"You have not got the coins: {fee} to enter and {stake} to stake." : $"You have not got the {fee} coins to enter.";

            if (fee + stake > 0) inv.RemoveItem("$item_coins", fee + stake);
            Stake = stake; Fee = fee; Daily = daily;
            Kind = kind; Tier = tier;
            Style = style < 0 ? UnityEngine.Random.Range(0, Kit.Styles.Length) : Mathf.Clamp(style, 0, Kit.Styles.Length - 1);   // (below 0: the armourer's pick)
            Rules.Set(fists, noFood, hard, timed);
            Rounds = kind == KindOf.Road ? 3 * Roster.TierNames.Length : kind == KindOf.Trial ? 3 : kind == KindOf.Champion ? 1 : 0;
            Title = kind == KindOf.Road ? "The Long Road" : KindName(kind) + " - " + Roster.TierNames[Tier];
            Round = 0; _kills = 0; _combo = 0; _outside = 0f; _knockedOut = false; _closeAt = 0f; _yieldAt = 0f;
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
            Net.Shout("TO THE RING!", "Walk through the gate and into the ring, " + player.GetPlayerName() + ". The crowd is waiting.", 6f);
            Phase = PhaseKind.Ready;
            _timer = ReadySeconds; _count = 4;
            if (lent) Hud.Say("At the ring the Arena Master takes what you carry, and the arena arms and feeds you. You get it all back when you come out.");
            Hud.Say("Play to the crowd: kills, parries, emotes (T) and answering their chants. When they are excited they throw you food, meads and better" + (lent ? " (the arena's meals are leftovers: you will need it)." : "."));
            Plugin.Log.LogInfo($"Contest started: {Title}, rules {Rules.Text()}, fee {fee}, stake {stake}" + (lent ? $", {Kit.Styles[Style]}" : ""));
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

            // giving up: twice, so a stray key does not end it. Between rounds it takes the purse; in a round it is half.
            if (Plugin.YieldKey.Value.IsDown() && !Window.IsOpen)
            {
                bool between = Phase == PhaseKind.Break || Phase == PhaseKind.Arming;
                if (_yieldAt > 0f && Time.time - _yieldAt < 3f) { End(between ? Outcome.CashOut : Outcome.Yield); return; }
                _yieldAt = Time.time;
                Hud.Say(Lent ? "Press again to leave with the coins you carry" : between ? "Press again to take your purse and leave" : "Press again to give up (you keep half your purse)");
            }

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
                        if (!Kit.Stow(player)) { End(Outcome.Aborted); Hud.Say("The Arena Master could not take your things: the contest is off, and your coins are back."); return; }
                        _stowed = true;
                        player.ClearFood();   // (what you ate outside went with your things: you fight on the arena's meals)
                    }
                    else Rules.Strip(player);
                    player.Heal(player.GetMaxHealth(), true);
                    player.AddStamina(player.GetMaxStamina());
                    _look = Figures.LookOf(player);
                    string intro = KindName(Kind).ToUpperInvariant() + "!";
                    Net.Sound("gong");
                    if (Lent)
                    {
                        // the armourer's chest rises in the middle of the ring: get your gear on and eat before the clock runs out
                        Show.PopChest(Show.Armoury, Kit.Outfit(Tier, Style, Rules.Fists, !Rules.NoFood));
                        Net.Shout(intro, Lines.Welcome(player.GetPlayerName()) + "   Arm yourself from the chest in the middle of the ring!", 5f);
                        Phase = PhaseKind.Arming;
                        _timer = 40f;
                    }
                    else
                    {
                        Net.Shout(intro, Lines.Welcome(player.GetPlayerName()) + "   (" + Rules.Text() + (Stake > 0 ? ", " + Stake + " coins on themselves" : "") + ")", 5f);
                        Phase = PhaseKind.Countdown;
                        _timer = 5f; _count = 4;
                    }
                }
                else if (_timer <= 0f) { End(Outcome.Aborted); Hud.Say("You did not come into the ring: the contest is off, and your coins are back."); }
                return;
            }

            // staying in the ring
            if (!Site.OnFloor(player.transform.position, 1.5f))
            {
                _outside += dt;
                if (Time.time - _lastWarn > 1f) { _lastWarn = Time.time; Hud.Say("Get back in the ring! " + Mathf.CeilToInt(8f - _outside)); }
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
                    if (_timer <= 0f) { Hud.Count("FIGHT!"); Net.Sound("cheer"); BeginRound(1); }
                    break;
                case PhaseKind.Spawning:
                case PhaseKind.Fighting:
                    if (Phase == PhaseKind.Spawning)
                    {
                        _spawnTimer -= dt;
                        if (_spawnTimer <= 0f && Queue.Count > 0) { Spawn(Queue.Dequeue(), player); _spawnTimer = 1.1f; }
                        if (Queue.Count == 0) Phase = PhaseKind.Fighting;
                    }
                    CheckFoes(player);
                    if (Rules.Timed)
                    {
                        float before = _roundLeft;
                        _roundLeft -= dt;
                        if (before > 10f && _roundLeft <= 10f) Net.Shout("TEN SECONDS!", "", 1.5f);
                        if (_roundLeft <= 0f) { End(Outcome.TimeUp); return; }
                    }
                    if (Phase == PhaseKind.Fighting && Foes.All(f => f.Counted) && Queue.Count == 0) RoundCleared(player);
                    break;
                case PhaseKind.Break:
                    _timer -= dt;
                    if (!Lent && Show.ChestEmptied && _timer > 6f) _timer = 6f;
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
            if (Kind == KindOf.Road && step == 0 && n > 1) { Show.TakeChest(); Net.Props(FightCover()); }   // (a new land: its own cover, the stage taken down)
            if (Lands)
            {
                // three rounds a land, its champion in the third; on the Long Road each three takes you to the next land
                if (Kind == KindOf.Road) Tier = Mathf.Min((n - 1) / 3, Roster.TierNames.Length - 1);
                champion = step == 2;
                fodder = 3 + step + (Rules.Hard ? 2 : 0) - (champion ? 3 : 0);   // (two with the champion)
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
                champion = n % 5 == 0;
                fodder = Mathf.Min(2 + n, 12) + (Rules.Hard ? 1 : 0) - (champion ? 2 : 0);
                lean = Mathf.Min(1f, n / 8f);
                level = 1 + (n - 1) / 5 + (Rules.Hard ? 1 : 0);
            }
            for (int i = 0; i < fodder; i++)
            {
                string prefab = Roster.Pick(Tier, lean);
                if (prefab == null) continue;
                int lv = level + (Lands && step >= 1 && UnityEngine.Random.value < 0.3f ? 1 : 0);
                Queue.Enqueue(new Item { Prefab = prefab, Level = Mathf.Clamp(lv, 1, 3) });
            }
            if (champion)
            {
                string prefab = Roster.Champion(Tier);
                // (its stars by what its land's gear can take: Roster.ChampionLevel)
                if (prefab != null) Queue.Enqueue(new Item { Prefab = prefab, Name = Roster.ChampionName(), Level = Roster.ChampionLevel(Tier, prefab, Kind, n, Rules.Hard), Champion = true });
            }
            if (Queue.Count == 0) { End(Outcome.Aborted); return; }
            _roundLeft = Rules.RoundSeconds + (champion ? 30f : 0f);
            Phase = PhaseKind.Spawning;
            _spawnTimer = 1.4f;
            string land = Roster.TierNames[Tier].ToUpperInvariant();
            string head = Kind == KindOf.Champion ? "THE CHAMPION!" : Kind == KindOf.Endless ? "WAVE " + n
                        : champion ? (Kind == KindOf.Road ? "THE CHAMPION OF THE " + land + "!" : "FINAL ROUND!") : (Kind == KindOf.Road ? land + ": ROUND " + (step + 1) : "ROUND " + n);
            string sub = champion && Kind != KindOf.Champion ? "A champion comes with them!" : Lines.RoundCall(Queue.Count);
            Net.Shout(head, sub, 3.5f);
            Net.Sound("gong");
            if (n > 1) Net.Sound("cheer");
        }

        private static void RoundCleared(Player player)
        {
            int step = (Round - 1) % 3;
            float units = Lands ? 0.7f + 0.4f * step : Kind == KindOf.Endless ? 0.5f + 0.15f * Round : 3f;
            if (!_hurtThisRound) { Crowd.Gain(12f, false); Net.Shout("FLAWLESS!", "Not a scratch on you", 2.2f); }
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
                    // (food and meads are the Armourer's to sell: only now and then is one in the chest too)
                    if (!Rules.NoFood && UnityEngine.Random.value < 0.3f)
                    {
                        if (UnityEngine.Random.value < 0.5f) foreach (string meal in Kit.Plate(Tier + 1, new[] { Kit.Role.Health }).Take(1)) reward.Add((meal, 1, 1, Tier.ToString()));
                        else reward.Add((Kit.Meads[Mathf.Clamp(Tier + 1, 0, Kit.Meads.Length - 1)], 1, 1, Tier.ToString()));
                    }
                    if (reward.Count > 0) Show.PopChest(Show.Reward, reward);
                    Show.Armourer(true);
                }
                Net.Sound("roar");
                Net.Shout("THE " + Roster.TierNames[Tier].ToUpperInvariant() + " IS BEATEN!", $"On to the {Roster.TierNames[next]}! Open your reward, see the Armourer by the gate"
                          + $"   -   {Plugin.YieldKey.Value} twice to take your coins and leave", 7f);
                _timer = 50f;
                return;
            }
            if (Lent)
            {
                Net.Shout("ROUND " + Round + " CLEARED!", $"Pick up your coins   -   {Plugin.YieldKey.Value} twice to take them and leave", 4.5f);
                player.Heal(player.GetMaxHealth() * 0.3f, true);
                return;
            }
            Net.Shout((Kind == KindOf.Endless ? "WAVE " : "ROUND ") + Round + " CLEARED!", $"Your purse: {PurseText()}   -   {Plugin.YieldKey.Value} twice to take it and leave", 4.5f);
            player.Heal(player.GetMaxHealth() * 0.3f, true);
        }

        private static void AddToPurse(float units)
        {
            float mult = Rules.Multiplier * (1f + Crowd.Favour / 200f) * Plugin.Rewards.Value / 100f;
            _purseCoins += (20 + 30 * Tier) * units * mult;
            // materials only from lands your world has reached (the arena's steel takes you further than that, but pays it in coins)
            string mat = Tier <= Roster.Beaten() ? Roster.Material(Tier) : null;
            if (mat != null) Mats[mat] = (Mats.TryGetValue(mat, out float had) ? had : 0f) + (2 + Tier) * 0.5f * units * mult;
        }

        internal static string PurseText()
        {
            if (Lent) return (Player.m_localPlayer != null ? Player.m_localPlayer.GetInventory().CountItems("$item_coins") : 0) + " coins in your bag";
            string s = PurseCoins + " coins";
            foreach (var m in Mats) if (Mathf.FloorToInt(m.Value) > 0) s += ", " + Mathf.FloorToInt(m.Value) + " " + ItemName(m.Key);
            return s;
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
                if (!string.IsNullOrEmpty(item.Name)) foe.Char.m_name = item.Name;
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
                Net.Shout(item.Name.ToUpperInvariant(), Roster.Origin(Tier) + "... a champion of the pit!", 4f);
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
                    string t = Roster.Trophy(f.Prefab), m = Roster.Material(Tier);
                    if (t != null) Armoury.Drop(t, 1, f.LastPos);
                    if (m != null) Armoury.Drop(m, Mathf.RoundToInt((2 + Tier) * 1.5f * Plugin.Rewards.Value / 100f), f.LastPos);
                }
            }
            if (f.Champion)
            {
                string trophy = !Lent && Tier <= Roster.Beaten() ? Roster.Trophy(f.Prefab) : null;
                if (trophy != null) Trophies.Add(trophy);
                Net.Shout("THE CHAMPION FALLS!", Lines.ChampionDown(), 3f);
                Net.Sound("roar");
            }
            else if (close) Net.Shout("CLOSE CALL!", "The crowd is on its feet", 1.8f);
            else if (_combo == 2) Net.Shout("DOUBLE!", "", 1.5f);
            else if (_combo == 3) Net.Shout("TRIPLE!", "", 1.5f);
            else if (_combo >= 5) Net.Shout("UNSTOPPABLE!", _combo + " in a row", 1.8f);
        }

        /// <summary>The local player blocked a blow in a contest (a perfect parry is worth more to the crowd).</summary>
        internal static void OnBlock(bool perfect)
        {
            if (!Fighting) return;
            Favours.OnParry(perfect);
            Crowd.Gain(perfect ? 6f : 1f, perfect);
            if (perfect && Time.time - _lastParryShout > 4f) { _lastParryShout = Time.time; Net.Shout("PARRY!", "", 1.2f); }
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

        /// <summary>Ends the contest without a result (the mod unloaded, the player logged out): the stake comes back, and the purse is paid.</summary>
        internal static void Abort(string why)
        {
            if (!Active) return;
            if (Phase == PhaseKind.Victory) { FinishVictory(); return; }
            End(Outcome.Aborted);
        }

        // ---- the victory ---------------------------------------------------------------------------------------------------------

        /// <summary>
        /// The whole contest won: the crowd on its feet, fireworks over the stands, your name called, a prize chest rising in the middle of the
        /// ring, and the main gate opening for you to walk out when you are ready (or after a while).
        /// </summary>
        private static void StartVictory(Player player)
        {
            Phase = PhaseKind.Victory;
            _victoryAt = Time.time; _victoryStep = 0; _offFloor = 0f; _nextChant = Time.time + 5f;
            _timer = 90f;
            ClearFoes();
            Crowd.Set(100f);
            PrizeItems.Clear();
            _toChest = true;
            Pay(player, Outcome.Win, false);   // (into the prize chest, which rises in a moment)
            _toChest = false;
            Net.Sound("roar"); Net.Sound("horn"); Net.Sound("applause");
            Net.Props(Stage);   // (the platform rises for the prize chest)
            Net.Fireworks(18, 12f);
            Net.Celebrate();
        }

        private static void TickVictory(Player player, float dt)
        {
            _timer -= dt;
            float t = Time.time - _victoryAt;
            string name = player.GetPlayerName().ToUpperInvariant();
            if (_victoryStep == 0 && t > 4f)
            {
                _victoryStep = 1;
                Net.Shout(_crowned ? "CHAMPION OF " + KindName(Kind).Replace("The ", "THE ").ToUpperInvariant() + "!" : name + " TRIUMPHS!", $"The crowd chants your name, {player.GetPlayerName()}!" + (_crowned ? " Your statue will stand in the Hall of Fame." : ""), 5f);
                Net.Sound("horn");
            }
            if (_victoryStep == 1 && t > 7f)
            {
                _victoryStep = 2;
                Show.PopChest(Show.Prize, PrizeItems);
                Net.Sound("roar");
                Hud.Say("Your prize is in the chest in the middle of the ring");
            }
            if (_victoryStep == 2 && t > 11f)
            {
                _victoryStep = 3;
                Net.Gate(Scenery.MainGrate, 120f);
                Net.Door(120f);
                Net.Shout("TAKE A BOW!", "Open your prize, then walk out through the main gate when you are ready", 3f);
                Net.Fireworks(12, 9f);
            }
            if (Time.time > _nextChant) { _nextChant = Time.time + UnityEngine.Random.Range(7f, 10f); Net.Sound(UnityEngine.Random.value < 0.5f ? "horn" : "cheer"); }
            // done: walked out of the ring (once the gate is open), fell, or the show has gone on long enough
            if (!Site.OnFloor(player.transform.position, 2f)) _offFloor += dt; else _offFloor = 0f;
            if (player.IsDead() || _timer <= 0f || _victoryStep >= 3 && _offFloor > 1.5f) FinishVictory();
        }

        /// <summary>"Bring them on!": the break ends now (well, in a moment).</summary>
        internal static void ReadyEarly()
        {
            if ((Phase == PhaseKind.Break || Phase == PhaseKind.Arming) && _timer > 3f) { _timer = 3f; Net.Sound("roar"); }
        }

        private static void FinishVictory()
        {
            Player player = Player.m_localPlayer;
            Phase = PhaseKind.None;
            Show.Armourer(false);
            Show.TakeChest();   // (what is still in the prize chest goes in your bag)
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
            bool wasReady = Phase == PhaseKind.Ready;
            Phase = PhaseKind.None;
            ClearFoes();
            Show.TakeChest();
            Show.Armourer(false);
            if (player != null && !player.IsDead() && !wasReady && _look.Length == 0) _look = Figures.LookOf(player);
            // your own things back (if you fell, they come back when you rise: Kit.Tick)
            if (_stowed && player != null && !player.IsDead()) Kit.Return(player);
            _stowed = false;
            if (player != null) Pay(player, outcome, wasReady);
            Rules.Restore(player);
            Rules.Clear();
            if (player != null && !player.IsDead() && outcome == Outcome.Carried) player.SetHealth(player.GetMaxHealth() * 0.35f);
            _knockedOut = false;
            Net.Gate(Scenery.MainGrate, wasReady ? 0f : 30f);
            Net.Door(wasReady ? 0f : 45f);   // (open again for you to walk out)
            Guard.Grace(60f);
            bool won = outcome == Outcome.Win || outcome == Outcome.CashOut;
            if (won) { Net.Sound("roar"); Net.Sound("horn"); Net.Sound("applause"); } else { Net.Sound("boo"); }
            _closeAt = Time.time + (won ? 8f : 5f);
        }

        // ---- the prize -------------------------------------------------------------------------------------------------------------

        private static void Pay(Player player, Outcome outcome, bool beforeTheFight)
        {
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
            if (Lent) { _purseCoins = win ? (Kind == KindOf.Road ? 250f : 80f) * (Tier + 1) * Rules.Multiplier * Plugin.Rewards.Value / 100f : 0f; Mats.Clear(); }
            float share = outcome == Outcome.Win ? 1.25f : outcome == Outcome.CashOut || outcome == Outcome.Aborted ? 1f : outcome == Outcome.Died ? 0f : 0.5f;
            bool dailyBonus = win && Daily && !Ladder.DailyDone;
            if (dailyBonus) { share *= 1.5f; Ladder.Set("daily", Ladder.Today); }
            int coins = Mathf.RoundToInt(_purseCoins * share);
            int stakeBack = win ? Stake * 2 : outcome == Outcome.Aborted ? Stake : 0;
            List<string> trophies = outcome != Outcome.Died ? Trophies.Distinct().ToList() : new List<string>();

            // (after a win the prize goes in the prize chest that rises in the ring)
            void Hand(string prefab, int amount) { if (_toChest) PrizeItems.Add((prefab, amount, 1, null)); else Give(prefab, amount, player); }
            var lines = new List<string>();
            if (coins + stakeBack > 0) Hand("Coins", coins + stakeBack);
            if (coins > 0) lines.Add(coins + (Lent ? " coins for the win" : " coins"));
            if (Lent && carried > 0 && outcome != Outcome.Died) lines.Add(carried + " coins you picked up");
            foreach (var m in Mats)
            {
                int n = Mathf.FloorToInt(m.Value * share);
                if (n <= 0) continue;
                Hand(m.Key, n);
                lines.Add(n + " " + ItemName(m.Key));
            }
            foreach (string trophy in trophies) { Hand(trophy, 1); lines.Add(ItemName(trophy)); }
            if (stakeBack > 0) lines.Add((win ? "your stake doubled: " : "your stake back: ") + stakeBack);
            if (dailyBonus) lines.Add("today's bonus");
            string prize = lines.Count > 0 ? string.Join(", ", lines) : "nothing";

            // the Hall of Champions at this arena
            int score = coins + (Lent && outcome != Outcome.Died ? carried : 0) + _kills * 5 + (win ? 150 : 0) + Mathf.RoundToInt(_peak) + Round * 20;
            string what = Kind == KindOf.Road ? (win ? "The Long Road, to the very end" : $"The Long Road, to the {Roster.TierNames[Tier]} (round {Round} of {Rounds})")
                        : (Kind == KindOf.Endless ? "Endless, wave " + Round : Kind == KindOf.Trial ? "Trial, round " + Round : "Champion Bout") + " (" + Roster.TierNames[Tier] + ")";
            string kindKey = Kind == KindOf.Road ? "road" : Kind == KindOf.Champion ? "champion" : Kind == KindOf.Endless ? "endless" : "trial";
            bool crowned = _crowned = outcome != Outcome.Aborted && Ladder.Enter(kindKey, player.GetPlayerName(), what, score, _look);   // (as they fought: their statue wears it)
            string hall = crowned ? $"   -   the new Champion of {KindName(Kind).Replace("The ", "the ")}!" : "";
            if (crowned) Net.Sound("roar");

            switch (outcome)
            {
                case Outcome.Win: Net.Shout("VICTORY!", Lines.Victory() + "   " + prize + hall, 7f); break;
                case Outcome.CashOut: Net.Shout("YOU TAKE YOUR PURSE", prize + hall, 6f); break;
                case Outcome.Died: Net.Shout("YOU HAVE FALLEN", Lent ? "Your purse is lost. The Arena Master gives your things back when you rise." : "Your purse is lost. Your tombstone waits in the forecourt, by the Hall of Fame.", 7f); break;
                case Outcome.TimeUp: Net.Shout("TIME!", "Half your purse: " + prize, 5f); break;
                case Outcome.LeftRing: Net.Shout("YOU LEFT THE RING", "Half your purse: " + prize, 5f); break;
                case Outcome.Yield: Net.Shout("YOU YIELD", "Half your purse: " + prize, 5f); break;
                case Outcome.Carried: Net.Shout("CARRIED OUT", "Half your purse: " + prize, 5f); break;
                default: break;
            }
            if (outcome != Outcome.Aborted && outcome != Outcome.Died && !_toChest) player.Message(MessageHud.MessageType.Center, "Prize: " + prize);
            Plugin.Log.LogInfo($"Contest ended: {outcome}, {_kills} kills, prize {prize}, score {score}");
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
            p.Message(MessageHud.MessageType.Center, "The Arena Master's guards show you out: the ring is for those who have taken up a challenge");
            Plugin.Log.LogInfo("Someone on the fighting floor without a challenge was shown out to the forecourt");
        }
    }

    /// <summary>What the announcer says, a few ways each so it does not repeat itself.</summary>
    internal static class Lines
    {
        private static string One(params string[] lines) => lines[UnityEngine.Random.Range(0, lines.Length)];

        internal static string Welcome(string name) => One($"Give it up for {name}!", $"{name} steps into the ring!", $"The crowd roars for {name}!", $"Here comes {name}! Will they survive?");
        internal static string RoundCall(int count) => One($"{count} challengers enter the ring", $"{count} of them! Show them no mercy", $"Here they come: {count} of them", $"{count} foes. The crowd wants blood");
        internal static string ChampionDown() => One("What a fight!", "The crowd goes wild!", "Nobody saw that coming!", "A new legend is born!");
        internal static string Victory() => One("The crowd chants your name!", "What a performance!", "A champion of the pit!", "Odin himself is watching!");
    }
}
