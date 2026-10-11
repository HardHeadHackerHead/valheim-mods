using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// Making sure E reaches a dungeon's levers and ways out. The game uses what its hover ray hits first, from the camera (Player
    /// FindHoverObject: the interact layers, piece_nonsolid among them, within m_maxInteractDistance of the eyes); from below a ledge, the
    /// ledge's lip or the lever's own post can take the ray before the lever's aim box. So when E is pressed with nothing usable under the
    /// crosshair, the nearest lever or way out in front of the player, within reach (3 m from the body, 70 degrees from where they look),
    /// is used as if hovered. And `dungeon aim` says what the hover ray hits, to see why in the game.
    /// </summary>
    internal static class UseReach
    {
        private const float Reach = 3f, Cone = 70f;
        private static readonly AccessTools.FieldRef<Player, GameObject> Hovering = AccessTools.FieldRefAccess<Player, GameObject>("m_hovering");
        private static readonly AccessTools.FieldRef<Player, int> InteractMask = AccessTools.FieldRefAccess<Player, int>("m_interactMask");
        private static readonly Func<Player, bool> TakeInput = AccessTools.MethodDelegate<Func<Player, bool>>(AccessTools.Method(typeof(Player), "TakeInput"));

        /// <summary>Every frame: E with nothing usable hovered, and one of ours near and in front: used.</summary>
        public static void Update()
        {
            Player p = Player.m_localPlayer;
            if (p == null || p.IsDead() || !(ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse")) || Hud.InRadial()) return;
            if (p.transform.position.y < 3000f || Dungeons.Containing(p.transform.position) == null) return;
            try { if (TakeInput != null && !TakeInput(p)) return; } catch (Exception) { return; }
            GameObject hover = Hovering(p);
            if (hover != null && hover.GetComponentInParent<Interactable>() != null) return;   // (the game uses it)
            Interactable near = Nearest(p, out _, out _);
            if (near == null) return;
            near.Interact(p, false, false);
        }

        /// <summary>Our nearest lever or way out within reach and in front of the player (by the closest point of its colliders).</summary>
        private static Interactable Nearest(Player p, out float distance, out Component found)
        {
            distance = float.MaxValue;
            found = null;
            Interactable best = null;
            Vector3 body = p.transform.position + Vector3.up * 1f;
            Vector3 look = GameCamera.instance != null ? GameCamera.instance.transform.forward : p.transform.forward;
            look.y = 0f;
            foreach (Component c in Ours())
            {
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                Vector3 closest = c.transform.position;
                float d = float.MaxValue;
                foreach (Collider col in c.GetComponentsInChildren<Collider>())
                {
                    if (col == null || !col.enabled) continue;
                    Vector3 q = col.ClosestPoint(body);
                    float dq = Vector3.Distance(q, body);
                    if (dq < d) { d = dq; closest = q; }
                }
                if (d == float.MaxValue) d = Vector3.Distance(c.transform.position, body);
                Vector3 to = closest - body;
                to.y = 0f;
                if (d > Reach || (to.sqrMagnitude > 0.01f && look.sqrMagnitude > 0.01f && Vector3.Angle(look, to) > Cone)) continue;
                if (d < distance) { distance = d; best = (Interactable)c; found = c; }
            }
            return best;
        }

        private static IEnumerable<Component> Ours()
        {
            foreach (Site site in Dungeons.Sites)
            {
                if (site.Exit != null) foreach (DungeonExit e in site.Exit.GetComponentsInChildren<DungeonExit>()) yield return e;
                if (site.Mechs == null) continue;
                foreach (Mech m in site.Mechs.All)
                {
                    if (m.Pivot == null) continue;
                    foreach (DungeonLever l in m.Pivot.GetComponents<DungeonLever>()) yield return l;
                    foreach (DungeonExit e in m.Pivot.GetComponents<DungeonExit>()) yield return e;
                    foreach (DungeonShortcut s in m.Pivot.GetComponents<DungeonShortcut>()) yield return s;
                }
            }
        }

        /// <summary>For `dungeon aim`: what the hover ray hits now (as the game casts it), the game's hover, and our nearest in reach.</summary>
        public static JObject Report()
        {
            Player p = Player.m_localPlayer;
            if (p == null || GameCamera.instance == null) return new JObject { ["error"] = "no player" };
            Transform cam = GameCamera.instance.transform;
            var hits = Physics.RaycastAll(cam.position, cam.forward, 50f, InteractMask(p)).OrderBy(h => h.distance).Take(6);
            var list = new JArray();
            foreach (RaycastHit h in hits)
            {
                bool own = h.collider.attachedRigidbody != null && h.collider.attachedRigidbody.gameObject == p.gameObject;
                Component ours = (Component)h.collider.GetComponentInParent<DungeonLever>() ?? h.collider.GetComponentInParent<DungeonExit>()
                                 ?? (Component)h.collider.GetComponentInParent<DungeonEntrance>();
                list.Add(new JObject
                {
                    ["collider"] = h.collider.name,
                    ["path"] = Path(h.collider.transform),
                    ["layer"] = LayerMask.LayerToName(h.collider.gameObject.layer),
                    ["trigger"] = h.collider.isTrigger,
                    ["fromEyes"] = Math.Round(Vector3.Distance(p.m_eye.position, h.point), 2),
                    ["ours"] = ours != null ? ours.GetType().Name : null,
                    ["usable"] = h.collider.GetComponentInParent<Interactable>() != null,
                    ["skipped"] = own ? "the player's own body" : null,
                });
            }
            GameObject hover = Hovering(p);
            Interactable near = Nearest(p, out float d, out Component nc);
            return new JObject
            {
                ["hits"] = list,
                ["hover"] = hover != null ? Path(hover.transform) : null,
                ["maxInteractDistance"] = p.m_maxInteractDistance,
                ["nearestOurs"] = nc != null ? new JObject { ["what"] = nc.GetType().Name, ["path"] = Path(nc.transform), ["distance"] = Math.Round(d, 2) } : null,
                ["note"] = "E uses the game's hover when it's usable; else our nearest lever or way out within 3 m in front (nearestOurs)",
            };
        }

        private static string Path(Transform t)
        {
            var names = new List<string>();
            for (int i = 0; t != null && i < 6; t = t.parent, i++) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
