using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Dungeons
{
    /// <summary>
    /// Reloading packs without a restart, for whoever makes them: edit the JSON, `dungeon reload`, walk the dungeon again.
    ///   dungeon reload [force]    (single player or the host; the cheat rule) the config folder read again now; per pack and look: added,
    ///                             changed, unchanged, removed, left out; each added or changed pack read (its problems) and laid out
    ///                             over 50 seeds (how many lay out, why the others don't, the solutions' lengths). Connected players get
    ///                             the new set. The dungeons of a changed pack are remade with it (Dungeons.Remake: back to new, built
    ///                             again on every game), except one with someone inside: never a change of floors under anyone. Those
    ///                             are named, with who is where; "force" first sends those players out to its entrance (on the ground in
    ///                             front of it, as its way out does), then remakes it.
    ///   dungeon respawn [same|new] [force] [id]   the nearest dungeon (or that one) remade from scratch now: its state back to new,
    ///                             the same seed or a new one that lays out. The same rule about players inside.
    /// "ReloadPacksOnSave" (off by default; single player and the host): the folder watched, and reloaded a second after the last save,
    /// never forced (PackWatch).
    /// </summary>
    internal static class PackReload
    {
        public const int CheckSeeds = 50;
        private const float OutWait = 15f;

        /// <summary>Dungeons a reload couldn't remake (someone was inside): `dungeon reload force` remakes them, even if no file changed since.</summary>
        private static readonly HashSet<string> Pending = new HashSet<string>();

        public static bool Busy { get; private set; }

        /// <summary>Who may reload: single player or the host (not a dedicated server: it reads its folder when it starts), with the cheat rule.</summary>
        public static string Refusal()
        {
            if (ZNet.instance == null || DungeonDB.instance == null) return "load a world first";
            if (!ZNet.instance.IsServer()) return "only single player or the host reads its packs folder (a client uses the server's packs)";
            if (!DHack.Shared.TestCommands.CheatsAllowed()) return DHack.Shared.TestCommands.Refusal("dungeon reload");
            if (Busy) return "a reload or respawn is still running";
            return null;
        }

        // ---- reload

        public static IEnumerator Reload(bool force, Action<JObject> output, Action<string> error)
        {
            string no = Refusal();
            if (no != null) { error(no); yield break; }
            Busy = true;
            try
            {
                IEnumerator run = DoReload(force, output);
                while (true)
                {
                    bool more;
                    try { more = run.MoveNext(); }
                    catch (Exception e) { error("the reload failed: " + e); yield break; }
                    if (!more) break;
                    yield return run.Current;
                }
            }
            finally { Busy = false; }
        }

        private static IEnumerator DoReload(bool force, Action<JObject> output)
        {
            PackSync.Set after = PackSync.Reload(out PackSync.Set before);
            int sent = SyncNet.Resend();

            var packs = new JArray();
            var changed = new List<string>();
            foreach (PackSync.Entry e in after.Packs.Values.OrderBy(e => e.Name))
            {
                PackSync.Entry old = PackSync.Find(before.Packs, e.Name);
                string what = old == null ? "added" : old.Hash != e.Hash ? "changed" : "unchanged";
                var row = new JObject { ["pack"] = e.Name, ["is"] = what, ["source"] = PackSync.Describe(e), ["hash"] = e.Hash };
                packs.Add(row);
                if (what == "unchanged") continue;
                if (what == "changed") changed.Add(e.Name);
                IEnumerator check = Check(e.Name, row);
                while (check.MoveNext()) yield return check.Current;
            }
            foreach (PackSync.Entry old in before.Packs.Values.Where(o => !after.Packs.ContainsKey(o.Name)).OrderBy(o => o.Name))
            {
                packs.Add(new JObject { ["pack"] = old.Name, ["is"] = "removed", ["note"] = "its dungeons keep what's built until they're remade or the world is loaded again; then they can't be made" });
                changed.Add(old.Name);
            }
            var looks = new JArray();
            foreach (PackSync.Entry e in after.Looks.Values.OrderBy(e => e.Name))
            {
                PackSync.Entry old = PackSync.Find(before.Looks, e.Name);
                looks.Add(new JObject
                {
                    ["look"] = e.Name, ["is"] = old == null ? "added" : old.Hash != e.Hash ? "changed" : "unchanged", ["source"] = PackSync.Describe(e),
                    ["problems"] = e.Problems.Count > 0 ? new JArray(e.Problems) : null,
                    ["note"] = old != null && old.Hash != e.Hash ? "entrances already standing show it once their area loads again" : null,
                });
            }
            foreach (PackSync.Entry old in before.Looks.Values.Where(o => !after.Looks.ContainsKey(o.Name)))
                looks.Add(new JObject { ["look"] = old.Name, ["is"] = "removed" });

            // the dungeons of a changed pack, and those an earlier reload couldn't remake
            var remade = new JArray();
            var notRemade = new JArray();
            // (those whose entrance isn't loaded here: marked, and remade the same way when they next load)
            int marked = 0;
            foreach (string pack in changed) marked += Rebuild.MarkUnloaded(pack);
            var sites = Dungeons.Sites.Where(s => !s.Gone && (changed.Contains((s.PackName ?? "").ToLowerInvariant()) || Pending.Contains(s.Id))).ToList();
            foreach (Site site in sites)
            {
                var result = new JObject();
                IEnumerator run = Remake(site, site.Seed, force, "its pack was reloaded", result);
                while (run.MoveNext()) yield return run.Current;
                ((bool)result["ok"] ? remade : notRemade).Add(result);
            }

            output(new JObject
            {
                ["folder"] = PackSync.ConfigFolder,
                ["set"] = $"{before.Hash} -> {after.Hash}",
                ["packs"] = packs,
                ["looks"] = looks,
                ["leftOut"] = after.LeftOut.Count > 0 ? new JArray(after.LeftOut) : null,
                ["sentTo"] = ZNet.instance != null && ZNet.instance.GetPeers().Count > 0 ? $"{sent} connected players" : null,
                ["remade"] = remade.Count > 0 ? remade : null,
                ["remadeWhenLoaded"] = marked > 0 ? $"{marked} dungeons of changed packs not loaded here" : null,
                ["notRemade"] = notRemade.Count > 0 ? notRemade : null,
                ["next"] = notRemade.Count > 0 ? "dungeon reload force: the players inside go out to its entrance first, then it's remade" : null,
            });
        }

        /// <summary>One pack, read (its problems with this game's rooms) and laid out over CheckSeeds seeds on a worker thread.</summary>
        private static IEnumerator Check(string name, JObject row)
        {
            float until = Time.realtimeSinceStartup + 20f;
            while (!Packs.Ready(name) && Time.realtimeSinceStartup < until) yield return null;   // (its game rooms, loading in the background)
            Pack pack;
            try { pack = Packs.Get(name); }
            catch (Exception e) { row["error"] = "can't be read: " + e.Message; yield break; }
            List<string> problems = Packs.Problems(name);
            if (problems.Count > 0) row["problems"] = new JArray(problems);
            if (pack == null) { row["error"] = "can't be read (see the log)"; yield break; }
            Task<JObject> task = Task.Run(() => Seeds(pack, CheckSeeds));
            while (!task.IsCompleted) yield return null;
            row["seeds"] = task.IsFaulted ? new JObject { ["error"] = task.Exception?.GetBaseException().ToString() } : task.Result;
        }

        /// <summary>Seeds 1..count laid out (Layouts.Make only uses our own classes and Vector3 arithmetic: safe off the main thread).</summary>
        private static JObject Seeds(Pack pack, int count)
        {
            int ok = 0;
            var lengths = new List<int>();
            var rooms = new List<int>();
            var reasons = new Dictionary<string, List<int>>();
            var threw = new List<string>();
            var clock = Stopwatch.StartNew();
            for (int seed = 1; seed <= count; seed++)
            {
                Layout layout;
                try { layout = Layouts.Make(pack, seed); }
                catch (Exception e) { threw.Add($"seed {seed}: {e.GetType().Name}: {e.Message}"); continue; }
                if (layout.Ok)
                {
                    ok++;
                    lengths.Add(layout.Solution.Count);
                    rooms.Add(layout.Rooms.Count);
                    continue;
                }
                string why = layout.Problems.FirstOrDefault() ?? "(no reason given)";
                if (!reasons.TryGetValue(why, out List<int> seeds)) reasons[why] = seeds = new List<int>();
                seeds.Add(seed);
            }
            return new JObject
            {
                ["laidOut"] = $"{ok} of {count}",
                ["failures"] = reasons.Count == 0 ? null : new JArray(reasons.OrderByDescending(kv => kv.Value.Count).Take(5)
                    .Select(kv => new JObject { ["why"] = kv.Key, ["count"] = kv.Value.Count, ["seeds"] = string.Join(",", kv.Value.Take(8)) })),
                ["faults"] = threw.Count == 0 ? null : new JArray(threw.Take(5)),
                ["solution"] = lengths.Count == 0 ? null : new JObject { ["min"] = lengths.Min(), ["avg"] = Math.Round(lengths.Average(), 1), ["max"] = lengths.Max() },
                ["rooms"] = rooms.Count == 0 ? null : new JObject { ["min"] = rooms.Min(), ["avg"] = Math.Round(rooms.Average(), 1), ["max"] = rooms.Max() },
                ["ms"] = clock.ElapsedMilliseconds,
            };
        }

        // ---- respawn

        public static IEnumerator Respawn(Site site, bool newSeed, bool force, Action<JObject> output, Action<string> error)
        {
            string no = Refusal();
            if (no != null) { error(no.Replace("dungeon reload", "dungeon respawn")); yield break; }
            Busy = true;
            try
            {
                int seed = site.Seed;
                if (newSeed)
                {
                    // a seed that lays out, from random ones (on a worker: a big pack takes ~75 ms each)
                    Pack pack = Packs.Get(site.PackName);
                    if (pack == null) { error($"its pack {site.PackName} isn't known now"); yield break; }
                    Task<int> pick = Task.Run(() =>
                    {
                        var random = new System.Random();
                        for (int i = 0; i < 25; i++)
                        {
                            int s = random.Next(1, int.MaxValue);
                            try { if (Layouts.Make(pack, s).Ok) return s; } catch (Exception) { }
                        }
                        return 0;
                    });
                    while (!pick.IsCompleted) yield return null;
                    if (pick.IsFaulted || pick.Result == 0) { error($"25 random seeds of {site.PackName} failed to lay out"); yield break; }
                    seed = pick.Result;
                }
                var result = new JObject();
                IEnumerator run = Remake(site, seed, force, newSeed ? "dungeon respawn new" : "dungeon respawn", result);
                while (run.MoveNext()) yield return run.Current;
                if ((bool)result["ok"]) output(result);
                else error((string)result["why"] + ((bool?)result["force"] == true ? " (dungeon respawn ... force: they go out to its entrance first)" : ""));
            }
            finally { Busy = false; }
        }

        // ---- remaking one dungeon, never under anyone

        /// <summary>Remakes a dungeon (Dungeons.Remake) unless someone is inside: then, with force, sends them out to its entrance first and
        /// waits until they are; without, names them and keeps it pending. result: ok, and what happened.</summary>
        private static IEnumerator Remake(Site site, int seed, bool force, string why, JObject result)
        {
            result["dungeon"] = site.Id;
            result["pack"] = site.PackName;
            result["seed"] = seed;
            result["ok"] = false;
            List<KeyValuePair<string, Vector3>> inside = Dungeons.WhoInside(site);
            if (inside.Count > 0)
            {
                result["inside"] = new JArray(inside.Select(kv => $"{kv.Key} at {kv.Value.x:0} {kv.Value.y:0} {kv.Value.z:0}"));
                if (!force)
                {
                    Pending.Add(site.Id);
                    result["why"] = $"not remade: {string.Join(", ", inside.Select(kv => kv.Key))} {(inside.Count == 1 ? "is" : "are")} inside";
                    result["force"] = true;
                    yield break;
                }
                SendOut(site);
                float until = Time.realtimeSinceStartup + OutWait;
                while (Dungeons.WhoInside(site).Count > 0 && Time.realtimeSinceStartup < until) yield return null;
                inside = Dungeons.WhoInside(site);
                if (inside.Count > 0)
                {
                    Pending.Add(site.Id);
                    result["why"] = $"not remade: {string.Join(", ", inside.Select(kv => kv.Key))} still inside after {OutWait:0} s";
                    yield break;
                }
                result["sentOut"] = result["inside"];
                result.Remove("inside");
            }
            if (!site.IsOwner) site.Claim();
            float claimed = Time.realtimeSinceStartup + 3f;
            while (!site.IsOwner && Time.realtimeSinceStartup < claimed) yield return null;
            if (!site.IsOwner) { result["why"] = "not remade: this game couldn't take its entrance over"; Pending.Add(site.Id); yield break; }
            if (!Rebuild.Remake(site, seed, why, out string refused)) { result["why"] = "not remade: " + refused; Pending.Add(site.Id); yield break; }
            Pending.Remove(site.Id);
            result["ok"] = true;
            result["why"] = why;
        }

        /// <summary>Everyone inside a dungeon goes out to its entrance, on their own game (Rebuild's Dungeons_GoOut: Dungeons.Leave, our
        /// teleport, the ground in front of it).</summary>
        private static void SendOut(Site site) => ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, Rebuild.OutRpc, site.Id);   // (this game too)
    }

    /// <summary>
    /// "ReloadPacksOnSave": the packs folder watched (single player and the host), and reloaded once the files have been quiet for a second.
    /// Unity has no ISynchronizeInvoke for FileSystemWatcher.SynchronizingObject, so the watcher's threads only note the time of the last
    /// change, and a coroutine on the game thread does the reload. Never forced: a dungeon with someone inside stays as it is (the host
    /// is told who). Disposed with the plugin.
    /// </summary>
    internal static class PackWatch
    {
        private static ConfigEntry<bool> _on;
        private static FileSystemWatcher _watcher;
        private static Coroutine _loop;
        private static long _changedAt;   // DateTime ticks (UTC) of the last change; 0: none

        public static void Setup(ConfigFile config)
        {
            _on = config.Bind("2 - Pack making", "ReloadPacksOnSave", false,
                "Reload BepInEx/config/Dungeons/packs a second after a file there is saved (as `dungeon reload`), in single player or as the host. " +
                "Dungeons of a changed pack are remade, unless someone is inside. For people making packs; off for play.");
            _on.SettingChanged += (s, e) => Apply();
            Apply();
        }

        private static void Apply()
        {
            Stop();
            if (_on == null || !_on.Value || Plugin.Instance == null) return;
            try
            {
                Directory.CreateDirectory(PackSync.ConfigFolder);
                _watcher = new FileSystemWatcher(PackSync.ConfigFolder, "*.json")
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                };
                _watcher.Changed += Noted;
                _watcher.Created += Noted;
                _watcher.Deleted += Noted;
                _watcher.Renamed += Noted;
                _watcher.EnableRaisingEvents = true;
                _loop = Plugin.Instance.StartCoroutine(Loop());
                Plugin.Log.LogInfo($"Watching {PackSync.ConfigFolder}: packs reload a second after a save");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Can't watch the packs folder: " + e.Message); Stop(); }
        }

        private static void Noted(object sender, FileSystemEventArgs e) => Interlocked.Exchange(ref _changedAt, DateTime.UtcNow.Ticks);

        private static IEnumerator Loop()
        {
            var wait = new WaitForSecondsRealtime(0.25f);
            while (true)
            {
                yield return wait;
                long at = Interlocked.Read(ref _changedAt);
                if (at == 0 || DateTime.UtcNow.Ticks - at < TimeSpan.TicksPerSecond) continue;   // (still being saved)
                if (PackReload.Refusal() != null) continue;   // (no world, a client, or one running: it waits)
                Interlocked.CompareExchange(ref _changedAt, 0, at);
                JObject answer = null;
                string failed = null;
                IEnumerator run = PackReload.Reload(false, o => answer = o, e => failed = e);
                while (run.MoveNext()) yield return run.Current;
                Tell(answer, failed);
            }
        }

        /// <summary>The host sees what the reload did (top left), and the log has the whole answer.</summary>
        private static void Tell(JObject answer, string failed)
        {
            if (failed != null) { Plugin.Log.LogWarning("Packs reload: " + failed); return; }
            if (answer == null) return;
            Plugin.Log.LogInfo("Packs reloaded: " + answer.ToString(Newtonsoft.Json.Formatting.None));
            int changed = (answer["packs"] as JArray)?.Count(p => (string)p["is"] != "unchanged") ?? 0;
            int remade = (answer["remade"] as JArray)?.Count ?? 0;
            Player p0 = Player.m_localPlayer;
            if (p0 == null) return;
            p0.Message(MessageHud.MessageType.TopLeft, T("dungeons_reloaded", changed, remade));
            if (answer["notRemade"] is JArray kept)
                foreach (JObject k in kept.OfType<JObject>())
                    p0.Message(MessageHud.MessageType.TopLeft, T("dungeons_reload_kept", (string)k["pack"], string.Join(", ", (k["inside"] as JArray ?? new JArray()).Select(x => (string)x))));
        }

        private static void Stop()
        {
            if (_watcher != null)
            {
                try { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); } catch (Exception) { }
                _watcher = null;
            }
            if (_loop != null && Plugin.Instance != null) Plugin.Instance.StopCoroutine(_loop);
            _loop = null;
            Interlocked.Exchange(ref _changedAt, 0);
        }

        public static void Dispose()
        {
            Stop();
            _on = null;
        }
    }
}
