using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// A player's action is never dropped, and never done twice. An act (Site.Act: a lever used, a wall hit, a block placed...) goes to
    /// every game, and the entrance's owner acts on it. In Quadbobheim (0.2.2) nobody owned the entrance while both players were far
    /// inside a big dungeon: the server takes an object off a player whose active area doesn't reach it (ZDOMan.ReleaseNearbyZDOS, every
    /// 2 s), so presses on two one-shot levers were lost without a word. Now:
    /// - the server leaves the entrance with the player who owns it while they're up at dungeon height (the patch below);
    /// - a game whose player is inside claims an entrance nobody owns (Dungeons.Tend);
    /// - every one-off act has an id (the sender's session, this copy of the mod and a count: "#id|act" on the wire). The owner keeps the ids it acted on for
    ///   30 s in the entrance's ZDO (Keys.ActsDone: a new owner sees them too) and only answers a repeat, so sending one again can never
    ///   flip a toggle lever twice;
    /// - an act that reaches the server while nobody owns the entrance: the server takes it and acts if it has the dungeon built; else it
    ///   tells the sender (Dungeons_NoOwner), whose game takes it and acts. Exactly one game decides: the server, which every act passes;
    /// - the owner answers each one-off act (Dungeons_Ack); a game with no answer in 1.5 s sends it again, with the same id, twice at
    ///   most. Every step is logged.
    /// Words said once a second anyway (plates, areas, positions) have no id and are never answered or sent again.
    /// An owner running an older copy (0.2.2: a host reloaded in the world while a friend still had the old one) knows neither ids nor
    /// answers: it ignored "#id|act" and every press of the host's was given up. Each owner of ours writes what it speaks into the
    /// entrance's ZDO (Keys.ActsProto, "protocol@its session"); an owner that hasn't (an older copy) gets the plain act it knows, or,
    /// with the dungeon built here, this game takes the entrance and acts itself, telling the player. An act given up after its tries
    /// is done the same way, never in silence.
    /// The one window left: a game that has just claimed an unowned entrance acts on an act while the server, not having seen the claim
    /// yet (well under a second), takes it too. The claim (Tend) comes before players act on a dungeon nobody owned for a second or more.
    /// </summary>
    internal static class Acts
    {
        public const string AckRpc = "Dungeons_Ack";           // (dungeon id, wire act): from the owner to the game that sent it
        public const string NoOwnerRpc = "Dungeons_NoOwner";   // (dungeon id, wire act): from the server, nobody owns it: you act
        private const float Wait = 1.5f, Remember = 30f;
        private const int Retries = 2, RememberMost = 64;

        private class Pending
        {
            public string Id, Wire;
            public float At;
            public int Tries;
        }

        private static readonly List<Pending> Waiting = new List<Pending>();
        private static ZRoutedRpc _for;
        private static int _count;
        // (each copy of the mod counts from its own start: after a reload or a relog a count can't meet an id the owner still remembers)
        private static readonly string Copy = new System.Random().Next().ToString("x", CultureInfo.InvariantCulture);

        public static void Register()
        {
            if (ZRoutedRpc.instance == null || ZRoutedRpc.instance == _for) return;
            _for = ZRoutedRpc.instance;
            Waiting.Clear();
            try
            {
                ZRoutedRpc.instance.Register<string, string>(AckRpc, (sender, id, wire) => Answered(id, wire));
                ZRoutedRpc.instance.Register<string, string>(NoOwnerRpc, NoOwner);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Registering the acts' RPCs: " + e.Message); }
        }

        /// <summary>Said once a second while it holds (a plate stood on, an area, a position): no id, never answered or sent again.</summary>
        public static bool Repeated(string act) => act.StartsWith("plate:") || act.StartsWith("area:") || act.StartsWith("blk:pos:");

        /// <summary>Site.Act: what goes on the wire (a one-off act gets its id), and it waits for its answer.</summary>
        public static string Sent(Site site, string act)
        {
            if (Repeated(act)) return act;
            string wire = "#" + ZDOMan.GetSessionID().ToString(CultureInfo.InvariantCulture) + "." + Copy + "." + (++_count).ToString(CultureInfo.InvariantCulture) + "|" + act;
            ZDO z = site.Zdo;
            if (z != null && OlderOwner(site, z))
            {
                // its owner is an older copy (it knows no ids and never answers): this game takes the entrance and acts, if it can
                if (TakeOver(site, wire, $"its owner ({z.GetOwner()}) runs an older Quad's Dungeons")) return null;
                Plugin.Log.LogWarning($"Dungeon {site.Id}: its owner ({z.GetOwner()}) runs an older Quad's Dungeons and it isn't built here: \"{act}\" sent the old way, unanswered");
                return act;
            }
            Waiting.Add(new Pending { Id = site.Id, Wire = wire, At = Time.time });
            return wire;
        }

        /// <summary>This game takes the entrance and acts on a wire act itself (the dungeon built here), telling the player. False: it
        /// can't (not built here).</summary>
        private static bool TakeOver(Site site, string wire, string why)
        {
            if (site?.Zdo == null || !site.Ready || site.Mechs == null) return false;
            Plugin.Log.LogWarning($"Dungeon {site.Id}: {why}: this game takes its entrance and acts on \"{wire}\" itself");
            site.Claim();
            Mark(site);
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, DHack.Shared.Translations.T("dungeons_act_takeover"));
            Handle(site, ZDOMan.GetSessionID(), wire);
            return true;
        }

        // ---- what the owner speaks: "protocol@session" in the entrance's ZDO, written by each owner of ours (older copies don't)

        public const int Protocol = 2;   // 2: act ids, answers, ActsProto (0.2.3); older owners write nothing

        private static string MyMark => Protocol.ToString(CultureInfo.InvariantCulture) + "@" + ZDOMan.GetSessionID().ToString(CultureInfo.InvariantCulture);

        /// <summary>The owner, every tend (and on taking it over): says what it speaks.</summary>
        public static void Mark(Site site)
        {
            ZDO z = site?.Zdo;
            if (z == null || !z.IsOwner()) return;
            string mark = MyMark;
            if (z.GetString(Keys.ActsProto) != mark) z.Set(Keys.ActsProto, mark);
        }

        private static readonly Dictionary<string, KeyValuePair<long, float>> OwnerSince = new Dictionary<string, KeyValuePair<long, float>>();

        /// <summary>The entrance is owned by a game that doesn't speak act ids (an older copy): owned, unmarked, for 3 s at least (a new
        /// owner of ours marks it within a frame; its mark reaches this game with its ownership, or just after).</summary>
        public static bool OlderOwner(Site site, ZDO z)
        {
            if (Unowned(z) || Speaks(z)) { OwnerSince.Remove(site.Id); return false; }
            long owner = z.GetOwner();
            if (!OwnerSince.TryGetValue(site.Id, out KeyValuePair<long, float> seen) || seen.Key != owner)
                OwnerSince[site.Id] = seen = new KeyValuePair<long, float>(owner, Time.time);
            return Time.time - seen.Value >= 3f;
        }

        /// <summary>The entrance's owner speaks act ids: it's this game, or it said so (its mark names it).</summary>
        public static bool Speaks(ZDO z)
        {
            long owner = z.GetOwner();
            if (owner == ZDOMan.GetSessionID()) return true;
            string mark = z.GetString(Keys.ActsProto) ?? "";
            int at = mark.IndexOf('@');
            return at > 0 && int.TryParse(mark.Substring(0, at), out int p) && p >= Protocol
                   && long.TryParse(mark.Substring(at + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long by) && by == owner;
        }

        /// <summary>A wire act: its id (null: none) and the act.</summary>
        public static string Split(string wire, out string actId)
        {
            actId = null;
            if (wire == null || !wire.StartsWith("#")) return wire;
            int bar = wire.IndexOf('|');
            if (bar < 0) return wire;
            actId = wire.Substring(1, bar - 1);
            return wire.Substring(bar + 1);
        }

        /// <summary>
        /// The owner (or the game about to be): acts on a wire act once. A repeat of one acted on in the last 30 s (its id in the
        /// entrance's ZDO) is only answered.
        /// </summary>
        public static void Handle(Site site, long sender, string wire)
        {
            string act = Split(wire, out string actId);
            if (actId != null && Done(site.Zdo, actId))
                Plugin.Log.LogInfo($"Dungeon {site.Id}: \"{act}\" ({actId}) came again: done already, only answered");
            else
            {
                if (actId != null) Note(site.Zdo, actId);   // (first: an act that throws is still never done twice)
                Dungeons.HandleAct(site, sender, act);
            }
            if (actId == null) return;
            if (sender == ZDOMan.GetSessionID()) Answered(site.Id, wire);
            else ZRoutedRpc.instance?.InvokeRoutedRPC(sender, AckRpc, site.Id, wire);
        }

        /// <summary>The server: an act came while nobody owns the entrance and the dungeon isn't built here: the sender acts.</summary>
        public static void TellNoOwner(long sender, string id, string wire)
        {
            Split(wire, out string actId);
            if (actId == null) return;   // (a repeated word: the claim in Tend sees to the owner)
            Plugin.Log.LogWarning($"Dungeon {id}: \"{wire}\" came while nobody owned its entrance, and it isn't built on the server: its sender acts on it");
            if (sender == ZDOMan.GetSessionID()) return;
            ZRoutedRpc.instance?.InvokeRoutedRPC(sender, NoOwnerRpc, id, wire);
        }

        private static void NoOwner(long sender, string id, string wire)
        {
            if (ZNet.instance == null || sender != (ZNet.instance.IsServer() ? ZDOMan.GetSessionID() : ZNet.instance.GetServerPeer()?.m_uid ?? 0L)) return;
            Site site = Dungeons.Sites.FirstOrDefault(s => !s.Gone && s.Id == id);
            if (site?.Zdo == null || !site.Ready || site.Mechs == null)
            {
                Plugin.Log.LogWarning($"Dungeon {id}: nobody owns its entrance and \"{wire}\" is ours to act on, but it isn't built here: sent again later");
                return;
            }
            Plugin.Log.LogWarning($"Dungeon {id}: nobody owned its entrance (the server says): this game takes it and acts on \"{wire}\"");
            site.Claim();
            Handle(site, ZDOMan.GetSessionID(), wire);
        }

        private static void Answered(string id, string wire)
        {
            int i = Waiting.FindIndex(w => w.Id == id && w.Wire == wire);
            if (i >= 0) Waiting.RemoveAt(i);
        }

        // ---- the ids acted on, in the entrance's ZDO: "id@time;..." (the server's clock, MechClock)

        private static bool Done(ZDO z, string actId)
        {
            string list = z?.GetString(Keys.ActsDone) ?? "";
            return list.Length > 0 && list.Split(';').Any(e => e.StartsWith(actId + "@", StringComparison.Ordinal));
        }

        private static void Note(ZDO z, string actId)
        {
            if (z == null) return;
            double now = MechClock.Now;
            var keep = new List<string>();
            foreach (string e in (z.GetString(Keys.ActsDone) ?? "").Split(';'))
            {
                int at = e.LastIndexOf('@');
                if (at <= 0 || !double.TryParse(e.Substring(at + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double t)) continue;
                if (now - t < Remember) keep.Add(e);
            }
            keep.Add(actId + "@" + now.ToString("0.0", CultureInfo.InvariantCulture));
            if (keep.Count > RememberMost) keep.RemoveRange(0, keep.Count - RememberMost);
            z.Set(Keys.ActsDone, string.Join(";", keep));
        }

        // ---- who owns it

        /// <summary>A game in the world now (this one, the server, or a player's).</summary>
        public static bool Connected(long uid)
        {
            if (uid == 0 || ZNet.instance == null) return false;
            if (uid == ZDOMan.GetSessionID()) return true;
            if (ZNet.instance.IsServer()) return ZNet.instance.GetPeer(uid) != null;
            if (ZNet.instance.GetServerPeer()?.m_uid == uid) return true;
            return ZNet.instance.GetPlayerList().Any(p => p.m_characterID.UserID == uid);
        }

        /// <summary>Nobody owns this entrance, or a game that left does.</summary>
        public static bool Unowned(ZDO z) => z != null && !Connected(z.GetOwner());

        /// <summary>Every frame: the one-off acts that got no answer in time are sent again (the same id: never done twice).</summary>
        public static void Tick()
        {
            for (int i = Waiting.Count - 1; i >= 0; i--)
            {
                Pending w = Waiting[i];
                if (Time.time - w.At < Wait) continue;
                Site site = Dungeons.Sites.FirstOrDefault(s => !s.Gone && s.Id == w.Id);
                ZDO z = site?.Zdo;
                if (z == null) { Waiting.RemoveAt(i); Plugin.Log.LogWarning($"Dungeon {w.Id}: \"{w.Wire}\" got no answer, and the dungeon isn't known here any more"); continue; }
                if (w.Tries >= Retries || OlderOwner(site, z))
                {
                    // never given up in silence: this game takes the entrance and acts (the id keeps it once, with an owner of ours), or
                    // the player is told it didn't go through
                    Waiting.RemoveAt(i);
                    string why = $"\"{w.Wire}\" got no answer from its owner ({z.GetOwner()}{(Speaks(z) ? "" : ", an older Quad's Dungeons or none")}) after {w.Tries + 1} tries";
                    if (TakeOver(site, w.Wire, why)) continue;
                    Plugin.Log.LogWarning($"Dungeon {w.Id}: {why}, and it isn't built here: given up");
                    Player.m_localPlayer?.Message(MessageHud.MessageType.Center, DHack.Shared.Translations.T("dungeons_act_lost"));
                    continue;
                }
                w.Tries++;
                w.At = Time.time;
                Plugin.Log.LogWarning($"Dungeon {w.Id}: \"{w.Wire}\" got no answer from its owner ({z.GetOwner()}{(Unowned(z) ? ": nobody" : "")}) in {Wait} s: sent again (try {w.Tries + 1})");
                ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, Dungeons.SiteRpc, w.Id, w.Wire);
            }
        }
    }

    // The server hands an object to nobody when its owner's active area doesn't reach it: a dungeon's entrance, while its owner is far
    // inside (up to 320 m from it). Kept with them while they're up at dungeon height; a player near the entrance still takes it over as
    // the game does (an owner whose area doesn't reach it), and a game that leaves loses it.
    [HarmonyPatch(typeof(ZDOMan), "ReleaseNearbyZDOS")]
    internal static class ZDOMan_ReleaseNearbyZDOS
    {
        private static void Prefix(Vector3 refPosition, long uid, out List<ZDO> __state)
        {
            __state = null;
            if (refPosition.y < 3000f || ZDOMan.instance == null) return;
            foreach (ZDO z in Dungeons.EntranceZdos())
                if (z.GetOwner() == uid) { if (__state == null) __state = new List<ZDO>(); __state.Add(z); }
        }

        private static void Postfix(long uid, List<ZDO> __state)
        {
            if (__state == null) return;
            foreach (ZDO z in __state)
                if (!z.HasOwner()) z.SetOwner(uid);
        }
    }
}
