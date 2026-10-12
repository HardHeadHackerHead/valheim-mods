using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// Reads the trigger-and-action system, the building blocks and key items from a pack's JSON (PackReader calls Block, Room and Pack).
    /// No game types, so the offline harness reads them too. No look is built in: every block's look is its "parts".
    ///
    /// TRIGGERS, on a room: "triggers": [ {
    ///     "id": "t1",                                        unique in the room (default t0, t1...)
    ///     "on": [ {"event": "...", ...}, ... ],              one event or a list: any of them fires it ("when": "sequence": all, in order)
    ///     "when": "all" | "any" | "sequence",                "all" (default): every "if" holds; "any": one does; "sequence": "on" in order
    ///     "within": 20,                                      a sequence: seconds from its first event to its last (0: no limit)
    ///     "if": [ {"mech": "gate1", "state": 1}, {"mech": "x", "not": 0}, {"counter": "kills", "atLeast": 3, "atMost": 9, "equals": 4} ],
    ///     "do": [ ...actions... ],
    ///     "repeat": false,                                   fires every time (default: once, until a reset or a reset of its group)
    ///     "group": "g1",                                     what {"reset": "g1"} puts back
    ///     "solver": "ignore"                                 not needed for the solution: the solver leaves it out (it must be solvable without)
    ///   } ]
    /// Events: "enter" / "leave" {"at", "size", "from"} (a box in the room's frame; none: the whole room) becomes occupied / empty (living
    ///   players); "state" {"mech", "state"} a mechanism goes to a state (any change without "state"); "lever" {"mech"} pulled; "press" /
    ///   "release" {"mech"} a plate; "kills" {"at", "size", "count"} that many creatures died in the area; "place" / "take" {"mech", "item"}
    ///   an item put on / taken off a pedestal; "timer" {"name"} a named timer rang, or {"every": 30} every 30 s of the server's clock;
    ///   "lit" / "out" {"mech"} a brazier; "broken" {"mech"} a breakable; "cleared" {"mech"} an encounter won; "signal" {"name"} another
    ///   trigger's {"signal"}; "counter" {"name"} a counter changed.
    /// Actions (one verb key each): {"set": "gate1", "to": 1}, {"next": "stair1"}, {"start": "enc1"} (an encounter), {"spawn": "melee",
    ///   "count": 2, "at": [[x,y,z]], "level": 1} (the land's creatures of that role: melee, ranged, flying, elite), {"sound": "sfx_...",
    ///   "at": [x,y,z]} (a game prefab), {"message": "$key", "to": "room" | "dungeon"}, {"music": "<the game's music>"} ("" gives it back),
    ///   {"counter": "x", "set": 0} / {"counter": "x", "add": 1}, {"reset": "g1"} (its triggers, counters and mechanisms back to new),
    ///   {"timer": "t", "after": 30} / {"timer": "t", "every": 5} / {"timer": "t", "stop": true}, {"signal": "s"}, {"item": "smallkey",
    ///   "at": [x,y,z], "count": 1} (a key item, or a game item, dropped there).
    /// Names of counters, timers, signals and groups belong to the room; "@name" to the whole dungeon. Mechanisms are named as wires name them.
    ///
    /// BLOCKS (mechanism kinds; their look is their parts; a part's "show": [1] shows it only in those states):
    ///   pedestal  {"item": "smallkey" | "<game item>", "accepts": [...], "itemAt": [x,y,z]}: E puts its item on (or a hotbar key any it
    ///             accepts), hold E takes it off (dropped at its foot). States 0 empty, 1 its item, 2 another it accepts.
    ///   Any mechanism's part may also have "lit": [states]: its own fire (a Fireplace's flame, light, warmth and crackle; or its lights,
    ///             particles and looping sounds) burns only in those states, the model staying (torches that gutter out and relight).
    ///   brazier   {"burn": 60, "lightable": true}: lit by a player with a torch in hand (E) or a fire hit, or by an action; goes out after
    ///             "burn" seconds (0: stays lit). States 0 out, 1 lit. ONE model: give it the game's lit fire piece (any Fireplace prefab)
    ///             as a plain part; only its fire (flame, light, warmth, crackle) is on while it's lit (BlockSet.FlamesOf). Never an
    ///             unlit model in one state and a lit one in the other: SwapCheck reports any mechanism that swaps models in one spot.
    ///   pushblock {"cell": 2, "rows": ["#..#", "S...", "  .."]}: the grid seen from above (+z up, the mechanism's At its centre): '.' floor,
    ///             '#' wall, 'S' where it starts, ' ' floor for players only (the block can't go there). The grid is all the room the
    ///             block and the player pushing it have: off it is wall. A player walking into it pushes it one cell. States: the floor
    ///             cells in reading order. One that can be pushed where it can't come back needs a "group" and a trigger resetting it.
    ///   trap      {"damage": "pierce", "scale": 1, "every": 4, "for": 1, "offset": 0, "area": [x,y,z], "areaAt": [x,y,z], "poses": [rest, fired]}:
    ///             while on (state 1) it fires "for" seconds every "every" seconds; "every" 0: once each time it's set on. Hurts players in
    ///             its area by the land's tier, never more than part of their health at low tiers. "warn": 0.8, "warnFx": [...],
    ///             "warnLift": 0.3: a wind-up before each firing (its sounds played, its parts drawn back that far from the strike and
    ///             shaking); one set on by an action winds up first, then fires.
    ///   teleporter {"pair": "tp1", "send": true, "step": false}: E (or stepping on it) takes a player to the pair's other end; "send":
    ///             false is arrival only. States 0 off, 1 on ("start": 0 to switch it on with an action).
    ///   lock      {"doorway": "north", "key": "smallkey", "parts", "poses": [shut, open]}: a gate a key opens (used up).
    ///   checkpoint {}: E makes it this player's: dying inside, they come back here instead of their bed (until they leave by the way out
    ///             or the dungeon resets).
    /// KEY ITEMS, on the pack: "items": { "smallkey": { "look": "CryptKey", "name": "$...", "description": "$..." } }.
    /// </summary>
    internal static class TriggerReader
    {
        public static readonly string[] BlockKinds = { "pedestal", "brazier", "pushblock", "trap", "teleporter", "lock", "checkpoint" };
        public static readonly string[] DamageTypes = { "blunt", "slash", "pierce", "fire", "frost", "lightning", "poison", "spirit" };

        private static readonly ConditionalWeakTable<Pack, Dictionary<string, KeyItemDef>> Items = new ConditionalWeakTable<Pack, Dictionary<string, KeyItemDef>>();

        /// <summary>A pack's key items by their pack name (empty if none).</summary>
        public static Dictionary<string, KeyItemDef> KeyItems(Pack pack) =>
            pack != null && Items.TryGetValue(pack, out Dictionary<string, KeyItemDef> d) ? d : new Dictionary<string, KeyItemDef>();

        /// <summary>The prefab an item name means in a pack: its key item's, else the name itself (one of the game's items).</summary>
        public static string ItemPrefab(Pack pack, string name) =>
            string.IsNullOrEmpty(name) ? null : KeyItems(pack).TryGetValue(name, out KeyItemDef k) ? k.Prefab : name;

        /// <summary>The prefab name of a pack's key item (lower case letters, digits and _ only).</summary>
        public static string KeyPrefab(string pack, string name) => "quaddungeon_key_" + Clean(pack) + "_" + Clean(name);

        private static string Clean(string s) => new string((s ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        public static bool IsBlock(MechDef m) => m != null && BlockKinds.Contains(m.Kind);

        // ---- the hooks PackReader calls

        /// <summary>The end of PackReader.ReadMech: the block's own fields, its parts' "show", and the states it needs.</summary>
        public static void Block(JObject m, MechDef mech, List<string> problems, string where)
        {
            // a part's "show": the states it shows in (any mechanism)
            if (m["parts"] is JArray parts)
            {
                List<JObject> list = parts.OfType<JObject>().ToList();
                for (int i = 0; i < list.Count && i < mech.Parts.Count; i++)
                {
                    if (list[i]["show"] is JArray show) mech.Parts[i].Show = show.Where(Num).Select(x => I(x, 0)).ToList();
                    if (list[i]["lit"] is JArray lit) mech.Parts[i].Lit = lit.Where(Num).Select(x => I(x, 0)).ToList();
                }
            }
            SwapCheck(mech, problems, $"{where} mechanism {mech.Id}");
            string group = S(m["group"]);
            if (!IsBlock(mech))
            {
                if (!string.IsNullOrEmpty(group)) mech.Block = new BlockDef { Group = group };   // (only its reset group)
                return;
            }
            string me = $"{where} mechanism {mech.Id}";
            var b = new BlockDef
            {
                Group = string.IsNullOrEmpty(group) ? null : group,
                Fx = Strings(m["fx"]).ToList(),
                Item = S(m["item"]),
                Accepts = Strings(m["accepts"]).ToList(),
                ItemAt = m["itemAt"] is JArray ia ? Vec(ia) : new Vector3(0f, 1f, 0f),
                Burn = F(m["burn"], 0f),
                Lightable = B(m["lightable"], true),
                Cell = F(m["cell"], 2f),
                Rows = Strings(m["rows"]).ToList(),
                Damage = (S(m["damage"]) ?? "pierce").ToLowerInvariant(),
                Scale = F(m["scale"], 1f),
                Every = F(m["every"], 0f),
                For = F(m["for"], 1f),
                Offset = F(m["offset"], 0f),
                AreaAt = m["areaAt"] is JArray aa ? Vec(aa) : Vector3.zero,
                AreaSize = m["area"] is JArray ar ? Vec(ar) : Vector3.zero,
                Warn = F(m["warn"], 0f),
                WarnFx = Strings(m["warnFx"]).ToList(),
                WarnLift = F(m["warnLift"], 0f),
                Send = B(m["send"], true),
                Step = B(m["step"], false),
                Key = S(m["key"]),
            };
            mech.Block = b;
            switch (mech.Kind)
            {
                case "pedestal":
                    if (!string.IsNullOrEmpty(b.Item) && !b.Accepts.Contains(b.Item)) b.Accepts.Insert(0, b.Item);
                    if (b.Accepts.Count == 0) problems.Add($"{me}: a pedestal needs its \"item\" (or \"accepts\")");
                    Pad(mech, 3);
                    break;
                case "brazier":
                    if (b.Burn < 0f) { problems.Add($"{me}: \"burn\" below 0 (taken as 0: stays lit)"); b.Burn = 0f; }
                    Pad(mech, 2);
                    break;
                case "teleporter":
                    if (string.IsNullOrEmpty(mech.Pair)) problems.Add($"{me}: a teleporter needs its \"pair\"");
                    if (m["start"] == null) mech.Start = 1;
                    Pad(mech, 2);
                    break;
                case "trap":
                    if (!DamageTypes.Contains(b.Damage)) problems.Add($"{me}: damage {b.Damage} ({string.Join(", ", DamageTypes)})");
                    if (b.Scale <= 0f || b.Scale > 5f) problems.Add($"{me}: \"scale\" {b.Scale} (above 0, at most 5)");
                    if (b.Every < 0f || b.For <= 0f || (b.Every > 0f && b.For >= b.Every)) problems.Add($"{me}: it fires \"for\" {b.For} s every {b.Every} s (for must be above 0 and below every)");
                    if (b.Warn < 0f || (b.Every > 0f && b.Warn > b.Every - b.For)) problems.Add($"{me}: \"warn\" {b.Warn} s (0 or more, fitting in the rest between firings: at most every - for = {b.Every - b.For:0.##} s)");
                    if (b.WarnLift < 0f || b.WarnLift > 2f) problems.Add($"{me}: \"warnLift\" {b.WarnLift} m (0 to 2)");
                    // its poses are its own clock's (rest, fired), not states: the mechanism itself stands at rest, off or on (so the
                    // layout's checks see it at rest), and its parts move from there on its own clock (BlockSet.Trap)
                    b.TrapPoses.AddRange(mech.Poses);
                    if (b.TrapPoses.Count == 1) b.TrapPoses.Add(b.TrapPoses[0]);
                    mech.Poses.Clear();
                    if (b.TrapPoses.Count > 0) mech.Poses.Add(b.TrapPoses[0]);
                    mech.Seconds = 0f;
                    if (m["start"] == null) mech.Start = b.Every > 0f ? 1 : 0;
                    Pad(mech, 2);
                    break;
                case "pushblock":
                    ReadGrid(mech, b, problems, me);
                    if (m["seconds"] == null) mech.Seconds = 0.5f;
                    break;
                case "lock":
                    if (string.IsNullOrEmpty(b.Key)) problems.Add($"{me}: a lock needs its \"key\" (one of the pack's \"items\")");
                    if (string.IsNullOrEmpty(mech.Doorway)) problems.Add($"{me}: a lock needs the \"doorway\" it closes");
                    if (mech.Once) problems.Add($"{me}: \"once\" is for levers and plates");
                    break;
                case "checkpoint":
                    Pad(mech, 1);
                    break;
            }
            if (mech.Cycle > 0f && mech.Kind != "teleporter")
            {
                problems.Add($"{me}: a {mech.Kind} doesn't cycle (\"cycle\" is left out; a trap has \"every\")");
                mech.Cycle = 0f;
            }
        }

        /// <summary>A mechanism whose states swap one model for another in the same spot (two parts shown in states that never meet, less
        /// than a metre apart): the player sees the model jump or change as it changes state. A block keeps one model and shows only what
        /// it adds ("show": a glow, a flame on top); a brazier's flames come from its own fire, lit only while it's lit.</summary>
        private static void SwapCheck(MechDef mech, List<string> problems, string me)
        {
            var shown = mech.Parts.Where(p => p.Show != null && p.Show.Count > 0).ToList();
            for (int i = 0; i < shown.Count; i++)
                for (int j = i + 1; j < shown.Count; j++)
                {
                    PartDef a = shown[i], b = shown[j];
                    if (a.Show.Intersect(b.Show).Any() || (a.At - b.At).magnitude >= 1f) continue;
                    problems.Add($"{me}: its parts {a.Prefab} (shown in {string.Join(",", a.Show)}) and {b.Prefab} (shown in {string.Join(",", b.Show)}) swap " +
                                 "one model for another in the same spot: keep one model, and \"show\" only what a state adds");
                    return;
                }
        }

        /// <summary>At least n states: the poses padded with the last one (or where it stands), so a block with no moving parts has them.</summary>
        private static void Pad(MechDef mech, int n)
        {
            while (mech.Poses.Count < n) mech.Poses.Add(mech.Poses.Count > 0 ? mech.Poses[mech.Poses.Count - 1] : new Pose());
        }

        private static void ReadGrid(MechDef mech, BlockDef b, List<string> problems, string me)
        {
            if (b.Rows.Count == 0) { problems.Add($"{me}: a pushblock needs its \"rows\" (the grid)"); Pad(mech, 1); return; }
            if (b.Cell < 0.5f) { problems.Add($"{me}: \"cell\" {b.Cell} (at least 0.5 m)"); b.Cell = 2f; }
            int cols = b.Rows.Max(r => r.Length), rows = b.Rows.Count, start = -1;
            mech.Poses.Clear();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < b.Rows[r].Length; c++)
                {
                    char ch = b.Rows[r][c];
                    if (ch == '#' || ch == ' ') continue;
                    if (ch != '.' && ch != 'S') { problems.Add($"{me}: the grid has '{ch}' (only '.', '#', ' ' and 'S')"); continue; }
                    if (ch == 'S') { if (start >= 0) problems.Add($"{me}: the grid has two 'S'"); start = b.Cells.Count; }
                    b.Cells.Add(new Vector2Int(c, r));
                    mech.Poses.Add(new Pose { At = CellAt(b, cols, rows, c, r) });
                }
            if (start < 0) problems.Add($"{me}: the grid has no 'S' (where the block starts)");
            mech.Start = Math.Max(0, start);
            if (mech.Poses.Count == 0) Pad(mech, 1);
        }

        /// <summary>A grid cell's centre in the mechanism's frame (row 0 the farthest +z, column 0 the farthest -x).</summary>
        public static Vector3 CellAt(BlockDef b, int cols, int rows, int c, int r) =>
            new Vector3((c - (cols - 1) / 2f) * b.Cell, 0f, ((rows - 1) / 2f - r) * b.Cell);

        /// <summary>The cell a pushblock's state stands on, and the state of a cell (-1: none).</summary>
        public static int StateAt(BlockDef b, int c, int r) => b.Cells.FindIndex(x => x.x == c && x.y == r);

        /// <summary>The end of PackReader.ReadRoom: the room's triggers.</summary>
        public static void Room(JObject r, RoomDef def, List<string> problems, string packName)
        {
            string where = $"{packName}: {def.Name}";
            foreach (JObject t in Objects(r["triggers"]))
            {
                var trig = new TriggerDef
                {
                    Id = S(t["id"]) ?? "t" + def.Triggers.Count,
                    When = (S(t["when"]) ?? "all").ToLowerInvariant(),
                    Within = F(t["within"], 0f),
                    Repeat = B(t["repeat"], false),
                    Group = S(t["group"]),
                    SolverIgnore = string.Equals(S(t["solver"]), "ignore", StringComparison.OrdinalIgnoreCase),
                };
                foreach (JObject e in Objects(t["on"]))
                    trig.On.Add(new TriggerEvent
                    {
                        Kind = (S(e["event"]) ?? "").ToLowerInvariant(),
                        Mech = S(e["mech"]),
                        State = I(e["state"], -1),
                        At = e["at"] is JArray at ? Vec(at) : Vector3.zero,
                        Size = e["size"] is JArray size ? Vec(size) : Vector3.zero,
                        From = S(e["from"]),
                        Count = I(e["count"], 1),
                        Item = S(e["item"]),
                        Name = S(e["name"]),
                        Every = F(e["every"], 0f),
                    });
                foreach (JObject c in Objects(t["if"]))
                    trig.If.Add(new TriggerCondition
                    {
                        Mech = S(c["mech"]),
                        State = I(c["state"], -1),
                        Not = I(c["not"], -1),
                        Counter = S(c["counter"]),
                        AtLeast = NI(c["atLeast"]),
                        AtMost = NI(c["atMost"]),
                        EqualTo = NI(c["equals"]),
                    });
                foreach (JObject a in Objects(t["do"])) trig.Do.Add(ReadAction(a, problems, $"{where} trigger {trig.Id}"));
                def.Triggers.Add(trig);
            }
            CheckTriggers(def, problems, where);
        }

        private static TriggerAction ReadAction(JObject a, List<string> problems, string where)
        {
            // (a counter's "set" is its field, not the verb)
            List<string> verbs = TriggerActions.All.Where(v => a[v] != null).ToList();
            if (verbs.Contains(TriggerActions.Counter)) verbs.Remove(TriggerActions.Set);
            string verb = verbs.FirstOrDefault();
            var act = new TriggerAction { Verb = verb };
            if (verb == null) { problems.Add($"{where}: an action with no verb ({string.Join(", ", TriggerActions.All)})"); return act; }
            if (verbs.Count > 1) problems.Add($"{where}: an action with two verbs ({string.Join(", ", verbs)}: one each, in two actions)");
            act.Target = S(a[verb]) ?? (a[verb].Type == JTokenType.Boolean ? null : a[verb].ToString());
            act.To = I(a["to"], 0);
            act.HasSet = a["set"] != null && verb == TriggerActions.Counter;
            if (verb == TriggerActions.Counter) { act.To = I(a["set"], 0); act.Add = I(a["add"], 0); }
            act.After = F(a["after"], 0f);
            act.Every = F(a["every"], 0f);
            act.Stop = B(a["stop"], false);
            act.Count = I(a["count"], 1);
            act.Level = I(a["level"], 1);
            act.ToWhom = (S(a["to"]) ?? "room").ToLowerInvariant();
            if (verb == TriggerActions.Message && a["to"] != null && a["to"].Type != JTokenType.String) act.ToWhom = "room";
            if (a["at"] is JArray at)
            {
                if (at.Count > 0 && at[0] is JArray) foreach (JArray p in at.OfType<JArray>()) act.At.Add(Vec(p));
                else act.At.Add(Vec(at));
            }
            return act;
        }

        /// <summary>What can't work in a room's triggers: unknown events, verbs and mechanisms, sequences, counters, timers.</summary>
        private static void CheckTriggers(RoomDef def, List<string> problems, string where)
        {
            bool Mech(string id, out MechDef m)
            {
                m = def.Mechs.FirstOrDefault(x => x.Id == id);
                return m != null || (id != null && id.StartsWith("step:"));
            }
            var ids = new HashSet<string>();
            string[] roles = { "melee", "ranged", "flying", "elite" };
            foreach (TriggerDef t in def.Triggers)
            {
                string me = $"{where} trigger {t.Id}";
                if (!ids.Add(t.Id)) problems.Add($"{where}: two triggers are called {t.Id}");
                if (t.Id.Contains(".") || t.Id.Contains(":") || t.Id.Contains(";") || t.Id.Contains("=")) problems.Add($"{me}: an id can't hold '.', ':', ';' or '='");
                if (t.When != "all" && t.When != "any" && t.When != "sequence") problems.Add($"{me}: \"when\" {t.When} (\"all\", \"any\" or \"sequence\")");
                if (t.On.Count == 0) problems.Add($"{me}: no \"on\" (the events it waits for)");
                if (t.Do.Count == 0) problems.Add($"{me}: no \"do\" (what it does)");
                if (t.When == "sequence" && t.On.Count < 2) problems.Add($"{me}: a sequence needs two events or more");
                if (t.Within < 0f) problems.Add($"{me}: \"within\" below 0");
                if (t.Within > 0f && t.When != "sequence") problems.Add($"{me}: \"within\" is for sequences");
                foreach (TriggerEvent e in t.On)
                {
                    string ev = $"{me}: event {e.Kind}";
                    if (!TriggerEvents.All.Contains(e.Kind)) { problems.Add($"{me}: unknown event \"{e.Kind}\" ({string.Join(", ", TriggerEvents.All)})"); continue; }
                    switch (e.Kind)
                    {
                        case TriggerEvents.State: case TriggerEvents.Lever: case TriggerEvents.Press: case TriggerEvents.Release:
                        case TriggerEvents.Place: case TriggerEvents.Take: case TriggerEvents.Lit: case TriggerEvents.Out:
                        case TriggerEvents.Broken: case TriggerEvents.Cleared:
                            if (string.IsNullOrEmpty(e.Mech)) { problems.Add($"{ev} needs its \"mech\""); break; }
                            if (!Mech(e.Mech, out MechDef m)) { problems.Add($"{ev}: {e.Mech} isn't a mechanism in its room (or \"step:<id>.<mech>\")"); break; }
                            if (m == null) break;
                            string need = e.Kind == TriggerEvents.Lever ? "lever" : e.Kind == TriggerEvents.Press || e.Kind == TriggerEvents.Release ? "plate"
                                : e.Kind == TriggerEvents.Place || e.Kind == TriggerEvents.Take ? "pedestal" : e.Kind == TriggerEvents.Lit || e.Kind == TriggerEvents.Out ? "brazier"
                                : e.Kind == TriggerEvents.Broken ? "breakable" : e.Kind == TriggerEvents.Cleared ? "encounter" : null;
                            if (need != null && m.Kind != need) problems.Add($"{ev}: {e.Mech} is a {m.Kind}, not a {need}");
                            if (e.Kind == TriggerEvents.State && e.State >= Layouts.States(m)) problems.Add($"{ev}: {e.Mech} has states 0 to {Layouts.States(m) - 1}");
                            break;
                        case TriggerEvents.Kills:
                            if (e.Count < 1) problems.Add($"{ev}: \"count\" at least 1");
                            break;
                        case TriggerEvents.Enter: case TriggerEvents.Leave:
                            if (e.From != null && !def.Doorways.Any(d => d.Id == e.From)) problems.Add($"{ev}: \"from\" {e.From} isn't one of its room's doorways");
                            break;
                        case TriggerEvents.Timer:
                            if (string.IsNullOrEmpty(e.Name) == (e.Every <= 0f)) problems.Add($"{ev}: a \"name\" (a timer an action starts) or \"every\" (seconds), one of them");
                            else if (!string.IsNullOrEmpty(e.Name) && !AnyAction(def, TriggerActions.Timer, e.Name)) problems.Add($"{ev}: no action in its room starts timer {e.Name}");
                            break;
                        case TriggerEvents.Signal: case TriggerEvents.Counter:
                            if (string.IsNullOrEmpty(e.Name)) problems.Add($"{ev} needs its \"name\"");
                            else if (e.Kind == TriggerEvents.Signal && !e.Name.StartsWith("@") && !AnyAction(def, TriggerActions.Signal, e.Name)) problems.Add($"{ev}: no action in its room sends signal {e.Name}");
                            break;
                    }
                }
                foreach (TriggerCondition c in t.If)
                {
                    if (string.IsNullOrEmpty(c.Mech) == string.IsNullOrEmpty(c.Counter)) { problems.Add($"{me}: a condition names a \"mech\" or a \"counter\", one of them"); continue; }
                    if (c.Mech != null)
                    {
                        if (!Mech(c.Mech, out MechDef m)) problems.Add($"{me}: a condition on {c.Mech}, which isn't a mechanism in its room");
                        else if (c.State < 0 && c.Not < 0) problems.Add($"{me}: a condition on {c.Mech} needs \"state\" or \"not\"");
                        else if (m != null && Math.Max(c.State, c.Not) >= Layouts.States(m)) problems.Add($"{me}: a condition on {c.Mech}: it has states 0 to {Layouts.States(m) - 1}");
                    }
                    else if (c.AtLeast == null && c.AtMost == null && c.EqualTo == null) problems.Add($"{me}: a condition on counter {c.Counter} needs \"atLeast\", \"atMost\" or \"equals\"");
                }
                foreach (TriggerAction a in t.Do)
                {
                    if (a.Verb == null) continue;
                    string ac = $"{me}: action {a.Verb}";
                    switch (a.Verb)
                    {
                        case TriggerActions.Set: case TriggerActions.Next: case TriggerActions.Start:
                            if (!Mech(a.Target, out MechDef m)) { problems.Add($"{ac}: {a.Target} isn't a mechanism in its room (or \"step:<id>.<mech>\")"); break; }
                            if (m == null) break;
                            if (a.Verb == TriggerActions.Start && m.Kind != "encounter") problems.Add($"{ac}: {a.Target} is a {m.Kind}, not an encounter");
                            if (a.Verb == TriggerActions.Set && (a.To < 0 || a.To >= Layouts.States(m))) problems.Add($"{ac}: {a.Target} has states 0 to {Layouts.States(m) - 1}, not {a.To}");
                            if (a.Verb != TriggerActions.Start && m.Kind == "encounter") problems.Add($"{ac}: an encounter is started ({{\"start\"}}), not set");
                            if (a.Verb != TriggerActions.Start && (m.Kind == "pedestal" || m.Kind == "checkpoint")) problems.Add($"{ac}: a {m.Kind} isn't set by actions (its state is what players do)");
                            break;
                        case TriggerActions.Spawn:
                            if (!roles.Contains(a.Target)) problems.Add($"{ac}: role {a.Target} (melee, ranged, flying or elite)");
                            if (a.Count < 1 || a.Count > 20) problems.Add($"{ac}: \"count\" {a.Count} (1 to 20)");
                            if (a.At.Count == 0) problems.Add($"{ac}: no \"at\" (where they come, in the room's frame)");
                            break;
                        case TriggerActions.Sound: case TriggerActions.Message: case TriggerActions.Signal: case TriggerActions.Reset:
                            if (string.IsNullOrEmpty(a.Target)) problems.Add($"{ac} names nothing");
                            if (a.Verb == TriggerActions.Message && a.ToWhom != "room" && a.ToWhom != "dungeon") problems.Add($"{ac}: \"to\" {a.ToWhom} (\"room\" or \"dungeon\")");
                            if (a.Verb == TriggerActions.Reset && !a.Target.StartsWith("@") && !def.Triggers.Any(x => x.Group == a.Target) && !Groups(def).Contains(a.Target))
                                problems.Add($"{ac}: nothing in its room is in group {a.Target}");
                            break;
                        case TriggerActions.Timer:
                            if (!a.Stop && (a.After > 0f) == (a.Every > 0f)) problems.Add($"{ac}: \"after\" (once) or \"every\" (repeating) seconds, one of them, or \"stop\"");
                            break;
                        case TriggerActions.Item:
                            if (a.Count < 1 || a.Count > 10) problems.Add($"{ac}: \"count\" {a.Count} (1 to 10)");
                            break;
                    }
                }
            }
        }

        private static bool AnyAction(RoomDef def, string verb, string name) => def.Triggers.Any(t => t.Do.Any(a => a.Verb == verb && a.Target == name));

        /// <summary>A mechanism's reset group ("group" on any mechanism: what {"reset": group} puts back to its start), or null.</summary>
        public static string GroupOf(MechDef m) => m?.Block?.Group;

        private static IEnumerable<string> Groups(RoomDef def) => def.Mechs.Select(GroupOf).Where(g => g != null);

        /// <summary>The end of PackReader.Read: key items, teleporter pairs, locks' keys, and what the solver can't model.</summary>
        public static void Pack(JObject json, Pack pack, List<string> problems)
        {
            var items = new Dictionary<string, KeyItemDef>();
            if (json["items"] is JObject list)
                foreach (JProperty p in list.Properties())
                {
                    if (!(p.Value is JObject o)) { problems.Add($"{pack.Name}: item {p.Name} isn't an object"); continue; }
                    var k = new KeyItemDef
                    {
                        Name = p.Name,
                        Prefab = KeyPrefab(pack.Name, p.Name),
                        Look = S(o["look"]),
                        Title = S(o["name"]) ?? p.Name,
                        Description = S(o["description"]) ?? "",
                    };
                    if (string.IsNullOrEmpty(k.Look)) problems.Add($"{pack.Name}: item {p.Name} needs its \"look\" (a game item to copy)");
                    items[p.Name] = k;
                }
            Items.Remove(pack);
            Items.Add(pack, items);
            foreach (RoomDef r in pack.Rooms)
                foreach (MechDef m in r.Mechs.Where(x => x.Kind == "lock" && x.Block?.Key != null))
                    if (!items.ContainsKey(m.Block.Key)) problems.Add($"{pack.Name}: {r.Name} lock {m.Id}: its key {m.Block.Key} isn't one of the pack's \"items\"");
            // teleporters: each pair has two ends or more, one of them sending
            foreach (var pair in pack.Rooms.SelectMany(r => r.Mechs).Where(m => m.Kind == "teleporter" && !string.IsNullOrEmpty(m.Pair)).GroupBy(m => m.Pair))
            {
                if (pair.Count() < 2) problems.Add($"{pack.Name}: teleporter pair {pair.Key} has only one end");
                else if (!pair.Any(m => m.Block == null || m.Block.Send)) problems.Add($"{pack.Name}: teleporter pair {pair.Key}: no end sends");
                else if (pair.Count() > 2) problems.Add($"{pack.Name}: teleporter pair {pair.Key} has {pair.Count()} ends (two: put the others in another pair)");
            }
            foreach (string p in TriggerSolve.Unmodelled(pack)) problems.Add($"{pack.Name}: {p}");
        }

        // ---- JSON helpers (PackReader's are private): a value of the wrong type reads as missing, never throws

        private static bool Num(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float);
        private static int I(JToken t, int d) => Num(t) ? (int)Math.Round((double)t) : d;
        private static int? NI(JToken t) => Num(t) ? (int?)(int)Math.Round((double)t) : null;
        private static float F(JToken t, float d) => Num(t) ? (float)t : d;
        private static bool B(JToken t, bool d) => t != null && t.Type == JTokenType.Boolean ? (bool)t : d;
        private static string S(JToken t) => t != null && (t.Type == JTokenType.String || Num(t)) ? (string)t : null;

        private static IEnumerable<JObject> Objects(JToken t) => t is JObject one ? new[] { one } : (t as JArray ?? new JArray()).OfType<JObject>();
        private static Vector3 Vec(JArray a) => new Vector3(a.Count > 0 ? F(a[0], 0f) : 0f, a.Count > 1 ? F(a[1], 0f) : 0f, a.Count > 2 ? F(a[2], 0f) : 0f);
        private static IEnumerable<string> Strings(JToken t) => t is JValue v && v.Type == JTokenType.String ? new[] { (string)v }
            : (t as JArray ?? new JArray()).Select(S).Where(x => !string.IsNullOrEmpty(x));
    }
}
