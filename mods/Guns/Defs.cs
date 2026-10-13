using UnityEngine;

namespace Guns
{
    /// <summary>
    /// The guns and what sets them apart. Numbers are first guesses for the balance pass (the guns are free while they are tested): the
    /// damage is the piercing damage of one round before skill; spread is in degrees. Settings (damage, rate, magazine, reload) are the
    /// server's in multiplayer and bound in Plugin.cs.
    /// </summary>
    internal static class Defs
    {
        private static SoundSpec Snd(float length, float crack, float crackT, float body, float bodyT, float thump, float thumpHz, float thumpT, float tail, float tailT, float drive = 1.6f,
                                     float minPitch = 0.94f, float maxPitch = 1.06f) =>
            new SoundSpec { Length = length, Crack = crack, CrackT = crackT, Body = body, BodyT = bodyT, Thump = thump, ThumpHz = thumpHz, ThumpT = thumpT, Tail = tail, TailT = tailT, Drive = drive, MinPitch = minPitch, MaxPitch = maxPitch };

        private static Cue[] Mag => new[] { new Cue(0f, Sound.Click.MagOut), new Cue(0.45f, Sound.Click.MagIn), new Cue(0.85f, Sound.Click.Rack) };

        internal static void Load()
        {
            if (Arsenal.All.Count > 0) return;

            Arsenal.Add(new GunDef
            {
                Id = "dh_pistol", Station = 1, Cost = "Iron:8, Wood:3, LeatherScraps:2", ReloadClip = "pistol", Cues = Mag, Stance = Stance.Pistol, Support = new Vector3(-0.012f, -0.06f, 0.02f), Key = "pistol", Section = "Pistol", BuildModel = Models.Pistol, Weight = 1.5f,
                Auto = false, Rate = 6f, Damage = 28f, Mag = 12, Reload = 1.6f,
                MinSpread = 0.8f, MaxSpread = 3.5f, PerShot = 0.18f, Recovery = 2f, AimSpread = 0.4f, Zoom = 1.3f,
                Speed = 120f, Gravity = 3.5f, Kick = 0.9f, Noise = 22f, Force = 6f, IconRoll = 10f,
                Tracer = new Color(1f, 0.82f, 0.45f), TracerLength = 0.3f, FlashSize = 0.7f, CasingSize = 0.8f,
                ActionTravel = 0.07f, ActionTime = 0.07f,
                Sound = Snd(0.30f, 1.0f, 0.004f, 0.6f, 0.03f, 0.6f, 220f, 0.04f, 0.35f, 0.10f, 1.4f),
            });
            Arsenal.Add(new GunDef
            {
                Id = "dh_revolver", Station = 2, Cost = "Iron:14, Wood:4, LeatherScraps:3", Cues = new[] { new Cue(0f, Sound.Click.CylOpen), new Cue(0.2f, Sound.Click.CylDump), new Cue(0.5f, Sound.Click.CylLoad), new Cue(0.78f, Sound.Click.CylLoad), new Cue(0.92f, Sound.Click.CylClose) }, Stance = Stance.Pistol, Support = new Vector3(-0.012f, -0.065f, 0.02f), Key = "revolver", Section = "Revolver", BuildModel = Models.Revolver, Weight = 2.5f,
                Auto = false, Rate = 2.5f, Damage = 70f, Mag = 6, Reload = 2.6f,
                MinSpread = 0.5f, MaxSpread = 4f, PerShot = 0.40f, Recovery = 1.4f, AimSpread = 0.3f, Zoom = 1.4f,
                Speed = 140f, Gravity = 3f, Kick = 2.2f, Noise = 32f, Force = 14f, Stagger = 0.4f, IconRoll = 10f,
                Tracer = new Color(1f, 0.78f, 0.40f), TracerLength = 0.4f, FlashSize = 1.4f, CasingSize = 0f,
                Sound = Snd(0.70f, 0.9f, 0.006f, 1.1f, 0.07f, 1.2f, 170f, 0.10f, 0.6f, 0.30f, 1.8f, 0.9f, 1.0f),
            });
            Arsenal.Add(new GunDef
            {
                Id = "dh_dualpistols", Station = 2, Cost = "Iron:16, Wood:5, LeatherScraps:4", ReloadClip = "pistol", Cues = new[] { new Cue(0f, Sound.Click.MagOut), new Cue(0.12f, Sound.Click.MagOut), new Cue(0.45f, Sound.Click.MagIn), new Cue(0.6f, Sound.Click.MagIn), new Cue(0.88f, Sound.Click.Rack) }, Key = "dualpistols", Section = "Dual Pistols", BuildModel = Models.DualPistols, Weight = 3f, Dual = true,
                Auto = false, Rate = 9f, Damage = 22f, Mag = 24, Reload = 2.4f,
                MinSpread = 1.5f, MaxSpread = 6f, PerShot = 0.12f, Recovery = 2f, AimSpread = 0.6f, Zoom = 1.15f,
                Speed = 120f, Gravity = 3.5f, Kick = 0.6f, Noise = 24f, Force = 5f, IconRoll = 8f,
                Tracer = new Color(1f, 0.82f, 0.45f), TracerLength = 0.3f, FlashSize = 0.7f, CasingSize = 0.8f,
                ActionTravel = 0.07f, ActionTime = 0.07f,
                Sound = Snd(0.28f, 1.0f, 0.004f, 0.6f, 0.03f, 0.55f, 230f, 0.04f, 0.32f, 0.09f, 1.4f, 0.97f, 1.1f),
            });
            Arsenal.Add(new GunDef
            {
                Id = "dh_smg", Station = 2, Cost = "Iron:16, Copper:4, LeatherScraps:3", ReloadClip = "rifle", Cues = Mag, Stance = Stance.Rifle, Support = new Vector3(0f, 0.01f, 0.24f), Key = "smg", Section = "Submachine Gun", BuildModel = Models.Smg, Weight = 3.5f,
                Auto = true, Rate = 14f, Damage = 11f, Mag = 32, Reload = 1.8f,
                MinSpread = 1.2f, MaxSpread = 7f, PerShot = 0.07f, Recovery = 1.6f, AimSpread = 0.4f, Zoom = 1.4f,
                Speed = 115f, Gravity = 4f, Kick = 0.7f, Noise = 22f, Force = 5f, IconRoll = 14f,
                Tracer = new Color(1f, 0.80f, 0.40f), TracerLength = 0.35f, FlashSize = 0.8f, CasingSize = 0.75f,
                ActionTravel = 0.04f, ActionTime = 0.05f,
                Sound = Snd(0.25f, 0.9f, 0.004f, 0.6f, 0.03f, 0.5f, 190f, 0.035f, 0.3f, 0.09f, 1.5f, 0.95f, 1.08f),
            });
            Arsenal.Add(new GunDef
            {
                Id = "dh_shotgun", Station = 2, Cost = "Iron:16, Wood:10, LeatherScraps:3", CycleClick = Sound.Click.Pump, Stance = Stance.Rifle, Support = new Vector3(0f, 0.02f, 0.46f), Key = "shotgun", Section = "Shotgun", BuildModel = Models.Shotgun, Weight = 4f,
                Auto = false, Rate = 1.2f, Cycle = 0.85f, Pellets = 9, Damage = 9f, Mag = 6, Reload = 0.55f, PerRound = true,
                MinSpread = 3f, MaxSpread = 5f, PerShot = 0.5f, Recovery = 1.5f, AimSpread = 0.7f, Zoom = 1.3f,
                Speed = 95f, Gravity = 6f, Ttl = 0.75f, Kick = 2.5f, Noise = 40f, Force = 5f, Stagger = 0.3f,
                Tracer = new Color(1f, 0.75f, 0.4f), TracerLength = 0.12f, FlashSize = 1.5f, CasingSize = 1.3f, CasingColor = new Color(0.70f, 0.12f, 0.08f),
                ActionTravel = 0.12f, ActionTime = 0.6f,
                Sound = Snd(0.85f, 0.6f, 0.008f, 1.5f, 0.09f, 1.4f, 120f, 0.12f, 0.8f, 0.35f, 1.9f, 0.92f, 1.0f),
            });
            Arsenal.Add(new GunDef
            {
                Id = "dh_rifle", Station = 2, Cost = "Iron:14, FineWood:8, LeatherScraps:3", CycleClick = Sound.Click.Bolt, Cues = new[] { new Cue(0f, Sound.Click.Bolt), new Cue(0.3f, Sound.Click.Shell), new Cue(0.45f, Sound.Click.Shell), new Cue(0.6f, Sound.Click.Shell), new Cue(0.9f, Sound.Click.Bolt) }, Stance = Stance.Rifle, Support = new Vector3(0f, 0.01f, 0.36f), Key = "rifle", Section = "Rifle", BuildModel = Models.Rifle, Weight = 4f,
                Auto = false, Rate = 1f, Cycle = 1.0f, Damage = 90f, Mag = 5, Reload = 3f,
                MinSpread = 0.2f, MaxSpread = 2.5f, PerShot = 1.0f, Recovery = 1.2f, AimSpread = 0.15f, Zoom = 2.2f,
                Speed = 260f, Gravity = 2f, Kick = 3f, Noise = 45f, Force = 25f, Stagger = 0.7f,
                Tracer = new Color(1f, 0.85f, 0.55f), TracerLength = 0.7f, FlashSize = 1.3f, CasingSize = 1.2f,
                ActionTravel = 0.07f, ActionTime = 0.8f,
                Sound = Snd(0.70f, 1.0f, 0.006f, 1.0f, 0.06f, 1.0f, 140f, 0.08f, 0.6f, 0.30f, 1.7f, 0.94f, 1.02f),
            });
            Arsenal.Add(new GunDef
            {
                Id = "dh_leveraction", Station = 2, Cost = "Iron:16, FineWood:8, Bronze:4, LeatherScraps:3", CycleClick = Sound.Click.Lever, Stance = Stance.Rifle, Support = new Vector3(0f, 0.01f, 0.38f), Key = "leveraction", Section = "Lever-Action Rifle", BuildModel = Models.LeverAction, Weight = 4f,
                Auto = false, Rate = 2f, Cycle = 0.55f, Damage = 55f, Mag = 10, Reload = 0.45f, PerRound = true,
                MinSpread = 0.5f, MaxSpread = 3.5f, PerShot = 0.5f, Recovery = 1.5f, AimSpread = 0.25f, Zoom = 1.8f,
                Speed = 200f, Gravity = 2.5f, Kick = 1.8f, Noise = 35f, Force = 15f, Stagger = 0.4f,
                Tracer = new Color(1f, 0.82f, 0.45f), TracerLength = 0.5f, FlashSize = 1.2f, CasingSize = 1.1f,
                ActionTurn = 70f, ActionTime = 0.5f,
                Sound = Snd(0.60f, 1.0f, 0.006f, 0.8f, 0.05f, 0.9f, 150f, 0.07f, 0.5f, 0.24f, 1.6f, 0.95f, 1.05f),
            });
            Arsenal.Add(new GunDef
            {
                Id = "dh_ar15", Station = 3, Cost = "BlackMetal:14, Iron:10, LeatherScraps:4", ReloadClip = "rifle", Cues = Mag, Stance = Stance.Rifle, Support = new Vector3(0f, 0.06f, 0.40f), Key = "ar15", Section = "AR-15", BuildModel = Models.Ar15, Weight = 4f,
                Auto = false, Rate = 8f, Damage = 26f, Mag = 30, Reload = 2.2f,
                MinSpread = 0.5f, MaxSpread = 4f, PerShot = 0.10f, Recovery = 2.5f, AimSpread = 0.3f, Zoom = 1.8f,
                Speed = 220f, Gravity = 2f, Kick = 0.9f, Noise = 30f, Force = 8f, Stagger = 0.25f,
                Tracer = new Color(1f, 0.76f, 0.35f), TracerLength = 0.5f, FlashSize = 1f, CasingSize = 0.9f,
                ActionTravel = 0.04f, ActionTime = 0.06f,
                Sound = Snd(0.45f, 1.0f, 0.005f, 0.9f, 0.05f, 0.9f, 150f, 0.07f, 0.5f, 0.20f, 1.6f, 0.95f, 1.05f),
            });
            Arsenal.Add(new GunDef
            {
                Id = "dh_sniper", Station = 3, Cost = "BlackMetal:20, Iron:12, FineWood:10, Silver:4", CanDodge = false, CycleClick = Sound.Click.Bolt, Cues = new[] { new Cue(0f, Sound.Click.Bolt), new Cue(0.3f, Sound.Click.MagOut), new Cue(0.55f, Sound.Click.MagIn), new Cue(0.88f, Sound.Click.Bolt) }, Stance = Stance.Rifle, Support = new Vector3(0f, 0.03f, 0.38f), Key = "sniper", Section = "Sniper Rifle", BuildModel = Models.Sniper, Weight = 9f, Scope = true,
                Auto = false, Rate = 1f, Cycle = 1.6f, Damage = 240f, Mag = 5, Reload = 3.4f,
                MinSpread = 0.5f, MaxSpread = 5f, PerShot = 1.5f, Recovery = 1.0f, AimSpread = 0f, Zoom = 4.5f,
                Speed = 400f, Gravity = 1.2f, Ttl = 2.5f, Kick = 4f, Noise = 60f, Force = 40f, Stagger = 1f,
                Tracer = new Color(1f, 0.9f, 0.7f), TracerLength = 1.2f, FlashSize = 1.8f, CasingSize = 1.4f,
                ActionTravel = 0.08f, ActionTime = 1.0f,
                Sound = Snd(1.30f, 1.2f, 0.007f, 1.4f, 0.08f, 1.3f, 110f, 0.14f, 1.0f, 0.60f, 1.9f, 0.93f, 1.0f),
            });
            Arsenal.Add(new GunDef
            {
                Id = "dh_machinegun", Station = 3, Cost = "BlackMetal:30, Iron:20, Chain:2, LeatherScraps:4", LoopRate = 11.0f, CanDodge = false, Cues = Mag, Stance = Stance.Heavy, Support = new Vector3(0f, 0.0f, 0.42f), Key = "machinegun", Section = "Machine Gun", BuildModel = Models.MachineGun, Weight = 8f,
                Auto = true, Rate = 10f, Damage = 14f, Mag = 75, Reload = 4.2f,
                MinSpread = 0.6f, MaxSpread = 6f, PerShot = 0.06f, Recovery = 1.2f, AimSpread = 0.35f, Zoom = 1.6f,
                Speed = 130f, Gravity = 3f, Kick = 1f, Noise = 25f, Force = 8f,
                ActionTravel = 0.05f, ActionTime = 0.05f,
                Sound = Snd(0.42f, 0.85f, 0.005f, 0.8f, 0.045f, 0.95f, 150f, 0.075f, 0.45f, 0.16f, 1.6f),
            });
            Arsenal.Add(new GunDef
            {
                Id = "dh_minigun", Station = 4, Cost = "BlackMetal:50, Iron:40, Chain:6, Silver:10", LoopRate = 21.4f, CanDodge = false, Cues = new[] { new Cue(0f, Sound.Click.Belt), new Cue(0.35f, Sound.Click.MagOut), new Cue(0.6f, Sound.Click.Belt), new Cue(0.8f, Sound.Click.MagIn), new Cue(0.93f, Sound.Click.Rack) }, Stance = Stance.Heavy, Support = new Vector3(0f, 0.0f, 0.24f), Key = "minigun", Section = "Minigun", BuildModel = Models.Minigun, Weight = 25f,
                Auto = true, Rate = 25f, Damage = 10f, Mag = 400, Reload = 7f, SpinUp = 0.9f, MoveSpeed = -0.35f,
                MinSpread = 1.2f, MaxSpread = 7f, PerShot = 0.025f, Recovery = 1.5f, AimSpread = 0.5f, Zoom = 1.3f,
                Speed = 150f, Gravity = 3.2f, Kick = 0.45f, Noise = 35f, Force = 6f, Stagger = 0.1f, IconRoll = 18f,
                Tracer = new Color(1f, 0.55f, 0.20f), TracerLength = 0.6f, FlashSize = 1.2f, CasingSize = 0.9f,
                Sound = Snd(0.20f, 0.7f, 0.004f, 0.8f, 0.035f, 0.8f, 130f, 0.04f, 0.25f, 0.07f, 1.6f, 0.95f, 1.05f),
            });
            // Damage is the fire of each puff (Flame.cs): fifteen a second, burning on after the stream moves away. Speed and Ttl are the puff's: about 7 m of flame.
            Arsenal.Add(new GunDef
            {
                Id = "dh_flamethrower", Station = 2, Cost = "Iron:20, Bronze:6, Resin:20, SurtlingCore:3", Flame = true, CanDodge = false, Cues = new[] { new Cue(0f, Sound.Click.MagOut), new Cue(0.5f, Sound.Click.MagIn), new Cue(0.85f, Sound.Click.Rack) }, Stance = Stance.Heavy, Support = new Vector3(0f, 0.01f, 0.62f), Key = "flamethrower", Section = "Flamethrower", BuildModel = Models.Flamethrower, Weight = 8f,
                Auto = true, Rate = 15f, Damage = 8f, Mag = 150, Reload = 3.5f,
                MinSpread = 2.5f, MaxSpread = 4f, PerShot = 0.02f, Recovery = 2f, AimSpread = 0.8f, Zoom = 1.15f,
                Speed = 20f, Gravity = 0f, Ttl = 0.7f, Kick = 0f, Noise = 20f, Force = 0.5f, Stagger = 0f, IconRoll = 14f,
                TracerLength = 0f, FlashSize = 0f, CasingSize = 0f,
            });
        }
    }
}
