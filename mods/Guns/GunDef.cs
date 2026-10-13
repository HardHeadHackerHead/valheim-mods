using HarmonyLib;
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Guns
{
    /// <summary>How a gun's shot sounds (Sound.cs): the loudness and length of the crack, the blast, the thump and the tail.</summary>
    internal struct SoundSpec
    {
        public float Length, Crack, CrackT, Body, BodyT, Thump, ThumpHz, ThumpT, Tail, TailT, Drive;
        public float MinPitch, MaxPitch;
    }

    /// <summary>
    /// One gun, as data: how it handles (rate, magazine, reload, spread, aiming), what its round does, how it sounds and how its model is
    /// built. Every gun is cloned from the Arbalest (Gun.cs) and fired by Fire.cs; adding a gun is one more GunDef in Arsenal.cs and a
    /// model in Models.cs.
    /// </summary>
    /// <summary>One sound of a reload: at this share of the way through it.</summary>
    internal struct Cue
    {
        public float At; public Sound.Click Click;
        public Cue(float at, Sound.Click click) { At = at; Click = click; }
    }

    /// <summary>How a gun is carried (Aim.PoseHeld): a rifle is shouldered to aim, a pistol held out at arm's length, a heavy gun stays at the hip.</summary>
    internal enum Stance { Rifle, Pistol, Heavy }

    internal sealed class GunDef
    {
        public string Id;          // the item prefab: dh_pistol
        public string Key;         // "pistol": the words $guns_pistol and $guns_pistol_desc
        public string Section;     // the settings section, in English
        public string BulletId => Id + "_bullet";

        // ---- handling ----
        public bool Auto;          // holds the trigger down to keep firing; otherwise one round for each pull
        public float Rate = 10f;   // rounds a second at most
        public float Cycle;        // seconds before the next round after one (a bolt, a lever, a pump): the slower of this and 1/Rate applies
        public int Pellets = 1;    // rounds in one shot (a shotgun)
        public float Damage = 20f; // piercing damage of one round (of each pellet), before skill
        public int Mag = 10;       // rounds in a full magazine
        public float Reload = 2f;  // seconds to reload (to load one round when PerRound)
        public bool PerRound;      // loads one round at a time (and can be fired between): a shotgun, a lever action
        public float SpinUp;       // seconds of holding the trigger before it fires (a rotary gun)
        public float MoveSpeed;    // added to the walking and running speed while held (negative: slower)
        public bool CanDodge = true;   // block + jump still rolls (a heavy gun is too heavy to roll with)
        public int Station = 1;            // the forge level it is made at
        public string Cost = "Iron:10";    // what it takes, as the setting writes it (Costs.cs)
        public bool Dual;          // two guns, one in each hand, firing in turn

        // ---- spread (degrees) ----
        public float MinSpread = 1f, MaxSpread = 5f, PerShot = 0.1f, Recovery = 1.5f;
        public float AimSpread = 0.35f;   // what share of the spread stays while aiming
        public float Zoom = 1.6f;         // how far the view zooms while aiming
        public bool Scope;                // a scope picture while aiming

        // ---- the round ----
        public float Speed = 130f, Gravity = 3f, Ttl = 3f;
        public Color Tracer = new Color(1f, 0.74f, 0.30f);
        public float TracerLength = 0.45f;   // 0: no streak (pellets)
        public float Kick = 1f, Noise = 25f, Force = 8f, Stagger = 0.2f, Weight = 6f;

        // ---- look and sound ----
        public SoundSpec Sound;
        public float FlashSize = 1f;
        public float CasingSize = 1f;      // 0: nothing is thrown out (a revolver keeps its brass)
        public Color CasingColor = new Color(0.86f, 0.62f, 0.20f);
        public float IconRoll = 24f;
        public Vector3 Grip = Vector3.zero;             // where the hand holds the model (it turns about this point to point where you look)
        public float ActionTravel;         // how far the moving part ("dh_action") slides back on a shot (m), or
        public float ActionTurn;           // how far it turns about its X axis (degrees: a lever)
        public float ActionTime = 0.08f;   // and how long it takes there and back; a Cycle longer than this is used when it is
        public float LoopRate;             // the rounds a second the looped recording of its fire (loop_<id>.wav, tail_<id>.wav) was recorded at: it plays while it fires, at the pitch that matches its rate
        public string ReloadClip;          // a recording of the whole reload ("pistol", "rifle"; Fx.Reload), used when there is one
        public Cue[] Cues;                 // the sounds of a reload, in order (a whole reload)
        public global::Guns.Sound.Click LoadClick = global::Guns.Sound.Click.Shell;   // a round going in (a reload one round at a time)
        public global::Guns.Sound.Click? CycleClick;    // the sound of working the action after a shot (a bolt, a pump, a lever), and when a round-at-a-time reload finishes
        public Stance Stance = Stance.Rifle;
        public Vector3 Support;            // where the left hand holds it, in the model's space (the grip is the origin): a foregrip, a pump, under a pistol's grip
        public Func<Transform, Material, Bounds> BuildModel;   // puts the model under a parent and returns its bounds

        // ---- settings (the server's in multiplayer) ----
        public ConfigEntry<float> CDamage, CRate, CReload;
        public ConfigEntry<string> CCost;
        public ConfigEntry<int> CMag;

        public float DamageNow => CDamage != null ? CDamage.Value : Damage;
        public float RateNow => CRate != null ? CRate.Value : Rate;
        public float ReloadNow => CReload != null ? CReload.Value : Reload;
        public int MagNow => CMag != null ? CMag.Value : Mag;
        public float Interval => Mathf.Max(1f / Mathf.Max(0.5f, RateNow), Cycle);
    }

    internal static class Arsenal
    {
        internal static readonly List<GunDef> All = new List<GunDef>();
        private static readonly Dictionary<string, GunDef> ById = new Dictionary<string, GunDef>();

        private static readonly Dictionary<int, GunDef> ByHash = new Dictionary<int, GunDef>();
        private static readonly AccessTools.FieldRef<VisEquipment, int> RightItem = AccessTools.FieldRefAccess<VisEquipment, int>("m_rightItem");

        internal static void Add(GunDef d) { All.Add(d); ById[d.Id] = d; ByHash[d.Id.GetStableHashCode()] = d; }

        /// <summary>The gun in any player's right hand, from what their equipment shows (the other players' items are not in our game, only what they wear).</summary>
        internal static GunDef HeldBy(Player p)
        {
            if (p == null) return null;
            if (p == Player.m_localPlayer) return Held(p, out ItemDrop.ItemData _);
            VisEquipment ve = p.GetComponent<VisEquipment>();
            return ve != null && ByHash.TryGetValue(RightItem(ve), out GunDef d) ? d : null;
        }

        internal static GunDef Of(string prefab) => prefab != null && ById.TryGetValue(prefab, out GunDef d) ? d : null;

        /// <summary>The gun an item is, or null when it isn't one of ours.</summary>
        internal static GunDef Of(ItemDrop.ItemData item) => item?.m_dropPrefab != null ? Of(item.m_dropPrefab.name) : null;

        /// <summary>The gun the local player holds, with its item.</summary>
        internal static GunDef Held(Player p, out ItemDrop.ItemData item)
        {
            item = p != null ? p.GetCurrentWeapon() : null;
            return Of(item);
        }
    }
}
