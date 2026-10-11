using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    /// <summary>
    /// The triggers of a built dungeon (the format: PackReaderTriggers.cs). They run on the entrance's owner only, and what they've done is
    /// saved in the entrance's ZDO (TriggerState): which fired, sequences' progress, counters, timers, kills, the music asked for. So a
    /// trigger fires once for everyone, and a relog or a new owner goes on where the last left off.
    /// - Events come from: state changes, seen by looking at every mechanism every tenth of a second (a lever's wires, an action, a cycle, a
    ///   brazier lit, a breakable broken, an encounter won: all the same way), a lever pulled or a plate pressed (MechSet's own hooks, as
    ///   they can happen without a change of state), players in areas (the owner sees its own loaded players, and every other game says
    ///   where its player is once a second), creatures' deaths (the creature's owner tells the dungeon's), timers on the server's clock,
    ///   pedestals, and other triggers' signals and counters.
    /// - Sounds and messages play on every game (the owner tells them: Dungeons_TrigNotice); a mechanism's change shows on every game as
    ///   any change does; music is the saved "m", read by every game.
    /// </summary>
    internal partial class MechSet
    {
        private const float TriggerEvery = 0.1f, AreaEvery = 0.25f, TellPosEvery = 1f, PosFor = 2.5f;
        public const string NoticeRpc = "Dungeons_TrigNotice";

        private sealed class Trig
        {
            public int Room;
            public TriggerDef Def;
            public string Key;                  // "<room>.<id>"
            public string[] Mechs;              // each event's mechanism key (or null)
        }

        private List<Trig> _trigs;
        private string _trigText;
        private Dictionary<string, string> _trig = new Dictionary<string, string>();
        private readonly Dictionary<Mech, int> _lastSeen = new Dictionary<Mech, int>();
        private readonly Dictionary<string, bool> _occupied = new Dictionary<string, bool>();
        private readonly Dictionary<string, long> _everyTick = new Dictionary<string, long>();
        private readonly Dictionary<long, KeyValuePair<Vector3, float>> _playerAt = new Dictionary<long, KeyValuePair<Vector3, float>>();
        private float _nextTrigger, _nextArea, _nextTellPos;

        // ---- the triggers

        private List<Trig> Trigs()
        {
            if (_trigs != null || !Complete) return _trigs ?? new List<Trig>();
            _trigs = new List<Trig>();
            foreach (PlacedRoom r in Layout.Rooms)
                if (r.Def?.Triggers != null)
                    foreach (TriggerDef t in r.Def.Triggers)
                        _trigs.Add(new Trig { Room = r.Index, Def = t, Key = r.Index + "." + t.Id, Mechs = t.On.Select(e => Resolve(r.Index, e.Mech)).ToArray() });
            return _trigs;
        }

        /// <summary>Whether this dungeon has triggers (any room's).</summary>
        public bool HasTriggers => Layout.Rooms.Any(r => r.Def?.Triggers != null && r.Def.Triggers.Count > 0);

        /// <summary>A mechanism a room's trigger names (as wires name them) to its key, or null.</summary>
        private string Resolve(int room, string id) => string.IsNullOrEmpty(id) ? null : Layouts.Target(Layout, room, new Wire { Target = id });

        private Mech MechOf(int room, string id) { string k = Resolve(room, id); return k != null && ByKey.TryGetValue(k, out Mech m) ? m : null; }

        /// <summary>A counter's, timer's, signal's or group's name in a room: its own, or "@name" for the whole dungeon.</summary>
        private static string Scoped(int room, string name) => string.IsNullOrEmpty(name) ? "" : name.StartsWith("@") ? TriggerState.Clean(name) : room + "." + TriggerState.Clean(name);

        // ---- the saved state

        private void ReadTriggers()
        {
            ZDO z = Site.Zdo;
            if (z == null) return;
            string text = z.GetString(TriggerState.Key) ?? "";
            if (text == _trigText) return;
            _trigText = text;
            _trig = TriggerState.Parse(text);
        }

        /// <summary>A saved value (null: none).</summary>
        public string TrigValue(string key) { ReadTriggers(); return _trig.TryGetValue(key, out string v) ? v : null; }

        /// <summary>The owner: saved values changed (null removes one), written at once.</summary>
        private void PutTrig(string key, string value) => PutTrig(new[] { new KeyValuePair<string, string>(key, value) });

        private void PutTrig(IEnumerable<KeyValuePair<string, string>> changes)
        {
            if (!Site.IsOwner || Site.Zdo == null) return;
            ReadTriggers();
            var all = new Dictionary<string, string>(_trig);
            foreach (KeyValuePair<string, string> kv in changes)
            {
                if (kv.Value == null) all.Remove(kv.Key);
                else all[kv.Key] = kv.Value;
            }
            string text = TriggerState.Format(all);
            if (text == _trigText) return;
            Site.Zdo.Set(TriggerState.Key, text);
            _trigText = text;
            _trig = all;
        }

        private int Counter(string scoped) => TriggerState.I(TrigValue("c|" + scoped));

        // ---- events

        /// <summary>The owner: an event that isn't a change of state (a lever pulled, a plate pressed or let go), from MechSet's hooks.</summary>
        public void TriggerEvent(string kind, Mech m)
        {
            if (!Site.IsOwner || m == null) return;
            try { Fire(kind, m.Key); }
            catch (Exception e) { WarnOnce($"Dungeon {Site.Id}: triggers on {kind} {m.Key}: {e}"); }
        }

        /// <summary>Every trigger waiting for this event: handled.</summary>
        private void Fire(string kind, string mech = null, int state = -1, string name = null, string item = null, int depth = 0)
        {
            foreach (Trig t in Trigs())
                for (int i = 0; i < t.Def.On.Count; i++)
                {
                    TriggerEvent e = t.Def.On[i];
                    if (e.Kind != kind) continue;
                    bool match;
                    switch (kind)
                    {
                        case TriggerEvents.Signal: case TriggerEvents.Counter: case TriggerEvents.Timer:
                            match = name != null && !string.IsNullOrEmpty(e.Name) && Scoped(t.Room, e.Name) == name;
                            break;
                        case TriggerEvents.State:
                            match = mech != null && t.Mechs[i] == mech && (e.State < 0 || e.State == state);
                            break;
                        case TriggerEvents.Place: case TriggerEvents.Take:
                            match = mech != null && t.Mechs[i] == mech && (e.Item == null || TriggerReader.ItemPrefab(Layout.Pack, e.Item) == item);
                            break;
                        default:
                            match = mech != null && t.Mechs[i] == mech;
                            break;
                    }
                    if (match) Handle(t, i, depth);
                }
        }

        /// <summary>One of a trigger's events happened: a sequence moves on (or starts again), the rest check their conditions; then it runs.</summary>
        private void Handle(Trig t, int index, int depth)
        {
            if (depth > 8) { WarnOnce($"Dungeon {Site.Id}: trigger {t.Key}: triggers firing triggers more than 8 deep (a loop?): stopped"); return; }
            if (!t.Def.Repeat && TrigValue("f|" + t.Key) != null) return;
            if (t.Def.When == "sequence")
            {
                string q = TrigValue("q|" + t.Key) ?? "0,0";
                string[] f = q.Split(',');
                int done = TriggerState.I(f[0]);
                double first = f.Length > 1 ? TriggerState.D(f[1]) : 0.0, now = MechClock.Now;
                if (done > 0 && t.Def.Within > 0f && now - first > t.Def.Within) done = 0;   // (too slow: from the start again)
                if (index == done) { if (done == 0) first = now; done++; }
                else if (index == 0) { done = 1; first = now; }
                else done = 0;
                if (done < t.Def.On.Count)
                {
                    PutTrig("q|" + t.Key, done == 0 ? null : done + "," + TriggerState.F(first));
                    return;
                }
                PutTrig("q|" + t.Key, null);
            }
            if (!Conditions(t)) return;
            Run(t, depth, "its event " + t.Def.On[index].Kind);
        }

        private bool Conditions(Trig t)
        {
            if (t.Def.If.Count == 0) return true;
            bool Holds(TriggerCondition c)
            {
                if (c.Mech != null)
                {
                    Mech m = MechOf(t.Room, c.Mech);
                    if (m == null) return false;
                    int s = m.Def.Encounter != null ? EncounterOf(m).State : StateOf(m);
                    return c.State >= 0 ? s == c.State : s != c.Not;
                }
                int v = Counter(Scoped(t.Room, c.Counter));
                return (c.AtLeast == null || v >= c.AtLeast) && (c.AtMost == null || v <= c.AtMost) && (c.EqualTo == null || v == c.EqualTo);
            }
            return t.Def.When == "any" ? t.Def.If.Any(Holds) : t.Def.If.All(Holds);
        }

        // ---- actions

        /// <summary>The owner: a trigger's actions, in order (mechanisms changed together at the end; signals and counters it sets fire after).</summary>
        private void Run(Trig t, int depth, string why)
        {
            if (!t.Def.Repeat) PutTrig("f|" + t.Key, "1");
            var pending = new Dictionary<Mech, int>();
            int Cur(Mech x) => pending.TryGetValue(x, out int s) ? s : StateOf(x);
            var then = new List<KeyValuePair<string, string>>();   // events to fire after: (kind, name)
            var said = new List<string>();
            double now = MechClock.Now;
            foreach (TriggerAction a in t.Def.Do)
            {
                try
                {
                    switch (a.Verb)
                    {
                        case TriggerActions.Set: case TriggerActions.Next:
                        {
                            Mech m = MechOf(t.Room, a.Target);
                            if (m == null) { WarnOnce($"Dungeon {Site.Id}: trigger {t.Key}: no mechanism {a.Target}"); break; }
                            pending[m] = a.Verb == TriggerActions.Set ? Mathf.Clamp(a.To, 0, m.States - 1) : Layouts.After(m.Def, Cur(m), "next");
                            break;
                        }
                        case TriggerActions.Start:
                        {
                            Mech m = MechOf(t.Room, a.Target);
                            if (m?.Def.Encounter != null) StartEncounter(m, "trigger " + t.Key);
                            break;
                        }
                        case TriggerActions.Spawn:
                            said.Add(SpawnRole(t, a));
                            break;
                        case TriggerActions.Sound:
                            foreach (Vector3 p in Points(t, a)) Notice("sound", a.Target, p, -1);
                            break;
                        case TriggerActions.Message:
                            Notice("message", a.Target, RoomPointOf(t.Room, Vector3.zero), a.ToWhom == "dungeon" ? -1 : t.Room);
                            break;
                        case TriggerActions.Music:
                            PutTrig("m", string.IsNullOrEmpty(a.Target) ? null : TriggerState.Clean(a.Target));
                            break;
                        case TriggerActions.Counter:
                        {
                            string c = Scoped(t.Room, a.Target);
                            int was = Counter(c), to = a.HasSet ? a.To : was + a.Add;
                            if (to == was) break;
                            PutTrig("c|" + c, to.ToString());
                            then.Add(new KeyValuePair<string, string>(TriggerEvents.Counter, c));
                            break;
                        }
                        case TriggerActions.Reset:
                            ResetGroup(t.Room, a.Target, pending);
                            break;
                        case TriggerActions.Timer:
                        {
                            string name = Scoped(t.Room, a.Target);
                            if (a.Stop) PutTrig("t|" + name, null);
                            else PutTrig("t|" + name, TriggerState.F(now + (a.After > 0f ? a.After : a.Every)) + "," + TriggerState.F(Math.Max(0f, a.Every)));
                            break;
                        }
                        case TriggerActions.Signal:
                            then.Add(new KeyValuePair<string, string>(TriggerEvents.Signal, Scoped(t.Room, a.Target)));
                            break;
                        case TriggerActions.Item:
                            foreach (Vector3 p in Points(t, a)) said.Add(DropItem(a.Target, p, a.Count));
                            break;
                    }
                }
                catch (Exception e) { WarnOnce($"Dungeon {Site.Id}: trigger {t.Key}, action {a.Verb} {a.Target}: {e}"); }
            }
            if (pending.Count > 0) Change(pending.ToList(), $"trigger {t.Key}");
            Plugin.Log.LogInfo($"Dungeon {Site.Id}: trigger {t.Key} fired ({why}): {string.Join(", ", t.Def.Do.Select(a => a.Verb + " " + a.Target))}"
                               + (said.Count > 0 ? " (" + string.Join("; ", said.Where(s => !string.IsNullOrEmpty(s))) + ")" : ""));
            foreach (KeyValuePair<string, string> ev in then) Fire(ev.Key, name: ev.Value, depth: depth + 1);
        }

        /// <summary>Where an action happens: its "at" points (room frame), else the trigger's first area or mechanism, else the room's middle.</summary>
        private IEnumerable<Vector3> Points(Trig t, TriggerAction a)
        {
            if (a.At.Count > 0) return a.At.Select(p => RoomPointOf(t.Room, p));
            TriggerEvent area = t.Def.On.FirstOrDefault(e => e.Size != Vector3.zero);
            if (area != null) return new[] { RoomPointOf(t.Room, area.At) };
            Mech m = t.Mechs.Where(k => k != null).Select(k => ByKey.TryGetValue(k, out Mech x) ? x : null).FirstOrDefault(x => x != null);
            return new[] { m != null ? m.WorldPos + Vector3.up : RoomPointOf(t.Room, Vector3.zero) };
        }

        /// <summary>A point of a room's frame in the world.</summary>
        private Vector3 RoomPointOf(int room, Vector3 p)
        {
            PlacedRoom r = Layout.Rooms[room];
            return Builder.WorldPos(Site.Built, r) + Builder.WorldRot(r) * p;
        }

        /// <summary>A world point in a room's frame.</summary>
        private Vector3 InRoomFrame(int room, Vector3 world)
        {
            PlacedRoom r = Layout.Rooms[room];
            return Quaternion.Inverse(Builder.WorldRot(r)) * (world - Builder.WorldPos(Site.Built, r));
        }

        /// <summary>A point inside a room's box.</summary>
        private bool InRoom(int room, Vector3 world)
        {
            if (room < 0 || room >= Layout.Rooms.Count || Site.Built == null) return false;
            Vector3 l = InRoomFrame(room, world), h = Layout.Rooms[room].Def.Size * 0.5f;
            return Mathf.Abs(l.x) <= h.x && Mathf.Abs(l.y) <= h.y && Mathf.Abs(l.z) <= h.z;
        }

        /// <summary>A point in an event's area (its box in the room's frame; none: the whole room).</summary>
        private bool InArea(int room, TriggerEvent e, Vector3 world)
        {
            if (e.Size == Vector3.zero) return InRoom(room, world);
            if (Site.Built == null) return false;
            Vector3 l = InRoomFrame(room, world) - e.At, h = e.Size * 0.5f;
            return Mathf.Abs(l.x) <= h.x && Mathf.Abs(l.y) <= h.y && Mathf.Abs(l.z) <= h.z;
        }

        /// <summary>A reset group back to new: its triggers (fired, sequences, kills), its mechanisms to their start (pedestals give their
        /// item back first), its timers stopped.</summary>
        private void ResetGroup(int room, string group, Dictionary<Mech, int> pending)
        {
            string scoped = Scoped(room, group);
            var changes = new List<KeyValuePair<string, string>>();
            foreach (Trig t in Trigs().Where(x => !string.IsNullOrEmpty(x.Def.Group) && Scoped(x.Room, x.Def.Group) == scoped))
            {
                changes.Add(new KeyValuePair<string, string>("f|" + t.Key, null));
                changes.Add(new KeyValuePair<string, string>("q|" + t.Key, null));
                for (int i = 0; i < t.Def.On.Count; i++) changes.Add(new KeyValuePair<string, string>("k|" + t.Key + "|" + i, null));
            }
            PutTrig(changes);
            foreach (Mech m in All.Where(x => TriggerReader.GroupOf(x.Def) is string g && Scoped(x.Room, g) == scoped))
            {
                if (m.Def.Kind == "pedestal") GiveBackItem(m, false);
                else if (m.Def.Encounter == null) pending[m] = Mathf.Clamp(m.Def.Start, 0, m.States - 1);
            }
            Plugin.Log.LogInfo($"Dungeon {Site.Id}: group {scoped} reset");
        }

        /// <summary>The land's creatures of a role, at the action's points (spawn): fitted as encounters' are, never past the land's level,
        /// hunting the players.</summary>
        private string SpawnRole(Trig t, TriggerAction a)
        {
            int room = t.Room;
            if (Site.Built == null || a.At.Count == 0) return null;
            var rng = new System.Random(unchecked(Site.Seed * 31 + room * 7919 + (int)(MechClock.Now * 10)));
            int level = Mathf.Clamp(a.Level, 1, Builder.LevelCapOf(Site.Built));
            var made = new List<string>();
            for (int i = 0; i < Mathf.Clamp(a.Count, 1, 20); i++)
            {
                GameObject prefab = Builder.EncounterCreature(Site.Built, a.Target, rng, out _);
                if (prefab == null) continue;
                Vector3 at = RoomPointOf(room, a.At[i % a.At.Count]) + Vector3.up * 0.2f;
                GameObject go = Object.Instantiate(prefab, at, Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f));
                // tagged as an encounter's are (its dungeon's), so removing the dungeon takes them too (Sweep)
                go.GetComponent<ZNetView>()?.GetZDO()?.Set(TagHash, Site.Id + "|trigger " + t.Key);
                Character c = go.GetComponent<Character>();
                if (c != null && level > 1) c.SetLevel(level);
                go.GetComponent<BaseAI>()?.SetHuntPlayer(true);
                PlayAt("vfx_spawn", at);
                made.Add(prefab.name);
            }
            return made.Count > 0 ? "spawned " + string.Join(", ", made) : null;
        }

        /// <summary>The owner: an item dropped at a point (a key item bound to this dungeon, as it is now; a reset takes it away).</summary>
        private string DropItem(string name, Vector3 at, int count)
        {
            string prefabName = TriggerReader.ItemPrefab(Layout.Pack, name);
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
            if (prefab?.GetComponent<ItemDrop>() == null) { WarnOnce($"Dungeon {Site.Id}: no item {prefabName} to drop (a pack item not registered, or not a game item)"); return null; }
            bool key = BlockKeys.IsKey(prefabName);
            for (int i = 0; i < Mathf.Clamp(count, 1, 10); i++)
            {
                GameObject go = Object.Instantiate(prefab, at + Vector3.up * (0.3f + 0.2f * i), Quaternion.identity);
                ItemDrop drop = go.GetComponent<ItemDrop>();
                ZNetView view = go.GetComponent<ZNetView>();
                if (drop == null || view == null || !view.IsValid()) continue;
                if (key)
                {
                    BlockKeys.Bind(drop.m_itemData, Site);
                    ItemDrop.SaveToZDO(drop.m_itemData, view.GetZDO());
                    // one of this dungeon's parts, so a reset takes it away (with the generation and layout it was made in)
                    view.GetZDO().Set(Keys.PartOf, Site.Id);
                    view.GetZDO().Set(Keys.PartSig, Dungeons.Signature(Layout));
                    view.GetZDO().Set(Keys.PartGen, Site.Zdo.GetInt(Keys.Gen));
                }
            }
            return $"dropped {count} {prefabName}";
        }

        // ---- the owner, every frame

        /// <summary>The owner (from OwnerTick): state changes seen, areas, timers, braziers going out, one-shot traps ending.</summary>
        public void TriggersTick()
        {
            if (!Alive || !Site.IsOwner || !Complete) return;
            if (Time.time < _nextTrigger) return;
            _nextTrigger = Time.time + TriggerEvery;
            try
            {
                ReadRecords();
                ReadTriggers();
                double now = MechClock.Now;
                var pending = new Dictionary<Mech, int>();
                var events = new List<Action>();
                foreach (Mech m in All)
                {
                    int s = m.Def.Encounter != null ? EncounterOf(m).State : m.Def.Kind == "breakable" ? (IsBroken(m) ? 1 : 0) : StateOf(m);
                    // blocks that change by themselves (the owner writes it): a brazier burning out, a one-shot trap ending
                    BlockDef b = m.Def.Block;
                    if (b != null && s == 1)
                    {
                        MechMotion mo = Motion(m, now);
                        if (m.Def.Kind == "brazier" && b.Burn > 0f && now - mo.Start >= b.Burn) pending[m] = 0;
                        if (m.Def.Kind == "trap" && b.Every <= 0f && now - mo.Start >= b.For) pending[m] = 0;
                    }
                    if (!_lastSeen.TryGetValue(m, out int was)) { _lastSeen[m] = s; continue; }
                    if (was == s) continue;
                    _lastSeen[m] = s;
                    Mech seen = m;
                    events.Add(() => Fire(TriggerEvents.State, seen.Key, s));
                    if (m.Def.Kind == "brazier") events.Add(() => Fire(s == 1 ? TriggerEvents.Lit : TriggerEvents.Out, seen.Key));
                    if (m.Def.Kind == "breakable" && s == 1) events.Add(() => Fire(TriggerEvents.Broken, seen.Key));
                    if (m.Def.Encounter != null && s == EncounterRecord.Cleared) events.Add(() => Fire(TriggerEvents.Cleared, seen.Key));
                }
                if (pending.Count > 0) Change(pending.ToList(), "burnt out, or a trap's firing over");
                if (Trigs().Count == 0) return;
                foreach (Action ev in events) ev();
                Timers(now);
                if (Time.time >= _nextArea) { _nextArea = Time.time + AreaEvery; Areas(); }
            }
            catch (Exception e) { WarnOnce($"Dungeon {Site.Id}: its triggers: {e}"); }
        }

        private void Timers(double now)
        {
            foreach (KeyValuePair<string, string> kv in _trig.Where(k => k.Key.StartsWith("t|")).ToList())
            {
                string[] f = kv.Value.Split(',');
                double ring = TriggerState.D(f[0]), every = f.Length > 1 ? TriggerState.D(f[1]) : 0.0;
                if (now < ring) continue;
                PutTrig(kv.Key, every > 0.0 ? TriggerState.F(ring + every * Math.Max(1.0, Math.Ceiling((now - ring) / every + 1e-6))) + "," + TriggerState.F(every) : null);
                Fire(TriggerEvents.Timer, name: kv.Key.Substring(2));
            }
            // every N seconds of the server's clock (nothing saved: every game would count the same)
            foreach (Trig t in Trigs())
                for (int i = 0; i < t.Def.On.Count; i++)
                {
                    TriggerEvent e = t.Def.On[i];
                    if (e.Kind != TriggerEvents.Timer || e.Every <= 0f || !string.IsNullOrEmpty(e.Name)) continue;
                    long tick = (long)Math.Floor(now / e.Every);
                    string k = t.Key + "|" + i;
                    if (_everyTick.TryGetValue(k, out long last) && tick > last) Handle(t, i, 0);
                    _everyTick[k] = tick;
                }
        }

        /// <summary>Enter and leave: an area becoming occupied or empty, by living players (this game's loaded ones and those other games
        /// tell of). An area not seen yet counts as empty, so a player already in it enters it.</summary>
        private void Areas()
        {
            var at = Player.GetAllPlayers().Where(p => p != null && !p.IsDead()).Select(p => p.transform.position)
                .Concat(_playerAt.Values.Where(v => v.Value > Time.time).Select(v => v.Key)).ToList();
            foreach (Trig t in Trigs())
                for (int i = 0; i < t.Def.On.Count; i++)
                {
                    TriggerEvent e = t.Def.On[i];
                    if (e.Kind != TriggerEvents.Enter && e.Kind != TriggerEvents.Leave) continue;
                    string k = t.Key + "|" + i;
                    bool now = at.Any(p => InArea(t.Room, e, p));
                    bool known = _occupied.TryGetValue(k, out bool was);
                    _occupied[k] = now;
                    if ((known ? was : false) == now) continue;
                    if ((e.Kind == TriggerEvents.Enter && now) || (e.Kind == TriggerEvents.Leave && !now && known)) Handle(t, i, 0);
                }
        }

        // ---- what other games tell the owner (Dungeons.HandleAct: "blk:...")

        /// <summary>The owner: something a player's game says ("blk:" acts): where its player is, a creature that died, a block used.</summary>
        public void BlockAct(long sender, string act)
        {
            if (!Site.IsOwner || string.IsNullOrEmpty(act)) return;
            int colon = act.IndexOf(':');
            string verb = colon < 0 ? act : act.Substring(0, colon), rest = colon < 0 ? "" : act.Substring(colon + 1);
            try
            {
                ReadTriggers();
                switch (verb)
                {
                    case "pos": if (TryVec(rest, out Vector3 p)) _playerAt[sender] = new KeyValuePair<Vector3, float>(p, Time.time + PosFor); break;
                    case "kill": if (TryVec(rest, out Vector3 k)) Killed(k); break;
                    default: BlockUse(sender, verb, rest); break;   // (Block*.cs)
                }
            }
            catch (Exception e) { WarnOnce($"Dungeon {Site.Id}: act blk:{act}: {e}"); }
        }

        private void Killed(Vector3 at)
        {
            foreach (Trig t in Trigs())
                for (int i = 0; i < t.Def.On.Count; i++)
                {
                    TriggerEvent e = t.Def.On[i];
                    if (e.Kind != TriggerEvents.Kills || !InArea(t.Room, e, at)) continue;
                    if (!t.Def.Repeat && TrigValue("f|" + t.Key) != null) continue;
                    string k = "k|" + t.Key + "|" + i;
                    int n = TriggerState.I(TrigValue(k)) + 1;
                    bool enough = n >= Math.Max(1, e.Count);
                    PutTrig(k, enough && t.Def.Repeat ? null : n.ToString());
                    if (enough) Handle(t, i, 0);
                }
        }

        private static bool TryVec(string s, out Vector3 v)
        {
            v = Vector3.zero;
            string[] f = (s ?? "").Split(',');
            if (f.Length < 3) return false;
            v = new Vector3((float)TriggerState.D(f[0]), (float)TriggerState.D(f[1]), (float)TriggerState.D(f[2]));
            return true;
        }

        public static string Vec(Vector3 v) => TriggerState.F(v.x) + "," + TriggerState.F(v.y) + "," + TriggerState.F(v.z);

        /// <summary>Not the owner: where this game's player is, told once a second while they're inside (for the owner's areas).</summary>
        private void TellPosition()
        {
            Player p = Player.m_localPlayer;
            if (p == null || p.IsDead() || Site.IsOwner || Time.time < _nextTellPos || !HasTriggers) return;
            _nextTellPos = Time.time + TellPosEvery;
            if (Site.Contains(p.transform.position)) Site.Act("blk:pos:" + Vec(p.transform.position));
        }

        // ---- what every game sees and hears

        /// <summary>The music the triggers asked for, for a player at this point inside (null: none, or not inside).</summary>
        public string TriggerMusic(Vector3 at)
        {
            string m = TrigValue("m");
            return string.IsNullOrEmpty(m) || !Site.Contains(at) ? null : m;
        }

        /// <summary>The owner: a sound or a message for every game (room &gt;= 0: a message only for players in that room).</summary>
        private void Notice(string kind, string text, Vector3 at, int room)
        {
            if (ZRoutedRpc.instance == null || string.IsNullOrEmpty(text)) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, NoticeRpc, Site.Id, kind, text, at, room);
        }

        private static ZRoutedRpc _noticeFor;

        /// <summary>Registers Dungeons_TrigNotice once per ZRoutedRpc (every world has its own).</summary>
        public static void RegisterNotices()
        {
            if (ZRoutedRpc.instance == null || ZRoutedRpc.instance == _noticeFor) return;
            _noticeFor = ZRoutedRpc.instance;
            try { ZRoutedRpc.instance.Register<string, string, string, Vector3, int>(NoticeRpc, RPC_Notice); }
            catch (Exception e) { Plugin.Log.LogWarning("Registering the triggers' notices: " + e.Message); }
        }

        private static void RPC_Notice(long sender, string id, string kind, string text, Vector3 at, int room)
        {
            Site site = Dungeons.Sites.FirstOrDefault(s => !s.Gone && s.Id == id);
            Player p = Player.m_localPlayer;
            if (site?.Mechs == null || p == null) return;
            if (kind == "sound") { if (Vector3.Distance(p.transform.position, at) < 80f) PlayAt(text, at); return; }
            if (kind != "message") return;
            bool here = room >= 0 ? site.Mechs.InRoom(room, p.transform.position) : site.Contains(p.transform.position);
            if (here && Localization.instance != null) p.Message(MessageHud.MessageType.Center, Localization.instance.Localize(text));
        }

        // ---- a reset

        /// <summary>A reset (Forget): items on pedestals given back as items on the floor (never lost), and everything the triggers
        /// remember cleared.</summary>
        private void ForgetTriggers()
        {
            if (Site.IsOwner)
            {
                foreach (Mech m in All.Where(x => x.Def.Kind == "pedestal")) GiveBackItem(m, false);
                Site.Zdo?.Set(TriggerState.Key, "");
            }
            _trigText = null;
            _trig = new Dictionary<string, string>();
            _lastSeen.Clear();
            _occupied.Clear();
            _everyTick.Clear();
            _playerAt.Clear();
        }

        /// <summary>The dungeon is removed for good (Dungeons.Remove): what's on its pedestals dropped outside its entrance.</summary>
        public void GiveBack()
        {
            if (!Site.IsOwner) return;
            foreach (Mech m in All.Where(x => x.Def.Kind == "pedestal")) GiveBackItem(m, true);
        }
    }
}
