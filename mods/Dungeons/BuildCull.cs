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
                int renderers = 0, drawn = 0, lights = 0, lit = 0;
                foreach (Renderer r in b.Root.GetComponentsInChildren<Renderer>(false))
                {
                    if (!r.enabled) continue;
                    renderers++;
                    if (!r.forceRenderingOff) drawn++;
                }
                foreach (Light l in b.Root.GetComponentsInChildren<Light>(false))
                {
                    if (!l.enabled) continue;
                    lights++;
                    if (l.cullingMask != 0) lit++;
                }
                int rooms = b.Rooms?.Count(r => r != null) ?? 0;
                sb.AppendLine($"  {b.Layout?.Pack?.Name} {b.Layout?.Seed} at y {b.Origin.y:0}: {(c != null && c.Inside ? "inside" : "outside")}, "
                              + $"rooms {c?.Shown ?? rooms} drawn / {rooms - (c?.Shown ?? rooms)} hidden, renderers {drawn} drawn of {renderers}, "
                              + $"lights {lit} lit of {lights}, {b.Batched} meshes batched{(b.Done ? "" : ", still building")}");
            }
            if (All.Count == 0) sb.AppendLine("  no dungeon built here");
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>One built dungeon's culling (Builder: BuildCull.cs), on its root.</summary>
    internal class DungeonCull : MonoBehaviour
    {
        public Built Built;
        public bool Inside;
        public int Shown;

        private class Part
        {
            public GameObject Root;
            public int Count = -1;                     // its hierarchyCount when last gathered: gathered again when it changes
            public Renderer[] Renderers = new Renderer[0];
            public readonly List<Light> Lights = new List<Light>();
            public readonly List<int> Masks = new List<int>();   // each light's own culling mask, put back when shown
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
        }

        /// <summary>Runtime's mechanisms ("quaddungeon_mech &lt;room&gt;.&lt;id&gt;" under "quaddungeon_mechs") go with their room; anything else
        /// under "quaddungeon_mechs" with no room in its name (dungeon-boss's telegraph rings and dash lanes, seen from anywhere in a fight) is
        /// never culled; anything else the dungeon's root holds besides its rooms (the way out) goes with the entrance room. Gathered again
        /// when the root's children change.</summary>
        private void GatherExtras()
        {
            int count = transform.childCount + (_mechs != null ? _mechs.childCount : 0);
            if (count == _mechCount) return;
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
        }

        private void LateUpdate()
        {
            if (Built == null || !Built.Done || Built.Root == null) return;
            if (_rooms.Length != Built.Layout.Rooms.Count) Setup();
            if (Time.time < _next) return;
            _next = Time.time + 0.2f;
            GatherExtras();
            if (!Builder.Culling) { ShowAll(); return; }
            Apply(Viewers());
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
            Apply(new List<Vector3> { at }, keepShown: true);
        }

        private void Apply(List<Vector3> viewers, bool keepShown = false)
        {
            Bounds all = Builder.BoxOf(Built.Layout);
            all = new Bounds(Built.Origin + all.center, all.size + Vector3.one * 8f);
            var want = new HashSet<int>();
            Inside = false;
            foreach (Vector3 p in viewers)
            {
                if (!all.Contains(p)) continue;
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
            for (int k = 0; k < _extra.Count; k++)
            {
                int room = _extraRoom[k];
                bool shown = room == NeverCull || (room >= 0 && room < _rooms.Length ? !_rooms[room].Hidden : Inside);
                if (shown) Show(_extra[k]); else Hide(_extra[k]);
            }
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
            foreach (Part p in _rooms) Show(p);
            foreach (Part p in _extra) Show(p);
            Shown = _rooms.Length;
        }

        private static void Gather(Part p)
        {
            if (p.Root == null) return;
            int count = p.Root.transform.hierarchyCount;
            if (count == p.Count) return;
            bool hidden = p.Hidden;
            if (hidden) Show(p);
            p.Count = count;
            Transform root = p.Root.transform;
            p.Renderers = p.Root.GetComponentsInChildren<Renderer>(true).Where(r => !Boss(r.transform, root)).ToArray();
            p.Lights.Clear();
            p.Masks.Clear();
            foreach (Light l in p.Root.GetComponentsInChildren<Light>(true))
                if (!Boss(l.transform, root)) { p.Lights.Add(l); p.Masks.Add(l.cullingMask); }
            if (hidden) Hide(p);
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
            if (p.Root == null) return;
            Gather(p);
            if (p.Hidden) return;
            foreach (Renderer r in p.Renderers) if (r != null) r.forceRenderingOff = true;
            for (int i = 0; i < p.Lights.Count; i++)
            {
                Light l = p.Lights[i];
                if (l == null) continue;
                if (l.cullingMask != 0) p.Masks[i] = l.cullingMask;
                l.cullingMask = 0;
            }
            p.Hidden = true;
        }

        private static void Show(Part p)
        {
            if (p.Root == null || !p.Hidden) { if (p.Root != null && p.Count < 0) Gather(p); return; }
            foreach (Renderer r in p.Renderers) if (r != null) r.forceRenderingOff = false;
            for (int i = 0; i < p.Lights.Count; i++) if (p.Lights[i] != null) p.Lights[i].cullingMask = p.Masks[i];
            p.Hidden = false;
        }
    }
}
