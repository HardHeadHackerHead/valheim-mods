using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Scripts.Blocks;
using Scripts.Editor;

namespace Scripts.Mp
{
    /// <summary>
    /// The server's side of remote editing (docs/scripting-multiplayer.md, section 7): an admin's editor on another game asks `req`, this
    /// answers `res`. The rules (who may ask what, how often, how big) are in AdminCore.cs and tested there; this class only does the
    /// asked thing on the server through the same code the local editor uses (HostBridge and Files), so a script edited from afar is
    /// checked, compiled, saved, approved and run exactly like one edited here. Every change an admin makes is written to the audit log.
    /// Changing scripts from the server's own game stays the owner's rule (HostBridge.MayChange); this is a separate path with the admin check.
    /// </summary>
    internal static class AdminServer
    {
        private static AdminDispatcher _dispatcher;
        private static bool _hooked;

        /// <summary>Starts listening (once). Safe to call every frame.</summary>
        internal static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            _dispatcher = new AdminDispatcher(new GameOps());
            Wire.ServerGot += OnGot;
        }

        internal static int Handled => _dispatcher?.Handled ?? 0;
        internal static int Refused => _dispatcher?.Refused ?? 0;
        internal static int Dropped => _dispatcher?.Dropped ?? 0;

        private static void OnGot(long peer, string kind, string json, long id)
        {
            if (kind != "req" || !Wire.IsServer || _dispatcher == null) return;
            try
            {
                string answer = _dispatcher.Handle(peer, json, Wire.IsAdmin(peer), UnityEngine.Time.realtimeSinceStartupAsDouble);
                if (answer != null) Wire.SendToPeer(peer, "res", answer, id);
            }
            catch (Exception e) { Core.Log.LogWarning("A request from an admin's editor failed: " + e.Message); }
        }

        /// <summary>The asked things, done on the game that runs the scripts.</summary>
        private sealed class GameOps : IAdminOps
        {
            public JObject Run(string op, JObject a, long peer)
            {
                switch (op)
                {
                    case AdminOps.List: return new JObject { ["rows"] = AdminFrames.ToJson(Rows()) };
                    case AdminOps.Settings: return SettingsFor(peer);
                    case AdminOps.Source: return new JObject { ["text"] = HostBridge.Source(Existing(a)) };
                    case AdminOps.Log: return new JObject { ["lines"] = new JArray(HostBridge.Log(Existing(a), Math.Max(1, Math.Min(AdminFrames.MaxLogLines, a.Value<int?>("count") ?? 100))).Cast<object>().ToArray()) };
                    case AdminOps.LogRows:
                        return new JObject { ["rows"] = AdminFrames.ToJson(AdminFrames.CapRows(HostBridge.LogRows(Existing(a), a.Value<int?>("min") ?? 0, a.Value<bool?>("only") ?? false, a.Value<bool?>("mine") ?? false, Clip(a.Value<string>("contains"), 40), 500))) };
                    case AdminOps.LogText: return new JObject { ["text"] = Cut(HostBridge.LogText(Existing(a), a.Value<int?>("min") ?? 0, a.Value<bool?>("only") ?? false, a.Value<bool?>("mine") ?? false, Clip(a.Value<string>("contains"), 40)), AdminFrames.MaxText) };
                    case AdminOps.LogCounts: return new JObject { ["counts"] = new JArray(HostBridge.LogCounts(Existing(a)).Cast<object>().ToArray()) };
                    case AdminOps.Stats: return new JObject { ["stats"] = HostBridge.Stats(Existing(a)) };
                    case AdminOps.Refused: return new JObject { ["refused"] = new JArray(HostBridge.Refused(Existing(a)).Cast<object>().ToArray()) };
                    case AdminOps.LogPath: return new JObject { ["path"] = HostBridge.LogPath(Existing(a)) };
                    case AdminOps.Status:
                        {
                            string file = Existing(a);
                            return new JObject
                            {
                                ["counts"] = new JArray(HostBridge.LogCounts(file).Cast<object>().ToArray()),
                                ["stats"] = HostBridge.Stats(file),
                                ["refused"] = new JArray(HostBridge.Refused(file).Cast<object>().ToArray()),
                                ["trace"] = HostBridge.Trace(file),
                                ["path"] = HostBridge.LogPath(file),
                            };
                        }
                    case AdminOps.Trace:
                        {
                            string file = Existing(a);
                            if (a["on"] != null) { HostBridge.SetTrace(file, a.Value<bool>("on")); Audited(peer, "turned the trace " + (a.Value<bool>("on") ? "on" : "off") + " for " + file); }
                            return new JObject { ["trace"] = HostBridge.Trace(file) };
                        }
                    case AdminOps.LogClear: HostBridge.LogClear(Existing(a)); return new JObject { ["ok"] = true };
                    case AdminOps.Start:
                        {
                            string file = Existing(a);
                            string why = HostBridge.Start(file) ?? "";
                            Audited(peer, "started " + file + (why.Length > 0 ? " (" + why + ")" : ""));
                            return new JObject { ["why"] = why };
                        }
                    case AdminOps.Stop:
                        {
                            string file = Existing(a);
                            HostBridge.Stop(file);
                            Audited(peer, "stopped " + file);
                            return new JObject { ["ok"] = true };
                        }
                    case AdminOps.Allow:
                        {
                            string file = Existing(a), power = Clip(a.Value<string>("power"), 20);
                            bool on = a.Value<bool?>("on") ?? false;
                            if (Array.IndexOf(Manifest.Capabilities, power) < 0) throw new AdminError("There is no power called '" + power + "'.");
                            HostBridge.Allow(file, power, on);
                            Audited(peer, (on ? "allowed " : "took back ") + power + " for " + file);
                            return new JObject { ["ok"] = true };
                        }
                    case AdminOps.Write:
                        {
                            string text = a.Value<string>("text") ?? "";
                            if (!NameClean.TextFits(text, AdminFrames.MaxText)) throw new AdminError("That script is too long (120 KB at most).");
                            string file = NameClean.ScriptFile(a.Value<string>("file"));
                            if (CollabGame.Server.HasSession(file)) throw new AdminError("Somebody is editing this script live: open it in the editor to join them.");
                            // a save that began from an older version is refused, with who changed it and when (the editor offers to reload)
                            string baseHash = a.Value<string>("base") ?? "";
                            if (baseHash.Length > 0 && Host.Scripts.Exists(s => s.File == file)
                                && Scripts.Collab.Version.Check(Scripts.Collab.Version.Hash(HostBridge.Source(file)), baseHash) == Scripts.Collab.SaveCheck.Stale)
                            {
                                LastWriter.Get(file, out string by, out double secs);
                                return new JObject { ["why"] = LastWriter.StaleMessage(file), ["stale"] = true, ["by"] = by, ["secs"] = Math.Round(secs), ["file"] = file };
                            }
                            string why = HostBridge.Write(file, text) ?? "";
                            if (why.Length == 0) { Audited(peer, "wrote script " + file); LastWriter.Set(file, NameOfPeer(peer)); }
                            return new JObject { ["why"] = why, ["file"] = file };
                        }
                    case AdminOps.WriteBlocks: return WriteBlocks(a, peer);
                    case AdminOps.New:
                        {
                            string name = Clip(a.Value<string>("name"), 40);
                            bool blocks = a.Value<bool?>("blocks") ?? false;
                            string file = blocks ? Files.CreateLocal(name) : HostBridge.New(name);
                            if (file.Length > 0) Audited(peer, "made " + (blocks ? "block " : "") + "script " + file);
                            return new JObject { ["file"] = file };
                        }
                    case AdminOps.Delete:
                        {
                            string file = Existing(a);
                            Files.Delete(file);
                            Audited(peer, "deleted script " + file);
                            return new JObject { ["ok"] = true };
                        }
                    case AdminOps.Blocks:
                        {
                            string file = Existing(a);
                            string path = Files.BlocksPath(file);
                            if (!File.Exists(path)) throw new AdminError("That script has no blocks.");
                            string json = File.ReadAllText(path);
                            if (!NameClean.TextFits(json, AdminFrames.MaxBlocks)) throw new AdminError("That script's blocks are too big to send.");
                            return new JObject { ["json"] = json };
                        }
                    default: throw new AdminError("The server does not know that request.");
                }
            }

            private static JObject WriteBlocks(JObject a, long peer)
            {
                string blocks = a.Value<string>("blocks") ?? "";
                if (!NameClean.TextFits(blocks, AdminFrames.MaxBlocks)) throw new AdminError("Those blocks are too big (400 KB at most).");
                string file = NameClean.ScriptFile(a.Value<string>("file"));
                // a block script that people are editing live changes only through that session (an older editor's whole-file save would overwrite them)
                if (CollabGame.Server.HasSession(file)) throw new AdminError("Somebody is editing this script live: open it in the editor to join them.");
                Program program = Program.FromJson(blocks, out string error);
                if (program == null) throw new AdminError("The blocks could not be read: " + error);
                Compiled compiled = Compiler.Compile(program);   // the server makes the code itself; the editor's copy is only a hint
                string why = Files.SaveLocal(file, program, compiled);
                if (why.Length == 0) { Audited(peer, "wrote script " + file + " (blocks)"); LastWriter.Set(file, NameOfPeer(peer)); }
                string note = "";
                if (why.Length == 0 && !compiled.Ok)
                {
                    Problem first = compiled.Problems.FirstOrDefault(p => p.Fatal);
                    note = "Saved, but it has problems, so the server did not make it into code: " + (first != null ? first.Message : "see the blocks.");
                }
                return new JObject { ["why"] = why, ["note"] = note, ["file"] = file, ["compiled"] = compiled.Ok };
            }

            // ---- helpers ----

            /// <summary>The file an op names, which must be a script the server has.</summary>
            private static string Existing(JObject a)
            {
                string file = a.Value<string>("file");
                if (!NameClean.IsScriptFile(file)) throw new AdminError("That is not a script's name.");
                if (Host.Scripts.Find(s => s.File == file) == null) throw new AdminError("The server has no script called " + file + ".");
                return file;
            }

            private static string Clip(string s, int max) => s == null ? "" : (s.Length > max ? s.Substring(0, max) : s);

            private static string Cut(string s, int maxBytes)
            {
                if (s == null) return "";
                while (Encoding.UTF8.GetByteCount(s) > maxBytes && s.Length > 10) s = s.Substring(s.Length / 4);
                return s;
            }

            private static string[][] Rows()
            {
                return HostBridge.List().Select(r =>
                {
                    bool has = Files.HasBlocks(r[0]);
                    return r.Concat(new[] { has ? "1" : "0", has ? string.Join(", ", CollabGame.Server.NamesOf(r[0])) : "" }).ToArray();   // (blocks or not, and who is editing it live)
                }).ToArray();
            }

            /// <summary>The server's name, its version, whether this player is an admin, and the limits in the server's own words (one list per power).</summary>
            private static JObject SettingsFor(long peer)
            {
                var lines = new JObject();
                string[] baseLines = Limits.Lines(new string[0]);
                lines[""] = new JArray(baseLines.Cast<object>().ToArray());
                foreach (string power in Manifest.Capabilities)
                {
                    string[] mine = Limits.Lines(new[] { power }).Except(baseLines).ToArray();
                    if (mine.Length > 0) lines[power] = new JArray(mine.Cast<object>().ToArray());
                }
                string world = "";
                try { world = ZNet.instance != null ? ZNet.instance.GetWorldName() ?? "" : ""; } catch (Exception) { }
                return new JObject { ["world"] = world, ["version"] = Core.Version, ["admin"] = Wire.IsAdmin(peer), ["lines"] = lines };
            }

            /// <summary>The player's name on the server, for the stale-save record and the audit.</summary>
            private static string NameOfPeer(long peer)
            {
                string who = peer.ToString();
                try { ZNetPeer p = ZNet.instance != null ? ZNet.instance.GetPeer(peer) : null; if (p != null && !string.IsNullOrEmpty(p.m_playerName)) who = p.m_playerName; } catch (Exception) { }
                return who;
            }

            private static void Audited(long peer, string what)
            {
                string who = NameOfPeer(peer);
                string line = DateTime.Now.ToString("HH:mm:ss") + " [admin] " + who + " " + what;
                if (Audit.Lines.Count >= 300) Audit.Lines.RemoveAt(0);
                Audit.Lines.Add(line);
                Core.Log.LogInfo("Audit: " + line);
            }
        }
    }
}
