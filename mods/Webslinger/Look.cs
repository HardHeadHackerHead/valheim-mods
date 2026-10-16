using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Webslinger
{
    /// <summary>
    /// What a web looks and sounds like, for every player in sight with one out (our own from Web.cs, the others' from their ZDO: Share.cs):
    /// a thin white line from the hand that flies out to its spot (wobbling as it goes), holds while it pulls, and goes slack and thin when
    /// let go. The sounds are our own (Sounds.cs), heard where they happen.
    /// Also the body: leaning along the web while swinging, facing the wall while crawling, the game's own "grappling" arm pose on a web (the
    /// game shares it with every player, mod or not), and the view widening a little at speed.
    /// </summary>
    internal static class Look
    {
        /// <summary>How fast a web flies out (metres per second): a 40 m web sticks in about a fifth of a second.</summary>
        public const float WebSpeed = 180f;
        private const float FadeSeconds = 0.35f;
        private const int Points = 16;
        /// <summary>How thick a web is (metres, at its middle: thicker at the hand).</summary>
        private const float Width = 0.065f;

        /// <summary>The animator flag set while on a web (a test can change it: web pose).</summary>
        public static string WebPose = "grappling";
        /// <summary>The animator flag set while crawling ("" none).</summary>
        public static string CrawlPose = "";
        /// <summary>The hand a web leaves from: the one on the anchor's side (0), or always the left (1) or the right (2): a test can set it.</summary>
        public static int HandTest;
        private static bool _lastRight;

        private sealed class Strand
        {
            public GameObject Go;
            public LineRenderer Line;
            public Player Owner;
            public Vector3 Anchor, Hand;
            public float ShotAt, Travel, LetGoAt = -1f;
            public bool Loose, Sounded, Right;
            public int Shot;
        }

        private static readonly Dictionary<Player, Strand> Strands = new Dictionary<Player, Strand>();
        private static readonly List<Strand> Fading = new List<Strand>();
        private static readonly Dictionary<Player, int> RemoteShot = new Dictionary<Player, int>();
        private static Material _material;
        private static string _pose = "";
        private static bool _fovOn;
        private static readonly AccessTools.FieldRef<GameCamera, float> FovBase = AccessTools.FieldRefAccess<GameCamera, float>("m_fovBase");

        public static void Reset()
        {
            foreach (Strand s in Strands.Values) Kill(s);
            foreach (Strand s in Fading) Kill(s);
            foreach (Hang h in Hangs) if (h.Go != null) Object.Destroy(h.Go);
            Hangs.Clear();
            Strands.Clear();
            Fading.Clear();
            RemoteShot.Clear();
            Player p = Player.m_localPlayer;
            if (p != null) { SetPose(p, ""); }
            if (_fovOn && GameCamera.instance != null) GameCamera.instance.ResetTempFOV();
            _fovOn = false;
            // (the material stays: it's ours, and a reload makes its own)
        }

        // ---- our own webs (Web.cs) ----

        public static void Shot(Player p, Vector3 anchor, bool zip = false)
        {
            Share.NewWeb();
            Begin(p, anchor, Web.Travel, loose: false, shot: Share.Shots, right: Side(p, anchor, alternate: true));
            Sounds.Play(zip ? Sfx.ZipThwip : Sfx.Thwip, Hand(p));
        }

        /// <summary>A web into nothing (a zip forward): flies out to the end, then goes slack.</summary>
        public static void Loose(Player p, Vector3 end)
        {
            Begin(p, end, Vector3.Distance(Hand(p), end) / WebSpeed, loose: true, shot: 0, right: Side(p, end, alternate: true));
            Sounds.Play(Sfx.ZipThwip, Hand(p));
        }

        /// <summary>A web holds: its thunk, and a web patch where it stuck.</summary>
        public static void Stick(Player p, Vector3 anchor)
        {
            Sounds.Play(Sfx.Stick, anchor);
            Fx.SplatAt(Hand(p), anchor, Suit.IsVenom(p));
        }

        /// <summary>The hand a new web leaves from: the one on the anchor's side; straight ahead, the other hand from the last web.</summary>
        private static bool Side(Player p, Vector3 anchor, bool alternate)
        {
            if (HandTest != 0) return HandTest == 2;
            float side = Vector3.Dot(anchor - p.transform.position, p.transform.right);
            bool right = side > 0.8f || (side >= -0.8f && (alternate ? !_lastRight : side >= 0f));
            if (alternate) _lastRight = right;
            return right;
        }

        /// <summary>A player's web out now (not one fading): where it's stuck, and which hand holds it.</summary>
        public static bool WebOf(Player p, out Vector3 anchor, out bool right)
        {
            anchor = Vector3.zero;
            right = false;
            if (p == null || !Strands.TryGetValue(p, out Strand s) || s.Loose) return false;
            anchor = s.Anchor;
            right = s.Right;
            return true;
        }

        public static void LetGo(Player p)
        {
            if (Strands.TryGetValue(p, out Strand s)) { Strands.Remove(p); Fade(s); }
            Sounds.Play(Sfx.Snap, Hand(p), 0.7f);
        }

        public static void Perfect(Player p)
        {
            Body.Flip(p);
            Sounds.Play(Sfx.Whoosh, p.transform.position + Vector3.up);
        }

        public static void Cling(Player p, Anchors.Wall wall) => Sounds.Play(Sfx.Cling, wall.Point);

        public static void Unstick(Player p) { }

        public static void Kick(Player p) => Sounds.Play(Sfx.Kick, p.transform.position + Vector3.up);

        // ---- the body ----

        /// <summary>Each physics step, after the move: the body turned for it, and the arm pose.</summary>
        public static void Pose(Player p, float dt)
        {
            Rigidbody body = Web.Body(p);
            Vector3 v = body.linearVelocity;
            switch (Web.State)
            {
                case Move.Swing:
                {
                    Vector3 up = Vector3.up;
                    if (Web.Stuck && Plugin.BodyLean.Value) up = Vector3.Slerp(Vector3.up, (Web.Anchor - Anchors.Pivot(p)).normalized, 0.75f).normalized;
                    Turn(p, up, v, dt, 8f);
                    SetPose(p, WebPose);
                    break;
                }
                case Move.Zip:
                {
                    Vector3 up = Web.Stuck && Plugin.BodyLean.Value ? Vector3.Slerp(Vector3.up, (Web.Anchor - Anchors.Pivot(p)).normalized, 0.5f).normalized : Vector3.up;
                    Turn(p, up, v, dt, 12f);
                    SetPose(p, WebPose);
                    break;
                }
                case Move.Crawl:
                {
                    Vector3 face = -Web.Wall.Normal; face.y = 0f;
                    if (face.sqrMagnitude > 0.01f) p.transform.rotation = Quaternion.Slerp(p.transform.rotation, Quaternion.LookRotation(face.normalized, Vector3.up), Mathf.Clamp01(15f * dt));
                    SetPose(p, CrawlPose);
                    break;
                }
                case Move.Fly:
                    Turn(p, Vector3.up, v, dt, 6f);
                    SetPose(p, "");
                    break;
                default:
                    SetPose(p, "");
                    break;
            }
        }

        private static void Turn(Player p, Vector3 up, Vector3 v, float dt, float rate)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(v, up);
            if (fwd.sqrMagnitude < 1f) fwd = Vector3.ProjectOnPlane(p.transform.forward, up);
            if (fwd.sqrMagnitude < 0.0001f) return;
            Quaternion to = Quaternion.LookRotation(fwd.normalized, up);
            p.transform.rotation = Quaternion.Slerp(p.transform.rotation, to, Mathf.Clamp01(rate * dt));
        }

        /// <summary>Upright again, facing the same way (a move left the body leaning).</summary>
        public static void StandUp(Player p)
        {
            Vector3 fwd = p.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Anchors.LookFlat(p);
            p.transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            SetPose(p, "");
        }

        private static void SetPose(Player p, string pose)
        {
            if (_pose == pose) return;
            ZSyncAnimation anim = p.GetZAnim();
            if (anim == null) return;
            if (_pose.Length > 0) anim.SetBool(_pose, false);
            if (pose.Length > 0) anim.SetBool(pose, true);
            _pose = pose;
        }

        // ---- every frame: the webs drawn ----

        public static void Late()
        {
            Player local = Player.m_localPlayer;
            // the other players' webs, from what they share
            foreach (Player other in Player.GetAllPlayers())
            {
                if (other == null || other == local) continue;
                if (local != null && Vector3.Distance(other.transform.position, local.transform.position) > 200f) { Drop(other); continue; }
                ZDO zdo = Share.ZdoOf(other);
                Vector3 anchor = Vector3.zero;
                int shot = 0;
                int kind = zdo != null ? Share.Read(zdo, out anchor, out shot) : Share.None;
                if (kind == Share.None) { Drop(other); RemoteShot.Remove(other); continue; }
                RemoteShot.TryGetValue(other, out int seen);
                if (seen == shot) continue;
                RemoteShot[other] = shot;
                Begin(other, anchor, Vector3.Distance(Hand(other), anchor) / WebSpeed, loose: kind == Share.Loose, shot: shot, right: Side(other, anchor, alternate: false));
                Sounds.Play(kind == Share.Zip || kind == Share.Loose ? Sfx.ZipThwip : Sfx.Thwip, Hand(other));
            }
            // players gone (logged out, out of range)
            List<Player> gone = null;
            foreach (KeyValuePair<Player, Strand> kv in Strands)
                if (kv.Key == null) (gone = gone ?? new List<Player>()).Add(kv.Key);
            if (gone != null) foreach (Player g in gone) { Fade(Strands[g]); Strands.Remove(g); }

            Body.Late();   // (the poses first: the webs are drawn from the hands where the pose put them)

            float now = Time.time;
            List<Player> done = null;
            foreach (Strand s in Strands.Values)
            {
                if (s.Owner != null) s.Hand = Hand(s.Owner, s.Right);
                float t = s.Travel > 0f ? (now - s.ShotAt) / s.Travel : 1f;
                if (t >= 1f && !s.Sounded)
                {
                    s.Sounded = true;
                    if (s.Owner != local && !s.Loose) Stick(s.Owner, s.Anchor);
                }
                if (s.Loose && t >= 1f) { (done = done ?? new List<Player>()).Add(s.Owner); continue; }
                Draw(s, Mathf.Clamp01(t), 0f);
            }
            if (done != null) foreach (Player d in done) { if (d != null && Strands.TryGetValue(d, out Strand s)) { Strands.Remove(d); Fade(s); } }

            for (int i = Fading.Count - 1; i >= 0; i--)
            {
                Strand s = Fading[i];
                float age = (now - s.LetGoAt) / FadeSeconds;
                if (age >= 1f || s.Line == null) { Kill(s); Fading.RemoveAt(i); continue; }
                Draw(s, 1f, age);
            }

            Swing(Time.deltaTime);
            if (local != null) Fov(local);
            Fx.Late(local);
            Sounds.Wind(local);
        }

        private static void Drop(Player other)
        {
            if (Strands.TryGetValue(other, out Strand s)) { Strands.Remove(other); Fade(s); Sounds.Play(Sfx.Snap, s.Hand, 0.7f); }
        }

        private static void Begin(Player owner, Vector3 anchor, float travel, bool loose, int shot, bool right)
        {
            if (Strands.TryGetValue(owner, out Strand old)) { Strands.Remove(owner); Fade(old); }
            var go = new GameObject("webslinger_web");
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = Points;
            line.widthMultiplier = Width;
            line.widthCurve = Taper;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = Material(Suit.IsVenom(owner));
            var s = new Strand { Go = go, Line = line, Owner = owner, Anchor = anchor, Hand = Hand(owner, right), ShotAt = Time.time, Travel = travel, Loose = loose, Shot = shot, Right = right };
            Strands[owner] = s;
            Draw(s, 0f, 0f);
        }

        private static void Fade(Strand s)
        {
            if (s == null || s.Line == null) return;
            s.LetGoAt = Time.time;
            // a web that stuck stays stuck: it drops from its spot and hangs there a while (WebTrails), else it goes slack and fades
            if (Plugin.WebTrails.Value && !s.Loose && Time.time - s.ShotAt >= s.Travel) { HangUp(s); return; }
            Fading.Add(s);
        }

        // ---- webs left hanging ----

        /// <summary>A web let go: hanging from where it stuck, a loose rope (points pulled down, each kept its length from the last).</summary>
        private sealed class Hang
        {
            public GameObject Go;
            public LineRenderer Line;
            public Vector3 Anchor;
            public Vector3[] Pos, Was;
            public float Segment, Born, Seed;
        }

        private static readonly List<Hang> Hangs = new List<Hang>();
        private const int HangPoints = 12, MostHanging = 24;
        private static readonly AnimationCurve Even = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.8f));

        private static void HangUp(Strand s)
        {
            if (Hangs.Count >= MostHanging) { if (Hangs[0].Go != null) Object.Destroy(Hangs[0].Go); Hangs.RemoveAt(0); }
            Vector3 from = s.Anchor, to = s.Hand;
            float length = Mathf.Min(Vector3.Distance(from, to), 10f);
            if (length < 0.5f) { Fading.Add(s); return; }
            Vector3 dir = (to - from).normalized;
            var h = new Hang { Go = s.Go, Line = s.Line, Anchor = from, Pos = new Vector3[HangPoints], Was = new Vector3[HangPoints],
                Segment = length / (HangPoints - 1), Born = Time.time, Seed = Random.Range(0f, 100f) };
            for (int i = 0; i < HangPoints; i++) h.Pos[i] = h.Was[i] = from + dir * (h.Segment * i);
            h.Line.positionCount = HangPoints;
            h.Line.widthCurve = Even;
            h.Line.widthMultiplier = Width * 0.8f;
            h.Line.SetPositions(h.Pos);
            Hangs.Add(h);
        }

        private static void Swing(float dt)
        {
            float life = Plugin.WebTrailSeconds.Value;
            dt = Mathf.Min(dt, 1f / 30f);
            Vector3 fall = Physics.gravity * 0.6f * dt * dt;
            for (int k = Hangs.Count - 1; k >= 0; k--)
            {
                Hang h = Hangs[k];
                float age = Time.time - h.Born;
                if (h.Line == null || age > life) { if (h.Go != null) Object.Destroy(h.Go); Hangs.RemoveAt(k); continue; }
                // a light breeze: each strand sways on its own
                Vector3 breeze = new Vector3(Mathf.Sin(Time.time * 0.9f + h.Seed), 0f, Mathf.Cos(Time.time * 0.7f + h.Seed * 1.3f)) * (0.4f * dt * dt);
                for (int i = 1; i < HangPoints; i++)
                {
                    Vector3 now = h.Pos[i];
                    h.Pos[i] += (now - h.Was[i]) * 0.97f + fall + breeze * i;
                    h.Was[i] = now;
                }
                for (int pass = 0; pass < 6; pass++)
                {
                    h.Pos[0] = h.Anchor;
                    for (int i = 1; i < HangPoints; i++)
                    {
                        Vector3 d = h.Pos[i] - h.Pos[i - 1];
                        float len = d.magnitude;
                        if (len < 0.0001f) continue;
                        Vector3 fix = d * ((len - h.Segment) / len);
                        if (i == 1) h.Pos[i] -= fix;
                        else { h.Pos[i] -= fix * 0.5f; h.Pos[i - 1] += fix * 0.5f; }
                    }
                }
                h.Pos[0] = h.Anchor;
                h.Line.SetPositions(h.Pos);
                float fade = Mathf.Clamp01((age - (life - 2f)) / 2f);
                h.Line.widthMultiplier = Width * 0.8f * (1f - fade);
            }
        }

        private static void Kill(Strand s)
        {
            if (s?.Go != null) Object.Destroy(s.Go);
            if (s != null) { s.Go = null; s.Line = null; }
        }

        /// <summary>
        /// The web from the hand: flying out (t below 1, its tip at t of the way, wobbling less as it goes), held (taut), or let go (slack:
        /// sagging and thinning as it fades).
        /// </summary>
        private static void Draw(Strand s, float t, float fade)
        {
            if (s.Line == null) return;
            Vector3 a = s.Hand, b = Vector3.Lerp(s.Hand, s.Anchor, t);
            Vector3 along = b - a;
            float length = along.magnitude;
            Vector3 side = length > 0.01f ? Vector3.Cross(along / length, Vector3.up) : Vector3.right;
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            side.Normalize();
            float wobble = (1f - t) * 0.25f, sag = fade * fade * Mathf.Min(length * 0.25f, 4f);
            for (int i = 0; i < Points; i++)
            {
                float u = i / (float)(Points - 1);
                Vector3 pt = Vector3.Lerp(a, b, u);
                pt += side * (Mathf.Sin(u * Mathf.PI * 3f + Time.time * 30f) * wobble * Mathf.Sin(u * Mathf.PI));
                pt += Vector3.down * (sag * 4f * u * (1f - u));
                s.Line.SetPosition(i, pt);
            }
            s.Line.widthMultiplier = Width * (1f - fade);
        }

        /// <summary>Thicker at the hand, thinner toward the spot it's stuck to.</summary>
        private static readonly AnimationCurve Taper = new AnimationCurve(new Keyframe(0f, 1.7f), new Keyframe(0.25f, 1.1f), new Keyframe(1f, 0.7f));

        /// <summary>The hand the player's web leaves from now (the web's own hand while one is out), else the chest.</summary>
        public static Vector3 Hand(Player p) => Hand(p, p != null && Strands.TryGetValue(p, out Strand s) ? s.Right : (HandTest == 2 || (HandTest == 0 && _lastRight)));

        public static Vector3 Hand(Player p, bool right)
        {
            VisEquipment vis = p != null ? p.GetVisEquipment() : null;
            Transform hand = vis != null ? (right ? vis.m_rightHand : vis.m_leftHand) : null;
            if (hand != null) return hand.position;
            return p != null ? Anchors.Pivot(p) : Vector3.zero;
        }

        private static Material _venom;

        /// <summary>A plain white web: the game's grappling hook wire, whitened (else a plain material); black for the Venom suit.</summary>
        private static Material Material(bool venom)
        {
            Material white = Material();
            if (!venom || white == null) return white;
            if (_venom != null) return _venom;
            _venom = new Material(white) { name = "webslinger_web_venom", color = new Color(0.05f, 0.05f, 0.07f) };
            if (_venom.HasProperty("_EmissionColor")) _venom.SetColor("_EmissionColor", new Color(0.03f, 0.035f, 0.06f));
            return _venom;
        }

        private static Material Material()
        {
            if (_material != null) return _material;
            Material basis = null;
            GameObject point = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("GrapplingPoint") : null;
            LineRenderer wire = point != null ? point.GetComponentInChildren<LineRenderer>(true) : null;
            if (wire != null) basis = wire.sharedMaterial;
            if (basis == null)
            {
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
                if (shader == null) return null;
                basis = new Material(shader);
            }
            _material = new Material(basis) { name = "webslinger_web", color = new Color(0.95f, 0.95f, 0.93f) };
            if (_material.HasProperty("_MainTex")) _material.mainTexture = null;
            if (_material.HasProperty("_EmissionColor")) { _material.EnableKeyword("_EMISSION"); _material.SetColor("_EmissionColor", new Color(0.55f, 0.55f, 0.55f)); }
            return _material;
        }

        // ---- the camera ----

        private static void Fov(Player p)
        {
            GameCamera cam = GameCamera.instance;
            if (cam == null) return;
            float speed = Web.State != Move.None ? Web.Body(p).linearVelocity.magnitude : 0f;
            if (Plugin.SpeedFov.Value && speed > 14f)
            {
                float basis = FovBase(cam) != 0f ? FovBase(cam) : cam.m_fov;
                cam.SetTempFOV(basis + Mathf.Min(14f, (speed - 14f) * 0.7f), 0.06f);
                _fovOn = true;
            }
            else if (_fovOn)
            {
                cam.ResetTempFOV();
                _fovOn = false;
            }
        }
    }
}
