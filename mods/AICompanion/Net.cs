using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// What players with the mod tell each other, over Valheim's routed messages (the server relays them, so distance does not matter):
    ///   * each companion's health, place and what it is doing, sent by whichever game runs it, for the party panel (PartyHud, if it is
    ///     installed, reads the list through AppDomain data "DHack.Companions": no reference between the two mods);
    ///   * when a companion falls: a skull on everyone's map where its gear crate is, saved with their map, and taken off everyone's map
    ///     once the crate has been emptied. Players who join later are told about the markers still standing.
    /// </summary>
    internal static class Net
    {
        private const string RpcStats = "DHack_CompanionStats", RpcFallen = "DHack_CompanionFallen", RpcCleared = "DHack_CompanionGearTaken", RpcSays = "DHack_CompanionSays";
        public const string CrateKey = "dhc_fallen";
        private const string MarkerSuffix = " fell here";

        private class Seen { public string Line; public float At; public string Master; }
        private class Fallen { public string Name, Master; public Vector3 Pos; public float At; public long Id, MasterId; }

        private static readonly Dictionary<long, Seen> Remote = new Dictionary<long, Seen>();
        private static readonly List<Fallen> RecentlyFallen = new List<Fallen>();
        private static ZRoutedRpc _registeredOn;
        private static float _nextSend, _nextMarkerCheck;
        private static int _peers, _registerAfterFrame;

        public static void Start()
        {
            _registerAfterFrame = Time.frameCount + 2; // after a hot reload the old copy removes its handlers at the end of the frame
            AppDomain.CurrentDomain.SetData("DHack.Companions", (Func<string>)PartyText);
        }

        public static void Stop()
        {
            AppDomain.CurrentDomain.SetData("DHack.Companions", null);
            RemoveLivePins();
            Unregister();
            _registeredOn = null;
            Remote.Clear();
            RecentlyFallen.Clear();
        }

        public static void Update(Player me)
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) { _registeredOn = null; return; }
            if (Time.frameCount <= _registerAfterFrame) return;
            if (rpc != _registeredOn)
            {
                _registeredOn = rpc;
                Remote.Clear();
                Unregister(); // the game throws if a name is registered twice
                rpc.Register<string>(RpcStats, OnStats);
                rpc.Register<string>(RpcFallen, OnFallen);
                rpc.Register<string>(RpcCleared, OnCleared);
                rpc.Register<string>(RpcSays, OnSays);
            }
            if (me == null) return;

            int peers = ZNet.instance != null ? ZNet.instance.GetPlayerList().Count : 0;
            if (peers > _peers && _peers > 0) Plugin.Instance.StartCoroutine(TellNewcomers()); // someone joined: tell them about the markers
            _peers = peers;

            if (Time.time >= _nextSend)
            {
                _nextSend = Time.time + 0.5f;
                if (peers > 1)
                    foreach (Humanoid c in Companion.All())
                        if (c.GetComponent<ZNetView>().IsOwner()) Send(RpcStats, StatsLine(c));
            }
            if (Time.time >= _nextMarkerCheck) { _nextMarkerCheck = Time.time + 2f; CheckMarkers(me); }
            if (Time.time >= _nextPins) { _nextPins = Time.time + 0.1f; UpdatePins(); }
        }

        // ---- live companions on the minimap and the big map -------------------------------------------------

        public static readonly Color PinColor = new Color(0.45f, 0.85f, 0.75f);
        private static readonly Dictionary<long, Minimap.PinData> LivePins = new Dictionary<long, Minimap.PinData>();
        private static float _nextPins;

        public static bool IsLivePin(Minimap.PinData pin) => LivePins.ContainsValue(pin);

        /// <summary>A pin that follows each companion (the same list as the party panel: yours and other players'), named after it.</summary>
        private static void UpdatePins()
        {
            if (Minimap.instance == null) { LivePins.Clear(); return; }
            var seen = new HashSet<long>();
            foreach (string line in PartyText().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = line.Split('|');
                if (f.Length < 9 || f[8] == "fallen" || !long.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)) continue;
                var pos = new Vector3(P(f[5]), P(f[6]), P(f[7]));
                seen.Add(id);
                if (!LivePins.TryGetValue(id, out Minimap.PinData pin) || !Pins(Minimap.instance).Contains(pin))
                    LivePins[id] = pin = Minimap.instance.AddPin(pos, Minimap.PinType.Player, f[1], false, false, 0L);
                pin.m_pos = pos;
            }
            foreach (long gone in LivePins.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                Minimap.instance.RemovePin(LivePins[gone]);
                LivePins.Remove(gone);
            }
        }

        private static void RemoveLivePins()
        {
            if (Minimap.instance != null) foreach (Minimap.PinData pin in LivePins.Values) Minimap.instance.RemovePin(pin);
            LivePins.Clear();
        }

        private static void Unregister()
        {
            if (ZRoutedRpc.instance == null) return;
            var table = AccessTools.Field(typeof(ZRoutedRpc), "m_functions").GetValue(ZRoutedRpc.instance) as IDictionary;
            foreach (string name in new[] { RpcStats, RpcFallen, RpcCleared, RpcSays }) table?.Remove(name.GetStableHashCode());
        }

        /// <summary>A companion's words for its player, from the game running it: "masterId|name|text".</summary>
        public static void SendSays(long masterId, string name, string text)
        {
            if (masterId == 0L || ZNet.instance == null || ZNet.instance.GetPlayerList().Count < 2) return;
            Send(RpcSays, masterId.ToString(CultureInfo.InvariantCulture) + "|" + Clean(name) + "|" + text.Replace('\n', ' '));
        }

        private static void OnSays(long sender, string payload)
        {
            string[] f = payload.Split(new[] { '|' }, 3);
            if (f.Length < 3 || !long.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long master)) return;
            if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == master) Talk.ToChat(f[1], f[2]);
        }

        private static void Send(string rpc, string payload)
        {
            try { ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, rpc, payload); }
            catch (Exception e) { Plugin.Instance?.Warn($"Could not send {rpc}: {e.Message}"); }
        }

        // ---- the party panel -------------------------------------------------------------------------------

        private static string Clean(string s) => (s ?? "").Replace('|', '/').Replace('\n', ' ');
        private static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        private static float P(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;

        /// <summary>"id|name|master|hp|maxhp|x|y|z|status|stamina|maxstamina|effects|foods" (effects "namehash:seconds,...", foods "prefab:seconds left:burn time,...", as PartyHud's own).</summary>
        private static string StatsLine(Humanoid c)
        {
            Vector3 p = c.transform.position;
            return string.Join("|", Companion.IdOf(c).ToString(CultureInfo.InvariantCulture), Clean(Companion.NameOf(c)), Clean(Companion.Zdo(c).GetString(Keys.MasterName, "")),
                F(c.GetHealth()), F(c.GetMaxHealth()), F(p.x), F(p.y), F(p.z), Clean(Companion.StatusOf(c)), F(Stamina.Get(c)), F(Stamina.Max(c)), EffectsText(c), FoodsText(c));
        }

        /// <summary>What it is eating (only known to the game that runs it; "-" elsewhere, so the panel shows nothing rather than "hungry").</summary>
        private static string FoodsText(Humanoid c)
        {
            if (!c.GetComponent<ZNetView>().IsOwner()) return "-";
            return string.Join(",", Food.Meals(c).Where(m => m.Item.m_dropPrefab != null).Select(m => Utils.GetPrefabName(m.Item.m_dropPrefab) + ":" + F(Mathf.Max(0f, m.Time)) + ":" + F(m.Item.m_shared.m_foodBurnTime)));
        }

        /// <summary>Its status effects with a picture (a boss power, meads, wet...): "namehash:seconds left,..." (only known to the game that runs it).</summary>
        private static string EffectsText(Humanoid c)
        {
            if (!c.GetComponent<ZNetView>().IsOwner()) return "";
            var parts = new List<string>();
            if (Rest.IsRested(c)) parts.Add(SEMan.s_statusEffectRested + ":" + F(Rest.Left(c)));
            foreach (StatusEffect se in c.GetSEMan().GetStatusEffects())
            {
                if (se == null || se.m_icon == null) continue;
                parts.Add(se.NameHash() + ":" + F(Mathf.Max(0f, se.GetRemaningTime())));
                if (parts.Count >= 8) break;
            }
            return string.Join(",", parts);
        }

        private static void OnStats(long sender, string line)
        {
            string[] f = (line ?? "").Split('|');
            if (f.Length < 9 || !long.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)) return;
            Remote[id] = new Seen { Line = line, At = Time.time, Master = f[2] };
        }

        /// <summary>
        /// Every companion whose owner is in the game, one per line ("id|name|master|hp|maxhp|x|y|z|status"), for PartyHud. Ones loaded here
        /// are read directly; others come from the game that runs them. A companion that just fell is listed with 0 health and status
        /// "fallen" until its owner summons one again.
        /// </summary>
        public static string PartyText()
        {
            try
            {
                if (ZNet.instance == null) return "";
                var online = new HashSet<string>(ZNet.instance.GetPlayerList().Select(p => p.m_name));
                var lines = new List<string>();
                var have = new HashSet<long>();
                foreach (Humanoid c in Companion.All())
                {
                    if (c.IsDead() || !online.Contains(Companion.Zdo(c).GetString(Keys.MasterName, ""))) continue;
                    long cid = Companion.IdOf(c);
                    bool runHere = c.GetComponent<ZNetView>().IsOwner();
                    if (!runHere && Remote.TryGetValue(cid, out Seen s) && Time.time - s.At < 2f) continue; // the game that runs it knows its effects: use its line
                    lines.Add(StatsLine(c));
                    have.Add(cid);
                }
                foreach (var kv in Remote.Where(kv => Time.time - kv.Value.At < 5f && !have.Contains(kv.Key) && online.Contains(kv.Value.Master)))
                    lines.Add(kv.Value.Line);
                // (not in the first seconds: the fallen companion itself is still listed for a moment as it dies)
                RecentlyFallen.RemoveAll(f => Time.time - f.At > 600f || (Time.time - f.At > 10f && lines.Any(l => l.Split('|')[2] == f.Master)));
                foreach (Fallen f in RecentlyFallen.Where(f => online.Contains(f.Master)))
                    lines.Add(string.Join("|", (-Math.Abs((long)(f.Name + f.Master).GetStableHashCode())).ToString(CultureInfo.InvariantCulture), Clean(f.Name), Clean(f.Master),
                        "0", "1", F(f.Pos.x), F(f.Pos.y), F(f.Pos.z), "fallen", "0", "1", "", "-"));
                return string.Join("\n", lines);
            }
            catch (Exception) { return ""; }
        }

        // ---- markers where a companion fell ---------------------------------------------------------------

        /// <summary>Called on the game that ran the companion when it fell, after its gear went into the crate.</summary>
        public static void AnnounceFall(Humanoid c, Vector3 pos, bool grave)
        {
            // "name|master|x|y|z|id|masterId": the marker for everyone, and for its player's game the news that it must wake it later
            Send(RpcFallen, string.Join("|", Clean(Companion.NameOf(c)), Clean(Companion.Zdo(c).GetString(Keys.MasterName, "")), F(pos.x), F(pos.y), F(pos.z),
                Companion.IdOf(c).ToString(CultureInfo.InvariantCulture), Companion.MasterId(c).ToString(CultureInfo.InvariantCulture), grave ? "1" : "0",
                Companion.Zdo(c)?.GetString(Companion.KeptKey, "") ?? "")); // (its gear, for its player's game to put back on it)
        }

        private static void OnFallen(long sender, string payload)
        {
            string[] f = (payload ?? "").Split('|');
            if (f.Length < 5) return;
            var pos = new Vector3(P(f[2]), P(f[3]), P(f[4]));
            AddMarker(pos, f[0]);
            if (f.Length >= 7 && long.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) && long.TryParse(f[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out long masterId)
                && Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == masterId)
                Home.MarkDead(Player.m_localPlayer, id, pos, null, f.Length < 8 || f[7] == "1", f.Length >= 9 ? f[8] : null); // ours, run by another game (or ours): it wakes in its bed later
            if (f[1] == "") return; // a marker re-sent for a player who just joined
            RecentlyFallen.RemoveAll(x => x.Name == f[0] && x.Master == f[1]);
            RecentlyFallen.Add(new Fallen { Name = f[0], Master = f[1], Pos = pos, At = Time.time,
                Id = f.Length >= 7 && long.TryParse(f[5], out long fid) ? fid : 0L, MasterId = f.Length >= 7 && long.TryParse(f[6], out long fm) ? fm : 0L });
            if (Player.m_localPlayer != null && f[1] != Player.m_localPlayer.GetPlayerName())
                Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, $"{f[1]}'s companion {f[0]} has fallen (marked on your map)");
        }

        private static void OnCleared(long sender, string payload)
        {
            string[] f = (payload ?? "").Split('|');
            if (f.Length < 3) return;
            RemoveMarkersNear(new Vector3(P(f[0]), P(f[1]), P(f[2])), 8f);
        }

        private static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> Pins = AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");

        private static IEnumerable<Minimap.PinData> Markers() =>
            Minimap.instance == null ? Enumerable.Empty<Minimap.PinData>() : Pins(Minimap.instance).Where(p => p.m_type == Minimap.PinType.Death && p.m_name != null && p.m_name.EndsWith(MarkerSuffix));

        private static void AddMarker(Vector3 pos, string name)
        {
            if (Minimap.instance == null) return;
            if (Markers().Any(p => Vector3.Distance(p.m_pos, pos) < 4f)) return;
            Minimap.instance.AddPin(pos, Minimap.PinType.Death, name + MarkerSuffix, true, false, 0L); // saved with this player's map of the world
        }

        private static void RemoveMarkersNear(Vector3 pos, float within = 4f)
        {
            if (Minimap.instance == null) return;
            foreach (Minimap.PinData pin in Markers().Where(p => Vector3.Distance(p.m_pos, pos) < within).ToList()) Minimap.instance.RemovePin(pin);
        }

        /// <summary>
        /// Its tombstone there was emptied (it took its things back, or you gave them to it): the skull comes off the map at once, here and for
        /// everyone, wherever you are (before, only once you came near and found it gone).
        /// </summary>
        public static void Recovered(Vector3 tomb)
        {
            RecentlyFallen.RemoveAll(f => Vector3.Distance(f.Pos, tomb) < 8f);
            foreach (Minimap.PinData pin in Markers().Where(p => Vector3.Distance(p.m_pos, tomb) < 8f).ToList())
            {
                Minimap.instance.RemovePin(pin);
                Send(RpcCleared, $"{F(pin.m_pos.x)}|{F(pin.m_pos.y)}|{F(pin.m_pos.z)}");
            }
            Send(RpcCleared, $"{F(tomb.x)}|{F(tomb.y)}|{F(tomb.z)}"); // (others' maps: their skull may sit a little off this one)
        }

        /// <summary>Near a marker whose crate is gone (emptied, so the game removed it): take the marker off, here and for everyone.</summary>
        private static void CheckMarkers(Player me)
        {
            foreach (Minimap.PinData pin in Markers().ToList())
            {
                if (Vector3.Distance(pin.m_pos, me.transform.position) > 30f) continue; // close enough that the crate would be loaded
                bool crate = UnityEngine.Object.FindObjectsOfType<Container>().Any(c => Vector3.Distance(c.transform.position, pin.m_pos) < 6f
                    && c.GetComponent<ZNetView>() is ZNetView v && v.IsValid() && v.GetZDO().GetString(CrateKey, "") != "");
                if (crate || RecentlyFallen.Any(f => Time.time - f.At < 120f && Vector3.Distance(f.Pos, pin.m_pos) < 4f)) continue; // (a fall without gear has no crate)
                Plugin.Instance?.Note($"The fallen companion's crate at {pin.m_pos:F0} is gone: marker removed");
                Minimap.instance.RemovePin(pin);
                Send(RpcCleared, $"{F(pin.m_pos.x)}|{F(pin.m_pos.y)}|{F(pin.m_pos.z)}");
            }
        }

        private static IEnumerator TellNewcomers()
        {
            yield return new WaitForSeconds(10f); // let them finish loading in
            foreach (Minimap.PinData pin in Markers().ToList())
            {
                string name = pin.m_name.Substring(0, pin.m_name.Length - MarkerSuffix.Length);
                Send(RpcFallen, string.Join("|", Clean(name), "", F(pin.m_pos.x), F(pin.m_pos.y), F(pin.m_pos.z)));
            }
            foreach (Fallen f in RecentlyFallen.Where(x => x.Id != 0L))
                Send(RpcFallen, string.Join("|", Clean(f.Name), Clean(f.Master), F(f.Pos.x), F(f.Pos.y), F(f.Pos.z),
                    f.Id.ToString(CultureInfo.InvariantCulture), f.MasterId.ToString(CultureInfo.InvariantCulture)));
        }
    }
}
