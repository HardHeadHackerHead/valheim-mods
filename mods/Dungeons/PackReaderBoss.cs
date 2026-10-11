using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// Reads and checks an encounter's "boss" (BossModel.cs; PackReader calls Read for each boss and Check for each room's encounters).
    /// The format, as the toolkit's authors keep it (pack-format.md and skill/bosses.md say it for pack makers):
    ///
    ///   "boss": { "name": "$...", "title": "$...", "role": "elite", "health": 6, "perPlayer": 0.5, "scale": 1.6, "mini": false,
    ///             "trophy": "quaddungeon_trophy_warden" (default; a mini boss: none; "" none),
    ///             "armour": 1, "armourOff": 10, "unstick": 5, "fight": 200 (seconds of one player's hitting: sets its health),
    ///             "look": { "light": [r, g, b], "range": 6, "aura": "vfx_Burning_blue", "enraged": [r, g, b] },
    ///             "intro": { "seconds": 6.5, "wake": 4, "leap": [x, y, z], "leapAt": 5, "beats": [ beat... ] },
    ///             "death": { "seconds": 3, "beats": [ beat... ] },
    ///             "abilities": [ { "id": "slam", "kind": "slam|rain|charge|summon|wires", "every": 10, "first": 6, "warn": 1.6,
    ///                              "radius": 5, "width": 3, "length": 18, "damage": "blunt", "scale": 2, "push": 30, "count": 0,
    ///                              "phases": [0, 1], "adds": {"melee": 2}, "points": [[x, y, z]], "wires": [...],
    ///                              "sound": "...", "fx": "...", "cue": "..." } ],
    ///             "weakTo": [ { "mech": "hammer", "state": 1, "area": [5, 4, 5], "areaAt": [0, -6, 0], "share": 0.1,
    ///                           "stagger": true, "breaks": true, "hurt": 2, "mark": true } ],
    ///             "phases": [ { "at": 0.66, "wires": [...], "adds": {...}, "use": ["slam", "rain"], "leap": [x, y, z], "enrage": 1.25, "healTo": 0.55,
    ///                           "banner": "$...", "message": "$...", "sound": "...", "shake": 0.5,
    ///                           "shield": { "take": 0, "until": [{"mech": "brazier1", "state": 1}], "seconds": 60, "hold": true,
    ///                                       "breakShare": 0.08, "breakWires": [...], "hint": "$...", "times": 1 } } ] }
    ///   a beat: { "at": 1.5, "wires": [...], "sound": "...", "fx": "...", "where": [x, y, z], "shake": 0.4, "banner": true, "message": "$..." }
    /// Points (leap, where, summon points) are in the room's frame, on a floor. Wires are a mechanism of the room (id) with "next" or
    /// "set:n", as a boss phase's.
    /// </summary>
    internal static class BossReader
    {
        public static readonly string[] Kinds = { "slam", "rain", "charge", "summon", "wires" };
        public static readonly string[] Roles = { "melee", "ranged", "flying", "elite" };
        public static readonly string[] DamageTypes = { "blunt", "slash", "pierce", "fire", "frost", "lightning", "poison", "spirit" };

        public static BossDef Read(JObject b, JObject mech)
        {
            var boss = new BossDef
            {
                Name = (string)b["name"],
                Role = (string)b["role"] ?? "elite",
                Health = F(b["health"], 4f),
                Title = (string)b["title"],
                Mini = (bool?)b["mini"] ?? false,
                Trophy = (string)b["trophy"],
                Scale = F(b["scale"], 1f),
                PerPlayer = F(b["perPlayer"], 0.5f),
                Fight = F(b["fight"], 0f),
                Armour = F(b["armour"], 1f),
                ArmourOff = F(b["armourOff"], 10f),
                Unstick = F(b["unstick"], 5f),
            };
            if (b["look"] is JObject look)
                boss.Look = new BossLook
                {
                    Light = Col(look["light"], new Color(0.45f, 0.75f, 1f)),
                    Range = F(look["range"], 6f),
                    Aura = (string)look["aura"],
                    Enraged = Col(look["enraged"], new Color(1f, 0.45f, 0.2f)),
                };
            if (b["intro"] is JObject intro)
            {
                boss.Intro = new BossIntro { Seconds = F(intro["seconds"], 0f), Wake = F(intro["wake"], 0f), Leap = OptVec(intro["leap"]), LeapAt = F(intro["leapAt"], 0f) };
                boss.Intro.Beats.AddRange(Beats(intro["beats"]));
            }
            if (b["death"] is JObject death)
            {
                boss.Death = new BossTimeline { Seconds = F(death["seconds"], 0f) };
                boss.Death.Beats.AddRange(Beats(death["beats"]));
            }
            foreach (JObject a in Objects(b["abilities"]))
            {
                var ab = new BossAbility
                {
                    Id = (string)a["id"],
                    Kind = (string)a["kind"],
                    Every = F(a["every"], 10f),
                    First = F(a["first"], -1f),
                    Warn = F(a["warn"], 1.6f),
                    Radius = F(a["radius"], 5f),
                    Width = F(a["width"], 3f),
                    Length = F(a["length"], 18f),
                    Damage = (string)a["damage"] ?? "blunt",
                    Scale = F(a["scale"], 2f),
                    Push = F(a["push"], 30f),
                    Count = (int?)a["count"] ?? 0,
                    Phases = a["phases"] is JArray ph ? ph.Where(Num).Select(x => (int)x).ToList() : null,
                    Sound = (string)a["sound"],
                    Fx = (string)a["fx"],
                    Cue = (string)a["cue"],
                };
                if (string.IsNullOrEmpty(ab.Id)) ab.Id = ab.Kind;
                if (a["adds"] is JObject adds) foreach (JProperty p in adds.Properties().Where(p => p.Value.Type == JTokenType.Integer)) ab.Adds[p.Name] = (int)p.Value;
                foreach (JArray p in (a["points"] as JArray ?? new JArray()).OfType<JArray>()) ab.Points.Add(Vec(p));
                ab.Wires.AddRange(Wires(a["wires"]));
                boss.Abilities.Add(ab);
            }
            foreach (JObject w in Objects(b["weakTo"]))
                boss.WeakTo.Add(new BossWeak
                {
                    Mech = (string)w["mech"],
                    State = (int?)w["state"] ?? 1,
                    Area = w["area"] is JArray area ? Vec(area) : new Vector3(5f, 4f, 5f),
                    AreaAt = w["areaAt"] is JArray areaAt ? Vec(areaAt) : Vector3.zero,
                    Share = F(w["share"], 0.1f),
                    Stagger = (bool?)w["stagger"] ?? true,
                    Breaks = (bool?)w["breaks"] ?? true,
                    Hurt = F(w["hurt"], 2f),
                    Mark = (bool?)w["mark"] ?? true,
                });
            // (its phases in the boss, or, as one draft wrote them, on the encounter)
            foreach (JObject ph in Objects(b["phases"] ?? mech["phases"]))
            {
                var phase = new BossPhase { At = F(ph["at"], 0.5f) };
                phase.Wires.AddRange(Wires(ph["wires"]));
                if (ph["adds"] is JObject adds)
                    foreach (JProperty a in adds.Properties().Where(p => p.Value.Type == JTokenType.Integer)) phase.Adds[a.Name] = (int)a.Value;
                if (ph["use"] is JArray use) phase.Use = use.Select(x => (string)x).Where(x => !string.IsNullOrEmpty(x)).ToList();
                phase.Leap = OptVec(ph["leap"]);
                phase.Enrage = F(ph["enrage"], 0f);
                phase.HealTo = F(ph["healTo"], 0f);
                phase.Banner = (string)ph["banner"];
                phase.Message = (string)ph["message"];
                phase.Sound = (string)ph["sound"];
                phase.Shake = F(ph["shake"], 0f);
                if (ph["shield"] is JObject s)
                {
                    phase.Shield = new BossShield
                    {
                        Take = F(s["take"], 0f),
                        Seconds = F(s["seconds"], 60f),
                        Hold = (bool?)s["hold"] ?? true,
                        BreakShare = F(s["breakShare"], 0.08f),
                        Hint = (string)s["hint"],
                        Times = (int?)s["times"] ?? 1,
                    };
                    foreach (JObject c in Objects(s["until"])) phase.Shield.Until.Add(new BossCond { Mech = (string)c["mech"], State = (int?)c["state"] ?? 1 });
                    phase.Shield.BreakWires.AddRange(Wires(s["breakWires"]));
                }
                boss.Phases.Add(phase);
            }
            return boss;
        }

        /// <summary>What can't work in an encounter's boss (CheckMechs, for each encounter with one). me: "pack: room mechanism id".</summary>
        public static void Check(RoomDef def, MechDef m, EncounterDef e, List<string> problems, string me)
        {
            BossDef b = e.Boss;
            if (!Roles.Contains(b.Role)) problems.Add($"{me}: the boss's role {b.Role} (melee, ranged, flying or elite)");
            if (b.Health <= 0f) problems.Add($"{me}: the boss's health {b.Health} (more than 0)");
            if (e.Waves.Count > 0) problems.Add($"{me}: has a boss and waves (a boss fight is one creature instead of waves)");
            if (e.Points.Count == 0) problems.Add($"{me}: has a boss but no \"points\" for it to come at");
            float last = 1f;
            foreach (BossPhase ph in b.Phases)
            {
                if (ph.At <= 0f || ph.At >= 1f) problems.Add($"{me}: a boss phase at {ph.At} (a health fraction between 0 and 1)");
                else if (ph.At >= last) problems.Add($"{me}: a boss phase at {ph.At} comes after one at {last} (phases go from high health to low)");
                last = Math.Min(last, ph.At);
                foreach (Wire w in ph.Wires.Where(w => !def.Mechs.Any(o => o.Id == w.Target)))
                    problems.Add($"{me}: a boss phase is wired to {w.Target}, which isn't a mechanism in its room");
                foreach (string role in ph.Adds.Keys.Where(r => !Roles.Contains(r))) problems.Add($"{me}: a boss phase adds role {role}");
                foreach (string id in ph.Use ?? new List<string>())
                    if (!b.Abilities.Any(a => a.Id == id)) problems.Add($"{me}: a boss phase uses {id}, which isn't one of its abilities");
                if (ph.Enrage != 0f && (ph.Enrage < 1f || ph.Enrage > 2f)) problems.Add($"{me}: a boss phase's enrage {ph.Enrage} (1 to 2)");
                if (ph.Shake < 0f || ph.Shake > 1f) problems.Add($"{me}: a boss phase's shake {ph.Shake} (0 to 1)");
                if (ph.HealTo != 0f && (ph.HealTo <= ph.At || ph.HealTo > 1f))
                    problems.Add($"{me}: a boss phase at {ph.At} heals to {ph.HealTo} (above its \"at\", 1 at most)");
                if (ph.Leap.HasValue) InRoom(def, ph.Leap.Value, $"{me}: a boss phase's leap", problems);
                if (ph.Shield != null)
                {
                    BossShield s = ph.Shield;
                    if (s.Take < 0f || s.Take >= 1f) problems.Add($"{me}: a shield's take {s.Take} (0 to under 1: the share of each hit it lets through)");
                    if (s.Seconds <= 0f) problems.Add($"{me}: a shield needs \"seconds\" (it fails by itself after them, so a fight never sticks)");
                    if (s.BreakShare < 0f || s.BreakShare > 0.5f) problems.Add($"{me}: a shield's breakShare {s.BreakShare} (0 to 0.5)");
                    if (s.Times < 1 || s.Times > 5) problems.Add($"{me}: a shield's times {s.Times} (1 to 5)");
                    if (s.Until.Count == 0 && !b.WeakTo.Any(w => w.Breaks))
                        problems.Add($"{me}: a shield with nothing to break it (no \"until\", no weakTo that breaks): it only fails after its seconds");
                    foreach (BossCond c in s.Until) Cond(def, c.Mech, c.State, $"{me}: a shield's until", problems);
                    CheckWires(def, s.BreakWires, $"{me}: a shield's breakWires", problems);
                }
            }
            if (b.Scale < 0.5f || b.Scale > 3f) problems.Add($"{me}: the boss's scale {b.Scale} (0.5 to 3)");
            if (b.PerPlayer < 0f || b.PerPlayer > 2f) problems.Add($"{me}: the boss's perPlayer {b.PerPlayer} (0 to 2)");
            if (b.Fight != 0f && (b.Fight < 10f || b.Fight > 900f)) problems.Add($"{me}: the boss's fight {b.Fight} s (10 to 900: seconds of one player's hitting)");
            if (b.Armour <= 0f || b.Armour > 1f) problems.Add($"{me}: the boss's armour {b.Armour} (over 0 to 1: the share of each hit it takes)");
            if (b.Armour < 1f && !b.WeakTo.Any(w => w.Breaks)) problems.Add($"{me}: an armoured boss with no weakTo that breaks its armour");
            if (b.Look != null && b.Look.Range < 0f) problems.Add($"{me}: the boss's look has a range below 0");
            if (b.Intro != null)
            {
                BossIntro i = b.Intro;
                if (i.Wake < 0f || i.Seconds < 0f || i.LeapAt < 0f) problems.Add($"{me}: the boss's intro has a time below 0");
                if (i.Wake > i.Seconds) problems.Add($"{me}: the boss's intro wakes it at {i.Wake} s, after its end ({i.Seconds} s)");
                if (i.Seconds > 30f) problems.Add($"{me}: the boss's intro lasts {i.Seconds} s (30 at most: players wait through it)");
                if (i.Leap.HasValue)
                {
                    InRoom(def, i.Leap.Value, $"{me}: the boss's intro leap", problems);
                    if (i.LeapAt < i.Wake) problems.Add($"{me}: the boss's intro leaps at {i.LeapAt} s, before it wakes ({i.Wake} s)");
                }
                Timeline(def, i, $"{me}: the boss's intro", problems);
            }
            if (b.Death != null) Timeline(def, b.Death, $"{me}: the boss's death", problems);
            var ids = new HashSet<string>();
            foreach (BossAbility a in b.Abilities)
            {
                string what = $"{me}: ability {a.Id}";
                if (!ids.Add(a.Id ?? "")) problems.Add($"{me}: two abilities are called {a.Id}");
                if (!Kinds.Contains(a.Kind)) { problems.Add($"{what}: kind {a.Kind ?? "(none)"} (one of {string.Join(", ", Kinds)})"); continue; }
                if (a.Every < 2f) problems.Add($"{what}: every {a.Every} s (2 at least)");
                if (a.Warn < 0.5f && a.Kind != "wires") problems.Add($"{what}: a warning of {a.Warn} s (0.5 at least: players must see it coming)");
                if ((a.Kind == "slam" || a.Kind == "rain") && (a.Radius < 1f || a.Radius > 12f)) problems.Add($"{what}: radius {a.Radius} (1 to 12)");
                if (a.Kind == "charge" && (a.Width < 1f || a.Length < 4f || a.Length > 40f)) problems.Add($"{what}: a lane {a.Width} by {a.Length} (width 1 or more, length 4 to 40)");
                if (!DamageTypes.Contains(a.Damage)) problems.Add($"{what}: damage {a.Damage} ({string.Join(", ", DamageTypes)})");
                if (a.Scale < 0f || a.Scale > 5f) problems.Add($"{what}: scale {a.Scale} (0 to 5)");
                if (a.Kind == "summon" && (a.Adds.Count == 0 || a.Adds.Values.Sum() <= 0)) problems.Add($"{what}: a summon with no \"adds\"");
                foreach (string role in a.Adds.Keys.Where(r => !Roles.Contains(r))) problems.Add($"{what}: adds role {role}");
                if (a.Kind == "summon" && a.Points.Count == 0 && e.Points.Count < 2) problems.Add($"{what}: a summon with no \"points\" and no encounter points but the boss's");
                foreach (Vector3 p in a.Points) InRoom(def, p, $"{what}: point", problems);
                if (a.Kind == "wires" && a.Wires.Count == 0) problems.Add($"{what}: kind wires with no \"wires\"");
                CheckWires(def, a.Wires, what, problems);
                foreach (int ph in a.Phases ?? new List<int>())
                    if (ph < 0 || ph > b.Phases.Count) problems.Add($"{what}: phase {ph} (0 to {b.Phases.Count}: 0 is before the first)");
            }
            foreach (BossWeak w in b.WeakTo)
            {
                Cond(def, w.Mech, w.State, $"{me}: weakTo", problems);
                if (w.Share < 0f || w.Share > 0.5f) problems.Add($"{me}: weakTo {w.Mech}: share {w.Share} (0 to 0.5)");
                if (w.Area.x <= 0f || w.Area.y <= 0f || w.Area.z <= 0f) problems.Add($"{me}: weakTo {w.Mech}: an empty area");
            }
        }

        /// <summary>Across the pack: the final bosses (not "mini") must be its hardest fights. A boss's length is its "fight" (else its
        /// health times 30 s, a creature's worth), longer with armour (a quarter of each hit: about twice as long, the hammer's windows
        /// counted); a final boss must take at least twice as long as every mini boss.</summary>
        public static void CheckPack(Pack pack, List<string> problems)
        {
            var bosses = pack.Rooms.SelectMany(r => r.Mechs.Where(m => m.Encounter?.Boss != null).Select(m => new { Room = r.Name, m.Id, B = m.Encounter.Boss })).ToList();
            float Length(BossDef b) => (b.Fight > 0f ? b.Fight : b.Health * 30f) * (b.Armour < 1f ? 1f + (1f - b.Armour) * 1.4f : 1f);
            foreach (var big in bosses.Where(x => !x.B.Mini))
                foreach (var mini in bosses.Where(x => x.B.Mini && Length(big.B) < 2f * Length(x.B)))
                    problems.Add($"{pack.Name}: {big.Room} mechanism {big.Id}: its boss's fight (about {Length(big.B):0} s of one player's hitting) is under twice the mini boss {mini.Room}'s ({Length(mini.B):0} s): the final boss must be the hardest");
        }

        private static void Timeline(RoomDef def, BossTimeline t, string what, List<string> problems)
        {
            foreach (BossBeat beat in t.Beats)
            {
                if (beat.At < 0f) problems.Add($"{what}: a beat at {beat.At} s");
                if (beat.Shake < 0f || beat.Shake > 1f) problems.Add($"{what}: a beat's shake {beat.Shake} (0 to 1)");
                if (beat.Where.HasValue) InRoom(def, beat.Where.Value, $"{what}: a beat's where", problems);
                CheckWires(def, beat.Wires, $"{what}: a beat", problems);
            }
        }

        private static void CheckWires(RoomDef def, List<Wire> wires, string what, List<string> problems)
        {
            foreach (Wire w in wires)
            {
                MechDef target = def.Mechs.FirstOrDefault(o => o.Id == w.Target);
                if (target == null) { problems.Add($"{what} is wired to {w.Target}, which isn't a mechanism in its room"); continue; }
                string d = w.Do ?? "next";
                if (d == "next") continue;
                if (!d.StartsWith("set:") || !int.TryParse(d.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int s))
                    problems.Add($"{what}: a wire to {w.Target} does {d} (a boss's wires do \"next\" or \"set:n\")");
                else if (s < 0 || s >= Layouts.States(target)) problems.Add($"{what}: a wire sets {w.Target} to {s}, but it has {Layouts.States(target)} states");
            }
        }

        private static void Cond(RoomDef def, string mech, int state, string what, List<string> problems)
        {
            MechDef target = def.Mechs.FirstOrDefault(o => o.Id == mech);
            if (target == null) problems.Add($"{what} names {mech ?? "(none)"}, which isn't a mechanism in its room");
            else if (state < 0 || state >= Layouts.States(target)) problems.Add($"{what}: {mech} in state {state}, but it has {Layouts.States(target)} states");
        }

        private static void InRoom(RoomDef def, Vector3 p, string what, List<string> problems)
        {
            Vector3 half = def.Size * 0.5f;
            if (def.Size != Vector3.zero && (Math.Abs(p.x) > half.x || Math.Abs(p.y) > half.y || Math.Abs(p.z) > half.z))
                problems.Add($"{what} {p} is outside the room");
        }

        // ---- reading

        private static IEnumerable<BossBeat> Beats(JToken t) => Objects(t).Select(x =>
        {
            var beat = new BossBeat
            {
                At = F(x["at"], 0f),
                Sound = (string)x["sound"],
                Fx = (string)x["fx"],
                Where = OptVec(x["where"]),
                Shake = F(x["shake"], 0f),
                Banner = (bool?)x["banner"] ?? false,
                Message = (string)x["message"],
            };
            beat.Wires.AddRange(Wires(x["wires"]));
            return beat;
        });

        private static IEnumerable<Wire> Wires(JToken t) =>
            Objects(t).Select(w => new Wire { Target = (string)w["target"], Do = (string)w["do"] ?? "next" });

        private static IEnumerable<JObject> Objects(JToken t) => (t as JArray ?? new JArray()).OfType<JObject>();

        private static bool Num(JToken t) => t.Type == JTokenType.Integer || t.Type == JTokenType.Float;

        private static float F(JToken t, float fallback) => t != null && Num(t) ? (float)t : fallback;

        private static Vector3 Vec(JArray a) => new Vector3(a.Count > 0 ? (float)a[0] : 0f, a.Count > 1 ? (float)a[1] : 0f, a.Count > 2 ? (float)a[2] : 0f);

        private static Vector3? OptVec(JToken t) => t is JArray a ? Vec(a) : (Vector3?)null;

        private static Color Col(JToken t, Color fallback) =>
            t is JArray a && a.Count >= 3 ? new Color((float)a[0], (float)a[1], (float)a[2]) : fallback;
    }
}
