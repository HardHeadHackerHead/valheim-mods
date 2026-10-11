using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using static DHack.Shared.Translations;

namespace Dungeons
{
    /// <summary>One dungeon this game knows about: its entrance (loaded, or Detached: its zone unloaded while this game's player is still
    /// inside), its layout, where it stands, and what of it is built here.</summary>
    internal class Site
    {
        public DungeonEntrance Entrance;
        public ZDO SavedZdo;            // the entrance's ZDO, kept for while the entrance object is gone (Detached)
        public Vector3 EntrancePos;     // where the entrance stands (entrances never move)
        public Quaternion EntranceRot;
        public string Look = EntranceLooks.Default;
        public bool Detached;           // its entrance unloaded here while the local player was inside: kept built until they leave
        public Layout Layout;           // null until laid out
        public string Problem;          // why it can't be built (an unknown pack, a layout that failed), or null
        public Built Built;             // the shells (from the moment Builder.Build starts: Ready once it's done)
        public bool Building, EnterWhenBuilt, Gone;
        public string PartsMadeFor;     // the rooms-done state the owner last asked the builder to make parts for
        public GameObject Exit;         // the way out, at the entrance room's doorway (local)
        public bool PartsPending;       // the owner: the builder has more saved parts to make (one frame's worth per call)
        public MechSet Mechs;           // its moving parts, once it's built (null: none, or not built)
        public bool MechsMade;
        public float NextOwnerCheck;

        public ZNetView View => Entrance != null ? Entrance.View : null;
        public ZDO Zdo => View != null && View.IsValid() ? View.GetZDO()
                        : Detached && SavedZdo != null && ZDOMan.instance != null && ZDOMan.instance.GetZDO(SavedZdo.m_uid) == SavedZdo ? SavedZdo : null;
        /// <summary>This game owns the entrance, and it's loaded here (the owner's jobs need its object).</summary>
        public bool IsOwner => View != null && View.IsValid() && View.IsOwner();

        /// <summary>Tells the entrance's owner something (Dungeons_Act): through the entrance object, or straight to its ZDO while it isn't
        /// loaded here.</summary>
        public void Act(string act)
        {
            if (View != null && View.IsValid()) { View.InvokeRPC("Dungeons_Act", act); return; }
            ZDO z = Zdo;
            if (z != null && ZRoutedRpc.instance != null) ZRoutedRpc.instance.InvokeRoutedRPC(z.GetOwner(), z.m_uid, "Dungeons_Act", act);
        }
        public string PackName => Zdo?.GetString(Keys.Pack) ?? "";
        public int Seed => Zdo?.GetInt(Keys.Seed) ?? 0;
        public string Id => Zdo?.GetString(Keys.Id) ?? "";
        public bool Ready => Built != null && Built.Done;
        public bool MechsReady => Ready && MechsMade && (Mechs == null || Mechs.Complete);   // built, its moving parts too

        public bool HasOrigin => Zdo != null && Zdo.GetVec3(Keys.Origin, out _);
        public Vector3 Origin => Zdo != null && Zdo.GetVec3(Keys.Origin, out Vector3 v) ? v : Vector3.zero;

        /// <summary>The whole dungeon in the world (its layout's box moved to its origin), when both are known.</summary>
        public bool Box(out Bounds box)
        {
            box = default;
            if (Layout == null || !HasOrigin) return false;
            box = new Bounds(Origin + Layout.Box.center, Layout.Box.size);
            return true;
        }

        public string Title
        {
            get
            {
                string title = Layout?.Pack?.Title;
                if (string.IsNullOrEmpty(title)) title = string.IsNullOrEmpty(PackName) ? T("dungeons_entrance") : PackName;
                return Localization.instance != null ? Localization.instance.Localize(title) : title;
            }
        }

        /// <summary>Inside the dungeon: within its box (a little more, for someone standing at a wall).</summary>
        public bool Contains(Vector3 p)
        {
            if (!Box(out Bounds box)) return false;
            box.Expand(4f);
            return box.Contains(p);
        }

        /// <summary>In the air space this dungeon will fill, so a player or object there must wait for it: its box and a margin around it,
        /// and everything under it down to 3000 m (what fell out of it: the safety net puts it back once it's built). Before its layout or
        /// origin is known, anything high above the entrance within a zone or two, unless one of the game's own dungeons is in that zone:
        /// that's where its own interior is.</summary>
        public bool Covers(Vector3 p)
        {
            if (p.y < 3000f) return false;
            if (Box(out Bounds box))
                return p.x > box.min.x - 16f && p.x < box.max.x + 16f && p.z > box.min.z - 16f && p.z < box.max.z + 16f && p.y < box.max.y + 100f;
            if (Entrance == null) return false;
            Vector3 e = EntrancePos;
            return p.y > e.y + 3000f && Dungeons.FlatDistance(p, e) < Dungeons.UnknownReach && Location.GetZoneLocation(p) == null;
        }

        /// <summary>The day it comes back new, while it waits to (entered, and resets are on).</summary>
        public bool RenewsOnDay(out int day)
        {
            day = 0;
            int entered = Zdo != null ? Zdo.GetInt(Keys.Entered, -1) : -1, days = Plugin.ResetDays.Value;
            if (entered < 0 || days <= 0) return false;
            day = entered + days;
            return true;
        }

        public bool RoomDone(int i)
        {
            string done = Zdo?.GetString(Keys.Done) ?? "";
            return i >= 0 && i < done.Length && done[i] == '1';
        }

        public int RoomsDone => (Zdo?.GetString(Keys.Done) ?? "").Count(c => c == '1');
    }

    /// <summary>
    /// The dungeons themselves (docs/dungeons-design.md): where each goes, building it in this game while its entrance is loaded and
    /// removing it after, the owner's jobs (the networked parts, the first entry, timed resets), and going in and out.
    ///
    /// When it's built: the brief said "when the player is near, removed when far". But the saved objects inside a dungeon (its doors and
    /// chests, creatures, a gravestone, dropped items) load and unload with the entrance's zone, and would fall 5000 m whenever they exist
    /// without the floor under them. So a dungeon is built as soon as its entrance exists in this game (its zone is loaded, which is "near":
    /// a zone or two), removed when the entrance goes (its zone unloads, and the objects inside with it), and the saved objects inside wait
    /// to be made until it is built (Safety.HoldBack). A dungeon can be wider than the zones loaded around a player, though: one this game's
    /// player is inside when its entrance unloads stays built (Detached) until they leave it.
    /// </summary>
    internal static class Dungeons
    {
        public const float Height = 5000f;       // above the entrance, as the game's own interiors (Location.Awake)
        public const float Step = 50f;           // a dungeon that would overlap another goes up by steps of this
        public const int MaxSlots = 40;
        public const float Margin = 8f;          // kept free between two dungeons' boxes
        public const float UnknownReach = 128f;  // how far around an entrance a dungeon not yet laid out may reach

        public static readonly List<Site> Sites = new List<Site>();
        private static readonly Dictionary<string, Layout> LayoutCache = new Dictionary<string, Layout>();
        private static readonly HashSet<string> Warned = new HashSet<string>();
        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> Instances = AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");
        private static readonly System.Reflection.FieldInfo AllLocations = AccessTools.Field(typeof(Location), "s_allLocations");
        private static readonly System.Reflection.MethodInfo DropAllItems = AccessTools.Method(typeof(Container), "DropAllItems", Type.EmptyTypes);

        /// <summary>Our own id for a dungeon, saved on its entrance (a ZDOID changes every time the world loads).</summary>
        public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 12);

        public static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        // ---- knowing them ----

        public static Site Track(DungeonEntrance entrance)
        {
            ZDO zdo = entrance.View?.GetZDO();
            // its entrance back while this game kept the dungeon (Detached): the same one again
            Site site = Sites.FirstOrDefault(s => s.Detached && zdo != null && s.SavedZdo == zdo);
            if (site != null)
            {
                site.Entrance = entrance;
                site.Detached = false;
                Plugin.Log.LogInfo($"Dungeon {site.Id}: its entrance is loaded here again");
                return site;
            }
            site = new Site { Entrance = entrance, SavedZdo = zdo, EntrancePos = entrance.transform.position, EntranceRot = entrance.transform.rotation, Look = entrance.Look };
            Sites.Add(site);
            return site;
        }

        /// <summary>The entrance object is going (its zone unloads here, or it's removed). A dungeon this game's player is inside stays built
        /// (Detached) until they leave it: a big dungeon reaches farther than the zones loaded around them, and its floor must not go from
        /// under them, their gravestone or anything they dropped. Otherwise it's taken down.</summary>
        public static void Lost(Site site)
        {
            Player p = Player.m_localPlayer;
            if (!site.Gone && site.Ready && site.SavedZdo != null && p != null && site.Contains(p.transform.position)
                && ZDOMan.instance != null && ZDOMan.instance.GetZDO(site.SavedZdo.m_uid) == site.SavedZdo)
            {
                site.Entrance = null;
                site.Detached = true;
                Plugin.Log.LogInfo($"Dungeon {site.Id}: its entrance unloaded here while the player is inside: kept built until they leave");
                return;
            }
            Untrack(site);
        }

        public static void Untrack(Site site)
        {
            site.Gone = true;
            Sites.Remove(site);
            Unbuild(site);
        }

        /// <summary>The dungeon the point is inside, or null.</summary>
        public static Site Containing(Vector3 p) => p.y < 3000f ? null : Sites.FirstOrDefault(s => s.Contains(p));

        /// <summary>The dungeon whose space the point is in (inside, or where it will be), or null.</summary>
        public static Site Covering(Vector3 p) => p.y < 3000f ? null : Sites.FirstOrDefault(s => s.Contains(p)) ?? Sites.FirstOrDefault(s => s.Covers(p));

        /// <summary>A pack's layout for a seed, made once per world (the same pack and seed always give the same one). Null with a problem
        /// when it can't be made.</summary>
        public static Layout LayoutFor(string packName, int seed, out string problem)
        {
            problem = null;
            if (string.IsNullOrEmpty(packName)) { problem = "no pack"; return null; }
            string key = packName.ToLowerInvariant() + "|" + seed;
            if (LayoutCache.TryGetValue(key, out Layout cached)) { problem = cached.Ok ? null : string.Join("; ", cached.Problems); return cached.Ok ? cached : null; }
            Layout layout;
            try
            {
                Pack pack = Packs.Get(packName);
                if (pack == null) { problem = T("dungeons_unknown_kind", packName); return null; }   // (not cached: a pack could come later)
                layout = Layouts.Make(pack, seed);
            }
            catch (Exception e)
            {
                problem = e.GetType().Name + ": " + e.Message;
                if (Warned.Add(key)) Plugin.Log.LogWarning($"Laying out {packName} seed {seed} failed: {e}");
                return null;
            }
            return Keep(key, layout, out problem);
        }

        private static Layout Keep(string key, Layout layout, out string problem)
        {
            problem = null;
            if (layout == null) { problem = "no layout"; return null; }
            LayoutCache[key] = layout;
            if (!layout.Ok) { problem = string.Join("; ", layout.Problems); return null; }
            return layout;
        }

        private static readonly Dictionary<string, Task<Layout>> Laying = new Dictionary<string, Task<Layout>>();

        /// <summary>
        /// LayoutFor without the wait, for the dungeons themselves: a big pack's layout takes up to ~75 ms (Shifting Halls), too long for
        /// one frame. The pack is read here, on the main thread (its game rooms already loaded in the background: Packs.Ready); the layout is
        /// then made on a worker thread, and picked up on a later call. Layouts.Make only uses our own classes and Vector3/Bounds arithmetic
        /// (no engine calls, its own random numbers, and it never changes the pack: what it adds goes into copies). waiting: call again next
        /// frame.
        /// </summary>
        public static Layout LayoutSoon(string packName, int seed, out string problem, out bool waiting)
        {
            problem = null;
            waiting = false;
            if (string.IsNullOrEmpty(packName)) { problem = "no pack"; return null; }
            string key = packName.ToLowerInvariant() + "|" + seed;
            if (LayoutCache.ContainsKey(key)) return LayoutFor(packName, seed, out problem);
            if (Laying.TryGetValue(key, out Task<Layout> task))
            {
                if (!task.IsCompleted) { waiting = true; return null; }
                Laying.Remove(key);
                if (task.IsFaulted || task.IsCanceled)
                {
                    Exception e = task.Exception?.GetBaseException() ?? new OperationCanceledException();
                    problem = e.GetType().Name + ": " + e.Message;
                    if (Warned.Add(key)) Plugin.Log.LogWarning($"Laying out {packName} seed {seed} failed: {e}");
                    return null;
                }
                return Keep(key, task.Result, out problem);
            }
            if (!Packs.Ready(packName)) { waiting = true; return null; }   // (the game rooms it names, loading in the background)
            Pack pack;
            try { pack = Packs.Get(packName); }
            catch (Exception e) { problem = e.GetType().Name + ": " + e.Message; return null; }
            if (pack == null) { problem = T("dungeons_unknown_kind", packName); return null; }   // (not cached: a pack could come later)
            Laying[key] = Task.Run(() => Layouts.Make(pack, seed));
            waiting = true;
            return null;
        }

        /// <summary>Forget everything (leaving a world, the mod unloading): every built dungeon removed.</summary>
        public static void Clear()
        {
            foreach (Site site in Sites.ToList()) { site.Gone = true; Unbuild(site); }
            Sites.Clear();
            Frozen.Clear();
            Laying.Clear();   // (a layout still being made finishes on its own and is dropped)
            LayoutCache.Clear();
            Warned.Clear();
        }

        // ---- every frame ----

        public static void Update()
        {
            try { Safety.Net(); }
            catch (Exception e) { if (Warned.Add("net")) Plugin.Log.LogWarning("The dungeons' safety net: " + e); }
            for (int i = Sites.Count - 1; i >= 0; i--)
            {
                Site site = Sites[i];
                if (site.Detached)
                {
                    Player p = Player.m_localPlayer;
                    if (site.Zdo == null || p == null || !site.Contains(p.transform.position)) { Untrack(site); continue; }   // (left, or it went)
                }
                else if (site.Entrance == null) { Untrack(site); continue; }   // (destroyed without OnDestroy: a reload)
                if (site.Zdo == null) continue;
                try { Tend(site); }
                catch (Exception e)
                {
                    if (Warned.Add("tend|" + site.Id)) Plugin.Log.LogWarning($"Dungeon {site.Id} ({site.PackName}): {e}");
                }
            }
        }

        private static void Tend(Site site)
        {
            if (site.IsOwner && site.Id.Length == 0) site.Zdo.Set(Keys.Id, NewId());   // (one made before it had an owner here)
            if (site.Layout == null && site.Problem == null && DungeonDB.instance != null && !string.IsNullOrEmpty(site.PackName))
            {
                site.Layout = LayoutSoon(site.PackName, site.Seed, out string problem, out bool waiting);
                if (waiting) return;
                site.Problem = problem;
                if (problem != null) Plugin.Log.LogWarning($"Dungeon {site.Id} ({site.PackName} seed {site.Seed}) can't be built: {problem}");
            }
            if (site.Layout == null) return;
            if (!site.HasOrigin && site.IsOwner) PlaceOrigin(site);
            if (!site.HasOrigin) return;   // (another player owns it and hasn't said yet)
            if (site.Built == null && !site.Building) Plugin.Instance.StartCoroutine(Build(site));
            if (site.Ready)
            {
                if (site.Exit == null) { PlaceExit(site); Thaw(site); }
                if (!site.MechsMade)
                {
                    site.MechsMade = true;
                    try { site.Mechs = MechSet.Make(site); }
                    catch (Exception e) { Fail(site, "making its mechanisms", e); }
                }
                if (site.Mechs != null && !site.Mechs.Complete) site.Mechs.Grow();
                if (site.EnterWhenBuilt && site.MechsReady) { site.EnterWhenBuilt = false; GoIn(site); }
                site.Mechs?.Update();
                if (site.IsOwner) site.Mechs?.OwnerTick();
            }
            if (site.IsOwner && site.Ready && site.PartsPending)
            {
                site.PartsPending = MakeParts(site);   // (the next frame's worth, without waiting for the owner's 2 s tend)
                site.PartsMadeFor = site.Zdo.GetString(Keys.Done) ?? "";
            }
            else if (site.IsOwner && Time.time >= site.NextOwnerCheck)
            {
                site.NextOwnerCheck = Time.time + 2f;
                OwnerTend(site);
            }
        }

        /// <summary>Every physics step: the server's clock, then every built dungeon's moving parts (carrying what stands on them).</summary>
        public static void FixedUpdate()
        {
            MechClock.Step();
            if (Sites.Count == 0) return;
            List<Character> characters = Character.GetAllCharacters();
            foreach (Site site in Sites)
            {
                if (site.Mechs == null || !site.Ready) continue;
                try { site.Mechs.FixedUpdate(characters); }
                catch (Exception e)
                {
                    if (Warned.Add("mechs|" + site.Id)) Plugin.Log.LogWarning($"Dungeon {site.Id} ({site.PackName}): its mechanisms: {e}");
                }
            }
        }

        // ---- where it goes ----

        /// <summary>The owner decides once where the dungeon stands: 5000 m above the entrance, raised by steps until its box is clear of
        /// every other dungeon this game knows (ours, and the game's own interiors near by). Saved, so every player builds it in the same place.</summary>
        private static void PlaceOrigin(Site site)
        {
            Vector3 e = site.EntrancePos;
            Vector3 origin = Vector3.zero;
            int slot;
            for (slot = 0; slot <= MaxSlots; slot++)
            {
                origin = new Vector3(e.x, e.y + Height + slot * Step, e.z);
                var box = new Bounds(origin + site.Layout.Box.center, site.Layout.Box.size);
                box.Expand(Margin * 2f);
                if (!Clashes(box, site)) break;
            }
            slot = Mathf.Min(slot, MaxSlots);
            site.Zdo.Set(Keys.Origin, origin);
            site.Zdo.Set(Keys.Slot, slot);
            Plugin.Log.LogInfo($"Dungeon {site.Id} ({site.PackName} seed {site.Seed}): at height step {slot}, {origin.y:0} m");
        }

        private static bool Clashes(Bounds box, Site self)
        {
            foreach (Site other in Sites)
                if (other != self && other.Box(out Bounds b) && b.Intersects(box)) return true;
            // the game's own interiors: their environment box (Location.Awake: the zone's centre, 5000 m up, 64 x 500 x 64)
            if (AllLocations?.GetValue(null) is List<Location> locations)
                foreach (Location loc in locations)
                {
                    if (loc == null || !loc.m_hasInterior) continue;
                    Vector3 zone = ZoneSystem.GetZonePos(ZoneSystem.GetZone(loc.transform.position));
                    var interior = new Bounds(new Vector3(zone.x, loc.transform.position.y + 5000f, zone.z), new Vector3(64f, 500f, 64f));
                    if (interior.Intersects(box)) return true;
                }
            return false;
        }

        // ---- building and removing ----

        private static IEnumerator Build(Site site)
        {
            site.Building = true;
            IEnumerator steps = null;
            try { steps = Builder.Build(site.Layout, site.Origin, null, b => site.Built = b); }   // (so Untrack can stop a build half way)
            catch (Exception e) { Fail(site, "building", e); }
            while (steps != null)
            {
                object current;
                try
                {
                    if (!steps.MoveNext()) break;
                    current = steps.Current;
                }
                catch (Exception e) { Fail(site, "building", e); break; }
                yield return current;
            }
            site.Building = false;
            if (site.Gone || site.Built == null) yield break;
            if (!site.Built.Done) { site.Problem = site.Problem ?? "its build stopped"; yield break; }
            Plugin.Log.LogInfo($"Dungeon {site.Id} ({site.PackName} seed {site.Seed}) built: {site.Built.Stats}");
        }

        private static void Fail(Site site, string what, Exception e)
        {
            site.Problem = e.GetType().Name + ": " + e.Message;
            if (Warned.Add(what + "|" + site.Id)) Plugin.Log.LogWarning($"Dungeon {site.Id} ({site.PackName} seed {site.Seed}): {what} failed: {e}");
        }

        private static void Unbuild(Site site)
        {
            if (site.Exit != null) Object.Destroy(site.Exit);
            site.Exit = null;
            site.Mechs?.Remove();
            site.Mechs = null;
            site.MechsMade = false;
            site.PartsPending = false;
            if (site.Built == null) return;
            Built built = site.Built;
            FreezeLeftovers(site);
            site.Built = null;
            Remove(built);   // (its environment goes with it, and a build still going stops)
        }

        private static void Remove(Built built)
        {
            try { Builder.Remove(built); }
            catch (Exception e) { Plugin.Log.LogWarning("Removing a dungeon failed: " + e); }
        }

        /// <summary>Saved objects inside that are still here as the floor goes (a part of the dungeon in a zone that unloads later than the
        /// entrance's) stay where they are, not falling, until their own zone unloads, or until the dungeon is built here again (Thaw).</summary>
        private static void FreezeLeftovers(Site site)
        {
            if (ZNetScene.instance == null || !site.Box(out Bounds box)) return;
            box.Expand(8f);
            if (!Frozen.TryGetValue(site.Id, out List<Rigidbody> frozen)) Frozen[site.Id] = frozen = new List<Rigidbody>();
            foreach (ZNetView view in Instances(ZNetScene.instance).Values)
            {
                if (view == null || !box.Contains(view.transform.position) || view.GetComponent<Player>() != null) continue;
                foreach (Rigidbody body in view.GetComponentsInChildren<Rigidbody>())
                {
                    if (body.isKinematic) continue;
                    body.isKinematic = true;
                    frozen.Add(body);
                }
            }
        }

        private static readonly Dictionary<string, List<Rigidbody>> Frozen = new Dictionary<string, List<Rigidbody>>();

        /// <summary>The dungeon is built here again: what FreezeLeftovers held still may move again (on its floor now).</summary>
        private static void Thaw(Site site)
        {
            if (!Frozen.TryGetValue(site.Id, out List<Rigidbody> frozen)) return;
            Frozen.Remove(site.Id);
            foreach (Rigidbody body in frozen) if (body != null) body.isKinematic = false;
        }

        /// <summary>The way out: the entrance's look at the entrance room's doorway, a little inside, facing in.</summary>
        private static void PlaceExit(Site site)
        {
            if (!Doorway(site, 0.3f, out Vector3 at, out Quaternion facing)) return;
            site.Exit = Entrance.MakeLook("quaddungeon_exit", at, facing);
            if (site.Exit != null) site.Exit.AddComponent<DungeonExit>().Site = site;
        }

        /// <summary>The entrance room's way in, in the world: inward from its doorway by this much, and facing into the room.</summary>
        public static bool Doorway(Site site, float inward, out Vector3 at, out Quaternion facing)
        {
            at = Vector3.zero;
            facing = Quaternion.identity;
            if (site.Layout == null || site.Layout.Rooms.Count == 0 || !site.HasOrigin) return false;
            PlacedRoom room = site.Layout.Rooms.FirstOrDefault(r => r.Depth == 0) ?? site.Layout.Rooms[0];
            int door = room.Def.Doorways.FindIndex(d => d.Entrance);
            if (door < 0 && room.Def.Doorways.Count > 0) door = 0;
            if (door < 0) { at = site.Origin + room.Pos; return true; }
            Vector3 dir = room.DoorwayFacing(door);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            dir.Normalize();
            at = site.Origin + room.DoorwayPos(door) + dir * inward;
            facing = Quaternion.LookRotation(dir);
            return true;
        }

        // ---- the owner's jobs ----

        private static void OwnerTend(Site site)
        {
            ZDO zdo = site.Zdo;
            bool inside = AnyoneInside(site);
            if (inside && zdo.GetInt(Keys.Entered, -1) < 0) RecordEntry(site);
            int days = Plugin.ResetDays.Value, entered = zdo.GetInt(Keys.Entered, -1);
            if (days > 0 && entered >= 0 && !inside && site.Ready && EnvMan.instance != null && EnvMan.instance.GetDay() - entered >= days)
                Reset(site, $"{days} days since it was first entered");
            // the networked parts of rooms not done: asked once per state (rooms the builder has nothing for stay not done)
            string state = zdo.GetString(Keys.Done) ?? "";
            if (site.Ready && site.PartsMadeFor != state && site.RoomsDone < site.Layout.Rooms.Count)
            {
                site.PartsPending = MakeParts(site);
                site.PartsMadeFor = zdo.GetString(Keys.Done) ?? "";
            }
        }

        /// <summary>The builder makes the rooms' saved parts, a frame's worth per call (true: more to do, called again next frame); every
        /// object it made in that call is marked as this dungeon's (Keys.PartOf), so a reset can find them again.</summary>
        private static bool MakeParts(Site site)
        {
            Dictionary<ZDO, ZNetView> instances = ZNetScene.instance != null ? Instances(ZNetScene.instance) : null;
            if (instances == null || !site.Box(out Bounds box)) return false;
            var before = new HashSet<ZDO>(instances.Keys);
            bool more = false;
            try { more = Builder.SpawnNetworked(site.Built, site.RoomDone, i => MarkDone(site, i)); }
            catch (Exception e) { if (Warned.Add("spawn|" + site.Id)) Plugin.Log.LogWarning($"Dungeon {site.Id}: making its networked parts failed: {e}"); }
            box.Expand(8f);
            int marked = 0;
            foreach (KeyValuePair<ZDO, ZNetView> kv in instances)
            {
                if (before.Contains(kv.Key) || kv.Value == null || !kv.Key.IsOwner() || !box.Contains(kv.Value.transform.position)) continue;
                kv.Key.Set(Keys.PartOf, site.Id);
                marked++;
            }
            if (marked > 0) Plugin.Log.LogInfo($"Dungeon {site.Id}: {marked} saved parts made");
            return more;
        }

        /// <summary>A player inside: one whose character is loaded here, or (on the server) one whose game says it's there (a player far
        /// inside a big dungeon may not be loaded on this game).</summary>
        public static bool AnyoneInside(Site site) =>
            Player.GetAllPlayers().Any(p => p != null && site.Contains(p.transform.position))
            || (ZNet.instance != null && ZNet.instance.IsServer() && ZNet.instance.GetPeers().Any(peer => peer != null && site.Contains(peer.m_refPos)));

        public static void RecordEntry(Site site)
        {
            if (!site.IsOwner || site.Zdo.GetInt(Keys.Entered, -1) >= 0 || EnvMan.instance == null) return;
            site.Zdo.Set(Keys.Entered, EnvMan.instance.GetDay());
        }

        private static void MarkDone(Site site, int i)
        {
            if (!site.IsOwner || i < 0 || site.Layout == null || i >= site.Layout.Rooms.Count) return;
            char[] done = (site.Zdo.GetString(Keys.Done) ?? "").PadRight(site.Layout.Rooms.Count, '0').ToCharArray();
            done[i] = '1';
            site.Zdo.Set(Keys.Done, new string(done));
        }

        /// <summary>Back to new (the owner, nobody inside): the networked parts it made are removed (made again by the next tend), its rooms
        /// count as not done, every mechanism is back at its start, the entry day is forgotten. Creatures still alive and items dropped
        /// inside stay. Since nobody may be inside, nobody stands in a door it takes away. False when it can't now.</summary>
        public static bool Reset(Site site, string why)
        {
            if (!site.IsOwner || AnyoneInside(site)) return false;
            int removed = RemoveParts(site);
            ZDO zdo = site.Zdo;
            zdo.Set(Keys.Done, "");
            zdo.Set(Keys.Mechs, "");
            site.Mechs?.Forget();
            zdo.Set(Keys.Entered, -1);
            zdo.Set(Keys.Resets, zdo.GetInt(Keys.Resets) + 1);
            if (EnvMan.instance != null) zdo.Set(Keys.ResetDay, EnvMan.instance.GetDay());
            Plugin.Log.LogInfo($"Dungeon {site.Id} ({site.PackName} seed {site.Seed}) is new again ({why}): {removed} of its parts removed to be made again");
            return true;
        }

        /// <summary>Gone for good (the owner, nobody inside): its saved parts as a reset removes them, then the entrance. Creatures it
        /// spawned are not marked as its own and stay. False when it can't now.</summary>
        public static bool Remove(Site site)
        {
            if (!site.IsOwner || AnyoneInside(site) || ZNetScene.instance == null) return false;
            int removed = RemoveParts(site);
            Plugin.Log.LogInfo($"Dungeon {site.Id} ({site.PackName} seed {site.Seed}) removed: its entrance and {removed} of its parts");
            ZNetScene.instance.Destroy(site.View.gameObject);
            return true;
        }

        /// <summary>The saved objects the builder made for this dungeon (marked with its Id), loaded here: removed. A chest's contents are
        /// dropped first (nobody loses what they put in one); one in use is left alone.</summary>
        private static int RemoveParts(Site site)
        {
            if (ZNetScene.instance == null || string.IsNullOrEmpty(site.Id)) return 0;
            var mine = Instances(ZNetScene.instance).Where(kv => kv.Value != null && kv.Key.GetString(Keys.PartOf) == site.Id).Select(kv => kv.Value).ToList();
            int removed = 0;
            foreach (ZNetView view in mine)
            {
                if (view.GetComponent<Character>() != null) continue;   // creatures stay
                ZDO zdo = view.GetZDO();
                if (zdo == null) continue;
                Container chest = view.GetComponent<Container>();
                if (chest != null)
                {
                    if (zdo.GetInt(ZDOVars.s_inUse) == 1) continue;
                    view.ClaimOwnership();
                    if (chest.GetInventory().NrOfItems() > 0)
                    {
                        if (DropAllItems == null) continue;   // (never destroy what we can't empty)
                        DropAllItems.Invoke(chest, null);
                    }
                }
                view.ClaimOwnership();
                ZNetScene.instance.Destroy(view.gameObject);
                removed++;
            }
            return removed;
        }

        // ---- in and out ----

        /// <summary>The entrance's E: in at once if it's built here, else as soon as it is.</summary>
        public static void Enter(Site site)
        {
            Player p = Player.m_localPlayer;
            if (p == null) return;
            if (site.Problem != null) { p.Message(MessageHud.MessageType.Center, T("dungeons_broken", site.Problem)); return; }
            if (!site.MechsReady)
            {
                site.EnterWhenBuilt = true;
                p.Message(MessageHud.MessageType.Center, T("dungeons_wait", site.Title));
                return;
            }
            GoIn(site);
        }

        private static void GoIn(Site site)
        {
            Player p = Player.m_localPlayer;
            if (p == null || !Doorway(site, 2f, out Vector3 at, out Quaternion facing)) return;
            Safety.OurTeleport();
            if (!p.TeleportTo(at + Vector3.up * 0.1f, facing, false)) return;
            try { site.Act("entered"); } catch (Exception) { }
            if (MessageHud.instance != null) MessageHud.instance.ShowBiomeFoundMsg(site.Title, false);
        }

        /// <summary>Out, in front of the entrance, facing away from it.</summary>
        public static void Leave(Site site)
        {
            Player p = Player.m_localPlayer;
            if (p == null) return;
            Safety.OurTeleport();
            p.TeleportTo(OutsidePoint(site), site.EntranceRot, false);
        }

        /// <summary>The ground just in front of the entrance, clear of its look (where the logout point goes for a player inside).</summary>
        public static Vector3 OutsidePoint(Site site)
        {
            Vector3 at = site.EntrancePos + site.EntranceRot * Vector3.forward * EntranceLooks.Get(site.Look).Front;
            float ground = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(at) : at.y;
            at.y = Mathf.Max(site.EntrancePos.y, ground) + 0.3f;
            return at;
        }
    }
}
