using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using static DHack.Shared.Translations;

namespace Dungeons
{
    /// <summary>
    /// Reloading the mod inside a world (Claude Tools' `reload Dungeons`, the mod manager's auto-reload, ScriptEngine's F6): the old copy
    /// unloads at the end of one frame and the new one wakes a frame later, and nothing may be lost, fall or run the old code meanwhile.
    /// - Prefabs and items: the old copy never unregisters them (Entrance.Unregister, Builder.UnregisterItems stop when Leaving); the new
    ///   one replaces them by name (ZNetScene, ObjectDB), gives the standing entrances its own DungeonEntrance (Reattach) and points the
    ///   items in this game's bags, chests and on the ground at its own prefabs (Repoint). The old prefabs stay alive (inactive): anything
    ///   still holding one keeps working.
    /// - Built dungeons: the old copy hands each built one over as a husk: the same objects, its own components taken off, its sounds
    ///   muted, its floors and walls still there, so nothing standing inside falls. The new copy lays it out and builds it again from the
    ///   entrance's ZDO (its state is all there: mechanisms, encounters, triggers, rooms done), holds the player still meanwhile (Safety),
    ///   and removes the husk once its own copy and its mechanisms are complete (the old copy's Builder.Remove, through a closure: it
    ///   frees what the old build held). A dungeon now laid out otherwise (a pack changed with the build) or that can't be built: a
    ///   player inside is taken out to its entrance first, as a remake does.
    /// - The frame between: a gap guard (Harmony id ...reloadgap, patched by the old copy, removed by the new one, inert after 30 s)
    ///   holds back saved objects above 3000 m, holds the local player still up there, counts no fall, and lets no spawner up there spawn
    ///   (the land-fitting patches are off).
    /// - The network: routed RPC handlers of any older copy are taken out (ZRoutedRpc.Register throws on a name twice), and the old copy
    ///   takes its own out as it goes; the version check's per-connection RPCs are registered again (SyncNet.Take); the entrance's
    ///   Dungeons_Act goes with the old component.
    /// - State only kept in memory is handed over (AppDomain data, plain and game types only, since each copy is its own assembly):
    ///   the packs in use and the version check (SyncNet, PackSync), Safety's hold and teleport grace, a pedestal item in escrow (MechSet),
    ///   bodies held still for a dungeon not built (Dungeons.Frozen), and the husks.
    /// Coroutines stop with the old plugin; the pack watcher is disposed; the statics of the old assembly are left behind with it.
    /// </summary>
    internal static class HotReload
    {
        private const string Slot = "com.quad.dungeons.hotreload";   // AppDomain data: the hand-over, from the old copy to the new
        private static readonly string GapId = Plugin.Guid + ".reloadgap";
        private const string ActRpc = "Dungeons_Act";                  // (DungeonEntrance's own RPC on its ZNetView)

        /// <summary>This copy is unloading inside a world: what the next one takes over stays registered and standing.</summary>
        public static bool Leaving { get; private set; }

        private static float _arrived = -100f;

        /// <summary>Just after a reload: until every husk is gone (and 10 s at least), nothing acts on "this player isn't in a dungeon"
        /// (a key taken away, an escrow given back): the dungeons are being known and built again.</summary>
        public static bool Settling => Husks.Count > 0 || Time.realtimeSinceStartup - _arrived < 10f;

        private sealed class Husk
        {
            public string Id;
            public int Sig;              // its layout's signature (Dungeons.Signature)
            public Bounds Box;
            public GameObject Root;
            public Action Remove;        // the copy that built it takes it down
            public Vector3 Outside;      // in front of its entrance
            public Quaternion Facing;
            public float Since;          // realtime it was handed over first
            public bool TookOut;         // the local player was sent out of it
        }

        private static readonly List<Husk> Husks = new List<Husk>();
        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> Instances = AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");
        private static readonly FieldInfo Functions = AccessTools.Field(typeof(ZRoutedRpc), "m_functions");
        private static Assembly Mine => typeof(HotReload).Assembly;

        /// <summary>A husk is standing over this point (the player is held until it's gone).</summary>
        public static bool Holds(Vector3 p) => p.y >= 3000f && Husks.Any(h => h.Root != null && Inside(h, p));

        private static bool Inside(Husk h, Vector3 p)
        {
            Bounds b = h.Box;
            b.Expand(4f);
            return b.Contains(p);
        }

        // ---- the old copy, going (Plugin.OnDestroy, first)

        /// <summary>The old copy, unloading: in a world, everything the next copy takes over handed to it; true then (a reload in a world).
        /// Each part on its own: whatever fails, the guard is on, every built dungeon stays standing (a husk at worst still running nothing),
        /// and the next copy builds again from the entrances' ZDOs; the failure is logged as an error.</summary>
        public static bool Leave()
        {
            if (ZNetScene.instance == null) return false;
            Leaving = true;
            StartGap();
            var data = new Dictionary<string, object> { ["scene"] = ZNetScene.instance };
            var husks = new List<object[]>();
            foreach (Husk h in Husks) if (h.Root != null) husks.Add(Pack(h));   // (a reload before the last one's were gone: carried on)
            Husks.Clear();
            foreach (Site site in Dungeons.Sites.ToList())
            {
                Built built = site.Built;
                if (!site.Ready || built?.Root == null) continue;   // (still building: removed as before, nobody stands in it)
                var husk = new Husk { Id = site.Id, Root = built.Root, Remove = () => Builder.Remove(built), Since = Time.realtimeSinceStartup };
                try
                {
                    husk.Box = site.Box(out Bounds box) ? box : new Bounds(built.Origin, Vector3.one * 640f);
                    husk.Sig = Dungeons.Signature(site.Layout);
                    husk.Outside = Dungeons.OutsidePoint(site);
                    husk.Facing = site.EntranceRot;
                }
                catch (Exception e) { Failed("reading built dungeon " + site.Id, e); }
                // let go of it, so nothing takes it down (Dungeons.Clear next: Unbuild finds nothing to remove or freeze)
                site.Built = null;
                site.Exit = null;
                site.Mechs = null;
                site.MechsMade = false;
                Builder.All.Remove(built);   // (so the plugin's removal of unfinished builds leaves it)
                husks.Add(Pack(husk));
                try { Strip(built.Root); }
                catch (Exception e) { Failed("stripping the build of dungeon " + site.Id, e); }
            }
            data["husks"] = husks;
            Carry(data, "frozen", Dungeons.CarryFrozen);
            Carry(data, "safety", Safety.Carry);
            Carry(data, "sync", SyncNet.Carry);
            Carry(data, "escrow", MechSet.CarryEscrow);
            AppDomain.CurrentDomain.SetData(Slot, data);
            try { ForgetRpcs(a => a == Mine); }
            catch (Exception e) { Failed("taking out the routed RPCs", e); }
            if (husks.Count > 0) Plugin.Log.LogInfo($"Reloading in a world: {husks.Count} built dungeon(s) handed over to the next copy as they stand");
            return true;
        }

        private static void Carry(Dictionary<string, object> data, string key, Func<object> what)
        {
            try { data[key] = what(); }
            catch (Exception e) { Failed("handing over " + key, e); }
        }

        private static void Failed(string what, Exception e) =>
            Plugin.Log.LogError($"Reload in a world: {what} failed (the dungeons are built again from their entrances' ZDOs; nobody is let fall): {e}");

        private static object[] Pack(Husk h) => new object[] { h.Id, h.Sig, h.Box.center, h.Box.size, h.Root, h.Remove, h.Outside, h.Facing, h.Since };

        private static Husk Unpack(object[] o) => new Husk
        {
            Id = (string)o[0], Sig = (int)o[1], Box = new Bounds((Vector3)o[2], (Vector3)o[3]), Root = (GameObject)o[4], Remove = (Action)o[5],
            Outside = (Vector3)o[6], Facing = (Quaternion)o[7], Since = (float)o[8],
        };

        /// <summary>A husk runs no code of ours and makes no sound: our components off its objects (levers, blocks, ways out, breakables,
        /// shortcuts), its sounds muted. Its colliders, models and lights stay.</summary>
        private static void Strip(GameObject root)
        {
            foreach (MonoBehaviour b in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (b != null && b.GetType().Assembly == Mine) Object.DestroyImmediate(b);
            foreach (AudioSource a in root.GetComponentsInChildren<AudioSource>(true)) if (a != null) a.mute = true;
        }

        // ---- the new copy, arriving (Plugin.Awake)

        /// <summary>The new copy, first thing in Awake: any older copy's routed RPCs out of the way, and what the last copy handed over taken.</summary>
        public static void Arrive()
        {
            try { ForgetRpcs(a => a != null && a != Mine && a.GetName().Name == Mine.GetName().Name); }
            catch (Exception e) { Failed("taking out an older copy's routed RPCs", e); }
            var data = AppDomain.CurrentDomain.GetData(Slot) as Dictionary<string, object>;
            if (data == null) return;
            AppDomain.CurrentDomain.SetData(Slot, null);   // (taken once: the next reload hands over afresh)
            var husks = new List<Husk>();
            foreach (object[] o in (data.TryGetValue("husks", out object h) ? h as List<object[]> : null) ?? new List<object[]>())
            {
                try { husks.Add(Unpack(o)); }
                catch (Exception e) { Failed("taking over a built dungeon", e); }
            }
            if (ZNetScene.instance == null || !ReferenceEquals(data.TryGetValue("scene", out object s) ? s : null, ZNetScene.instance))
            {
                // (not the world it was handed over in: nothing to take over)
                foreach (Husk old in husks) Dispose(old);
                return;
            }
            _arrived = Time.realtimeSinceStartup;
            _askSettings = true;
            Husks.AddRange(husks.Where(x => x.Root != null));
            Take(data, "sync", x => SyncNet.Take(x as Dictionary<string, object>));
            Take(data, "safety", x => Safety.Take(x as object[]));
            Take(data, "frozen", x => Dungeons.TakeFrozen(x as Dictionary<string, List<Rigidbody>>));
            Take(data, "escrow", x => MechSet.TakeEscrow(x as object[]));
            Plugin.Log.LogInfo($"Reloaded in a world: {Husks.Count} built dungeon(s) taken over, to be built again");
        }

        private static void Take(Dictionary<string, object> data, string key, Action<object> take)
        {
            if (!data.TryGetValue(key, out object value)) return;
            try { take(value); }
            catch (Exception e) { Failed("taking over " + key, e); }
        }

        /// <summary>The new copy, its own patches on: the gap guard comes off.</summary>
        public static void EndGap() => new Harmony(GapId).UnpatchSelf();

        /// <summary>The new copy, its prefabs and items registered: the standing entrances get its component, the items in this game's
        /// bags, chests and on the ground its prefabs.</summary>
        public static void Settle()
        {
            if (ZNetScene.instance == null) return;
            int entrances = 0, items = 0;
            try { entrances = Reattach(); }
            catch (Exception e) { Failed("giving the standing entrances this copy's component", e); }
            try { items = ItemReload.Repoint(Builder.RewardItem, MechSet.EscrowItem); }
            catch (Exception e) { Failed("pointing the items here at this copy's prefabs", e); }
            if (entrances + items > 0) Plugin.Log.LogInfo($"Reloaded in a world: {entrances} entrance(s) and {items} item(s) given to this copy");
        }

        /// <summary>Every standing entrance with an older copy's DungeonEntrance gets this copy's (its Awake tracks it here, its Start dresses
        /// it again: EntranceLooks.Dress then takes the old look down).</summary>
        private static int Reattach()
        {
            int changed = 0;
            foreach (ZNetView view in Instances(ZNetScene.instance).Values.ToList())
            {
                ZDO z = view != null ? view.GetZDO() : null;
                if (z == null || !Entrance.IsEntrance(z.GetPrefab())) continue;
                bool stale = false;
                foreach (MonoBehaviour old in view.GetComponents<MonoBehaviour>())
                {
                    if (old == null || old.GetType().Assembly == Mine || old.GetType().FullName != typeof(DungeonEntrance).FullName) continue;
                    Object.DestroyImmediate(old);   // (now: the hover and E would still find it this frame)
                    stale = true;
                }
                if (!stale || view.GetComponent<DungeonEntrance>() != null) continue;
                view.Unregister(ActRpc);   // (the old one's: registering a name twice throws)
                view.gameObject.AddComponent<DungeonEntrance>();
                changed++;
            }
            return changed;
        }

        // ---- the husks, every frame (Plugin.Update)

        private static bool _askSettings;

        public static void Update()
        {
            if (_askSettings) AskSettings();
            for (int i = Husks.Count - 1; i >= 0; i--)
            {
                try { if (Tend(Husks[i])) Husks.RemoveAt(i); }
                catch (Exception e) { Failed($"tending the last copy's build of dungeon {Husks[i].Id}", e); }   // (it stays: so does the hold)
            }
        }

        /// <summary>A client: the server's settings again (ServerSettings asks only when a server connects, which a reload doesn't do).
        /// Once, after this copy's ServerSettings registered (in the plugin's Update, before this).</summary>
        private static void AskSettings()
        {
            _askSettings = false;
            ZNetPeer server = ZNet.instance != null && !ZNet.instance.IsServer() ? ZNet.instance.GetServerPeer() : null;
            if (server == null || ZRoutedRpc.instance == null) return;
            try { ZRoutedRpc.instance.InvokeRoutedRPC(server.m_uid, Plugin.Guid + ".settings.ask"); }   // (ServerSettings' own name for it)
            catch (Exception e) { Failed("asking the server for its settings", e); }
        }

        /// <summary>One husk, every frame: removed once this copy's build of its dungeon stands (true then).</summary>
        private static bool Tend(Husk h)
        {
            if (h.Root == null) return true;
            Player p = Player.m_localPlayer;
            Site site = Dungeons.Sites.FirstOrDefault(s => !s.Gone && s.Id == h.Id);
            float age = Time.realtimeSinceStartup - h.Since;
            string done = null, outWhy = null;
            if (site != null && site.MechsReady)
            {
                if (site.Layout != null && h.Sig != 0 && Dungeons.Signature(site.Layout) != h.Sig) outWhy = "it is laid out otherwise now (its pack changed)";
                else done = "built again";
            }
            else if (site?.Problem != null) outWhy = "it can't be built now: " + site.Problem;
            else if (site == null && age > 20f) outWhy = "its dungeon isn't known here any more";
            else if (age > 300f) outWhy = "it wasn't built again in five minutes";
            if (done == null && outWhy == null) return false;
            // never take the floor from under the player: out to the entrance first, as a remake does
            if (outWhy != null && p != null && !p.IsDead() && Inside(h, p.transform.position))
            {
                if (h.Outside == Vector3.zero && ZoneSystem.instance != null)   // (its entrance couldn't be read: the ground below them)
                    h.Outside = new Vector3(p.transform.position.x, ZoneSystem.instance.GetGroundHeight(p.transform.position) + 0.5f, p.transform.position.z);
                if (!h.TookOut && !p.IsTeleporting())
                {
                    h.TookOut = true;
                    Plugin.Log.LogWarning($"Dungeon {h.Id} after the reload: {outWhy}: taking the player out to its entrance");
                    p.Message(MessageHud.MessageType.Center, T("dungeons_sent_out"));
                    Safety.OurTeleport();
                    if (!p.TeleportTo(h.Outside, h.Facing, false)) h.TookOut = false;
                }
                return false;
            }
            Plugin.Log.LogInfo($"Dungeon {h.Id}: the last copy's build removed ({done ?? outWhy}, {age:0.0} s after the reload)");
            if (done == null) Dungeons.FreezeIn(h.Id, h.Box);   // (what's still loaded in it waits, as when a dungeon is taken down)
            Dispose(h);
            return true;
        }

        private static void Dispose(Husk h)
        {
            try { h.Remove?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogWarning($"Removing the last copy's build of dungeon {h.Id}: {e.Message}"); }
            if (h.Root != null) Object.Destroy(h.Root);
        }

        // ---- routed RPCs

        /// <summary>Takes out the routed RPC handlers whose code is in an assembly this says (by the delegate each one holds).</summary>
        private static void ForgetRpcs(Func<Assembly, bool> whose)
        {
            if (ZRoutedRpc.instance == null || !(Functions?.GetValue(ZRoutedRpc.instance) is IDictionary table)) return;
            int n = 0;
            foreach (object key in table.Keys.Cast<object>().ToList())
                if (whose(AssemblyOf(table[key]))) { table.Remove(key); n++; }
            if (n > 0) Plugin.Log.LogInfo($"{n} routed RPC handler(s) of {(whose(Mine) ? "this copy" : "an older copy")} taken out");
        }

        private static Assembly AssemblyOf(object method)
        {
            if (method == null) return null;
            foreach (FieldInfo f in method.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (typeof(Delegate).IsAssignableFrom(f.FieldType) && f.GetValue(method) is Delegate d) return d.Method.DeclaringType?.Assembly;
            return null;
        }

        // ---- the gap guard: the frame with no copy of the mod (patched by the old copy, by hand: CreateAndPatchAll never sees it)

        private static float _gapUntil;
        private static Vector3? _gapAt;
        private static readonly List<ZDO> GapHeld = new List<ZDO>();
        private static readonly AccessTools.FieldRef<Character, float> MaxAirAltitude = AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");
        private static readonly AccessTools.FieldRef<Character, Rigidbody> Body = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");

        private static bool GapOn => Time.realtimeSinceStartup < _gapUntil;

        private static void StartGap()
        {
            _gapUntil = Time.realtimeSinceStartup + 30f;   // (inert after that, should no new copy come to take it off)
            _gapAt = null;
            var harmony = new Harmony(GapId);
            HarmonyMethod Of(string name) => new HarmonyMethod(AccessTools.Method(typeof(HotReload), name));
            try
            {
                harmony.Patch(AccessTools.Method(typeof(ZNetScene), "CreateObjects"), prefix: Of(nameof(GapHoldBack)), finalizer: Of(nameof(GapLetGo)));
                harmony.Patch(AccessTools.Method(typeof(Character), "UpdateGroundContact"), prefix: Of(nameof(GapNoFall)));
                harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.CustomFixedUpdate)), postfix: Of(nameof(GapHoldPlayer)));
                foreach (MethodInfo spawn in new[] { AccessTools.Method(typeof(CreatureSpawner), "Spawn"), AccessTools.Method(typeof(SpawnArea), "SpawnOne"),
                                                     AccessTools.Method(typeof(TriggerSpawner), "Spawn") })
                    if (spawn != null) harmony.Patch(spawn, prefix: Of(nameof(GapNoSpawn)));
            }
            catch (Exception e) { Plugin.Log.LogWarning("The reload's gap guard: " + e.Message); }
        }

        /// <summary>Saved objects above 3000 m outside the game's own interiors aren't made this frame (no dungeon is known here now).</summary>
        private static void GapHoldBack(List<ZDO> currentNearObjects, List<ZDO> currentDistantObjects)
        {
            if (!GapOn) return;
            foreach (List<ZDO> list in new[] { currentNearObjects, currentDistantObjects })
            {
                if (list == null) continue;
                foreach (ZDO z in list)
                {
                    if (z.Created || z.GetPosition().y < 3000f || Location.GetZoneLocation(z.GetPosition()) != null) continue;
                    z.Created = true;
                    GapHeld.Add(z);
                }
            }
        }

        private static void GapLetGo()
        {
            foreach (ZDO z in GapHeld) z.Created = false;
            GapHeld.Clear();
        }

        private static void GapNoFall(Character __instance)
        {
            if (GapOn && ReferenceEquals(__instance, Player.m_localPlayer)) MaxAirAltitude(__instance) = __instance.transform.position.y;
        }

        private static void GapHoldPlayer(Character __instance)
        {
            if (!GapOn || !ReferenceEquals(__instance, Player.m_localPlayer) || __instance.transform.position.y < 3000f || ((Player)__instance).IsTeleporting()) return;
            if (_gapAt == null) _gapAt = __instance.transform.position;
            __instance.transform.position = _gapAt.Value;
            Rigidbody body = Body(__instance);
            if (body != null) { body.position = _gapAt.Value; body.linearVelocity = Vector3.zero; }
        }

        /// <summary>No spawner above 3000 m spawns while the land-fitting patches are off (it would make the game's own creatures).</summary>
        private static bool GapNoSpawn(MonoBehaviour __instance) => !GapOn || __instance == null || __instance.transform.position.y < 3000f;
    }

    // ---- what each part keeps in memory, carried over (plain and game types only: each copy is its own assembly)

    internal static partial class Safety
    {
        public static object[] Carry() => new object[] { _holding, _sentOut, _ourTeleport, _holdAt, _heldFor, _grace, _falling };

        public static void Take(object[] s)
        {
            if (s == null || s.Length < 7) return;
            _holding = (bool)s[0];
            _sentOut = (bool)s[1];
            _ourTeleport = (bool)s[2];
            _holdAt = (Vector3)s[3];
            _heldFor = (float)s[4];
            _grace = (float)s[5];
            _falling = (float)s[6];
        }
    }

    internal static partial class Dungeons
    {
        /// <summary>Bodies held still for dungeons not built here (FreezeLeftovers): the next copy thaws them when it builds those.</summary>
        public static Dictionary<string, List<Rigidbody>> CarryFrozen()
        {
            var all = Frozen.ToDictionary(kv => kv.Key, kv => kv.Value);
            Frozen.Clear();
            return all;
        }

        /// <summary>FreezeLeftovers for a box: the bodies of what's loaded in it held still for that dungeon (a husk taken down with no
        /// build of this copy's under it).</summary>
        public static void FreezeIn(string id, Bounds box)
        {
            if (ZNetScene.instance == null || string.IsNullOrEmpty(id)) return;
            box.Expand(8f);
            if (!Frozen.TryGetValue(id, out List<Rigidbody> frozen)) Frozen[id] = frozen = new List<Rigidbody>();
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

        public static void TakeFrozen(Dictionary<string, List<Rigidbody>> all)
        {
            if (all == null) return;
            foreach (KeyValuePair<string, List<Rigidbody>> kv in all)
            {
                if (!Frozen.TryGetValue(kv.Key, out List<Rigidbody> mine)) Frozen[kv.Key] = mine = new List<Rigidbody>();
                mine.AddRange(kv.Value.Where(b => b != null));
            }
        }
    }

    internal partial class MechSet
    {
        /// <summary>The item on its way to a pedestal (taken from this game's player, until the owner's word), and a lock asked to open.</summary>
        public static object[] CarryEscrow() =>
            new object[] { _escrow?.Site, _escrow?.Mech, _escrow?.Prefab, _escrow?.Item, _escrow?.Sent ?? 0.0, _escrow?.Until ?? 0f, _unlocking, _unlockAsked };

        public static void TakeEscrow(object[] e)
        {
            if (e == null || e.Length < 8) return;
            if (e[3] is ItemDrop.ItemData item)
                _escrow = new Escrow { Site = (string)e[0], Mech = (string)e[1], Prefab = (string)e[2], Item = item, Sent = (double)e[4], Until = (float)e[5] };
            _unlocking = (string)e[6];
            _unlockAsked = (float)e[7];
        }

        public static ItemDrop.ItemData EscrowItem => _escrow?.Item;
    }

    internal static partial class PackSync
    {
        /// <summary>The set in use: the server's (packed as it's sent), or "the same as ours", waiting, off and why; this game's own hash.</summary>
        public static Dictionary<string, object> Carry() => new Dictionary<string, object>
        {
            ["server"] = _server != null ? Pack(_server) : null, ["same"] = _same, ["waiting"] = _waiting, ["off"] = OffReason, ["local"] = _local?.Hash,
        };

        /// <summary>The set the last copy used, used again (the server's isn't sent again).</summary>
        public static void Take(Dictionary<string, object> d)
        {
            if (d == null) return;
            if (d["server"] is byte[] server)
            {
                try { UseServer(Unpack(server)); }
                catch (Exception e) { TurnOff("the server's packs couldn't be read after a reload: " + e.Message); }
            }
            else if ((bool)d["same"]) UseSame();
            else if ((bool)d["waiting"]) Expect();
            if (d["off"] is string off) TurnOff(off);
        }
    }

    internal static partial class SyncNet
    {
        public static Dictionary<string, object> Carry() => new Dictionary<string, object>
        {
            ["packs"] = PackSync.Carry(), ["serverVersion"] = ServerVersion, ["serverHash"] = ServerHash, ["serverProtocol"] = ServerProtocol, ["mismatch"] = MismatchText,
            ["answered"] = _serverAnswered, ["failed"] = Failed, ["expectedSince"] = _expectedSince,
            ["parts"] = _parts, ["partsHash"] = _partsHash, ["partsIn"] = _partsIn,
            ["peers"] = Peers.Select(p => new object[] { p.Rpc, p.Version, p.Hash, p.Protocol, p.Sent }).ToList(), ["refused"] = Refused.ToList(),
        };

        /// <summary>The version check and the packs as the last copy left them, and this copy's handlers on every open connection (ZRpc
        /// replaces a name registered again). A host whose own set changed with the build sends it to its players again.</summary>
        public static void Take(Dictionary<string, object> d)
        {
            ZNet znet = ZNet.instance;
            if (d == null || znet == null) return;
            var packs = d["packs"] as Dictionary<string, object>;
            PackSync.Take(packs);
            ServerVersion = (string)d["serverVersion"];
            ServerHash = (string)d["serverHash"];
            // (a copy from before 0.2.3 doesn't carry it: a server with a version heard had protocol 1 then)
            ServerProtocol = d.TryGetValue("serverProtocol", out object sp) && sp is int spi ? spi : ServerVersion != null ? 1 : 0;
            MismatchText = (string)d["mismatch"];
            _serverAnswered = (bool)d["answered"];
            Failed = (string)d["failed"];
            _expectedSince = (float)d["expectedSince"];
            _parts = d["parts"] as byte[][];
            _partsHash = (string)d["partsHash"];
            _partsIn = (int)d["partsIn"];
            foreach (object[] p in (d["peers"] as List<object[]>) ?? new List<object[]>())
                if (p[0] is ZRpc rpc && znet.GetPeers().Any(z => z.m_rpc == rpc))
                    Peers.Add(new Peer { Rpc = rpc, Version = (string)p[1], Hash = (string)p[2], Protocol = (int)p[3], Sent = (string)p[4] });
            Refused.AddRange((d["refused"] as List<string>) ?? new List<string>());
            foreach (ZNetPeer peer in znet.GetPeers())
            {
                peer.m_rpc.Register<ZPackage>(VersionRpc, OnVersion);
                if (!znet.IsServer() && peer.m_server) peer.m_rpc.Register<ZPackage>(SetRpc, OnSetPart);
            }
            if (!znet.IsServer() && PackSync.Waiting && ServerVersion != null && MismatchText == null && Plugin.Instance != null)
                Plugin.Instance.StartCoroutine(WatchSet(_connection));
            if (znet.IsServer() && packs?["local"] is string was && was != PackSync.Local.Hash)
                Plugin.Log.LogInfo($"This build's own packs differ from the last copy's ({was} to {PackSync.Local.Hash}): sent to {Resend()} player(s) again");
            AfterReload();   // (this copy's version to whoever understands it again; the host told who needs to update)
        }
    }
}
