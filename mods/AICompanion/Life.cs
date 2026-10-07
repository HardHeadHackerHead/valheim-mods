using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// Food, as a player's: a companion starts with little health and stamina (Companion, BaseHealth and BaseStamina: a player's 25 and 75)
    /// and up to three foods add theirs, fading as they burn down (the game's own curve), and give health back every 10 seconds. It eats
    /// from its bag when a slot is free or a food is half gone (never the same food twice), and takes food from its chests. Without food it
    /// does not heal, as a player does not. Its meals are kept in its ZDO ("prefab:seconds left,...") by the game that runs it.
    /// </summary>
    internal static class Food
    {
        private const string Key = "dhc_food", MaxStaminaKey = "dhc_maxst";

        internal class Meal { public ItemDrop.ItemData Item; public float Time; public float Fraction => Mathf.Clamp01(Time / Mathf.Max(1f, Item.m_shared.m_foodBurnTime)); }
        private class State { public List<Meal> Meals = new List<Meal>(); public float NextTick, NextRegen, NextEat; }
        private static readonly Dictionary<Humanoid, State> States = new Dictionary<Humanoid, State>();

        public static bool IsFood(ItemDrop.ItemData i) => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && i.m_shared.m_foodBurnTime > 0f && (i.m_shared.m_food > 0f || i.m_shared.m_foodStamina > 0f);

        private static State Of(Humanoid c)
        {
            if (States.TryGetValue(c, out State s)) return s;
            foreach (Humanoid gone in States.Keys.Where(k => k == null).ToList()) States.Remove(gone);
            States[c] = s = new State();
            foreach (string part in (Companion.Zdo(c)?.GetString(Key, "") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = part.Split(':');
                GameObject prefab = f.Length == 2 ? ObjectDB.instance?.GetItemPrefab(f[0]) : null;
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop != null && float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float t)) { ItemDrop.ItemData item = drop.m_itemData.Clone(); item.m_dropPrefab = drop.gameObject; s.Meals.Add(new Meal { Item = item, Time = t }); } // (a prefab's own item has no drop prefab)
            }
            return s;
        }

        /// <summary>Its meals right now (on the game that runs it; empty elsewhere).</summary>
        public static List<Meal> Meals(Humanoid c) => Of(c).Meals;

        private static float Curve(Meal m) => Mathf.Pow(m.Fraction, 0.3f); // the game's: a food stays strong for most of its time

        public static float MaxHealth(Humanoid c) => Plugin.BaseHealth.Value + Of(c).Meals.Sum(m => m.Item.m_shared.m_food * Curve(m));

        public static float MaxEitr(Humanoid c) => Of(c).Meals.Sum(m => m.Item.m_shared.m_foodEitr * Curve(m));

        public static float MaxStamina(Character c)
        {
            ZNetView v = c.GetComponent<ZNetView>();
            if (v != null && v.IsValid() && !v.IsOwner()) return v.GetZDO().GetFloat(MaxStaminaKey, Plugin.BaseStamina.Value);
            return c is Humanoid h ? Plugin.BaseStamina.Value + Of(h).Meals.Sum(m => m.Item.m_shared.m_foodStamina * Curve(m)) : Plugin.BaseStamina.Value;
        }

        /// <summary>Every frame on the game that runs it: burn, regenerate, eat.</summary>
        public static void Tick(Humanoid c, BrainState st)
        {
            State s = Of(c);
            if (Time.time >= s.NextTick)
            {
                s.NextTick = Time.time + 1f;
                foreach (Meal m in s.Meals) m.Time -= 1f * Game.m_foodRate;
                foreach (Meal done in s.Meals.Where(m => m.Time <= 0f).ToList()) { s.Meals.Remove(done); st.Remember($"finished its {Name(done.Item)}"); }
                c.SetMaxHealth(MaxHealth(c));
                ZDO z = Companion.Zdo(c);
                z.Set(MaxStaminaKey, MaxStamina(c));
                z.Set(Key, string.Join(",", s.Meals.Where(m => m.Item.m_dropPrefab != null).Select(m => Utils.GetPrefabName(m.Item.m_dropPrefab) + ":" + m.Time.ToString("0", CultureInfo.InvariantCulture))));
            }
            if (Time.time >= s.NextRegen)
            {
                s.NextRegen = Time.time + 10f;
                float regen = s.Meals.Sum(m => m.Item.m_shared.m_foodRegen);
                if (regen > 0f)
                {
                    float mult = 1f;
                    c.GetSEMan().ModifyHealthRegen(ref mult);
                    if (Rest.IsRested(c)) mult *= Rest.Mult;
                    c.Heal(regen * mult, true);
                }
            }
            if (Time.time >= s.NextEat) { s.NextEat = Time.time + 2f; TryEat(c, s, st); }
        }

        private static string Name(ItemDrop.ItemData i) => Localization.instance.Localize(i.m_shared.m_name);

        private static void TryEat(Humanoid c, State s, BrainState st)
        {
            Meal depleted = s.Meals.Where(m => m.Time < m.Item.m_shared.m_foodBurnTime / 2f).OrderBy(m => m.Time).FirstOrDefault();
            if (s.Meals.Count >= 3 && depleted == null) return; // full, as a player can be
            ItemDrop.ItemData best = c.GetInventory().GetAllItems().Where(IsFood)
                .Where(i => !s.Meals.Any(m => m.Item.m_shared.m_name == i.m_shared.m_name && m.Time >= m.Item.m_shared.m_foodBurnTime / 2f))
                .OrderByDescending(i => i.m_shared.m_food + i.m_shared.m_foodStamina).FirstOrDefault();
            if (best == null) return;
            Meal same = s.Meals.FirstOrDefault(m => m.Item.m_shared.m_name == best.m_shared.m_name);
            if (same != null) s.Meals.Remove(same);
            else if (s.Meals.Count >= 3) s.Meals.Remove(depleted);
            s.Meals.Add(new Meal { Item = best.Clone(), Time = best.m_shared.m_foodBurnTime });
            if (best.m_shared.m_consumeStatusEffect != null) c.GetSEMan().AddStatusEffect(best.m_shared.m_consumeStatusEffect, true);
            c.m_consumeItemEffects.Create(c.transform.position, Quaternion.identity);
            c.GetInventory().RemoveOneItem(best);
            st.Remember($"ate {Name(best)}");
            s.NextTick = 0f;
        }

        /// <summary>Time that passed while nobody was near (CatchUp): its foods burn down, and it eats from its bag as they do.</summary>
        public static void PassTime(Humanoid c, BrainState st, float seconds)
        {
            State s = Of(c);
            for (float t = 0f; t < seconds; t += 60f)
            {
                foreach (Meal m in s.Meals) m.Time -= 60f * Game.m_foodRate;
                s.Meals.RemoveAll(m => m.Time <= 0f);
                TryEat(c, s, st);
            }
            s.NextTick = 0f;
        }

        /// <summary>A fallen companion has eaten nothing (as a player after death).</summary>
        public static void Clear(Humanoid c) { if (States.TryGetValue(c, out State s)) s.Meals.Clear(); }

        public static void Forget() => States.Clear();
    }

    /// <summary>
    /// Skills, as a player's: each kind of weapon (and blocking, bows...) has a level from 0 to 100 that rises as its hits land, with the
    /// game's own steps and level curve, and makes it hit harder (40% of the weapon's damage at 0, 100% at 100, the player's formula).
    /// It loses 5% of every skill when it falls, as a player does. Kept in its ZDO ("skill:level:progress,...") and in its Profile.
    /// </summary>
    internal static class Skill
    {
        public const string Key = "dhc_skills";
        private class Level { public float Value, Progress; }
        private static readonly Dictionary<Humanoid, Dictionary<Skills.SkillType, Level>> Levels = new Dictionary<Humanoid, Dictionary<Skills.SkillType, Level>>();
        private static Dictionary<Skills.SkillType, Skills.SkillDef> _defs;

        private static Skills.SkillDef Def(Skills.SkillType type)
        {
            if (_defs == null)
            {
                _defs = new Dictionary<Skills.SkillType, Skills.SkillDef>();
                Skills skills = ZNetScene.instance?.GetPrefab("Player")?.GetComponent<Skills>();
                if (skills != null) foreach (Skills.SkillDef d in skills.m_skills) _defs[d.m_skill] = d;
            }
            return _defs.TryGetValue(type, out Skills.SkillDef def) ? def : null;
        }

        private static Dictionary<Skills.SkillType, Level> Of(Humanoid c)
        {
            if (Levels.TryGetValue(c, out var map)) return map;
            foreach (Humanoid gone in Levels.Keys.Where(k => k == null).ToList()) Levels.Remove(gone);
            Levels[c] = map = Parse(Companion.Zdo(c)?.GetString(Key, "") ?? "");
            return map;
        }

        private static Dictionary<Skills.SkillType, Level> Parse(string text)
        {
            var map = new Dictionary<Skills.SkillType, Level>();
            foreach (string part in text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = part.Split(':');
                if (f.Length == 3 && int.TryParse(f[0], out int id) && float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float lv) && float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float acc))
                    map[(Skills.SkillType)id] = new Level { Value = lv, Progress = acc };
            }
            return map;
        }

        private static string Write(Dictionary<Skills.SkillType, Level> map) =>
            string.Join(",", map.Select(kv => $"{(int)kv.Key}:{kv.Value.Value.ToString("0.##", CultureInfo.InvariantCulture)}:{kv.Value.Progress.ToString("0.###", CultureInfo.InvariantCulture)}"));

        public static float Get(Character c, Skills.SkillType type)
        {
            if (!(c is Humanoid h)) return 0f;
            ZNetView v = h.GetComponent<ZNetView>();
            Dictionary<Skills.SkillType, Level> map = v != null && v.IsValid() && !v.IsOwner() ? Parse(v.GetZDO().GetString(Key, "")) : Of(h);
            return map.TryGetValue(type, out Level l) ? l.Value : Plugin.StartingSkill.Value;
        }

        /// <summary>Every skill a player has (from the game's own list).</summary>
        public static IEnumerable<Skills.SkillType> Types()
        {
            Def(Skills.SkillType.Swords);
            return _defs.Keys.Where(t => t != Skills.SkillType.None && t != Skills.SkillType.All);
        }

        /// <summary>How far toward its next level (0 to 1), on the game that runs it (else 0).</summary>
        public static float Progress(Humanoid c, Skills.SkillType type)
        {
            if (!Of(c).TryGetValue(type, out Level l) || l.Value >= 100f) return 0f;
            float need = Mathf.Pow(Mathf.Floor(l.Value + 1f), 1.5f) * 0.5f + 0.5f;
            return Mathf.Clamp01(l.Progress / need);
        }

        public static IEnumerable<KeyValuePair<Skills.SkillType, float>> All(Humanoid c) =>
            Of(c).Where(kv => kv.Value.Value >= 1f).Select(kv => new KeyValuePair<Skills.SkillType, float>(kv.Key, kv.Value.Value)).OrderByDescending(kv => kv.Value);

        public static void Raise(Humanoid c, Skills.SkillType type, float factor)
        {
            if (type == Skills.SkillType.None) return;
            Dictionary<Skills.SkillType, Level> map = Of(c);
            if (!map.TryGetValue(type, out Level l)) map[type] = l = new Level { Value = Plugin.StartingSkill.Value };
            if (l.Value >= 100f) return;
            l.Progress += (Def(type)?.m_increseStep ?? 1f) * factor * Game.m_skillGainRate;
            float need = Mathf.Pow(Mathf.Floor(l.Value + 1f), 1.5f) * 0.5f + 0.5f; // the game's level curve
            if (l.Progress >= need)
            {
                l.Value = Mathf.Min(100f, l.Value + 1f);
                l.Progress = 0f;
                string skill = Localization.instance.Localize("$skill_" + type.ToString().ToLower());
                Brain.Get(c)?.Remember($"{skill} skill: {(int)l.Value}");
                if (Companion.Master(c) == Player.m_localPlayer) Plugin.Tell($"{Companion.NameOf(c)}'s {skill} skill: {(int)l.Value}");
            }
            Companion.Zdo(c)?.Set(Key, Write(map));
        }

        /// <summary>A player's death penalty: every skill 5% lower (applied to the profile it wakes from).</summary>
        public static string AfterDeath(string text)
        {
            Dictionary<Skills.SkillType, Level> map = Parse(text ?? "");
            foreach (Level l in map.Values) { l.Value = Mathf.Max(0f, l.Value - l.Value * 0.05f); l.Progress = 0f; }
            return Write(map);
        }

        public static void Forget() { Levels.Clear(); _defs = null; }
        public static void Forget(Humanoid c) => Levels.Remove(c);
    }

    /// <summary>
    /// The weather, as for a player (the game's own rules from Player.UpdateEnvStatusEffects): wet in the rain without a roof, sheltered
    /// under a roof with walls round it, warm by a fire, cold at night or in cold places away from a fire, freezing in the mountains without
    /// frost resistance (its gear's and its meads'). The effects are the game's own, so wet and cold slow its stamina and health coming back.
    /// </summary>
    internal static class Weather
    {
        private static readonly Dictionary<Humanoid, float> Next = new Dictionary<Humanoid, float>();

        public static void Tick(Humanoid c)
        {
            if (Next.TryGetValue(c, out float t) && Time.time < t) return;
            Next[c] = Time.time + 1f;
            SEMan se = c.GetSEMan();
            Vector3 pos = c.transform.position;
            bool fire = EffectArea.IsPointInsideArea(pos, EffectArea.Type.Heat, 1f) != null;
            Cover.GetCoverForPoint(c.GetCenterPoint(), out float cover, out bool underRoof);
            bool shelter = cover >= 0.8f && underRoof;
            bool freezingAir = EnvMan.IsFreezing(), coldAir = EnvMan.IsCold(), rain = EnvMan.IsWet();
            bool cozy = EffectArea.IsPointInsideArea(pos, EffectArea.Type.WarmCozyArea, 1f) != null;
            bool freezing = freezingAir && !fire && !shelter;
            bool cold = (coldAir && !fire) || (freezingAir && fire && !shelter) || (freezingAir && !fire && shelter);
            HitData.DamageModifier frost = Gear.Modifiers(c).GetModifier(HitData.DamageType.Frost);
            if (frost == HitData.DamageModifier.Resistant || frost == HitData.DamageModifier.VeryResistant || frost == HitData.DamageModifier.SlightlyResistant || cozy) { freezing = false; cold = false; }

            if (rain && !underRoof && !ShieldGenerator.IsInsideShield(pos)) se.AddStatusEffect(SEMan.s_statusEffectWet, true);
            Rest.Update(c, shelter, fire);
            if (shelter) se.AddStatusEffect(SEMan.s_statusEffectShelter); else se.RemoveStatusEffect(SEMan.s_statusEffectShelter);
            if (fire) se.AddStatusEffect(SEMan.s_statusEffectCampFire); else se.RemoveStatusEffect(SEMan.s_statusEffectCampFire);
            if (freezing) { if (!se.RemoveStatusEffect(SEMan.s_statusEffectCold, true)) se.AddStatusEffect(SEMan.s_statusEffectFreezing); }
            else if (cold) { if (!se.RemoveStatusEffect(SEMan.s_statusEffectFreezing, true)) se.AddStatusEffect(SEMan.s_statusEffectCold); }
            else { se.RemoveStatusEffect(SEMan.s_statusEffectCold); se.RemoveStatusEffect(SEMan.s_statusEffectFreezing); }
        }

        public static void Forget() => Next.Clear();
    }

    /// <summary>What its worn gear resists (a wolf cape: frost...), as a player's armour does, plus its status effects (meads).</summary>
    internal static class Gear
    {
        public static HitData.DamageModifiers Modifiers(Humanoid c)
        {
            var mods = new HitData.DamageModifiers();
            foreach (ItemDrop.ItemData item in Companion.Worn(c)) mods.Apply(item.m_shared.m_damageModifiers);
            c.GetSEMan().ApplyDamageMods(ref mods);
            return mods;
        }
    }

    /// <summary>
    /// Repairs, as a player makes them: when something it wears or works with is worn below half, and a station that can repair it is within
    /// 30 m (the one it is made at, at a high enough level: the game's rule), it walks there and repairs it, for free as the game does.
    /// Only out of a fight.
    /// </summary>
    internal static class Repair
    {
        private static readonly AccessTools.FieldRef<List<CraftingStation>> Stations = AccessTools.StaticFieldRefAccess<List<CraftingStation>>(AccessTools.Field(typeof(CraftingStation), "m_allStations"));

        private static bool Worn(ItemDrop.ItemData i) => i.m_shared.m_useDurability && i.m_shared.m_canBeReparied && i.GetMaxDurability() > 0f && i.m_durability < i.GetMaxDurability() * 0.5f;

        private static bool CanRepairAt(ItemDrop.ItemData item, CraftingStation station)
        {
            Recipe recipe = ObjectDB.instance?.GetRecipe(item);
            if (recipe == null || (recipe.m_craftingStation == null && recipe.m_repairStation == null)) return false;
            bool right = (recipe.m_repairStation != null && recipe.m_repairStation.m_name == station.m_name) || (recipe.m_craftingStation != null && recipe.m_craftingStation.m_name == station.m_name);
            return right && Mathf.Min(station.GetLevel(), 4) >= recipe.m_minStationLevel;
        }

        /// <summary>Catching up (CatchUp): everything worn that a station near home can repair, repaired. Returns how many.</summary>
        public static int All(Humanoid me, Vector3 center, float radius)
        {
            List<CraftingStation> near = Upgrades.StationsNear(center, radius + 10f);
            int n = 0;
            foreach (ItemDrop.ItemData item in me.GetInventory().GetAllItems().Where(i => i.m_shared.m_useDurability && i.m_shared.m_canBeReparied && i.GetMaxDurability() > 0f && i.m_durability < i.GetMaxDurability() * 0.9f))
                if (near.Any(s => CanRepairAt(item, s))) { item.m_durability = item.GetMaxDurability(); n++; }
            return n;
        }

        /// <summary>True while it is busy going to a station and repairing (the rest of its peaceful behaviour waits).</summary>
        public static bool Tick(BrainState st, Action<Vector3, float, bool> moveTo, Action stop)
        {
            Humanoid me = st.Body;
            if (st.RepairAt == null && Time.time >= st.NextRepairLook)
            {
                st.NextRepairLook = Time.time + 5f;
                var worn = me.GetInventory().GetAllItems().Where(Worn).Where(i => me.IsItemEquiped(i) || Work.IsTool(i)).ToList();
                if (worn.Count == 0) return false;
                List<CraftingStation> all = Stations() ?? new List<CraftingStation>();
                st.RepairAt = all.Where(s => s != null && Vector3.Distance(s.transform.position, me.transform.position) < 30f && worn.Any(i => CanRepairAt(i, s)))
                                 .OrderBy(s => Vector3.Distance(s.transform.position, me.transform.position)).FirstOrDefault();
                if (st.RepairAt == null) return false;
                st.RepairSince = Time.time;
            }
            if (st.RepairAt == null) return false;
            CraftingStation station = st.RepairAt;
            if (Time.time - st.RepairSince > 30f) { st.RepairAt = null; return false; } // could not get there
            float d = Vector3.Distance(station.transform.position, me.transform.position);
            if (d > 2.6f) { moveTo(station.transform.position, 1.8f, d > 8f); Brain.Status(st, $"going to the {Localization.instance.Localize(station.m_name)} to repair its gear"); return true; }
            stop();
            var fixedItems = new List<string>();
            foreach (ItemDrop.ItemData item in me.GetInventory().GetAllItems().Where(Worn).Where(i => CanRepairAt(i, station)))
            {
                item.m_durability = item.GetMaxDurability();
                fixedItems.Add(Localization.instance.Localize(item.m_shared.m_name));
            }
            if (fixedItems.Count > 0)
            {
                Companion.SaveBag(me);
                station.m_repairItemDoneEffects.Create(station.transform.position, Quaternion.identity);
                st.Remember($"repaired {string.Join(", ", fixedItems)} at the {Localization.instance.Localize(station.m_name)}");
                Plugin.Instance?.Note($"{Companion.NameOf(me)} repaired {string.Join(", ", fixedItems)}");
            }
            st.RepairAt = null;
            return false;
        }
    }

    /// <summary>
    /// Riding along: when its player boards a boat (on deck, steering or sitting), a companion following it gets on too, takes a free seat
    /// (sitting as a player does) or a spot on deck, and rides there until its player gets off; then it steps off beside it. The game has no
    /// seat for anyone but players, so it is held in place on the moving boat each frame.
    /// </summary>
    internal static class Ride
    {
        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation> Anim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");
        private static readonly Dictionary<Chair, Humanoid> Taken = new Dictionary<Chair, Humanoid>();

        public static Ship ShipOf(Player p) => p == null ? null : p.GetStandingOnShip() ?? p.GetControlledShip() ?? (p == Player.m_localPlayer ? Ship.GetLocalShip() : null);

        /// <summary>True while it rides (the rest of its behaviour waits).</summary>
        public static bool Tick(BrainState st, Player master)
        {
            Humanoid me = st.Body;
            Ship ship = Companion.OrderOf(me) == Order.Follow ? ShipOf(master) : null;
            if (ship == null)
            {
                if (st.Riding == null) return false;
                if (Time.time - st.RideLastSeen < 1.5f) { Hold(st); return true; } // a moment's grace (stepping about on deck)
                Off(st, master);
                return false;
            }
            st.RideLastSeen = Time.time;
            if (st.Riding != ship) Board(st, ship, master);
            Hold(st);
            return true;
        }

        private static void Board(BrainState st, Ship ship, Player master)
        {
            Humanoid me = st.Body;
            if (st.Riding != null) Off(st, null);
            Chair seat = ship.GetComponentsInChildren<Chair>().Where(ch => ch.m_inShip && !ch.IsInUse() && (!Taken.TryGetValue(ch, out Humanoid who) || who == null || who == me))
                             .OrderBy(ch => Vector3.Distance(ch.transform.position, master.transform.position)).FirstOrDefault();
            st.Riding = ship;
            st.Seat = seat;
            st.DeckSpot = ship.transform.InverseTransformPoint(master.transform.position) + new Vector3(-0.8f, 0f, -0.8f);
            if (seat != null) { Taken[seat] = me; Anim(me)?.SetBool(seat.m_attachAnimation, true); }
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.linearVelocity = Vector3.zero; body.isKinematic = true; }
            st.Ai.StopMoving();
            st.Remember("boarded the boat");
            Plugin.Instance?.Note($"{Companion.NameOf(me)} boarded {Utils.GetPrefabName(ship.gameObject)}{(seat != null ? " and sat down" : "")}");
        }

        private static void Hold(BrainState st)
        {
            Humanoid me = st.Body;
            if (st.Riding == null) return;
            Transform t = st.Riding.transform;
            Vector3 pos = st.Seat != null ? st.Seat.m_attachPoint.position : t.TransformPoint(st.DeckSpot);
            Quaternion rot = st.Seat != null ? st.Seat.m_attachPoint.rotation : Quaternion.LookRotation(Vector3.ProjectOnPlane(t.forward, Vector3.up), Vector3.up);
            me.transform.SetPositionAndRotation(pos, rot);
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.position = pos; body.rotation = rot; body.linearVelocity = Vector3.zero; }
            Brain.Status(st, "riding along");
        }

        private static void Off(BrainState st, Player master)
        {
            Humanoid me = st.Body;
            if (st.Seat != null) { Anim(me)?.SetBool(st.Seat.m_attachAnimation, false); Taken.Remove(st.Seat); }
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) body.isKinematic = false;
            st.Riding = null;
            st.Seat = null;
            if (master != null && master.IsOnGround()) Brain.TeleportBehind(me, master, "stepped off the boat beside");
        }

        public static void Forget()
        {
            foreach (var kv in Taken) if (kv.Value != null) { Rigidbody b = kv.Value.GetComponent<Rigidbody>(); if (b != null) b.isKinematic = false; }
            Taken.Clear();
        }
    }

    // ---- the game's skill questions, answered for companions ----

    [HarmonyPatch(typeof(Character), nameof(Character.RaiseSkill))]
    internal static class Character_RaiseSkill
    {
        private static bool Prefix(Character __instance, Skills.SkillType skill, float value)
        {
            if (!(__instance is Humanoid h) || !Companion.Is(h)) return true;
            if (h.GetComponent<ZNetView>().IsOwner()) Skill.Raise(h, skill, value);
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetSkillFactor))]
    internal static class Character_GetSkillFactor
    {
        private static void Postfix(Character __instance, Skills.SkillType skill, ref float __result) { if (Companion.Is(__instance)) __result = Skill.Get(__instance, skill) / 100f; }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetRandomSkillFactor))]
    internal static class Character_GetRandomSkillFactor
    {
        private static bool Prefix(Character __instance, Skills.SkillType skill, ref float __result)
        {
            if (!Companion.Is(__instance)) return true;
            float mid = Mathf.Lerp(0.4f, 1f, Skill.Get(__instance, skill) / 100f); // the player's formula (Skills.GetRandomSkillFactor)
            __result = Mathf.Lerp(Mathf.Clamp01(mid - 0.15f), Mathf.Clamp01(mid + 0.15f), UnityEngine.Random.value);
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetSkillLevel))]
    internal static class Character_GetSkillLevel
    {
        private static void Postfix(Character __instance, Skills.SkillType skillType, ref float __result) { if (Companion.Is(__instance)) __result = Skill.Get(__instance, skillType); }
    }

    // Its armour's resistances (fire, frost, poison...) protect it, as a player's do.
    [HarmonyPatch(typeof(Character), "ApplyArmorDamageMods")]
    internal static class Character_ApplyArmorDamageMods
    {
        private static void Postfix(Character __instance, ref HitData.DamageModifiers mods)
        {
            if (!(__instance is Humanoid h) || !Companion.Is(h)) return;
            foreach (ItemDrop.ItemData item in Companion.Worn(h)) mods.Apply(item.m_shared.m_damageModifiers);
        }
    }
}
