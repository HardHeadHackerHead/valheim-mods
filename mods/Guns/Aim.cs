using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Guns
{
    /// <summary>
    /// Aiming down the sights: while a gun is in hand, the block button (right mouse, or the left trigger) zooms the camera in and tightens
    /// the spread (Fire.cs). No new key: the button is the game's own, which a gun has no other use for. The zoom is the game's own
    /// temporary field of view (GameCamera.SetTempFOV), eased in and out, and put back whenever aiming stops for any reason. A scoped gun
    /// zooms much further, draws a scope picture over the screen and hides the player's own body (which would fill the view from behind).
    /// Also posed here: the model in the hand pointing where you look, its recoil, the reload dip, the moving parts and a second gun in the
    /// left hand.
    /// </summary>
    internal static class Aim
    {
        private static readonly MethodInfo TakeInput = AccessTools.Method(typeof(Player), "TakeInput");
        private static GunDef _zoomed;
        internal static bool Active;

        private static readonly List<Renderer> Hidden = new List<Renderer>();
        private static float _nextHide;

        /// <summary>Called every frame from the plugin.</summary>
        internal static void Tick()
        {
            Player p = Player.m_localPlayer;
            GunDef d = Arsenal.Held(p, out ItemDrop.ItemData item);
            bool want = d != null && !p.IsDead() && InputAllowed(p) && (ZInput.GetButton("Block") || ZInput.GetButton("JoyBlock"));
            if (want != Active || (want && d != _zoomed))
            {
                Active = want;
                _zoomed = want ? d : null;
                GameCamera cam = GameCamera.instance;
                if (cam != null)
                {
                    if (want) cam.SetTempFOV(cam.m_fov / Mathf.Max(1f, d.Zoom * Plugin.AimZoom.Value), 0.2f);
                    else cam.ResetTempFOV();
                }
            }
            bool scoped = Active && _zoomed != null && _zoomed.Scope;
            AmmoHud.Scope(scoped);
            if (scoped) HideBody(p, true);
            else if (Hidden.Count > 0) HideBody(p, false);
        }

        /// <summary>The player's own renderers off (or back on): through a scope the character would stand in the way.</summary>
        private static void HideBody(Player p, bool hide)
        {
            if (!hide)
            {
                foreach (Renderer r in Hidden) if (r != null) r.enabled = true;
                Hidden.Clear();
                return;
            }
            if (p == null || Time.time < _nextHide) return;
            _nextHide = Time.time + 0.25f;
            foreach (Renderer r in p.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                r.enabled = false;
                Hidden.Add(r);
            }
        }

        // ---- posing the model -----------------------------------------------------------------------------------------

        private static readonly Dictionary<Transform, KeyValuePair<Vector3, Quaternion>> Bases = new Dictionary<Transform, KeyValuePair<Vector3, Quaternion>>();
        private static readonly Vector3 PistolGrip = new Vector3(0f, -0.075f, -0.085f);   // (the pistol's grip in its own parts' coordinates, Models.PistolParts)
        internal static readonly Carry Mine = new Carry();

        /// <summary>The part's resting place, noted the first time it is seen, put back before each frame's pose is worked out.</summary>
        private static void Rest(Transform t)
        {
            if (!Bases.TryGetValue(t, out KeyValuePair<Vector3, Quaternion> b)) Bases[t] = b = new KeyValuePair<Vector3, Quaternion>(t.localPosition, t.localRotation);
            t.localPosition = b.Key;
            t.localRotation = b.Value;
        }

        /// <summary>Forgets the resting places of parts that are gone (a player's gun swapped, a player who left).</summary>
        internal static void PruneBases()
        {
            if (Bases.Count < 64) return;
            var gone = new List<Transform>();
            foreach (Transform t in Bases.Keys) if (t == null) gone.Add(t);
            foreach (Transform t in gone) Bases.Remove(t);
        }

        /// <summary>
        /// After the animation has posed the arms (the game's crossbow carry lowers the gun across the chest when you walk), the gun in the
        /// hand is turned to point where you look, pivoting about the grip so it stays in the hand, kicked back by a shot and dipped while it
        /// is reloaded. Called every frame (LateUpdate) for the local player; Share.Late does the same for the others from what they share.
        /// </summary>
        internal static void Pose()
        {
            Player p = Player.m_localPlayer;
            GunDef d = Arsenal.Held(p, out ItemDrop.ItemData item);
            Carry c = Mine;
            if (d == null || p.IsDead()) { c.Model = null; return; }
            c.Def = d;
            c.Look = p.GetLookDir();
            c.Aiming = Active || Dive.Active;
            c.Sprinting = p.IsRunning();
            c.Dip = Ammo.Dip;
            c.Recoil = Fx.Recoil;
            c.LastShot = Fire.LastShot;
            c.Second = Fire.LastWasSecond;
            c.SpinAngle = Fire.SpinAngle;
            c.Arms = Plugin.ArmPosing.Value;
            PoseOn(p, c);
        }

        /// <summary>One player's gun posed from their carry's state (the local player's, or another's as their game shares it).</summary>
        internal static void PoseOn(Player p, Carry c)
        {
            GunDef d = c.Def;
            Transform model = Fx.ModelRoot(p);
            if (model == null) return;
            c.Model = model;
            Rest(model);
            if (p.InDodge())   // rolling: the animation has the arms, and the gun goes back to how it sits in the hand
            {
                if (d.Dual) { Transform l = Fx.Part(p, "pistolL"); if (l != null) Rest(l); }
                return;
            }
            Vector3 look = c.Look;
            if (look.sqrMagnitude < 0.0001f) return;

            float dip = c.Dip;
            if (!d.Dual && c.Arms && PoseHeld(p, c, model, look, dip)) { Tail(p, c); return; }
            // a pair kicks one pistol at a time: the one that fired, the other only a little
            float kickR = c.Recoil, kickL = c.Recoil;
            if (d.Dual) { if (c.Second) kickR *= 0.3f; else kickL *= 0.3f; }
            Quaternion aim = Quaternion.LookRotation(look, Vector3.up);
            // a pair of pistols: each arm reaches out and forward to its own gun, spread to the sides (the hands go where the pistols are)
            Vector3? rightAt = null, leftAt = null;
            VisEquipment ve = d.Dual ? p.GetComponent<VisEquipment>() : null;
            if (ve != null && ve.m_rightHand != null && ve.m_leftHand != null)
            {
                Transform ur = Arms.Upper(ve.m_rightHand), ul = Arms.Upper(ve.m_leftHand);
                if (ur != null && ul != null)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, look).normalized;   // (to the right of where you look)
                    // sprinting or aiming (right mouse): the arms straighten, come in and reach further in front of you (eased in and out)
                    c.Run = Mathf.MoveTowards(c.Run, (c.Sprinting || c.Aiming) ? 1f : 0f, Time.deltaTime * 6f);
                    float spread = Mathf.Lerp(0.22f, 0.08f, c.Run), reach = Mathf.Lerp(0.46f, 0.64f, c.Run);
                    Vector3 tr = ur.position + side * spread + look * reach, tl = ul.position - side * spread + look * reach;
                    if (Arms.Reach(ve.m_rightHand, tr, side * 0.6f + Vector3.down)) rightAt = tr;
                    if (Arms.Reach(ve.m_leftHand, tl, -side * 0.6f + Vector3.down)) leftAt = tl;
                }
            }
            Quaternion rot = aim * Quaternion.Euler(-kickR * 5f + dip * 32f, 0f, 0f);
            Vector3 push = rot * new Vector3(0f, -dip * 0.12f, -kickR * 0.035f);
            Vector3 held = model.TransformPoint(d.Grip);
            model.rotation = rot;
            model.position += held - model.TransformPoint(d.Grip) + push;
            if (rightAt.HasValue) model.position = rightAt.Value + push;

            Moving(p, c, "dh_action", d.Dual && c.Second);
            if (d.Dual)
            {
                Moving(p, c, "dh_action2", !c.Second);
                Transform left = Fx.Part(p, "pistolL");
                Transform hand = ve != null ? ve.m_leftHand : null;
                if (left != null && hand != null)
                {
                    Rest(left);
                    Quaternion rotL = aim * Quaternion.Euler(-kickL * 5f + dip * 32f, 0f, 0f);
                    left.rotation = rotL;
                    left.position = (leftAt ?? hand.position) - rotL * PistolGrip + rotL * new Vector3(0f, -dip * 0.12f, -kickL * 0.035f);
                }
            }
            if (d.SpinUp > 0f) Tail(p, c);
        }

        /// <summary>The moving parts of a single gun: a slide, bolt, pump or lever, and a rotary gun's barrels.</summary>
        private static void Tail(Player p, Carry c)
        {
            GunDef d = c.Def;
            if (!d.Dual) Moving(p, c, "dh_action", false);
            if (d.SpinUp > 0f)
            {
                Transform spin = Fx.Part(p, "dh_spin");
                if (spin != null) { Rest(spin); spin.localRotation = Quaternion.Euler(0f, 0f, c.SpinAngle); }
            }
        }

        /// <summary>
        /// Holds a gun with both hands: the right on the grip, the left on the foregrip (or cupped under a pistol's grip), the arms bent to
        /// reach them (Arms.cs). Walking, the gun is held in front of the chest pointing where you look; aiming, a rifle comes up to the shoulder
        /// and a pistol goes out to arm's length; sprinting, it drops to a low ready, muzzle down, and the hands come in. While it is reloaded the
        /// left hand goes down to the magazine. If the left hand cannot reach its place the gun is drawn back until it can. Returns false when
        /// the rig's arm bones are not found (the game's own carry is left alone then).
        /// </summary>
        private static bool PoseHeld(Player p, Carry c, Transform model, Vector3 look, float dip)
        {
            GunDef d = c.Def;
            VisEquipment ve = p.GetComponent<VisEquipment>();
            if (ve == null || ve.m_rightHand == null || ve.m_leftHand == null) return false;
            Transform ur = Arms.Upper(ve.m_rightHand), ul = Arms.Upper(ve.m_leftHand);
            if (ur == null || ul == null) return false;

            c.AimT = Mathf.MoveTowards(c.AimT, c.Aiming ? 1f : 0f, Time.deltaTime * 7f);
            c.SprintT = Mathf.MoveTowards(c.SprintT, c.Sprinting && !c.Aiming ? 1f : 0f, Time.deltaTime * 5f);
            float aimT = c.AimT, sprintT = c.SprintT;
            // where the grip is from the right shoulder: (to the right, forward, up), and how far the muzzle drops when sprinting
            Vector3 walk, aim, sprint;
            float drop;
            switch (d.Stance)
            {
                case Stance.Pistol: walk = new Vector3(-0.06f, 0.34f, -0.22f); aim = new Vector3(-0.06f, 0.44f, -0.04f); sprint = new Vector3(-0.10f, 0.28f, -0.28f); drop = 40f; break;
                case Stance.Heavy: walk = new Vector3(0.11f, 0.08f, -0.60f); aim = new Vector3(0.09f, 0.14f, -0.52f); sprint = new Vector3(0.11f, 0.05f, -0.62f); drop = 20f; break;   // (down at the right hip)
                default: walk = new Vector3(-0.10f, 0.30f, -0.24f); aim = new Vector3(-0.10f, 0.18f, -0.10f); sprint = new Vector3(-0.12f, 0.26f, -0.30f); drop = 35f; break;
            }
            Vector3 offset = Vector3.Lerp(Vector3.Lerp(walk, aim, aimT), sprint, sprintT);
            float kick = c.Recoil;
            Quaternion rot = Quaternion.LookRotation(look, Vector3.up) * Quaternion.Euler(-kick * 5f + dip * 32f + drop * sprintT, 0f, 0f);
            Vector3 side = Vector3.Cross(Vector3.up, look).normalized;   // (to the right of where you look)
            Vector3 fwd = look.normalized;
            Vector3 grip = ur.position + side * offset.x + fwd * offset.y + Vector3.up * offset.z;

            // the left hand: on the foregrip, and down at the magazine while reloading
            Vector3 hold = Vector3.Lerp(d.Support, new Vector3(0f, -0.17f, 0.03f), Mathf.Clamp01(dip * 2.5f));
            float reach = Arms.Length(ve.m_leftHand) * 0.96f;
            Vector3 start = grip;
            for (int i = 0; i < 3 && reach > 0f; i++)
            {
                float far = (grip + rot * hold - ul.position).magnitude;
                if (far <= reach) break;
                grip -= fwd * (far - reach) * 0.8f;   // (draw the gun back towards the body, but not into it)
                if ((start - grip).magnitude > 0.18f) { grip = start - fwd * 0.18f; break; }
            }
            Vector3 push = rot * new Vector3(0f, -dip * 0.12f, -kick * 0.035f);
            Arms.Reach(ve.m_rightHand, grip, side * 0.6f + Vector3.down);
            Arms.Reach(ve.m_leftHand, grip + rot * hold, -side * 0.6f + Vector3.down);
            model.rotation = rot;
            model.position = grip + push;
            return true;
        }

        /// <summary>A part that moves when the gun fires: it slides back and returns (a slide, bolt or pump), or swings (a lever).</summary>
        private static void Moving(Player p, Carry c, string name, bool resting)
        {
            GunDef d = c.Def;
            if (d.ActionTravel <= 0f && d.ActionTurn <= 0f) return;
            Transform part = Fx.Part(p, name);
            if (part == null) return;
            Rest(part);
            if (resting) return;
            float u = (Time.time - c.LastShot) / Mathf.Max(0.02f, d.ActionTime);
            if (u < 0f || u > 1f) return;
            float swing = Mathf.Sin(u * Mathf.PI);
            part.localPosition += new Vector3(0f, 0f, -d.ActionTravel * swing);
            if (d.ActionTurn > 0f) part.localRotation *= Quaternion.Euler(d.ActionTurn * swing, 0f, 0f);
        }

        /// <summary>Stops aiming and gives the camera its field of view back (the plugin unloading, the gun put away).</summary>
        internal static void Stop()
        {
            HideBody(null, false);
            AmmoHud.Scope(false);
            if (!Active) return;
            Active = false;
            _zoomed = null;
            GameCamera.instance?.ResetTempFOV();
        }

        internal static bool InputAllowed(Player p)
        {
            try { return TakeInput == null || (bool)TakeInput.Invoke(p, null); }   // (no window open, not typing in chat)
            catch { return true; }
        }
    }

    // The block button is how a gun aims, but the game must not take it as blocking: that raises the weapon across the chest (a block
    // pose), and block + jump is a dodge. For a gun the game is told the button isn't pressed; Aim.Tick reads the button itself.
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class Player_SetControls_Aim
    {
        private static void Prefix(Player __instance, Vector3 movedir, ref bool block, ref bool blockHold, ref bool jump)
        {
            if (__instance != Player.m_localPlayer) return;
            GunDef d = Arsenal.Of(__instance.GetCurrentWeapon());
            if (d == null) return;
            // block + jump with a light gun is a dive (Dive.cs), not the game's roll, and not a jump: the game never sees it
            if (d.CanDodge && jump && blockHold && Dive.TryStart(__instance, movedir)) jump = false;
            block = false;
            blockHold = false;
        }
    }

    // With a gun in hand the body always faces where the camera looks (as it does while drawing a bow, strafing when it walks), so the gun
    // points where you look instead of swinging to the side as you walk, and there is nothing to shoot through behind you.
    [HarmonyPatch(typeof(Player), "AlwaysRotateCamera")]
    internal static class Player_AlwaysRotateCamera_Aim
    {
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (__result || __instance != Player.m_localPlayer || __instance.InDodge()) return;   // (a roll goes the way it was started, not round to face the camera)
            if (Arsenal.Of(__instance.GetCurrentWeapon()) != null) __result = true;
        }
    }
}
