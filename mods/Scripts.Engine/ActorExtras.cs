using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using UnityEngine;
using static Scripts.Api;

namespace Scripts
{
    /// <summary>
    /// What an actor handle gained in the expansion (docs/scripting-expansion.md section 2): more to read (stamina, speed, skills, bag,
    /// what is worn...) and more to do (speak, status effects, a shove, taming, removing what the script spawned). Like the rest, each
    /// member asks the game when it is used and a handle that has gone does nothing.
    /// </summary>
    internal static class ActorExtras
    {
        internal static readonly AccessTools.FieldRef<Player, float> StaminaField = AccessTools.FieldRefAccess<Player, float>("m_stamina");
        internal static readonly AccessTools.FieldRef<Character, Rigidbody> BodyField = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");
        internal static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> Right = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_rightItem"),
            Left = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_leftItem"), Chest = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_chestItem"),
            Legs = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_legItem"), Head = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_helmetItem");

        private static readonly string[] DoNothing = { "talk", "banner", "setStamina", "applyEffect", "removeEffect", "launch", "tame", "remove", "setName", "skill" };

        internal static void AddFrozen(Script s, JsObject o)
        {
            foreach (string name in DoNothing) o.Set(name, Func(s, name, a => JsValue.Null));
            o.Set("inventory", Func(s, "inventory", a => Array(s.Engine, new JsValue[0])));
            o.Set("equipped", Func(s, "equipped", a => Obj(s.Engine, ("right", Text("")), ("left", Text("")), ("chest", Text("")), ("legs", Text("")), ("head", Text("")))));
            foreach (string name in new[] { "stamina", "maxStamina", "speed", "deaths" }) o.Set(name, Num(0));
            foreach (string name in new[] { "isSwimming", "isSitting", "isRunning", "isBlocking", "isInWater" }) o.Set(name, Bool(false));
            o.Set("faction", Text("wild"));
            o.Set("facing", Vec(s.Engine, Vector3.forward));
        }

        /// <summary>What a player on another game last reported, or null (this game's own player, a creature, or no report yet).</summary>
        private static Mp.PlayerState Reported(Character c) => c is Player p && Mp.Remote.IsRemote(p) ? Mp.Remote.StateOf(p) : null;

        private static string FactionName(Character c)
        {
            if (c.IsBoss()) return "boss";
            if (c.GetFaction() == Character.Faction.Players && c is Player) return "players";
            if (c.IsTamed()) return "tamed";
            return "wild";
        }

        internal static string PrefabOf(ItemDrop.ItemData item) => item == null || item.m_dropPrefab == null ? "" : Utils.GetPrefabName(item.m_dropPrefab);

        private static bool Mine(Script s, Character c, string what)
        {
            ZNetView view = c.GetComponent<ZNetView>();
            if (view != null && view.IsOwner()) return true;
            Host.Refuse(s, what + " only works on something this game owns, for now");
            return false;
        }

        private static bool OwnSpawn(Script s, Character c, string what)
        {
            if (!(c is Player) && Players.SpawnedBy(c) == s.Name) return true;
            Host.Refuse(s, what + " only works on a creature this script spawned");
            return false;
        }

        internal static void Add(Script s, JsObject o, Character first, string id)
        {
            Engine e = s.Engine;
            JsValue Live(Func<Character, JsValue> read) { Character now = Handles.Find(id); return now == null ? JsValue.Null : read(now); }
            Player AsPlayer() => Handles.Find(id) as Player;

            // ---- reads ----
            // (a player on another game: what their game last reported, up to a few seconds old; everything else is read live)
            Handles.Getter(e, o, "stamina", () => Live(x => Num(Reported(x)?.Stamina ?? (x is Player p ? p.GetStamina() : 0f))));
            Handles.Getter(e, o, "maxStamina", () => Live(x => Num(Reported(x)?.MaxStamina ?? (x is Player p ? p.GetMaxStamina() : 0f))));
            Handles.Getter(e, o, "isSwimming", () => Live(x => Bool(Reported(x)?.Swimming ?? x.IsSwimming())));
            Handles.Getter(e, o, "isSitting", () => Live(x => Bool(Reported(x)?.Sitting ?? x.IsSitting())));
            Handles.Getter(e, o, "isRunning", () => Live(x => Bool(Reported(x)?.Running ?? x.IsRunning())));
            Handles.Getter(e, o, "isBlocking", () => Live(x => Bool(Reported(x)?.Blocking ?? x.IsBlocking())));
            Handles.Getter(e, o, "isInWater", () => Live(x => Bool(x.InWater())));
            Handles.Getter(e, o, "faction", () => Live(x => Text(FactionName(x))));
            Handles.Getter(e, o, "facing", () => Live(x => Vec(e, x is Player ? x.GetLookDir() : x.transform.forward)));
            Handles.Getter(e, o, "speed", () => Live(x => Num(x.GetVelocity().magnitude)));

            // ---- doing ----
            Handles.Method(s, o, "talk", a =>
            {
                if (!Extras.Gate(s, "messages")) return JsValue.Null;
                Character x = Handles.Find(id);
                return x == null ? Bool(false) : Bool(Effects.Talk(s, x, Str(a, 0, 120)));
            });
            Handles.Method(s, o, "applyEffect", a =>
            {
                if (!Extras.Gate(s, "combat")) return JsValue.Null;
                Character x = Handles.Find(id);
                return x == null ? Bool(false) : Bool(ApplyEffect(s, x, Str(a, 0, 40), a.Length > 1 ? N(a, 1) : 30.0));
            });
            Handles.Method(s, o, "removeEffect", a =>
            {
                if (!Extras.Gate(s, "combat")) return JsValue.Null;
                Character x = Handles.Find(id);
                if (x == null) return Bool(false);
                string name = Str(a, 0, 40);
                if (!Lists.StatusEffects.ContainsKey(name)) { Host.Refuse(s, "'" + name + "' is not a status effect a script may handle"); return Bool(false); }
                if (x is Player rp && Mp.Remote.IsRemote(rp)) return Bool(Mp.Remote.RemoveEffect(s, rp, name));
                if (!x.GetComponent<ZNetView>().IsOwner()) return Bool(Mp.Remote.OnceOwned(s, x, "removing an effect", y => y.GetSEMan().RemoveStatusEffect(name.GetStableHashCode(), true)));
                return Bool(x.GetSEMan().RemoveStatusEffect(name.GetStableHashCode(), true));
            });
            Handles.Method(s, o, "launch", a =>
            {
                if (!Extras.Gate(s, "combat")) return JsValue.Null;
                Character x = Handles.Find(id);
                if (x == null) return Bool(false);
                if (x.IsBoss()) { Host.Refuse(s, "a shove on a boss"); return Bool(false); }
                Vector3 dir = Where(s, Arg(a, 0));
                if (x is Player rp && Mp.Remote.IsRemote(rp)) return Bool(Mp.Remote.Launch(s, rp, dir, N(a, 1)));
                if (!Players.Rate(s, "launch", 2.0, 2.0)) { Host.Refuse(s, "shoves more than twice a second"); return Bool(false); }
                float force = (float)Math.Max(0.0, Math.Min(15.0, N(a, 1)));
                if (dir.sqrMagnitude < 0.0001f || force <= 0f) return Bool(false);
                Func<Character, bool> shove = y =>
                {
                    Rigidbody body = BodyField(y);
                    if (body == null) return false;
                    body.AddForce(dir.normalized * force, ForceMode.VelocityChange);
                    Audit.Add(s, $"shoved {Handles.PrefabName(y)} at {force:0} m/s");
                    return true;
                };
                if (!x.GetComponent<ZNetView>().IsOwner()) return Bool(Mp.Remote.OnceOwned(s, x, "a shove", y => shove(y)));
                return Bool(shove(x));
            });

            if (first is Player)
            {
                Handles.Getter(e, o, "deaths", () => Live(x => Num(Events.Deaths(id))));
                Handles.Method(s, o, "skill", a =>
                {
                    Player p = AsPlayer();
                    if (p == null) return JsValue.Null;
                    if (!Enum.TryParse(Str(a, 0, 30), true, out Skills.SkillType type)) return JsValue.Null;
                    Mp.PlayerState reported = Reported(p);
                    if (Mp.Remote.IsRemote(p)) return reported != null && reported.Skills.TryGetValue(type.ToString(), out double level) ? Num(level) : JsValue.Null;
                    return Num(Math.Round(p.GetSkills().GetSkillLevel(type), 1));
                });
                Handles.Method(s, o, "inventory", a =>
                {
                    if (!s.Manifest.Needs.Contains("items") && !s.Manifest.Needs.Contains("takeItems")) Need(s, "items");
                    Player p = AsPlayer();
                    if (p != null && Mp.Remote.IsRemote(p))
                    {
                        Mp.PlayerState st = Mp.Remote.StateOf(p);
                        return Array(e, st == null ? new JsValue[0] : st.Inventory.Take(100).Select(i => (JsValue)Obj(e, ("prefab", Text(i.Prefab)), ("name", Text(i.Name)), ("count", Num(i.Count)), ("quality", Num(i.Quality)))).ToArray());
                    }
                    if (p == null || p != Player.m_localPlayer) return Array(e, new JsValue[0]);
                    return Array(e, p.GetInventory().GetAllItems().Take(100).Select(i => (JsValue)Obj(e, ("prefab", Text(PrefabOf(i))), ("name", Text(Localization.instance.Localize(i.m_shared.m_name))), ("count", Num(i.m_stack)), ("quality", Num(i.m_quality)))).ToArray());
                });
                Handles.Method(s, o, "equipped", a =>
                {
                    Player p = AsPlayer();
                    if (p == null) return JsValue.Null;
                    if (Mp.Remote.IsRemote(p))
                    {
                        string[] worn = Mp.Remote.StateOf(p)?.Equipped ?? new[] { "", "", "", "", "" };
                        return Obj(e, ("right", Text(worn[0])), ("left", Text(worn[1])), ("chest", Text(worn[2])), ("legs", Text(worn[3])), ("head", Text(worn[4])));
                    }
                    return Obj(e, ("right", Text(PrefabOf(Right(p)))), ("left", Text(PrefabOf(Left(p)))), ("chest", Text(PrefabOf(Chest(p)))), ("legs", Text(PrefabOf(Legs(p)))), ("head", Text(PrefabOf(Head(p)))));
                });
                Handles.Method(s, o, "banner", a =>
                {
                    if (!Extras.Gate(s, "messages")) return JsValue.Null;
                    Player p = AsPlayer();
                    return p == null ? Bool(false) : Bool(Effects.Banner(s, p, Str(a, 0, 60)));
                });
                Handles.Method(s, o, "setStamina", a =>
                {
                    if (!Extras.Gate(s, "combat")) return JsValue.Null;
                    Player p = AsPlayer();
                    if (p == null) return Bool(false);
                    if (Mp.Remote.IsRemote(p)) return Bool(Mp.Remote.Stamina(s, p, N(a, 0)));
                    if (!Mine(s, p, "setting stamina")) return Bool(false);
                    StaminaField(p) = Mathf.Clamp((float)N(a, 0), 0f, p.GetMaxStamina());
                    return Bool(true);
                });
            }
            else
            {
                Handles.Method(s, o, "tame", a =>
                {
                    if (!Extras.Gate(s, "spawn")) return JsValue.Null;
                    Character x = Handles.Find(id);
                    if (x == null || !OwnSpawn(s, x, "taming")) return Bool(false);
                    x.SetTamed(true);
                    Audit.Add(s, $"tamed {Handles.PrefabName(x)}");
                    return Bool(true);
                });
                Handles.Method(s, o, "remove", a =>
                {
                    if (!Extras.Gate(s, "spawn")) return JsValue.Null;
                    Character x = Handles.Find(id);
                    if (x == null || !OwnSpawn(s, x, "removing")) return Bool(false);
                    ZNetView view = x.GetComponent<ZNetView>();
                    if (view != null) view.ClaimOwnership();
                    ZNetScene.instance.Destroy(x.gameObject);
                    Audit.Add(s, $"removed {Handles.PrefabName(x)}");
                    return Bool(true);
                });
                Handles.Method(s, o, "setName", a =>
                {
                    if (!Extras.Gate(s, "messages")) return JsValue.Null;
                    Character x = Handles.Find(id);
                    if (x == null || !OwnSpawn(s, x, "naming")) return Bool(false);
                    x.m_name = Str(a, 0, 24);   // (this game's hover name; the players' games that have the mod are told to do the same)
                    Mp.Remote.FxName(x, x.m_name);
                    return Bool(true);
                });
            }
        }

        private static bool ApplyEffect(Script s, Character c, string name, double seconds)
        {
            if (!Lists.StatusEffects.TryGetValue(name, out int most)) { Host.Refuse(s, "'" + name + "' is not a status effect a script may hand out"); return false; }
            if (c is Player rp && Mp.Remote.IsRemote(rp)) return Mp.Remote.Effect(s, rp, name, seconds);
            if (!c.GetComponent<ZNetView>().IsOwner()) return Mp.Remote.OnceOwned(s, c, "an effect", y => ApplyEffect(s, y, name, seconds));
            if (ObjectDB.instance == null || ObjectDB.instance.GetStatusEffect(name.GetStableHashCode()) == null) { Host.Refuse(s, "this game has no status effect called '" + name + "'"); return false; }
            int per = Limits.StatusEffectsPerMinute;
            if (!Players.Rate(s, "effects", per / 60.0, per)) { Host.Refuse(s, "too many status effects a minute (" + per + " is the limit; the owner can change it in the Blockscript settings)"); return false; }
            seconds = Math.Max(5.0, Math.Min(most, seconds));
            StatusEffect se = c.GetSEMan().AddStatusEffect(name.GetStableHashCode(), true);
            if (se == null) return false;
            se.m_ttl = (float)seconds;
            Audit.Add(s, $"gave {name} for {seconds:0} s to {Handles.PrefabName(c)}");
            return true;
        }
    }
}
