using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SoftReferenceableAssets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    /// <summary>A dungeon built in this player's game: the rooms' shells under one root object, never saved.</summary>
    internal class Built
    {
        public Layout Layout;
        public Vector3 Origin;          // where the layout's origin (its entrance room) stands in the world
        public GameObject Root;
        public bool Done;               // every room is built

        public GameObject[] Rooms;      // each placed room's object, by PlacedRoom.Index (null until built, or when it couldn't be)
        public EnvZone Environment;     // the pack's environment over the whole dungeon (Builder.MakeEnvironment)
        public Room.Theme Theme;        // what the game's RandomSpawn/RandomObject see as the dungeon's theme (Builder.ThemeOf)
        public bool Removed;            // Remove was called (a build still running stops)
        public string Stats = "";       // how the build went, for the log and commands: rooms, time, frames, worst frame, meshes batched

        internal DungeonGenerator Themes;   // a never-woken DungeonGenerator, only for its m_themes (RandomSpawn.Randomize reads it)
        internal readonly List<SoftReference<GameObject>> Held = new List<SoftReference<GameObject>>();   // rooms loaded for the build
        internal readonly List<Mesh> Owned = new List<Mesh>();   // meshes static batching made: ours to destroy
    }

    /// <summary>
    /// Builds a laid-out dungeon in the world (docs/dungeons-design.md, "Building rooms from parts"): every room's shell locally on each
    /// player's game, never saved (the game's rooms the way DungeonGenerator.PlaceRoom does it, our own rooms from their parts); the networked
    /// parts once, by the owner, saved.
    ///
    /// Split across files: Builder.cs (building over frames, removing, the networked parts), BuildGameRoom.cs (one of the game's rooms, as
    /// PlaceRoom does it), BuildParts.cs (one of our rooms, from its parts), BuildLook.cs (the environment, and static batching), BuildSaved.cs
    /// (the doors between rooms and our rooms' saved parts), BuildCreatures.cs (every saved object's creatures fitted to the entrance's land).
    /// </summary>
    internal static partial class Builder
    {
        /// <summary>How long building may take in one frame (ms) before the rest waits for the next. One room is never split, so a big game
        /// room can go over: Built.Stats has the worst frame.</summary>
        internal static float FrameBudgetMs = 3f;

        /// <summary>Static meshes put together into one per material (fewer draw calls), where nothing moves them (BuildLook.cs).</summary>
        internal static bool BatchMeshes = true;

        /// <summary>Every dungeon built (or being built) in this game, so the plugin can take them all down when it unloads.</summary>
        internal static readonly List<Built> All = new List<Built>();

        /// <summary>Builds the shells over several frames (no stutter), then calls done. The caller keeps the Built to remove it later.
        /// started (optional) gets the Built at once, before any room is built, so a build can be removed while it's still going.</summary>
        public static IEnumerator Build(Layout layout, Vector3 origin, Action<Built> done, Action<Built> started = null)
        {
            var built = new Built { Layout = layout, Origin = origin, Rooms = new GameObject[layout.Rooms.Count] };
            built.Root = new GameObject($"quaddungeon_{layout.Pack?.Name}_{layout.Seed}");
            built.Root.transform.position = origin;
            All.Add(built);
            started?.Invoke(built);
            if (!layout.Ok) Plugin.Log.LogWarning($"Building a dungeon whose layout has problems ({layout.Pack?.Name} {layout.Seed}): {string.Join("; ", layout.Problems)}");

            float began = Time.realtimeSinceStartup;
            var frame = new FrameClock { GameMsBefore = Time.smoothDeltaTime * 1000f };
            var missing = new HashSet<string>();

            // The game's rooms it uses, loaded in the background first (as the game's own dungeons do), so building never waits on the disk.
            var datas = new Dictionary<string, DungeonDB.RoomData>();
            foreach (PlacedRoom room in layout.Rooms)
            {
                string name = room.Def?.GamePrefab;
                if (string.IsNullOrEmpty(name) || datas.ContainsKey(name)) continue;
                DungeonDB.RoomData data = RoomData(name);
                datas[name] = data;
                if (data == null) { missing.Add(name); continue; }
                data.m_prefab.LoadAsync();
                built.Held.Add(data.m_prefab);
            }
            built.Theme = PackTheme(layout.Pack, out Room.Theme theme) ? theme : ThemeOf(datas.Values.Where(d => d != null));
            built.Themes = MakeThemes(built);

            float waitUntil = Time.realtimeSinceStartup + 30f;
            while (built.Held.Any(h => h.IsLoading) && Time.realtimeSinceStartup < waitUntil)
            {
                yield return null;
                if (Gone(built)) { Remove(built); yield break; }
            }

            // The pack's environment over the whole dungeon, first, so it's dark as soon as anyone arrives.
            IEnumerator interior = FindInterior();
            while (interior.MoveNext())
            {
                yield return interior.Current;
                if (Gone(built)) { Remove(built); yield break; }
            }
            try { built.Environment = MakeEnvironment(built); }
            catch (Exception e) { Plugin.Log.LogError("The dungeon's environment: " + e); }

            frame.Restart();
            int gameRooms = 0, ownRooms = 0, failed = 0, batched = 0;
            foreach (PlacedRoom room in layout.Rooms)
            {
                if (Gone(built)) { Remove(built); yield break; }
                double before = frame.Ms;
                GameObject made = null;
                if (!string.IsNullOrEmpty(room.Def?.GamePrefab))
                {
                    datas.TryGetValue(room.Def.GamePrefab, out DungeonDB.RoomData data);
                    if (data != null)
                    {
                        try { made = BuildGameRoom(built, room, data); }
                        catch (Exception e) { Plugin.Log.LogError($"Building room {room.Index} ({room.Def.Name}, {room.Def.GamePrefab}): {e}"); }
                    }
                    if (made != null) gameRooms++; else failed++;
                }
                else if (room.Def != null)
                {
                    // our own rooms can be big: their parts are made over frames too
                    IEnumerator parts = BuildOwnRoom(built, room, frame, missing, go => made = go);
                    while (true)
                    {
                        bool more;
                        try { more = parts.MoveNext(); }
                        catch (Exception e) { Plugin.Log.LogError($"Building room {room.Index} ({room.Def.Name}): {e}"); break; }
                        if (!more) break;
                        yield return parts.Current;
                        if (Gone(built)) { Remove(built); yield break; }
                    }
                    if (made != null) ownRooms++; else failed++;
                }
                if (room.Index >= 0 && room.Index < built.Rooms.Length) built.Rooms[room.Index] = made;
                if (made != null && BatchMeshes)
                {
                    // (in pieces, over frames: a hall of a few hundred parts is too much for one)
                    IEnumerator batch = Batch(built, made, frame, n => batched += n);
                    while (true)
                    {
                        bool more;
                        try { more = batch.MoveNext(); }
                        catch (Exception e) { Plugin.Log.LogWarning($"Batching room {room.Index} ({room.Def?.Name}): {e.Message}"); break; }
                        if (!more) break;
                        yield return batch.Current;
                        if (Gone(built)) { Remove(built); yield break; }
                    }
                }
                frame.Note(room.Def?.Name, frame.Ms - before);
                if (frame.Over)
                {
                    frame.Pause();
                    yield return null;
                    if (Gone(built)) { Remove(built); yield break; }
                    frame.Resume();
                }
            }

            frame.Finish();
            ReleaseHeld(built);
            built.Done = true;
            built.Stats = $"{gameRooms + ownRooms} rooms ({gameRooms} the game's, {ownRooms} ours{(failed > 0 ? $", {failed} failed" : "")}) in "
                        + $"{Time.realtimeSinceStartup - began:0.00} s over {frame.Frames} frames, building {frame.TotalMs:0} ms, at most {frame.WorstMs:0.0} ms in one frame"
                        + $"{(frame.SlowestRoom != null ? $" (slowest room {frame.SlowestRoom}, {frame.SlowestMs:0.0} ms)" : "")}, {batched} meshes batched; "
                        + $"the game's frames meanwhile: worst {frame.WorstGameMs:0.0} ms (about {frame.GameMsBefore:0.0} ms before)";
            Plugin.Log.LogInfo($"Built dungeon {layout.Pack?.Name} {layout.Seed} at {origin}: {built.Stats}");
            if (missing.Count > 0) Plugin.Log.LogWarning("The dungeon names rooms or parts this game does not have: " + string.Join(", ", missing));
            done?.Invoke(built);
        }

        /// <summary>Removes a built dungeon's shells (not its networked parts: those are saved objects). Safe to call twice, and while it's
        /// still being built (the build stops).</summary>
        public static void Remove(Built built)
        {
            if (built == null) return;
            built.Removed = true;
            All.Remove(built);
            try { LeaveEnvironment(built); }
            catch (Exception e) { Plugin.Log.LogWarning("Leaving the dungeon's environment: " + e.Message); }
            // the shells hold their own reference to their room's prefab (SoftReferenceableAssets.Utils.Instantiate): destroying them lets it go
            if (built.Root != null) Object.Destroy(built.Root);
            built.Root = null;
            ReleaseHeld(built);
            foreach (Mesh m in built.Owned) if (m != null) Object.Destroy(m);
            built.Owned.Clear();
            built.Done = false;
        }

        /// <summary>Removes every dungeon built in this game (the plugin unloading).</summary>
        public static void RemoveAll()
        {
            foreach (Built built in All.ToList()) Remove(built);
            All.Clear();
        }

        /// <summary>The owner only, once per room: creates the room's saved objects: a game room's networked parts (spawners, chests, pots),
        /// one of our rooms' Saved parts, and the doors at Layout.Doors whose link starts in this room (BuildSaved.cs). roomDone says whether a
        /// room's were already made (saved in the dungeon's state); markDone records each room as soon as it's done.
        /// Spread over calls: one call makes rooms until it has used a frame's budget (always at least one room), and returns true while
        /// rooms are left, so the caller calls again on a later frame (every object a call makes is made inside that call).</summary>
        public static bool SpawnNetworked(Built built, Func<int, bool> roomDone, Action<int> markDone)
        {
            if (built == null || Gone(built)) return false;
            // things with a body would fall 5000 m through rooms that aren't there yet
            if (!built.Done) { Plugin.Log.LogWarning("Not making a dungeon's saved parts before its rooms are built"); return false; }
            if (ZNetScene.instance == null || ZDOMan.instance == null) return false;
            Stopwatch clock = Stopwatch.StartNew();
            Dictionary<int, List<Link>> doors = DoorsByRoom(built.Layout);
            CreatureFit fit = FitFor(built);
            int made = 0, rooms = 0;
            bool more = false;
            foreach (PlacedRoom room in built.Layout.Rooms)
            {
                if (room.Def == null || roomDone(room.Index)) continue;
                // (only once this call has marked a room: a call that stopped before marking any would never be called again)
                if (rooms > 0 && clock.Elapsed.TotalMilliseconds >= FrameBudgetMs) { more = true; break; }
                int n = 0;
                bool started = false;
                if (!string.IsNullOrEmpty(room.Def.GamePrefab))
                {
                    DungeonDB.RoomData data = RoomData(room.Def.GamePrefab);
                    if (data != null) n += SpawnRoomNetworked(built, room, data, fit, out started);
                }
                else
                {
                    n += SpawnSavedParts(built, room, fit);
                    started = true;
                }
                if (doors.TryGetValue(room.Index, out List<Link> mine))
                {
                    n += SpawnDoors(built, mine, fit);
                    started = true;
                }
                // once anything may have been made, the room counts as done even if it failed half way: twice would be worse than missing
                if (started) { markDone(room.Index); rooms++; made += n; }
            }
            if (rooms > 0) Plugin.Log.LogInfo($"Made the saved parts of {rooms} rooms of dungeon {built.Layout.Pack?.Name} {built.Layout.Seed}: {made} objects "
                                              + $"in {clock.Elapsed.TotalMilliseconds:0.0} ms{(more ? " (more next frame)" : "")}");
            if (fit.Notes.Count > 0)
                Plugin.Log.LogInfo($"Dungeon {built.Layout.Pack?.Name} {built.Layout.Seed}: enemies fitted to {fit.Biome} (tier {fit.Tier}, levels up to "
                                   + $"{LevelCap(fit.Tier)}), by room: {string.Join("; ", fit.Notes)}");
            if (fit.LootNotes.Count > 0 || fit.Filtered.Count > 0)
                Plugin.Log.LogInfo($"Dungeon {built.Layout.Pack?.Name} {built.Layout.Seed}: loot fitted to {fit.Biome} (tier {fit.Tier}), by room: "
                                   + string.Join("; ", fit.LootNotes)
                                   + (fit.Filtered.Count > 0 ? $"; drops kept to the tier on {fit.Filtered.Values.Sum()} objects: "
                                      + string.Join(", ", fit.Filtered.Select(kv => kv.Value > 1 ? $"{kv.Key} x{kv.Value}" : kv.Key)) : ""));
            return more;
        }

        // ---- shared helpers

        private static bool Gone(Built built) => built.Removed || built.Root == null;

        /// <summary>One of the game's rooms by its prefab name, or null.</summary>
        internal static DungeonDB.RoomData RoomData(string prefab)
        {
            if (DungeonDB.instance == null || string.IsNullOrEmpty(prefab)) return null;
            return DungeonDB.instance.GetRoom(prefab.GetStableHashCode());
        }

        /// <summary>A room's world position and rotation: the layout's frame has no turn of its own, only the origin.</summary>
        internal static Vector3 WorldPos(Built built, PlacedRoom room) => built.Origin + room.Pos;
        internal static Quaternion WorldRot(PlacedRoom room) => Quaternion.Euler(0f, room.Yaw, 0f);

        /// <summary>The seed for a room's RandomSpawn/RandomObject, as PlaceRoom makes it (from the room's position), but from its position in
        /// the layout plus the layout's seed (the game's m_useCustomInteriorTransform + m_addBaseSeedToRandomSpawn way): every player gets the
        /// same props, wherever the entrance stands, and two dungeons of the same pack differ.</summary>
        internal static int RoomSeed(Layout layout, PlacedRoom room)
        {
            Vector3 p = room.Pos;
            return unchecked((int)p.x * 4271 + (int)p.y * 9187 + (int)p.z * 2134 + layout.Seed);
        }

        /// <summary>The theme the game's own dungeon would have: the one all the layout's game rooms share (520 = ForestCrypt + ForestCryptHildir
        /// rooms in a ForestCrypt pack give ForestCrypt), its lowest bit, so RandomSpawns that need another theme (Hildir's) stay off.
        /// None when they share nothing (each room then uses its own lowest bit).</summary>
        internal static Room.Theme ThemeOf(IEnumerable<DungeonDB.RoomData> rooms)
        {
            int common = ~0;
            bool any = false;
            foreach (DungeonDB.RoomData d in rooms) { common &= (int)d.m_theme; any = true; }
            return any ? Lowest((Room.Theme)common) : Room.Theme.None;
        }

        internal static Room.Theme Lowest(Room.Theme t) => (Room.Theme)((int)t & -(int)t);

        /// <summary>The pack's own theme (Pack.Theme: one of the game's Room.Theme names, several with commas, or its number), when it sets
        /// one the game knows.</summary>
        internal static bool PackTheme(Pack pack, out Room.Theme theme)
        {
            theme = Room.Theme.None;
            if (string.IsNullOrEmpty(pack?.Theme)) return false;
            try
            {
                theme = (Room.Theme)Enum.Parse(typeof(Room.Theme), pack.Theme.Trim(), true);
                if (theme != Room.Theme.None) return true;
            }
            catch (Exception) { }
            Plugin.Log.LogWarning($"Pack {pack.Name}: its theme \"{pack.Theme}\" isn't one of the game's (Room.Theme): using its rooms' own");
            return false;
        }

        private static DungeonGenerator MakeThemes(Built built)
        {
            // added to an inactive object, so its Awake (which loads saved rooms from a ZNetView) never runs: only m_themes is read
            var go = new GameObject("quaddungeon_themes");
            go.SetActive(false);
            go.transform.SetParent(built.Root.transform, false);
            return go.AddComponent<DungeonGenerator>();
        }

        private static void ReleaseHeld(Built built)
        {
            foreach (SoftReference<GameObject> h in built.Held)
            {
                try { h.Release(); }
                catch (Exception e) { Plugin.Log.LogWarning("Releasing a room: " + e.Message); }
            }
            built.Held.Clear();
        }

        /// <summary>Time spent building in the current frame, and the worst so far; and how long the game's own frames took meanwhile.
        /// Pause before each yield and Resume after it: only the building's own time counts as ours (step 1's clock ran on through the yield,
        /// so its "worst frame" was the game's whole frame: 47.8 ms, while no room took more than 4.7).</summary>
        internal class FrameClock
        {
            private readonly Stopwatch _frame = Stopwatch.StartNew();
            public int Frames = 1;
            public double WorstMs, TotalMs, SlowestMs;
            public double WorstGameMs, GameMsBefore;   // the game's frames (Time.unscaledDeltaTime) while building, and its usual one before
            public string SlowestRoom;
            public double Ms => _frame.Elapsed.TotalMilliseconds;
            public bool Over => Ms >= FrameBudgetMs;
            public void Restart() => _frame.Restart();
            public void Pause() { double ms = Ms; TotalMs += ms; if (ms > WorstMs) WorstMs = ms; _frame.Reset(); }
            public void Resume()
            {
                Frames++;
                double game = Time.unscaledDeltaTime * 1000.0;   // the frame we just built in, whole
                if (game > WorstGameMs) WorstGameMs = game;
                _frame.Restart();
            }
            public void Note(string room, double ms) { if (ms > SlowestMs) { SlowestMs = ms; SlowestRoom = room; } }
            public void Finish() => Pause();
        }
    }
}
