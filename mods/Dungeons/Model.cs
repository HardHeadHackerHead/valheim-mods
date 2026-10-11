using System.Collections.Generic;
using UnityEngine;

namespace Dungeons
{
    // The data every part of the mod shares: packs, rooms, doorways, and a laid-out dungeon. Plain classes, no game objects, so a layout can
    // be made and checked without building anything (and the same seed always gives the same one).

    /// <summary>A doorway in a room's own frame: where it is, which way points into the room, and its kind. Two rooms join where doorways
    /// of the same type meet face to face (positions equal, facings opposite), as the game's own rooms do (RoomConnection).</summary>
    internal class Doorway
    {
        public string Type = "";        // the game's RoomConnection.m_type ("" for the crypts, "dvergr", "stair"...), or our own
        public Vector3 At;              // in the room's frame
        public Vector3 Facing;          // points INTO the room (as the game's connections do)
        public bool Entrance;           // the room's way in (the game marks one per entrance room)
        public bool AllowDoor;          // a door or gate may stand here
        public string Id = "";          // our own rooms name their doorways ("north", "landing2"); the game's are numbered
    }

    /// <summary>One piece of one of our own rooms: a prefab from the game (build piece, prop, light), where and how it stands.</summary>
    internal class PartDef
    {
        public string Prefab;
        public Vector3 At;
        public Vector3 Euler;
        public Vector3 Scale = Vector3.one;
        public bool Saved;              // made once by the owner as a saved object (a chest, a spawner, a door), not part of the shell
        // (added in step 3, Runtime) a mechanism's part only: degrees a second it turns about its own x, y and z while its mechanism moves
        // (a gear, a winch: backwards when the mechanism goes back a state), or all the time in a mechanism with one state (machinery
        // that only turns). Seen, not saved: each game turns it by itself. JSON "spin": [x, y, z].
        public Vector3 Spin;
        // (added by triggers, TriggerModel.cs) a mechanism's part only: the states it shows in ("show": [1]: a brazier's flames, a
        // teleporter's glow); null: always
        public List<int> Show;
        // (added by triggers) a mechanism's part only: the states its own fire burns in ("lit": [1, 2]: its Fireplace's flame, light,
        // warmth and crackle, or its lights, particles and looping sounds; the model itself stays). null: always (a brazier's: lit only)
        public List<int> Lit;
    }

    // ---- step 2: moving parts and puzzles ----

    /// <summary>Where a mechanism's moving part stands in one of its states, in the mechanism's own frame.</summary>
    internal class Pose
    {
        public Vector3 At;
        public Vector3 Euler;
    }

    /// <summary>What using a lever (or standing on a plate) does to another mechanism.</summary>
    internal class Wire
    {
        public string Target;           // a mechanism's Id in the same room, or "step:<plan step id>" (the layout finds its room)
        public string Do = "next";      // "next" (one state on, wrapping), "set:<n>", or "hold" (a plate: state 1 while pressed, 0 after)
    }

    /// <summary>In one state of a mechanism, two of its room's doorways are joined through the room (a turning stair that links two
    /// landings, a rotating room facing a doorway, a bridge that is out). The layout's solver walks these.</summary>
    internal class Join
    {
        public int State;               // -1: in every state
        public string A, B;             // doorway ids of the same room
    }

    /// <summary>A mechanism: a thing in a room that moves between states (a gate, a turning stair, a platform, a rotating room, a floor that
    /// falls) or that players use to change others (a lever, a pressure plate). Its state is saved in the entrance's ZDO and its motion is
    /// worked out from that state and the server's clock, so every player sees it in the same place.</summary>
    internal class MechDef
    {
        public string Id;               // unique in its room ("stair1"); in a layout "<room index>.<id>"
        public string Kind;             // lever, plate, gate, door, platform, stair, rotator, floor
        public Vector3 At;              // in the room's frame
        public Vector3 Euler;
        public List<PartDef> Parts = new List<PartDef>();   // what moves (its look and colliders), in the mechanism's own frame
        public List<Pose> Poses = new List<Pose>();         // one per state: where the moving parts stand
        public int Start;               // its state when the dungeon is new
        public float Seconds = 1.5f;    // to move from one state to the next
        public float Cycle;             // > 0: goes to its next state by itself every Cycle seconds (server clock), for timing puzzles
        public string Doorway;          // a gate or door: the doorway id it closes in state 0 (open in state 1)
        public List<Wire> Wires = new List<Wire>();         // a lever or plate: what using it does
        public List<Join> Joins = new List<Join>();         // which doorways it joins in which state
        public string ReachedFrom;      // a lever or plate: a doorway id on the side of the room it's reached from (null: anywhere in the room)
        public bool Once;               // (added in step 3, Layout) a lever or plate used once only (JSON "once"; its wires all "set:n")
        public EncounterDef Encounter;  // (added in step 3, Runtime) kind "encounter": the fight (its Wires fire once, when it's cleared)
        public float Health;            // (added in step 3, Runtime) kind "breakable": the damage (pickaxe or blunt) it takes to break
        public string Pair;             // (added in step 3, Runtime) kind "shortcut": the name both doors of a pair share
        public bool Deep;               // (added in step 3, Runtime) kind "shortcut": the far door, opened from its front (+z) side
        public List<int> CycleIn;       // (added in step 3, Runtime) its Cycle runs only while it's set to one of these states ("cycleIn": [1]:
                                        // a crusher still until a wire sets it to 1); null or empty: always, as before
        public List<float> SpinScale;   // (added in step 3, Runtime) its parts' Spin times this in each state ("spinScale": [1, 2.5])
        public BlockDef Block;          // (added by triggers, TriggerModel.cs) a building block's own fields (pedestal, brazier, pushblock,
                                        // trap, teleporter, lock, checkpoint), and any mechanism's reset "group"; null: none
    }

    /// <summary>A room a pack can use: one of the game's (by prefab name, from DungeonDB) or one of the pack's own (a list of parts).</summary>
    internal class RoomDef
    {
        public string Name;             // unique in the pack
        public string GamePrefab;       // the game's room prefab name (DungeonDB), or null for our own room
        public Vector3 Size;            // the room's box, centred on its origin (the game's Room.m_size)
        public List<Doorway> Doorways = new List<Doorway>();
        public List<PartDef> Parts = new List<PartDef>();   // our own rooms only
        public float Weight = 1f;       // how often it's picked among those that fit
        public bool Entrance;           // can start a dungeon
        public bool EndCap;             // closes a doorway nothing else joins (the game's end caps)
        public int Max = 0;             // at most this many in one dungeon (0: no limit)
        public int MinDepth = 0;        // rooms from the entrance before it may appear
        public List<string> Tags = new List<string>();   // what the plan can ask for: "lever", "gate", "vault", "boss", "chest", "stairs"...
        public List<MechDef> Mechs = new List<MechDef>();
        public List<Join> Inside = new List<Join>();     // which of its doorways are joined through it whatever the mechanisms do (empty: all)
        public string Music;            // (added in step 3, Runtime) the game's music while a player is in it (MusicMan's name), or null
        public List<TriggerDef> Triggers = new List<TriggerDef>();   // (added by triggers, TriggerModel.cs) its "triggers"
    }

    /// <summary>One step of a pack's plan: a room with this tag must be on the way from the entrance, after the steps before it. A step that
    /// "locks" names a doorway the dungeon closes until an earlier step's thing ("key") opens it.</summary>
    internal class PlanStep
    {
        public string Tag;              // a RoomDef tag
        public string Id;               // this step's name, for keys and locks ("lever1")
        public string OpensWith;        // the Id of an earlier step that opens this one (a lever opens a gate), or null
    }

    /// <summary>A kind of dungeon: its rooms, how big, its plan, its look, its creatures and loot.</summary>
    internal class Pack
    {
        public string Name;             // "testcrypt", "shiftinghalls"
        public string Title;            // "Test Crypt"
        public string Environment = "Crypt";   // the game's environment name for inside (EnvZone)
        public int MinRooms = 6, MaxRooms = 12;
        public List<RoomDef> Rooms = new List<RoomDef>();
        public List<PlanStep> Plan = new List<PlanStep>();
        public List<string> Creatures = new List<string>();   // prefab names its spawn points may use (later)
        public List<string> Loot = new List<string>();        // item prefab names its chests may hold (later)
        public string Theme;            // the game's Room.Theme its rooms' random props use (null: the one its game rooms share)
        public string EntranceLook;     // what its entrance looks like in the world (a look the core knows by name; null: the default)
        public Dictionary<string, string> Doors = new Dictionary<string, string>();   // doorway type -> the game's door prefab placed where
                                                                                       // two rooms join at doorways that allow a door
        public float DoorChance = 0.5f; // how often such a joint gets a door (seeded)
        public string Music;            // (added in step 3, Runtime) the game's music inside where a room names none (null: the game's own)
    }

    /// <summary>A link between two placed rooms' doorways.</summary>
    internal struct Link
    {
        public int Room, Doorway, OtherRoom, OtherDoorway;
    }

    /// <summary>A room placed in a laid-out dungeon, in the dungeon's own frame (the entrance room at the origin).</summary>
    internal class PlacedRoom
    {
        public int Index;
        public RoomDef Def;
        public Vector3 Pos;
        public float Yaw;               // multiples of 90
        public int Depth;               // rooms from the entrance
        public string Step;             // the plan step it fulfils, or null
        public Vector3 DoorwayPos(int i) => Pos + Turn(Def.Doorways[i].At, Yaw);
        public Vector3 DoorwayFacing(int i) => Turn(Def.Doorways[i].Facing, Yaw);

        /// <summary>v turned about the up axis by yaw degrees, the way Unity turns a transform (Quaternion.Euler(0, yaw, 0) * v), in plain
        /// arithmetic: Quaternion.Euler is the engine's own code, so a layout made with it couldn't be tested outside the game.</summary>
        public static Vector3 Turn(Vector3 v, float yaw)
        {
            double r = yaw * System.Math.PI / 180.0, c = System.Math.Cos(r), s = System.Math.Sin(r);
            return new Vector3((float)(v.x * c + v.z * s), v.y, (float)(-v.x * s + v.z * c));
        }
    }

    /// <summary>A whole laid-out dungeon: the same pack and seed always give the same one.</summary>
    internal class Layout
    {
        public Pack Pack;
        public int Seed;
        public List<PlacedRoom> Rooms = new List<PlacedRoom>();
        public List<Link> Links = new List<Link>();
        public List<Link> Locks = new List<Link>();   // doorways closed by a plan step until opened (Room/Doorway: the gate's side)
        public Bounds Box;              // everything, in the dungeon's frame
        public List<string> Problems = new List<string>();   // why a seed failed, when it did
        public List<Link> Doors = new List<Link>();      // joints that get a door (the pack's Doors), seeded
        public List<string> Solution = new List<string>(); // a puzzle dungeon: one order of uses that reaches the end ("3.lever1", ...),
                                                           // found by the solver; proves it can be solved
        public bool Ok => Problems.Count == 0;
    }

    /// <summary>(Added in step 3, Runtime.) A fight room's encounter (a mechanism of kind "encounter"): when it starts, its locks shut and
    /// waves of the land's creatures come at its points; when the last is dead it's cleared and its mechanism's wires fire.</summary>
    internal class EncounterDef
    {
        public Vector3 Area = new Vector3(16f, 8f, 16f);   // a box round the mechanism's At (its frame): who's in it is in the fight
        public string StartOn = "wire";  // "enter" (a living player steps in), "wire" (a lever's or plate's wire "do": "start"), "open" (the chest at Chest)
        public Vector3 Chest;            // for "open": where the chest stands (room frame)
        public List<string> Lock = new List<string>();      // mechanism ids in the room: to state 0 while it fights, back as they were after a wipe
        public List<Dictionary<string, int>> Waves = new List<Dictionary<string, int>>();   // role ("melee", "ranged", "flying", "elite") -> count;
                                                                                             // none: guard mode (cleared when the area is empty of creatures)
        public int Level = 1, EliteLevel = 3;                // the game's levels (1 = no star), capped to the land
        public List<Vector3> Points = new List<Vector3>();  // where they come (room frame, on the floor)
        public float Pause = 4f, Timeout = 40f;              // after a wave's last death; the longest a wave holds the next one back
        public int Siege;                // > 0: a siege, cleared after this many kills; waves come only while HoldBy (a plate) is pressed
        public string HoldBy;
        public string LockLever;         // a lever in the room that can't be pulled until it's cleared
        public string Music;             // its music while it fights (null: a combat track, or a boss track for a boss)
        public BossDef Boss;             // a boss fight: one creature instead of waves
    }

    /// <summary>(Added in step 3, Runtime.) An encounter's boss: the land's elite at the level cap, harder to kill by Health, with the game's
    /// boss bar and a name; its phases fire wires and bring adds as its health falls.</summary>
    internal partial class BossDef
    {
        public string Name;              // what its bar says (a $ key)
        public string Role = "elite";    // the creature picked for it (Builder.EncounterCreature)
        public float Health = 4f;        // it takes this many times the damage to kill
        public List<BossPhase> Phases = new List<BossPhase>();
    }

    internal partial class BossPhase
    {
        public float At;                 // its health fraction (0.66: at two thirds)
        public List<Wire> Wires = new List<Wire>();                       // fired then (mechanisms of the room)
        public Dictionary<string, int> Adds = new Dictionary<string, int>();   // creatures that come then, by role (at the encounter's points)
    }
}
