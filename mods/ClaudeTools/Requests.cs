using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClaudeTools
{
    /// <summary>
    /// The request mailbox and its commands. Other mods add commands and named places through the public static methods below, called by
    /// reflection (so they need no reference to this mod and work without it installed):
    ///
    ///   RegisterCommand(owner, name, usage, Func&lt;string[] args, Action&lt;JObject&gt; output, Action&lt;string&gt; error, IEnumerator&gt;)
    ///   RegisterFrame(owner, Func&lt;string name, float[] {x, y, z, yaw}&gt;)    a named place that view/orbit/top/survey can point at
    ///   UnregisterAll(owner)
    ///
    /// args[0] is the command's name; a command may return null (done at once) or an IEnumerator to take its time (yield as in a coroutine).
    /// Both mods may hot-reload: a mod registers again when it sees a new Claude Tools, and its own registrations replace its old ones.
    /// </summary>
    public partial class Plugin
    {
        public const int ApiVersion = 1;

        internal class Command
        {
            public string Owner, Name, Usage;
            public Func<string[], Action<JObject>, Action<string>, IEnumerator> Run;
        }

        private static readonly Dictionary<string, Command> Commands = new Dictionary<string, Command>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Func<string, float[]>> Frames = new Dictionary<string, Func<string, float[]>>();

        public static void RegisterCommand(string owner, string name, string usage, Func<string[], Action<JObject>, Action<string>, IEnumerator> run)
        {
            if (string.IsNullOrEmpty(name) || run == null) return;
            if (Commands.TryGetValue(name, out Command old) && old.Owner == Name && owner != Name)
            {
                Log?.LogWarning($"{owner} tried to replace the built-in command '{name}'; keeping the built-in one");
                return;
            }
            Commands[name] = new Command { Owner = owner, Name = name.ToLowerInvariant(), Usage = usage ?? name, Run = run };
        }

        public static void RegisterFrame(string owner, Func<string, float[]> resolve)
        {
            if (resolve != null) Frames[owner] = resolve;
        }

        public static void UnregisterAll(string owner)
        {
            foreach (string key in Commands.Where(kv => kv.Value.Owner == owner).Select(kv => kv.Key).ToList()) Commands.Remove(key);
            Frames.Remove(owner);
        }

        private static void Builtin(string name, string usage, Func<string[], Action<JObject>, Action<string>, IEnumerator> run) => RegisterCommand(Name, name, usage, run);

        /// <summary>
        /// A place to measure camera positions from: "world" (world coordinates), "here" (you, facing where you face), "look" (the spot you
        /// look at, facing where you face), or a name another mod knows (a placed blueprint, for BuildOrders; "last" for the latest one).
        /// </summary>
        internal static bool Frame(string token, out Vector3 origin, out float yaw, out string error)
        {
            origin = Vector3.zero; yaw = 0f; error = null;
            Player p = Player.m_localPlayer;
            token = string.IsNullOrEmpty(token) ? "look" : token;
            if (token == "world") return true;
            if (token == "here" && p != null) { origin = p.transform.position; yaw = p.transform.eulerAngles.y; return true; }
            if (token == "look" && p != null) { origin = LookPoint(out Vector3 hit) ? hit : p.transform.position; yaw = p.transform.eulerAngles.y; return true; }
            foreach (var kv in Frames.ToList())
            {
                try
                {
                    float[] f = kv.Value(token);
                    if (f != null && f.Length >= 4) { origin = new Vector3(f[0], f[1], f[2]); yaw = f[3]; return true; }
                }
                catch (Exception) { }
            }
            error = $"no place called '{token}' (use world, here, look{(Frames.Count > 0 ? ", or a name " + string.Join("/", Frames.Keys.ToArray()) + " knows" : "")})";
            return false;
        }

        // ---- running a request ----

        private IEnumerator RunRequest(string file)
        {
            _busy = true;
            string taken = Path.ChangeExtension(file, ".taken");
            var result = new JObject { ["request"] = Path.GetFileName(file), ["time"] = DateTime.Now.ToString("s") };
            var outputs = new JArray();
            var errors = new JArray();
            string[] lines;
            try { lines = File.ReadAllLines(file); File.Delete(taken); File.Move(file, taken); }
            catch (Exception e) { _busy = false; Logger.LogWarning("Could not read request " + file + ": " + e.Message); yield break; }

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] a = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (!Commands.TryGetValue(a[0], out Command cmd)) { errors.Add($"unknown command: {a[0]} (\"help\" lists them)"); continue; }
                if (Player.m_localPlayer == null && !NoWorld.Contains(cmd.Name)) { errors.Add($"{cmd.Name}: needs the player in a world ({line})"); continue; }
                if (Cheats.Contains(cmd.Name) && !CheatsAllowed()) { errors.Add(CheatRefusal(cmd.Name)); continue; }

                IEnumerator run = null;
                string name = cmd.Name;
                try { run = cmd.Run(a, o => outputs.Add(o), e => errors.Add(name + ": " + e)); }
                catch (Exception e) { errors.Add(name + ": " + e.Message); }
                // a command that takes its time: step through it here, so an error inside it is reported, not lost
                while (run != null)
                {
                    object step;
                    try
                    {
                        if (!run.MoveNext()) break;
                        step = run.Current;
                    }
                    catch (Exception e) { errors.Add(name + ": " + e.Message); break; }
                    yield return step;
                }
            }

            result["outputs"] = outputs;
            result["errors"] = errors;
            result["ok"] = errors.Count == 0;
            try { File.WriteAllText(Path.ChangeExtension(file, ".done.json"), result.ToString()); }
            catch (Exception e) { Logger.LogWarning("Could not write the request result: " + e.Message); }
            Logger.LogInfo($"Request {Path.GetFileName(file)}: {outputs.Count} result(s), {errors.Count} error(s)");
            _busy = false;
        }

        // ---- reading arguments ----

        internal static float F(string[] a, int i, float fallback) =>
            a.Length > i && float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;

        internal static int I(string[] a, int i, int fallback) => a.Length > i && int.TryParse(a[i], out int v) ? v : fallback;

        internal static string Rest(string[] a, int from) => a.Length > from ? string.Join(" ", a.Skip(from).ToArray()) : "";
    }
}
