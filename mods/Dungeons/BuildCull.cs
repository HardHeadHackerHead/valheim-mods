using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Dungeons
{
    // Drawing only what can be seen. Valheim has no occlusion culling: from inside one dungeon every room of it, and of any other dungeon built
    // near by (several stacked 50-150 m apart over one base), was drawn wherever it fell in the view (30 FPS in the 0.3.2 test). So, on this
    // game only (nothing saved, nothing sent):
    // - a dungeon the viewer (the camera, or the local player) isn't inside draws nothing;
    // - inside one, only the rooms near the viewer are drawn: the room it's in and those within CullDoorways doorways of it (Layout.Links),
    //   and any room whose box comes within CullDistance m of it (other floors, a hall's void, a long view). Measured offline on 3 seeds each
    //   (tools/dungeons harness): Shifting Halls draws about 14 of its 52 rooms on average (25 at most), testtheme about 11 of 30. A room
    //   leaving that set is hidden only after CullLinger s (a room missing is worse than a few frames).
    // Hidden means Renderer.forceRenderingOff (meshes and particles; it leaves `enabled` to whoever else sets it: Runtime's broken walls,
    // Fireplaces) and Light.cullingMask 0 (it lights nothing and casts no shadow; LightLod and LightFlicker keep running their own fields).
    // Hidden rooms also stop costing time (Runtime, after Rimeforge's ~500 fire pieces held the game at 6 FPS with every room hidden): their
    // particle systems are paused, and the purely cosmetic per-frame behaviours (CosmeticTypes: flickering and LOD lights, smoke spawners,
    // wind) are switched off; the ones that were running are started again when the room is shown, and only those.
    // And their renderers and LODGroups are switched off (enabled = false, each one that was on, put back on when shown): forceRenderingOff
    // only skips the draw, the renderer stays in Unity's culling and its LODGroup is still worked out for every camera each frame, which
    // grew with a hidden dungeon's part count (Rootdeep's 6458 logs, each with a LODGroup: 25 FPS down to 8-15). None of our code switches
    // Renderer.enabled or LODGroup.enabled (breakables and triggers use SetActive, which is apart from it); forceRenderingOff stays too.
    // Colliders, sounds and the saved parts (chests, creatures, the game rooms' networked parts) are untouched: nothing falls, nothing
    // changes for other players. Runtime's mechanisms (quaddungeon_mech <room>.<id>) go with their room; anything else under the dungeon's
    // root (the way out) with the entrance room.
    internal static partial class Builder
    {
        /// <summary>Culling on (false: everything drawn, as before).</summary>
        internal static bool Culling = true;
        /// <summary>Rooms within this many doorways of the viewer's room are drawn.</summary>
        internal static int CullDoorways = 2;
        /// <summary>Rooms whose box is within this many metres of the viewer are drawn.</summary>
        internal static float CullDistance = 40f;
        /// <summary>A room stays drawn this long after it leaves the drawn set.</summary>
        internal static float CullLinger = 1.0f;

        /// <summary>Draws the rooms a player arriving at this point will see, at once, and keeps the point as a viewer for ArriveSeconds
        /// (a teleport takes a moment: until it lands, the camera and the player are still where they were). Called before a teleport into or
        /// within the dungeon, so nothing is missing on arrival.</summary>
        internal static void Reveal(Built built, Vector3 at)
        {
            ArriveAt = at;
            ArriveUntil = Time.time + ArriveSeconds;
            DungeonCull c = built?.Root != null ? built.Root.GetComponent<DungeonCull>() : null;
            if (c != null) c.Reveal(at);
        }

        /// <summary>A teleport of ours is taking the local player to this point (Safety.OurTeleport calls it): for ArriveSeconds, the
        /// dungeon there draws as if the viewer already stood there too, and it's drawn at once.</summary>
        internal static void Arriving(Vector3 at)
        {
            ArriveAt = at;
            ArriveUntil = Time.time + ArriveSeconds;
            foreach (Built b in All) Reveal(b, at);
        }
        internal static Vector3? ArriveAt;
        internal static float ArriveUntil;
        internal const float ArriveSeconds = 2f;


        private static void StartCulling(Built built)
        {
            if (built?.Root == null || built.Root.GetComponent<DungeonCull>() != null) return;
            built.Root.AddComponent<DungeonCull>().Built = built;
        }

        /// <summary>`dungeon perf`: the frame rate, and for each dungeon built here what's drawn.</summary>
        internal static string PerfReport()
        {
            var sb = new StringBuilder();
            float dt = Time.smoothDeltaTime;
            sb.AppendLine($"{(dt > 0 ? 1f / dt : 0):0} FPS ({dt * 1000f:0.0} ms a frame, smoothed); culling {(Culling ? "on" : "off")}: "
                          + $"{CullDoorways} doorways, {CullDistance:0} m, linger {CullLinger:0.0} s");
            foreach (Built b in All)
            {
                if (b?.Root == null) continue;
                DungeonCull c = b.Root.GetComponent<DungeonCull>();
                int renderers = 0, drawn = 0, lights = 0, lit = 0, lightsAll = 0, systems = 0, playing = 0;
                foreach (Renderer r in b.Root.GetComponentsInChildren<Renderer>(true))   // (inactive ones too, as the culling counts them)
                {
                    renderers++;
                    if (r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy) drawn++;
                }
                int lods = 0, lodsOn = 0;
                foreach (LODGroup g in b.Root.GetComponentsInChildren<LODGroup>(true)) { lods++; if (g.enabled) lodsOn++; }
                foreach (Light l in b.Root.GetComponentsInChildren<Light>(false))
                {
                    lightsAll++;
                    if (!l.enabled) continue;
                    lights++;
                    if (l.cullingMask != 0) lit++;
                }
                foreach (ParticleSystem ps in b.Root.GetComponentsInChildren<ParticleSystem>(false)) { systems++; if (ps.isPlaying) playing++; }
                var byType = new Dictionary<string, int>();
                foreach (MonoBehaviour mb in b.Root.GetComponentsInChildren<MonoBehaviour>(false))
                {
                    if (mb == null || !mb.isActiveAndEnabled) continue;
                    string t = mb.GetType().Name;
                    byType[t] = byType.TryGetValue(t, out int n) ? n + 1 : 1;
                }
                int rooms = b.Rooms?.Count(r => r != null) ?? 0;
                sb.AppendLine($"  {b.Layout?.Pack?.Name} {b.Layout?.Seed} at y {b.Origin.y:0}: {(c != null && c.Inside ? "inside" : "outside")}, "
                              + $"rooms {c?.Shown ?? rooms} drawn / {rooms - (c?.Shown ?? rooms)} hidden, renderers {drawn} drawn of {renderers}, "
                              + $"lights {lit} lit of {lights} on ({lightsAll} in all), {b.Batched} meshes batched{(b.Done ? "" : ", still building")}");
                sb.AppendLine($"    particle systems {playing} playing of {systems}; LODGroups {lodsOn} on of {lods}; switched off by culling: {c?.PausedSystems ?? 0} particle systems, "
                              + $"{c?.PausedBehaviours ?? 0} behaviours, {c?.OffRenderers ?? 0} renderers, {c?.OffLods ?? 0} LODGroups");
                if (c != null && c.Settled) sb.AppendLine("    settled: outside and all hidden, nothing to do each tick");
                sb.AppendLine("    behaviours on (top 10): " + (byType.Count == 0 ? "none"
                              : string.Join(", ", byType.OrderByDescending(kv => kv.Value).Take(10).Select(kv => $"{kv.Key} {kv.Value}"))));
            }
            if (All.Count == 0) sb.AppendLine("  no dungeon built here");

            sb.AppendLine($"  the whole game: smoke puffs {Smoke.GetTotalSmoke()}, smoke spawners running {SmokeSpawner.Instances.Count}, flickering lights {LightFlicker.Instances.Count}");
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>One built dungeon's culling (Builder: BuildCull.cs), on its root.</summary>
    internal class DungeonCull : MonoBehaviour
    {
        public Built Built;
        public bool Inside;
        public int Shown;
        public int PausedSystems => Sum(p => p.Stopped.Count);

        /// <summary>Settled: every viewer outside, everything hidden, nothing new under the root: the 0.2 s tick costs one box test.</summary>
        public bool Settled => _settled;
        public int PausedBehaviours => Sum(p => p.Paused.Count);
        public int OffRenderers => Sum(p => p.Off.Count);
        public int OffLods => Sum(p => p.LodsOff.Count);

        private int Sum(System.Func<Part, int> f) => _rooms.Where(p => p != null).Sum(f) + _extra.Sum(f);

        /// <summary>Purely cosmetic per-frame behaviours (the game's own): switched off in a hidden room, back on when it's shown. Each one
        /// leaves the game's update list as it's disabled (OnDisable) and comes back as it's enabled.</summary>
        private static readonly HashSet<string> CosmeticTypes = new HashSet<string>
        {
            "LightFlicker", "LightLod", "SmokeSpawner", "ObjectFlicker", "GlobalWind", "MaterialVariationWorld",
        };

        private class Part
        {
            public GameObject Root;
            public Renderer[] Renderers = new Renderer[0];
            public readonly List<Light> Lights = new List<Light>();
            public readonly List<int> Masks = new List<int>();   // each light's own culling mask, put back when shown
            public ParticleSystem[] Systems = new ParticleSystem[0];
            public Behaviour[] Cosmetic = new Behaviour[0];
            public readonly List<ParticleSystem> Stopped = new List<ParticleSystem>();   // paused while hidden: played again when shown
            public readonly List<Behaviour> Paused = new List<Behaviour>();              // switched off while hidden: on again when shown
            public LODGroup[] Lods = new LODGroup[0];
            public readonly List<Renderer> Off = new List<Renderer>();                   // renderers switched off while hidden: on again when shown
            public readonly List<LODGroup> LodsOff = new List<LODGroup>();               // LODGroups switched off while hidden: on again when shown
            public bool Hidden;
            public float Until;                        // drawn until then at least (linger)
        }

        private Part[] _rooms = new Part[0];
        private readonly List<Part> _extra = new List<Part>();   // mechanisms and the like, by room: (room, part)
        private readonly List<int> _extraRoom = new List<int>();   // NeverCull: drawn always
        private const int NeverCull = -1;
        private Bounds[] _boxes = new Bounds[0];
        private List<int>[] _links = new List<int>[0];
        private float _next;
        private bool _settled;                     // outside and all hidden: nothing to do until a viewer comes in or the root changes
        private Bounds _all;                       // the whole dungeon, a margin round it
        private int _mechCount = -1;
        private Transform _mechs;

        private void Setup()
        {
            Layout l = Built.Layout;
            int n = l.Rooms.Count;
            _rooms = new Part[n];
            _boxes = new Bounds[n];
            _links = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                _links[i] = new List<int>();
                PlacedRoom r = l.Rooms[i];
                Vector3 s = r.Def?.Size ?? Vector3.one * 4f;
                bool sideways = Mathf.Abs(Mathf.DeltaAngle(r.Yaw, 90f)) < 1f || Mathf.Abs(Mathf.DeltaAngle(r.Yaw, -90f)) < 1f;
                _boxes[i] = new Bounds(Built.Origin + r.Pos, sideways ? new Vector3(s.z, s.y, s.x) : s);
                GameObject go = i < Built.Rooms.Length ? Built.Rooms[i] : null;
                _rooms[i] = new Part { Root = go };
            }
            foreach (Link k in l.Links)
            {
                if (k.Room < 0 || k.Room >= n || k.OtherRoom < 0 || k.OtherRoom >= n) continue;
                _links[k.Room].Add(k.OtherRoom);
                _links[k.OtherRoom].Add(k.Room);
            }
            Bounds box = Builder.BoxOf(l);
            _all = new Bounds(Built.Origin + box.center, box.size + Vector3.one * 8f);
            _settled = false;
        }

        /// <summary>Runtime's mechanisms ("quaddungeon_mech &lt;room&gt;.&lt;id&gt;" under "quaddungeon_mechs") go with their room; anything else
        /// under "quaddungeon_mechs" with no room in its name (dungeon-boss's telegraph rings and dash lanes, seen from anywhere in a fight) is
        /// never culled; anything else the dungeon's root holds besides its rooms (the way out) goes with the entrance room. Gathered again
        /// when the root's children change (true then; childCount is the transform's own, cheap).</summary>
        private bool GatherExtras()
        {
            int count = transform.childCount + (_mechs != null ? _mechs.childCount : 0);
            if (count == _mechCount) return false;
            _mechCount = count;
            foreach (Part p in _extra) Show(p);
            _extra.Clear();
            _extraRoom.Clear();
            var rooms = new HashSet<GameObject>(Built.Rooms.Where(r => r != null));
            _mechs = transform.Find("quaddungeon_mechs");
            foreach (Transform child in transform)
            {
                if (rooms.Contains(child.gameObject) || child.GetComponent<EnvZone>() != null) continue;
                if (child == _mechs)
                {
                    foreach (Transform m in child)
                    {
                        int room = NeverCull;
                        string name = m.name;
                        int sp = name.LastIndexOf(' '), dot = name.IndexOf('.', sp + 1);
                        if (!name.StartsWith("quaddungeon_mech ", System.StringComparison.Ordinal) || sp < 0 || dot <= sp || !int.TryParse(name.Substring(sp + 1, dot - sp - 1), out room))
                            room = NeverCull;
                        _extra.Add(new Part { Root = m.gameObject });
                        _extraRoom.Add(room);
                    }
                    continue;
                }
                _extra.Add(new Part { Root = child.gameObject });
                _extraRoom.Add(0);
            }
            _mechCount = transform.childCount + (_mechs != null ? _mechs.childCount : 0);
            _settled = false;
            return true;
        }

        private void LateUpdate()
        {
            long timed = PerfTiming.Begin();   // (`dungeon perf`'s "ours by part")
            try { Tick(); }
            finally { PerfTiming.Add("culling: " + (Built?.Layout?.Pack?.Name ?? "?"), timed); }
        }

        private void Tick()
        {
            if (Built == null || !Built.Done || Built.Root == null) return;
            if (_rooms.Length != Built.Layout.Rooms.Count) Setup();
            if (Time.time < _next) return;
            _next = Time.time + 0.2f;
            GatherExtras();
            if (!Builder.Culling) { _settled = false; ShowAll(); return; }
            List<Vector3> viewers = Viewers();
            if (_settled && !viewers.Any(v => _all.Contains(v))) return;   // (outside, all hidden, nothing new: nothing to do)
            Apply(viewers);
        }

        /// <summary>The camera (what's seen: `dungeon tour` moves only the camera, a far third-person camera mustn't see empty rooms), the
        /// local player, and where a teleport of ours is taking it (Builder.Arriving, for a moment).</summary>
        private List<Vector3> Viewers()
        {
            var v = new List<Vector3>(3);
            Camera cam = Utils.GetMainCamera();
            if (cam != null) v.Add(cam.transform.position);
            if (Player.m_localPlayer != null) v.Add(Player.m_localPlayer.transform.position);
            if (Builder.ArriveAt.HasValue && Time.time < Builder.ArriveUntil) v.Add(Builder.ArriveAt.Value);
            return v;
        }

        private void OnDestroy()
        {
            // the dungeon still standing (a culling taken off: HotReload strips an old copy's behaviours off the dungeons it hands over, then
            // the new copy builds its own): leave nothing hidden. Uses nothing but this component's own lists, so it works with the plugin gone
            if (Built?.Root != null) ShowAll();
        }

        public void Reveal(Vector3 at)
        {
            if (Built == null || !Built.Done) return;
            if (_rooms.Length != Built.Layout.Rooms.Count) Setup();
            GatherExtras();
            _settled = false;
            Apply(new List<Vector3> { at }, keepShown: true);
        }

        private void Apply(List<Vector3> viewers, bool keepShown = false)
        {
            var want = new HashSet<int>();
            Inside = false;
            foreach (Vector3 p in viewers)
            {
                if (!_all.Contains(p)) continue;
                Inside = true;
                int at = RoomAt(p);
                if (at >= 0) foreach (int r in Near(at)) want.Add(r);
                float d2 = Builder.CullDistance * Builder.CullDistance;
                for (int i = 0; i < _boxes.Length; i++)
                    if (_boxes[i].SqrDistance(p) <= d2) want.Add(i);
            }
            float now = Time.time;
            Shown = 0;
            for (int i = 0; i < _rooms.Length; i++)
            {
                Part part = _rooms[i];
                if (want.Contains(i)) { part.Until = now + Builder.CullLinger; Show(part); }
                else if (!keepShown && (!Inside || now >= part.Until)) Hide(part);
                if (!part.Hidden) Shown++;
            }
            bool allHidden = Shown == 0;
            for (int k = 0; k < _extra.Count; k++)
            {
                int room = _extraRoom[k];
                bool shown = room == NeverCull || (room >= 0 && room < _rooms.Length ? !_rooms[room].Hidden : Inside);
                if (shown) Show(_extra[k]); else Hide(_extra[k]);
                if (room != NeverCull && !_extra[k].Hidden) allHidden = false;
            }
            _settled = !Inside && !keepShown && allHidden;
        }

        /// <summary>The room whose box holds the point, or the nearest one.</summary>
        private int RoomAt(Vector3 p)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < _boxes.Length; i++)
            {
                float d = _boxes[i].SqrDistance(p);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private IEnumerable<int> Near(int start)
        {
            var depth = new Dictionary<int, int> { [start] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                if (depth[u] >= Builder.CullDoorways) continue;
                foreach (int v in _links[u])
                    if (!depth.ContainsKey(v)) { depth[v] = depth[u] + 1; queue.Enqueue(v); }
            }
            return depth.Keys;
        }

        private void ShowAll()
        {
            Inside = true;
            _settled = false;
            foreach (Part p in _rooms) Show(p);
            foreach (Part p in _extra) Show(p);
            Shown = _rooms.Length;
        }

        /// <summary>What a part has to switch off, read as it goes from shown to hidden (Hide): always current, and never repeated while it
        /// stays hidden. (It used to be read again whenever Transform.hierarchyCount changed, but that's the whole root's count, which every
        /// room shares: anything added anywhere under the dungeon made every hidden room show, gather and hide again, 27-32 ms a time.) Show
        /// only puts back what Hide recorded. Something parented into a room while it's hidden is gathered at its next hide.</summary>
        private static void Gather(Part p)
        {
            if (p.Root == null) return;
            Transform root = p.Root.transform;
            p.Renderers = p.Root.GetComponentsInChildren<Renderer>(true).Where(r => !Boss(r.transform, root)).ToArray();
            p.Lights.Clear();
            p.Masks.Clear();
            foreach (Light l in p.Root.GetComponentsInChildren<Light>(true))
                if (!Boss(l.transform, root)) { p.Lights.Add(l); p.Masks.Add(l.cullingMask); }
            p.Systems = p.Root.GetComponentsInChildren<ParticleSystem>(true).Where(s => !Boss(s.transform, root)).ToArray();
            p.Cosmetic = p.Root.GetComponentsInChildren<Behaviour>(true).Where(b => b != null && CosmeticTypes.Contains(b.GetType().Name) && !Boss(b.transform, root)).ToArray();
            p.Lods = p.Root.GetComponentsInChildren<LODGroup>(true).Where(g => !Boss(g.transform, root)).ToArray();
        }

        /// <summary>Under one of dungeon-boss's objects ("quaddungeon_boss..."; its rings and lanes warn of a blow, seen from anywhere in a
        /// fight): never hidden, wherever it's parented.</summary>
        private static bool Boss(Transform t, Transform stop)
        {
            for (; t != null; t = t.parent)
            {
                if (t.name.StartsWith("quaddungeon_boss", System.StringComparison.Ordinal)) return true;
                if (t == stop) break;
            }
            return false;
        }

        private static void Hide(Part p)
        {
            if (p.Root == null || p.Hidden) return;
            Gather(p);
            p.Off.Clear();
            foreach (Renderer r in p.Renderers)
            {
                if (r == null) continue;
                r.forceRenderingOff = true;
                if (r.enabled) { r.enabled = false; p.Off.Add(r); }   // (out of Unity's culling altogether)
            }
            p.LodsOff.Clear();
            foreach (LODGroup g in p.Lods) if (g != null && g.enabled) { g.enabled = false; p.LodsOff.Add(g); }
            for (int i = 0; i < p.Lights.Count; i++)
            {
                Light l = p.Lights[i];
                if (l == null) continue;
                if (l.cullingMask != 0) p.Masks[i] = l.cullingMask;
                l.cullingMask = 0;
            }
            p.Stopped.Clear();
            foreach (ParticleSystem s in p.Systems)
                if (s != null && s.isPlaying) { s.Pause(false); p.Stopped.Add(s); }
            p.Paused.Clear();
            foreach (Behaviour b in p.Cosmetic)
                if (b != null && b.enabled) { b.enabled = false; p.Paused.Add(b); }
            p.Hidden = true;
        }

        private static void Show(Part p)
        {
            if (p.Root == null || !p.Hidden) return;
            foreach (LODGroup g in p.LodsOff) if (g != null) g.enabled = true;   // (LODGroups first: renderers back before them could show every LOD level for a frame)
            p.LodsOff.Clear();
            foreach (Renderer r in p.Off) if (r != null) r.enabled = true;
            p.Off.Clear();
            foreach (Renderer r in p.Renderers) if (r != null) r.forceRenderingOff = false;
            for (int i = 0; i < p.Lights.Count; i++) if (p.Lights[i] != null) p.Lights[i].cullingMask = p.Masks[i];
            foreach (Behaviour b in p.Paused) if (b != null) b.enabled = true;
            p.Paused.Clear();
            foreach (ParticleSystem s in p.Stopped) if (s != null) s.Play(false);
            p.Stopped.Clear();
            p.Hidden = false;
        }
    }
}
