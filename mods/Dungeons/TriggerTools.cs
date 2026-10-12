using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// The Claude Tools commands of the triggers and blocks (Tools.cs passes them here):
    ///   dungeon triggers [id]              every trigger: its events, conditions (holding now or not), actions, fired, a sequence's progress;
    ///                                      the counters, timers and music the triggers keep
    ///   dungeon fire &lt;trigger key&gt; [id]    runs that trigger's actions now, events and conditions ignored (a cheat)
    ///   dungeon fire &lt;event&gt; &lt;what&gt; [id]  sends an event through the triggers as if it happened (a cheat): lever, press, release, state
    ///                                      (&lt;key&gt;=&lt;state&gt;), lit, out, broken, cleared, place, take (&lt;key&gt;[=&lt;item&gt;]) on a mechanism key;
    ///                                      signal, timer, counter on a name ("3.name" for room 3's, "@name" for the dungeon's); kills at the
    ///                                      player (&lt;what&gt;: how many)
    ///   dungeon blocks [id]                every block: pedestals' items, braziers lit (and for how long), pushblocks' cells, traps on and
    ///                                      firing, teleporters' pairs, locks and their keys, checkpoints
    ///   dungeon checkpoint [clear]         the local player's checkpoint, or clear it
    /// </summary>
    internal static class TriggerTools
    {
        public const string Usage = "triggers [id] | fire <trigger key | event what> [id] | blocks [id] | checkpoint [clear]";

        public static void Command(string sub, string[] args, Action<JObject> output, Action<string> error, Func<string[], int, Action<string>, Site> find)
        {
            if (sub == "checkpoint") { Checkpoint(args, output); return; }
            bool fire = sub == "fire";
            if (fire && !DHack.Shared.TestCommands.CheatsAllowed()) { error(DHack.Shared.TestCommands.Refusal("dungeon fire")); return; }
            if (fire && args.Length < 2) { error("dungeon fire <trigger key> [id]  or  dungeon fire <event> <what> [id]"); return; }
            bool isEvent = fire && TriggerEvents.All.Contains(args[1].ToLowerInvariant());
            int at = !fire ? 1 : isEvent ? 3 : 2;
            Site site = find(args, at, error);
            if (site == null) return;
            if (site.Mechs == null) { error(site.Ready ? "this dungeon has no mechanisms or triggers" : "it isn't built here yet"); return; }
            switch (sub)
            {
                case "triggers": output(site.Mechs.TriggersReport()); return;
                case "blocks": output(site.Mechs.BlocksReport()); return;
            }
            if (!site.IsOwner)
            {
                if (site.View == null) { error("its entrance isn't loaded here (the player is far inside it): go nearer the entrance"); return; }
                site.View.ClaimOwnership();
            }
            string why = isEvent ? site.Mechs.FireCommand(args[1].ToLowerInvariant(), args.Length > 2 ? args[2] : "") : site.Mechs.RunCommand(args[1]);
            if (why != null) { error(why); return; }
            output(new JObject { ["fired"] = string.Join(" ", args.Skip(1).Take(at - 1)), ["triggers"] = site.Mechs.TriggersReport() });
        }

        private static void Checkpoint(string[] args, Action<JObject> output)
        {
            if (args.Length > 1 && args[1].ToLowerInvariant() == "clear") Checkpoints.Clear(Player.m_localPlayer, "the checkpoint command");
            Checkpoints.Point? p = Checkpoints.Mine;
            output(p is Checkpoints.Point c
                ? new JObject { ["dungeon"] = c.Dungeon, ["resets"] = c.Resets, ["mechanism"] = c.Mech, ["at"] = new JArray(Math.Round(c.At.x, 2), Math.Round(c.At.y, 2), Math.Round(c.At.z, 2)),
                                ["note"] = "dying inside, the player comes back here (until they leave by the way out, or the dungeon resets)" }
                : new JObject { ["checkpoint"] = null, ["note"] = "none: dying, the player comes back at their bed" });
        }
    }

    internal partial class MechSet
    {
        /// <summary>For `dungeon triggers`.</summary>
        public JObject TriggersReport()
        {
            ReadTriggers();
            var list = new JArray();
            foreach (Trig t in Trigs())
            {
                var o = new JObject
                {
                    ["key"] = t.Key,
                    ["room"] = t.Room + " " + Layout.Rooms[t.Room].Def?.Name,
                    ["on"] = new JArray(t.Def.On.Select((e, i) => e.Kind + (t.Mechs[i] != null ? " " + t.Mechs[i] : "") + (e.State >= 0 ? "=" + e.State : "")
                                                              + (e.Name != null ? " " + Scoped(t.Room, e.Name) : "") + (e.Every > 0f ? $" every {e.Every:0.#} s" : "")
                                                              + (e.Kind == TriggerEvents.Kills ? $" ({TriggerState.I(TrigValue("k|" + t.Key + "|" + i))} of {e.Count})" : "")
                                                              + (_occupied.TryGetValue(t.Key + "|" + i, out bool occ) ? (occ ? " (occupied)" : " (empty)") : ""))),
                    ["when"] = t.Def.When + (t.Def.Within > 0f ? $" within {t.Def.Within:0.#} s" : ""),
                    ["if"] = new JArray(t.Def.If.Select(c => (c.Mech != null ? $"{Resolve(t.Room, c.Mech)} {(c.State >= 0 ? "is " + c.State : "isn't " + c.Not)}"
                                                                   : $"counter {Scoped(t.Room, c.Counter)} = {Counter(Scoped(t.Room, c.Counter))}"
                                                                     + (c.AtLeast != null ? $", at least {c.AtLeast}" : "") + (c.AtMost != null ? $", at most {c.AtMost}" : "") + (c.EqualTo != null ? $", equals {c.EqualTo}" : "")))),
                    ["holdsNow"] = Conditions(t),
                    ["do"] = new JArray(t.Def.Do.Select(a => a.Verb + " " + a.Target + (a.Verb == TriggerActions.Set ? " to " + a.To : ""))),
                    ["fired"] = TrigValue("f|" + t.Key) != null,
                };
                if (t.Def.Repeat) o["repeat"] = true;
                if (t.Def.Group != null) o["group"] = Scoped(t.Room, t.Def.Group);
                if (t.Def.SolverIgnore) o["solver"] = "ignored";
                string q = TrigValue("q|" + t.Key);
                if (q != null) o["sequence"] = $"{q.Split(',')[0]} of {t.Def.On.Count} done";
                list.Add(o);
            }
            double now = MechClock.Now;
            return new JObject
            {
                ["triggers"] = list,
                ["counters"] = new JObject(_trig.Where(k => k.Key.StartsWith("c|")).Select(k => new JProperty(k.Key.Substring(2), TriggerState.I(k.Value)))),
                ["timers"] = new JObject(_trig.Where(k => k.Key.StartsWith("t|")).Select(k =>
                {
                    string[] f = k.Value.Split(',');
                    double every = f.Length > 1 ? TriggerState.D(f[1]) : 0.0;
                    return new JProperty(k.Key.Substring(2), $"rings in {Math.Max(0.0, TriggerState.D(f[0]) - now):0.0} s" + (every > 0 ? $", then every {every:0.#} s" : ""));
                })),
                ["music"] = TrigValue("m"),
                ["owner"] = Site.IsOwner ? "this game" : Site.Zdo?.GetOwner().ToString(),
                ["saved"] = _trigText,
            };
        }

        /// <summary>For `dungeon blocks`.</summary>
        public JObject BlocksReport()
        {
            double now = MechClock.Now;
            var list = new JArray();
            foreach (Mech m in All.Where(x => TriggerReader.IsBlock(x.Def)))
            {
                BlockDef b = m.Def.Block;
                int s = StateOf(m);
                var o = new JObject { ["key"] = m.Key, ["kind"] = m.Def.Kind, ["state"] = s, ["room"] = m.Room + " " + Layout.Rooms[m.Room].Def?.Name };
                if (b?.Group != null) o["group"] = b.Group;
                switch (m.Def.Kind)
                {
                    case "pedestal":
                        o["holds"] = TrigValue("p|" + m.Key);
                        o["wants"] = TriggerReader.ItemPrefab(Layout.Pack, b?.Item);
                        o["accepts"] = new JArray((b?.Accepts ?? new List<string>()).Select(a => TriggerReader.ItemPrefab(Layout.Pack, a)));
                        break;
                    case "brazier":
                        o["lit"] = s == 1;
                        if (s == 1 && b != null && b.Burn > 0f) o["goesOutIn"] = Math.Round(Math.Max(0.0, Motion(m, now).Start + b.Burn - now), 1);
                        o["lightable"] = b?.Lightable ?? false;
                        break;
                    case "pushblock":
                        if (b != null && s >= 0 && s < b.Cells.Count) o["cell"] = $"column {b.Cells[s].x}, row {b.Cells[s].y}";
                        o["cells"] = b?.Cells.Count ?? 0;
                        break;
                    case "trap":
                        o["on"] = s == 1;
                        o["firing"] = b != null && Firing(m, now, out _, out _);
                        o["damage"] = b == null ? null : $"{b.Damage} x{b.Scale:0.##}" + (b.Every > 0f ? $", {b.For:0.#} s every {b.Every:0.#} s" : $", {b.For:0.#} s when set on")
                                      + (b.Warn > 0f ? $", winds up {b.Warn:0.#} s before" : "");
                        break;
                    case "teleporter":
                        Mech other = OtherTeleporter(m);
                        o["pair"] = m.Def.Pair;
                        o["otherEnd"] = other?.Key;
                        o["sends"] = b?.Send ?? true;
                        o["on"] = s >= 1;
                        o["usable"] = CanSend(m, out _);
                        break;
                    case "lock":
                        o["key"] = TriggerReader.ItemPrefab(Layout.Pack, b?.Key);
                        o["open"] = s >= 1;
                        o["doorway"] = m.Def.Doorway;
                        break;
                    case "checkpoint":
                        o["yours"] = Checkpoints.IsMine(Site, m);
                        break;
                }
                if (Player.m_localPlayer != null) o["distance"] = Math.Round(Vector3.Distance(Player.m_localPlayer.transform.position, m.WorldPos), 1);
                list.Add(o);
            }
            return new JObject
            {
                ["blocks"] = list,
                ["keyItems"] = new JArray(TriggerReader.KeyItems(Layout.Pack).Values.Select(k => $"{k.Name}: {k.Prefab} (looks like {k.Look})")),
            };
        }

        /// <summary>`dungeon fire &lt;trigger key&gt;`: its actions now. Null, or why not.</summary>
        public string RunCommand(string key)
        {
            Trig t = Trigs().FirstOrDefault(x => x.Key == key);
            if (t == null) return $"no trigger {key} (dungeon triggers lists them)";
            Run(t, 0, "the fire command");
            return null;
        }

        /// <summary>`dungeon fire &lt;event&gt; &lt;what&gt;`: the event sent through the triggers. Null, or why not.</summary>
        public string FireCommand(string kind, string what)
        {
            switch (kind)
            {
                case TriggerEvents.Signal: case TriggerEvents.Timer: case TriggerEvents.Counter:
                    if (string.IsNullOrEmpty(what)) return $"dungeon fire {kind} <room>.<name> (or @name)";
                    Fire(kind, name: what);
                    return null;
                case TriggerEvents.Kills:
                    if (Player.m_localPlayer == null) return "no player";
                    for (int i = 0; i < Math.Max(1, TriggerState.I(what)); i++) Killed(Player.m_localPlayer.transform.position);
                    return null;
                case TriggerEvents.Enter: case TriggerEvents.Leave:
                    return "enter and leave come from players in the areas: walk in (or out), or fire the trigger itself";
            }
            string key = what, arg = null;
            int eq = what.IndexOf('=');
            if (eq > 0) { key = what.Substring(0, eq); arg = what.Substring(eq + 1); }
            if (!ByKey.ContainsKey(key)) return $"no mechanism {key} (dungeon mechs lists them)";
            Fire(kind, key, arg != null ? TriggerState.I(arg) : -1, item: arg);
            return null;
        }
    }
}
