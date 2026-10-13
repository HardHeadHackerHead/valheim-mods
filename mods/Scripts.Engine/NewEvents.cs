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
            Events.Mark("raidEnd"); Events.Mark("weatherChange"); Events.Mark("command");   // (the last three are watched or routed, not patched)
        }

        private static void Try(Harmony h, string eventName, Type type, string method, string prefix = null, string postfix = null)
        {
            try
            {
                MethodInfo target = AccessTools.Method(type, method);
                if (target == null) { Core.Log.LogWarning($"Scripts: the event '{eventName}' is not hooked: {type.Name}.{method} was not found in this version of the game"); return; }
                MethodInfo pre = prefix != null ? AccessTools.Method(typeof(NewEvents), prefix) : null, post = postfix != null ? AccessTools.Method(typeof(NewEvents), postfix) : null;
                h.Patch(target, pre != null ? new HarmonyMethod(pre) : null, post != null ? new HarmonyMethod(post) : null);
                Events.Mark(eventName);
            }
            catch (Exception e) { Core.Log.LogWarning($"Scripts: the event '{eventName}' is not hooked: {e.Message}"); }
        }

        // ---- firing: from this game's own events, and from the ones a player's game forwards ----

        internal static void FirePickup(Player player, ItemInfo item) => Host.Fire("itemPickup", e => Obj(e, ("player", Handles.Actor(ScriptOf(e), player)), ("item", ItemObject(e, item))));
        internal static void FireEat(Player player, ItemInfo item) => Host.Fire("eat", e => Obj(e, ("player", Handles.Actor(ScriptOf(e), player)), ("item", ItemObject(e, item))));
        internal static void FireSkill(Player player, string name, int level) => Host.Fire("skillUp", e => Obj(e, ("player", Handles.Actor(ScriptOf(e), player)), ("skill", Text(name)), ("level", Num(level))));
        internal static void FireRespawn(Player player) => Host.Fire("playerRespawn", e => Obj(e, ("player", Handles.Actor(ScriptOf(e), player))));
        internal static void FireTamed(Character creature, Player tamer) => Host.Fire("tamed", e => { Script s = ScriptOf(e); return Obj(e, ("creature", Handles.Actor(s, creature)), ("player", Handles.Actor(s, tamer))); });

        /// <summary>
        /// A piece a player's game says it built. The message usually arrives before the piece itself does (the world's own sync is
        /// slower), so it waits for the piece to show up here, up to five seconds, and says so in the log if it never does.
        /// </summary>
        internal static void FirePiece(Player player, string pieceId)
        {
            Piece piece = Pieces.Find(pieceId);
            if (piece != null) { FirePieceNow(player, piece); return; }
            if (Core.Plugin != null) Core.Plugin.StartCoroutine(WaitForPiece(player, pieceId));
        }

        private static System.Collections.IEnumerator WaitForPiece(Player player, string pieceId)
        {
            float until = UnityEngine.Time.unscaledTime + 5f;
            while (UnityEngine.Time.unscaledTime < until)
            {
                yield return new UnityEngine.WaitForSeconds(0.2f);
                Piece piece = Pieces.Find(pieceId);
                if (piece != null) { FirePieceNow(player, piece); yield break; }
            }
            Core.Log.LogInfo("Scripts (multiplayer): a piece " + (player != null ? player.GetPlayerName() : "a player") + " built never arrived here, so pieceBuilt was not fired for it");
        }

        private static void FirePieceNow(Player player, Piece piece)
        {
            if (player == null || piece == null) return;
            Host.Fire("pieceBuilt", e => { Script s = ScriptOf(e); return Obj(e, ("player", Handles.Actor(s, player)), ("piece", Pieces.Handle(s, piece))); });
        }

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
                Host.Fire("pieceBuilt", e => { Script s = ScriptOf(e); return Obj(e, ("player", Handles.Actor(s, player)), ("piece", Pieces.Handle(s, piece))); });
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
                FirePickup(player, __state);
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
                FireEat(player, __state);
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
                FireSkill(player, name, (int)Math.Floor(level));
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
                FireRespawn(player);
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
                FireTamed(creature, tamer);
            }
            catch (Exception ex) { Core.Log.LogWarning("Scripts: the tamed event failed: " + ex.Message); }
        }
    }
}
