using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    /// <summary>
    /// Building a dungeon again from its pack as it is now (a pack maker's `dungeon reload` / `respawn`, Sync's commands): never with a
    /// player inside. Refused, naming them, unless forced; forced, every game whose player is inside takes them out to the entrance first
    /// (Dungeons_GoOut), and the rebuild waits until nobody is in. Both RPCs are taken only from the server or the entrance's owner.
    ///
    /// Then, on the entrance's owner:
    /// - what's on its pedestals is handed back outside the entrance (the triggers' GiveBack);
    /// - the loose things inside (gravestones and dropped items, loaded here or not: the server goes through every ZDO) go to the ground
    ///   in front of the entrance, spread out, never into the new layout by position. A gravestone's death spot is moved with it (its
    ///   own PositionCheck would pull it back), and its owner is named in the log. Creatures inside are removed.
    /// - its saved parts are removed everywhere (a new generation, Sweep), and its state (rooms done, mechanisms, encounters, triggers) is
    ///   cleared;
    /// - every game drops its layout and its built copy (Dungeons_Rebuilt), lays it out again from the packs in use and builds it.
    /// </summary>
    internal static class Rebuild
    {
        public const string OutRpc = "Dungeons_GoOut";         // (dungeon id): every game whose player is inside takes them out
        public const string LooseRpc = "Dungeons_Loose";       // (dungeon id, box centre, box size, outside point, outward): to the server

        private static ZRoutedRpc _for;
        private static readonly Dictionary<string, int> Pending = new Dictionary<string, int>();   // the owner: rebuilds waiting for everyone to be out (their seed)

        public static void Register()
        {
            if (ZRoutedRpc.instance == null || ZRoutedRpc.instance == _for) return;
            _for = ZRoutedRpc.instance;
            try
            {
                ZRoutedRpc.instance.Register<string>(OutRpc, (sender, id) => WhenTrusted(sender, id, () => TakeOut(id)));
                ZRoutedRpc.instance.Register<string, Vector3, Vector3, Vector3, Vector3>(LooseRpc, (sender, id, centre, size, outside, outward) =>
                {
                    if (ZNet.instance != null && ZNet.instance.IsServer()) WhenTrusted(sender, id, () => MoveLoose(id, new Bounds(centre, size), outside, outward));
                });
            }
            catch (Exception e) { Plugin.Log.LogWarning("Registering the rebuild RPCs: " + e.Message); }
        }

        /// <summary>
        /// A rebuild RPC is done only when it came from the server or from the dungeon's entrance owner (any client could send it).
        /// The owner may have only just claimed it, and its ownership can reach here after the RPC: waits up to 5 s for it.
        /// </summary>
        private static void WhenTrusted(long sender, string id, Action act)
        {
            if (Trusted(sender, id)) { act(); return; }
            if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(Waiting(sender, id, act));
        }

        private static System.Collections.IEnumerator Waiting(long sender, string id, Action act)
        {
            float until = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSecondsRealtime(0.25f);
                if (Trusted(sender, id)) { act(); yield break; }
            }
            Plugin.Log.LogWarning($"Dungeon {id}: a rebuild request from {sender}, neither the server nor its entrance's owner, ignored");
        }

        private static bool Trusted(long sender, string id)
        {
            if (ZNet.instance == null) return false;
            long server = ZNet.instance.IsServer() ? ZDOMan.GetSessionID() : (ZNet.instance.GetServerPeer()?.m_uid ?? 0L);
            if (sender == server) return true;
            ZDO entrance = Dungeons.Sites.FirstOrDefault(s => !s.Gone && s.Id == id)?.Zdo;
            if (entrance == null && ZNet.instance.IsServer() && ZDOMan.instance != null)
                entrance = Objects(ZDOMan.instance).Values.FirstOrDefault(z => z != null && Entrance.IsEntrance(z.GetPrefab()) && z.GetString(Keys.Id) == id);
            return entrance != null && entrance.GetOwner() == sender;
        }

        /// <summary>The players inside a dungeon, by name (loaded here, or on the server the peers whose position is inside).</summary>
        public static List<string> Inside(Site site)
        {
            var names = new HashSet<string>(Player.GetAllPlayers().Where(p => p != null && site.Contains(p.transform.position)).Select(p => p.GetPlayerName()));
            if (ZNet.instance != null && ZNet.instance.IsServer())
                foreach (ZNetPeer peer in ZNet.instance.GetPeers()) if (peer != null && site.Contains(peer.m_refPos)) names.Add(peer.m_playerName);
            return names.ToList();
        }

        /// <summary>
        /// Builds a dungeon again from its pack as it is now. This game takes the entrance over (a test command's rule) and does it. Someone
        /// inside: refused with their names (why), or with force, they're taken out to the entrance and it's done once nobody is in (true,
        /// and why says it's waiting).
        /// </summary>
        public static bool Start(Site site, bool force, out string why)
        {
            why = null;
            if (site?.Zdo == null) { why = "its entrance isn't known here"; return false; }
            if (Pending.ContainsKey(site.Id)) { why = "already waiting for everyone to be out"; return true; }
            if (!site.IsOwner) site.Claim();
            List<string> inside = Inside(site);
            if (inside.Count > 0)
            {
                if (!force) { why = "someone is inside: " + string.Join(", ", inside) + " (force takes them out to the entrance first)"; return false; }
                ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, OutRpc, site.Id);
                Pending[site.Id] = site.Seed;
                why = $"taking {string.Join(", ", inside)} out to the entrance first; it's built again as soon as nobody is inside";
                return true;
            }
            return Remake(site, site.Seed, "rebuilt", out why);
        }

        /// <summary>
        /// Sync's seam: the owner remakes a dungeon from the packs in use, with this seed (a new one, or its own). False (and why) when
        /// someone is inside or this game doesn't own it. Everything Do describes, then the seed written if new, and Keys.Remake bumped:
        /// every game that built it with another count lays it out again (Watch).
        /// </summary>
        public static bool Remake(Site site, int seed, string reason, out string why)
        {
            why = null;
            if (site?.Zdo == null || !site.IsOwner) { why = "this game doesn't own its entrance (Claim it first)"; return false; }
            List<string> inside = Inside(site);
            if (inside.Count > 0) { why = "someone is inside: " + string.Join(", ", inside); return false; }
            Do(site, reason);
            ZDO zdo = site.Zdo;
            if (seed != zdo.GetInt(Keys.Seed))
            {
                zdo.Set(Keys.Seed, seed);
                zdo.Set(Keys.Origin, Vector3.zero);   // (a new layout may need another place: the owner finds one again)
                zdo.RemoveVec3(Keys.Origin);
            }
            zdo.Set(Keys.Remake, zdo.GetInt(Keys.Remake) + 1);
            zdo.Set(Keys.RemakeWanted, 0);
            return true;
        }

        /// <summary>
        /// Sync's seam for a reload, the host: every dungeon of this pack whose entrance isn't loaded here (no Site) is marked to be remade
        /// when it next loads (Keys.RemakeWanted): its owner then does exactly what a reload does (Start, forced). The host knows every
        /// entrance's ZDO; it takes each over to write the mark. How many were marked.
        /// </summary>
        public static int MarkUnloaded(string packName)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null || string.IsNullOrEmpty(packName)) return 0;
            int marked = 0;
            foreach (ZDO z in Objects(ZDOMan.instance).Values.ToList())
            {
                if (z == null || !Entrance.IsEntrance(z.GetPrefab())) continue;
                if (!string.Equals(z.GetString(Keys.Pack), packName, StringComparison.OrdinalIgnoreCase)) continue;
                if (Dungeons.Sites.Any(s => !s.Gone && s.SavedZdo == z)) continue;   // (loaded here: Start it instead)
                z.SetOwner(ZDOMan.GetSessionID());
                z.Set(Keys.RemakeWanted, 1);
                marked++;
            }
            if (marked > 0) Plugin.Log.LogInfo($"Pack {packName} changed: {marked} of its dungeons not loaded here will be remade when they next load");
            return marked;
        }

        /// <summary>Sync's seam: who is inside, and where (players loaded here; on the host, peers whose position is inside).</summary>
        public static List<KeyValuePair<string, Vector3>> WhoInside(Site site)
        {
            var list = Player.GetAllPlayers().Where(p => p != null && site.Contains(p.transform.position))
                             .Select(p => new KeyValuePair<string, Vector3>(p.GetPlayerName(), p.transform.position)).ToList();
            if (ZNet.instance != null && ZNet.instance.IsServer())
                foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                    if (peer != null && site.Contains(peer.m_refPos) && !list.Any(kv => kv.Key == peer.m_playerName))
                        list.Add(new KeyValuePair<string, Vector3>(peer.m_playerName, peer.m_refPos));
            return list;
        }

        /// <summary>Every game, every frame for each site: its remake count differs from the one it built with: laid out again from the
        /// packs in use (never under this game's player: it waits until they're out).</summary>
        public static void Watch(Site site)
        {
            ZDO z = site.Zdo;
            if (z == null) return;
            int count = z.GetInt(Keys.Remake);
            if (site.RemakeSeen < 0) { site.RemakeSeen = count; return; }
            if (count == site.RemakeSeen) return;
            Player p = Player.m_localPlayer;
            if (p != null && site.Contains(p.transform.position)) return;   // (later)
            site.RemakeSeen = count;
            Dungeons.Forget(site);
        }

        /// <summary>The owner, every tend: a forced rebuild waiting for everyone to be out.</summary>
        public static void Tick(Site site)
        {
            if (Pending.Count == 0 || !site.IsOwner || !Pending.TryGetValue(site.Id, out int seed) || Inside(site).Count > 0) return;
            Pending.Remove(site.Id);
            Remake(site, seed, "rebuilt once everyone was out", out _);
        }

        private static void Do(Site site, string reason)
        {
            ZDO zdo = site.Zdo;
            if (zdo == null) return;
            site.Mechs?.GiveBack();   // (pedestal items outside the entrance)
            // the loose things: on the server, wherever they are
            if (site.Box(out Bounds box))
            {
                box.Expand(8f);
                Vector3 outside = Dungeons.OutsidePoint(site), outward = site.EntranceRot * Vector3.forward;
                if (ZNet.instance.IsServer()) MoveLoose(site.Id, box, outside, outward);
                else ZRoutedRpc.instance?.InvokeRoutedRPC(LooseRpc, site.Id, box.center, box.size, outside, outward);
            }
            // its parts everywhere, and its state
            int gen = zdo.GetInt(Keys.Gen) + 1;
            zdo.Set(Keys.Gen, gen);
            Sweep.Everywhere(site.Id, gen, false);
            zdo.Set(Keys.Done, "");
            zdo.Set(Keys.Mechs, "");
            zdo.Set(Keys.Encounters, "");
            zdo.Set(Keys.LayoutSig, 0);
            site.Mechs?.Forget();
            Plugin.Log.LogInfo($"Dungeon {site.Id} ({site.PackName} seed {site.Seed}): {reason}, from its pack as it is now");
        }

        /// <summary>Every game: this game's player, if inside that dungeon, out to its entrance.</summary>
        private static void TakeOut(string id)
        {
            Site site = Dungeons.Sites.FirstOrDefault(s => !s.Gone && s.Id == id);
            Player p = Player.m_localPlayer;
            if (site == null || p == null || !site.Contains(p.transform.position)) return;
            p.Message(MessageHud.MessageType.Center, DHack.Shared.Translations.T("dungeons_rebuilding"));
            Dungeons.Leave(site);
        }

        // ---- the loose things (the server)

        private static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> Objects = AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");

        /// <summary>
        /// The server: every gravestone and dropped item inside the box, loaded or not, to the ground in front of the entrance, spread out
        /// (a gravestone's death spot with it, its owner named in the log); every creature inside removed. Players and the dungeon's own
        /// tagged parts are left (the sweep takes those).
        /// </summary>
        private static void MoveLoose(string id, Bounds box, Vector3 outside, Vector3 outward)
        {
            if (ZDOMan.instance == null || ZNetScene.instance == null) return;
            outward.y = 0f;
            outward = outward.sqrMagnitude < 0.01f ? Vector3.forward : outward.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, outward);
            int moved = 0, removed = 0;
            foreach (ZDO z in Objects(ZDOMan.instance).Values.ToList())
            {
                if (z == null || !box.Contains(z.GetPosition()) || Sweep.DungeonOf(z) == id) continue;
                GameObject prefab = ZNetScene.instance.GetPrefab(z.GetPrefab());
                if (prefab == null) continue;
                if (prefab.GetComponent<Player>() != null) continue;
                if (prefab.GetComponent<Character>() != null)
                {
                    // a tame that followed someone in goes out with the loose things; the dungeon's own creatures go (they come again)
                    if (z.GetBool(ZDOVars.s_tamed)) { Place(z, Ground(Spot(outside, right, outward, moved), outside.y), false); moved++; }
                    else if (Sweep.Destroy(z)) removed++;
                    continue;
                }
                if (BlockKeys.IsKey(prefab.name)) { if (Sweep.Destroy(z)) removed++; continue; }   // (a remake makes its keys again)
                bool grave = prefab.GetComponent<TombStone>() != null;
                if (!grave && prefab.GetComponent<ItemDrop>() == null) continue;
                Vector3 to = Ground(Spot(outside, right, outward, moved), outside.y);
                Place(z, to, grave);
                if (grave) Plugin.Log.LogWarning($"Dungeon {id} built again: {z.GetString(ZDOVars.s_ownerName)}'s gravestone moved out to its entrance, {to}");
                moved++;
            }
            if (moved > 0 || removed > 0) Plugin.Log.LogInfo($"Dungeon {id} built again: {moved} gravestones, items and tames moved out to its entrance, {removed} creatures and keys removed");
        }

        private static Vector3 Spot(Vector3 outside, Vector3 right, Vector3 outward, int i) =>
            outside + right * ((i % 5) - 2) * 0.9f + outward * (i / 5) * 0.9f;

        /// <summary>The ground at a point (what's built there, or the terrain), never below the entrance's own height.</summary>
        private static Vector3 Ground(Vector3 p, float atLeast)
        {
            float y = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(p) : p.y;
            if (WorldGenerator.instance != null) y = Mathf.Max(y, WorldGenerator.instance.GetHeight(p.x, p.z));
            if (Physics.Raycast(new Vector3(p.x, Mathf.Max(y, atLeast) + 20f, p.z), Vector3.down, out RaycastHit hit, 40f,
                                LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain")))
                y = Mathf.Max(y, hit.point.y);
            return new Vector3(p.x, Mathf.Max(y, atLeast - 2f) + 0.5f, p.z);
        }

        private static void Place(ZDO z, Vector3 to, bool grave)
        {
            z.SetOwner(ZDOMan.GetSessionID());
            z.SetPosition(to);
            if (grave) z.Set(ZDOVars.s_spawnPoint, to);   // (or the gravestone's own check pulls it back to its old spot)
            ZNetView view = ZNetScene.instance.FindInstance(z);
            if (view == null) return;
            view.transform.position = to;
            Rigidbody body = view.GetComponent<Rigidbody>();
            if (body != null) { body.position = to; body.linearVelocity = Vector3.zero; }
        }
    }
}
