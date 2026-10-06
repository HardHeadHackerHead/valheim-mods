using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// What a bridge looks like: the settings (width, material, sides, roof, supports, shape, ends, torches; remembered between bridges) and the
    /// design made from them, in the start's frame (z along the bridge, x across, y above the start's ground). New styles and materials go here.
    /// </summary>
    public partial class Plugin
    {
        internal enum BridgeMaterial { Wood, CoreWood, Darkwood, Stone }
        internal enum BridgeSides { Rails, HalfWalls, None }
        internal enum BridgeSupports { Auto, Every2m, Every4m }
        internal enum BridgeShape { Straight, Arched }
        internal enum BridgeEnds { Sloped, Steps }

        private ConfigEntry<int> _bridgeWidth;
        private ConfigEntry<BridgeMaterial> _bridgeMaterial;
        private ConfigEntry<BridgeSides> _bridgeSides;
        private ConfigEntry<bool> _bridgeRoof, _bridgeTorches;
        private ConfigEntry<BridgeSupports> _bridgeSupports;
        private ConfigEntry<BridgeShape> _bridgeShape;
        private ConfigEntry<BridgeEnds> _bridgeEnds;

        private void BindBridgeConfig()
        {
            _bridgeWidth = Config.Bind("Bridge", "Width", 2, new ConfigDescription("Deck width in metres (2, 4 or 6). The last bridge's choices are kept.", new AcceptableValueList<int>(2, 4, 6)));
            _bridgeMaterial = Config.Bind("Bridge", "Material", BridgeMaterial.Wood, "Wood; CoreWood (round logs from pines: stronger, reaches deeper water); Darkwood (needs tar); Stone (stone floors on stone pillars, needs a stonecutter).");
            _bridgeSides = Config.Bind("Bridge", "Sides", BridgeSides.Rails, "Rails (posts and a handrail), HalfWalls (a low wall along each side) or None (cheapest).");
            _bridgeRoof = Config.Bind("Bridge", "Roof", false, "A roof over the deck on tall posts (a covered bridge).");
            _bridgeSupports = Config.Bind("Bridge", "Supports", BridgeSupports.Auto, "Posts down to the riverbed: Auto (every 2 m in deep water, else 4 m), Every2m (strongest) or Every4m (cheapest). Stone always has a pillar under every slab.");
            _bridgeShape = Config.Bind("Bridge", "Shape", BridgeShape.Straight, "Straight from end to end, or Arched (the middle raised in a gentle curve).");
            _bridgeEnds = Config.Bind("Bridge", "Ends", BridgeEnds.Sloped, "Sloped (the deck slopes from one bank's height to the other's) or Steps (a level deck, with steps down at the lower end).");
            _bridgeTorches = Config.Bind("Bridge", "Torches", false, "Standing torches along the deck every 8 m (wood and resin).");
        }

        internal class BridgeOptions
        {
            public int Width = 2;
            public BridgeMaterial Material;
            public BridgeSides Sides;
            public bool Roof, Torches;
            public BridgeSupports Supports;
            public BridgeShape Shape;
            public BridgeEnds Ends;
        }

        private BridgeOptions CurrentBridgeOptions() => new BridgeOptions
        {
            Width = _bridgeWidth.Value, Material = _bridgeMaterial.Value, Sides = _bridgeSides.Value, Roof = _bridgeRoof.Value,
            Supports = _bridgeSupports.Value, Shape = _bridgeShape.Value, Ends = _bridgeEnds.Value, Torches = _bridgeTorches.Value,
        };

        private void SaveBridgeOptions(BridgeOptions o)
        {
            _bridgeWidth.Value = o.Width; _bridgeMaterial.Value = o.Material; _bridgeSides.Value = o.Sides; _bridgeRoof.Value = o.Roof;
            _bridgeSupports.Value = o.Supports; _bridgeShape.Value = o.Shape; _bridgeEnds.Value = o.Ends; _bridgeTorches.Value = o.Torches;
        }

        /// <summary>The pieces of one material.</summary>
        private class BridgeKit
        {
            public string Name, Station;
            public string Deck; public float DeckThickness;          // the deck's slab and how far its middle sits below the walking surface
            public bool PillarUnderEverySlab;                         // stone cannot hang off a pillar at its corner: one goes under the middle of each slab
            public string[] Posts; public float[] PostLengths;        // longest first
            public string TallPost;                                   // a 2 m post: the covered bridge's walls
            public string Beam, Roof, RoofTop, Stair;
            public string[] HalfWall; public float[] HalfWallY;       // pieces of a low wall along a side, and their heights above the deck
            public float HalfWallInset;                               // how far in from the edge the wall stands
            public float MaxDepth;
        }

        private static BridgeKit KitFor(BridgeMaterial m)
        {
            switch (m)
            {
                case BridgeMaterial.CoreWood:
                    return new BridgeKit { Name = "core wood", Station = "piece_workbench", Deck = "wood_floor", Posts = new[] { "wood_pole_log_4", "wood_pole_log", "wood_pole" }, PostLengths = new[] { 4f, 2f, 1f },
                        TallPost = "wood_pole_log", Beam = "wood_wall_log", Roof = "wood_roof", RoofTop = "wood_roof_top", Stair = "wood_stair",
                        HalfWall = new[] { "wood_wall_log", "wood_wall_log" }, HalfWallY = new[] { 0.25f, 0.75f }, HalfWallInset = 0.3f, MaxDepth = 16f };
                case BridgeMaterial.Darkwood:
                    return new BridgeKit { Name = "darkwood", Station = "piece_workbench", Deck = "wood_floor", Posts = new[] { "darkwood_pole4", "darkwood_pole", "wood_pole" }, PostLengths = new[] { 4f, 2f, 1f },
                        TallPost = "darkwood_pole", Beam = "darkwood_beam", Roof = "darkwood_roof", RoofTop = "darkwood_roof_top", Stair = "wood_stair",
                        HalfWall = new[] { "wood_wall_half" }, HalfWallY = new[] { 0.5f }, HalfWallInset = 0.2f, MaxDepth = 12f };
                case BridgeMaterial.Stone:
                    return new BridgeKit { Name = "stone", Station = "piece_stonecutter", Deck = "stone_floor_2x2", DeckThickness = 0.5f, PillarUnderEverySlab = true,
                        Posts = new[] { "stone_pillar" }, PostLengths = new[] { 2f }, TallPost = "stone_pillar", Beam = "wood_beam", Roof = "wood_roof", RoofTop = "wood_roof_top", Stair = "stone_stair",
                        HalfWall = new[] { "stone_wall_2x1" }, HalfWallY = new[] { 0.5f }, HalfWallInset = 0.5f, MaxDepth = 15f };
                default:
                    return new BridgeKit { Name = "wood", Station = "piece_workbench", Deck = "wood_floor", Posts = new[] { "wood_pole2", "wood_pole" }, PostLengths = new[] { 2f, 1f },
                        TallPost = "wood_pole2", Beam = "wood_beam", Roof = "wood_roof", RoofTop = "wood_roof_top", Stair = "wood_stair",
                        HalfWall = new[] { "wood_wall_half" }, HalfWallY = new[] { 0.5f }, HalfWallInset = 0.2f, MaxDepth = 12f };
            }
        }

        // ---- the design ----

        internal class BridgeDesign
        {
            public List<Entry> Entries = new List<Entry>();
            public float Yaw, Length;
            public List<string> Warnings = new List<string>();
        }

        /// <summary>The bridge from a start to an end point with these settings.</summary>
        private BridgeDesign DesignBridge(Vector3 start, Vector3 end, BridgeOptions o)
        {
            var d = new BridgeDesign();
            Vector3 flat = end - start; flat.y = 0f;
            if (flat.magnitude < 0.5f || ZoneSystem.instance == null) return d;
            BridgeKit kit = KitFor(o.Material);
            d.Yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            int segments = Mathf.Clamp(Mathf.CeilToInt(flat.magnitude / 2f), 1, 60);   // 2 m slabs, up to 120 m
            float length = segments * 2f;
            d.Length = length;
            int width = Mathf.Clamp(o.Width / 2 * 2, 2, 6);
            float half = width / 2f;
            Quaternion turn = Quaternion.Euler(0f, d.Yaw, 0f);
            float water = ZoneSystem.instance.m_waterLevel - start.y;
            float Ground(float x, float z) => ZoneSystem.instance.GetGroundHeight(start + turn * new Vector3(x, 0f, z)) - start.y;
            void Add(string prefab, float x, float y, float z, Quaternion rot) => d.Entries.Add(new Entry { Prefab = prefab, Local = new Vector3(x, y, z), Rot = rot });

            // the walking surface's height along the bridge: from one end's height to the other's (an end over water kept above it), or level with
            // steps at the lower end; raised in the middle if arched
            float startDeck = Mathf.Max(0f, water + 0.6f), endDeck = Mathf.Max(end.y - start.y, water + 0.6f);
            bool steps = o.Ends == BridgeEnds.Steps;
            if (steps) startDeck = endDeck = Mathf.Max(startDeck, endDeck);
            float arch = o.Shape == BridgeShape.Arched ? Mathf.Clamp(length / 12f, 0.8f, 3f) : 0f;   // gentle: about 18 degrees at the ends
            float Deck(float z) { float t = Mathf.Clamp01(z / length); return Mathf.Lerp(startDeck, endDeck, t) + arch * 4f * t * (1f - t) + 0.05f; }
            float Tilt(float z) { float t = Mathf.Clamp01(z / length); float slope = (endDeck - startDeck) / length + arch * 4f * (1f - 2f * t) / length; return Mathf.Atan(slope) * Mathf.Rad2Deg; }
            Quaternion Along(float z) => Quaternion.Euler(0f, -90f, Tilt(z));   // a long piece (along x) turned to run along the bridge, tilted with it

            // the deck: 2 m slabs across the width, tilted with the deck
            var slabs = new List<Vector2>();
            for (int s = 0; s < segments; s++)
            {
                float z = s * 2f + 1f;
                Quaternion slab = Quaternion.Euler(-Tilt(z), 0f, 0f);
                for (float x = -half + 1f; x < half; x += 2f) { Add(kit.Deck, x, Deck(z) - kit.DeckThickness, z, slab); slabs.Add(new Vector2(x, z)); }
            }

            // supports down to the riverbed: wood under the slab edges every 2 or 4 m along; stone under the middle of every slab
            float deepest = 0f;
            for (int k = 0; k <= segments; k++)
                for (float x = -half; x <= half + 0.01f; x += 2f) deepest = Mathf.Max(deepest, Deck(k * 2f) - Ground(x, k * 2f));
            void Column(float x, float z, float top)
            {
                float ground = Ground(x, z);
                if (top - ground < 0.3f) return;                     // already resting on the bank
                for (int n = 0; n < 16 && top - ground > 0.05f; n++)
                {
                    float left = top - ground;
                    int pick = kit.PostLengths.Length - 1;
                    for (int i = 0; i < kit.PostLengths.Length; i++) if (kit.PostLengths[i] <= left + 0.6f) { pick = i; break; }   // the longest that does not sink far in
                    Add(kit.Posts[pick], x, top - kit.PostLengths[pick] / 2f, z, Quaternion.identity);
                    top -= kit.PostLengths[pick];
                }
            }
            if (kit.PillarUnderEverySlab)
                foreach (Vector2 slab in slabs) Column(slab.x, slab.y, Deck(slab.y) - kit.DeckThickness * 2f);
            else
            {
                int every = o.Supports == BridgeSupports.Every2m ? 1 : o.Supports == BridgeSupports.Every4m ? 2 : (deepest > 6f ? 1 : 2);
                for (int k = 0; k <= segments; k++)
                {
                    if (k % every != 0 && k != segments) continue;   // the far end always gets its posts
                    for (float x = -half; x <= half + 0.01f; x += 2f) Column(x, k * 2f, Deck(k * 2f) - 0.1f);
                }
            }

            // sides: rails (a short post every 2 m and a handrail), low walls, or nothing; a covered bridge adds tall posts carrying the roof
            float edge = half - 0.15f;
            for (int k = 0; k <= segments; k++)
                foreach (float x in new[] { -edge, edge })
                {
                    float z = k * 2f;
                    if (o.Roof) Add(kit.TallPost, x, Deck(z) + 1f, z, Quaternion.identity);
                    else if (o.Sides == BridgeSides.Rails) Add("wood_pole", x, Deck(z) + 0.5f, z, Quaternion.identity);
                    if (k == segments) continue;
                    if (o.Sides == BridgeSides.Rails) Add(o.Material == BridgeMaterial.Stone ? "wood_beam" : kit.Beam, x, Deck(z + 1f) + 1.05f, z + 1f, Along(z + 1f));
                    if (o.Sides == BridgeSides.HalfWalls)
                    {
                        float wx = Mathf.Sign(x) * (half - kit.HalfWallInset);
                        for (int i = 0; i < kit.HalfWall.Length; i++) Add(kit.HalfWall[i], wx, Deck(z + 1f) + kit.HalfWallY[i], z + 1f, Along(z + 1f));
                    }
                    if (o.Roof) Add(kit.Beam, x, Deck(z + 1f) + 1.9f, z + 1f, Along(z + 1f));
                }

            // the roof: slopes rising in from each edge (one slope on a 2 m bridge, two meeting on 4 m, two and a ridge cap on 6 m), 2 m up
            if (o.Roof)
                for (int s = 0; s < segments; s++)
                {
                    float z = s * 2f + 1f, eave = Deck(z) + 2.0f;
                    Quaternion tilt = Quaternion.Euler(-Tilt(z), 0f, 0f);
                    Add(kit.Roof, -half + 1f, eave, z, tilt * Quaternion.Euler(0f, -90f, 0f));                 // rises towards +x
                    if (width >= 4) Add(kit.Roof, half - 1f, eave, z, tilt * Quaternion.Euler(0f, 90f, 0f));   // rises towards -x
                    if (width >= 6) Add(kit.RoofTop, 0f, eave + 1f, z, tilt * Quaternion.Euler(0f, 90f, 0f));
                }

            // steps down from a level deck at the lower end, out beyond it, until they reach the ground (not into water)
            int stepCount = 0;
            if (steps)
            {
                float deckTop = Deck(0f) - 0.05f;
                bool atStart = start.y + 0.0f < end.y;                  // the lower bank is where the steps go
                float bankGround = atStart ? 0f : end.y - start.y;
                if (bankGround > water && deckTop - bankGround > 0.6f)
                    for (int k = 0; k < 12; k++)
                    {
                        float top = deckTop - k, bottom = top - 1f;
                        float zc = atStart ? -2f * k - 1f : length + 2f * k + 1f;
                        if (Ground(0f, zc) < water) break;              // the bank ends in water: the steps stop at the waterline
                        Quaternion climb = atStart ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;   // a stair climbs towards its -z
                        for (float x = -half + 1f; x < half; x += 2f) Add(kit.Stair, x, bottom, zc, climb);
                        stepCount++;
                        if (bottom <= Ground(0f, atStart ? zc - 1f : zc + 1f) + 0.05f) break;
                    }
            }

            // torches every 8 m, both sides, standing upright on the deck inside the sides
            if (o.Torches)
                for (float z = 1f; z < length; z += 8f)
                    foreach (float x in new[] { -half + 0.6f, half - 0.6f })
                        Add("piece_groundtorch_wood", x, Deck(z) - 0.05f, z, Quaternion.identity);

            // what to tell the player
            float steepest = 0f;
            for (float z = 0f; z <= length; z += 2f) steepest = Mathf.Max(steepest, Mathf.Abs(Tilt(z)));
            if (steepest > 25f) d.Warnings.Add($"very steep ({steepest:0}°): hard to walk up (try Ends: Steps)");
            if (deepest > kit.MaxDepth) d.Warnings.Add($"supports {deepest:0} m long: {kit.Name} may not hold that far down" + (o.Material == BridgeMaterial.Wood || o.Material == BridgeMaterial.Darkwood ? " (core wood or stone reach further)" : ""));
            if (Deck(length * 0.5f) < water + 0.5f && arch <= 0f) d.Warnings.Add("the middle is close to the water: start or end higher up the bank, or arch it");
            if (deepest < 0.4f && length >= 6f) d.Warnings.Add("there is no water or dip under it: is this where you meant?");
            if (steps && stepCount == 0 && Mathf.Abs(end.y - start.y) > 0.6f) d.Warnings.Add("no steps: the lower end is in the water");
            bool stationStart = CraftingStation.HaveBuildStationInRange(kit.Station, start) != null;
            bool stationEnd = CraftingStation.HaveBuildStationInRange(kit.Station, start + turn * new Vector3(0f, endDeck, length)) != null;
            string station = kit.Station == "piece_stonecutter" ? "stonecutter" : "workbench";
            if (!stationStart || !stationEnd) d.Warnings.Add(stationStart ? $"no {station} near the far end: build one there to finish it" : $"needs a {station} within 20 m to build: put one by the start");
            if (o.Material == BridgeMaterial.Stone && (o.Roof || o.Sides == BridgeSides.Rails)) d.Warnings.Add("the roof and rails of a stone bridge are wood (a workbench is needed too)");
            return d;
        }
    }
}
