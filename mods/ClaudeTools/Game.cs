using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BepInEx;
using Newtonsoft.Json.Linq;
using static ClaudeTools.Compat;
using UnityEngine;

namespace ClaudeTools
{
    /// <summary>
    /// The commands for mod makers that read code: game (look up the game's real types, methods, fields, who calls and changes what),
    /// check (the pre-release check of a mod's DLL) and gameupdate (after a game update: which methods changed or vanished, and which mods
    /// patch them). A snapshot of the game's methods is kept per game version in library/game, so updates can be compared.
    /// </summary>
    public partial class Plugin
    {
        private static GameIndex _game;
        private static Thread _gameBuild;

        private static string GameDir => Path.Combine(LibraryDir, "game");

        /// <summary>The game index, built once on a worker thread (a few seconds), then handed to <paramref name="done"/>.</summary>
        private static string _gameError;

        /// <summary>The game index for a command; on failure <paramref name="error"/> says why (so a command never answers "ok" with nothing).</summary>
        private static IEnumerator WithGame(Action<GameIndex> done, Action<string> error)
        {
            if (_game == null)
            {
                if (_gameBuild == null || !_gameBuild.IsAlive)
                {
                    string managed = Paths.ManagedPath;
                    _gameBuild = new Thread(() =>
                    {
                        try { _game = GameIndex.Build(managed); _gameError = null; }
                        catch (Exception e) { _gameError = e.Message; Log?.LogWarning("Could not read the game's code: " + e.Message); }
                    }) { IsBackground = true, Name = "ClaudeTools game index" };
                    _gameBuild.Start();
                }
                while (_gameBuild.IsAlive) yield return null;
            }
            if (_game != null) done(_game);
            else error("could not read the game's code: " + (_gameError ?? "unknown error"));
        }

        private static string Safe(string s) => new string((s ?? "").Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_').ToArray());

        /// <summary>After each launch: save this game version's snapshot, and if the game changed since the last one, report what it means for mods.</summary>
        private IEnumerator GameSnapshot()
        {
            yield return new WaitForSecondsRealtime(40f);
            string managed = Paths.ManagedPath, version = null, mvid = null, path = null, failed = null;
            JObject snapshot = null, older = null, report = null;
            var worker = new Thread(() =>
            {
                try
                {
                    GameIndex.Identify(managed, out version, out mvid);
                    path = Path.Combine(GameDir, $"methods_{Safe(version)}_{mvid}.json.gz");
                    if (File.Exists(path)) return; // this game version was seen before: nothing new
                    string previous = Directory.Exists(GameDir)
                        ? Directory.GetFiles(GameDir, "methods_*.json.gz").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
                    GameIndex.Save(GameIndex.Build(managed).Snapshot(), path); // (an index of its own, let go when this ends)
                    if (previous == null) return;
                    snapshot = GameIndex.Load(path);
                    older = GameIndex.Load(previous);
                }
                catch (Exception e) { failed = e.Message; }
            }) { IsBackground = true, Name = "ClaudeTools game snapshot" };
            worker.Start();
            while (worker.IsAlive) yield return null;
            if (failed != null) { Logger.LogWarning("Game snapshot: " + failed); yield break; }
            if (snapshot == null || older == null) yield break;
            yield return UpdateReport(older, snapshot, r => report = r);
            if (report == null) yield break;
            string reportPath = Path.Combine(GameDir, $"update_{Safe((string)older["version"])}_to_{Safe(version)}_{mvid}.json");
            try { File.WriteAllText(reportPath, report.ToString()); }
            catch (Exception e) { Logger.LogWarning("Game update report: " + e.Message); }
            Logger.LogInfo($"The game changed since the last launch ({older["version"]} to {version}): {((JArray)report["diff"]["gone"]).Count} methods gone, " +
                           $"{((JArray)report["diff"]["changed"]).Count} changed; {((JArray)report["installed"]).Count} installed mods patch them. " +
                           $"Details: {reportPath}, or claude gameupdate in the console (F5).");
        }

        /// <summary>Let go of the game index and caches (a hot reload keeps the old copy's static fields alive for the whole session).</summary>
        internal static void ForgetCaches()
        {
            _game = null;
            _shipped = null;
            lock (InstalledCache) InstalledCache.Clear();
        }

        /// <summary>Compare two snapshots and list the mods (installed and in the library) that patch what changed.</summary>
        private static IEnumerator UpdateReport(JObject older, JObject newer, Action<JObject> done)
        {
            JObject diff = GameIndex.Diff(older, newer);
            List<Use> installed = null;
            yield return Installed(u => installed = u);
            done(UpdateImpact(diff, installed, LibraryUses()));
        }

        private void RegisterGameCommands()
        {
            Builtin("game", "game find <text> | game <Type> | game <Type.Member>: look up the game's real code: names and signatures, who calls a method " +
                "or changes a field, what a method calls, and which mods patch it",
                (a, output, error) =>
                {
                    if (a.Length < 2) { error("game find <text>, game <Type> or game <Type.Member> (e.g. game Player.ConsumeResources, game find tombstone)"); return null; }
                    return WithGame(gi => output(GameIndex.Lookup(gi, a, LibraryUses(), Paths.ManagedPath)), error);
                });

            Builtin("modcheck", "modcheck <mod name | GUID | path to a .dll>: the pre-release check: mistakes that lose players' things or break other mods, with why and how to fix",
                (a, output, error) =>
                {
                    string what = Rest(a, 1).Trim('"');
                    if (what.Length == 0) { error("say which mod: modcheck MyMod, or modcheck C:/path/to/MyMod.dll"); return null; }
                    return CheckMod(what, output, error);
                });

            Builtin("gameupdate", "gameupdate: what changed in the game since the last version seen here (methods gone, changed), and which mods patch those methods",
                (a, output, error) =>
                {
                    if (!Directory.Exists(GameDir)) { error("no game snapshot yet: one is made about a minute after the game starts"); return null; }
                    var files = Directory.GetFiles(GameDir, "methods_*.json.gz").OrderByDescending(File.GetLastWriteTimeUtc).ToList();
                    if (files.Count < 2)
                    {
                        output(new JObject { ["snapshots"] = new JArray(files.Select(Path.GetFileName)), ["note"] = "Only one game version seen so far: after the next game update this compares the two." });
                        return null;
                    }
                    return UpdateReport(GameIndex.Load(files[1]), GameIndex.Load(files[0]), output);
                });
        }

        /// <summary>The pre-release check on an installed mod (by name or GUID) or any DLL, on a worker thread.</summary>
        private static IEnumerator CheckMod(string what, Action<JObject> output, Action<string> error)
        {
            List<Use> installed = null;
            yield return Installed(u => installed = u);
            string dll = what.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(what) ? what : null;
            string guid = null;
            if (dll == null)
            {
                lock (InstalledCache)
                {
                    foreach (var kv in InstalledCache)
                    {
                        JObject plugin = (kv.Value.Value["plugins"] as JArray)?.Cast<JObject>().FirstOrDefault(p =>
                            string.Equals((string)p["name"], what, StringComparison.OrdinalIgnoreCase) || string.Equals((string)p["guid"], what, StringComparison.OrdinalIgnoreCase));
                        if (plugin != null) { dll = kv.Key; guid = (string)plugin["guid"]; break; }
                    }
                    if (dll == null) // (by the DLL's file name: a plugin's name can differ, "Quad's Cigars" is CigarSmoking.dll)
                        foreach (var kv in InstalledCache)
                            if (string.Equals(Path.GetFileNameWithoutExtension(kv.Key), what, StringComparison.OrdinalIgnoreCase))
                            {
                                dll = kv.Key;
                                guid = (string)(kv.Value.Value["plugins"] as JArray)?.FirstOrDefault()?["guid"];
                                break;
                            }
                }
            }
            if (dll == null) { error($"no installed mod called {what} (use its name or GUID as in \"mods\", its DLL's name, or a path to its .dll)"); yield break; }

            Check.Others help = OthersFor(guid, installed.Concat(LibraryUses()), LibraryScans());
            JObject result = null;
            Exception failed = null;
            string[] search = SearchDirs();
            var worker = new Thread(() => { try { result = Check.Run(dll, search, help); } catch (Exception e) { failed = e; } }) { IsBackground = true };
            worker.Start();
            while (worker.IsAlive) yield return null;
            if (failed != null) { error(failed.Message); yield break; }
            result["note"] = "Candidates, not certainties: read the code at \"where\" before changing it. Rules come from real bugs (lost items, deleted buildings, broken mods).";
            output(result);
        }
    }
}
