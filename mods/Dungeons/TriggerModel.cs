using System.Collections.Generic;
using UnityEngine;

namespace Dungeons
{
    // The data of the trigger-and-action system and of the building blocks (pedestal, brazier, pushblock, trap, teleporter, lock,
    // checkpoint) and key items: plain classes, no game types, so the offline harness (tools/dungeons) reads and solves them too. The pack
    // format is in PackReaderTriggers.cs; the runtime in TriggerSet.cs and Block*.cs; the solver's view in TriggerSolve.cs.
    // No look is built in: every block's look is its parts (the pack's), every sound a game prefab the pack names.

    /// <summary>The event kinds a trigger can wait for ("on": [{"event": ...}]).</summary>
    internal static class TriggerEvents
    {
        public const string Enter = "enter", Leave = "leave", State = "state", Lever = "lever", Press = "press", Release = "release",
                            Kills = "kills", Place = "place", Take = "take", Timer = "timer", Lit = "lit", Out = "out", Broken = "broken",
                            Cleared = "cleared", Signal = "signal", Counter = "counter";

        public static readonly string[] All = { Enter, Leave, State, Lever, Press, Release, Kills, Place, Take, Timer, Lit, Out, Broken, Cleared, Signal, Counter };
    }

    /// <summary>The action verbs ("do": [{"set": ...}]): each action is an object whose one verb key names it.</summary>
    internal static class TriggerActions
    {
        public const string Set = "set", Next = "next", Start = "start", Spawn = "spawn", Sound = "sound", Message = "message", Music = "music",
                            Counter = "counter", Reset = "reset", Timer = "timer", Signal = "signal", Item = "item";

        public static readonly string[] All = { Set, Next, Start, Spawn, Sound, Message, Music, Counter, Reset, Timer, Signal, Item };
    }

    /// <summary>A trigger on a room: when one of its events happens (or all of them in order, for a sequence) and its conditions hold, its
    /// actions run. On the entrance's owner only; what it has done is saved in the entrance's ZDO (TriggerState), so it's the same for every
    /// player and survives relogs. Once only (until the dungeon resets) unless Repeat.</summary>
    internal class TriggerDef
    {
        public string Id;                                   // unique in its room ("t0", "t1"... when the pack gives none)
        public List<TriggerEvent> On = new List<TriggerEvent>();
        public string When = "all";                         // "all" (every condition), "any" (one condition), "sequence" (On in order)
        public float Within;                                // a sequence: seconds from its first event to its last (0: no limit)
        public List<TriggerCondition> If = new List<TriggerCondition>();
        public List<TriggerAction> Do = new List<TriggerAction>();
        public bool Repeat;                                 // fires every time; else once, until a reset (or a "reset" of its group)
        public string Group;                                // its reset group (room-local, or "@name" for the whole dungeon)
        public bool SolverIgnore;                           // "solver": "ignore": not needed for the solution, so the solver leaves it out
    }

    /// <summary>One event a trigger waits for. Mechanisms are named as wires name them: an id in the same room, or "step:&lt;id&gt;.&lt;mech&gt;".</summary>
    internal class TriggerEvent
    {
        public string Kind;                                 // TriggerEvents
        public string Mech;                                 // state, lever, press, release, place, take, lit, out, broken, cleared
        public int State = -1;                              // state: the state it goes to (-1: any change)
        public Vector3 At, Size;                            // enter, leave, kills: the area, a box in the room's frame (Size zero: the room)
        public string From;                                 // enter: the doorway the area is reached from (the solver; null: anywhere)
        public int Count = 1;                               // kills: how many deaths in the area
        public string Item;                                 // place, take: only this item (null: any)
        public string Name;                                 // timer, signal, counter: its name (room-local, or "@name")
        public float Every;                                 // timer without a name: every this many seconds of the server's clock
    }

    /// <summary>A condition checked when an event fires.</summary>
    internal class TriggerCondition
    {
        public string Mech;                                 // a mechanism in a state (or not in one)
        public int State = -1, Not = -1;
        public string Counter;                              // a counter compared
        public int? AtLeast, AtMost, EqualTo;
    }

    /// <summary>One action (TriggerActions); Target is what the verb names.</summary>
    internal class TriggerAction
    {
        public string Verb;
        public string Target;                               // a mechanism, encounter, creature role, sound prefab, $key, music, counter, group, timer, signal, item
        public int To;                                      // set: the state; counter "set"
        public int Add;                                     // counter "add"
        public bool HasSet;                                 // counter: "set" given (else "add")
        public float After, Every;                          // timer: once after, or every (seconds)
        public bool Stop;                                   // timer: stopped
        public int Count = 1;                               // spawn, item: how many
        public int Level = 1;                               // spawn: the game's level (1: no star), capped to the land
        public List<Vector3> At = new List<Vector3>();      // spawn, sound, item: where (room frame; none: the trigger's place)
        public string ToWhom = "room";                      // message: "room" (players in the room) or "dungeon" (everyone inside)
    }

    /// <summary>The fields of the building blocks, on their MechDef (MechDef.Block). One class for all of them; each kind reads its own.</summary>
    internal class BlockDef
    {
        public List<string> Fx = new List<string>();        // the game's sound or effect prefabs played when it acts (lit, fires, teleports...)
        public string Group;                                // its reset group ("group": any mechanism may have one; {"reset": group})

        // pedestal: states 0 empty, 1 holds its Item, 2 holds another of Accepts
        public string Item;                                 // the right item (a pack key item's name, or a game item prefab)
        public List<string> Accepts = new List<string>();   // every item it takes (Item among them)
        public Vector3 ItemAt;                              // where the item shows (the mechanism's frame)

        // brazier: states 0 out, 1 lit
        public float Burn;                                  // seconds it stays lit (0: until something puts it out)
        public bool Lightable = true;                       // players light it (a torch in hand, E; or a fire hit)

        // pushblock: states are the grid's floor cells, in reading order
        public float Cell = 2f;                             // metres per cell
        public List<string> Rows = new List<string>();      // the grid seen from above, +z up: '.' floor, '#' wall, 'S' the start
        public List<Vector2Int> Cells = new List<Vector2Int>();   // each state's (column, row)

        // trap: states 0 off, 1 on
        public string Damage = "pierce";                    // blunt, slash, pierce, fire, frost, lightning, poison, spirit
        public float Scale = 1f;                            // times the land's trap damage
        public float Every, For = 1f, Offset;               // on: fires For seconds every Every seconds (server clock); Every 0: once when set on
        public Vector3 AreaAt, AreaSize;                    // where it hurts (the mechanism's frame; size zero: its parts' box)
        public List<Pose> TrapPoses = new List<Pose>();     // [rest, fired]: how its parts move (not states: the trap's own clock)

        // teleporter (MechDef.Pair names the pair): states 0 off, 1 on
        public bool Send = true;                            // this end sends (false: arrival only, a one-way pair)
        public bool Step;                                   // stepping on it sends (else E)

        // lock (a gate a key opens: MechDef.Doorway closed in state 0)
        public string Key;                                  // the pack key item it takes (one, used up)
    }

    /// <summary>A pack's dungeon-only item ("items": {"smallkey": {...}}): a copy of a game item's look under our name, worth nothing, taken
    /// from a player's inventory when they're outside the dungeon it came from.</summary>
    internal class KeyItemDef
    {
        public string Name;                                 // the pack's name for it ("smallkey")
        public string Prefab;                               // quaddungeon_key_&lt;pack&gt;_&lt;name&gt;
        public string Look;                                 // the game item copied ("CryptKey")
        public string Title, Description;                   // $keys or plain text
    }
}
