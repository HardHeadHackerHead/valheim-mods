using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Scripts.Mp
{
    /// <summary>What the server's side needs from the transport (Wire.cs fills it in; the tests use a loopback).</summary>
    internal interface IServerLink
    {
        bool HasAgent(long peer);
        void Send(long peer, Frame frame);
        double Now { get; }
    }

    /// <summary>What a script's call gets back at once: it was sent (with the value to return now) or it was not (and why).</summary>
    internal struct Outcome
    {
        internal bool Sent;
        internal double Value;
        internal string Why;
        internal static Outcome Yes(double value) => new Outcome { Sent = true, Value = value, Why = "" };
        internal static Outcome No(string why) => new Outcome { Sent = false, Value = 0.0, Why = why };
    }

    /// <summary>
    /// The server's side of "ask a player's game to do it" with the Unity parts taken out: check what is asked against the same rules the
    /// player's game will use, keep the hourly counts honest (reserve at once, settle from the answer: what was not done is given back),
    /// send, and match the answer to the question. An answer that does not come is "no answer from their game", never a hang.
    /// </summary>
    internal sealed class RemoteCore
    {
        internal const string NoAgent = "needs Quad's Blockscript on that player's game";
        private readonly IServerLink _link;
        internal readonly PendingTable Pending = new PendingTable();

        internal RemoteCore(IServerLink link) { _link = link; }

        /// <summary>Sends an action to one player's game. `settled` gets the answer (ok, n, why), or null when none came in time.</summary>
        internal string Act(long peer, string op, JObject args, Action<Frame> settled)
        {
            if (!_link.HasAgent(peer)) return NoAgent;
            args = args ?? new JObject();
            args["op"] = op;
            long id = Pending.Ask(peer, _link.Now, settled ?? (f => { }));
            _link.Send(peer, new Frame("act", id, args));
            return "";
        }

        /// <summary>A `done` came in from `peer`. False when it answered nothing we asked that peer (ignored).</summary>
        internal bool OnDone(long peer, Frame f) => Pending.Answer(f.I, peer, f);

        internal void Tick() { Pending.Expire(_link.Now); }

        internal void PeerLeft(long peer) { Pending.Drop(peer); }

        // ---- items: the ones with a count to keep ----

        /// <summary>
        /// Gives items to a player's game. `left` is how many the script may still give this player this hour; `use` changes that count
        /// (reserved now, settled when the answer comes); `settled(given, why)` is told what really happened. The value returned at once
        /// is what was asked after the caps: the player's own bag decides the rest.
        /// </summary>
        internal Outcome Give(long peer, string prefab, int n, int left, Action<int> use, Action<int, string> settled)
        {
            if (!_link.HasAgent(peer)) return Outcome.No(NoAgent);
            Verdict v = Validate.Give(prefab, n);
            if (!v.Ok) return Outcome.No(v.Why);
            if (left <= 0) return Outcome.No("the hourly limit for items");
            int ask = (int)Math.Min(v.N, left);
            use(ask);
            string why = Act(peer, "give", new JObject { ["p"] = prefab, ["n"] = ask }, f =>
            {
                int got = f == null || !f.B("ok") ? 0 : (int)f.N("n");
                use(got - ask);
                settled(got, f == null ? "no answer from their game" : f.S("why"));
            });
            if (why.Length > 0) { use(-ask); return Outcome.No(why); }
            return Outcome.Yes(ask);
        }

        /// <summary>Takes items from a player: only counted once the player's game says what it took (the owner decides, not our copy of their bag).</summary>
        internal Outcome Take(long peer, string prefab, int n, int knownCount, int left, Action<int> use, Action<int, string> settled)
        {
            if (!_link.HasAgent(peer)) return Outcome.No(NoAgent);
            Verdict v = Validate.Take(prefab, n);
            if (!v.Ok) return Outcome.No(v.Why);
            if (left <= 0) return Outcome.No("the hourly limit for taking items");
            int ask = (int)Math.Min(v.N, left);
            use(ask);
            string why = Act(peer, "take", new JObject { ["p"] = prefab, ["n"] = ask }, f =>
            {
                int got = f == null || !f.B("ok") ? 0 : (int)f.N("n");
                use(got - ask);
                settled(got, f == null ? "no answer from their game" : f.S("why"));
            });
            if (why.Length > 0) { use(-ask); return Outcome.No(why); }
            return Outcome.Yes(knownCount >= 0 ? Math.Min(ask, knownCount) : ask);
        }

        // ---- the broadcast ----

        /// <summary>A world-visible thing (`fx`) to every game in `peers` that has the mod.</summary>
        internal int Fx(IEnumerable<long> peers, JObject args)
        {
            int sent = 0;
            foreach (long peer in peers.Distinct())
            {
                if (!_link.HasAgent(peer)) continue;
                _link.Send(peer, new Frame("fx", 0, (JObject)args.DeepClone()));
                sent++;
            }
            return sent;
        }
    }

    /// <summary>
    /// What the server does with an event a player's game forwards: only events a player's game can see are accepted, only about what that
    /// connection owns, no faster than 50 a second, and the same thing told twice is one event.
    /// </summary>
    internal sealed class EventGate
    {
        internal static readonly HashSet<string> Names = new HashSet<string>
        {
            "kill", "bossDeath", "playerDeath", "playerRespawn", "itemPickup", "eat", "skillUp", "pieceBuilt", "tamed", "hit", "treeFelled", "logBroken",
        };

        private readonly Dedup _dedup = new Dedup();
        private readonly Dictionary<long, Bucket> _rates = new Dictionary<long, Bucket>();
        internal int Refused;

        /// <summary>`owns(event, subject)`: does that connection own the thing the event is about (asked of the game; the tests say yes or no).</summary>
        internal bool Accept(long peer, Frame f, Func<string, string, bool> owns, double now, out string why)
        {
            why = "";
            string name = f.S("e");
            if (!Names.Contains(name)) { why = "not an event a player's game may forward"; Refused++; return false; }
            if (!_rates.TryGetValue(peer, out Bucket b)) _rates[peer] = b = new Bucket(50.0, 100.0);
            if (!b.Take(now)) { why = "too many events"; Refused++; return false; }
            string subject = f.S("sub");
            if (subject.Length == 0 || !owns(name, subject)) { why = "not theirs"; Refused++; return false; }
            string id = f.S("id");
            if (id.Length > 0 && id != "0:0" && !_dedup.IsNew(name + "|" + id, now)) { why = "duplicate"; return false; }
            return true;
        }

        /// <summary>The server's own game saw the event: true when this is the first of it (a copy a player's game forwards later is a duplicate).</summary>
        internal bool Own(string name, string id, double now) => string.IsNullOrEmpty(id) || id == "0:0" || _dedup.IsNew(name + "|" + id, now);

        internal void PeerLeft(long peer) { _rates.Remove(peer); }
    }
}
