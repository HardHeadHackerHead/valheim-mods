using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Dungeons
{
    /// <summary>
    /// What must never happen (docs/dungeons-design.md, "Safety"): nobody falls 5000 m.
    /// - A player who logs out inside a dungeon logs back in at its entrance (the logout point is moved there): safe even if the mod is
    ///   removed meanwhile.
    /// - A player above a dungeon that isn't built yet (a teleport, a lag spike, a login) is held still until it is; if it never will be
    ///   (it can't be made) or takes too long, they're taken out to its entrance. Someone high up with nothing at all under them and no
    ///   dungeon known there is put back on the ground.
    /// - The saved objects inside a dungeon (doors, chests, creatures, a gravestone, dropped items) aren't made in this game until the
    ///   dungeon is built here, so they never fall either. That holds for anything saved above 3000 m that isn't in one of the game's own
    ///   interiors and isn't in a dungeon built here, whether or not its dungeon is known yet: a dungeon reaches farther than one zone, so
    ///   the zone of a gravestone can load before the zone of its dungeon's entrance (that's how two gravestones fell 5000 m in the first
    ///   Shifting Halls test: the player came back to the dungeon from the side, its far end loaded first, and nothing knew yet that a
    ///   dungeon was there).
    /// - The safety net: what this game owns that fell out of a dungeon built here (below its box, still above 3000 m) is put back on the
    ///   floor of the room above it, and a gravestone whose dungeon is built here but which is far below where it was made (even on the
    ///   ground) goes back to that spot's floor. Dying inside is a corpse run inside, as in the game's crypts: a gravestone is never moved
    ///   out of its dungeon on purpose. A player who falls out of one is put back at its way in.
    /// - Our teleports count no fall: while one runs and for a moment after it lands, falls are measured from where the player is.
    /// </summary>
    internal static class Safety
    {
        private const float HoldLimit = 30f;     // seconds held before being taken out to the entrance
        private const float GraceSeconds = 3f;   // after one of our teleports lands

        private static readonly AccessTools.FieldRef<Character, float> MaxAirAltitude = AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");
        private static readonly AccessTools.FieldRef<Character, Rigidbody> Body = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");
        private static readonly List<ZDO> Waiting = new List<ZDO>();

        private static bool _holding, _sentOut, _ourTeleport;
        private static Vector3 _holdAt;
        private static float _heldFor, _grace, _falling;

        public static bool Holding => _holding;
        public static bool NoFall => _holding || _grace > 0f;

        public static void Reset()
        {
            _holding = _sentOut = _ourTeleport = false;
            _heldFor = _grace = _falling = 0f;
            Waiting.Clear();
            TransientPrefabs.Clear();
        }

        /// <summary>Called just before we teleport the player in or out: the fall grace covers this teleport.</summary>
        public static void OurTeleport() => _ourTeleport = true;

        // ---- the player ----

        /// <summary>Every physics step, for the local player.</summary>
        public static void Step(Player p, float dt)
        {
            if (p.IsTeleporting())
            {
                if (_ourTeleport) _grace = GraceSeconds;
                Release();
                return;
            }
            if (_grace > 0f) _grace -= dt;
            else _ourTeleport = false;
            Vector3 pos = p.transform.position;
            if (pos.y < 3000f || p.IsDead() || p.IsDebugFlying() || ZNetScene.instance == null) { Release(); _falling = 0f; return; }

            Site site = Dungeons.Covering(pos);
            bool asking = false;
            if (site == null && Location.GetZoneLocation(pos) == null)
            {
                // nothing known here: look the dungeon up (the entrances this game knows, or the server's) before letting go
                asking = Dungeons.Discover(pos);
                site = Dungeons.Covering(pos);
            }
            bool hold = site != null ? !site.Ready : asking || !ZNetScene.instance.IsAreaReady(pos) && Location.GetZoneLocation(pos) == null;
            if (!hold)
            {
                Release();
                if (site == null) CatchFall(p, pos, dt);
                else if (site.Box(out Bounds box) && pos.y < box.min.y - 2f && Dungeons.Doorway(site, 2f, out Vector3 wayIn, out Quaternion facing))
                {
                    Plugin.Log.LogWarning($"Dungeon {site.Id}: the player fell out of it (at {pos.y:0} m, its floor {box.min.y:0} m): back at its way in");
                    p.Message(MessageHud.MessageType.Center, T("dungeons_rescued"));
                    OurTeleport();
                    p.TeleportTo(wayIn + Vector3.up * 0.1f, facing, false);
                }
                return;
            }
            if (!_holding)
            {
                _holding = true;
                _sentOut = false;
                _holdAt = pos;
                _heldFor = 0f;
                p.Message(MessageHud.MessageType.Center, T("dungeons_holding"));
                Plugin.Log.LogInfo($"Holding the player at {pos} until the dungeon there is built ({(site != null ? site.Id + ", " + (site.Problem ?? (site.Building ? "building" : "waiting")) : "nothing known there yet")})");
            }
            _heldFor += dt;
            p.transform.position = _holdAt;
            Rigidbody body = Body(p);
            if (body != null) { body.position = _holdAt; body.linearVelocity = Vector3.zero; }
            MaxAirAltitude(p) = _holdAt.y;
            // it can't be built, or it's taking too long: out to its entrance
            if (site != null && !_sentOut && (site.Problem != null || _heldFor > HoldLimit))
            {
                _sentOut = true;
                Plugin.Log.LogWarning($"Dungeon {site.Id}: taking the player out ({site.Problem ?? "held " + _heldFor.ToString("0") + " s"})");
                p.Message(MessageHud.MessageType.Center, T("dungeons_sent_out"));
                OurTeleport();
                p.TeleportTo(Dungeons.OutsidePoint(site), site.EntranceRot, false);
            }
        }

        private static void Release()
        {
            if (!_holding) return;
            _holding = false;
            _grace = GraceSeconds;   // (the drop to the floor from where they were held isn't theirs)
            Plugin.Log.LogInfo($"Let the player go after {_heldFor:0.0} s");
        }

        /// <summary>High up, no dungeon of ours known here, none of the game's in this zone, falling, and nothing at all below (the game's own
        /// floor test, 1000 m down): back on the ground.</summary>
        private static void CatchFall(Player p, Vector3 pos, float dt)
        {
            Rigidbody body = Body(p);
            if (p.IsOnGround() || body == null || body.linearVelocity.y > -10f || Location.GetZoneLocation(pos) != null) { _falling = 0f; return; }
            _falling += dt;
            if (_falling < 1f || ZoneSystem.instance == null || ZoneSystem.instance.FindFloor(pos, out _)) return;
            _falling = 0f;
            Vector3 ground = new Vector3(pos.x, ZoneSystem.instance.GetGroundHeight(pos) + 0.5f, pos.z);
            Plugin.Log.LogWarning($"The player was falling from {pos.y:0} m with nothing below: put back on the ground");
            p.Message(MessageHud.MessageType.Center, T("dungeons_rescued"));
            OurTeleport();
            p.TeleportTo(ground, p.transform.rotation, false);
        }

        // ---- saved objects inside ----

        /// <summary>Before the game makes the objects that came into range: the ones in a dungeon's space that isn't built here yet are
        /// marked as made for this one pass, so it skips them (and tries again next frame). LetGo unmarks them right after.</summary>
        public static void HoldBack(List<ZDO> near, List<ZDO> distant)
        {
            HoldBack(near);
            HoldBack(distant);
        }

        private static void HoldBack(List<ZDO> zdos)
        {
            if (zdos == null) return;
            foreach (ZDO z in zdos)
            {
                if (z.Created) continue;
                Vector3 p = z.GetPosition();
                if (p.y < 3000f || Transient(z.GetPrefab()) || !MustWait(p, out bool known)) continue;
                z.Created = true;
                Waiting.Add(z);
                if (!known) Strays.Held(p);   // (no dungeon known over it: the server looks, and moves it down if none is)
            }
        }

        /// <summary>How many saved objects the last pass held back (for dungeon state).</summary>
        public static int HeldBack { get; private set; }

        public static void LetGo()
        {
            if (Waiting.Count > 0 || HeldBack > 0) HeldBack = Waiting.Count;
            foreach (ZDO z in Waiting) z.Created = false;
            Waiting.Clear();
        }

        /// <summary>A saved object above 3000 m waits unless a dungeon built here covers it, or it's in one of the game's own interiors
        /// (a location in its zone), or the dungeon covering it can never be built (then it may fall to the ground beside its entrance:
        /// better than gone). Its dungeon may not be known here yet at all: its entrance's zone may load after its own.</summary>
        private static bool MustWait(Vector3 p, out bool known)
        {
            Site site = Dungeons.Covering(p);
            known = site != null;
            if (site != null) return !site.Ready && site.Problem == null;
            return Location.GetZoneLocation(p) == null;
        }

        /// <summary>A saved object this game holds back on purpose (above 3000 m, waiting for its dungeon): not one a teleport waits for.</summary>
        public static bool Holds(ZDO z)
        {
            Vector3 p = z.GetPosition();
            return p.y >= 3000f && !Transient(z.GetPrefab()) && MustWait(p, out _);
        }

        // ---- what's only passing ----

        private static readonly Dictionary<int, bool> TransientPrefabs = new Dictionary<int, bool>();

        /// <summary>A prefab that's only passing: a sound or an effect (sfx_, vfx_, fx_, or a ZSFX or TimedDestruction with nothing to keep:
        /// no item, character or gravestone), or a projectile in flight (an arrow becomes an item when it lands). Neither held back nor put
        /// back by the net: it goes by itself.</summary>
        internal static bool Transient(int prefabHash)
        {
            if (TransientPrefabs.TryGetValue(prefabHash, out bool known)) return known;
            if (ZNetScene.instance == null) return false;
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabHash);
            bool passing = false;
            if (prefab != null)
            {
                string name = prefab.name.ToLowerInvariant();
                bool keep = prefab.GetComponent<ItemDrop>() != null || prefab.GetComponent<Character>() != null || prefab.GetComponent<TombStone>() != null;
                passing = prefab.GetComponent<Projectile>() != null
                          || (!keep && (name.StartsWith("sfx_") || name.StartsWith("vfx_") || name.StartsWith("fx_")
                                        || prefab.GetComponentInChildren<ZSFX>(true) != null || prefab.GetComponent<TimedDestruction>() != null));
            }
            TransientPrefabs[prefabHash] = passing;
            return passing;
        }

        // ---- the safety net ----

        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> Instances = AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");
        private static readonly List<KeyValuePair<ZNetView, Vector3>> ToMove = new List<KeyValuePair<ZNetView, Vector3>>();
        private static readonly List<ZNetView> ToRemove = new List<ZNetView>();
        private static int _floorMask = -1;
        private static float _nextNet;

        /// <summary>Once a second: what this game owns that fell out of a dungeon built here goes back onto its floor (see the class).</summary>
        public static void Net()
        {
            if (Time.time < _nextNet || ZNetScene.instance == null) return;
            _nextNet = Time.time + 1f;
            List<Site> built = Dungeons.Sites.Where(s => s.Ready && s.Box(out _)).ToList();
            if (built.Count == 0) return;
            ToMove.Clear();
            foreach (KeyValuePair<ZDO, ZNetView> kv in Instances(ZNetScene.instance))
            {
                ZNetView view = kv.Value;
                if (view == null || !kv.Key.IsOwner() || view.GetComponent<Player>() != null || Transient(kv.Key.GetPrefab())) continue;
                string belongs = Sweep.DungeonOf(kv.Key);   // (one of another dungeon's is never moved into this one: the orphan sweep takes it)
                Vector3 pos = view.transform.position;
                // a gravestone: from wherever it is (even the ground), if it was made in a dungeon built here and has left it (far below)
                if (view.GetComponent<TombStone>() != null && kv.Key.GetVec3(ZDOVars.s_spawnPoint, out Vector3 spawn) && spawn.y > 3000f && pos.y < spawn.y - 30f)
                {
                    Site made = built.FirstOrDefault(s => s.Contains(spawn));
                    if (made != null && !made.Contains(pos)) { ToMove.Add(new KeyValuePair<ZNetView, Vector3>(view, FloorUnder(made, spawn + Vector3.up * 0.5f, true))); continue; }
                }
                if (pos.y < 3000f) continue;
                foreach (Site site in built)
                {
                    if (belongs.Length > 0 && belongs != site.Id) continue;
                    site.Box(out Bounds box);
                    if (pos.y >= box.min.y - 1f || pos.x < box.min.x || pos.x > box.max.x || pos.z < box.min.z || pos.z > box.max.z) continue;
                    // a creature goes back into the room it fell from (the room over it), or, with none over it (it never was in the
                    // dungeon: a spawner left outside its rooms), is taken away if a dungeon could have spawned it: never stacked at the way
                    // in. One no dungeon spawns (a companion, another mod's) or a tame goes out to the entrance instead, never removed.
                    if (view.GetComponent<Character>() != null)
                    {
                        if (RoomFloor(site, pos, out Vector3 floor)) ToMove.Add(new KeyValuePair<ZNetView, Vector3>(view, floor));
                        else if (kv.Key.GetBool(ZDOVars.s_tamed) || !Strays.DungeonCreature(Utils.GetPrefabName(view.gameObject)))
                            ToMove.Add(new KeyValuePair<ZNetView, Vector3>(view, Rebuild.OutsideSpot(site)));
                        else ToRemove.Add(view);
                    }
                    // a dropped item that isn't one of the dungeon's own: out to the ground in front of its entrance, never a guessed floor
                    // in its layout (after a remake that's another room, maybe behind a shut gate)
                    else if (belongs.Length == 0 && view.GetComponent<ItemDrop>() != null) ToMove.Add(new KeyValuePair<ZNetView, Vector3>(view, Rebuild.OutsideSpot(site)));
                    else ToMove.Add(new KeyValuePair<ZNetView, Vector3>(view, FloorUnder(site, pos, false)));
                    break;
                }
            }
            foreach (ZNetView view in ToRemove)
            {
                Plugin.Log.LogWarning($"{view.name.Replace("(Clone)", "")} fell out of a dungeon at {view.transform.position} with no room over it: taken away");
                ZNetScene.instance.Destroy(view.gameObject);
            }
            ToRemove.Clear();
            foreach (KeyValuePair<ZNetView, Vector3> move in ToMove)
            {
                ZNetView view = move.Key;
                Vector3 from = view.transform.position;
                Rigidbody body = view.GetComponent<Rigidbody>();
                view.transform.position = move.Value;
                if (body != null) { body.position = move.Value; body.linearVelocity = Vector3.zero; }
                Plugin.Log.LogWarning($"{view.name.Replace("(Clone)", "")} had fallen out of a dungeon (at {from}): put back at {move.Value}");
            }
        }

        /// <summary>The floor under a point in a dungeon: straight down from it (spawn: a gravestone's own spot), or from just under the
        /// ceiling of the lowest room over it (something that fell out below the dungeon); else the dungeon's way in.</summary>
        private static Vector3 FloorUnder(Site site, Vector3 p, bool spot)
        {
            if (_floorMask == -1) _floorMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
            if (spot && Physics.Raycast(p, Vector3.down, out RaycastHit hit, 60f, _floorMask)) return hit.point + Vector3.up * 0.3f;
            if (!spot && RoomFloor(site, p, out Vector3 found)) return found;
            return Dungeons.Doorway(site, 2f, out Vector3 wayIn, out _) ? wayIn + Vector3.up * 0.3f : p;
        }

        /// <summary>The floor of the lowest room over a point (a ray down from just under its ceiling), if a room is over it and has one.</summary>
        private static bool RoomFloor(Site site, Vector3 p, out Vector3 floorAt)
        {
            if (_floorMask == -1) _floorMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
            foreach (PlacedRoom room in site.Layout.Rooms.Where(r => r.Def != null).OrderBy(r => r.Pos.y))
            {
                Vector3 c = site.Origin + room.Pos;
                Vector3 size = room.Def.Size;
                bool sideways = Mathf.Abs(Mathf.DeltaAngle(room.Yaw, 90f)) < 1f || Mathf.Abs(Mathf.DeltaAngle(room.Yaw, -90f)) < 1f;
                float hx = (sideways ? size.z : size.x) / 2f, hz = (sideways ? size.x : size.z) / 2f;
                if (Mathf.Abs(p.x - c.x) > hx || Mathf.Abs(p.z - c.z) > hz) continue;
                var from = new Vector3(p.x, c.y + size.y / 2f - 0.3f, p.z);
                if (Physics.Raycast(from, Vector3.down, out RaycastHit floor, size.y + 1f, _floorMask)) { floorAt = floor.point + Vector3.up * 0.3f; return true; }
            }
            floorAt = p;
            return false;
        }

        // ---- the logout point ----

        public static void FixLogoutPoint(PlayerProfile profile)
        {
            Player p = Player.m_localPlayer;
            if (p == null || p.IsDead()) return;
            Site site = Dungeons.Covering(p.transform.position);
            if (site == null) return;
            profile.SetLogoutPoint(Dungeons.OutsidePoint(site));
        }
    }

    // A player inside a dungeon logs back in at its entrance.
    [HarmonyPatch(typeof(PlayerProfile), nameof(PlayerProfile.SaveLogoutPoint))]
    internal static class PlayerProfile_SaveLogoutPoint
    {
        private static void Postfix(PlayerProfile __instance) => Safety.FixLogoutPoint(__instance);
    }

    // Held, or just after one of our teleports: no fall counted from up there.
    [HarmonyPatch(typeof(Character), "UpdateGroundContact")]
    internal static class Character_UpdateGroundContact
    {
        private static readonly AccessTools.FieldRef<Character, float> MaxAirAltitude = AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");

        private static void Prefix(Character __instance)
        {
            if (Safety.NoFall && ReferenceEquals(__instance, Player.m_localPlayer)) MaxAirAltitude(__instance) = __instance.transform.position.y;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
    internal static class Character_CustomFixedUpdate
    {
        private static void Postfix(Character __instance, float dt)
        {
            if (ReferenceEquals(__instance, Player.m_localPlayer)) Safety.Step((Player)__instance, dt);
        }
    }

    // The saved objects in a dungeon's space wait until it's built here.
    [HarmonyPatch(typeof(ZNetScene), "CreateObjects")]
    internal static class ZNetScene_CreateObjects
    {
        private static void Prefix(List<ZDO> currentNearObjects, List<ZDO> currentDistantObjects)
        {
            Safety.HoldBack(currentNearObjects, currentDistantObjects);
        }

        private static void Finalizer() => Safety.LetGo();
    }

    // A teleport waits for its area to be ready (Player.UpdateTeleport: every saved object in its zone and the ones round it made): the
    // ones Safety holds back on purpose don't count, or a teleport near a dungeon's leftovers waited for ever (a black screen). Only when
    // every missing one is held: anything else missing still means not ready.
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.IsAreaReady))]
    internal static class ZNetScene_IsAreaReady
    {
        private static readonly List<ZDO> Near = new List<ZDO>();

        private static void Postfix(ZNetScene __instance, Vector3 point, ref bool __result)
        {
            if (__result || ZoneSystem.instance == null || ZDOMan.instance == null) return;
            Vector2s zone = ZoneSystem.GetZone(point);
            if (!ZoneSystem.instance.IsZoneLoaded(zone)) return;
            Near.Clear();
            ZDOMan.instance.FindSectorObjects(zone, new SimulationDistance(1, 0), Near);
            bool held = false;
            foreach (ZDO z in Near)
            {
                int prefab = z.GetPrefab();
                if (prefab == 0 || __instance.GetPrefab(prefab) == null || __instance.FindInstance(z) != null) continue;
                if (!Safety.Holds(z)) return;
                held = true;
            }
            if (held) __result = true;
            Near.Clear();
        }
    }

    // Leaving the world: every dungeon built here goes, and the packs are read again for the next world (its DungeonDB).
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Shutdown))]
    internal static class ZNetScene_Shutdown
    {
        private static void Prefix()
        {
            Dungeons.Clear();
            Safety.Reset();
            MechClock.Reset();
            MechSet.ForgetSounds();
            try { Packs.Clear(); } catch (System.Exception) { }
        }
    }
}
