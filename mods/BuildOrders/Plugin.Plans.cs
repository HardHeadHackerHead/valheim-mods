using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// Plans: the build orders grouped by where they came from (a placed blueprint, or the pieces a player planned by hand), and placement
    /// mode: a whole blueprint (or a plan being moved) shown as a green preview that follows where you look, which you turn, raise and then
    /// place with a click.
    /// </summary>
    public partial class Plugin
    {
        internal const string BlueprintPrefix = "Blueprint: ";

        internal class PlanInfo
        {
            public string Key;     // the orders' "By": "Blueprint: <name>" or a player's name
            public string Title;
            public bool IsBlueprint;
            public List<Order> Orders = new List<Order>();
            public Vector3 Centre;
            public float Distance;
        }

        /// <summary>Every plan, nearest first.</summary>
        internal List<PlanInfo> Plans(Vector3 me)
        {
            var list = new List<PlanInfo>();
            foreach (var group in _orders.Values.GroupBy(o => o.By ?? ""))
            {
                var plan = new PlanInfo { Key = group.Key, Orders = group.ToList() };
                plan.IsBlueprint = plan.Key.StartsWith(BlueprintPrefix);
                plan.Title = plan.IsBlueprint ? plan.Key.Substring(BlueprintPrefix.Length) : "Planned by " + (plan.Key.Length > 0 ? plan.Key : "someone");
                Vector3 sum = Vector3.zero;
                foreach (Order o in plan.Orders) sum += o.Pos;
                plan.Centre = sum / Mathf.Max(1, plan.Orders.Count);
                plan.Distance = plan.Orders.Min(o => Vector3.Distance(o.Pos, me));
                list.Add(plan);
            }
            return list.OrderBy(p => p.Distance).ToList();
        }

        internal int RemovePlan(string key)
        {
            StopLevel(key);
            _levelJobs.Remove(key);
            string ground = RestoreTerrain(key);
            if (ground != null) Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, ground);
            var ids = _orders.Values.Where(o => (o.By ?? "") == key).Select(o => o.Id).ToList();
            foreach (string id in ids) RemoveOrder(id, broadcast: true, save: false);
            SaveOrders();
            return ids.Count;
        }

        internal int RemoveWithin(Vector3 centre, float radius)
        {
            var ids = _orders.Values.Where(o => (o.Pos - centre).sqrMagnitude <= radius * radius).Select(o => o.Id).ToList();
            foreach (string id in ids) RemoveOrder(id, broadcast: true, save: false);
            SaveOrders();
            return ids.Count;
        }

        /// <summary>A plan name that is not in use yet ("Wooden fort", then "Wooden fort 2"...).</summary>
        private string FreeTitle(string title)
        {
            var used = new HashSet<string>(_orders.Values.Select(o => o.By ?? ""));
            if (!used.Contains(BlueprintPrefix + title)) return title;
            for (int n = 2; ; n++) if (!used.Contains(BlueprintPrefix + title + " " + n)) return title + " " + n;
        }

        // ---- the pieces of a blueprint (or of a plan being moved), relative to its anchor ----

        internal class Entry
        {
            public string Prefab;
            public Vector3 Local;      // metres from the anchor; y above the anchor's ground (or above the piece's own ground when Ground)
            public Quaternion Rot;     // turn relative to the anchor's turn
            public bool Ground;
        }

        internal static List<Entry> EntriesFrom(JArray pieces)
        {
            var list = new List<Entry>();
            foreach (JToken p in pieces.Take(1500))
            {
                string prefab = (string)p["p"];
                if (string.IsNullOrEmpty(prefab)) continue;
                list.Add(new Entry
                {
                    Prefab = prefab,
                    Local = new Vector3((float)(p["x"] ?? 0f), (float)(p["y"] ?? 0f), (float)(p["z"] ?? 0f)),
                    Rot = Quaternion.Euler((float)(p["rx"] ?? 0f), (float)(p["ry"] ?? 0f), (float)(p["rz"] ?? 0f)),
                    Ground = (bool?)p["g"] == true,
                });
            }
            return list;
        }

        /// <summary>Where an entry goes in the world for an anchor point, turn and extra height.</summary>
        private static void WorldPose(Entry e, Vector3 anchor, float yaw, float baseY, out Vector3 pos, out Quaternion rot)
        {
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            pos = anchor + turn * new Vector3(e.Local.x, 0f, e.Local.z);
            float ground = e.Ground ? ZoneSystem.instance.GetGroundHeight(pos) : baseY;
            pos.y = ground + e.Local.y;
            rot = turn * e.Rot;
        }

        private HashSet<string> _buildable;

        /// <summary>The pieces a hammer, hoe or cultivator can build (a blueprint naming anything else is skipped: old or hidden pieces).</summary>
        private HashSet<string> Buildable()
        {
            if (_buildable != null && _buildable.Count > 0) return _buildable;
            _buildable = new HashSet<string>();
            if (ObjectDB.instance == null) return _buildable;
            foreach (string tool in new[] { "Hammer", "Hoe", "Cultivator" })
            {
                GameObject item = ObjectDB.instance.GetItemPrefab(tool);
                PieceTable table = item != null ? item.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
                if (table != null) foreach (GameObject p in table.m_pieces) if (p != null) _buildable.Add(p.name);
            }
            return _buildable;
        }

        /// <summary>Turn entries into build orders at an anchor. Returns how many were placed.</summary>
        internal int PlaceEntries(Player player, string title, string file, List<Entry> entries, Vector3 anchor, float yaw, float offsetY, bool auto, bool level = false, float fixedBaseY = float.NaN)
        {
            if (ZNetScene.instance == null || ZoneSystem.instance == null) return 0;
            title = FreeTitle(title);
            float baseY = float.IsNaN(fixedBaseY) ? ZoneSystem.instance.GetGroundHeight(anchor) + offsetY : fixedBaseY;
            HashSet<string> buildable = Buildable();
            int added = 0, skipped = 0;
            var unknown = new HashSet<string>();
            string by = BlueprintPrefix + title;
            foreach (Entry e in entries)
            {
                if (ZNetScene.instance.GetPrefab(e.Prefab) == null || (buildable.Count > 0 && !buildable.Contains(e.Prefab))) { skipped++; unknown.Add(e.Prefab); continue; }
                WorldPose(e, anchor, yaw, baseY, out Vector3 pos, out Quaternion rot);
                if (_orders.Values.Any(o => o.Prefab == e.Prefab && (o.Pos - pos).sqrMagnitude < 0.01f && Quaternion.Angle(o.Rot, rot) < 5f)) { skipped++; continue; }
                var order = new Order { Id = Guid_(), Prefab = e.Prefab, Pos = pos, Rot = rot, By = by };
                _orders[order.Id] = order;
                Send("A|" + Encode(order));
                added++;
            }
            int posts = 0;
            List<Foot> feet = GroundFeet(entries);
            foreach (Post post in Supports(feet, anchor, yaw, baseY, out int _, level))
            {
                if (_orders.Values.Any(o => o.Prefab == post.Prefab && (o.Pos - post.Pos).sqrMagnitude < 0.01f)) continue;
                var order = new Order { Id = SupportIdPrefix + Guid_(), Prefab = post.Prefab, Pos = post.Pos, Rot = post.Rot, By = by };
                _orders[order.Id] = order;
                Send("A|" + Encode(order));
                added++; posts++;
            }
            _stabilityDirty = true;
            Save();
            RecordSnapshotOrders(by);
            if (posts > 0) Logger.LogInfo($"Blueprint '{title}': {posts} support posts added where the ground falls away");
            if (file != null) try { File.WriteAllText(file + ".imported", DateTime.Now.ToString("s") + "  " + added + " placed, " + skipped + " skipped  at " + anchor); } catch (Exception) { }
            if (unknown.Count > 0) Logger.LogWarning($"Blueprint '{title}': skipped pieces that cannot be built: {string.Join(", ", unknown.ToArray())}");
            Logger.LogInfo($"Blueprint '{title}': {added} build orders placed at {anchor} turned {yaw:0}° ({skipped} skipped)");
            player.Message(MessageHud.MessageType.Center, $"\"{title}\": {added} pieces planned" + (skipped > 0 ? $" ({skipped} skipped)" : "") + (auto ? " near your bed" : ""));
            player.Message(MessageHud.MessageType.TopLeft, $"Wrong spot? {_blueprintKey.Value} > Placed plans: Move or Remove it");
            RememberImport(title, file ?? "", new Vector3(anchor.x, baseY - offsetY, anchor.z), yaw, added, offsetY);
            if (level) StartLevel(by, title, LevelPoints(entries, feet, anchor, yaw, baseY), anchor, yaw);
            return added;
        }

        // ---- placement mode ----

        private class Placement
        {
            public string Title, File, ReplaceKey;
            public List<Entry> Entries;
            public float Yaw, OffsetY;
            public readonly List<KeyValuePair<Entry, GameObject>> Ghosts = new List<KeyValuePair<Entry, GameObject>>();
            public readonly List<Material> Materials = new List<Material>();
            public int Spawned;
            public Vector3 LastAnchor = new Vector3(float.NaN, 0, 0);
            public float LastYaw = float.NaN, LastOffset = float.NaN;
            public Vector3 Anchor;
            public bool HaveAnchor;
            public List<Foot> Feet;
            public readonly List<KeyValuePair<string, GameObject>> PostGhosts = new List<KeyValuePair<string, GameObject>>();
            public List<Post> Posts = new List<Post>();
            public int TooHigh;
            public bool Level;
            public List<Vector3> LevelSpots = new List<Vector3>();
            public int Strokes;
            public float Cut, Fill;
            public GameObject Pad;
        }
        private static readonly Color SupportColor = new Color(1f, 0.85f, 0.35f);

        private Placement _placing;
        internal static bool Placing => Instance != null && Instance._placing != null;
        private static readonly Color PreviewColor = new Color(0.45f, 1f, 0.55f);

        internal void StartPlacement(string title, string file, List<Entry> entries, float yaw, float offsetY, string replaceKey)
        {
            CancelPlacement();
            _placing = new Placement { Title = title, File = file, Entries = entries, Yaw = yaw, OffsetY = offsetY, ReplaceKey = replaceKey, Feet = GroundFeet(entries), Level = _levelByDefault.Value };
            PlansWindowOpen = false;
        }

        internal void CancelPlacement()
        {
            if (_placing == null) return;
            foreach (var kv in _placing.Ghosts) if (kv.Value != null) Destroy(kv.Value);
            foreach (var kv in _placing.PostGhosts) if (kv.Value != null) Destroy(kv.Value);
            if (_placing.Pad != null) Destroy(_placing.Pad);
            foreach (Material m in _placing.Materials) if (m != null) Destroy(m);
            _placing = null;
        }

        /// <summary>Called every frame while placing: grow the preview, follow the look point, read the controls.</summary>
        private void UpdatePlacement(Player player)
        {
            Placement pl = _placing;
            if (pl == null) return;
            if (player == null || player.IsDead()) { CancelPlacement(); return; }

            // controls (the mouse wheel turns it; the game's zoom is held off while placing)
            if (!TypingOrMenuOpen())
            {
                float wheel = Input.mouseScrollDelta.y;
                bool fine = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (Mathf.Abs(wheel) > 0.01f) pl.Yaw = Mathf.Repeat(pl.Yaw + Mathf.Sign(wheel) * (fine ? 5f : 15f), 360f);
                if (Input.GetKeyDown(KeyCode.R)) pl.Yaw = Mathf.Repeat(Mathf.Round((pl.Yaw + 90f) / 90f) * 90f, 360f);
                if (Input.GetKeyDown(KeyCode.PageUp)) pl.OffsetY += fine ? 0.1f : 0.5f;
                if (Input.GetKeyDown(KeyCode.PageDown)) pl.OffsetY -= fine ? 0.1f : 0.5f;
                if (Input.GetKeyDown(KeyCode.Home)) { pl.OffsetY = 0f; }
                if (Input.GetKeyDown(KeyCode.L)) { pl.Level = !pl.Level; pl.LastAnchor = new Vector3(float.NaN, 0, 0); }
                if (Input.GetMouseButtonDown(1)) { CancelPlacement(); player.Message(MessageHud.MessageType.TopLeft, "Placement cancelled"); return; }
                if ((Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && pl.HaveAnchor)
                {
                    ConfirmPlacement(player);
                    return;
                }
            }

            // a few preview pieces per frame, so a big plan does not stall the game
            for (int k = 0; k < 30 && pl.Spawned < pl.Entries.Count; k++, pl.Spawned++)
            {
                Entry e = pl.Entries[pl.Spawned];
                GameObject go = MakeGhostObject(e.Prefab, player.transform.position, Quaternion.identity, pl.Materials);
                if (go == null) continue;
                go.name = e.Prefab + "_preview";
                TintWith(go, new Color(PreviewColor.r, PreviewColor.g, PreviewColor.b, Mathf.Clamp(_ghostOpacity.Value * 1.4f, 0.12f, 0.6f)));
                pl.Ghosts.Add(new KeyValuePair<Entry, GameObject>(e, go));
                pl.LastAnchor = new Vector3(float.NaN, 0, 0); // place the new ones too
            }

            pl.HaveAnchor = LookPoint(player, out Vector3 anchor);
            if (!pl.HaveAnchor) anchor = player.transform.position + player.transform.forward * 10f;
            pl.Anchor = anchor;
            bool moved = float.IsNaN(pl.LastAnchor.x) || (anchor - pl.LastAnchor).sqrMagnitude > 0.0025f || pl.Yaw != pl.LastYaw || pl.OffsetY != pl.LastOffset;
            if (!moved) return;
            pl.LastAnchor = anchor; pl.LastYaw = pl.Yaw; pl.LastOffset = pl.OffsetY;
            float baseY = ZoneSystem.instance.GetGroundHeight(anchor) + pl.OffsetY;
            foreach (var kv in pl.Ghosts)
            {
                if (kv.Value == null) continue;
                WorldPose(kv.Key, anchor, pl.Yaw, baseY, out Vector3 pos, out Quaternion rot);
                kv.Value.transform.SetPositionAndRotation(pos, rot);
            }
            UpdatePreviewLevel(pl, anchor, baseY);
            UpdatePreviewPosts(pl, player, anchor, baseY);
        }

        /// <summary>Show the auto-support posts the plan would get here, reusing the post ghosts already made.</summary>
        private void UpdatePreviewPosts(Placement pl, Player player, Vector3 anchor, float baseY)
        {
            pl.Posts = Supports(pl.Feet, anchor, pl.Yaw, baseY, out pl.TooHigh, pl.Level);
            int i = 0;
            for (; i < pl.Posts.Count && i < 400; i++)
            {
                Post post = pl.Posts[i];
                if (i < pl.PostGhosts.Count && pl.PostGhosts[i].Key != post.Prefab)
                {
                    if (pl.PostGhosts[i].Value != null) Destroy(pl.PostGhosts[i].Value);
                    pl.PostGhosts[i] = new KeyValuePair<string, GameObject>(post.Prefab, null);
                }
                if (i >= pl.PostGhosts.Count) pl.PostGhosts.Add(new KeyValuePair<string, GameObject>(post.Prefab, null));
                GameObject go = pl.PostGhosts[i].Value;
                if (go == null)
                {
                    go = MakeGhostObject(post.Prefab, post.Pos, post.Rot, pl.Materials);
                    if (go == null) continue;
                    go.name = post.Prefab + "_support_preview";
                    TintWith(go, new Color(SupportColor.r, SupportColor.g, SupportColor.b, Mathf.Clamp(_ghostOpacity.Value * 1.6f, 0.15f, 0.65f)));
                    pl.PostGhosts[i] = new KeyValuePair<string, GameObject>(post.Prefab, go);
                }
                if (!go.activeSelf) go.SetActive(true);
                go.transform.SetPositionAndRotation(post.Pos, post.Rot);
            }
            for (; i < pl.PostGhosts.Count; i++) if (pl.PostGhosts[i].Value != null && pl.PostGhosts[i].Value.activeSelf) pl.PostGhosts[i].Value.SetActive(false);
        }

        private void ConfirmPlacement(Player player)
        {
            Placement pl = _placing;
            if (pl.ReplaceKey != null) RemovePlan(pl.ReplaceKey);
            string title = pl.Title;
            Vector3 anchor = pl.Anchor;
            float yaw = pl.Yaw, offset = pl.OffsetY;
            bool level = pl.Level;
            List<Entry> entries = pl.Entries;
            string file = pl.File;
            CancelPlacement();
            if (!level) { PlaceEntries(player, title, file, entries, anchor, yaw, offset, auto: false); return; }

            // level first, then place the ghosts on the finished ground (posts only where it still falls short)
            title = FreeTitle(title);
            float baseY = ZoneSystem.instance.GetGroundHeight(anchor) + offset;
            List<Vector3> points = LevelPoints(entries, GroundFeet(entries), anchor, yaw, baseY);
            string key = BlueprintPrefix + title;
            StartLevel(key, title, points, anchor, yaw, () =>
            {
                Player me = Player.m_localPlayer;
                if (me != null) PlaceEntries(me, title, file, entries, anchor, yaw, offset, auto: false, fixedBaseY: baseY);
            });
            player.Message(MessageHud.MessageType.TopLeft, $"Levelling the ground for \"{title}\": its ghosts appear when it is done");
        }

        /// <summary>Pick a placed plan up again: its pieces relative to where it was placed, ready to be put down somewhere else.</summary>
        internal void StartMove(Player player, PlanInfo plan)
        {
            List<Entry> entries = PlanEntries(plan, out Vector3 _, out float yaw, out float offset);
            StartPlacement(plan.Title, null, entries, yaw, offset, plan.Key);
            player.Message(MessageHud.MessageType.TopLeft, $"Moving \"{plan.Title}\": look where it should go and click");
        }

        /// <summary>Level the ground under a plan that is already placed (to its floor level).</summary>
        internal int LevelPlacedPlan(PlanInfo plan)
        {
            List<Entry> entries = PlanEntries(plan, out Vector3 origin, out float yaw, out float offset);
            List<Vector3> points = LevelPoints(entries, GroundFeet(entries), origin, yaw, origin.y + offset);
            StartLevel(plan.Key, plan.Title, points, origin, yaw);
            return points.Count;
        }

        /// <summary>A placed plan's pieces (not its auto-supports) relative to where it was placed, and that place.</summary>
        private List<Entry> PlanEntries(PlanInfo plan, out Vector3 origin, out float yaw, out float offset)
        {
            yaw = 0f; offset = 0f;
            if (plan.IsBlueprint && Frame(plan.Title, out Vector3 o, out float y, out string _)) { origin = o; yaw = y; offset = ImportOffset(plan.Title); }
            else
            {
                origin = plan.Centre;
                origin.y = ZoneSystem.instance.GetGroundHeight(origin);
            }
            Quaternion back = Quaternion.Inverse(Quaternion.Euler(0f, yaw, 0f));
            float baseY = origin.y + offset;
            Vector3 o0 = origin;
            return plan.Orders.Where(ord => !ord.Id.StartsWith(SupportIdPrefix)).Select(ord => // its posts are worked out again where it lands
            {
                Vector3 local = back * new Vector3(ord.Pos.x - o0.x, 0f, ord.Pos.z - o0.z);
                local.y = ord.Pos.y - baseY;
                return new Entry { Prefab = ord.Prefab, Local = local, Rot = back * ord.Rot, Ground = false };
            }).ToList();
        }

        /// <summary>The help bar shown while placing.</summary>
        private void DrawPlacementBanner(float sw)
        {
            Placement pl = _placing;
            if (pl == null) return;
            bool supports = _autoSupports.Value && (pl.Posts.Count > 0 || pl.TooHigh > 0);
            var r = new Rect(sw / 2f - 450f, 70f, 900f, supports ? 100f : 78f);
            Round(r, new Color(0.04f, 0.1f, 0.06f, 0.88f), 8f);
            Outline(r, new Color(PreviewColor.r, PreviewColor.g, PreviewColor.b, 0.9f), 8f);
            string loading = pl.Spawned < pl.Entries.Count ? $"   (showing {pl.Spawned}/{pl.Entries.Count})" : "";
            Label(new Rect(r.x, r.y + 4f, r.width, 22f), $"PLACING \"{pl.Title}\"   turned {pl.Yaw:0}°   height {pl.OffsetY:+0.0;-0.0;0} m{loading}", _bold, PreviewColor, TextAnchor.MiddleCenter);
            Label(new Rect(r.x, r.y + 28f, r.width, 20f), "Look where it goes   ·   Wheel: turn (Shift: fine)   ·   R: 90°   ·   PgUp/PgDn: height   ·   Click: place   ·   Right-click / Esc: cancel",
                _text, Color.white, TextAnchor.MiddleCenter);
            if (supports)
            {
                string text = pl.Posts.Count > 0 ? $"+ {pl.Posts.Count} support posts down to the ground ({PostCost(pl.Posts)})" : "";
                if (pl.TooHigh > 0) text += (text.Length > 0 ? "   ·   " : "") + $"{pl.TooHigh} spot(s) more than {_supportMaxHeight.Value:0} m up: lower it";
                Label(new Rect(r.x, r.y + 50f, r.width, 20f), text, _bold, pl.TooHigh > 0 ? new Color(1f, 0.6f, 0.4f) : SupportColor, TextAnchor.MiddleCenter);
            }
            Label(new Rect(r.x, r.yMax - 24f, r.width, 20f), LevelBannerText(pl), pl.Level ? _bold : _text, pl.Level ? PadColor : new Color(0.8f, 0.8f, 0.8f), TextAnchor.MiddleCenter);
        }
    }
}
