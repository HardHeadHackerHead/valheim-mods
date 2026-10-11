using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// The boss toolkit's Claude Tools commands (Tools.cs passes "dungeon boss ..." here):
    ///   dungeon boss [id]                     every boss fight of the dungeon: its room, state, phase, shield, enrage, the ability being
    ///                                         cast, and its boss's health (loaded here or not)
    ///   dungeon boss go &lt;room&gt; [id]           (a cheat) the player to 2.5 m outside that room's way in (in the room before it, so
    ///                                         stepping in starts its fight), on the floor there, facing in: a room's name
    ///                                         (warden, guardian) or index. Only inside the dungeon, only when it's built here and a
    ///                                         floor is found under the spot (a ray down), by the mod's own teleport (no fall counted):
    ///                                         never into the air
    ///   dungeon boss hurt &lt;percent&gt; [room] [id] (a cheat) the boss loses that share of its whole health, through its shield and armour
    ///                                         (phases follow as in a real fight)
    ///   dungeon boss cast &lt;ability&gt; [room] [id] (a cheat) its next use of that ability is now
    /// </summary>
    internal static class BossTools
    {
        public const string Usage = "boss [id] | boss go <room> [id] | boss hurt <percent> [room] [id] | boss cast <ability> [room] [id]";

        public static IEnumerator Command(string[] args, Action<JObject> output, Action<string> error, Func<string[], int, Action<string>, Site> find)
        {
            string verb = args.Length > 1 ? args[1].ToLowerInvariant() : "";
            bool cheat = verb == "go" || verb == "hurt" || verb == "cast";
            if (cheat && !DHack.Shared.TestCommands.CheatsAllowed()) { error(DHack.Shared.TestCommands.Refusal("dungeon boss " + verb)); yield break; }
            if (cheat && args.Length < 3) { error("dungeon " + Usage); yield break; }
            // [room] is optional before [id]: a word that names a boss room of the dungeon is the room, anything after it the id
            // (hurt and cast: found first without an id, the dungeon the player is in, then sorted out below)
            Site site = find(args, verb == "go" ? 3 : cheat ? 99 : 1, error);
            if (site == null) yield break;
            if (site.Mechs == null || site.Layout == null) { error(site.Ready ? "this dungeon has no mechanisms" : "it isn't built here yet"); yield break; }
            switch (verb)
            {
                case "go": { IEnumerator run = Go(site, args[2], output, error); while (run.MoveNext()) yield return run.Current; yield break; }
                case "hurt":
                case "cast":
                {
                    string room = args.Length > 3 ? args[3] : null;
                    if (room != null && site.Mechs.BossMech(room) == null)
                    {
                        // (not a room: it was the id)
                        Site other = find(args, 3, error);
                        if (other == null) yield break;
                        site = other;
                        room = null;
                    }
                    else if (room != null && args.Length > 4) { site = find(args, 4, error); if (site == null) yield break; }
                    Mech m = site.Mechs.BossMech(room);
                    if (m == null) { error("no boss fight " + (room != null ? "in room " + room : "going on here") + " (dungeon boss lists them)"); yield break; }
                    if (!site.IsOwner)
                    {
                        if (site.View == null) { error("its entrance isn't loaded here (the player is far inside it): go nearer the entrance"); yield break; }
                        site.View.ClaimOwnership();
                    }
                    string why = verb == "hurt" ? site.Mechs.BossHurtCommand(m, args[2]) : site.Mechs.BossCastCommand(m, args[2]);
                    if (why != null) { error(why); yield break; }
                    output(site.Mechs.BossReport());
                    yield break;
                }
                default: output(site.Mechs.BossReport()); yield break;
            }
        }

        /// <summary>The player to 2.5 m outside a room's way in, on the floor found there; once the teleport has landed, where the player's feet
        /// are against that floor (a wrong landing shows at once).</summary>
        private static IEnumerator Go(Site site, string name, Action<JObject> output, Action<string> error)
        {
            Player p = Player.m_localPlayer;
            if (p == null || p.IsDead()) { error("no living player here"); yield break; }
            if (!site.Ready || !site.Contains(p.transform.position)) { error("go inside the dungeon first (dungeon enter), and wait until it's built"); yield break; }
            PlacedRoom room = int.TryParse(name, out int index) && index >= 0 && index < site.Layout.Rooms.Count ? site.Layout.Rooms[index]
                : site.Layout.Rooms.FirstOrDefault(r => string.Equals(r.Def?.Name, name, StringComparison.OrdinalIgnoreCase));
            if (room?.Def == null) { error($"no room {name} in this layout (dungeon map lists them)"); yield break; }
            // the doorway it's entered by: the one joined to the room before it (its lowest depth neighbour), else its first
            int door = -1;
            foreach (Link l in site.Layout.Links)
            {
                int mine = l.Room == room.Index ? l.Doorway : l.OtherRoom == room.Index ? l.OtherDoorway : -1;
                int other = l.Room == room.Index ? l.OtherRoom : l.OtherRoom == room.Index ? l.Room : -1;
                if (mine >= 0 && other >= 0 && site.Layout.Rooms[other].Depth < room.Depth) { door = mine; break; }
            }
            if (door < 0) door = 0;
            if (room.Def.Doorways.Count == 0) { error($"room {name} has no doorway"); yield break; }
            Vector3 at = site.Origin + room.DoorwayPos(door), inward = room.DoorwayFacing(door);
            // just outside it, in the room before (stepping in is what starts a fight that starts on "enter")
            Vector3 spot = at - inward * 2.5f;
            if (!Physics.Raycast(spot + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3.5f, LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle"), QueryTriggerInteraction.Ignore))
            {
                error($"no floor found 2.5 m outside room {room.Def.Name}'s way in: not moved");
                yield break;
            }
            Vector3 to = hit.point + Vector3.up * 0.1f, from = p.transform.position;
            Safety.OurTeleport();
            p.TeleportTo(to, Quaternion.LookRotation(Vector3.ProjectOnPlane(inward, Vector3.up).normalized), false);
            // wait for it to land (the teleport, then a second on the ground), then say where the feet are against the floor
            float until = Time.time + 15f;
            yield return null;
            while (p != null && p.IsTeleporting() && Time.time < until) yield return null;
            float settle = Time.time + 1f;
            while (Time.time < settle) yield return null;
            if (p == null) { error("the player is gone"); yield break; }
            Vector3 now = p.transform.position;
            float off = now.y - hit.point.y;
            bool ok = Mathf.Abs(off) < 1f && Vector2.Distance(new Vector2(now.x, now.z), new Vector2(to.x, to.z)) < 3f && !p.IsDead();
            output(new JObject
            {
                ["moved"] = $"to 2.5 m outside room {room.Index} {room.Def.Name}'s doorway {room.Def.Doorways[door].Id}, facing in: step in to start its fight",
                ["from"] = V(from), ["to"] = V(to), ["up"] = Math.Round(to.y - from.y, 2),
                ["landed"] = V(now), ["feetOverFloor"] = Math.Round(off, 2), ["health"] = Math.Round(p.GetHealth(), 1),
                ["result"] = ok ? "landed on the floor" : "WRONG LANDING: the feet aren't on the floor found (or the player died): check now",
            });
        }

        private static JArray V(Vector3 v) => new JArray(Math.Round(v.x, 2), Math.Round(v.y, 2), Math.Round(v.z, 2));
    }

    internal partial class MechSet
    {
        /// <summary>A boss fight's encounter: in the room named (or of that index), else the first one fighting, else the first.</summary>
        public Mech BossMech(string room)
        {
            List<Mech> bosses = All.Where(m => m.Def.Encounter?.Boss != null).ToList();
            if (!string.IsNullOrEmpty(room))
                return bosses.FirstOrDefault(m => m.Room.ToString() == room || string.Equals(Layout.Rooms[m.Room].Def?.Name, room, StringComparison.OrdinalIgnoreCase));
            return bosses.FirstOrDefault(m => EncounterOf(m).State == EncounterRecord.Fighting) ?? bosses.FirstOrDefault();
        }

        /// <summary>The boss command: every boss fight here.</summary>
        public JObject BossReport()
        {
            ReadBosses();
            var list = new JArray();
            double now = MechClock.Now;
            foreach (Mech m in All.Where(x => x.Def.Encounter?.Boss != null))
            {
                BossDef d = m.Def.Encounter.Boss;
                EncounterRecord er = EncounterOf(m);
                _bosses.TryGetValue(m.Key, out BossRecord b);
                Character boss = Spawned(m).FirstOrDefault(c => IsBoss(c) && (b == null || OfFight(c, b)));
                var o = new JObject
                {
                    ["key"] = m.Key, ["room"] = Layout.Rooms[m.Room].Def?.Name, ["name"] = d.Name,
                    ["state"] = er.State == EncounterRecord.Idle ? "idle" : er.State == EncounterRecord.Fighting ? "fighting" : "cleared",
                    ["abilities"] = string.Join(", ", d.Abilities.Select(a => $"{a.Id} ({a.Kind}, phases {(a.Phases == null ? "by use" : string.Join("/", a.Phases))})")),
                    ["phases"] = string.Join(", ", d.Phases.Select(p => $"{p.At:P0}{(p.Shield != null ? " shield" : "")}{(p.Enrage > 1f ? " enrage" : "")}")),
                };
                if (b != null)
                {
                    o["fight"] = new JObject
                    {
                        ["seconds"] = Math.Round(now - b.Start, 1), ["woke"] = b.Woke, ["phase"] = b.Phase,
                        ["shield"] = b.Shield >= 0 ? $"up (phase {b.Shield + 1}, {now - b.ShieldAt:0} s)" : "none",
                        ["armourOffFor"] = b.ArmourOff > now ? Math.Round(b.ArmourOff - now, 1) : 0.0,
                        ["enrage"] = b.Enrage,
                        ["casting"] = b.Cast >= 0 && b.Cast < d.Abilities.Count ? $"{d.Abilities[b.Cast].Id} ({now - b.CastAt:0.0} of {d.Abilities[b.Cast].Warn} s)" : "nothing",
                        ["next"] = string.Join(", ", d.Abilities.Select((a, i) => i < b.Next.Count && b.Next[i] > 0 ? $"{a.Id} in {Math.Max(0, b.Next[i] - now):0} s" : $"{a.Id} -")),
                        ["deadFor"] = b.DeadAt > 0 ? Math.Round(now - b.DeadAt, 1) : 0.0,
                    };
                }
                if (boss != null)
                    o["boss"] = new JObject
                    {
                        ["creature"] = Utils.GetPrefabName(boss.gameObject), ["health"] = $"{boss.GetHealthPercentage():P0}",
                        ["level"] = boss.GetLevel(), ["toughness"] = BossToughness(boss.GetComponent<ZNetView>()?.GetZDO()),
                        ["effectiveHealth"] = Math.Round(boss.GetMaxHealth() * BossToughness(boss.GetComponent<ZNetView>()?.GetZDO())),
                        ["soloSeconds"] = Math.Round(boss.GetMaxHealth() * BossToughness(boss.GetComponent<ZNetView>()?.GetZDO()) / PlayerDps(Site) / Mathf.Clamp(d.Armour, 0.05f, 1f)),
                        ["soloNote"] = $"its whole health at the land's {PlayerDps(Site):0} damage a second, through its armour all the time (its hammer windows and shield breaks make it shorter)",
                        ["at"] = new JArray(Math.Round(boss.transform.position.x, 1), Math.Round(boss.transform.position.y, 1), Math.Round(boss.transform.position.z, 1)),
                        ["owner"] = boss.GetComponent<ZNetView>()?.GetZDO()?.GetOwner().ToString(),
                    };
                list.Add(o);
            }
            return new JObject { ["dungeon"] = Site.Id, ["owner"] = Site.IsOwner, ["bosses"] = list };
        }

        /// <summary>The hurt command: a share of the boss's whole health, through its shield and armour (null: done; else why not).</summary>
        public string BossHurtCommand(Mech m, string percent)
        {
            if (!float.TryParse(percent.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float p) || p <= 0f || p > 100f)
                return "a share from 1 to 100 (percent of its whole health)";
            BossRecord fight = BossRecordOf(m);
            Character boss = Spawned(m).FirstOrDefault(c => IsBoss(c) && OfFight(c, fight));
            if (fight == null || boss == null) return "its boss isn't alive and loaded here (start the fight, and stand in its room)";
            Blow(boss, p / 100f, false);
            Plugin.Log.LogInfo($"Dungeon {Site.Id}: boss {m.Key}: the hurt command takes {p:0.#}% of its health");
            return null;
        }

        /// <summary>The cast command: that ability's next use is now (it's cast at the owner's next look, if it can be: its phase, nothing
        /// being cast, not leaping, not held by a shield for a slam or charge).</summary>
        public string BossCastCommand(Mech m, string ability)
        {
            BossDef d = m.Def.Encounter.Boss;
            int i = d.Abilities.FindIndex(a => string.Equals(a.Id, ability, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return $"no ability {ability} (it has {string.Join(", ", d.Abilities.Select(a => a.Id))})";
            BossRecord b = BossRecordOf(m);
            if (b == null || EncounterOf(m).State != EncounterRecord.Fighting) return "its fight isn't going on (dungeon set <key> 1 starts it)";
            if (!Usable(d, b.Phase).Contains(i)) return $"{ability} isn't used in phase {b.Phase} (its phases: {(d.Abilities[i].Phases == null ? "by the phases' use" : string.Join("/", d.Abilities[i].Phases))})";
            while (b.Next.Count < d.Abilities.Count) b.Next.Add(0.0);
            b.Next[i] = MechClock.Now - 0.01;
            WriteBoss(m, b, $"the cast command: {ability} now");
            return null;
        }
    }
}
