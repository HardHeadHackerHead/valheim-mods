using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    /// <summary>One mechanism built in this game: its frame in the room, the pivot its moving parts hang from, its colliders.</summary>
    internal class Mech
    {
        public string Key;              // "<room index>.<mech id>"
        public int Room;
        public MechDef Def;
        public int States;
        public Transform Base;          // the room's frame + the mechanism's At/Euler
        public Transform Pivot;         // moved to the pose of the moment (local to Base)
        public readonly List<Collider> Colliders = new List<Collider>();

        // physics: the pivot's matrix at the last physics step, and how it moved in this one (carrying what stands on it)
        public Matrix4x4 Last;
        public bool HasLast, MovedThisStep;
        public Matrix4x4 Delta;

        // sound: the motion last heard (a new one starting near now plays its sound)
        public double HeardStart = double.NaN;
        public int HeardTo = -1;
        public EffectList OpenFx, CloseFx;

        // a plate (the owner): where something stands on it (in Base's frame), and whether something did at the last look
        public Bounds PlateBox;
        public bool Pressed, PressKnown;
        public float NextUse;           // a lever: not again before this (Time.time), so one press is one pull
        public float PulledHere = -10f; // a lever: when this game's player pulled it (its sound already played here)

        // parts that turn while it moves (PartDef.Spin): seen only, each game turns them by itself
        public readonly List<Spinner> Spinners = new List<Spinner>();
        public float SpinTime;          // seconds it has moved here, forwards less backwards

        public bool IsLever => Def.Kind == "lever";
        public bool IsPlate => Def.Kind == "plate";
        public bool IsGate => Layouts.IsGate(Def);

        public Vector3 WorldPos => Pivot != null ? Pivot.position : Base != null ? Base.position : Vector3.zero;

        public Pose PoseOf(int state)
        {
            if (Def.Poses.Count == 0) return null;
            return Def.Poses[Mathf.Clamp(state, 0, Def.Poses.Count - 1)];
        }
    }

    /// <summary>A part that turns while its mechanism moves (a gear): its rest rotation and degrees a second about its own axes.</summary>
    internal struct Spinner
    {
        public Transform Part;
        public Quaternion Rest;
        public Vector3 Spin;
    }

    /// <summary>
    /// A dungeon's mechanisms in this game (docs/dungeons-design.md, "Moving parts and puzzles"): made once its rooms are built, as local
    /// objects under its root (gone with it). Their state is in the entrance's ZDO (MechRecords) and their motion is worked out from it and
    /// the server's clock every frame, so every player sees them in the same place; only the entrance's owner changes the state:
    /// - a lever: a player's E sends Dungeons_Act "use:&lt;key&gt;" to the owner, who checks the sender stands near it, then turns the lever
    ///   and applies its wires (DungeonLever);
    /// - a plate: the owner looks every tenth of a second for a character standing on it ("hold" wires: on while pressed);
    /// - a cycling mechanism turns by itself on the server's clock: computed, never written.
    /// What stands on a moving part goes with it: each physics step, a character this game moves (the local player, the creatures it owns)
    /// whose ground is one of the part's colliders is moved by the part's own movement in that step. The game's own way (Character's
    /// ApplyGroundForce: the ground body's velocity, sideways only) needs a Rigidbody with a velocity and doesn't lift; our parts have no
    /// Rigidbody, so the game adds nothing and this does all of it, up and down and round included.
    /// </summary>
    internal class MechSet
    {
        public const float UseReach = 6f;        // how near a player must be to the lever they pull (the owner checks)
        private const float PlateEvery = 0.1f;

        public readonly Site Site;
        public readonly List<Mech> All = new List<Mech>();
        public readonly Dictionary<string, Mech> ByKey = new Dictionary<string, Mech>();
        private readonly Dictionary<Collider, Mech> _byCollider = new Dictionary<Collider, Mech>();
        private GameObject _root;
        private string _text;                    // the ZDO string the records were read from
        private Dictionary<string, MechRecord> _records = new Dictionary<string, MechRecord>();
        private float _nextPlates;
        private static readonly HashSet<string> Warned = new HashSet<string>();

        private MechSet(Site site) { Site = site; }

        public Layout Layout => Site.Layout;
        public bool Alive => _root != null;

        // ---- making them

        /// <summary>A built dungeon's mechanisms, to be made under its root (Grow, a few each frame). Null when its rooms have none.</summary>
        public static MechSet Make(Site site)
        {
            Built built = site.Built;
            Layout layout = site.Layout;
            if (built?.Root == null || layout == null || !layout.Rooms.Any(r => r.Def?.Mechs != null && r.Def.Mechs.Count > 0)) return null;
            var set = new MechSet(site);
            set._root = new GameObject("quaddungeon_mechs");
            set._root.transform.SetParent(built.Root.transform, false);
            foreach (PlacedRoom room in layout.Rooms)
                if (room.Def?.Mechs != null)
                    foreach (MechDef def in room.Def.Mechs) set._todo.Enqueue(new KeyValuePair<PlacedRoom, MechDef>(room, def));
            set.Grow();
            return set;
        }

        private readonly Queue<KeyValuePair<PlacedRoom, MechDef>> _todo = new Queue<KeyValuePair<PlacedRoom, MechDef>>();

        /// <summary>Every mechanism is made.</summary>
        public bool Complete => _todo.Count == 0;

        /// <summary>Makes mechanisms until this frame's building time is used (Builder.FrameBudgetMs; at least one each frame).</summary>
        public void Grow()
        {
            if (!Alive || _todo.Count == 0) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Built built = Site.Built;
            while (_todo.Count > 0 && clock.Elapsed.TotalMilliseconds < Builder.FrameBudgetMs)
            {
                KeyValuePair<PlacedRoom, MechDef> next = _todo.Dequeue();
                try { MakeOne(built, next.Key, next.Value); }
                catch (Exception e) { Plugin.Log.LogWarning($"Dungeon {Site.Id}: mechanism {next.Key.Index}.{next.Value.Id} ({next.Value.Kind}) couldn't be made: {e}"); }
            }
            if (_todo.Count > 0) return;
            Refresh(MechClock.Now, false);
            Plugin.Log.LogInfo($"Dungeon {Site.Id}: {All.Count} mechanisms made ({string.Join(", ", All.GroupBy(m => m.Def.Kind).Select(g => g.Count() + " " + g.Key))})");
        }

        private void MakeOne(Built built, PlacedRoom room, MechDef def)
        {
            if (built?.Root == null) return;
            var m = new Mech { Key = room.Index + "." + def.Id, Room = room.Index, Def = def, States = Math.Max(1, Layouts.States(def)) };
            var b = new GameObject("quaddungeon_mech " + m.Key);
            b.transform.SetParent(_root.transform, false);
            b.transform.SetPositionAndRotation(Builder.WorldPos(built, room) + Builder.WorldRot(room) * def.At, Builder.WorldRot(room) * Quaternion.Euler(def.Euler));
            m.Base = b.transform;
            var p = new GameObject("pivot");
            p.transform.SetParent(m.Base, false);
            m.Pivot = p.transform;
            Pose start = m.PoseOf(def.Start);   // (so a plate's area is measured where it rests)
            if (start != null) { m.Pivot.localPosition = start.At; m.Pivot.localRotation = Quaternion.Euler(start.Euler); }
            foreach (PartDef part in def.Parts)
            {
                GameObject made = Builder.MakePart(part, m.Pivot);
                if (made == null) continue;
                if (part.Spin != Vector3.zero) m.Spinners.Add(new Spinner { Part = made.transform, Rest = made.transform.localRotation, Spin = part.Spin });
                if (part.Saved) WarnOnce($"Mechanism {def.Id}: its part {part.Prefab} is marked saved, but moving parts are scenery (made as scenery)");
            }
            m.Colliders.AddRange(m.Pivot.GetComponentsInChildren<Collider>(true).Where(c => c != null && !c.isTrigger));

            // a gate must block its doorway when shut, whatever its parts are: one box over what shows of them, if nothing in them is solid
            if (m.IsGate && m.Colliders.Count == 0) AddBox(m, false);
            // a lever must be something to aim at
            if (m.IsLever && m.Pivot.GetComponentsInChildren<Collider>(true).Length == 0) AddBox(m, true);
            if (m.IsLever) p.AddComponent<DungeonLever>().Init(this, m.Key);
            if (m.IsPlate) m.PlateBox = PlateArea(m);

            foreach (Collider c in m.Colliders) _byCollider[c] = m;
            FindSounds(m);
            All.Add(m);
            ByKey[m.Key] = m;
        }

        /// <summary>A box over a mechanism's parts as they show (in its pivot's frame): solid, or one to aim at only (piece_nonsolid: players
        /// walk through it, the hover ray still finds it).</summary>
        private static void AddBox(Mech m, bool aimOnly)
        {
            Bounds? local = null;
            foreach (Renderer r in m.Pivot.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                Bounds wb = r.bounds;
                foreach (Vector3 corner in Corners(wb))
                {
                    Vector3 c = m.Pivot.InverseTransformPoint(corner);
                    if (local == null) local = new Bounds(c, Vector3.zero);
                    else { Bounds l = local.Value; l.Encapsulate(c); local = l; }
                }
            }
            Bounds box = local ?? new Bounds(new Vector3(0f, 0.6f, 0f), new Vector3(0.4f, 1.2f, 0.4f));
            box.size = Vector3.Max(box.size, Vector3.one * 0.2f);
            var go = new GameObject(aimOnly ? "quaddungeon_aim" : "quaddungeon_block");
            go.transform.SetParent(m.Pivot, false);
            int nonsolid = LayerMask.NameToLayer("piece_nonsolid");
            if (aimOnly && nonsolid >= 0) go.layer = nonsolid;
            BoxCollider bc = go.AddComponent<BoxCollider>();
            bc.center = box.center;
            bc.size = box.size;
            if (!aimOnly) m.Colliders.Add(bc);
        }

        private static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++)
                yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
        }

        /// <summary>Where a character presses a plate: over its parts (as they stand at its start), from a little below its top to a body's
        /// height above, in the mechanism's own frame (the plate sinking doesn't let go of whoever's on it).</summary>
        private static Bounds PlateArea(Mech m)
        {
            Bounds? local = null;
            var shapes = m.Pivot.GetComponentsInChildren<Collider>(true).Select(c => c.bounds)
                .Concat(m.Pivot.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer).Select(r => r.bounds));
            foreach (Bounds wb in shapes)
                foreach (Vector3 corner in Corners(wb))
                {
                    Vector3 c = m.Base.InverseTransformPoint(corner);
                    if (local == null) local = new Bounds(c, Vector3.zero);
                    else { Bounds l = local.Value; l.Encapsulate(c); local = l; }
                }
            Bounds box = local ?? new Bounds(Vector3.zero, new Vector3(1.5f, 0.2f, 1.5f));
            float top = box.max.y;
            var area = new Bounds();
            area.SetMinMax(new Vector3(box.min.x - 0.1f, top - 0.4f, box.min.z - 0.1f), new Vector3(box.max.x + 0.1f, top + 1.2f, box.max.z + 0.1f));
            return area;
        }

        // ---- the state

        /// <summary>Reads the records again when the ZDO's string changed.</summary>
        private void ReadRecords()
        {
            string text = Site.Zdo?.GetString(Keys.Mechs) ?? "";
            if (text == _text) return;
            _text = text;
            _records = MechRecords.Parse(text);
        }

        public MechMotion Motion(Mech m, double now)
        {
            bool has = _records.TryGetValue(m.Key, out MechRecord rec);
            return MechMotion.At(m.Def, m.States, has, rec, now);
        }

        /// <summary>The state a mechanism is in (or going to) now.</summary>
        public int StateOf(Mech m) { ReadRecords(); return Motion(m, MechClock.Now).To; }

        /// <summary>The owner: mechanisms to new states, written at once. A mechanism still moving finishes first (its new motion is queued
        /// after it), so nothing ever jumps.</summary>
        public void Change(IEnumerable<KeyValuePair<Mech, int>> changes, string why)
        {
            if (!Site.IsOwner) return;
            ReadRecords();
            double now = MechClock.Now;
            var all = new Dictionary<string, MechRecord>(_records);
            var said = new List<string>();
            foreach (KeyValuePair<Mech, int> kv in changes)
            {
                Mech m = kv.Key;
                int to = Mathf.Clamp(kv.Value, 0, m.States - 1);
                bool has = all.TryGetValue(m.Key, out MechRecord rec);
                MechMotion now0 = MechMotion.At(m.Def, m.States, has, rec, now);
                if (now0.To == to) continue;
                double end = now0.Start + Math.Max(0f, now0.Seconds);   // (a motion still going, or queued: the new one follows it)
                double begin = !double.IsInfinity(end) && end > now ? end : now;
                all[m.Key] = new MechRecord { To = to, From = now0.To, Time = begin };
                said.Add($"{m.Key} {now0.To}->{to}");
            }
            if (said.Count == 0) return;
            string text = MechRecords.Format(all);
            Site.Zdo.Set(Keys.Mechs, text);
            _text = text;
            _records = all;
            Plugin.Log.LogInfo($"Dungeon {Site.Id}: {why}: {string.Join(", ", said)}");
        }

        /// <summary>The owner: what using a mechanism does. A lever turns itself (when it has the states) and applies its wires; a plate
        /// (a command using it) applies its wires as if stepped on and off at once ("hold" acts as "next"); anything else goes one state
        /// on. False with why when it can't.</summary>
        public bool Use(string key, string who, out string why)
        {
            why = null;
            if (!ByKey.TryGetValue(key ?? "", out Mech m)) { why = $"no mechanism {key}"; return false; }
            ReadRecords();
            var pending = new Dictionary<Mech, int>();
            int Cur(Mech x) => pending.TryGetValue(x, out int s) ? s : Motion(x, MechClock.Now).To;
            if (Layouts.IsUsable(m.Def))
            {
                if (m.IsLever) pending[m] = Layouts.After(m.Def, Cur(m), "next");
                ApplyWires(m, pending, Cur, false, true);
            }
            else pending[m] = Layouts.After(m.Def, Cur(m), "next");
            Change(pending.ToList(), $"{who} used {key}");
            return true;
        }

        /// <summary>The owner: one mechanism to a state (the set command).</summary>
        public bool Set(string key, int state, out string why)
        {
            why = null;
            if (!ByKey.TryGetValue(key ?? "", out Mech m)) { why = $"no mechanism {key}"; return false; }
            if (state < 0 || state >= m.States) { why = $"{key} has states 0 to {m.States - 1}"; return false; }
            Change(new[] { new KeyValuePair<Mech, int>(m, state) }, "set");
            return true;
        }

        /// <summary>A lever's or plate's wires into pending. pressed: a plate going down (true) or up (false); "hold" wires follow it.</summary>
        private void ApplyWires(Mech m, Dictionary<Mech, int> pending, Func<Mech, int> cur, bool plate, bool pressed)
        {
            foreach (Wire w in m.Def.Wires)
            {
                string key = Layouts.Target(Layout, m.Room, w);
                if (key == null || !ByKey.TryGetValue(key, out Mech target))
                {
                    WarnOnce($"Dungeon {Site.Id}: {m.Key}'s wire to {w.Target} finds no mechanism");
                    continue;
                }
                string act = w.Do ?? "next";
                if (act == "hold")
                {
                    if (!plate) act = "next";
                    else act = pressed ? "set:1" : "set:0";
                }
                else if (plate && !pressed) continue;   // only on the way down
                pending[target] = Layouts.After(target.Def, cur(target), act);
            }
        }

        /// <summary>A player's E on a lever (an RPC to the owner): only from someone standing near it.</summary>
        public void UseFrom(long sender, string key)
        {
            if (!Site.IsOwner || !ByKey.TryGetValue(key ?? "", out Mech m)) return;
            // where the sender stands: their character if it's loaded here (a big dungeon can reach past what's loaded around its owner),
            // else (on the server) the position their game reports, which is a second or two old
            Player who = Player.GetAllPlayers().FirstOrDefault(p => p != null && p.GetOwner() == sender);
            if (who == null && sender == ZDOMan.GetSessionID()) who = Player.m_localPlayer;
            string name;
            float d, reach;
            if (who != null) { name = who.GetPlayerName(); d = Vector3.Distance(who.transform.position, m.WorldPos); reach = UseReach; }
            else
            {
                ZNetPeer peer = ZNet.instance != null ? ZNet.instance.GetPeer(sender) : null;
                if (peer == null)
                {
                    // a client owner knows nothing of a player out of its range: it takes the pull (all it can do), and says so
                    name = sender.ToString();
                    d = 0f;
                    reach = 1f;
                    Plugin.Log.LogInfo($"Dungeon {Site.Id}: {key} used by {sender}, who isn't loaded here: taken without checking where they stand");
                }
                else { name = peer.m_playerName; d = Vector3.Distance(peer.m_refPos, m.WorldPos); reach = UseReach * 3f; }
            }
            if (d > reach) { Plugin.Log.LogInfo($"Dungeon {Site.Id}: {name} used {key} from {d:0.0} m away: ignored"); return; }
            if (Time.time < m.NextUse) return;
            m.NextUse = Time.time + 0.5f;
            Use(key, name, out _);
        }

        // ---- every frame, every physics step

        /// <summary>Every frame: each mechanism to its pose of the moment; the sound of any that just started moving.</summary>
        public void Update()
        {
            if (!Alive) return;
            ReadRecords();
            double now = MechClock.Now;
            foreach (Mech m in All)
            {
                if (m.Pivot == null) continue;
                MechMotion mo = Motion(m, now);
                Place(m, mo, now);
                Hear(m, mo, now);
                if (m.Spinners.Count > 0) Turn(m, mo, now);
            }
        }

        /// <summary>Every physics step: each mechanism to its pose at this step, and whatever stands on one that moved carried with it.</summary>
        public void FixedUpdate(List<Character> characters)
        {
            if (!Alive) return;
            ReadRecords();
            double now = MechClock.Now;
            bool any = false;
            foreach (Mech m in All)
            {
                if (m.Pivot == null) continue;
                Place(m, Motion(m, now), now);
                Matrix4x4 at = m.Pivot.localToWorldMatrix;
                m.MovedThisStep = m.HasLast && !Same(at, m.Last);
                if (m.MovedThisStep) { m.Delta = at * m.Last.inverse; any = true; }
                m.Last = at;
                m.HasLast = true;
            }
            if (!any) return;
            // the game's own ground checks in this step (Character's) see the parts where they are now, not where they were
            Physics.SyncTransforms();
            if (characters == null) return;
            foreach (Character c in characters)
            {
                if (c == null || !c.IsOwner() || !c.IsOnGround() || c.IsAttached()) continue;
                Collider ground = c.GetLastGroundCollider();
                if (ground == null || !_byCollider.TryGetValue(ground, out Mech m) || !m.MovedThisStep) continue;
                Rigidbody body = CharacterBody(c);
                if (body == null) continue;
                Vector3 to = m.Delta.MultiplyPoint3x4(body.position);
                body.position = to;
            }
        }

        /// <summary>Its turning parts on by this frame's time: forwards while it goes on a state ("next", or up), backwards while it goes
        /// back; always forwards in a mechanism with one state.</summary>
        private static void Turn(Mech m, MechMotion mo, double now)
        {
            float way = 0f;
            if (m.States <= 1) way = 1f;
            else if (mo.Moving(now) && mo.From != mo.To)
                way = mo.To == (mo.From + 1) % m.States ? 1f : mo.From == (mo.To + 1) % m.States ? -1f : Mathf.Sign(mo.To - mo.From);
            if (way == 0f) return;
            m.SpinTime += way * Time.deltaTime;
            float t = m.SpinTime % 36000f;   // (whole turns for any spin of whole degrees a second, so no jump; and floats stay exact)
            foreach (Spinner s in m.Spinners)
                if (s.Part != null) s.Part.localRotation = s.Rest * Quaternion.Euler(s.Spin * t);
        }

        private static bool Same(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > 1e-6f) return false;
            return true;
        }

        private static void Place(Mech m, MechMotion mo, double now)
        {
            Pose from = m.PoseOf(mo.From), to = m.PoseOf(mo.To);
            if (to == null) return;
            float t = Mathf.SmoothStep(0f, 1f, mo.Progress(now));
            Vector3 at = Vector3.Lerp(from.At, to.At, t);
            Quaternion turn = Quaternion.Slerp(Quaternion.Euler(from.Euler), Quaternion.Euler(to.Euler), t);
            m.Pivot.localPosition = at;
            m.Pivot.localRotation = turn;
        }

        /// <summary>A motion that started in the last second, not heard yet: its sound, where it is (a gate its own door's, the rest the
        /// game's gate sound; a lever or plate the game's lever).</summary>
        private static void Hear(Mech m, MechMotion mo, double now)
        {
            if (m.HeardTo == mo.To && m.HeardStart == mo.Start) return;
            bool first = m.HeardTo < 0;
            if (!first && now < mo.Start - 0.05) return;   // (queued after the motion still going: heard when it starts)
            m.HeardTo = mo.To;
            m.HeardStart = mo.Start;
            if (first || mo.From == mo.To || now - mo.Start > 1.0) return;
            if (m.IsLever && Time.time - m.PulledHere < 2f) return;
            Play(m, mo.To != 0 ? m.OpenFx : m.CloseFx ?? m.OpenFx);
        }

        public static void Play(Mech m, EffectList fx)
        {
            if (fx == null || m.Pivot == null) return;
            try { fx.Create(m.WorldPos, m.Pivot.rotation); }
            catch (Exception e) { WarnOnce("A mechanism's sound: " + e.Message); }
        }

        // ---- the owner, often

        /// <summary>The owner, every frame: plates (a tenth of a second apart).</summary>
        public void OwnerTick()
        {
            if (!Alive || !Site.IsOwner || Time.time < _nextPlates) return;
            _nextPlates = Time.time + PlateEvery;
            List<Character> characters = Character.GetAllCharacters();
            var pending = new Dictionary<Mech, int>();
            var why = new List<string>();
            foreach (Mech m in All)
            {
                if (!m.IsPlate || m.Base == null) continue;
                bool pressed = characters.Any(c => c != null && !c.IsDead() && m.PlateBox.Contains(m.Base.InverseTransformPoint(c.transform.position)));
                if (!m.PressKnown)
                {
                    m.PressKnown = true;
                    m.Pressed = StateOf(m) > 0;   // (as it was left, so letting go of it still lets go of what it holds)
                }
                if (pressed == m.Pressed) continue;
                m.Pressed = pressed;
                int Cur(Mech x) => pending.TryGetValue(x, out int s) ? s : StateOf(x);
                if (m.States > 1) pending[m] = pressed ? 1 : 0;
                ApplyWires(m, pending, Cur, true, pressed);
                why.Add($"{m.Key} {(pressed ? "pressed" : "let go")}");
            }
            if (pending.Count > 0) Change(pending.ToList(), string.Join(", ", why));
        }

        // ---- helpers

        private static readonly AccessTools.FieldRef<Character, Rigidbody> Body = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");
        private static Rigidbody CharacterBody(Character c) => Body(c);

        /// <summary>The sounds: a gate or door the game's door its parts come from (its open and close sounds), or the iron gate's; a lever
        /// or plate the Obliterator's lever (the game's only lever, "Pull lever").</summary>
        private static void FindSounds(Mech m)
        {
            if (ZNetScene.instance == null) return;
            if (Layouts.IsUsable(m.Def))
            {
                m.OpenFx = m.CloseFx = LeverFx();
                return;
            }
            foreach (PartDef part in m.Def.Parts)
            {
                Door door = ZNetScene.instance.GetPrefab(part.Prefab ?? "")?.GetComponentInChildren<Door>(true);
                if (door == null) continue;
                m.OpenFx = door.m_openEffects;
                m.CloseFx = door.m_closeEffects;
                return;
            }
            Door gate = ZNetScene.instance.GetPrefab("iron_grate")?.GetComponentInChildren<Door>(true);
            if (gate != null) { m.OpenFx = gate.m_openEffects; m.CloseFx = gate.m_closeEffects; }
        }

        private static EffectList _leverFx;

        /// <summary>The Obliterator's lever sounds (its effects that are sounds only: the lightning stays with the Obliterator).</summary>
        public static EffectList LeverFx()
        {
            if (_leverFx != null || ZNetScene.instance == null) return _leverFx;
            Incinerator inc = ZNetScene.instance.GetPrefab("incinerator")?.GetComponentInChildren<Incinerator>(true);
            if (inc?.m_leverEffects?.m_effectPrefabs == null) { WarnOnce("The game's lever sound (the Obliterator's) wasn't found: levers are silent"); return null; }
            _leverFx = new EffectList
            {
                m_effectPrefabs = inc.m_leverEffects.m_effectPrefabs
                    .Where(e => e != null && e.m_prefab != null && e.m_prefab.GetComponentInChildren<ZSFX>(true) != null).ToArray(),
            };
            if (_leverFx.m_effectPrefabs.Length == 0) WarnOnce("The Obliterator's lever has no sound effect: levers are silent");
            return _leverFx;
        }

        public static void ForgetSounds() => _leverFx = null;

        private static void WarnOnce(string message)
        {
            if (Warned.Add(message)) Plugin.Log.LogWarning(message);
        }

        public void Refresh(double now, bool sounds)
        {
            ReadRecords();
            foreach (Mech m in All)
            {
                if (m.Pivot == null) continue;
                MechMotion mo = Motion(m, now);
                Place(m, mo, now);
                if (!sounds) { m.HeardTo = mo.To; m.HeardStart = mo.Start; }
            }
        }

        /// <summary>A reset: plates and levers forget what they knew (every mechanism is back at its start).</summary>
        public void Forget()
        {
            foreach (Mech m in All) { m.PressKnown = m.Pressed = false; m.NextUse = 0f; }
            _text = null;
        }

        public void Remove()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            All.Clear();
            ByKey.Clear();
            _byCollider.Clear();
        }
    }
}
