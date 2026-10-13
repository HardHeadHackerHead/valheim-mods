using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Jint;
using Jint.Native;
using UnityEngine;
using static Scripts.Api;

namespace Scripts
{
    /// <summary>
    /// The events added in the expansion (docs/scripting-expansion.md section 4). Each is patched by hand, one at a time, each in its own
    /// try: a game update that moves one method turns that one event into "not built yet" (a script that asks for it is told so) and the
    /// rest carry on. Nothing here has an attribute, so it is never part of the all-at-once patching, and the patches belong to the mod's
    /// Harmony id, so unloading the mod takes them all away. Every patch body is wrapped so a fault never reaches the game.
    /// </summary>
    internal static class NewEvents
    {
        private static bool _hooked;

        internal static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            var h = new Harmony(Core.Guid);
            Try(h, "hit", typeof(Character), "RPC_Damage", prefix: nameof(HitPrefix));
            Try(h, "pieceBuilt", typeof(Piece), nameof(Piece.SetCreator), postfix: nameof(PiecePostfix));
            Try(h, "itemPickup", typeof(Humanoid), nameof(Humanoid.Pickup), prefix: nameof(PickupPrefix), postfix: nameof(PickupPostfix));
            Try(h, "eat", typeof(Player), nameof(Player.EatFood), prefix: nameof(EatPrefix), postfix: nameof(EatPostfix));
            Try(h, "skillUp", typeof(Player), nameof(Player.OnSkillLevelup), postfix: nameof(SkillPostfix));
            Try(h, "raidStart", typeof(RandEventSystem), "SetRandomEvent", postfix: nameof(RaidPostfix));
            Try(h, "playerRespawn", typeof(Player), nameof(Player.OnRespawn), postfix: nameof(RespawnPostfix));
            Try(h, "tamed", typeof(Tameable), "Tame", postfix: nameof(TamedPostfix));
            Try(h, "treeFelled", typeof(TreeBase), "RPC_Damage", prefix: nameof(TreePrefix), postfix: nameof(TreePostfix));
            Try(h, "logBroken", typeof(TreeLog), "Destroy", prefix: nameof(LogPrefix), args: new[] { typeof(HitData), typeof(bool) });
            Events.Mark("raidEnd"); Events.Mark("weatherChange"); Events.Mark("command");   // (the last three are watched or routed, not patched)
        }

        private static void Try(Harmony h, string eventName, Type type, string method, string prefix = null, string postfix = null, Type[] args = null)
        {
            try
            {
                MethodInfo target = AccessTools.Method(type, method, args);
                if (target == null) { Core.Log.LogWarning($"Scripts: the event '{eventName}' is not hooked: {type.Name}.{method} was not found in this version of the game"); return; }
                MethodInfo pre = prefix != null ? AccessTools.Method(typeof(NewEvents), prefix) : null, post = postfix != null ? AccessTools.Method(typeof(NewEvents), postfix) : null;
                h.Patch(target, pre != null ? new HarmonyMethod(pre) : null, post != null ? new HarmonyMethod(post) : null);
                Events.Mark(eventName);
            }
            catch (Exception e) { Core.Log.LogWarning($"Scripts: the event '{eventName}' is not hooked: {e.Message}"); }
        }

        // ---- firing: from this game's own events, and from the ones a player's game forwards ----

        internal static void FirePickup(Who player, ItemInfo item) => Host.Fire("itemPickup", e => Obj(e, ("player", Handles.PlayerActor(ScriptOf(e), player)), ("item", ItemObject(e, item))));
        internal static void FireEat(Who player, ItemInfo item) => Host.Fire("eat", e => Obj(e, ("player", Handles.PlayerActor(ScriptOf(e), player)), ("item", ItemObject(e, item))));
        internal static void FireSkill(Who player, string name, int level) => Host.Fire("skillUp", e => Obj(e, ("player", Handles.PlayerActor(ScriptOf(e), player)), ("skill", Text(name)), ("level", Num(level))));
        internal static void FireRespawn(Who player) => Host.Fire("playerRespawn", e => Obj(e, ("player", Handles.PlayerActor(ScriptOf(e), player))));
        internal static void FireTamed(Character creature, Who tamer) => Host.Fire("tamed", e => { Script s = ScriptOf(e); return Obj(e, ("creature", Handles.Actor(s, creature)), ("player", Handles.PlayerActor(s, tamer))); });

        /// <summary>
        /// A piece a player's game says it built. The message usually arrives before the piece itself does (the world's own sync is
        /// slower), so it waits for the piece to show up here, up to five seconds, and says so in the log if it never does.
        /// </summary>
        internal static void FirePiece(Who player, string pieceId)
        {
            Piece piece = Pieces.Find(pieceId);
            if (piece != null) { FirePieceNow(player, piece); return; }
            if (Core.Plugin != null) Core.Plugin.StartCoroutine(WaitForPiece(player, pieceId));
        }

        private static System.Collections.IEnumerator WaitForPiece(Who player, string pieceId)
        {
            float until = UnityEngine.Time.unscaledTime + 5f;
            while (UnityEngine.Time.unscaledTime < until)
            {
                yield return new UnityEngine.WaitForSeconds(0.2f);
                Piece piece = Pieces.Find(pieceId);
                if (piece != null) { FirePieceNow(player, piece); yield break; }
            }
            Core.Log.LogInfo("Scripts (multiplayer): a piece " + (player != null ? player.Name : "a player") + " built never arrived here, so pieceBuilt was not fired for it");
        }

        private static void FirePieceNow(Who player, Piece piece)
        {
            if (player == null || piece == null) return;
            Host.Fire("pieceBuilt", e => { Script s = ScriptOf(e); return Obj(e, ("player", Handles.PlayerActor(s, player)), ("piece", Pieces.Handle(s, piece))); });
        }

        /// <summary>A tree came down (here, or on the player's game that owns it). `player` is who felled it, or null (another tree, a troll, fire).</summary>
        internal static void FireTree(Felled w, Who player) =>
            Host.Fire("treeFelled", e => Obj(e, ("player", player != null ? Handles.PlayerActor(ScriptOf(e), player) : JsValue.Null), ("tree", Text(w.Prefab)), ("kind", Text(w.Kind)), ("wood", Text(w.Wood)), ("pos", Vec(e, w.Pos))));

        /// <summary>A fallen log broke: into smaller logs (`splits`), or into the wood it gives.</summary>
        internal static void FireLog(Felled w, Who player) =>
            Host.Fire("logBroken", e => Obj(e, ("player", player != null ? Handles.PlayerActor(ScriptOf(e), player) : JsValue.Null), ("log", Text(w.Prefab)), ("kind", Text(w.Kind)), ("wood", Text(w.Wood)), ("splits", Bool(w.Splits)), ("pos", Vec(e, w.Pos))));

        /// <summary>A hit a player's game told us about: scripts see it, and cannot change it (the damage was settled on the game that owns the target).</summary>
        internal static void FireHit(Handles.Snap target, Handles.Snap attacker, Vector3 pos, string weapon, double damage) =>
            Host.Fire("hit", e => { Script s = ScriptOf(e); return Obj(e, ("attacker", Handles.ActorOrFrozen(s, attacker)), ("target", Handles.ActorOrFrozen(s, target)), ("pos", Vec(e, pos)), ("weapon", Text(weapon)), ("damage", Num(damage))); });

        // ---- values shared by the events ----

        private static JsValue ItemObject(Engine e, ItemInfo i) =>
            Obj(e, ("prefab", Text(i.Prefab)), ("name", Text(i.Name)), ("count", Num(i.Count)), ("quality", Num(i.Quality)));

        internal sealed class ItemInfo { public string Prefab = "", Name = ""; public int Count, Quality; }

        private static ItemInfo InfoOf(ItemDrop.ItemData item) => item == null ? null : new ItemInfo
        {
            Prefab = item.m_dropPrefab != null ? Utils.GetPrefabName(item.m_dropPrefab) : "", Name = Localization.instance.Localize(item.m_shared.m_name), Count = item.m_stack, Quality = item.m_quality,
        };

        private static Script ScriptOf(Engine e) => Host.Scripts.Find(s => ReferenceEquals(s.Engine, e));

        // ---- hit: before the damage is applied, so a script can change it ----

        private static void HitPrefix(Character __instance, HitData hit)
        {
            try
            {
                if (hit == null || __instance == null) return;
                ZNetView view = __instance.GetComponent<ZNetView>();
                if (view == null || !view.IsOwner()) return;   // (the owner's game decides the damage, so it is the one that tells the scripts)
                if (Mp.Wire.IsAgent)
                {
                    // a player's game: tell the server (a script there can only watch: the damage is settled here)
                    Character who = hit.GetAttacker();
                    if (hit.GetTotalDamage() > 0f) Mp.Agent.ForwardHit(__instance, who, hit.m_point, WeaponOf(who), hit.GetTotalDamage());
                    return;
                }
                bool any = false;
                foreach (Script s in Host.Scripts) if (s.State == RunState.Running && s.Handlers.ContainsKey("hit")) { any = true; break; }
                if (!any) return;
                float original = hit.GetTotalDamage();
                if (original <= 0f) return;
                Character attacker = hit.GetAttacker();
                foreach (Script s in Host.Scripts.ToList())
                {
                    if (s.State != RunState.Running || !s.Handlers.TryGetValue("hit", out List<JsValue> list) || s.Engine == null) continue;
                    Engine e = s.Engine;
                    JsObject arg = Obj(e, ("attacker", attacker != null ? Handles.Actor(s, attacker) : JsValue.Null), ("target", Handles.Actor(s, __instance)),
                        ("pos", Vec(e, hit.m_point)), ("weapon", Text(WeaponOf(attacker))),
                        ("damage", Num(original)));
                    foreach (JsValue fn in list.ToList()) { if (s.State != RunState.Running) break; Host.Call(s, fn, "hit", arg, Host.HitCallMs); }
                    JsValue d = arg.Get("damage");
                    if (!d.IsNumber() || Math.Abs(d.AsNumber() - original) < 0.001) continue;
                    // a script may change the damage only with the power to, and only inside the bounds of the API reference
                    if (!s.Manifest.Needs.Contains("combat") || !Allowed(s, "combat")) { Host.Refuse(s, "changing damage (combat not approved)"); continue; }
                    double wanted = d.AsNumber();
                    double most = __instance is Player ? Math.Min(original * 1.5, Math.Max(0.0, __instance.GetHealth() - 1f)) : original * 3.0;
                    wanted = Math.Max(0.0, Math.Min(most, wanted));
                    hit.m_damage.Modify((float)(wanted / original));
                    original = hit.GetTotalDamage();
                    Audit.Add(s, $"changed damage to {Handles.PrefabName(__instance)} to {wanted:0}");
                }
            }
            catch (Exception ex) { Core.Log.LogWarning("Scripts: the hit event failed: " + ex.Message); }
        }

        /// <summary>The prefab of what the attacker is holding (the hit itself doesn't say), or "".</summary>
        private static string WeaponOf(Character attacker)
        {
            ItemDrop.ItemData weapon = (attacker as Humanoid)?.GetCurrentWeapon();
            return weapon != null && weapon.m_dropPrefab != null ? Utils.GetPrefabName(weapon.m_dropPrefab) : "";
        }

        // ---- pieceBuilt ----

        private static void PiecePostfix(Piece __instance, long uid)
        {
            try
            {
                if (__instance == null || uid == 0L) return;
                Player player = Player.GetAllPlayers().FirstOrDefault(p => p.GetPlayerID() == uid);
                if (player == null) return;
                Piece piece = __instance;
                if (Mp.Wire.IsAgent) { Mp.Agent.ForwardPiece(player, piece); return; }
                FirePieceNow(Roster.Of(player), piece);
            }
            catch (Exception ex) { Core.Log.LogWarning("Scripts: the pieceBuilt event failed: " + ex.Message); }
        }

        // ---- itemPickup ----

        private static void PickupPrefix(Humanoid __instance, GameObject go, out ItemInfo __state)
        {
            __state = null;
            try
            {
                if (!(__instance is Player) || go == null) return;
                ItemDrop drop = go.GetComponent<ItemDrop>();
                if (drop != null) __state = InfoOf(drop.m_itemData);
            }
            catch (Exception) { }
        }

        private static void PickupPostfix(Humanoid __instance, bool __result, ItemInfo __state)
        {
            try
            {
                if (!__result || __state == null || !(__instance is Player player)) return;
                if (Mp.Wire.IsAgent) { Mp.Agent.ForwardItem("itemPickup", player, __state); return; }
                FirePickup(Roster.Of(player), __state);
            }
            catch (Exception ex) { Core.Log.LogWarning("Scripts: the itemPickup event failed: " + ex.Message); }
        }

        // ---- eat ----

        private static void EatPrefix(ItemDrop.ItemData item, out ItemInfo __state) { __state = null; try { __state = InfoOf(item); } catch (Exception) { } }

        private static void EatPostfix(Player __instance, bool __result, ItemInfo __state)
        {
            try
            {
                if (!__result || __state == null) return;
                Player player = __instance;
                if (Mp.Wire.IsAgent) { Mp.Agent.ForwardItem("eat", player, __state); return; }
                FireEat(Roster.Of(player), __state);
            }
            catch (Exception ex) { Core.Log.LogWarning("Scripts: the eat event failed: " + ex.Message); }
        }

        // ---- skillUp ----

        private static void SkillPostfix(Player __instance, Skills.SkillType skill, float level)
        {
            try
            {
                Player player = __instance;
                string name = skill.ToString();
                if (Mp.Wire.IsAgent) { Mp.Agent.ForwardSkill(player, name, (int)Math.Floor(level)); return; }
                FireSkill(Roster.Of(player), name, (int)Math.Floor(level));
            }
            catch (Exception ex) { Core.Log.LogWarning("Scripts: the skillUp event failed: " + ex.Message); }
        }

        // ---- raids ----

        private static string _raid;

        private static void RaidPostfix(RandomEvent ev)
        {
            try
            {
                if (ev != null)
                {
                    string name = ev.m_name;
                    _raid = name;
                    Host.Fire("raidStart", e => Obj(e, ("name", Text(name))));
                }
                else if (_raid != null)
                {
                    string name = _raid;
                    _raid = null;
                    Host.Fire("raidEnd", e => Obj(e, ("name", Text(name))));
                }
            }
            catch (Exception ex) { Core.Log.LogWarning("Scripts: the raid events failed: " + ex.Message); }
        }

        // ---- respawn ----

        private static void RespawnPostfix(Player __instance)
        {
            try
            {
                Player player = __instance;
                if (Mp.Wire.IsAgent) { Mp.Agent.ForwardRespawn(player); return; }
                FireRespawn(Roster.Of(player));
            }
            catch (Exception ex) { Core.Log.LogWarning("Scripts: the playerRespawn event failed: " + ex.Message); }
        }

        // ---- tamed ----

        private static void TamedPostfix(Tameable __instance)
        {
            try
            {
                Character creature = __instance.GetComponent<Character>();
                if (creature == null) return;
                Player tamer = Player.GetAllPlayers().OrderBy(p => Vector3.Distance(p.transform.position, creature.transform.position)).FirstOrDefault();
                if (tamer == null || Vector3.Distance(tamer.transform.position, creature.transform.position) > 30f) return;
                if (Mp.Wire.IsAgent) { Mp.Agent.ForwardTamed(creature, tamer); return; }
                FireTamed(creature, Roster.Of(tamer));
            }
            catch (Exception ex) { Core.Log.LogWarning("Scripts: the tamed event failed: " + ex.Message); }
        }
    
        // ---- trees and logs ----

        /// <summary>What the tree and log events tell, taken before the object is gone (a forwarded one is rebuilt from the message).</summary>
        internal sealed class Felled { public string Id = "", Prefab = "", Kind = "", Wood = ""; public bool Splits; public Vector3 Pos; }

        /// <summary>"beech", "birch", "fir", "oak", "pine", "ancient" (the swamp's trees), "yggdrasil" (the Mistlands' shoots), else the name's first word.</summary>
        internal static string KindOf(string prefab)
        {
            string n = (prefab ?? "").ToLowerInvariant();
            if (n.Contains("birch")) return "birch";
            if (n.Contains("beech")) return "beech";
            if (n.Contains("oak")) return "oak";
            if (n.Contains("pine")) return "pine";
            if (n.Contains("fir")) return "fir";
            if (n.Contains("swamp")) return "ancient";
            if (n.Contains("ygga")) return "yggdrasil";
            string first = n.Split('_')[0].TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            return first.Length > 0 ? first : n;
        }

        /// <summary>The wood a log gives: its own drop, or (a whole log drops nothing, it splits) its smaller logs' drop.</summary>
        private static string WoodOf(TreeLog log, int depth = 0)
        {
            if (log == null || depth > 3) return "";
            string own = FirstDrop(log.m_dropWhenDestroyed);
            if (own.Length > 0) return own;
            return log.m_subLogPrefab != null ? WoodOf(log.m_subLogPrefab.GetComponent<TreeLog>(), depth + 1) : "";
        }

        private static string FirstDrop(DropTable table)
        {
            if (table == null || table.m_drops == null) return "";
            foreach (DropTable.DropData d in table.m_drops) if (d.m_item != null) return Utils.GetPrefabName(d.m_item);
            return "";
        }

        private static Felled WoodInfo(MonoBehaviour thing, ZNetView view, TreeLog log, bool splits) => new Felled
        {
            Id = view.GetZDO().m_uid.ToString(), Prefab = Utils.GetPrefabName(thing.gameObject), Kind = KindOf(Utils.GetPrefabName(thing.gameObject)),
            Wood = WoodOf(log), Splits = splits, Pos = thing.transform.position,
        };

        private static bool Listening(string name)
        {
            if (Mp.Wire.IsAgent) return Mp.Agent.Wants(name);
            foreach (Script s in Host.Scripts) if (s.State == RunState.Running && s.Handlers.ContainsKey(name)) return true;
            return false;
        }

        // the tree's own damage RPC runs on its owner's game; the tree is felled when its health went from above zero to zero or below
        private static void TreePrefix(TreeBase __instance, out Felled __state)
        {
            __state = null;
            try
            {
                if (__instance == null || !Listening("treeFelled")) return;
                ZNetView view = __instance.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !view.IsOwner()) return;
                if (view.GetZDO().GetFloat(ZDOVars.s_health, __instance.m_health) <= 0f) return;
                __state = WoodInfo(__instance, view, __instance.m_logPrefab != null ? __instance.m_logPrefab.GetComponent<TreeLog>() : null, false);
            }
            catch (Exception) { }
        }

        private static void TreePostfix(TreeBase __instance, HitData hit, Felled __state)
        {
            try
            {
                if (__state == null) return;
                ZNetView view = __instance != null ? __instance.GetComponent<ZNetView>() : null;
                if (view != null && view.IsValid() && view.GetZDO().GetFloat(ZDOVars.s_health, 1f) > 0f) return;   // (still standing)
                Player player = hit != null ? hit.GetAttacker() as Player : null;
                if (Mp.Wire.IsAgent) { Mp.Agent.ForwardWood("treeFelled", __state, player); return; }
                FireTree(__state, Roster.Of(player));
            }
            catch (Exception ex) { Core.Log.LogWarning("Scripts: the treeFelled event failed: " + ex.Message); }
        }

        // the log's Destroy runs once, on its owner's game, when its health reaches zero
        private static void LogPrefix(TreeLog __instance, HitData hitData)
        {
            try
            {
                if (__instance == null || !Listening("logBroken")) return;
                ZNetView view = __instance.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) return;
                Felled w = WoodInfo(__instance, view, __instance, __instance.m_subLogPrefab != null);
                Player player = hitData != null ? hitData.GetAttacker() as Player : null;
                if (Mp.Wire.IsAgent) { Mp.Agent.ForwardWood("logBroken", w, player); return; }
                FireLog(w, Roster.Of(player));
            }
            catch (Exception ex) { Core.Log.LogWarning("Scripts: the logBroken event failed: " + ex.Message); }
        }
    }
}
