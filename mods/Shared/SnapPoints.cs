using UnityEngine;

namespace DHack.Shared
{
    /// <summary>
    /// A build piece's snap points, set to fit its own shape. A piece copied from a game prefab keeps that prefab's points (a wooden pole's
    /// top and bottom, 1 m above and below its middle), which mean nothing once the copy has a model of its own: the ghost then jumps to
    /// spots that float it or bury it. Some prefabs have none at all (chests, tables, every station), so a copy of a chest never lines up.
    ///
    /// How the game uses them (assembly_valheim): `Piece.GetSnapPoints` takes every DIRECT child of the piece's root tagged "snappoint",
    /// switched on or off (the game's own are switched-off empty objects on the "piece" layer, named "$hud_snappoint_corner 1" and the
    /// like). While placing, `Player.UpdatePlacementGhost` moves the ghost so that one of its points meets the nearest point of a piece
    /// within 10 m (a piece found by a collider on the "piece"/"piece_nonsolid" layers) when they are under 0.5 m apart; the build
    /// key that cycles snap points (Tab) picks one by hand and shows its name, translated ("Snapping: Corner 1"); holding the alt-place
    /// key (Shift) places freely. Nothing else reads them (wear, support and saving don't).
    ///
    ///   SnapPoints.Clear(go);                                                    // the copied prefab's points go
    ///   SnapPoints.Box(go, new Vector3(-0.76f, 0f, -0.42f), new Vector3(0.76f, 0f, 0.42f));   // one on each corner of its base
    ///   SnapPoints.Add(go, "bottom", Vector3.zero);                              // or any points, named with one of the game's words
    ///
    /// Call it on your own copy (under your inactive holder), never on the game's prefab from ZNetScene: that would change the piece for
    /// everyone. Pieces already standing keep the points they were made with until their area reloads. It never throws (a piece must
    /// still be registered, or the host deletes the placed ones).
    /// Shared source (mods/Shared/SnapPoints.cs), linked into the mods that use it: Arena, BountyBoard, LedgerChest, Recycler, SlotMachine,
    /// Ziplines. Claude Tools ships it as a template.
    /// </summary>
    internal static class SnapPoints
    {
        public const string Tag = "snappoint";
        /// <summary>The start of the game's snap point names: "$hud_snappoint_" + a word it translates (corner, top, bottom, edge, mid,
        /// center, end, inner, attach) shows as "Corner 1" when the player picks it by hand.</summary>
        public const string Prefix = "$hud_snappoint_";

        /// <summary>Removes the piece's snap points (direct children tagged "snappoint"); nothing else. Returns how many went.</summary>
        public static int Clear(GameObject piece)
        {
            if (piece == null) return 0;
            int removed = 0;
            for (int i = piece.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = piece.transform.GetChild(i).gameObject;
                if (!child.CompareTag(Tag)) continue;
                Object.DestroyImmediate(child);
                removed++;
            }
            return removed;
        }

        /// <summary>Snap points at these positions (metres from the piece's origin, in its own frame), named "corner".</summary>
        public static int Add(GameObject piece, params Vector3[] localPositions) => Add(piece, "corner", localPositions);

        /// <summary>
        /// Snap points at these positions, made as the game makes its own: switched-off empty children on the "piece" layer, named
        /// "$hud_snappoint_" + kind + " " + a number (no number for a lone one), numbered on from the piece's points of that kind.
        /// Returns how many were made.
        /// </summary>
        public static int Add(GameObject piece, string kind, params Vector3[] localPositions)
        {
            if (piece == null || localPositions == null || localPositions.Length == 0) return 0;
            int layer = LayerMask.NameToLayer("piece");
            if (layer < 0) layer = piece.layer;
            string name = Prefix + (string.IsNullOrEmpty(kind) ? "corner" : kind);
            int before = 0;
            foreach (Transform child in piece.transform)
                if (child.CompareTag(Tag) && (child.name == name || child.name.StartsWith(name + " "))) before++;
            bool numbered = before + localPositions.Length > 1;

            int made = 0;
            foreach (Vector3 at in localPositions)
            {
                var go = new GameObject(numbered ? name + " " + (before + made + 1) : name);
                go.SetActive(false);
                try
                {
                    go.tag = Tag;   // (a tag the game defines; throws only outside the game)
                    go.layer = layer;
                    go.transform.SetParent(piece.transform, false);   // at once: no stray object left in the scene
                    go.transform.localPosition = at;
                    go.transform.localRotation = Quaternion.identity;
                    made++;
                }
                catch (System.Exception e)
                {
                    Object.DestroyImmediate(go);
                    Debug.LogWarning($"SnapPoints: no snap point on {piece.name}: {e.Message}");
                    break;
                }
            }
            return made;
        }

        /// <summary>
        /// A snap point on each corner of a box-shaped piece's base ("bottom 1-4"), and of its top when `top` ("top 1-4"); with
        /// `edgeMiddles`, the middle of each edge of the base (and top) too ("edge"). `localBox` is in the piece's own frame: give its
        /// footprint as far as the model reaches, so two side by side just touch and one on a floor corner stays on the floor.
        /// </summary>
        public static int Box(GameObject piece, Bounds localBox, bool top = false, bool edgeMiddles = false)
        {
            Vector3 lo = localBox.min, hi = localBox.max;
            int made = Add(piece, "bottom", Corners(lo, hi, lo.y));
            if (top) made += Add(piece, "top", Corners(lo, hi, hi.y));
            if (edgeMiddles)
            {
                made += Add(piece, "edge", Middles(lo, hi, lo.y));
                if (top) made += Add(piece, "edge", Middles(lo, hi, hi.y));
            }
            return made;
        }

        /// <summary>As Box(piece, bounds): the box from its lowest corner to its highest (metres from the piece's origin).</summary>
        public static int Box(GameObject piece, Vector3 min, Vector3 max, bool top = false, bool edgeMiddles = false)
        {
            var b = new Bounds();
            b.SetMinMax(Vector3.Min(min, max), Vector3.Max(min, max));
            return Box(piece, b, top, edgeMiddles);
        }

        // (in the order of the game's floors: right front, left front, right back, left back)
        private static Vector3[] Corners(Vector3 lo, Vector3 hi, float y) => new[]
        {
            new Vector3(hi.x, y, lo.z), new Vector3(lo.x, y, lo.z), new Vector3(hi.x, y, hi.z), new Vector3(lo.x, y, hi.z),
        };

        private static Vector3[] Middles(Vector3 lo, Vector3 hi, float y)
        {
            float mx = (lo.x + hi.x) * 0.5f, mz = (lo.z + hi.z) * 0.5f;
            return new[] { new Vector3(mx, y, lo.z), new Vector3(mx, y, hi.z), new Vector3(lo.x, y, mz), new Vector3(hi.x, y, mz) };
        }
    }
}
