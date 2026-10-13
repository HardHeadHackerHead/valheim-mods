using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Scripts.Mp
{
    /// <summary>A token bucket: `PerSecond` refills it, `Burst` is how many may go at once.</summary>
    internal sealed class Bucket
    {
        private double _tokens, _last;
        private readonly double _perSecond, _burst;
        internal Bucket(double perSecond, double burst) { _perSecond = perSecond; _burst = burst; _tokens = burst; _last = double.NaN; }

        internal bool Take(double now, double cost = 1.0)
        {
            if (double.IsNaN(_last)) _last = now;
            _tokens = Math.Min(_burst, _tokens + Math.Max(0.0, now - _last) * _perSecond);
            _last = now;
            if (_tokens < cost) return false;
            _tokens -= cost;
            return true;
        }
    }

    /// <summary>What one connection may send: 60 messages a second (burst 120) and 1 MB a second (burst 2 MB). Over it, the message is dropped and counted.</summary>
    internal sealed class PeerLimiter
    {
        private readonly Bucket _messages = new Bucket(60.0, 120.0), _bytes = new Bucket(1024.0 * 1024.0, 2.0 * 1024.0 * 1024.0);
        internal int Dropped;

        internal bool Allow(double now, int bytes)
        {
            if (!_messages.Take(now) || !_bytes.Take(now, bytes)) { Dropped++; return false; }
            return true;
        }
    }

    /// <summary>Remembers ids for a minute, so the same kill told twice (by the server's own patch and by the player's game) is one event.</summary>
    internal sealed class Dedup
    {
        internal const double Window = 60.0;
        private const int Max = 5000;
        private readonly Dictionary<string, double> _seen = new Dictionary<string, double>();

        /// <summary>True the first time an id is seen within the window.</summary>
        internal bool IsNew(string id, double now)
        {
            if (_seen.Count > Max / 2) Purge(now);
            if (_seen.TryGetValue(id, out double at) && now - at <= Window) return false;
            if (_seen.Count >= Max) Purge(now);
            if (_seen.Count >= Max) return false;   // (a flood of ids: nothing more is believed until they age out)
            _seen[id] = now;
            return true;
        }

        private void Purge(double now)
        {
            foreach (string k in _seen.Where(kv => now - kv.Value > Window).Select(kv => kv.Key).ToList()) _seen.Remove(k);
        }
    }

    /// <summary>Questions waiting for an answer: an id for each, a callback, and a time after which the answer is given up on.</summary>
    internal sealed class PendingTable
    {
        private sealed class Entry { public Action<Frame> Done; public double Deadline; public long Peer; }
        private readonly Dictionary<long, Entry> _by = new Dictionary<long, Entry>();
        private long _next;

        /// <summary>A new id for a question to `peer`. `done` gets the answer, or null when none came in time.</summary>
        internal long Ask(long peer, double now, Action<Frame> done, double seconds = 10.0)
        {
            long id = ++_next;
            _by[id] = new Entry { Done = done, Deadline = now + seconds, Peer = peer };
            return id;
        }

        /// <summary>An answer came: true when it was one we asked for, from the one we asked (an answer for someone else's question is ignored).</summary>
        internal bool Answer(long id, long peer, Frame f)
        {
            if (!_by.TryGetValue(id, out Entry e) || e.Peer != peer) return false;
            _by.Remove(id);
            e.Done(f);
            return true;
        }

        internal void Expire(double now)
        {
            if (_by.Count == 0) return;
            foreach (long id in _by.Where(kv => now > kv.Value.Deadline).Select(kv => kv.Key).ToList())
            {
                Entry e = _by[id];
                _by.Remove(id);
                e.Done(null);
            }
        }

        internal void Drop(long peer)
        {
            foreach (long id in _by.Where(kv => kv.Value.Peer == peer).Select(kv => kv.Key).ToList())
            {
                Entry e = _by[id];
                _by.Remove(id);
                e.Done(null);
            }
        }

        internal int Count => _by.Count;
    }

    /// <summary>One item in a player's bag as the player's game reports it.</summary>
    internal sealed class ItemInfo
    {
        internal string Prefab = "", Name = "";
        internal int Count, Quality;
    }

    /// <summary>What only a player's own game knows about them (section 5 of the design), as last reported.</summary>
    internal sealed class PlayerState
    {
        internal double At;
        internal readonly List<ItemInfo> Inventory = new List<ItemInfo>();
        internal string[] Equipped = { "", "", "", "", "" };   // right, left, chest, legs, head
        internal readonly Dictionary<string, double> Skills = new Dictionary<string, double>();
        internal int Deaths;
        internal double Stamina, MaxStamina;
        internal bool Swimming, Sitting, Running, Blocking;
        internal bool HasLook;
        internal double LookX, LookY, LookZ;

        internal int Count(string prefab) => Inventory.Where(i => i.Prefab == prefab).Sum(i => i.Count);
    }

    /// <summary>The server's copy of those reports, per player (by connection). A report is capped and cleaned on the way in.</summary>
    internal sealed class StateCache
    {
        internal const int MaxItems = 120, MaxSkills = 40, MaxText = 60;
        internal const double StaleAfter = 15.0;
        private readonly Dictionary<long, PlayerState> _by = new Dictionary<long, PlayerState>();

        /// <summary>Takes a report in. False when it was not understood (nothing changes then).</summary>
        internal bool Update(long peer, JObject d, double now)
        {
            try
            {
                var s = new PlayerState { At = now };
                if (d["inv"] is JArray inv)
                    foreach (JToken t in inv.Take(MaxItems))
                    {
                        if (!(t is JArray a) || a.Count < 2) continue;
                        s.Inventory.Add(new ItemInfo { Prefab = Clip((string)a[0]), Count = Math.Max(0, Math.Min(100000, (int)a[1])), Quality = a.Count > 2 ? Math.Max(0, Math.Min(10, (int)a[2])) : 1, Name = a.Count > 3 ? Clip((string)a[3]) : "" });
                    }
                if (d["eq"] is JArray eq) for (int i = 0; i < 5 && i < eq.Count; i++) s.Equipped[i] = Clip((string)eq[i]);
                if (d["sk"] is JObject sk) foreach (JProperty p in sk.Properties().Take(MaxSkills)) s.Skills[Clip(p.Name)] = Math.Max(0.0, Math.Min(100.0, (double)p.Value));
                s.Deaths = d["de"] != null ? Math.Max(0, Math.Min(100000, (int)d["de"])) : 0;
                if (d["st"] is JArray st && st.Count >= 2) { s.Stamina = Math.Max(0.0, Math.Min(10000.0, (double)st[0])); s.MaxStamina = Math.Max(0.0, Math.Min(10000.0, (double)st[1])); }
                int flags = d["fl"] != null ? (int)d["fl"] : 0;
                s.Swimming = (flags & 1) != 0; s.Sitting = (flags & 2) != 0; s.Running = (flags & 4) != 0; s.Blocking = (flags & 8) != 0;
                if (d["lk"] is JArray lk && lk.Count >= 3) { s.HasLook = true; s.LookX = (double)lk[0]; s.LookY = (double)lk[1]; s.LookZ = (double)lk[2]; }
                _by[peer] = s;
                return true;
            }
            catch (Exception) { return false; }
        }

        internal PlayerState Get(long peer) => _by.TryGetValue(peer, out PlayerState s) ? s : null;
        internal double Age(long peer, double now) => _by.TryGetValue(peer, out PlayerState s) ? now - s.At : double.MaxValue;
        internal void Remove(long peer) { _by.Remove(peer); }
        internal int Count => _by.Count;

        private static string Clip(string s) => s == null ? "" : (s.Length > MaxText ? s.Substring(0, MaxText) : s);
    }
}
