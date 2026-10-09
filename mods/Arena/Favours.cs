using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Arena
{
    /// <summary>
    /// Playing to the crowd. The arena's meals are yesterday's leftovers (they start part spent), so what keeps you fed is the crowd: the more
    /// they love you the more often they throw you something, and the better it is. Excited, a bite or a small mead; roaring, the land's best
    /// food, a bigger mead, arrows or bombs; ecstatic, meads that make you stronger or faster, and now and then (on the arena's steel, once a
    /// land) the next land's weapon. A crowd that hates you pelts you with rotten meat.
    ///
    /// You win them with the fight (kills, quick kills, parries, flawless rounds, close calls) and with a show: emotes in the ring please them
    /// (more with a foe at arm's length, or just after a kill), and they answer in kind; the same one over and over bores them, and cowering
    /// makes them laugh at you (though crying at death's door may move them to pity, once). Now and then they chant for something (a flex, a
    /// roar, a kill now, a perfect parry, not a scratch for a while): give it to them in time and they go wild and throw you something good.
    /// </summary>
    internal static class Favours
    {
        // ---- gifts ------------------------------------------------------------------------------------------------------------------

        private static float _nextGift;

        private static bool _pitied;
        private static int _steelLand = -1;   // (the land the crowd last threw you steel in: once a land at most)

        internal static void Reset()
        {
            _nextGift = Time.time + 25f; _pitied = false; _steelLand = -1;
            _demand = Demand.None; _nextDemand = Time.time + 35f;
            _lastEmote = 0f; LastEmotes.Clear();
        }

        private static bool Open => Contest.Active && (Contest.Fighting || Contest.Phase == Contest.PhaseKind.Break || Contest.Phase == Contest.PhaseKind.Arming);

        internal static void Tick()
        {
            if (!Open) { _demand = Demand.None; return; }
            TickDemand();
            if (!Plugin.Gifts.Value || Time.time < _nextGift) return;
            float f = Crowd.Favour;
            if (f >= 90f) { Throw(3); _nextGift = Time.time + Random.Range(20f, 28f); }
            else if (f >= 75f) { Throw(2); _nextGift = Time.time + Random.Range(30f, 40f); }
            else if (f >= 55f) { Throw(1); _nextGift = Time.time + Random.Range(45f, 60f); }
            else if (f <= 10f && Contest.Fighting)
            {
                if (Crowd.Throw("RottenMeat", 1)) Hud.Call("THE CROWD PELTS YOU!", "Rotten meat. Win them back", false);
                _nextGift = Time.time + Random.Range(25f, 40f);
            }
            else _nextGift = Time.time + 5f;
        }

        private static readonly string[] HealthMeads = { "MeadHealthMinor", "MeadHealthMinor", "MeadHealthMinor", "MeadHealthMedium", "MeadHealthMedium", "MeadHealthMajor", "MeadHealthMajor" };
        private static readonly string[] StaminaMeads = { "MeadStaminaMinor", "MeadStaminaMinor", "MeadStaminaMinor", "MeadStaminaMedium", "MeadStaminaMedium", "MeadStaminaMedium", "MeadStaminaLingering" };
        private static readonly string[][] Bombs = { new string[0], new string[0], new[] { "BombOoze" }, new[] { "BombOoze" }, new[] { "BombBile", "BombOoze" }, new[] { "BombBile" }, new[] { "BombLava", "BombBile" } };
        private static readonly string[] Boosts = { "MeadBzerker", "MeadHasty", "MeadTasty", "MeadHealthLingering", "MeadStaminaLingering" };

        /// <summary>A gift from the crowd, better the higher the level (1 excited, 2 roaring, 3 ecstatic or a chant answered).</summary>
        internal static void Throw(int level)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            int land = Mathf.Clamp(Contest.Tier, 0, Roster.TierNames.Length - 1);
            bool food = !Rules.NoFood, arms = !Rules.Fists;
            var options = new List<(string Prefab, int Amount, string Call)>();
            if (food)
            {
                List<(string Prefab, Kit.Role Role)> menu = Kit.Menu(land);
                if (menu.Count > 0)
                {
                    var best = level >= 2 ? menu.Take(Mathf.Max(1, menu.Count / 2)).ToList() : menu;
                    var meal = best[Random.Range(0, best.Count)];
                    options.Add((meal.Prefab, 1, "A fresh " + Contest.ItemName(meal.Prefab).ToLowerInvariant()));
                }
                int mead = Mathf.Clamp(land + (level >= 2 ? 1 : 0), 0, HealthMeads.Length - 1);
                options.Add((HealthMeads[mead], 1, "A healing mead"));
                options.Add((StaminaMeads[mead], 1, "A stamina mead"));
                if (level >= 3)
                {
                    string boost = Boosts[Random.Range(0, Boosts.Length)];
                    options.Add((boost, 1, Contest.ItemName(boost)));
                    options.Add((boost, 1, Contest.ItemName(boost)));
                    if (land == 2) options.Add(("MeadPoisonResist", 1, "A poison resistance mead"));
                    if (land == 3) options.Add(("MeadFrostResist", 1, "A frost resistance mead"));
                    if (land == 6) options.Add(("BarleyWine", 1, "A fire resistance barley wine"));
                }
            }
            if (arms && level >= 2)
            {
                if (player.GetInventory().GetAllItems().Any(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow))
                {
                    string arrows = Kit.ArrowFor(land);
                    if (arrows != null) options.Add((arrows, 30, "A quiver of arrows"));
                }
                foreach (string bomb in Bombs[land]) options.Add((bomb, 2, "Bombs"));
            }
            // the big one, on the arena's steel: an upgrade for something you wear (then you need not buy it)
            if (Contest.Lent && level >= 2 && _steelLand != land && Random.value < (level >= 3 ? 0.25f : 0.1f) && Armoury.Random(player, Contest.Style, Rules.Fists, Kit.CrowdLoan) is var up && up != null)
            {
                var u = up.Value;
                if (Crowd.Throw(u.Prefab, 1, d => { d.m_itemData.m_quality = u.Quality; d.m_itemData.m_durability = d.m_itemData.GetMaxDurability(); d.m_itemData.m_customData[Kit.LoanKey] = Kit.CrowdLoan; }))
                {
                    _steelLand = land;
                    Net.Shout("THE CROWD THROWS YOU GEAR!", "An upgrade: " + Contest.ItemName(u.Prefab) + "! Pick it up and put it on", 3f);
                    Net.Sound("roar");
                    return;
                }
            }
            options.RemoveAll(o => !Roster.Has(o.Prefab));
            if (options.Count == 0) options.Add(("Coins", Random.Range(8, 20) * (level + land), "Coins"));
            var gift = options[Random.Range(0, options.Count)];
            if (Crowd.Throw(gift.Prefab, gift.Amount))
                Hud.Call(level >= 3 ? "THE CROWD SHOWERS YOU!" : "A GIFT FROM THE CROWD!", gift.Call, false);
        }

        // ---- the crowd's chants -----------------------------------------------------------------------------------------------------

        private enum Demand { None, Emote, Kill, Parry, Untouched }
        private static Demand _demand;
        private static string _emote = "", _call = "";
        private static float _until, _nextDemand;

        private static readonly (string Emote, string Call)[] Calls =
        {
            ("flex", "FLEX!"), ("roar", "ROAR!"), ("dance", "DANCE!"), ("bow", "TAKE A BOW!"), ("challenge", "CHALLENGE THEM!"), ("headbang", "HEADBANG!"), ("cheer", "CHEER WITH US!"),
        };

        private static void TickDemand()
        {
            if (_demand != Demand.None)
            {
                if (Time.time < _until) return;
                if (_demand == Demand.Untouched) Answered(); else Missed();
                return;
            }
            if (Time.time < _nextDemand) return;
            bool foes = Contest.Fighting && Contest.FoesLeft > 0;
            float r = Random.value;
            if (!foes || r < 0.45f)
            {
                var c = Calls[Random.Range(0, Calls.Length)];
                _demand = Demand.Emote; _emote = c.Emote; _call = c.Call; _until = Time.time + 10f;
            }
            else if (r < 0.7f) { _demand = Demand.Kill; _call = "A KILL! NOW!"; _until = Time.time + 10f; }
            else if (r < 0.87f) { _demand = Demand.Parry; _call = "A PERFECT PARRY!"; _until = Time.time + 15f; }
            else { _demand = Demand.Untouched; _call = "NOT A SCRATCH!"; _until = Time.time + 12f; }
            Net.Shout("THE CROWD CHANTS: " + _call, _demand == Demand.Emote ? "Give them what they want (your emotes, T)" : _demand == Demand.Untouched ? "Don't get hit" : "Quickly!", 3f);
            Net.Sound("horn");
        }

        private static void Answered()
        {
            _demand = Demand.None;
            _nextDemand = Time.time + Random.Range(35f, 55f);
            Crowd.Gain(15f, true);
            Net.Shout("THE CROWD LOVES IT!", "", 2.5f);
            Net.Sound("roar");
            Throw(Crowd.Favour >= 75f ? 3 : 2);
        }

        private static void Missed()
        {
            _demand = Demand.None;
            _nextDemand = Time.time + Random.Range(35f, 55f);
            Crowd.Hurt(6f);
            Net.Sound("boo");
            Hud.Call("The crowd is disappointed", "", false);
        }

        /// <summary>The chant on, and how long is left (seconds below zero: none).</summary>
        internal static string DemandLabel(out float seconds)
        {
            seconds = _demand == Demand.None ? -1f : _until - Time.time;
            return _demand == Demand.None ? "" : "The crowd chants: " + _call;
        }

        internal static void OnKill() { if (_demand == Demand.Kill) Answered(); }
        internal static void OnParry(bool perfect) { if (_demand == Demand.Parry && perfect) Answered(); }
        internal static void OnHurt() { if (_demand == Demand.Untouched) Missed(); }

        // ---- showboating ------------------------------------------------------------------------------------------------------------

        private static float _lastEmote;
        private static readonly Dictionary<string, float> LastEmotes = new Dictionary<string, float>();
        private static readonly HashSet<string> Shameful = new HashSet<string> { "cower", "cry", "despair", "nonono", "shrug", "kneel", "rest", "relax", "sit" };

        /// <summary>The player did an emote in the ring.</summary>
        internal static void OnEmote(string emote)
        {
            Player player = Player.m_localPlayer;
            if (!Open || player == null || !Site.OnFloor(player.transform.position, 0f)) return;
            emote = (emote ?? "").ToLowerInvariant();
            if (_demand == Demand.Emote && emote == _emote) { Answered(); Crowd.Mirror(emote); return; }
            if (Time.time - _lastEmote < 3f) return;
            _lastEmote = Time.time;
            bool seen = LastEmotes.TryGetValue(emote, out float at) && Time.time - at < 20f;
            LastEmotes[emote] = Time.time;
            if (Shameful.Contains(emote))
            {
                if ((emote == "cry" || emote == "despair" || emote == "cower") && !_pitied && player.GetHealth() < player.GetMaxHealth() * 0.3f)
                {
                    _pitied = true;
                    Crowd.Throw(HealthMeads[Mathf.Clamp(Contest.Tier, 0, HealthMeads.Length - 1)], 1);
                    Hud.Call("THE CROWD TAKES PITY ON YOU", "Just this once", false);
                    return;
                }
                Crowd.Hurt(4f);
                Crowd.Mirror("laugh");
                Hud.Call("The crowd laughs at you", "", false);
                return;
            }
            if (seen) { Hud.Call("They have seen that one", "Show them something new", false); return; }
            float gain = 4f;
            string call = null;
            if (Contest.Fighting && Contest.FoeWithin(player.transform.position, 8f)) { gain *= 2.5f; call = "SHOWBOAT!"; }
            else if (Time.time - Contest.LastKillTime < 4f && (emote == "bow" || emote == "flex" || emote == "roar" || emote == "challenge")) { gain *= 1.8f; call = "WHAT A SHOW!"; }
            Crowd.Gain(gain, false);
            Crowd.Mirror(emote);
            if (call != null) Hud.Call(call, "The crowd eats it up", false);
        }
    }

    /// <summary>An emote in the ring plays to the crowd.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.StartEmote))]
    internal static class Player_StartEmote_Crowd
    {
        private static void Postfix(Player __instance, string emote, bool __result)
        {
            if (__result && __instance == Player.m_localPlayer) Favours.OnEmote(emote);
        }
    }

    /// <summary>
    /// The kitchen's meals are yesterday's leftovers: on the arena's steel, one of them starts with only part of its time left (MealFreshness)
    /// once eaten. The crowd's gifts and the Armourer's food are fresh.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.EatFood))]
    internal static class Player_EatFood_Leftovers
    {
        private static void Postfix(Player __instance, ItemDrop.ItemData item, bool __result)
        {
            if (!__result || __instance != Player.m_localPlayer || !Contest.Active || !Contest.Lent || !Kit.IsLoan(item) || item.m_shared.m_foodBurnTime <= 0f) return;
            Player.Food food = __instance.GetFoods().FirstOrDefault(f => f.m_item != null && f.m_item.m_shared.m_name == item.m_shared.m_name);
            if (food == null) return;
            food.m_time = Mathf.Min(food.m_time, item.m_shared.m_foodBurnTime * Mathf.Clamp01(Plugin.MealFreshness.Value / 100f));
        }
    }

    /// <summary>
    /// Fighting is hungry work: on the arena's steel all food burns faster (RingHunger times as fast), so a meal lasts about a land and the
    /// crowd's gifts and the Armourer's fresh food are what keep you going. (Only how long it lasts: what it gives is as usual.)
    /// </summary>
    [HarmonyPatch(typeof(Player), "UpdateFood")]
    internal static class Player_UpdateFood_Hunger
    {
        private static float _owed;

        private static void Postfix(Player __instance, float dt, bool forceUpdate)
        {
            if (forceUpdate || __instance != Player.m_localPlayer || !Contest.Active || !Contest.Lent) { _owed = 0f; return; }
            _owed += dt * Mathf.Max(0f, Plugin.RingHunger.Value - 1f);
            if (_owed < 1f) return;
            float burn = Mathf.Floor(_owed);
            _owed -= burn;
            foreach (Player.Food food in __instance.GetFoods()) food.m_time = Mathf.Max(1f, food.m_time - burn);   // (the game's own tick lets it run out)
        }
    }
}
