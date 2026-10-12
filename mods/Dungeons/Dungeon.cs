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
    /// <summary>One dungeon this game knows about: its entrance (loaded, or Detached: not loaded here, its zone being out of this game's
    /// range while its player is inside, which in a dungeon of 320 m happens well inside it), its layout, where it stands, and what of it
    /// is built here. What the entrance's ZDO says is kept as it's read (pack, seed, id, origin), so the dungeon is still known while the
    /// ZDO itself isn't at hand.</summary>
    internal class Site
    {
        public DungeonEntrance Entrance;
        public ZDO SavedZdo;            // the entrance's ZDO, kept for while the entrance object is gone (Detached)
        public Vector3 EntrancePos;     // where the entrance stands (entrances never move)
        public Quaternion EntranceRot;
        public string Look = EntranceLooks.Default;
        public bool Detached;           // its entrance object isn't loaded here: kept (built) while the local player is in its space
        public int RemakeSeen = -1;     // the remake count (Keys.Remake) this game built it with
        public float NextFresh;         // a client, Detached: when to ask the server for the entrance's ZDO again (its state, fresh)
        private string _pack = "", _id = "";
        private int _seed;
        private Vector3 _origin;
        private bool _hasOrigin;
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
        public float NextClaim;         // (Tend: an entrance nobody owns, claimed at most once a second)

        public ZNetView View => Entrance != null ? Entrance.View : null;
        /// <summary>The entrance's ZDO: its object's, or the one kept from it while this game's ZDOMan still has it (its object unloaded, or
        /// between the game taking the ZDO off the object and destroying it).</summary>
        public ZDO Zdo => View != null && View.IsValid() ? View.GetZDO()
                        : SavedZdo != null && ZDOMan.instance != null && ZDOMan.instance.GetZDO(SavedZdo.m_uid) == SavedZdo ? SavedZdo : null;
        /// <summary>This game owns the entrance's ZDO (its object loaded here or not: the owner's jobs need only the ZDO and the built
        /// dungeon).</summary>
        public bool IsOwner => Zdo != null && Zdo.IsOwner();

        /// <summary>Tells the entrance's owner something: to every game (the one that owns the entrance acts on it, wherever it is; the
        /// entrance object's own RPC would only reach a game that has it loaded).</summary>
        public void Act(string act)
        {
            if (ZRoutedRpc.instance == null || Id.Length == 0) return;
            string wire = Acts.Sent(this, act);   // (a one-off act gets an id: answered by the owner, sent again if not, never done twice: Acts.cs)
            if (wire == null) return;            // (its owner runs an older copy: this game took it over and acted)
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, Dungeons.SiteRpc, Id, wire);
        }

        /// <summary>This game takes the entrance's ZDO (a command, or nobody owning it while this game's player is far inside).</summary>
        public void Claim()
        {
            if (View != null && View.IsValid()) View.ClaimOwnership();
            else Zdo?.SetOwner(ZDOMan.GetSessionID());
        }

        public string PackName { get { ZDO z = Zdo; if (z != null) _pack = z.GetString(Keys.Pack) ?? ""; return _pack; } }
        public int Seed { get { ZDO z = Zdo; if (z != null) _seed = z.GetInt(Keys.Seed); return _seed; } }
        public string Id { get { ZDO z = Zdo; if (z != null) { string id = z.GetString(Keys.Id); if (!string.IsNullOrEmpty(id)) _id = id; } return _id; } }
        public bool Ready => Built != null && Built.Done;
        public bool MechsReady => Ready && MechsMade && (Mechs == null || Mechs.Complete);   // built, its moving parts too

        public bool HasOrigin { get { ReadOrigin(); return _hasOrigin; } }
        public Vector3 Origin { get { ReadOrigin(); return _origin; } }

        /// <summary>Forgets the origin it read (a remake with a new seed may place the dungeon elsewhere).</summary>
        public void ForgetOrigin() { _hasOrigin = false; _origin = Vector3.zero; }

        private void ReadOrigin()
        {
            ZDO z = Zdo;
            if (z != null && z.GetVec3(Keys.Origin, out Vector3 v)) { _origin = v; _hasOrigin = true; }
        }

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
    internal static partial class Dungeons   // (HotReload.cs: Frozen is carried over a reload)
    {
        public const float Height = 5000f;       // above the entrance, as the game's own interiors (Location.Awake)
        public const float Step = 50f;           // a dungeon that would overlap another goes up by steps of this
        public const int MaxSlots = 40;
        public const float Margin = 8f;          // kept free between two dungeons' boxes
        public const float UnknownReach = 480f;  // how far around an entrance a dungeon not yet laid out may reach (layouts are up to 320 x
                                                 // 320 m, the entrance room anywhere in them: the far corner is ~450 m away)
        public const string SiteRpc = "Dungeons_Site";   // (dungeon id, act): to every game; the entrance's owner acts
        public const string NearRpc = "Dungeons_Near";   // (a point): a client asks the server for the entrances near it

        public static readonly List<Site> Sites = new List<Site>();
        private static readonly Dictionary<string, Layout> LayoutCache = new Dictionary<string, Layout>();
        private static readonly HashSet<string> Warned = new HashSet<string>();
        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> Instances = AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");
        private static readonly System.Reflection.FieldInfo AllLocations = AccessTools.Field(typeof(Location), "s_allLocations");

        /// <summary>Our own id for a dungeon, saved on its entrance (a ZDOID changes every time the world loads).</summary>
        public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 12);

        public static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        // ---- knowing them ----

        public static Site Track(DungeonEntrance entrance)
        {
            ZDO zdo = entrance.View?.GetZDO();
            // its entrance back while this game kept the dungeon (Detached): the same one again
            Site site = Sites.FirstOrDefault(s => s.Detached && zdo != null && s.SavedZdo == zdo && !s.Gone);
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

        /// <summary>The entrance object is going (its zone unloads here, or it's removed). A dungeon this game's player is in (or over: its
        /// space) stays (Detached) until they leave it: a big dungeon reaches far past the zones loaded around them (up to 320 m, the
        /// game loads a zone or two each way), and its floor must not go from under them, their gravestone or anything they dropped.
        /// Otherwise it's taken down. (The game takes the ZDO off the object before destroying it: the site's cached origin and the
        /// saved ZDO answer meanwhile.)</summary>
        public static void Lost(Site site)
        {
            Player p = Player.m_localPlayer;
            bool removed = site.SavedZdo == null || ZDOMan.instance == null || ZDOMan.instance.GetZDO(site.SavedZdo.m_uid) != site.SavedZdo;
            if (!site.Gone && !removed && p != null && site.Covers(p.transform.position))
            {
                site.Entrance = null;
                site.Detached = true;
                Plugin.Log.LogInfo($"Dungeon {site.Id}: its entrance unloaded here while the player is in it ({FlatDistance(p.transform.position, site.EntrancePos):0} m from the entrance): kept until they leave");
                return;
            }
            Untrack(site);
        }

        // ---- dungeons known from their ZDO alone (a relog or a teleport far inside, the entrance out of range) ----

        private static float _nextAsk;
        public static float AskedAt = -100f;

        /// <summary>The dungeons whose space a point (above 3000 m) may be in, from the entrances this game's ZDOMan knows (the host knows
        /// every one; a client those the server sent it), tracked as Detached sites to lay out and build here. A client that knows of none
        /// asks the server (Dungeons_Near) for the entrances near the point, at most every 3 s. True while it's waiting for that answer.</summary>
        public static bool Discover(Vector3 p)
        {
            if (p.y < 3000f || ZDOMan.instance == null) return false;
            int added = 0;
            foreach (ZDO z in EntranceZdos())
            {
                if (FlatDistance(z.GetPosition(), p) > UnknownReach || !z.GetVec3(Keys.Origin, out Vector3 origin)) continue;
                if (Sites.Any(s => s.SavedZdo == z && !s.Gone)) continue;
                // laid out before (this world): only if its space has the point, so one that doesn't isn't built and taken down over and over
                if (LayoutCache.TryGetValue((z.GetString(Keys.Pack) ?? "").ToLowerInvariant() + "|" + z.GetInt(Keys.Seed), out Layout known) && known.Ok)
                {
                    Bounds b = new Bounds(origin + known.Box.center, known.Box.size);
                    if (p.x < b.min.x - 16f || p.x > b.max.x + 16f || p.z < b.min.z - 16f || p.z > b.max.z + 16f || p.y > b.max.y + 100f) continue;
                }
                var site = new Site { SavedZdo = z, EntrancePos = z.GetPosition(), EntranceRot = z.GetRotation(), Detached = true,
                                      Look = Entrance.LookOf(z) };
                Sites.Add(site);
                added++;
                Plugin.Log.LogInfo($"Dungeon {site.Id} ({site.PackName} seed {site.Seed}): known from its entrance's ZDO ({FlatDistance(site.EntrancePos, p):0} m away, not loaded here): laying it out");
            }
            if (added > 0 || Covering(p) != null) return false;
            if (ZNet.instance == null || ZNet.instance.IsServer() || ZRoutedRpc.instance == null) return false;
            if (Time.time >= _nextAsk)
            {
                _nextAsk = Time.time + 3f;
                AskedAt = Time.time;
                ZRoutedRpc.instance.InvokeRoutedRPC(NearRpc, p);
            }
            return Time.time - AskedAt < 6f;
        }

        private static readonly List<ZDO> FoundZdos = new List<ZDO>();

        internal static List<ZDO> EntranceZdos()
        {
            FoundZdos.Clear();
            foreach (string name in Entrance.PrefabNames)
            {
                int index = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(name, FoundZdos, ref index)) { }
            }
            return FoundZdos;
        }

        /// <summary>A client with a dungeon not loaded here: the entrance's ZDO asked from the server every 2 s (it sends it, out of this
        /// game's range or not), so the dungeon's state (its mechanisms, rooms done) stays the owner's. Nobody owning it (everyone far from
        /// its entrance, inside): this game takes it, so its plates and levers still work.</summary>
        private static void KeepFresh(Site site)
        {
            ZDO z = site.Zdo;
            if (z == null || Time.time < site.NextFresh) return;
            site.NextFresh = Time.time + 2f;
            if (ZNet.instance != null && !ZNet.instance.IsServer()) ZDOMan.instance.RequestZDO(z.m_uid);
            if (!z.HasOwner())
            {
                z.SetOwner(ZDOMan.GetSessionID());
                Plugin.Log.LogInfo($"Dungeon {site.Id}: nobody near its entrance owned it: this game took it (its player is inside)");
            }
        }

        // ---- the dungeons' RPCs, through ZRoutedRpc (they reach every game, whatever is loaded where) ----

        private static ZRoutedRpc _rpcFor;

        /// <summary>Registers our RPCs once per ZRoutedRpc (every world has its own).</summary>
        private static void RegisterRpcs()
        {
            if (ZRoutedRpc.instance == null || ZRoutedRpc.instance == _rpcFor) return;
            _rpcFor = ZRoutedRpc.instance;
            try
            {
                ZRoutedRpc.instance.Register<string, string>(SiteRpc, RPC_Site);
                ZRoutedRpc.instance.Register<Vector3>(NearRpc, RPC_Near);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Registering the dungeons' RPCs: " + e.Message); }
        }

        /// <summary>Something for a dungeon's owner: the game that owns its entrance acts on it (the sender is the RPC's).</summary>
        private static void RPC_Site(long sender, string id, string act)
        {
            Site site = Sites.FirstOrDefault(s => !s.Gone && s.Id == id && s.IsOwner);
            if (site == null && ZNet.instance != null && ZNet.instance.IsServer())
            {
                // nobody owns the entrance (or a game that left): the server decides, alone (every act passes it). With the dungeon built
                // here it takes the entrance and acts; else the sender is told to (Acts.cs). Never dropped, never done twice.
                Site built = Sites.FirstOrDefault(s => !s.Gone && s.Id == id && s.Ready && s.Mechs != null);
                ZDO entrance = built?.Zdo ?? EntranceZdos().FirstOrDefault(z => z.GetString(Keys.Id) == id);
                if (entrance != null && Acts.Unowned(entrance))
                {
                    if (built == null) { Acts.TellNoOwner(sender, id, act); return; }
                    Plugin.Log.LogWarning($"Dungeon {id}: \"{act}\" came while nobody owned its entrance (owner {entrance.GetOwner()}): the server takes it and acts on it");
                    built.Claim();
                    site = built;
                }
            }
            if (site == null) return;
            Acts.Handle(site, sender, act);   // (once per act id, answered)
        }

        public static void HandleAct(Site site, long sender, string act)
        {
            if (act == "entered") RecordEntry(site);
            else if (act != null && act.StartsWith("use:")) site.Mechs?.UseFrom(sender, act.Substring(4));
            else if (act != null && act.StartsWith("plate:")) site.Mechs?.PressFrom(sender, act.Substring(6));
            else if (act != null && act.StartsWith("area:")) site.Mechs?.InAreaFrom(sender, act.Substring(5));
            else if (act != null && act.StartsWith("hit:")) site.Mechs?.HitFrom(sender, act.Substring(4));
            else if (act != null && act.StartsWith("blk:")) site.Mechs?.BlockAct(sender, act.Substring(4));   // (triggers: blocks used, players' places, kills)
            else Plugin.Log.LogInfo($"Dungeons act from {sender}: \"{act}\" isn't known (a newer version?)");
        }

        /// <summary>The server: a client is somewhere high up with no dungeon it knows of there; it gets the entrances near that point.</summary>
        private static void RPC_Near(long sender, Vector3 p)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null) return;
            int sent = 0;
            foreach (ZDO z in EntranceZdos())
            {
                if (FlatDistance(z.GetPosition(), p) > UnknownReach) continue;
                ZDOMan.instance.ForceSendZDO(sender, z.m_uid);
                sent++;
            }
            if (sent > 0) Plugin.Log.LogInfo($"A player at {p} asked for the dungeons there: {sent} entrance(s) sent");
        }

        /// <summary>Sync's names for the remake seam (Rebuild.cs has the work): who's inside, by name and position.</summary>
        public static List<KeyValuePair<string, Vector3>> WhoInside(Site site) => Rebuild.WhoInside(site);

        /// <summary>Sync's name for Rebuild.Remake: the owner remakes the dungeon from the packs in use, with this seed; false when it can't
        /// (someone inside, not the owner).</summary>
        public static bool Remake(Site site, int seed, string why) => Rebuild.Remake(site, seed, why, out _);

        /// <summary>This game forgets a dungeon's layout and built copy (Rebuild): it's laid out again from the packs in use and built, as
        /// when its entrance first came. Its objects in the air wait meanwhile (Safety.HoldBack).</summary>
        public static void Forget(Site site)
        {
            Unbuild(site);
            site.RemakeSeen = site.Zdo != null ? site.Zdo.GetInt(Keys.Remake) : site.RemakeSeen;
            site.ForgetOrigin();
            string key = site.PackName.ToLowerInvariant() + "|" + site.Seed;
            LayoutCache.Remove(key);
            Laying.Remove(key);
            site.Layout = null;
            site.Problem = null;
            site.PartsMadeFor = null;
            site.PartsPending = false;
            site.Building = false;
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
            DungeonMap.Clear();   // (added by the map, MapWindow.cs: its window, pictures and what it remembered)
            DungeonMusic.Clear();
            foreach (Site site in Sites.ToList()) { site.Gone = true; Unbuild(site); }
            Sites.Clear();
            Frozen.Clear();
            HeldWas.Clear();
            FrozenBox.Clear();
            LetGoAt.Clear();
            Laying.Clear();   // (a layout still being made finishes on its own and is dropped)
            LayoutCache.Clear();
            Warned.Clear();
        }

        // ---- every frame ----

        private static int _packGen = -1;

        public static void Update()
        {
            // the packs in use changed (the server's set arrived): layouts made from the old set go, and a dungeon that couldn't be laid
            // out (its pack unknown then) tries again
            if (_packGen != PackSync.Generation)
            {
                bool first = _packGen < 0;
                _packGen = PackSync.Generation;
                if (!first)
                {
                    LayoutCache.Clear();
                    Laying.Clear();
                    foreach (Site s in Sites) if (s.Layout == null) s.Problem = null;
                }
            }
            RegisterRpcs();
            Sweep.Register();
            Rebuild.Register();
            Strays.Register();
            Acts.Register();
            Sweep.Tick();
            try { Acts.Tick(); }
            catch (Exception e) { if (Warned.Add("acts")) Plugin.Log.LogWarning("Acts waiting for an answer: " + e); }
            try { DungeonMusic.Update(); }
            catch (Exception e) { if (Warned.Add("music")) Plugin.Log.LogWarning("The dungeon's music: " + e); }
            try { UseReach.Update(); }
            catch (Exception e) { if (Warned.Add("reach")) Plugin.Log.LogWarning("E on a lever or way out: " + e); }
            try { ThawMovedOut(); }
            catch (Exception e) { if (Warned.Add("thaw")) Plugin.Log.LogWarning("Letting held bodies go: " + e); }
            try { Safety.Net(); }
            catch (Exception e) { if (Warned.Add("net")) Plugin.Log.LogWarning("The dungeons' safety net: " + e); }
            for (int i = Sites.Count - 1; i >= 0; i--)
            {
                Site site = Sites[i];
                if (site.Detached)
                {
                    // kept while the local player is in its space (inside, or over it: up to its box's top and down to 3000 m), however far
                    // from the entrance; and its ZDO kept fresh on a client (the server sends it on request, out of the client's range)
                    Player p = Player.m_localPlayer;
                    if (p == null || !site.Covers(p.transform.position)) { Untrack(site); continue; }
                    KeepFresh(site);
                }
                else if (site.Entrance == null) { Untrack(site); continue; }   // (destroyed without OnDestroy: a reload)
                if (site.Zdo == null && site.Layout == null) continue;   // (a dungeon already laid out goes on from what it knows)
                try { Tend(site); }
                catch (Exception e)
                {
                    if (Warned.Add("tend|" + site.Id)) Plugin.Log.LogWarning($"Dungeon {site.Id} ({site.PackName}): {e}");
                }
            }
            // (added by the map, MapWindow.cs: the rooms the player has been in, and the map window while it's open)
            try { DungeonMap.Tick(); }
            catch (Exception e) { if (Warned.Add("map")) Plugin.Log.LogWarning("The dungeon map: " + e); }
        }

        private static void Tend(Site site)
        {
            Rebuild.Watch(site);
            // nobody owns the entrance (or a game that left) while a player is inside: this game takes it if its player is in there, the
            // server if anyone is (an act needs an owner: Acts.cs)
            if (site.Ready && Acts.Unowned(site.Zdo) && Time.time >= site.NextClaim)
            {
                Player me = Player.m_localPlayer;
                bool mine = me != null && site.Contains(me.transform.position);
                if (mine || (ZNet.instance != null && ZNet.instance.IsServer() && AnyoneInside(site)))
                {
                    site.NextClaim = Time.time + 1f;
                    Plugin.Log.LogInfo($"Dungeon {site.Id}: nobody owned its entrance (owner {site.Zdo.GetOwner()}) with {(mine ? "this game's player" : "a player")} inside: this game takes it");
                    site.Claim();
                }
            }
            if (site.IsOwner) Acts.Mark(site);   // (the owner says it speaks act ids: Acts.cs)
            else if (site.Zdo != null) Acts.OlderOwner(site, site.Zdo);   // (how long its owner has gone unmarked: an older copy after 3 s)
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
            if (site.Box(out Bounds box)) HoldStill(site.Id, box);
        }

        private static readonly Dictionary<string, List<Rigidbody>> Frozen = new Dictionary<string, List<Rigidbody>>();
        private static readonly Dictionary<Rigidbody, RigidbodyConstraints> HeldWas = new Dictionary<Rigidbody, RigidbodyConstraints>();   // (each held body's own constraints)
        private static readonly Dictionary<string, Bounds> FrozenBox = new Dictionary<string, Bounds>();   // (where they were held: one moved out of it goes free)
        private static float _nextThawCheck;

        /// <summary>
        /// The bodies of what's loaded in a dungeon's box (and 8 m round it), players aside, held still for that dungeon: every position
        /// and turn frozen by the body's constraints (its own kept, given back after). Never isKinematic: a creature's movement sets its
        /// body's velocity every physics step, and Unity warns for each on a kinematic body (19,500 lines in Quadbobheim, 0.3.2).
        /// Also HotReload's FreezeIn (a husk's box).
        /// </summary>
        internal static void HoldStill(string id, Bounds box)
        {
            if (ZNetScene.instance == null || string.IsNullOrEmpty(id)) return;
            box.Expand(8f);
            if (!Frozen.TryGetValue(id, out List<Rigidbody> frozen)) Frozen[id] = frozen = new List<Rigidbody>();
            if (FrozenBox.TryGetValue(id, out Bounds had)) box.Encapsulate(had);
            FrozenBox[id] = box;
            foreach (ZNetView view in Instances(ZNetScene.instance).Values)
            {
                if (view == null || !box.Contains(view.transform.position) || view.GetComponent<Player>() != null) continue;
                foreach (Rigidbody body in view.GetComponentsInChildren<Rigidbody>()) Hold(id, body);
            }
        }

        /// <summary>One body held still for a dungeon (HoldStill). One kinematic of its own isn't ours to hold.</summary>
        private static void Hold(string id, Rigidbody body)
        {
            if (body == null || body.isKinematic || HeldWas.ContainsKey(body)) return;
            if (!Frozen.TryGetValue(id, out List<Rigidbody> frozen)) Frozen[id] = frozen = new List<Rigidbody>();
            HeldWas[body] = body.constraints;
            body.constraints = RigidbodyConstraints.FreezeAll;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            frozen.Add(body);
        }

        /// <summary>A held body goes free: its own constraints back (or, one a last copy made kinematic, not kinematic).</summary>
        private static void Free(Rigidbody body)
        {
            if (body == null) return;
            if (HeldWas.TryGetValue(body, out RigidbodyConstraints was)) { body.constraints = was; HeldWas.Remove(body); }
            else if (body.isKinematic) body.isKinematic = false;
        }

        /// <summary>One body moved by us (Rebuild.Place: a remake's move-out, Strays): let go if held, and forgotten.</summary>
        public static void Release(Rigidbody body)
        {
            if (body == null) return;
            bool held = HeldWas.ContainsKey(body);
            foreach (List<Rigidbody> list in Frozen.Values) held |= list.Remove(body);
            if (held) Free(body);
        }

        /// <summary>A dungeon removed (its entrance gone): what's still held for it is let go once Strays have had time to move it down
        /// (they run 5 s after a remove); anything still held then goes free where it is.</summary>
        private static readonly Dictionary<string, float> LetGoAt = new Dictionary<string, float>();

        public static void LetGoLater(string id, float seconds)
        {
            if (!string.IsNullOrEmpty(id) && Frozen.ContainsKey(id)) LetGoAt[id] = Time.time + seconds;
        }

        /// <summary>The dungeon is built here again: what FreezeLeftovers held still may move again (on its floor now).</summary>
        private static void Thaw(Site site)
        {
            FrozenBox.Remove(site.Id);
            if (!Frozen.TryGetValue(site.Id, out List<Rigidbody> frozen)) return;
            Frozen.Remove(site.Id);
            foreach (Rigidbody body in frozen) Free(body);
        }

        /// <summary>Once a second: a held body that's gone, or out of the box it was held in (moved down by Strays, out to the entrance by a
        /// remake, a companion teleported to its player), is let go: nothing stays held where no dungeon needs it.</summary>
        private static void ThawMovedOut()
        {
            if ((Frozen.Count == 0 && LetGoAt.Count == 0) || Time.time < _nextThawCheck) return;
            _nextThawCheck = Time.time + 1f;
            foreach (KeyValuePair<string, float> due in LetGoAt.ToList())
            {
                if (Time.time < due.Value) continue;
                LetGoAt.Remove(due.Key);
                if (!Frozen.TryGetValue(due.Key, out List<Rigidbody> left)) continue;
                int n = left.Count(b => b != null);
                foreach (Rigidbody body in left) Free(body);
                Frozen.Remove(due.Key);
                FrozenBox.Remove(due.Key);
                if (n > 0) Plugin.Log.LogInfo($"Dungeon {due.Key} is gone: {n} bodies held for it let go");
            }
            foreach (KeyValuePair<string, List<Rigidbody>> kv in Frozen.ToList())
            {
                Bounds box = default;
                bool hasBox = FrozenBox.TryGetValue(kv.Key, out box);
                if (!hasBox)
                {
                    Site site = Sites.FirstOrDefault(s => !s.Gone && s.Id == kv.Key);
                    if (site != null && site.Box(out box)) { box.Expand(8f); hasBox = true; }
                }
                // its dungeon removed (the server knows every entrance: none with this id): let go once Strays have had their go
                if (!hasBox && !LetGoAt.ContainsKey(kv.Key) && ZNet.instance != null && ZNet.instance.IsServer()
                    && !EntranceZdos().Any(z => z.GetString(Keys.Id) == kv.Key))
                    LetGoLater(kv.Key, 30f);
                kv.Value.RemoveAll(body =>
                {
                    if (body == null) return true;
                    // below 3000 m no dungeon is (moved down, or on the ground already); else out of the box it was held in
                    if (body.position.y >= 3000f && (!hasBox || box.Contains(body.position))) return false;
                    Free(body);
                    return true;
                });
                if (kv.Value.Count == 0) { Frozen.Remove(kv.Key); FrozenBox.Remove(kv.Key); }
            }
            foreach (Rigidbody gone in HeldWas.Keys.Where(b => b == null).ToList()) HeldWas.Remove(gone);
        }

        /// <summary>HotReload: the held bodies' own constraints, carried with Frozen.</summary>
        public static Dictionary<Rigidbody, RigidbodyConstraints> CarryHeldWas()
        {
            var all = HeldWas.Where(kv => kv.Key != null).ToDictionary(kv => kv.Key, kv => kv.Value);
            HeldWas.Clear();
            return all;
        }

        /// <summary>HotReload's TakeFrozen, for each body a last copy held: kept held. One that copy made kinematic (0.3.2 and before) is
        /// held the new way instead, so its creature's velocity no longer warns.</summary>
        public static void Adopt(string id, Rigidbody body)
        {
            if (body == null || string.IsNullOrEmpty(id)) return;
            if (!Frozen.TryGetValue(id, out List<Rigidbody> frozen)) Frozen[id] = frozen = new List<Rigidbody>();
            if (HeldWas.ContainsKey(body)) { if (!frozen.Contains(body)) frozen.Add(body); return; }
            if (body.isKinematic) body.isKinematic = false;
            Hold(id, body);
        }

        /// <summary>HotReload: the boxes the bodies were held in (a body moved out of one goes free), carried with Frozen.</summary>
        public static Dictionary<string, Bounds> CarryFrozenBox()
        {
            var all = FrozenBox.ToDictionary(kv => kv.Key, kv => kv.Value);
            FrozenBox.Clear();
            return all;
        }

        public static void TakeFrozenBox(Dictionary<string, Bounds> all)
        {
            if (all == null) return;
            foreach (KeyValuePair<string, Bounds> kv in all) FrozenBox[kv.Key] = FrozenBox.TryGetValue(kv.Key, out Bounds had) ? Enclose(had, kv.Value) : kv.Value;
        }

        private static Bounds Enclose(Bounds a, Bounds b) { a.Encapsulate(b); return a; }

        public static void TakeHeldWas(Dictionary<Rigidbody, RigidbodyConstraints> all)
        {
            if (all == null) return;
            foreach (KeyValuePair<Rigidbody, RigidbodyConstraints> kv in all) if (kv.Key != null) HeldWas[kv.Key] = kv.Value;
        }

        /// <summary>
        /// The way out, in the entrance room's way-in doorway, as seen from inside: the look's "inside" parts (EntranceLooks: the same door
        /// as outside, sized to the doorway), in the doorway's frame with its origin on the floor under the doorway (found with a ray: the
        /// game's doorways and ours don't sit at the same height over it) and +z into the room. A look without them gets the stand-in door
        /// scaled to the doorway (its end caps' size). E anywhere on it: an aim box over the whole door.
        /// </summary>
        private static void PlaceExit(Site site)
        {
            if (!Doorway(site, 0f, out Vector3 at, out Quaternion facing)) return;
            Vector3 floor = FloorAt(at + facing * Vector3.forward * 0.6f, at.y);
            var root = new GameObject("quaddungeon_exit");
            root.transform.SetPositionAndRotation(new Vector3(at.x, floor.y, at.z), facing);
            if (site.Built?.Root != null) root.transform.SetParent(site.Built.Root.transform, true);
            int made = 0;
            foreach (PartDef part in EntranceLooks.Get(site.Look).Inside)
            {
                try { if (Builder.MakePart(part, root.transform) != null) made++; }
                catch (Exception e) { Plugin.Log.LogWarning($"The way out of dungeon {site.Id}, part {part.Prefab}: {e.Message}"); }
            }
            if (made == 0)
            {
                // no inside parts (or none this game has): the stand-in, scaled to the doorway, standing on the floor
                GameObject door = Entrance.MakeLook("quaddungeon_exit_door", root.transform.position, facing);
                if (door != null)
                {
                    door.transform.SetParent(root.transform, true);
                    door.transform.localPosition = new Vector3(0f, 0f, 0.3f);
                    FitToDoorway(site, door);
                }
            }
            AimBox(root);
            root.AddComponent<DungeonExit>().Site = site;
            site.Exit = root;
        }

        private static int _floorMask = -1;

        /// <summary>The floor under a point (a ray down from a little above it), or the point's own height when nothing's found.</summary>
        private static Vector3 FloorAt(Vector3 p, float fallback)
        {
            if (_floorMask == -1) _floorMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
            return Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 5f, _floorMask) ? hit.point : new Vector3(p.x, fallback, p.z);
        }

        /// <summary>The stand-in made the size of the way-in doorway: its type's end caps' box (width and height), a little smaller.</summary>
        private static void FitToDoorway(Site site, GameObject door)
        {
            PlacedRoom room = site.Layout?.Rooms.FirstOrDefault(r => r.Depth == 0);
            Doorway way = room?.Def.Doorways.FirstOrDefault(d => d.Entrance) ?? room?.Def.Doorways.FirstOrDefault();
            RoomDef cap = way == null ? null : site.Layout.Pack.Rooms.Where(r => r.EndCap && r.Doorways.Any(d => (d.Type ?? "") == (way.Type ?? "")))
                                                                     .OrderBy(r => r.Size.x).FirstOrDefault();
            Bounds? b = null;
            foreach (Renderer r in door.GetComponentsInChildren<Renderer>())
            {
                if (b == null) b = r.bounds; else { Bounds x = b.Value; x.Encapsulate(r.bounds); b = x; }
            }
            if (cap == null || b == null || cap.Size.x <= 0f || cap.Size.y <= 0f) return;
            float width = Mathf.Max(b.Value.size.x, b.Value.size.z), height = b.Value.size.y;
            float k = Mathf.Min(cap.Size.x * 0.6f / Mathf.Max(width, 0.1f), cap.Size.y * 0.6f / Mathf.Max(height, 0.1f));
            if (k > 1f) door.transform.localScale *= k;
        }

        /// <summary>One box to aim at over everything under root (piece_nonsolid: walked through, found by the hover ray), a little bigger.</summary>
        private static void AimBox(GameObject root)
        {
            Bounds? local = null;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                Bounds wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = root.transform.InverseTransformPoint(new Vector3((i & 1) == 0 ? wb.min.x : wb.max.x, (i & 2) == 0 ? wb.min.y : wb.max.y, (i & 4) == 0 ? wb.min.z : wb.max.z));
                    if (local == null) local = new Bounds(c, Vector3.zero); else { Bounds l = local.Value; l.Encapsulate(c); local = l; }
                }
            }
            if (local == null) return;
            var go = new GameObject("quaddungeon_aim");
            go.transform.SetParent(root.transform, false);
            int layer = LayerMask.NameToLayer("piece_nonsolid");
            if (layer >= 0) go.layer = layer;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.center = local.Value.center;
            box.size = local.Value.size + Vector3.one * 0.4f;
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
            // the saved parts were made for another layout (the pack changed in an update, and the same seed lays out otherwise): they
            // stand where no room is now (spawners in the air, their creatures falling). Removed and made again, once nobody is inside.
            Rebuild.Tick(site);
            if (site.Ready && zdo.GetInt(Keys.RemakeWanted) > 0) { Rebuild.Start(site, true, out _); return; }   // (marked by a reload while it wasn't loaded)
            if (site.Ready && !LayoutMatches(site, zdo, inside)) return;
            if (site.Ready) RemoveStale(site);
            // the networked parts of rooms not done: asked once per state (rooms the builder has nothing for stay not done)
            string state = zdo.GetString(Keys.Done) ?? "";
            if (site.Ready && site.PartsMadeFor != state && site.RoomsDone < site.Layout.Rooms.Count)
            {
                site.PartsPending = MakeParts(site);
                site.PartsMadeFor = zdo.GetString(Keys.Done) ?? "";
            }
        }

        /// <summary>A layout's signature: its pack, and each room's name, place and turn. The same pack and seed give the same one; a
        /// pack changed by an update may not.</summary>
        public static int Signature(Layout layout)
        {
            var sb = new System.Text.StringBuilder(layout.Pack?.Name ?? "");
            foreach (PlacedRoom r in layout.Rooms)
                sb.Append(';').Append(r.Def?.Name).Append('@').Append(Mathf.RoundToInt(r.Pos.x)).Append(',').Append(Mathf.RoundToInt(r.Pos.y))
                  .Append(',').Append(Mathf.RoundToInt(r.Pos.z)).Append('/').Append(Mathf.RoundToInt(r.Yaw));
            return sb.ToString().GetStableHashCode();
        }

        /// <summary>The owner, every tend: this dungeon's saved parts loaded here that were made for another layout (out of reach when the
        /// parts were made again: a big dungeon reaches past what's loaded) are removed as they come into range.</summary>
        private static void RemoveStale(Site site)
        {
            int sig = Signature(site.Layout), gen = site.Zdo.GetInt(Keys.Gen);
            int removed = RemoveParts(site, z => z.GetInt(Keys.PartSig, 0) != sig || z.GetInt(Keys.PartGen, 0) != gen);
            if (ChestsLeft > 0 && Time.time >= _nextChestSweep) { _nextChestSweep = Time.time + 30f; Sweep.Everywhere(site.Id, gen, false); }   // (old chests holding something: the server empties them)
            if (removed > 0) Plugin.Log.LogWarning($"Dungeon {site.Id}: {removed} saved parts of an older layout or an earlier reset removed");
        }

        /// <summary>The owner: the saved parts belong to this layout (true), or they're being made again (false: nothing more this tend).
        /// A dungeon with no signature yet but with parts made counts as another layout's (it was made before signatures were kept).</summary>
        private static bool LayoutMatches(Site site, ZDO zdo, bool inside)
        {
            int sig = Signature(site.Layout), saved = zdo.GetInt(Keys.LayoutSig, 0);
            if (saved == sig) return true;
            if (site.RoomsDone == 0) { zdo.Set(Keys.LayoutSig, sig); return true; }
            // made for another layout (its pack changed in an update): the same remake as a pack reload (Rebuild): players inside taken
            // out first, gravestones, items and tames to the ground outside, creatures and keys removed, parts and state made again
            Plugin.Log.LogWarning($"Dungeon {site.Id} ({site.PackName} seed {site.Seed}): its saved parts were made for another layout (its pack changed): remade");
            Rebuild.Start(site, true, out _);
            return false;
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
                kv.Key.Set(Keys.PartSig, Signature(site.Layout));
                kv.Key.Set(Keys.PartGen, site.Zdo.GetInt(Keys.Gen));
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
        /// count as not done, every mechanism is back at its start, the entry day is forgotten. Its encounters' creatures (a boss, a wave)
        /// are removed with their records; the spawners' creatures and items dropped inside stay. Since nobody may be inside, nobody stands in a door it takes away. False when it can't now.</summary>
        public static bool Reset(Site site, string why)
        {
            if (!site.IsOwner || AnyoneInside(site)) return false;
            int removed = RemoveParts(site);
            ZDO zdo = site.Zdo;
            // a new generation of parts: the old ones out of this game's range go too (the server goes through every saved object)
            int gen = zdo.GetInt(Keys.Gen) + 1;
            zdo.Set(Keys.Gen, gen);
            Sweep.Everywhere(site.Id, gen, true);   // (its encounters' creatures too: their records are cleared, so a boss or a wave left over would join the next fight)
            zdo.Set(Keys.Done, "");
            zdo.Set(Keys.Mechs, "");
            zdo.Set(Keys.Encounters, "");
            zdo.Set(MechSet.BossKeyHash, "");   // (the boss fights' records: BossFight.cs)
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
            site.Mechs?.GiveBack();   // (triggers: what's on its pedestals dropped outside, never lost)
            int removed = RemoveParts(site);
            Sweep.Everywhere(site.Id, -1, true);   // (everything else of it, loaded or not: its parts and its encounters' creatures)
            Strays.Around(site.EntrancePos, UnknownReach, 5f);   // (once its entrance is gone: what's left up there, untagged, moved down or removed)
            LetGoLater(site.Id, 30f);   // (what this game held still for it: let go once Strays have moved it down)
            Plugin.Log.LogInfo($"Dungeon {site.Id} ({site.PackName} seed {site.Seed}) removed: its entrance and {removed} of its parts here (the rest wherever they are)");
            if (site.View != null) ZNetScene.instance.Destroy(site.View.gameObject);
            else if (site.Zdo != null) ZDOMan.instance.DestroyZDO(site.Zdo);
            return true;
        }

        /// <summary>The saved objects the builder made for this dungeon (marked with its Id), loaded here: removed. A chest holding
        /// something is left to the server's sweep, which empties it out to the entrance (Rebuild.EmptyOut): only one game ever empties a
        /// chest (never twice), and never where it stood (a reset or remake changes the floor under it). On the server it's done here.
        /// One in use is left alone. ChestsLeft: how many were left.</summary>
        private static int ChestsLeft;
        private static float _nextChestSweep;

        private static int RemoveParts(Site site, Func<ZDO, bool> which = null)
        {
            ChestsLeft = 0;
            if (ZNetScene.instance == null || string.IsNullOrEmpty(site.Id)) return 0;
            var mine = Instances(ZNetScene.instance).Where(kv => kv.Value != null && kv.Key.GetString(Keys.PartOf) == site.Id && (which == null || which(kv.Key)))
                                                    .Select(kv => kv.Value).ToList();
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
                    if (chest.GetInventory().NrOfItems() > 0)
                    {
                        if (!ZNet.instance.IsServer()) { ChestsLeft++; continue; }
                        view.ClaimOwnership();
                        if (!Rebuild.EmptyOut(site.Id, zdo, chest.GetInventory())) continue;
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
            if (PackSync.OffReason != null) { p.Message(MessageHud.MessageType.Center, T("dungeons_off")); return; }
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
            Builder.Reveal(site.Built, at);   // (culling: what's seen from the way in is drawn before the player lands: BuildCull.cs)
            if (!p.TeleportTo(at + Vector3.up * 0.1f, facing, false)) return;
            try { site.Act("entered"); } catch (Exception) { }
            if (MessageHud.instance != null) MessageHud.instance.ShowBiomeFoundMsg(site.Title, false);
        }

        /// <summary>Out, in front of the entrance, facing away from it.</summary>
        public static void Leave(Site site)
        {
            Player p = Player.m_localPlayer;
            if (p == null) return;
            Checkpoints.Left(site);   // (triggers: leaving by the way out clears the checkpoint in it)
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
