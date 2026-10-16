using System;
using System.Collections.Generic;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Arena
{
    /// <summary>
    /// What a contest pays beside coin: the spoils of a land, its valuables (metal, hides, the rarer things its creatures give), far better
    /// the deeper the land. Only from lands this world has reached (else their worth in coins), so the arena is never a way past a boss.
    ///
    /// None of it can be farmed by running the same thing again: the Long Road pays a land's spoils in full only the first time it is beaten
    /// each week (a real week, the same for everyone; repeats pay a tenth), today's trial only its first win of the day, and the Champion Bout
    /// and the Endless Horde in full for three paid wins a day, then a quarter. Each character keeps its own count (Ladder).
    /// </summary>
    internal static class Spoils
    {
        // each land's spoils, a full bundle (what fighting there by hand would give in a long evening, and a rare thing or two)
        private static readonly (string Prefab, int Amount)[][] Lands =
        {
            new[] { ("DeerHide", 6), ("LeatherScraps", 10), ("Flint", 10), ("Resin", 10) },
            new[] { ("Bronze", 4), ("TrollHide", 3), ("FineWood", 10), ("RoundLog", 10) },
            new[] { ("Iron", 6), ("Chain", 2), ("ElderBark", 10), ("Guck", 5) },
            new[] { ("Silver", 6), ("WolfPelt", 4), ("FreezeGland", 4), ("Obsidian", 10) },
            new[] { ("BlackMetal", 6), ("LinenThread", 10), ("Needle", 8), ("Barley", 10) },
            new[] { ("Eitr", 4), ("Carapace", 8), ("BlackCore", 1), ("YggdrasilWood", 10) },
            new[] { ("FlametalNew", 4), ("AskHide", 4), ("CharredBone", 10), ("MoltenCore", 1) },
        };

        // a bundle's worth in coins, paid instead where the world has not reached the land
        private static readonly int[] CoinWorth = { 60, 120, 200, 300, 420, 560, 720 };

        /// <summary>A land's spoils at this size (1: a full bundle): its valuables if the world has reached it, else their worth in coins.</summary>
        internal static List<(string Prefab, int Amount)> Of(int land, float size)
        {
            land = Mathf.Clamp(land, 0, Lands.Length - 1);
            var list = new List<(string, int)>();
            if (size <= 0f) return list;
            if (land <= Roster.Beaten())
                foreach (var s in Lands[land])
                {
                    int n = Mathf.RoundToInt(s.Amount * size);
                    if (n > 0 && Roster.Has(s.Prefab)) list.Add((s.Prefab, n));
                }
            if (list.Count == 0) list.Add(("Coins", Mathf.Max(1, Mathf.RoundToInt(CoinWorth[land] * size * Plugin.Rewards.Value / 100f))));
            return list;
        }

        // ---- the Long Road's weekly record ------------------------------------------------------------------------------------

        /// <summary>The week (from a Monday, by the real calendar): the Long Road's record starts again each week.</summary>
        internal static int Week => (int)((DateTime.UtcNow.Date - new DateTime(2024, 1, 1)).TotalDays / 7);

        /// <summary>How many lands this character has beaten on the Long Road this week, at best.</summary>
        internal static int RoadBest => Ladder.Get("roadweek") == Week ? Ladder.Get("roadweekbest") : 0;

        internal static void RoadReached(int lands)
        {
            if (lands <= RoadBest) return;
            Ladder.Set("roadweek", Week);
            Ladder.Set("roadweekbest", lands);
        }

        // ---- the Champion Bout's and the Endless Horde's daily allowance --------------------------------------------------------

        internal const int FullPerDay = 3;

        /// <summary>The share of the spoils a paid win of the Bout or the Horde gets today (counting it): in full three times a day, then a quarter.</summary>
        internal static float TakeDailyShare()
        {
            int paid = PaidToday;
            Ladder.Set("spoilday", Ladder.Today);
            Ladder.Set("spoilwins", paid + 1);
            return paid < FullPerDay ? 1f : 0.25f;
        }

        /// <summary>The Bout's and the Horde's paid wins today, so far.</summary>
        internal static int PaidToday => Ladder.Get("spoilday") == Ladder.Today ? Ladder.Get("spoilwins") : 0;

        /// <summary>The share the next paid win would get (without counting it).</summary>
        internal static float NextShare => PaidToday < FullPerDay ? 1f : 0.25f;
    }

    /// <summary>
    /// The Arena's Blessing: walking out of the Long Road after beating a land or more, the crowd's roar goes with you for about a day of the
    /// world (half an hour): faster health and stamina back, and harder blows, more the further you got. It does not stack, and a shorter run
    /// never renews a stronger blessing. It is not saved: logging out ends it.
    /// </summary>
    internal static class Blessing
    {
        private const string Name = "ArenaBlessing";
        private const float Seconds = 1800f;

        internal static void Give(Player player, int lands)
        {
            if (player == null || player.IsDead() || lands <= 0) return;
            lands = Mathf.Clamp(lands, 1, Roster.TierNames.Length);
            SEMan seman = player.GetSEMan();
            int hash = Name.GetStableHashCode();
            if (seman.GetStatusEffect(hash) is SE_Stats had && had.m_staminaRegenMultiplier > Regen(lands) + 0.001f) return;   // (a stronger one stays as it is)
            seman.RemoveStatusEffect(hash, true);
            var se = ScriptableObject.CreateInstance<SE_Stats>();
            se.name = Name;
            se.m_name = "$arena_blessing";
            se.m_tooltip = T("arena_blessing_tip", lands);
            se.m_icon = ObjectDB.instance?.GetItemPrefab("SwordIron")?.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
            se.m_ttl = Seconds;
            se.m_staminaRegenMultiplier = Regen(lands);
            se.m_healthRegenMultiplier = Regen(lands);
            se.m_modifyAttackSkill = Skills.SkillType.All;
            se.m_damageModifier = 1f + 0.02f * lands;
            seman.AddStatusEffect(se, true);
            player.Message(MessageHud.MessageType.TopLeft, T("arena_blessing_given", lands));
        }

        private static float Regen(int lands) => 1f + 0.05f * lands;

        /// <summary>The blessing on the player now: how many lands it is for, and the seconds it has left (0: none).</summary>
        internal static int Active(Player player, out float left)
        {
            left = 0f;
            if (player == null || !(player.GetSEMan().GetStatusEffect(Name.GetStableHashCode()) is SE_Stats se)) return 0;
            left = Mathf.Max(0f, se.GetRemaningTime());
            return Mathf.RoundToInt((se.m_staminaRegenMultiplier - 1f) / 0.05f);
        }
    }
}
