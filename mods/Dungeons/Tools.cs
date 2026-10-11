using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    /// <summary>
    /// A "dungeon" command for the Claude Tools mod (its request mailbox), so an AI assistant can build and test dungeons:
    ///   dungeon packs                  the packs: title, rooms, plan, environment
    ///   dungeon spawn &lt;pack&gt; [seed]    an entrance 4 m in front of the player, on the ground, facing them (no seed: a random one that lays out)
    ///   dungeon list                   the dungeons in the world (all of them on the host, the ones this game knows on a client): pack, seed,
    ///                                  where, state
    ///   dungeon enter [id]             in, as the entrance does (from farther than 10 m away, a cheat)
    ///   dungeon leave                  out, as the way out does
    ///   dungeon map [id | &lt;pack&gt; &lt;seed&gt;]   the layout, floor by floor from above (also written to BepInEx/claude/dungeon-map.txt), and its rooms
    ///   dungeon seeds &lt;pack&gt; &lt;count&gt;   lays out seeds 1..count without building them: failures and why, room counts, sizes, time
    ///   dungeon state [id]             everything about one dungeon: its saved state, where it stands, what's built here, who's inside
    ///   dungeon reset [id]             back to new now, as the timed reset does (nobody may be inside): mechanisms back at their start too
    ///   dungeon mechs [id]             every mechanism: key, kind, state (and where it's going), moving or not, where, its wires
    ///   dungeon use &lt;key&gt; [id]         uses one as a player would (a lever's pull, a plate stepped on and off; anything else goes one state
    ///                                  on), from anywhere: a cheat
    ///   dungeon set &lt;key&gt; &lt;state&gt; [id] puts one in a state: a cheat
    ///   dungeon solve [id]             the layout's solution (the uses that reach the end, in order), with each one's state now
    ///   dungeon remove [id]            gone for good: its saved parts, then its entrance (nobody may be inside)
    ///   dungeon bring &lt;player&gt;        that player, wherever they are, to 2 m beside this one (the game's own teleport RPC): a rescue, a cheat
    ///   dungeon rooms [theme]          every room the game has (DungeonDB), written to BepInEx/claude/dungeon-rooms.json; the answer counts
    ///                                  them by theme and doorway type
    /// An id is the entrance's ZDO id as the answers show it ("123456:78", good until the world is loaded again) or our own id ("quaddungeon_id",
    /// or its start); left out, the dungeon the player is in, else the nearest. Commands that change the world (spawn, reset, a far enter)
    /// follow the game's cheat rule (Shared/TestCommands.cs), and so does bring.
    /// </summary>
    internal static class Tools
    {
        private const string Usage = "dungeon packs | spawn <pack> [seed] | list | enter [id] | leave | map [id | <pack> <seed>] | seeds <pack> <count> | state [id] | reset [id] | remove [id] | mechs [id] | use <key> [id] | set <key> <state> [id] | solve [id] | bring <player> | rooms [theme]";
        private const string NoWorld = "the packs need the game's dungeon rooms: load a world first";
        private static BaseUnityPlugin _claudeTools;
        private static float _next;

        public static void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 5f;
            BaseUnityPlugin found = Chainloader.PluginInfos.TryGetValue("com.quad.claudetools", out PluginInfo info) ? info.Instance : null;
            if (found == _claudeTools) return;
            _claudeTools = found;
            if (found == null) return;
            try
            {
                MethodInfo register = found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static);
                register?.Invoke(null, new object[] { Plugin.Name, "dungeon", Usage + ": test Quad's Dungeons (an id: the entrance's ZDO id or our own; left out, the nearest)",
                    (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Command });
                Plugin.Log.LogInfo("Claude Tools found: dungeon command added");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not add the dungeon command to Claude Tools: " + e.Message); }
        }

        public static void Unregister()
        {
            try { _claudeTools?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Plugin.Name }); }
            catch (Exception) { }
            _claudeTools = null;
            _next = 0f;
        }

        private static IEnumerator Command(string[] args, Action<JObject> output, Action<string> error)
        {
            args = args.Skip(1).ToArray();   // (the first word is the command's own name)
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            switch (sub)
            {
                case "rooms": Rooms(args, output, error); yield break;
                case "packs": case "spawn": case "seeds": case "map":
                    if (DungeonDB.instance == null) { error(NoWorld); yield break; }
                    break;
            }
            switch (sub)
            {
                case "packs": PackList(output, error); yield break;
                case "list": List(output, error); yield break;
                case "map": Map(args, output, error); yield break;
                case "state": State(args, output, error); yield break;
                case "reset": Reset(args, output, error); yield break;
                case "remove": Remove(args, output, error); yield break;
                case "bring": Bring(args, output, error); yield break;
                case "mechs": Mechs(args, output, error); yield break;
                case "use": case "set": UseOrSet(sub, args, output, error); yield break;
                case "solve": Solve(args, output, error); yield break;
                case "spawn": { IEnumerator run = Spawn(args, output, error); while (run.MoveNext()) yield return run.Current; yield break; }
                case "seeds": { IEnumerator run = Seeds(args, output, error); while (run.MoveNext()) yield return run.Current; yield break; }
                case "enter":
                case "leave": { IEnumerator run = Move(sub, args, output, error); while (run.MoveNext()) yield return run.Current; yield break; }
            }
            error(Usage);
        }

        // ---- reading ----

        private static void Rooms(string[] args, Action<JObject> output, Action<string> error)
        {
            if (DungeonDB.instance == null) { error("the game's room list isn't loaded (load a world first)"); return; }
            JArray rooms = Catalogue.Rooms(args.Length > 1 ? args[1] : null);
            string file = Path.Combine(Paths.BepInExRootPath, "claude", "dungeon-rooms.json");
            File.WriteAllText(file, rooms.ToString(Formatting.Indented));
            output(new JObject
            {
                ["rooms"] = rooms.Count,
                ["byTheme"] = new JObject(rooms.GroupBy(r => (string)r["theme"]).OrderBy(g => g.Key).Select(g => new JProperty(g.Key, g.Count()))),
                ["doorwayTypes"] = new JObject(rooms.SelectMany(r => r["doorways"]).GroupBy(d => (string)d["type"]).OrderByDescending(g => g.Count())
                    .Select(g => new JProperty(string.IsNullOrEmpty(g.Key) ? "(none)" : g.Key, g.Count()))),
                ["file"] = file,
            });
        }

        private static void PackList(Action<JObject> output, Action<string> error)
        {
            if (DungeonDB.instance == null) { error(NoWorld); return; }
            var list = new JArray();
            try
            {
                foreach (string name in Packs.Names)
                {
                    Pack pack = Packs.Get(name);
                    List<string> problems = Packs.Problems(name);
                    if (pack == null) { list.Add(new JObject { ["name"] = name, ["problems"] = new JArray(problems ?? new List<string> { "could not be read" }) }); continue; }
                    list.Add(new JObject
                    {
                        ["name"] = pack.Name,
                        ["title"] = Localization.instance != null ? Localization.instance.Localize(pack.Title ?? "") : pack.Title,
                        ["environment"] = pack.Environment,
                        ["rooms"] = $"{pack.MinRooms}-{pack.MaxRooms}",
                        ["roomKinds"] = pack.Rooms.Count,
                        ["plan"] = string.Join(", ", pack.Plan.Select(s => s.Tag + (s.OpensWith != null ? $" (opened by {s.OpensWith})" : ""))),
                        ["problems"] = problems != null && problems.Count > 0 ? new JArray(problems) : null,
                    });
                }
            }
            catch (Exception e) { error("the packs could not be read: " + e.Message); return; }
            output(new JObject { ["packs"] = list });
        }

        private static void List(Action<JObject> output, Action<string> error)
        {
            if (ZDOMan.instance == null) { error("load a world first"); return; }
            var zdos = new List<ZDO>();
            foreach (string name in Entrance.PrefabNames)
            {
                int index = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(name, zdos, ref index)) { }
            }
            Vector3 me = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            var list = new JArray();
            foreach (ZDO z in zdos.OrderBy(z => Dungeons.FlatDistance(z.GetPosition(), me)))
            {
                Site site = Dungeons.Sites.FirstOrDefault(s => s.Zdo == z);
                var o = new JObject
                {
                    ["id"] = z.m_uid.ToString(),
                    ["ownId"] = z.GetString(Keys.Id),
                    ["pack"] = z.GetString(Keys.Pack),
                    ["seed"] = z.GetInt(Keys.Seed),
                    ["look"] = EntranceLooks.All.FirstOrDefault(l => l.Prefab.GetStableHashCode() == z.GetPrefab())?.Name,
                    ["at"] = V(z.GetPosition()),
                    ["distance"] = Mathf.Round(Dungeons.FlatDistance(z.GetPosition(), me)),
                    ["enteredDay"] = z.GetInt(Keys.Entered, -1),
                    ["roomsDone"] = (z.GetString(Keys.Done) ?? "").Count(c => c == '1'),
                    ["resets"] = z.GetInt(Keys.Resets),
                    ["here"] = site == null ? "not loaded" : site.Problem != null ? "can't be built: " + site.Problem : site.Ready ? "built" : site.Building ? "building" : "waiting",
                };
                list.Add(o);
            }
            output(new JObject { ["dungeons"] = list, ["note"] = ZNet.instance != null && ZNet.instance.IsServer() ? "every dungeon in the world" : "the dungeons this game knows (on a client: near the player)" });
        }

        private static void Map(string[] args, Action<JObject> output, Action<string> error)
        {
            Layout layout;
            string what;
            if (args.Length >= 3 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
            {
                Pack pack;
                try { pack = Packs.Get(args[1]); }
                catch (Exception e) { error("the packs could not be read: " + e.Message); return; }
                if (pack == null) { error($"no pack \"{args[1]}\" (dungeon packs lists them)"); return; }
                try { layout = Layouts.Make(pack, seed); }
                catch (Exception e) { error("laying it out failed: " + e); return; }
                what = $"{pack.Name} seed {seed}";
            }
            else
            {
                Site site = Find(args, 1, error);
                if (site == null) return;
                layout = site.Layout;
                if (layout == null) { error($"dungeon {site.Zdo.m_uid} isn't laid out: {site.Problem ?? "not yet"}"); return; }
                what = $"{site.PackName} seed {site.Seed} (dungeon {site.Zdo.m_uid})";
            }
            string map;
            JObject describe;
            try { map = Layouts.Map(layout); describe = Layouts.Describe(layout); }
            catch (Exception e) { error("describing it failed: " + e); return; }
            string file = Path.Combine(Paths.BepInExRootPath, "claude", "dungeon-map.txt");
            try { File.WriteAllText(file, what + "\n\n" + map); } catch (Exception) { file = null; }
            output(new JObject { ["dungeon"] = what, ["ok"] = layout.Ok, ["map"] = map, ["layout"] = describe, ["file"] = file });
        }

        private static void State(string[] args, Action<JObject> output, Action<string> error)
        {
            Site site = Find(args, 1, error);
            if (site == null) return;
            ZDO z = site.Zdo;
            int today = EnvMan.instance != null ? EnvMan.instance.GetDay() : -1;
            var o = new JObject
            {
                ["id"] = z.m_uid.ToString(),
                ["ownId"] = site.Id,
                ["pack"] = site.PackName,
                ["title"] = site.Title,
                ["seed"] = site.Seed,
                ["entrance"] = V(site.EntrancePos),
                ["entranceLoaded"] = site.Detached ? "no: kept built while the player is inside" : "yes",
                ["entranceLook"] = site.Look + (site.Layout?.Pack?.EntranceLook != null && EntranceLooks.Get(site.Layout.Pack.EntranceLook).Name != site.Look
                                    ? $" (its pack now asks for {site.Layout.Pack.EntranceLook})" : ""),
                ["standIn"] = Entrance.LookFrom,
                ["owner"] = site.IsOwner ? "this game" : z.GetOwner().ToString(),
                ["problem"] = site.Problem,
                ["rooms"] = site.Layout?.Rooms.Count ?? 0,
                ["here"] = site.Ready ? "built" : site.Building ? "building" : site.Problem != null ? "can't be built" : "waiting",
                ["roomsDone"] = $"{site.RoomsDone}/{site.Layout?.Rooms.Count ?? 0}",
                ["done"] = z.GetString(Keys.Done),
                ["enteredDay"] = z.GetInt(Keys.Entered, -1),
                ["today"] = today,
                ["resetDays"] = Plugin.ResetDays.Value,
                ["resets"] = z.GetInt(Keys.Resets),
                ["lastResetDay"] = z.GetInt(Keys.ResetDay, -1),
                ["inside"] = new JArray(Player.GetAllPlayers().Where(p => p != null && site.Contains(p.transform.position)).Select(p => p.GetPlayerName())),
                ["exit"] = site.Exit != null,
                ["heldBack"] = Safety.HeldBack,
                ["biome"] = BiomeOf(site),
                ["mechanisms"] = site.Mechs == null ? (site.MechsMade ? "none" : "not made yet") : $"{site.Mechs.All.Count}{(site.Mechs.Complete ? "" : " (still being made)")}, {site.Mechs.All.Count(m => site.Mechs.Motion(m, MechClock.Now).Moving(MechClock.Now))} moving",
                ["mechState"] = z.GetString(Keys.Mechs),
                ["solution"] = site.Layout != null && site.Layout.Solution.Count > 0 ? string.Join(" ", site.Layout.Solution) : null,
            };
            if (site.RenewsOnDay(out int day)) o["renewsOnDay"] = day;
            if (site.HasOrigin) { o["origin"] = V(site.Origin); o["heightStep"] = z.GetInt(Keys.Slot); }
            if (site.Box(out Bounds box)) o["box"] = new JObject { ["min"] = V(box.min), ["max"] = V(box.max) };
            if (Dungeons.Doorway(site, 2f, out Vector3 inAt, out _)) o["wayIn"] = V(inAt);
            o["wayOut"] = V(Dungeons.OutsidePoint(site));
            output(o);
        }

        // ---- changing ----

        private static void Reset(string[] args, Action<JObject> output, Action<string> error)
        {
            if (!DHack.Shared.TestCommands.CheatsAllowed()) { error(DHack.Shared.TestCommands.Refusal("dungeon reset")); return; }
            Site site = Find(args, 1, error);
            if (site == null) return;
            if (Dungeons.AnyoneInside(site)) { error("someone is inside: a dungeon never comes back new while anyone is in it"); return; }
            if (!site.IsOwner)
            {
                if (site.View == null) { error("its entrance isn't loaded here (the player is far inside it): go nearer the entrance"); return; }
                site.View.ClaimOwnership();
            }
            if (!Dungeons.Reset(site, "the reset command")) { error("could not reset it"); return; }
            output(new JObject { ["id"] = site.Zdo.m_uid.ToString(), ["reset"] = true, ["resets"] = site.Zdo.GetInt(Keys.Resets) });
        }

        /// <summary>dungeon bring &lt;player&gt;: that player, wherever they are, to 2 m beside this one (a rescue: someone stuck or dead in a
        /// dungeon). Through the game's own RPC_TeleportPlayer, which every game registers (Chat.TeleportPlayer): their game moves them.</summary>
        private static void Bring(string[] args, Action<JObject> output, Action<string> error)
        {
            if (!DHack.Shared.TestCommands.CheatsAllowed()) { error(DHack.Shared.TestCommands.Refusal("dungeon bring")); return; }
            Player me = Player.m_localPlayer;
            if (me == null || ZNet.instance == null || Chat.instance == null) { error("no player in the world"); return; }
            if (args.Length < 2) { error("dungeon bring <player name>"); return; }
            string name = string.Join(" ", args.Skip(1));
            ZNetPeer peer = ZNet.instance.GetPeerByPlayerName(name);
            if (peer == null) { error($"no player called \"{name}\" is connected"); return; }
            if (peer.m_characterID.IsNone()) { error($"{name} has no character right now (dead and respawning?): try again once they're up"); return; }
            Vector3 to = me.transform.position + me.transform.right * 2f + Vector3.up * 0.3f;
            bool far = Vector3.Distance(peer.m_refPos, to) > 60f;
            Chat.instance.TeleportPlayer(peer.m_uid, to, me.transform.rotation, far);
            Plugin.Log.LogInfo($"dungeon bring: {name} to {to} (from about {peer.m_refPos})");
            output(new JObject { ["brought"] = name, ["to"] = new JArray(Math.Round(to.x, 2), Math.Round(to.y, 2), Math.Round(to.z, 2)), ["far"] = far });
        }

        private static void Remove(string[] args, Action<JObject> output, Action<string> error)
        {
            if (!DHack.Shared.TestCommands.CheatsAllowed()) { error(DHack.Shared.TestCommands.Refusal("dungeon remove")); return; }
            Site site = Find(args, 1, error);
            if (site == null) return;
            if (Dungeons.AnyoneInside(site)) { error("someone is inside: take them out first (dungeon leave)"); return; }
            string id = site.Zdo.m_uid.ToString();
            if (!site.IsOwner)
            {
                if (site.View == null) { error("its entrance isn't loaded here (the player is far inside it): go nearer the entrance"); return; }
                site.View.ClaimOwnership();
            }
            if (!Dungeons.Remove(site)) { error("could not remove it"); return; }
            output(new JObject { ["id"] = id, ["removed"] = true });
        }

        private static IEnumerator Spawn(string[] args, Action<JObject> output, Action<string> error)
        {
            if (!DHack.Shared.TestCommands.CheatsAllowed()) { error(DHack.Shared.TestCommands.Refusal("dungeon spawn")); yield break; }
            Player player = Player.m_localPlayer;
            if (player == null || ZoneSystem.instance == null) { error("no player in the world"); yield break; }
            if (Entrance.Prefab == null) { error("the entrance prefabs aren't registered"); yield break; }
            if (args.Length < 2) { error("dungeon spawn <pack> [seed]"); yield break; }
            Pack pack;
            try { pack = Packs.Get(args[1]); }
            catch (Exception e) { error("the packs could not be read: " + e.Message); yield break; }
            if (pack == null) { error($"no pack \"{args[1]}\" (dungeon packs lists them)"); yield break; }

            // a seed that lays out (given, or random ones until one does)
            int seed;
            string problem;
            if (args.Length > 2)
            {
                if (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed)) { error("the seed is a whole number"); yield break; }
                if (Dungeons.LayoutFor(pack.Name, seed, out problem) == null) { error($"{pack.Name} seed {seed} can't be laid out: {problem}"); yield break; }
            }
            else
            {
                var random = new System.Random();
                seed = 0;
                problem = null;
                for (int i = 0; i < 25; i++)
                {
                    seed = random.Next(1, int.MaxValue);
                    if (Dungeons.LayoutFor(pack.Name, seed, out problem) != null) { problem = null; break; }
                }
                if (problem != null) { error($"25 random seeds of {pack.Name} failed to lay out; the last: {problem}"); yield break; }
            }

            Vector3 forward = player.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;
            Vector3 at = player.transform.position + forward * 4f;
            at.y = ZoneSystem.instance.GetGroundHeight(at);
            GameObject go = Object.Instantiate(Entrance.PrefabFor(pack.EntranceLook), at, Quaternion.LookRotation(-forward));
            DungeonEntrance entrance = go.GetComponent<DungeonEntrance>();
            ZDO zdo = entrance?.View?.GetZDO();
            if (zdo == null) { Object.Destroy(go); error("the entrance was made without a ZDO"); yield break; }
            zdo.Set(Keys.Pack, pack.Name);
            zdo.Set(Keys.Seed, seed);

            // wait for it to find its place (and start building)
            float until = Time.time + 3f;
            while (Time.time < until && entrance != null && entrance.Site != null && !entrance.Site.HasOrigin && entrance.Site.Problem == null) yield return null;
            Site site = entrance != null ? entrance.Site : null;
            if (site == null) { error("the entrance went away"); yield break; }
            var o = new JObject
            {
                ["id"] = zdo.m_uid.ToString(),
                ["ownId"] = site.Id,
                ["pack"] = pack.Name,
                ["seed"] = seed,
                ["at"] = V(at),
                ["rooms"] = site.Layout?.Rooms.Count ?? 0,
                ["look"] = EntranceLooks.Get(pack.EntranceLook).Name + (pack.EntranceLook != null && !EntranceLooks.Known(pack.EntranceLook) ? $" (the pack's \"{pack.EntranceLook}\" isn't a look this version knows)" : ""),
                ["problem"] = site.Problem,
            };
            if (site.HasOrigin) { o["origin"] = V(site.Origin); o["heightStep"] = zdo.GetInt(Keys.Slot); }
            output(o);
        }

        private static IEnumerator Move(string sub, string[] args, Action<JObject> output, Action<string> error)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { error("no player in the world"); yield break; }
            Site site;
            if (sub == "leave")
            {
                site = Dungeons.Covering(player.transform.position);
                if (site == null) { error("the player isn't in a dungeon"); yield break; }
                Dungeons.Leave(site);
            }
            else
            {
                site = Find(args, 1, error);
                if (site == null) yield break;
                bool far = Vector3.Distance(player.transform.position, site.EntrancePos) > 10f;
                if (far && !DHack.Shared.TestCommands.CheatsAllowed()) { error(DHack.Shared.TestCommands.Refusal("dungeon enter from afar")); yield break; }
                if (site.Problem != null) { error($"it can't be built: {site.Problem}"); yield break; }
                Dungeons.Enter(site);
            }
            // the game's teleport takes about 2 s, more while the area loads (and an enter waits for the build)
            float until = Time.time + 20f;
            Vector3 from = player.transform.position;
            while (Time.time < until && Player.m_localPlayer == player && (player.IsTeleporting() || site.EnterWhenBuilt || Vector3.Distance(player.transform.position, from) < 1f)) yield return null;
            yield return new WaitForSeconds(0.5f);
            Vector3 now = player.transform.position;
            output(new JObject
            {
                ["id"] = site.Zdo != null ? site.Zdo.m_uid.ToString() : "",
                ["at"] = V(now),
                ["moved"] = Vector3.Distance(now, from) > 1f,
                ["inside"] = site.Contains(now),
                ["onGround"] = player.IsOnGround(),
                ["held"] = Safety.Holding,
                ["health"] = Math.Round(player.GetHealth(), 1),
            });
        }

        private static IEnumerator Seeds(string[] args, Action<JObject> output, Action<string> error)
        {
            if (args.Length < 3 || !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)) { error("dungeon seeds <pack> <count>"); yield break; }
            count = Mathf.Clamp(count, 1, 10000);
            Pack pack;
            try { pack = Packs.Get(args[1]); }
            catch (Exception e) { error("the packs could not be read: " + e.Message); yield break; }
            if (pack == null) { error($"no pack \"{args[1]}\" (dungeon packs lists them)"); yield break; }

            int ok = 0;
            var rooms = new List<int>();
            var reasons = new Dictionary<string, List<int>>();
            Vector3 biggest = Vector3.zero;
            double totalMs = 0, worstMs = 0;
            var clock = new Stopwatch();
            for (int seed = 1; seed <= count; seed++)
            {
                Layout layout;
                clock.Restart();
                try { layout = Layouts.Make(pack, seed); }
                catch (Exception e) { error($"seed {seed} threw (Make must never throw): {e}"); yield break; }
                clock.Stop();
                totalMs += clock.Elapsed.TotalMilliseconds;
                worstMs = Math.Max(worstMs, clock.Elapsed.TotalMilliseconds);
                if (layout.Ok)
                {
                    ok++;
                    rooms.Add(layout.Rooms.Count);
                    biggest = Vector3.Max(biggest, layout.Box.size);
                }
                else
                {
                    string why = layout.Problems.FirstOrDefault() ?? "(no reason given)";
                    if (!reasons.TryGetValue(why, out List<int> seeds)) reasons[why] = seeds = new List<int>();
                    seeds.Add(seed);
                }
                if (seed % 25 == 0) yield return null;   // (the game keeps running)
            }
            output(new JObject
            {
                ["pack"] = pack.Name,
                ["seeds"] = $"1-{count}",
                ["ok"] = ok,
                ["failed"] = count - ok,
                ["failures"] = new JArray(reasons.OrderByDescending(kv => kv.Value.Count).Take(10)
                    .Select(kv => new JObject { ["why"] = kv.Key, ["count"] = kv.Value.Count, ["seeds"] = string.Join(",", kv.Value.Take(8)) })),
                ["rooms"] = rooms.Count == 0 ? null : new JObject { ["min"] = rooms.Min(), ["avg"] = Math.Round(rooms.Average(), 1), ["max"] = rooms.Max() },
                ["biggestBox"] = V(biggest),
                ["msPerLayout"] = Math.Round(totalMs / count, 2),
                ["worstMs"] = Math.Round(worstMs, 1),
            });
        }

        // ---- the moving parts ----

        private static void Mechs(string[] args, Action<JObject> output, Action<string> error)
        {
            Site site = Find(args, 1, error);
            if (site == null) return;
            if (site.Mechs == null) { error(site.Ready ? (site.MechsMade ? "this dungeon has no mechanisms" : "its mechanisms aren't made yet") : "it isn't built here yet"); return; }
            double now = MechClock.Now;
            var list = new JArray();
            foreach (Mech m in site.Mechs.All)
            {
                MechMotion mo = site.Mechs.Motion(m, now);
                bool moving = mo.Moving(now);
                var o = new JObject
                {
                    ["key"] = m.Key,
                    ["kind"] = m.Def.Kind,
                    ["state"] = mo.To,
                    ["states"] = m.States,
                    ["start"] = m.Def.Start,
                    ["moving"] = moving ? $"{mo.From} -> {mo.To}, {Math.Round(mo.Progress(now) * 100)}%" : null,
                    ["at"] = V(m.WorldPos),
                    ["room"] = m.Room + " " + (site.Layout.Rooms[m.Room].Def?.Name ?? ""),
                };
                if (m.Def.Cycle > 0f) o["cycle"] = $"every {m.Def.Cycle:0.#} s, next in {Math.Max(0, mo.Start + m.Def.Cycle - now):0.0} s";
                if (!string.IsNullOrEmpty(m.Def.Doorway)) o["doorway"] = m.Def.Doorway + (mo.To == 0 ? " (shut)" : " (open)");
                if (m.Def.Wires.Count > 0) o["wires"] = new JArray(m.Def.Wires.Select(w => (Layouts.Target(site.Layout, m.Room, w) ?? "nothing (" + w.Target + ")") + " " + (w.Do ?? "next")));
                if (m.IsPlate) o["pressed"] = m.Pressed;
                if (Player.m_localPlayer != null) o["distance"] = Math.Round(Vector3.Distance(Player.m_localPlayer.transform.position, m.WorldPos), 1);
                list.Add(o);
            }
            output(new JObject
            {
                ["id"] = site.Zdo.m_uid.ToString(),
                ["mechanisms"] = list,
                ["complete"] = site.Mechs.Complete,
                ["owner"] = site.IsOwner ? "this game" : site.Zdo.GetOwner().ToString(),
                ["saved"] = site.Zdo.GetString(Keys.Mechs),
            });
        }

        /// <summary>use &lt;key&gt; [id] / set &lt;key&gt; &lt;state&gt; [id]: this game takes the entrance's ownership (as reset does) and
        /// changes it itself.</summary>
        private static void UseOrSet(string sub, string[] args, Action<JObject> output, Action<string> error)
        {
            if (!DHack.Shared.TestCommands.CheatsAllowed()) { error(DHack.Shared.TestCommands.Refusal("dungeon " + sub)); return; }
            bool set = sub == "set";
            if (args.Length < (set ? 3 : 2)) { error(set ? "dungeon set <key> <state> [id]" : "dungeon use <key> [id]"); return; }
            int state = 0;
            if (set && !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out state)) { error("the state is a whole number"); return; }
            Site site = Find(args, set ? 3 : 2, error);
            if (site == null) return;
            if (site.Mechs == null) { error("its mechanisms aren't made here (dungeon mechs)"); return; }
            string key = args[1];
            if (!site.Mechs.ByKey.ContainsKey(key)) { error($"no mechanism {key} (dungeon mechs lists them)"); return; }
            if (!site.IsOwner)
            {
                if (site.View == null) { error("its entrance isn't loaded here (the player is far inside it): go nearer the entrance"); return; }
                site.View.ClaimOwnership();
            }
            string why;
            bool ok = set ? site.Mechs.Set(key, state, out why) : site.Mechs.Use(key, "the use command", out why);
            if (!ok) { error(why); return; }
            var after = new JObject();
            foreach (Mech m in site.Mechs.All)
            {
                MechMotion mo = site.Mechs.Motion(m, MechClock.Now);
                if (mo.Moving(MechClock.Now) || mo.Start > MechClock.Now) after[m.Key] = $"{mo.From} -> {mo.To}";
            }
            output(new JObject { ["id"] = site.Zdo.m_uid.ToString(), ["key"] = key, ["done"] = sub, ["moving"] = after, ["saved"] = site.Zdo.GetString(Keys.Mechs) });
        }

        private static void Solve(string[] args, Action<JObject> output, Action<string> error)
        {
            Site site = Find(args, 1, error);
            if (site == null) return;
            Layout layout = site.Layout;
            if (layout == null) { error($"it isn't laid out: {site.Problem ?? "not yet"}"); return; }
            var steps = new JArray();
            foreach (string key in layout.Solution)
            {
                MechDef def = Layouts.Mech(layout, key, out int room);
                Mech m = null;
                site.Mechs?.ByKey.TryGetValue(key, out m);
                steps.Add(new JObject
                {
                    ["use"] = key,
                    ["kind"] = def?.Kind ?? "(not in the layout)",
                    ["room"] = room >= 0 ? room + " " + layout.Rooms[room].Def?.Name : null,
                    ["stateNow"] = m != null ? site.Mechs.StateOf(m) : (JToken)null,
                    ["at"] = m != null ? V(m.WorldPos) : null,
                });
            }
            output(new JObject
            {
                ["id"] = site.Zdo.m_uid.ToString(),
                ["pack"] = site.PackName,
                ["seed"] = site.Seed,
                ["uses"] = layout.Solution.Count,
                ["solution"] = steps,
                ["note"] = layout.Solution.Count == 0 ? "no uses needed (or the pack has no puzzle)" : "from a new dungeon, every mechanism at its start: use these in order",
            });
        }

        // ---- helpers ----

        /// <summary>The dungeon an id names (the entrance's ZDO id or our own id, or its start), or with none the one the player is in, else
        /// the nearest. Only dungeons whose entrance is loaded here.</summary>
        private static Site Find(string[] args, int at, Action<string> error)
        {
            if (Dungeons.Sites.Count == 0) { error("no dungeon is loaded here (dungeon list shows them all; go near one)"); return null; }
            string id = args.Length > at ? args[at] : null;
            if (!string.IsNullOrEmpty(id))
            {
                Site named = Dungeons.Sites.FirstOrDefault(s => s.Zdo != null && s.Zdo.m_uid.ToString() == id)
                    ?? Dungeons.Sites.FirstOrDefault(s => s.Id.Length > 0 && s.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase));
                if (named == null) error($"no dungeon \"{id}\" is loaded here (dungeon list shows them all)");
                return named;
            }
            Player p = Player.m_localPlayer;
            if (p == null) return Dungeons.Sites[0];
            return Dungeons.Covering(p.transform.position)
                ?? Dungeons.Sites.OrderBy(s => Vector3.Distance(s.EntrancePos, p.transform.position)).FirstOrDefault();
        }

        /// <summary>The entrance's biome and the creature tier the builder fits its enemies to (BuildCreatures.cs).</summary>
        private static string BiomeOf(Site site)
        {
            if (WorldGenerator.instance == null) return null;
            Heightmap.Biome biome = WorldGenerator.instance.GetBiome(site.EntrancePos.x, site.EntrancePos.z);
            try { return $"{biome} (tier {Builder.TierOf(biome)})"; }
            catch (Exception) { return biome.ToString(); }
        }

        private static JArray V(Vector3 v) => new JArray(Math.Round(v.x, 2), Math.Round(v.y, 2), Math.Round(v.z, 2));
    }
}
