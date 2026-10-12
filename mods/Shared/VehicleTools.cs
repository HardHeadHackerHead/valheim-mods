using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DHack.Shared
{
    /// <summary>
    /// A test command for the Claude Tools mod (its request mailbox), for working on the rider's pose while someone sits on a vehicle:
    /// "atv" or "bike" (the spec's Command), with status, anims [text], ride [far], sit, off, base &lt;attach animation&gt;, pose on|off,
    /// tune [&lt;key&gt; &lt;value&gt;]... and bones. Nothing here cheats: it does what pressing E does, or changes how things look on this game.
    /// Shared source (mods/Shared/VehicleTools.cs), linked into FourWheelers and MountainBikes; the plugin calls <see cref="Update"/>
    /// every frame and <see cref="Unregister"/> when it unloads.
    /// </summary>
    internal static class VehicleTools
    {
        private static BaseUnityPlugin _claudeTools;
        private static float _next;
        private static readonly AccessTools.FieldRef<Player, string> AttachAnim = AccessTools.FieldRefAccess<Player, string>("m_attachAnimation");
        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation> ZAnim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");
        private static string _modName;

        public static void Update(string modName)
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 5f;
            _modName = modName;
            BaseUnityPlugin found = Chainloader.PluginInfos.TryGetValue("com.quad.claudetools", out PluginInfo info) ? info.Instance : null;
            if (found == _claudeTools || GroundVehicle.Spec == null || GroundVehicle.Spec.Command == null) return;
            _claudeTools = found;
            if (found == null) return;
            try
            {
                MethodInfo register = found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static);
                register?.Invoke(null, new object[] { modName, GroundVehicle.Spec.Command,
                    GroundVehicle.Spec.Command + " status | anims [text] | ride [far] | sit | off | base <attach animation> | pose on|off | tune [<key> <value>]... | bones: the rider's pose on a vehicle, for working on it",
                    (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Command });
            }
            catch (Exception e) { Debug.LogWarning("[" + modName + "] Could not add the test command to Claude Tools: " + e.Message); }
        }

        public static void Unregister()
        {
            try { _claudeTools?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { _modName }); }
            catch (Exception) { }
        }

        private static GroundVehicle Nearest(Player me, Func<GroundVehicle, bool> where = null) =>
            GroundVehicle.Loaded.Where(v => v != null && (where == null || where(v))).OrderBy(v => Vector3.Distance(v.transform.position, me.transform.position)).FirstOrDefault();

        private static IEnumerator Command(string[] args, Action<JObject> output, Action<string> error)
        {
            Player me = Player.m_localPlayer;
            if (me == null) { error("no player in the world"); return null; }
            args = args.Skip(1).ToArray();
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            GroundVehicle riding = VehicleRider.RidingOf(me);
            var inv = System.Globalization.CultureInfo.InvariantCulture;

            switch (sub)
            {
                case "status":
                    output(new JObject
                    {
                        ["riding"] = riding != null, ["passenger"] = VehicleRider.PassengerOf(me) != null, ["attached"] = me.IsAttached(), ["animation"] = AttachAnim(me),
                        ["base"] = GroundVehicle.AttachAnimation, ["pose"] = VehicleRider.Enabled, ["tune"] = JObject.FromObject(VehicleRider.Tune),
                        ["loaded"] = GroundVehicle.Loaded.Count(), ["speed"] = riding != null ? Math.Round(riding.Speed, 1) : 0,
                        ["animatorUpdate"] = me.GetComponentInChildren<Animator>()?.updateMode.ToString(),
                    });
                    return null;

                case "anims":
                {
                    Animator anim = me.GetComponentInChildren<Animator>();
                    if (anim == null) { error("no animator on the player"); return null; }
                    string text = args.Length > 1 ? args[1].ToLowerInvariant() : "";
                    var list = new JArray();
                    foreach (AnimatorControllerParameter p in anim.parameters)
                        if (p.type == AnimatorControllerParameterType.Bool && p.name.ToLowerInvariant().Contains(text)) list.Add(p.name);
                    output(new JObject { ["bools"] = list });
                    return null;
                }

                case "base":
                {
                    if (args.Length < 2) { error("base <attach animation> (see: anims attach)"); return null; }
                    string anim = args[1];
                    GroundVehicle.AttachAnimation = anim;
                    foreach (Chair chair in GroundVehicle.Loaded.Where(v => v != null).Select(v => v.GetComponentInChildren<Chair>(true)).Where(c => c != null)) chair.m_attachAnimation = anim;
                    if (riding != null && me.IsAttached())
                    {
                        string was = AttachAnim(me);
                        ZAnim(me).SetBool(was, false);
                        ZAnim(me).SetBool(anim, true);
                        AttachAnim(me) = anim;   // (so getting off clears this one)
                    }
                    output(new JObject { ["base"] = anim, ["appliedToRider"] = riding != null });
                    return null;
                }

                case "ride":
                {
                    // as pressing E on the nearest vehicle would ("far": from any distance, for a test one placed away from you)
                    bool far = args.Length > 1 && args[1].ToLowerInvariant() == "far";
                    GroundVehicle near = Nearest(me, v => v.Seat != null);
                    if (near == null) { error("none loaded near you"); return null; }
                    near.Mount(me, far);
                    output(new JObject { ["mounting"] = true, ["distance"] = Math.Round(Vector3.Distance(near.transform.position, me.transform.position), 1) });
                    return null;
                }

                case "sit":
                {
                    // the passenger seat of the nearest vehicle, as pressing E on it would (in reach)
                    GroundVehicle near = Nearest(me, v => v.PassengerSeat != null);
                    Chair seat = near != null ? near.GetComponentInChildren<Chair>() : null;
                    if (seat == null) { error("no vehicle with a passenger seat near you"); return null; }
                    seat.Interact(me, false, false);
                    output(new JObject { ["sitting"] = me.IsAttached() });
                    return null;
                }

                case "off":
                    me.StopDoodadControl();
                    if (me.IsAttached()) me.AttachStop();
                    output(new JObject { ["off"] = true });
                    return null;

                case "pose":
                    VehicleRider.Enabled = args.Length < 2 || args[1].ToLowerInvariant() != "off";
                    output(new JObject { ["pose"] = VehicleRider.Enabled });
                    return null;

                case "tune":
                    for (int i = 1; i + 1 < args.Length; i += 2)
                    {
                        if (!VehicleRider.Tune.ContainsKey(args[i])) { error("no such number: " + args[i] + " (they are: " + string.Join(", ", VehicleRider.Tune.Keys) + ")"); continue; }
                        if (float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, inv, out float v)) VehicleRider.Tune[args[i]] = v;
                        else error("not a number: " + args[i + 1]);
                    }
                    output(new JObject { ["tune"] = JObject.FromObject(VehicleRider.Tune) });
                    return null;

                case "bones":
                {
                    if (riding == null) { error("not riding"); return null; }
                    var bones = new JObject();
                    foreach (var kv in VehicleRider.Last) bones[kv.Key] = new JArray(Math.Round(kv.Value.x, 3), Math.Round(kv.Value.y, 3), Math.Round(kv.Value.z, 3));
                    Vector3 root = riding.PoseFrame.InverseTransformPoint(me.transform.position);
                    output(new JObject { ["note"] = "after the pose, metres in the vehicle's frame: x right, y up, z forward", ["playerRoot"] = new JArray(Math.Round(root.x, 3), Math.Round(root.y, 3), Math.Round(root.z, 3)), ["bones"] = bones });
                    return null;
                }
            }
            error("unknown: " + sub);
            return null;
        }
    }
}
