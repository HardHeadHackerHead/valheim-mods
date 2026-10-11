using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// Draws the dungeon map (the one M opens inside a dungeon: mods/Dungeons/MapLayout.cs and MapDraw.cs) outside the game, every floor of
    /// one layout, to check how it looks without playing:
    ///
    ///   dotnet build tools/dungeonmap -c Release
    ///   tools/dungeonmap/bin/Release/net48/MapRender.exe [pack.json] [--seed 1] [--visit 0.6] [--states 7] [--out folder] [--size 1024]
    ///       [--zoom 3 [--centre room]] [--mini [--centre room]] [--fake standing|broken,open,won]
    ///
    /// --fake: made-up breakable walls (before every room with one doorway), a shortcut pair and a boss, for packs that have none yet; "broken", "open" and
    /// "won" in its word break the walls, open the shortcut, defeat the boss. --mini: the corner minimap instead (round the player, or a room's middle): dungeon-map-mini-&lt;floor&gt;.png and a copy shrunk to
    /// 180 px, about its size on screen. --zoom: that much closer than the whole map, centred on a room (its index) or on the player. --visit: the share of rooms visited, nearest the entrance first (1: all of them, as `dungeon mapshot all` draws). --states: a seed
    /// for random mechanism states (0: every mechanism at its start). The game's own rooms come from the catalogue `dungeon rooms` writes.
    /// Writes dungeon-map-&lt;floor&gt;.png per floor (1 is the lowest) and legend.png, into tools/dungeonmap/out unless --out says.
    /// Doors and chests are guessed from the layout (the game finds the real ones in the world), and the player stands in the entrance room.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string here = AppDomain.CurrentDomain.BaseDirectory;
            string repo = Path.GetFullPath(Path.Combine(here, "..", "..", "..", "..", ".."));
            string packFile = Path.Combine(repo, "mods", "Dungeons", "packs", "shiftinghalls.json");
            string catalogue = @"D:\SteamLibrary\steamapps\common\Valheim\BepInEx\claude\dungeon-rooms.json";
            string outDir = Path.Combine(repo, "tools", "dungeonmap", "out");
            int seed = 1, states = 7, size = 1024;
            float visit = 1f, zoom = 1f;
            bool mini = false;
            string fake = null;
            int centre = -1;
            for (int i = 0; i < args.Length; i++)
                switch (args[i])
                {
                    case "--seed": seed = int.Parse(args[++i]); break;
                    case "--visit": visit = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--states": states = int.Parse(args[++i]); break;
                    case "--out": outDir = args[++i]; break;
                    case "--size": size = int.Parse(args[++i]); break;
                    case "--catalogue": catalogue = args[++i]; break;
                    case "--zoom": zoom = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--centre": centre = int.Parse(args[++i]); break;
                    case "--mini": mini = true; break;
                    case "--fake": fake = args[++i]; break;
                    default: packFile = args[i]; break;
                }
            var rooms = new Dictionary<string, JObject>();
            if (File.Exists(catalogue))
                foreach (JObject r in JArray.Parse(File.ReadAllText(catalogue)).OfType<JObject>()) rooms[(string)r["name"]] = r;
            var problems = new List<string>();
            Pack pack = PackReader.Read(JObject.Parse(File.ReadAllText(packFile)), name => rooms.TryGetValue(name, out JObject r) ? PackReader.FromCatalogue(r) : null, problems);
            foreach (string p in problems) Console.WriteLine("pack problem: " + p);
            Layout layout = Layouts.Make(pack, seed);
            if (!layout.Ok) { Console.WriteLine("layout failed: " + string.Join("; ", layout.Problems)); return 1; }
            if (fake != null && !fake.Contains("real")) Fake(layout, fake);   // ("real": the pack's own, only their states set)
            var map = new MapLayout(layout);
            Directory.CreateDirectory(outDir);

            // visited: the rooms nearest the entrance first
            var scene = new MapScene { Map = map, Shown = new bool[layout.Rooms.Count] };
            int count = (int)Math.Ceiling(layout.Rooms.Count * Math.Max(0f, Math.Min(1f, visit)));
            foreach (PlacedRoom r in layout.Rooms.OrderBy(r => r.Depth).ThenBy(r => r.Index).Take(count)) scene.Shown[r.Index] = true;
            var rng = new System.Random(states);
            foreach (PlacedRoom r in layout.Rooms)
                foreach (MechDef m in r.Def.Mechs)
                    scene.States[r.Index + "." + m.Id] = states == 0 ? m.Start : rng.Next(Math.Max(1, Layouts.States(m)));
            if (fake != null)
            {
                // the made-up secrets: "broken" breaks the walls, "open" opens the shortcuts, "won" clears the boss
                foreach (PlacedRoom r in layout.Rooms)
                    foreach (MechDef m in r.Def.Mechs)
                    {
                        string key = r.Index + "." + m.Id;
                        if (m.Kind == "breakable" && fake.Contains("broken")) scene.Recorded.Add(key);
                        if (m.Kind == "shortcut" && fake.Contains("open")) scene.Recorded.Add(key);
                        if (m.Kind == "encounter" && fake.Contains("won")) scene.Cleared.Add(key);
                    }
                scene.HideSecrets();
            }
            // the guesses: a door at each layout door (every other one open), a chest at each saved part named like one
            int k = 0;
            foreach (Link l in layout.Doors)
            {
                PlacedRoom r = layout.Rooms[l.Room];
                scene.Things.Add(new MapThing { Mark = k++ % 2 == 0 ? MapMark.GateShut : MapMark.GateOpen, At = r.DoorwayPos(l.Doorway), Forward = r.DoorwayFacing(l.Doorway), Width = 3f });
            }
            foreach (PlacedRoom r in layout.Rooms)
                foreach (PartDef p in r.Def.Parts)
                    if (p.Saved && (p.Prefab ?? "").IndexOf("chest", StringComparison.OrdinalIgnoreCase) >= 0)
                        scene.Things.Add(new MapThing { Mark = MapMark.Chest, At = r.Pos + Layouts.Turn(p.At, MapLayout.Quarter(r.Yaw)) });
            int door = map.EntranceDoor;
            Vector3 you = door >= 0 ? layout.Rooms[0].DoorwayPos(door) + layout.Rooms[0].DoorwayFacing(door) * 4f : layout.Rooms[0].Pos;
            scene.Things.Add(new MapThing { Mark = MapMark.You, At = you, Forward = door >= 0 ? layout.Rooms[0].DoorwayFacing(door) : Vector3.forward });

            if (Environment.GetEnvironmentVariable("MAPDEBUG") != null)
                foreach (PlacedRoom r in layout.Rooms)
                    Console.WriteLine($"room {r.Index} {r.Def.Name} floors {map.Base[r.Index] + 1}-{map.Top[r.Index] + 1}{(map.IsWall(r.Index) ? " (wall)" : "")} at {r.Pos} yaw {r.Yaw}");
            if (Environment.GetEnvironmentVariable("MAPDEBUG") != null)
                foreach (MapLayout.Breakable b in map.Breakables)
                    Console.WriteLine($"breakable {b.Room}.{b.Mech.Id} on doorway {b.Door} (floor {map.DoorFloor[b.Room][b.Door] + 1}): secret room {b.Secret} {layout.Rooms[b.Secret].Def.Name}");
            if (Environment.GetEnvironmentVariable("MAPDEBUG") != null)
                foreach (MapThing t in scene.Things)
                {
                    int room = map.RoomAt(t.At);
                    foreach (PlacedRoom pr in layout.Rooms) { Bounds bb = map.Boxes[pr.Index]; if (t.At.x >= bb.min.x && t.At.x <= bb.max.x && t.At.z >= bb.min.z && t.At.z <= bb.max.z) Console.WriteLine($"   xz in {pr.Index} {pr.Def.Name} y {bb.min.y}..{bb.max.y} pos {pr.Pos} yaw {pr.Yaw} size {pr.Def.Size}"); }
                    Console.WriteLine($"{t.Mark} at {t.At} floor {map.FloorOf(t.At.y + 0.5f) + 1} room {room} {(room >= 0 ? layout.Rooms[room].Def.Name + " box " + map.Boxes[room].min + "-" + map.Boxes[room].max + " floors " + (map.Base[room] + 1) + "-" + (map.Top[room] + 1) : "")}");
                }
            if (mini)
            {
                // the minimap: round the player (or a room's middle), its window cut out, at its size and shrunk to about what shows
                Vector3 at = centre >= 0 ? map.Boxes[centre].center : you;
                if (centre >= 0) { at.y = map.Boxes[centre].min.y + 1f; scene.Things.RemoveAll(t => t.Mark == MapMark.You); scene.Things.Add(new MapThing { Mark = MapMark.You, At = at, Forward = Vector3.forward }); }
                scene.Mini = true;
                scene.Look(4f, new Vector2(at.x, at.z), 1024, 1024);
                var whole = new MapCanvas(1024, 1024);
                int f = map.FloorOf(at.y);
                MapPainter.Draw(scene, f, whole);
                int n = 256, x0 = 512 - n / 2;
                var cut = new MapCanvas(n, n);
                for (int y = 0; y < n; y++) Array.Copy(whole.Px, (x0 + y) * 1024 + x0, cut.Px, y * n, n);
                string file = Path.Combine(outDir, $"dungeon-map-mini-{f + 1}.png");
                Save(cut, file);
                using (var big = new Bitmap(file))
                using (var small = new Bitmap(big, new Size(180, 180))) small.Save(Path.Combine(outDir, $"dungeon-map-mini-{f + 1}-180.png"), ImageFormat.Png);
                Console.WriteLine($"minimap, floor {f + 1} -> {file}");
                return 0;
            }
            var canvas = new MapCanvas(size, size);
            scene.Fit(size, size, 40f);
            if (zoom != 1f || centre >= 0)
            {
                Vector3 c = centre >= 0 ? map.Boxes[centre].center : you;
                scene.Zoom(zoom, new Vector2(c.x, c.z), size, size);
            }
            Console.WriteLine($"{pack.Name} seed {seed}: {layout.Rooms.Count} rooms, {map.Floors} floors at {string.Join(", ", map.Levels.Select(l => l.ToString("0.#")))}; " +
                              $"{scene.Shown.Count(v => v)} shown; {scene.Ppm:0.00} px a metre");
            for (int f = 0; f < map.Floors; f++)
            {
                if (!Enumerable.Range(0, layout.Rooms.Count).Any(i => scene.Shown[i] && map.On(i, f))) continue;
                if (zoom == 1f && centre < 0) scene.Fit(size, size, 40f, f);   // (as the map opens: fitted to what this floor shows)
                var clock = System.Diagnostics.Stopwatch.StartNew();
                MapPainter.Draw(scene, f, canvas);
                long drawn = clock.ElapsedMilliseconds;
                string file = Path.Combine(outDir, $"dungeon-map-{f + 1}.png");
                Save(canvas, file);
                Console.WriteLine($"floor {f + 1}: {scene.Ppm:0.0} px a metre, {Enumerable.Range(0, layout.Rooms.Count).Count(i => scene.Shown[i] && map.On(i, f))} rooms, " +
                                  $"drawn in {drawn} ms -> {file}");
            }
            // the legend's pictures, side by side
            var marks = (MapMark[])Enum.GetValues(typeof(MapMark));
            var legend = new MapCanvas(48 * marks.Length, 48);
            legend.Clear(new Color32(222, 203, 160, 255));
            for (int i = 0; i < marks.Length; i++) MapPainter.Icon(legend, marks[i], new Vector2(24 + 48 * i, 24), 30f, Vector2.up);
            Save(legend, Path.Combine(outDir, "legend.png"));
            return 0;
        }

        /// <summary>Made-up secrets for a pack that has none yet (--fake): a breakable wall in front of every room with one doorway (it is the secret
        /// room), a shortcut pair between the entrance room and the deepest room, and a boss fight in the deepest room.</summary>
        private static void Fake(Layout layout, string what)
        {
            var clone = typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            RoomDef Own(PlacedRoom r)
            {
                var def = (RoomDef)clone.Invoke(r.Def, null);
                def.Mechs = new List<MechDef>(r.Def.Mechs);
                def.Tags = new List<string>(r.Def.Tags);
                r.Def = def;
                return def;
            }
            int walls = 0;
            foreach (PlacedRoom nook in layout.Rooms.Where(r => r.Index > 0 && r.Def.Doorways.Count == 1 && r.Def.Size.x > 1f && r.Def.Size.z > 1f).ToList())
                foreach (Link l in layout.Links)
                {
                    int parent = l.Room == nook.Index ? l.OtherRoom : l.OtherRoom == nook.Index ? l.Room : -1;
                    int door = l.Room == nook.Index ? l.OtherDoorway : l.Doorway;
                    if (parent < 0) continue;
                    PlacedRoom p = layout.Rooms[parent];
                    Own(p).Mechs.Add(new MechDef { Id = "wall" + walls++, Kind = "breakable", Doorway = p.Def.Doorways[door].Id, At = p.Def.Doorways[door].At });
                    break;
                }
            PlacedRoom deep = layout.Rooms.Where(r => !r.Def.EndCap).OrderByDescending(r => r.Depth).First();
            Own(layout.Rooms[0]).Mechs.Add(new MechDef { Id = "short", Kind = "shortcut", Pair = "short", At = new Vector3(3f, layout.Rooms[0].Def.Doorways[0].At.y, 0f) });
            Own(deep).Mechs.Add(new MechDef { Id = "short", Kind = "shortcut", Pair = "short", Deep = true, At = new Vector3(-3f, deep.Def.Doorways[0].At.y, 0f) });
            Own(deep).Mechs.Add(new MechDef { Id = "bossfight", Kind = "encounter", At = new Vector3(0f, deep.Def.Doorways[0].At.y - 1f, 2f) });
            Console.WriteLine($"made up: {walls} breakable walls (before rooms with one doorway), a shortcut between rooms 0 and {deep.Index}, a boss in room {deep.Index} ({deep.Def.Name})");
        }

        private static void Save(MapCanvas c, string file)
        {
            using (var bmp = new Bitmap(c.W, c.H, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < c.H; y++)
                    for (int x = 0; x < c.W; x++)
                    {
                        Color32 p = c.Px[y * c.W + x];
                        bmp.SetPixel(x, c.H - 1 - y, System.Drawing.Color.FromArgb(p.a, p.r, p.g, p.b));
                    }
                bmp.Save(file, ImageFormat.Png);
            }
        }
    }
}
