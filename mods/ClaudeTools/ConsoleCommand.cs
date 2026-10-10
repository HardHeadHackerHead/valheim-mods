using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using static ClaudeTools.Compat;
using UnityEngine;

namespace ClaudeTools
{
    /// <summary>
    /// "claude &lt;command&gt;" in the game's console (F5): any request command, typed by the player, with the answer printed in the console
    /// and saved in full to BepInEx/claude/console/&lt;command&gt;.json. For debugging by hand: claude clashes, claude who Player.ConsumeResources,
    /// claude library update. Works whether or not AllowRequests is on (the player is typing it); commands that change the world or hand
    /// out items only where the game allows cheats.
    /// </summary>
    public partial class Plugin
    {
        private const string ConsoleName = "claude";
        private const int ConsoleLines = 60;

        // commands that need no world: they run from the main menu too (in the console, and from request files)
        private static readonly HashSet<string> NoWorld = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "help", "library", "patches", "who", "clashes", "systems", "mods", "config", "log", "errors", "modcheck", "game", "gameupdate", "waitfor" };

        // commands that change the world or hand out items: only where the game itself allows cheats (single player, the host, or a server
        // admin who turned devcommands on), from the console and from request files alike
        private static readonly HashSet<string> Cheats = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "give", "grow", "fixlevels", "chestrule" };

        internal static bool CheatsAllowed() =>
            ZNet.instance == null || ZNet.instance.IsServer() || (global::Console.instance != null && global::Console.instance.IsCheatsEnabled());

        internal static string CheatRefusal(string name) =>
            $"{name} changes the world, so it only works in single player, for the host, or with devcommands on (a server admin)";

        private void RegisterConsole()
        {
            new Terminal.ConsoleCommand(ConsoleName,
                "Claude Tools: claude help (all commands), claude clashes [mod], claude who <Type.Method>, claude patches [text], claude library [update]",
                (Terminal.ConsoleEvent)(args => FromConsole(args)));
        }

        private void UnregisterConsole()
        {
            try
            {
                var commands = AccessTools.Field(typeof(Terminal), "commands")?.GetValue(null) as IDictionary<string, Terminal.ConsoleCommand>;
                commands?.Remove(ConsoleName);
            }
            catch (Exception) { }
        }

        private void FromConsole(Terminal.ConsoleEventArgs args)
        {
            Terminal term = args.Context;
            string[] a = args.ArgsAll.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0) a = new[] { "help" };
            if (!Commands.TryGetValue(a[0], out Command cmd)) { term.AddString($"claude: no command '{a[0]}' (claude help lists them)"); return; }
            if (Player.m_localPlayer == null && !NoWorld.Contains(cmd.Name)) { term.AddString($"claude {cmd.Name}: join a world first"); return; }
            if (Cheats.Contains(cmd.Name) && !CheatsAllowed()) { term.AddString("claude " + CheatRefusal(cmd.Name)); return; }

            var outputs = new JArray();
            var errors = new List<string>();
            IEnumerator run = null;
            try { run = cmd.Run(a, o => outputs.Add(o), e => errors.Add(e)); }
            catch (Exception e) { errors.Add(e.Message); }
            if (run == null) Print(term, cmd.Name, outputs, errors);
            else
            {
                term.AddString($"claude {cmd.Name}: working...");
                StartCoroutine(Finish(term, cmd.Name, run, outputs, errors));
            }
        }

        private IEnumerator Finish(Terminal term, string name, IEnumerator run, JArray outputs, List<string> errors)
        {
            while (true)
            {
                object step;
                try
                {
                    if (!run.MoveNext()) break;
                    step = run.Current;
                }
                catch (Exception e) { errors.Add(e.Message); break; }
                yield return step;
            }
            Print(term, name, outputs, errors);
        }

        private static void Print(Terminal term, string name, JArray outputs, List<string> errors)
        {
            var lines = new List<string>();
            foreach (JToken o in outputs) lines.AddRange(Lines(o, ""));
            lines.AddRange(errors.Select(e => "error: " + e));
            string saved = null;
            try
            {
                string dir = Path.Combine(Folder, "console");
                Directory.CreateDirectory(dir);
                saved = Path.Combine(dir, name + ".json");
                File.WriteAllText(saved, new JObject { ["outputs"] = outputs, ["errors"] = new JArray(errors) }.ToString());
            }
            catch (Exception) { saved = null; }
            foreach (string line in lines.Take(ConsoleLines)) term.AddString(line);
            if (lines.Count > ConsoleLines) term.AddString($"... {lines.Count - ConsoleLines} more lines" + (saved != null ? " in " + saved : ""));
            else if (lines.Count == 0) term.AddString($"claude {name}: done");
        }
    }
}
