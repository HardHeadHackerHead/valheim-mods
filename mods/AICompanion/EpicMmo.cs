using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using static AICompanion.Words;

namespace AICompanion
{
    /// <summary>
    /// EpicMMO System (WackyMole.EpicMMOSystem), when it is installed: a companion levels up as a player does there. It earns experience from
    /// what it kills, chops, mines, breaks and picks (EpicMMO's own table, rates and switches, times our CompanionExp setting), on EpicMMO's
    /// level curve, and its points go
    /// where it fights: Strength and Intellect (its hits), Body (stamina, physical armour), Vigour (health, magic armour), at the server's
    /// per-point values. It loses some of its way to the next level when it falls (EpicMMO's LossExp). Kept in its ZDO and its Profile.
    ///
    /// Its kills also count for its player as a tame's do (EpicMMO's "Tames give XP on Mob kill"), and keep their drops: EpicMMO gives
    /// experience only for kills by a player or a tame, and with its defaults (creature levels on, "RemoveAllDrops From NonPlayer Kills")
    /// a creature a companion finished dropped nothing. For EpicMMO's checks in Character.RPC_Damage the attacking companion counts as tamed
    /// (EpicMmo_CreditKill), and only there.
    ///
    /// Nothing here runs without EpicMMO. Everything of it is reached by reflection, so a missing or changed EpicMMO can't break ours.
    /// </summary>
    internal static class EpicMmo
    {
        public const string PluginId = "WackyMole.EpicMMOSystem";
        public const string XpKey = "dhc_mmoxp";   // all the experience it has earned (long); its level comes from it and EpicMMO's curve

        private static bool _checked, _have;
        private static Type _main;
        private static MethodInfo _key, _xpOverride, _contains, _exp, _maxExp;

        /// <summary>EpicMMO is installed (asked once a world is up: mods loaded after ours aren't listed before).</summary>
        public static bool Installed { get { if (!_checked) Detect(); return _have; } }

        /// <summary>Installed, and the server lets companions level (CompanionExp above 0).</summary>
        public static bool On => Installed && Plugin.MmoExp.Value > 0f;

        private static void Detect()
        {
            if (ZNetScene.instance == null) return;   // too early to know: ask again later
            _checked = true;
            try
            {
                Type main = BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(PluginId, out PluginInfo info) && info.Instance != null
                    ? info.Instance.GetType() : Type.GetType("EpicMMOSystem.EpicMMOSystem, EpicMMOSystem");
                if (main == null) return;
                Type monsters = main.Assembly.GetType("EpicMMOSystem.DataMonsters");
                const BindingFlags S = BindingFlags.Public | BindingFlags.Static;
                _key = monsters?.GetMethod("GetConfigurationKey", S, null, new[] { typeof(Character) }, null);
                _xpOverride = monsters?.GetMethod("GetXPOverride", S, null, new[] { typeof(Character) }, null);   // (newer versions only)
                _contains = monsters?.GetMethod("contains", S, null, new[] { typeof(string) }, null);
                _exp = monsters?.GetMethod("getExp", S, null, new[] { typeof(string) }, null);
                _maxExp = monsters?.GetMethod("getMaxExp", S, null, new[] { typeof(string) }, null);
                _main = main;
                _have = _contains != null && _exp != null && _maxExp != null && Entry("maxLevel") != null && Entry("levelExp") != null;
                Plugin.Instance?.Note(_have ? "EpicMMO System found: companions earn experience and levels, and their kills count for their player as a tame's"
                                            : "EpicMMO System found, but not the parts companions need (a new version?): companions don't level with it");
            }
            catch (Exception e) { _have = false; Plugin.Instance?.Warn("Looking for EpicMMO System: " + e.Message); }
        }

        // ---- EpicMMO's settings (the server's, through its own sync) ------------------------------------------

        private static readonly Dictionary<string, ConfigEntryBase> Entries = new Dictionary<string, ConfigEntryBase>();

        private static ConfigEntryBase Entry(string field)
        {
            if (Entries.TryGetValue(field, out ConfigEntryBase e)) return e;
            try { e = _main?.GetField(field, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as ConfigEntryBase; } catch { e = null; }
            return Entries[field] = e;
        }

        private static float F(string field, float fallback) { try { return Entry(field) is ConfigEntryBase e ? Convert.ToSingle(e.BoxedValue, CultureInfo.InvariantCulture) : fallback; } catch { return fallback; } }
        private static int I(string field, int fallback) { try { return Entry(field) is ConfigEntryBase e ? Convert.ToInt32(e.BoxedValue, CultureInfo.InvariantCulture) : fallback; } catch { return fallback; } }
        private static bool B(string field, bool fallback) => Entry(field)?.BoxedValue is bool b ? b : fallback;
        private static string Str(string field) => Entry(field)?.BoxedValue as string ?? "";

        // ---- levels ---------------------------------------------------------------------------------------

        private static long[] _need;
        private static string _needFor;

        /// <summary>The experience from each level to the next (index: the level), EpicMMO's curve (LevelSystem.FillLevelsExp).</summary>
        private static long[] Need()
        {
            int max = Mathf.Max(1, I("maxLevel", 100)), first = I("levelExp", 300);
            float mult = F("multiNextLevel", 1.05f);
            bool add = B("levelexpforeachlevel", true);
            string sig = $"{max}|{first}|{mult}|{add}";
            if (sig == _needFor) return _need;
            var need = new long[max + 1];
            long n = add ? 0L : first;
            for (int i = 1; i <= max; i++)
            {
                n = add ? (long)Math.Round(n * mult + first) : (long)Math.Round(n * mult);
                need[i] = Math.Max(1L, n);
            }
            _needFor = sig;
            return _need = need;
        }

        /// <summary>Its level from all the experience it has, and how far it is into that level (0 to go: the top level).</summary>
        public static int LevelOf(long total, out long into, out long toGo)
        {
            long[] need = Need();
            int max = need.Length - 1, level = 1;
            long rest = Math.Max(0L, total);
            while (level < max && rest >= need[level]) { rest -= need[level]; level++; }
            into = rest;
            toGo = level < max ? need[level] : 0L;
            return level;
        }

        /// <summary>All its experience: what is saved, and what this game earned for it since (Unsaved).</summary>
        public static long Xp(Component c)
        {
            long saved = Companion.Zdo(c)?.GetLong(XpKey, 0L) ?? 0L;
            return c is Humanoid h && Unsaved.TryGetValue(h, out Held held) ? saved + held.Xp : saved;
        }

        /// <summary>Its experience set outright (a fall while away), on the game that runs it.</summary>
        public static void SetXp(Humanoid c, long total)
        {
            Unsaved.Remove(c);
            ZDO z = Companion.Zdo(c);
            if (z != null && z.GetLong(XpKey, 0L) != Math.Max(0L, total)) z.Set(XpKey, Math.Max(0L, total));   // (unchanged: no write, so nothing new without EpicMMO)
        }

        /// <summary>What its level gives it: the points (as EpicMMO's attributes) and what they do, at the server's per-point values.</summary>
        internal struct Bonus
        {
            public int Level, Points, Strength, Intellect, Body, Vigour;
            public float Physical, Magic, Health, Stamina, PhysicalArmor, MagicArmor;   // damage and armour in percent
        }

        public static Bonus Of(Component c)
        {
            var b = new Bonus { Level = 1 };
            if (!On || c == null) return b;
            b.Level = LevelOf(Xp(c), out _, out _);
            // EpicMMO's free points: per level, at the start, and its bonus levels ("5:5,10:5").
            int points = b.Level * I("freePointForLevel", 5) + I("startFreePoint", 5);
            foreach (string part in Str("levelsForBinusFreePoint").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = part.Split(':');
                if (f.Length == 2 && int.TryParse(f[0].Trim(), out int at) && int.TryParse(f[1].Trim(), out int more) && at <= b.Level) points += more;
            }
            b.Points = points;
            // Where it fights: 40% to its hits (Strength for weapons, Intellect for staffs: the same points, it uses one at a time), 30% Body, 30% Vigour.
            int attack = Mathf.RoundToInt(points * 0.4f), body = Mathf.RoundToInt(points * 0.3f), vigour = points - attack - body;
            b.Strength = Mathf.Min(attack, I("maxValueStrength", 200));
            b.Intellect = Mathf.Min(attack, I("maxValueIntelligence", 200));
            b.Body = Mathf.Min(body, I("maxValueEndurance", 200));
            b.Vigour = Mathf.Min(vigour, I("maxValueVigour", 200));
            b.Physical = b.Strength * F("physicDamage", 0.2f);
            b.Magic = b.Intellect * F("magicDamage", 0.2f);
            b.Stamina = b.Body * F("addStamina", 1f);
            b.PhysicalArmor = Mathf.Min(80f, b.Body * F("physicArmor", 0.15f));
            b.Health = b.Vigour * F("addHp", 1f);
            b.MagicArmor = Mathf.Min(80f, b.Vigour * F("magicArmor", 0.1f));
            return b;
        }

        public static float Health(Component c) => On ? Of(c).Health : 0f;
        public static float Stamina(Component c) => On ? Of(c).Stamina : 0f;

        // ---- experience ---------------------------------------------------------------------------------------

        /// <summary>
        /// The experience a kill is worth, as EpicMMO works it out for a player (MonsterDeath_Path.RPC_DeadMonster): the monster's own
        /// override if it has one, else a roll between its min and max, and more for each star.
        /// </summary>
        private static int KillXp(Character victim, string name, int stars)
        {
            string key = victim != null && _key != null ? _key.Invoke(null, new object[] { victim }) as string ?? name : name;
            if (!Contains(key)) key = name;
            int over = victim != null && _xpOverride != null ? (int)_xpOverride.Invoke(null, new object[] { victim }) : -1;
            if (over >= 0) return over;
            if (!Contains(key)) return 0;
            int exp = (int)_exp.Invoke(null, new object[] { key }), max = (int)_maxExp.Invoke(null, new object[] { key });
            return Convert.ToInt32(exp + max * F("expForLvlMonster", 0.25f) * Mathf.Max(0, stars - 1));
        }

        private static bool Contains(string key) => !string.IsNullOrEmpty(key) && (bool)_contains.Invoke(null, new object[] { key });

        /// <summary>It killed a creature (on the game that runs it).</summary>
        public static void Killed(Humanoid c, Character victim)
        {
            if (!On || c == null || victim == null || victim.IsPlayer() || Companion.Is(victim) || victim.IsTamed()) return;
            try { Earn(c, KillXp(victim, victim.gameObject.name, victim.GetLevel())); }
            catch (Exception e) { Plugin.Instance?.Warn("EpicMMO experience for a kill: " + e.Message); }
        }

        /// <summary>What it worked, for EpicMMO's non-combat experience (each with EpicMMO's own switch for it).</summary>
        public enum Work { Tree, Mining, Destructible, Pick }

        /// <summary>
        /// It chopped, mined, broke or picked something (on the game that runs it), worth EpicMMO's experience for it, <paramref name="times"/>
        /// times over, as for a player (LevelSystem_noncombat): a tree once it falls, each hit on a log or a rock, a stump or the like once it
        /// breaks, each pick (at most one every 3 s, unless <paramref name="away"/>: CatchUp's picks all happen in one frame). Picking goes by
        /// EpicMMO's mining switch, as for players.
        /// </summary>
        public static void Worked(Humanoid c, Component thing, Work kind, int times = 1, bool away = false)
        {
            if (!On || c == null || thing == null || times <= 0 || B("disableNonCombatObjects", false)) return;
            if (B(kind == Work.Tree ? "disableTreeXP" : kind == Work.Destructible ? "disableDestructablesXP" : "disableMiningXP", false)) return;
            try
            {
                string name = thing.gameObject.name;
                if (kind == Work.Pick && !away)
                {
                    if (!Unsaved.TryGetValue(c, out Held held) || Time.time >= held.NextPick) Hold(c).NextPick = Time.time + 3f;   // (EpicMMO: a pick at most every 3 s)
                    else return;
                }
                if (!Contains(name)) return;
                int xp = 0;
                for (int i = 0; i < times; i++) xp += (int)_exp.Invoke(null, new object[] { name });
                Earn(c, xp);
            }
            catch (Exception e) { Plugin.Instance?.Warn("EpicMMO experience for work: " + e.Message); }
        }

        /// <summary>A prefab's worth (CatchUp: a tree's fallen log, which it chopped up without it being made).</summary>
        public static void WorkedAway(Humanoid c, GameObject prefab, Work kind, int times)
        {
            if (prefab == null || !On || B("disableNonCombatObjects", false) || B(kind == Work.Tree ? "disableTreeXP" : "disableMiningXP", false)) return;
            try
            {
                string name = prefab.name + "(Clone)";   // (EpicMMO's table is by a spawned thing's name)
                if (!Contains(name)) return;
                int xp = 0;
                for (int i = 0; i < times; i++) xp += (int)_exp.Invoke(null, new object[] { name });
                Earn(c, xp);
            }
            catch (Exception e) { Plugin.Instance?.Warn("EpicMMO experience while away: " + e.Message); }
        }

        /// <summary>A creature it fought or hunted while nobody was near (CatchUp): its prefab, no stars.</summary>
        public static void KilledAway(Humanoid c, GameObject prefab)
        {
            if (!On || c == null || prefab == null) return;
            try { Earn(c, KillXp(null, prefab.name + "(Clone)", 1)); }   // (EpicMMO's table is by a spawned creature's name)
            catch (Exception e) { Plugin.Instance?.Warn("EpicMMO experience while away: " + e.Message); }
        }

        // Experience earned and not yet saved: into its ZDO every 10 s or at a new level, as its skills are. Mining pays for every hit, and
        // each write sends its whole ZDO to every player near (docs/modding-pitfalls.md, "Keep a moving object's ZDO small").
        private class Held { public long Xp; public float Due, NextPick; }
        private static readonly Dictionary<Humanoid, Held> Unsaved = new Dictionary<Humanoid, Held>();

        private static Held Hold(Humanoid c)
        {
            if (Unsaved.TryGetValue(c, out Held held)) return held;
            foreach (Humanoid gone in new List<Humanoid>(Unsaved.Keys)) if (gone == null) Unsaved.Remove(gone);
            return Unsaved[c] = new Held();
        }

        private static void Earn(Humanoid c, int xp)
        {
            if (xp <= 0 || Companion.Zdo(c) == null || !c.GetComponent<ZNetView>().IsOwner()) return;
            long gained = (long)Math.Round(xp * F("rateExp", 1f) * Plugin.MmoExp.Value);
            if (gained <= 0L) return;
            long before = Xp(c);
            int was = LevelOf(before, out _, out _), now = LevelOf(before + gained, out _, out _);
            Held held = Hold(c);
            if (held.Xp == 0L) held.Due = Time.time + 10f;   // (saved 10 s after the first of it)
            held.Xp += gained;
            if (now > was) { Save(c); LevelUp(c, now); }
        }

        /// <summary>Its unsaved experience into its ZDO (only its owner's write counts: on another game it is dropped).</summary>
        private static void Save(Humanoid c)
        {
            if (!Unsaved.TryGetValue(c, out Held held)) return;
            Unsaved.Remove(c);
            ZDO z = Companion.Zdo(c);
            if (z == null || held.Xp <= 0L || !c.GetComponent<ZNetView>().IsOwner()) return;
            z.Set(XpKey, z.GetLong(XpKey, 0L) + held.Xp);   // (added to what is saved now: another game may have run it in between)
            Unsaved[c] = new Held { NextPick = held.NextPick };   // (keeps the pick timer; nothing held)
        }

        /// <summary>Every frame on the game that runs it: saves what it earned once 10 s have passed.</summary>
        public static void Tick(Humanoid c)
        {
            if (Unsaved.TryGetValue(c, out Held held) && held.Xp > 0L && Time.time >= held.Due) Save(c);
        }

        /// <summary>Unloading: what every companion earned goes into its save first.</summary>
        public static void Forget()
        {
            foreach (Humanoid c in new List<Humanoid>(Unsaved.Keys)) if (c != null) { try { Save(c); } catch { } }
            Unsaved.Clear();
        }

        private static void LevelUp(Humanoid c, int level)
        {
            Brain.Get(c)?.Remember(P("aicompanion_mmo_reached", level));
            if (level % 5 == 0) Journal.Add(c, P("aicompanion_mmo_reached", level));
            if (Player.m_localPlayer != null && Companion.Master(c) == Player.m_localPlayer)
                Plugin.Tell(P("aicompanion_mmo_levelup_tell", Companion.NameOf(c), level));
            Player proto = ZNetScene.instance?.GetPrefab("Player")?.GetComponent<Player>();   // (the game's skill level-up glow)
            proto?.m_skillLevelupEffects.Create(c.GetHeadPoint(), c.transform.rotation, c.transform);
            Plugin.Instance?.Note($"{Companion.NameOf(c)} reached EpicMMO level {level}");
        }

        /// <summary>A fallen companion loses part of its way to the next level (never a level), as EpicMMO's players do (LossExp).</summary>
        public static long AfterDeath(long total)
        {
            if (!Installed || total <= 0L || !B("lossExp", true)) return total;
            LevelOf(total, out long into, out _);
            float loss = Mathf.Clamp01(UnityEngine.Random.Range(F("minLossExp", 0.05f), F("maxLossExp", 0.25f)));
            return total - (long)(into * loss);
        }

        // ---- what its level does in a fight ------------------------------------------------------------------

        /// <summary>Its hit, as it leaves it (on its game): Strength adds to the physical part, Intellect to the magic part.</summary>
        public static void Boost(Character c, HitData hit)
        {
            Bonus b = Of(c);
            float p = 1f + b.Physical / 100f, m = 1f + b.Magic / 100f;
            hit.m_damage.m_blunt *= p; hit.m_damage.m_slash *= p; hit.m_damage.m_pierce *= p; hit.m_damage.m_chop *= p; hit.m_damage.m_pickaxe *= p;
            hit.m_damage.m_fire *= m; hit.m_damage.m_frost *= m; hit.m_damage.m_lightning *= m; hit.m_damage.m_poison *= m; hit.m_damage.m_spirit *= m;
        }

        /// <summary>A hit it takes (on its game, before its armour): Body takes off the physical part, Vigour the magic part.</summary>
        public static void Resist(Character c, HitData hit)
        {
            Bonus b = Of(c);
            float p = 1f - b.PhysicalArmor / 100f, m = 1f - b.MagicArmor / 100f;
            hit.m_damage.m_blunt *= p; hit.m_damage.m_slash *= p; hit.m_damage.m_pierce *= p; hit.m_damage.m_chop *= p; hit.m_damage.m_pickaxe *= p;
            hit.m_damage.m_fire *= m; hit.m_damage.m_frost *= m; hit.m_damage.m_lightning *= m; hit.m_damage.m_poison *= m; hit.m_damage.m_spirit *= m;
        }
    }

    // A companion's hit grows with its level (Strength, Intellect). Character.Damage runs on the attacker's game, before the hit is sent.
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class EpicMmo_Boost
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            if (hit == null || !EpicMmo.On) return;
            Character a = hit.GetAttacker();
            if (a == null || a == __instance || !Companion.Is(a) || !a.GetComponent<ZNetView>().IsOwner()) return;
            EpicMmo.Boost(a, hit);
        }
    }

    /// <summary>
    /// A creature hit by a companion: for EpicMMO's own prefix on Character.RPC_Damage the companion counts as tamed, so the kill goes to
    /// the game that runs the companion (its player's, when near) under EpicMMO's tame rules, and keeps its drops. Switched on first of all
    /// the prefixes there and off again at the last one, so the game's own code and every postfix see the companion as it is.
    /// </summary>
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class EpicMmo_CreditKill
    {
        private static readonly AccessTools.FieldRef<Character, bool> Tamed = AccessTools.FieldRefAccess<Character, bool>("m_tamed");
        private static readonly AccessTools.FieldRef<Character, float> TamedCheck = AccessTools.FieldRefAccess<Character, float>("m_lastTamedCheck");
        private static Character _as;
        private static float _check;

        [HarmonyPriority(Priority.First), HarmonyBefore(EpicMmo.PluginId)]
        private static void Prefix(Character __instance, HitData hit)
        {
            if (_as != null || hit == null || !EpicMmo.Installed || __instance.IsPlayer() || __instance.IsTamed() || Companion.Is(__instance)) return;
            Character a = hit.GetAttacker();
            if (a == null || a == __instance || !Companion.Is(a) || a.IsTamed()) return;
            _as = a;
            _check = TamedCheck(a);
            Tamed(a) = true;
            TamedCheck(a) = Time.time;   // (else a game that doesn't run it reads "not tamed" from its ZDO)
        }

        internal static void Restore()
        {
            if (ReferenceEquals(_as, null)) return;
            Character a = _as;
            _as = null;
            if (a == null) return;   // (destroyed in between)
            Tamed(a) = false;
            TamedCheck(a) = _check;
        }

        private static Exception Finalizer(Exception __exception) { Restore(); return __exception; }
    }

    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class EpicMmo_CreditKill_End
    {
        [HarmonyPriority(Priority.Last), HarmonyAfter(EpicMmo.PluginId)]
        private static void Prefix() => EpicMmo_CreditKill.Restore();
    }

    // ---- its work, for EpicMMO's non-combat experience: on the game that runs the thing, when it runs the companion too ----------------

    internal static class EpicMmo_Work
    {
        /// <summary>A hit that would pay a player (the right tool tier, a point of damage or more), by a companion this game runs.</summary>
        internal static Humanoid Worker(HitData hit, int minToolTier)
        {
            if (hit == null || !hit.CheckToolTier(minToolTier) || hit.GetTotalDamage() < 1f) return null;
            return hit.GetAttacker() is Humanoid h && Companion.Is(h) && h.GetComponent<ZNetView>().IsOwner() ? h : null;
        }

        /// <summary>This game runs it and it is still there (health left), before a hit.</summary>
        internal static bool Standing(Component thing, float health)
        {
            if (!EpicMmo.On) return false;
            ZNetView v = thing.GetComponent<ZNetView>();
            return v != null && v.IsValid() && v.IsOwner() && v.GetZDO().GetFloat(ZDOVars.s_health, health) > 0f;
        }

        internal static bool Gone(Component thing) { ZNetView v = thing.GetComponent<ZNetView>(); return v == null || !v.IsValid(); }
    }

    // A tree: once, when it falls.
    [HarmonyPatch(typeof(TreeBase), "RPC_Damage")]
    internal static class EpicMmo_Tree
    {
        private static void Prefix(TreeBase __instance, out bool __state) => __state = EpicMmo_Work.Standing(__instance, __instance.m_health);

        private static void Postfix(TreeBase __instance, HitData hit, bool __state)
        {
            if (__state && EpicMmo_Work.Gone(__instance) && EpicMmo_Work.Worker(hit, __instance.m_minToolTier) is Humanoid h) EpicMmo.Worked(h, __instance, EpicMmo.Work.Tree);
        }
    }

    // A log: every hit.
    [HarmonyPatch(typeof(TreeLog), "RPC_Damage")]
    internal static class EpicMmo_Log
    {
        private static void Prefix(TreeLog __instance, out bool __state) => __state = EpicMmo_Work.Standing(__instance, __instance.m_health);

        private static void Postfix(TreeLog __instance, HitData hit, bool __state)
        {
            if (__state && EpicMmo_Work.Worker(hit, __instance.m_minToolTier) is Humanoid h) EpicMmo.Worked(h, __instance, EpicMmo.Work.Tree);
        }
    }

    // A rock or an ore vein: every hit.
    [HarmonyPatch(typeof(MineRock), "RPC_Hit")]
    internal static class EpicMmo_Rock
    {
        private static void Prefix(MineRock __instance, out bool __state) => __state = EpicMmo_Work.Standing(__instance, 1f);

        private static void Postfix(MineRock __instance, HitData hit, bool __state)
        {
            if (__state && EpicMmo_Work.Worker(hit, __instance.m_minToolTier) is Humanoid h) EpicMmo.Worked(h, __instance, EpicMmo.Work.Mining);
        }
    }

    [HarmonyPatch(typeof(MineRock5), "RPC_Damage")]
    internal static class EpicMmo_Rock5
    {
        private static void Prefix(MineRock5 __instance, out bool __state) => __state = EpicMmo_Work.Standing(__instance, 1f);

        private static void Postfix(MineRock5 __instance, HitData hit, bool __state)
        {
            if (__state && EpicMmo_Work.Worker(hit, __instance.m_minToolTier) is Humanoid h) EpicMmo.Worked(h, __instance, EpicMmo.Work.Mining);
        }
    }

    // A stump, a small rock, a pot...: once, when it breaks.
    [HarmonyPatch(typeof(Destructible), "RPC_Damage")]
    internal static class EpicMmo_Destructible
    {
        private static readonly AccessTools.FieldRef<Destructible, bool> Destroyed = AccessTools.FieldRefAccess<Destructible, bool>("m_destroyed");

        private static void Prefix(Destructible __instance, out bool __state) => __state = EpicMmo_Work.Standing(__instance, __instance.m_health) && !Destroyed(__instance);

        private static void Postfix(Destructible __instance, HitData hit, bool __state)
        {
            if (__state && Destroyed(__instance) && EpicMmo_Work.Worker(hit, __instance.m_minToolTier) is Humanoid h) EpicMmo.Worked(h, __instance, EpicMmo.Work.Destructible);
        }
    }
}
