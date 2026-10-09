using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Ziplines
{
    /// <summary>
    /// A Zipline Post: what it does when you look at it (what E will do), press E on it (start a line, finish one, or ride), and the rope it
    /// draws. Each post keeps its own id, its partner's id and where its partner's pulley is in its saved data (not the game's object ids, which
    /// change every time a world loads), so a line is found again whenever either end is loaded, and the far post need not be: you can ride a
    /// line a few hundred metres long, and the world around you loads as you go. Both posts hold the link, so whichever you press E on, you
    /// ride toward the other.
    /// </summary>
    internal class Post : MonoBehaviour, Hoverable, Interactable
    {
        private const string IdKey = "dhz_id", ToKey = "dhz_to", ToPosKey = "dhz_tp";

        private static readonly List<Post> All = new List<Post>();

        // The post a line is being run from (E on the first of two): kept by the game's id for this session only (never saved), so it is still
        // found when you have walked a long way to the other post and the first is no longer loaded.
        private static ZDOID _pendingZdo = ZDOID.None;
        private static long _pendingId;
        private static Vector3 _pendingAnchor;
        private static float _pendingAt;

        private ZNetView _view;
        private LineRenderer _rope;
        private float _next;
        private long _ropeTo;
        private Vector3 _ropeA, _ropeB;
        private static Material _ropeMaterial;

        public static IEnumerable<Post> Loaded() => All.Where(p => p != null);

        public static void ForgetAll() { _pendingZdo = ZDOID.None; _pendingId = 0; foreach (Post p in All.ToList()) if (p != null) p.DropRope(); }

        private void Awake() { _view = GetComponent<ZNetView>(); All.Add(this); }

        private void OnDestroy() { All.Remove(this); DropRope(); }

        private ZDO Zdo() => _view != null && _view.IsValid() ? _view.GetZDO() : null;
        public long Id => Zdo()?.GetLong(IdKey, 0L) ?? 0L;
        public long ToId => Zdo()?.GetLong(ToKey, 0L) ?? 0L;
        public Post Partner { get { long to = ToId; return to == 0L ? null : All.FirstOrDefault(p => p != null && p != this && p.Id == to); } }

        /// <summary>The far end of the line: the post itself if it is here, else where the line remembers it (the far post need not be loaded).</summary>
        public Vector3 PartnerAnchor
        {
            get
            {
                Post p = Partner;
                if (p != null) return Line.Anchor(p);
                return Zdo()?.GetVec3(ToPosKey, Vector3.zero) ?? Vector3.zero;
            }
        }

        public bool HasLine => ToId != 0L && PartnerAnchor != Vector3.zero;

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            ZDO zdo = Zdo();
            if (zdo == null) return;
            if (zdo.GetLong(IdKey, 0L) == 0L && _view.IsOwner()) zdo.Set(IdKey, NewId());   // (its own id, once, by whoever owns it)
            UpdateRope();
        }

        /// <summary>The post's own id, made now if it has none (for a post set up from outside, the test command).</summary>
        internal static long EnsureId(ZDO zdo)
        {
            long id = zdo.GetLong(IdKey, 0L);
            if (id == 0L) { id = NewId(); zdo.Set(IdKey, id); }
            return id;
        }

        /// <summary>Takes this post's line down (its other end is left as it is).</summary>
        internal void ClearLine()
        {
            if (_view != null && !_view.IsOwner()) _view.ClaimOwnership();
            Zdo()?.Set(ToKey, 0L);
            Zdo()?.Set(ToPosKey, Vector3.zero);
        }

        private static long NewId() => BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0) & long.MaxValue;

        // ---- the line being run -------------------------------------------------------------------------------------------

        /// <summary>The post a line was started from, if it is still there and free (it need not be loaded).</summary>
        private static ZDO PendingZdo()
        {
            if (_pendingZdo == ZDOID.None || Time.time - _pendingAt > 21600f || ZDOMan.instance == null) return null;   // (six hours: a long walk to the other post)
            ZDO zdo = ZDOMan.instance.GetZDO(_pendingZdo);
            return zdo != null && zdo.IsValid() && zdo.GetLong(IdKey, 0L) == _pendingId && zdo.GetLong(ToKey, 0L) == 0L ? zdo : null;
        }

        // ---- looking at it ------------------------------------------------------------------------------------------------

        public string GetHoverName() => "Zipline Post";

        public float GetHoverOffset() => 0f;

        public string GetHoverText()
        {
            string use = "[<color=yellow><b>$KEY_Use</b></color>]", alt = "[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>]";
            string text;
            if (HasLine)
            {
                Vector3 far = PartnerAnchor;
                float length = Vector3.Distance(Line.Anchor(this), far), drop = Line.Anchor(this).y - far.y;
                string far0 = length >= 1000f ? $"{length / 1000f:0.0} km" : $"{length:0} m";
                text = drop >= 0.5f ? $"Zipline Post\n{use} Ride to the other post ({far0}, {drop:0} m down)\n{alt} Take the line down"
                                    : $"Zipline Post\nThe bottom of a line: it only runs downhill, so ride it from the other post ({far0}, {-drop:0} m up)\n{alt} Take the line down";
            }
            else if (ToId != 0L) text = $"Zipline Post\nThis line has lost its other end\n{alt} Take the line down";
            else
            {
                ZDO pending = PendingZdo();
                if (pending == null) text = $"Zipline Post\n{use} Start a line here";
                else if (pending == Zdo()) text = $"Zipline Post\n{use} Cancel the line";
                else text = $"Zipline Post\n{use} Run the line here ({Vector3.Distance(_pendingAnchor, Line.Anchor(this)):0} m from the other post)";
            }
            return Localization.instance.Localize(text);
        }

        // ---- pressing E ----------------------------------------------------------------------------------------------------

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            Plugin.Log.LogInfo($"Interact: hold {hold}, alt {alt}, user {user?.name}, hasLine {HasLine}");
            if (hold || !(user is Player player) || player != Player.m_localPlayer) return false;
            if (alt) { TakeDown(player); return true; }

            if (HasLine)
            {
                if (Line.Anchor(this).y - PartnerAnchor.y < 0.5f) { player.Message(MessageHud.MessageType.Center, "A zipline only runs downhill: ride this one from its other end."); return true; }
                Ride.Start(player, this, PartnerAnchor, Partner);
                return true;
            }
            if (ToId != 0L) { player.Message(MessageHud.MessageType.Center, "This line has lost its other end: take it down (Shift+E) and run it again."); return true; }

            ZDO zdo = Zdo();
            if (zdo == null) return true;
            ZDO pending = PendingZdo();
            if (pending == null)
            {
                if (zdo.GetLong(IdKey, 0L) == 0L) { player.Message(MessageHud.MessageType.Center, "This post is not ready yet: try again in a moment."); return true; }
                _pendingZdo = zdo.m_uid; _pendingId = zdo.GetLong(IdKey, 0L); _pendingAnchor = Line.Anchor(this); _pendingAt = Time.time;
                player.Message(MessageHud.MessageType.Center, "Line started: go to the other post and press E on it.");
                return true;
            }
            if (pending == zdo) { _pendingZdo = ZDOID.None; player.Message(MessageHud.MessageType.Center, "Line cancelled."); return true; }
            if (Join(player, pending, _pendingId, _pendingAnchor, this)) _pendingZdo = ZDOID.None;
            return true;
        }

        /// <summary>
        /// Run a line between two posts, the first held by its saved data only (it may be far off and not loaded). Whether it can be ridden is
        /// checked first (the length, the ground and what is in the way); the message says why not.
        /// </summary>
        internal static bool Join(Player player, ZDO farZdo, long farId, Vector3 farAnchor, Post here)
        {
            Vector3 hereAnchor = Line.Anchor(here);
            float length = Vector3.Distance(hereAnchor, farAnchor);
            if (here.ToId != 0L) { player.Message(MessageHud.MessageType.Center, "This post already has a line."); return false; }
            if (length < 8f) { player.Message(MessageHud.MessageType.Center, "These posts are too close together for a line (8 m at least)."); return false; }
            if (length > Plugin.MaxLength.Value) { player.Message(MessageHud.MessageType.Center, $"Too far: a line can be {Plugin.MaxLength.Value:0} m long at most, this one would be {length:0} m."); return false; }
            Post farPost = All.FirstOrDefault(p => p != null && p.Id == farId);
            // it must fall: the higher post is where you ride from
            float fall = Mathf.Abs(farAnchor.y - hereAnchor.y), needed = Mathf.Max(2f, length * Plugin.MinSlope.Value);
            if (fall < needed) { player.Message(MessageHud.MessageType.Center, $"A line has to run downhill: one post must be at least {needed:0} m higher than the other for {length:0} m (it is {fall:0} m). Build one on higher ground."); return false; }
            Vector3 top = farAnchor.y >= hereAnchor.y ? farAnchor : hereAnchor, bottom = farAnchor.y >= hereAnchor.y ? hereAnchor : farAnchor;
            string why = Line.Clearance(top, bottom, farPost, here);
            if (why != null) { player.Message(MessageHud.MessageType.Center, why); return false; }

            if (!here._view.IsOwner()) here._view.ClaimOwnership();
            ZDO mine = here.Zdo();
            long hereId = mine.GetLong(IdKey, 0L);
            if (hereId == 0L) { hereId = NewId(); mine.Set(IdKey, hereId); }
            mine.Set(ToKey, farId); mine.Set(ToPosKey, farAnchor);

            // the far post: its saved data is written from here (it is ours to write while no one else has it)
            if (farPost != null && !farPost._view.IsOwner()) farPost._view.ClaimOwnership();
            else if (farPost == null) farZdo.SetOwner(ZDOMan.GetSessionID());
            farZdo.Set(ToKey, hereId); farZdo.Set(ToPosKey, hereAnchor);
            player.Message(MessageHud.MessageType.Center, $"A line {length:0} m long runs between the posts. Press E on either one to ride it.");
            return true;
        }

        /// <summary>Two posts that are both here (the test command): a line between them.</summary>
        internal static bool Join(Player player, Post a, Post b)
        {
            ZDO za = a.Zdo(), zb = b.Zdo();
            if (za == null || zb == null || a.Id == 0L || b.Id == 0L) { player.Message(MessageHud.MessageType.Center, "A post is not ready yet."); return false; }
            return Join(player, za, a.Id, Line.Anchor(a), b);
        }

        private void TakeDown(Player player)
        {
            Post to = Partner;
            long far = ToId;
            foreach (Post p in new[] { this, to })
            {
                if (p == null) continue;
                if (!p._view.IsOwner()) p._view.ClaimOwnership();
                p.Zdo().Set(ToKey, 0L);
                p.Zdo().Set(ToPosKey, Vector3.zero);
            }
            if (to == null && far != 0L && ZDOMan.instance != null)
            {
                // the far post is not loaded: still known to the game by its saved data, so it is freed too
                foreach (ZDO z in FarPosts(far)) { z.SetOwner(ZDOMan.GetSessionID()); z.Set(ToKey, 0L); z.Set(ToPosKey, Vector3.zero); }
            }
            player.Message(MessageHud.MessageType.Center, "The line is down.");
        }

        /// <summary>The saved data of the post with this id, if the game has it (loaded or not).</summary>
        internal static IEnumerable<ZDO> FarPosts(long id)
        {
            var found = new List<ZDO>();
            int index = 0;
            int hash = Things.PostPrefab.GetStableHashCode();
            for (int guard = 0; guard < 2000 && !ZDOMan.instance.GetAllZDOsWithPrefabIterative(Things.PostPrefab, found, ref index); guard++) { }
            return found.Where(z => z != null && z.GetLong(IdKey, 0L) == id && z.GetPrefab() == hash);
        }

        // ---- the rope -------------------------------------------------------------------------------------------------------

        /// <summary>Drawn by the post with the lower id (or the one that is here, if the other is not): a line has one rope.</summary>
        private void UpdateRope()
        {
            if (!HasLine) { DropRope(); return; }
            Post to = Partner;
            if (to != null && Id > to.Id) { DropRope(); return; }
            Vector3 a = Line.Anchor(this), b = PartnerAnchor;
            if (_rope != null && _ropeTo == ToId && _ropeA == a && _ropeB == b) return;
            DropRope();

            var go = new GameObject("ZipRope");
            go.transform.SetParent(transform, false);
            _rope = go.AddComponent<LineRenderer>();
            _rope.useWorldSpace = true;
            _rope.widthMultiplier = 0.045f;
            _rope.numCapVertices = 3;
            _rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _rope.sharedMaterial = RopeMaterial();
            int points = Mathf.Clamp(Mathf.RoundToInt(Vector3.Distance(a, b) / 4f), 28, 500);
            _rope.positionCount = points + 1;
            for (int i = 0; i <= points; i++) _rope.SetPosition(i, Line.Point(a, b, i / (float)points));
            _ropeTo = ToId; _ropeA = a; _ropeB = b;
        }

        private void DropRope()
        {
            if (_rope != null) Destroy(_rope.gameObject);
            _rope = null; _ropeTo = 0L;
        }

        /// <summary>A plain tan: the game's wood item material with its picture taken off (as Quad's Cigars does).</summary>
        private static Material RopeMaterial()
        {
            if (_ropeMaterial != null) return _ropeMaterial;
            Material basis = null;
            GameObject wood = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Wood") : null;
            if (wood != null) foreach (Renderer r in wood.GetComponentsInChildren<Renderer>(true)) basis = basis ?? r.sharedMaterials.FirstOrDefault(m => m != null && m.name == "wood_item");
            basis = basis ?? new Material(Shader.Find("Standard") ?? Shader.Find("Sprites/Default"));
            _ropeMaterial = new Material(basis) { color = new Color(0.82f, 0.72f, 0.48f) };
            if (_ropeMaterial.HasProperty("_MainTex")) _ropeMaterial.mainTexture = null;
            return _ropeMaterial;
        }
    }
}
