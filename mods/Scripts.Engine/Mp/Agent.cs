using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Scripts.Mp
{
    /// <summary>
    /// A player's own game on a server that runs scripts (docs/scripting-multiplayer.md): it does what the server asks of its own character
    /// after checking it with its own lists and the player's own settings (ActEngine), tells the server what only this game can see (a report
    /// of the bag, skills and so on every two seconds when it changed; the events of things this game owns), and shows what the server
    /// asks every game to show (weather, effects, shakes, banners, speech bubbles) when the player allows it. It is silent unless the
    /// server answered hello, so a server without the mod, or single player, never sees any of this.
    /// </summary>
    internal static class Agent
    {
        private static HashSet<string> _listen = new HashSet<string>();
        private static readonly Bucket ActRate = new Bucket(30.0, 60.0), FxRate = new Bucket(60.0, 120.0), EffectRate = new Bucket(20.0, 40.0), EventRate = new Bucket(40.0, 80.0);
        private static float _nextReport;
        private static string _lastReport = "";
        private static float _lastReportAt;
        private static double _envUntil, _windUntil;
        private static bool _envSet, _windSet;
        private static long _eventNumber;

        // ================================================================ from the server

        internal static void OnWelcome(Frame f)
        {
            SetListen(f.D["listen"] as JArray);
            if (f.D["env"] is JObject e) ApplyEnv(e["name"] != null ? (string)e["name"] : "", e["s"] != null ? (double)e["s"] : 0.0);
            if (f.D["wind"] is JObject w) ApplyWind(w["deg"] != null ? (double)w["deg"] : 0.0, w["str"] != null ? (double)w["str"] : 0.0, w["s"] != null ? (double)w["s"] : 0.0);
            _lastReport = ""; _nextReport = 0f;
        }

        internal static void OnListen(Frame f) { SetListen(f.D["events"] as JArray); }

        private static void SetListen(JArray names)
        {
            var set = new HashSet<string>();
            if (names != null) foreach (JToken t in names.Take(30)) if (t.Type == JTokenType.String && EventGate.Names.Contains((string)t)) set.Add((string)t);
            _listen = set;
        }

        /// <summary>The server asked for something to be done to this game's character. Checked, done, answered.</summary>
        internal static void OnAct(Frame f)
        {
            Frame done;
            try
            {
                if (!ActRate.Take(Time.timeAsDouble)) done = new Frame("done", f.I, new JObject { ["ok"] = false, ["n"] = 0, ["why"] = "too many requests" });
                else if (Player.m_localPlayer == null) done = new Frame("done", f.I, new JObject { ["ok"] = false, ["n"] = 0, ["why"] = "they have no character in the world right now" });
                else done = ActEngine.Handle(new UnityGame(Player.m_localPlayer), Limits.Policy(), f);
            }
            catch (Exception e) { done = new Frame("done", f.I, new JObject { ["ok"] = false, ["n"] = 0, ["why"] = "failed: " + Validate.Text(e.Message, 60) }); }
            Wire.SendToServer(done);
        }

        /// <summary>Something every game shows. All of it is only for the eyes and ears, and all of it is the player's to turn off.</summary>
        internal static void OnFx(Frame f)
        {
            try
            {
                if (!Limits.MaySee || !FxRate.Take(Time.timeAsDouble)) return;
                switch (f.S("op"))
                {
                    case "env": ApplyEnv(f.S("name"), f.N("s")); break;
                    case "wind": ApplyWind(f.N("deg"), f.N("str"), f.N("s")); break;
                    case "effect": case "sound": PlayEffect(f); break;
                    case "shake":
                        {
                            Verdict v = Validate.Shake(f.N("r"), f.N("st"));
                            if (v.Ok && GameCamera.instance != null) GameCamera.instance.AddShake(new Vector3((float)f.N("x"), (float)f.N("y"), (float)f.N("z")), (float)Validate.Range(f.N("r")), (float)v.N, false);
                            break;
                        }
                    case "banner":
                        {
                            string text = Validate.Text(f.S("text"), 60);
                            if (Validate.Banner(text).Ok && MessageHud.instance != null) MessageHud.instance.ShowBiomeFoundMsg(text, true);
                            break;
                        }
                    case "talk":
                        {
                            Character c = Handles.Find(f.S("id"));
                            if (c != null && Chat.instance != null) Chat.instance.SetNpcText(c.gameObject, Vector3.up * 2.2f, 30f, 6f, Validate.Text(f.S("name"), 30), Validate.Text(f.S("text"), 120), false);
                            break;
                        }
                    case "name":
                        {
                            Character c = Handles.Find(f.S("id"));
                            if (c != null && !(c is Player)) c.m_name = Validate.Text(f.S("text"), 24);
                            break;
                        }
                }
            }
            catch (Exception e) { Core.Log.LogWarning("Scripts (multiplayer): something the server showed could not be shown: " + e.Message); }
        }

        private static void ApplyEnv(string friendly, double seconds)
        {
            Verdict v = Validate.Env(friendly, seconds);
            if (!v.Ok || EnvMan.instance == null) return;
            EnvMan.instance.SetForceEnvironment(Validate.EnvOf(friendly));
            _envSet = true; _envUntil = Time.timeAsDouble + v.N;
        }

        private static void ApplyWind(double degrees, double strength, double seconds)
        {
            Verdict v = Validate.Wind(degrees, strength, seconds);
            if (!v.Ok || EnvMan.instance == null) return;
            EnvMan.instance.SetDebugWind((float)(((degrees % 360.0) + 360.0) % 360.0), (float)Math.Max(0.0, Math.Min(1.0, strength)));
            _windSet = true; _windUntil = Time.timeAsDouble + v.N;
        }

        private static void PlayEffect(Frame f)
        {
            bool sound = f.S("op") == "sound";
            string name = f.S("name");
            if (!Validate.Fx(name, sound).Ok || !EffectRate.Take(Time.timeAsDouble)) return;
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            if (prefab == null || Effects.Unsafe(prefab, name).Length > 0) return;   // (the same check of what the prefab carries as on the server: never anything that could do more than show or play)
            var pos = new Vector3((float)f.N("x"), (float)f.N("y"), (float)f.N("z"));
            if (Player.m_localPlayer == null || Vector3.Distance(Player.m_localPlayer.transform.position, pos) > 110f) return;
            Effects.SpawnLocal(prefab, pos, (float)Validate.Scale(f.N("sc", 1.0)));
        }

        /// <summary>Left the server (or the world): the weather and wind the server forced end, and nothing is forwarded any more.</summary>
        internal static void Leave()
        {
            _listen = new HashSet<string>();
            try
            {
                if (EnvMan.instance != null)
                {
                    if (_envSet) EnvMan.instance.SetForceEnvironment("");
                    if (_windSet) EnvMan.instance.ResetDebugWind();
                }
            }
            catch (Exception) { }
            _envSet = _windSet = false; _envUntil = _windUntil = 0.0;
            _lastReport = "";
        }

        // ================================================================ to the server

        /// <summary>Every frame (the plugin's Update): puts the weather back when the server's time is up, and sends the report.</summary>
        internal static void Tick()
        {
            if (!Wire.IsAgent) return;
            double now = Time.timeAsDouble;
            if (_envSet && now >= _envUntil) { _envSet = false; if (EnvMan.instance != null) EnvMan.instance.SetForceEnvironment(""); }
            if (_windSet && now >= _windUntil) { _windSet = false; if (EnvMan.instance != null) EnvMan.instance.ResetDebugWind(); }
            if (Time.unscaledTime < _nextReport) return;
            _nextReport = Time.unscaledTime + 2f;
            Player p = Player.m_localPlayer;
            if (p == null) return;
            try
            {
                string report = BuildReport(p).ToString(Newtonsoft.Json.Formatting.None);
                if (report == _lastReport && Time.unscaledTime - _lastReportAt < 10f) return;
                if (report.Length > 8192) { Core.Log.LogWarning("Scripts (multiplayer): the report of this player is over 8 KB and was not sent"); return; }
                _lastReport = report; _lastReportAt = Time.unscaledTime;
                Wire.SendToServer(new Frame("state", 0, JObject.Parse(report)));
            }
            catch (Exception e) { Core.Log.LogWarning("Scripts (multiplayer): the report could not be made: " + e.Message); }
        }

        private static JObject BuildReport(Player p)
        {
            var inv = new JArray();
            foreach (ItemDrop.ItemData i in p.GetInventory().GetAllItems().Take(StateCache.MaxItems))
                inv.Add(new JArray(ActorExtras.PrefabOf(i), i.m_stack, i.m_quality, Localization.instance.Localize(i.m_shared.m_name)));
            var skills = new JObject();
            foreach (Skills.SkillType type in Enum.GetValues(typeof(Skills.SkillType)))
            {
                if (type == Skills.SkillType.None || type == Skills.SkillType.All) continue;
                skills[type.ToString()] = Math.Round(p.GetSkills().GetSkillLevel(type), 1);
            }
            int flags = (p.IsSwimming() ? 1 : 0) | (p.IsSitting() ? 2 : 0) | (p.IsRunning() ? 4 : 0) | (p.IsBlocking() ? 8 : 0);
            var d = new JObject
            {
                ["inv"] = inv,
                ["eq"] = new JArray(ActorExtras.PrefabOf(ActorExtras.Right(p)), ActorExtras.PrefabOf(ActorExtras.Left(p)), ActorExtras.PrefabOf(ActorExtras.Chest(p)), ActorExtras.PrefabOf(ActorExtras.Legs(p)), ActorExtras.PrefabOf(ActorExtras.Head(p))),
                ["sk"] = skills, ["de"] = Events.Deaths(Handles.Id(p)), ["st"] = new JArray(Math.Round(p.GetStamina(), 1), Math.Round(p.GetMaxStamina(), 1)), ["fl"] = flags,
            };
            if (Physics.Raycast(p.GetEyePoint(), p.GetLookDir(), out RaycastHit hit, 100f, Extras.GroundMask)) d["lk"] = new JArray(Math.Round(hit.point.x), Math.Round(hit.point.y), Math.Round(hit.point.z));
            return d;
        }

        // ================================================================ events only this game can see

        private static JObject SnapJson(Handles.Snap s) => s == null ? null : new JObject
        {
            ["id"] = s.Id, ["pf"] = s.Prefab, ["nm"] = s.Name, ["x"] = s.Pos.x, ["y"] = s.Pos.y, ["z"] = s.Pos.z, ["mhp"] = s.MaxHp, ["lv"] = s.Level, ["pl"] = s.IsPlayer, ["bs"] = s.IsBoss, ["tm"] = s.IsTamed, ["by"] = s.SpawnedBy ?? "",
        };

        private static bool Wants(string name) => Wire.IsAgent && _listen.Contains(name);

        private static void Send(JObject d)
        {
            if (!EventRate.Take(Time.timeAsDouble)) return;
            Wire.SendToServer(new Frame("event", ++_eventNumber, d));
        }

        private static bool Owned(Character c) { ZNetView v = c != null ? c.GetComponent<ZNetView>() : null; return v != null && v.IsValid() && v.IsOwner(); }

        /// <summary>A creature or player this game owns died: the server cannot see it (most creatures near a player belong to that player's game).</summary>
        internal static void ForwardDeath(Character victim, Handles.Snap v, Handles.Snap k, Vector3 pos, bool boss, string cause)
        {
            if (!Wants("kill") || !Owned(victim)) return;
            Send(new JObject { ["e"] = "kill", ["id"] = v.Id, ["sub"] = v.Id, ["v"] = SnapJson(v), ["k"] = SnapJson(k), ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z, ["boss"] = boss, ["cause"] = cause });
        }

        internal static void ForwardRespawn(Player p)
        {
            if (p != Player.m_localPlayer || !Wants("playerRespawn")) return;
            Send(new JObject { ["e"] = "playerRespawn", ["id"] = "r" + Handles.Id(p) + "@" + (int)Time.time, ["sub"] = Handles.Id(p) });
        }

        internal static void ForwardItem(string name, Player p, NewEvents.ItemInfo item)
        {
            if (p != Player.m_localPlayer || !Wants(name) || item == null) return;
            Send(new JObject { ["e"] = name, ["sub"] = Handles.Id(p), ["item"] = new JObject { ["pf"] = item.Prefab, ["nm"] = item.Name, ["n"] = item.Count, ["q"] = item.Quality } });
        }

        internal static void ForwardSkill(Player p, string skill, int level)
        {
            if (p != Player.m_localPlayer || !Wants("skillUp")) return;
            Send(new JObject { ["e"] = "skillUp", ["sub"] = Handles.Id(p), ["skill"] = skill, ["level"] = level });
        }

        internal static void ForwardPiece(Player p, Piece piece)
        {
            if (p != Player.m_localPlayer || !Wants("pieceBuilt")) return;
            ZNetView view = piece != null ? piece.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid()) return;
            Send(new JObject { ["e"] = "pieceBuilt", ["sub"] = Handles.Id(p), ["piece"] = view.GetZDO().m_uid.ToString() });
        }

        internal static void ForwardTamed(Character creature, Player tamer)
        {
            if (!Wants("tamed") || !Owned(creature) || tamer == null) return;
            Send(new JObject { ["e"] = "tamed", ["id"] = "t" + Handles.Id(creature), ["sub"] = Handles.Id(creature), ["by"] = Handles.Id(tamer) });
        }

        internal static void ForwardHit(Character target, Character attacker, Vector3 pos, string weapon, double damage)
        {
            if (!Wants("hit") || !Owned(target)) return;
            Send(new JObject { ["e"] = "hit", ["sub"] = Handles.Id(target), ["t"] = SnapJson(Handles.Take(target)), ["a"] = SnapJson(Handles.Take(attacker)), ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z, ["weapon"] = weapon, ["damage"] = damage });
        }

        // ================================================================ this game's character, for the actions

        private sealed class UnityGame : IGame
        {
            private readonly Player _p;
            internal UnityGame(Player p) { _p = p; }

            public double Health => _p.GetHealth();
            public double MaxHealth => _p.GetMaxHealth();
            public double MaxStamina => _p.GetMaxStamina();
            public bool KnowsItem(string prefab) => ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(prefab) != null;
            public bool KnowsEffect(string name) => ObjectDB.instance != null && ObjectDB.instance.GetStatusEffect(name.GetStableHashCode()) != null;
            public int AddItem(string prefab, int n) => Players.AddToBag(_p, prefab, n);
            public int RemoveItem(string prefab, int n) => Players.RemoveFromBag(_p, prefab, n);

            public double Heal(double n)
            {
                float before = _p.GetHealth();
                _p.Heal((float)n, true);
                return Math.Max(0f, _p.GetHealth() - before);
            }

            public double Hurt(double n)
            {
                float before = _p.GetHealth();
                _p.Damage(new HitData { m_damage = { m_damage = (float)n } });
                return Math.Max(0f, before - _p.GetHealth());
            }

            public void SetStamina(double value) { ActorExtras.StaminaField(_p) = Mathf.Clamp((float)value, 0f, _p.GetMaxStamina()); }

            public bool AddEffect(string name, double seconds)
            {
                StatusEffect se = _p.GetSEMan().AddStatusEffect(name.GetStableHashCode(), true);
                if (se == null) return false;
                se.m_ttl = (float)seconds;
                return true;
            }

            public bool RemoveEffect(string name) => _p.GetSEMan().RemoveStatusEffect(name.GetStableHashCode(), true);

            public bool Launch(double dx, double dy, double dz, double force)
            {
                Rigidbody body = ActorExtras.BodyField(_p);
                var dir = new Vector3((float)dx, (float)dy, (float)dz);
                if (body == null || dir.sqrMagnitude < 0.0001f) return false;
                body.AddForce(dir.normalized * (float)force, ForceMode.VelocityChange);
                return true;
            }

            public bool ZoneLoaded(double x, double z) => ZoneSystem.instance != null && ZoneSystem.instance.IsZoneLoaded(new Vector3((float)x, 0f, (float)z));

            public bool Ground(double x, double z, out double y)
            {
                y = 0.0;
                if (ZoneSystem.instance == null || !ZoneSystem.instance.GetSolidHeight(new Vector3((float)x, 0f, (float)z), out float h)) return false;
                y = h;
                return true;
            }

            public bool TeleportTo(double x, double y, double z) => SafeTeleport.Go(_p, new Vector3((float)x, (float)y, (float)z));

            public void Banner(string text) { if (MessageHud.instance != null) MessageHud.instance.ShowBiomeFoundMsg(text, true); }
        }
    }
}
