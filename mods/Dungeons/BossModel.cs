using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Dungeons
{
    // The boss toolkit's data (an encounter's "boss" in a pack): read by PackReaderBoss.cs, run by BossFight.cs (the owner) and
    // BossShow.cs (every game). Plain classes, no game objects, so the harness can read and check them outside the game.

    /// <summary>The boss toolkit's fields of an encounter's boss (the rest of BossDef is in Model.cs).</summary>
    internal partial class BossDef
    {
        public string Title;            // the line under its name on the banner (a $ key), or null
        public bool Mini;               // a mini boss: its banner without the game's stinger, a smaller moment
        public string Trophy;           // the item it drops where it falls: null, the Warden's trophy (a mini boss: none); "" none
        public float Scale = 1f;        // its size (1: the creature's own); seen on every game
        public float PerPlayer = 0.5f;  // its health grows by this share for each player inside past the first
        public float Fight;             // > 0: its health is set so one player of the land's tier, hitting it all the time with no
                                        // armour or shield, needs this many seconds (BossFight.PlayerDps); "health" is then ignored
        public float Armour = 1f;       // the share of each hit it takes when no shield is up (0.25: armoured); a weak hit takes it off
        public float ArmourOff = 10f;   // seconds its armour stays off after a weak hit
        public BossLook Look;           // its glow and aura, or null
        public BossIntro Intro;         // how it wakes, or null (it comes at once, as before)
        public BossTimeline Death;      // what happens as it falls, or null
        public List<BossAbility> Abilities = new List<BossAbility>();
        public List<BossWeak> WeakTo = new List<BossWeak>();
        public float Unstick = 5f;      // seconds without moving 1 m, away from its target, before it leaps to it (0: never)

        /// <summary>Every wire the fight can fire (phases, intro and death beats, shields breaking, abilities): what the solver and the
        /// audit must see, so a fight that opens a gate is counted.</summary>
        public IEnumerable<Wire> AllWires()
        {
            IEnumerable<Wire> all = Phases.SelectMany(p => p.Wires.Concat(p.Shield?.BreakWires ?? new List<Wire>()));
            if (Intro != null) all = all.Concat(Intro.Beats.SelectMany(b => b.Wires));
            if (Death != null) all = all.Concat(Death.Beats.SelectMany(b => b.Wires));
            return all.Concat(Abilities.SelectMany(a => a.Wires));
        }
    }

    /// <summary>The boss toolkit's fields of a boss phase (At, Wires and Adds are in Model.cs).</summary>
    internal partial class BossPhase
    {
        public List<string> Use;        // the abilities (ids) it uses from this phase on; null: as before
        public BossShield Shield;       // a shield that comes up with the phase, or null
        public Vector3? Leap;           // it leaps there (room frame, on a floor) as the phase starts
        public float Enrage;            // > 1: it moves this much faster and hits this much harder from now on
        public float HealTo;            // > 0: a second wind: as the phase starts it recovers to this share of its health (a surge of
                                        // light and sound everyone sees); give the phase a low "at" (0.3) and this higher (0.55)
        public string Banner;           // a big banner (a $ key) for the players in the fight
        public string Message;          // a centre message for them
        public string Sound;            // a game sound or effect at the boss
        public float Shake;             // the camera's shake (0..1)
    }

    /// <summary>How it looks: a light, a tint of its glow and an aura (a game effect prefab with particles, kept on it).</summary>
    internal class BossLook
    {
        public Color Light = new Color(0.45f, 0.75f, 1f);
        public float Range = 6f;        // the light's reach (0: no light)
        public string Aura;             // a game effect prefab (vfx_Burning_blue): its particles stay on the boss
        public Color Enraged = new Color(1f, 0.45f, 0.2f);   // the light once it's enraged
    }

    /// <summary>A moment in a timeline: at At seconds, wires fire (the owner), and every game near plays its sound and effect, shakes, and
    /// shows its banner or message.</summary>
    internal class BossBeat
    {
        public float At;
        public List<Wire> Wires = new List<Wire>();
        public string Sound;            // a game sound or effect prefab
        public string Fx;               // another, played at the same place
        public Vector3? Where;          // room frame; null: at the boss (or its point before it wakes)
        public float Shake;             // 0..1
        public bool Banner;             // the boss's banner: its name and title
        public string Message;          // a centre message (a $ key; "{0}" is its name)
    }

    internal class BossTimeline
    {
        public float Seconds;           // how long it lasts (death: the vault opens, the fight is cleared, after this)
        public List<BossBeat> Beats = new List<BossBeat>();
    }

    /// <summary>The intro: the fight starts (the gates shut), beats play, the boss wakes at Wake (spawned there with a burst), can leap to
    /// Leap at LeapAt, and can't be hurt or move until Seconds.</summary>
    internal class BossIntro : BossTimeline
    {
        public float Wake;              // when it appears
        public Vector3? Leap;           // where it leaps to (room frame), or null
        public float LeapAt;            // when it leaps
    }

    /// <summary>A telegraphed special attack (kinds below), used on a cooldown in the phases that list it. A red ward ring (a lane for a
    /// charge) fills on the floor for Warn seconds, then it lands: damage fitted to the land as traps' (Damage type, Scale), pushing
    /// players away; a dodge-roll gets out of it, a block takes it.</summary>
    internal class BossAbility
    {
        public string Id;
        public string Kind;             // slam (round the boss), rain (under each player), charge (a dash down a lane to a player),
                                        // summon (adds at points), wires (the room's mechanisms)
        public float Every = 10f;       // seconds between its uses (a little more or less each time)
        public float First = -1f;       // seconds after it becomes usable to its first use (-1: 60% of Every)
        public float Warn = 1.6f;       // the telegraph's seconds
        public float Radius = 5f;       // slam and rain: the ring's radius
        public float Width = 3f;        // charge: the lane's width
        public float Length = 18f;      // charge: the longest dash
        public string Damage = "blunt";
        public float Scale = 2f;        // times the land's trap damage
        public float Push = 30f;        // the game's push force
        public int Count;               // rain: rings more than one under each player (round the boss)
        public List<int> Phases;        // the phases it's used in (0: before the first); null: as phases' "use" say, or all
        public Dictionary<string, int> Adds = new Dictionary<string, int>();   // summon: creatures by role
        public List<Vector3> Points = new List<Vector3>();                    // summon: where (room frame); none: the fight's points but its first
        public List<Wire> Wires = new List<Wire>();                           // wires: fired as it lands
        public string Sound;            // played as it lands (default by kind)
        public string Fx;               // the effect as it lands (default by kind)
        public string Cue;              // the warning sound as its telegraph appears (default by kind; "" none)
    }

    /// <summary>A phase's shield: it takes Take of each hit (0: none; hits clang) until every condition holds (a brazier lit, a lever's
    /// mechanism down), a weak hit breaks it, or Seconds pass. Hold: it stands still channelling while it's up. Broken: it staggers and
    /// takes BreakShare of its health; BreakWires fire.</summary>
    internal class BossShield
    {
        public float Take;
        public List<BossCond> Until = new List<BossCond>();
        public float Seconds = 60f;
        public bool Hold = true;
        public float BreakShare = 0.08f;
        public List<Wire> BreakWires = new List<Wire>();
        public string Hint;             // a centre message (a $ key) for the players in the fight when it comes up
        public int Times = 1;           // its conditions must hold this many times (each time but the last they're put back, and it
                                        // flickers on)
    }

    internal class BossCond
    {
        public string Mech;             // a mechanism in the room
        public int State = 1;
    }

    /// <summary>A mechanism that hurts the boss: when it arrives at State (a hammer coming down) with the boss inside Area (the mechanism's
    /// frame, round AreaAt), the boss takes Share of its health, staggers, and its shield breaks and its armour comes off (Breaks).
    /// Players inside take Hurt times the land's trap damage.</summary>
    internal class BossWeak
    {
        public string Mech;
        public int State = 1;
        public Vector3 Area = new Vector3(5f, 4f, 5f);
        public Vector3 AreaAt;
        public float Share = 0.1f;
        public bool Stagger = true;
        public bool Breaks = true;
        public float Hurt = 2f;
        public bool Mark = true;        // an amber ring on the floor round its area while the fight is on: where to lure the boss
    }
}
