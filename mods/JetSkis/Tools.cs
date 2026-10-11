using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace JetSkis
{
    /// <summary>
    /// A "jetski" command for the Claude Tools mod (its request mailbox), for working on the rider's pose while someone sits on a jet ski:
    /// which seat animations the game has, trying another, the pose's numbers changed live, and where the rider's hands and feet are.
    /// Only "spawn" (a test jet ski) needs cheat rights; the rest changes how things look on this game, or does what pressing E does.
    /// </summary>
    internal static class Tools
    {
        private static BaseUnityPlugin _claudeTools;
        private static float _next;
        private static readonly System.Collections.Generic.List<GameObject> Spawned = new System.Collections.Generic.List<GameObject>();   // test jet skis, for "clear"
        private static readonly AccessTools.FieldRef<Player, string> AttachAnim = AccessTools.FieldRefAccess<Player, string>("m_attachAnimation");
        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation> ZAnim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");

        public static void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 5f;
            BaseUnityPlugin found = Chainloader.PluginInfos.TryGetValue("com.quad.claudetools", out PluginInfo info) ? info.Instance : null;
            if (found == _claudeTools) return;
            _claudeTools = found;
            if (found == null) return;
            try
            {
                MethodInfo register = found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static);
                register?.Invoke(null, new object[] { Plugin.Name, "jetski", "jetski status | anims [text] | spawn [metres ahead | at <x> <z>] | clear | remove | ride [far] | sit | off | float | paint | all | bags | base <attach animation> | pose on|off | tune [<key> <value>]... | bones: the rider's pose on a jet ski, for working on it",
                    (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Command });
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not add the jetski command to Claude Tools: " + e.Message); }
        }

        private static bool CheatsAllowed() =>
            ZNet.instance == null || ZNet.instance.IsServer() || (global::Console.instance != null && global::Console.instance.IsCheatsEnabled());

        public static void Unregister()
        {
            try { _claudeTools?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Plugin.Name }); }
            catch (Exception) { }
        }

        private static IEnumerator Command(string[] args, Action<JObject> output, Action<string> error)
        {
            Player me = Player.m_localPlayer;
            if (me == null) { error("no player in the world"); return null; }
            args = args.Skip(1).ToArray();
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            JetSki riding = JetSki.RidingOf(me);

            switch (sub)
            {
                case "status":
                    output(new JObject
                    {
                        ["riding"] = riding != null, ["attached"] = me.IsAttached(), ["animation"] = AttachAnim(me),
                        ["base"] = Things.AttachAnimation, ["pose"] = Rider.Enabled, ["tune"] = JObject.FromObject(Rider.Tune),
                        ["jetskisLoaded"] = JetSki.Loaded.Count(),
                        ["animatorUpdate"] = me.GetComponentInChildren<Animator>()?.updateMode.ToString(),
                        ["cloth"] = new JArray(me.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name.Contains("Cloth")).Select(c => c.GetType().FullName + " on " + c.name).Distinct().Take(12)),
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
                    if (args.Length < 2) { error("base <attach animation> (see: jetski anims attach)"); return null; }
                    string anim = args[1];
                    Things.AttachAnimation = anim;
                    foreach (JetSki ski in JetSki.Loaded) if (ski != null && ski.Controls != null) ski.Controls.m_attachAnimation = anim;
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

                case "spawn":
                {
                    // a test jet ski on the ground (or water) 3 m in front of the player, facing the way they face
                    if (!CheatsAllowed()) { error("jetski spawn puts things in the world: single player, the host, or devcommands only"); return null; }
                    GameObject prefab = ZNetScene.instance?.GetPrefab(Things.Prefab);
                    if (prefab == null) { error("the jet ski isn't registered"); return null; }
                    Vector3 fwd = Vector3.ProjectOnPlane(me.transform.forward, Vector3.up).normalized;
                    float ahead = args.Length > 1 && float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float m) ? Mathf.Clamp(m, 2f, 20f) : 3f;
                    Vector3 at = me.transform.position + fwd * ahead;
                    if (args.Length > 3 && args[1].ToLowerInvariant() == "at"   // "spawn at <x> <z>": a spot in the world (within 40 m)
                        && float.TryParse(args[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float wx)
                        && float.TryParse(args[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float wz))
                    {
                        at = new Vector3(wx, me.transform.position.y, wz);
                        if (Vector3.Distance(at, me.transform.position) > 40f) { error("spawn at: within 40 m of you"); return null; }
                    }
                    WaterVolume wv = null;
                    at.y = Mathf.Max(at.y, Floating.GetWaterLevel(at, ref wv)) + 0.6f;   // (on the water if there is water there)
                    GameObject go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.LookRotation(fwd, Vector3.up));
                    Spawned.Add(go);
                    output(new JObject { ["spawned"] = new JArray(Math.Round(at.x, 1), Math.Round(at.y, 1), Math.Round(at.z, 1)) });
                    return null;
                }

                case "clear":
                {
                    int n = 0;
                    foreach (GameObject go in Spawned)
                    {
                        if (go == null) continue;
                        ZNetView view = go.GetComponent<ZNetView>();
                        if (view != null && view.IsValid()) { view.ClaimOwnership(); ZNetScene.instance.Destroy(go); n++; }
                    }
                    Spawned.Clear();
                    output(new JObject { ["removed"] = n });
                    return null;
                }

                case "remove":
                {
                    // the nearest jet ski within 6 m (a test one gone wrong), the player taken off it first
                    if (!CheatsAllowed()) { error("jetski remove takes things out of the world: single player, the host, or devcommands only"); return null; }
                    JetSki near = JetSki.Loaded.Where(j => j != null).OrderBy(j => Vector3.Distance(j.transform.position, me.transform.position)).FirstOrDefault();
                    if (near == null || Vector3.Distance(near.transform.position, me.transform.position) > 6f) { error("no jet ski within 6 m"); return null; }
                    if (riding == near) { me.StopDoodadControl(); if (me.IsAttached()) me.AttachStop(); }
                    ZNetView view = near.GetComponent<ZNetView>();
                    view.ClaimOwnership();
                    ZNetScene.instance.Destroy(near.gameObject);
                    output(new JObject { ["removed"] = true });
                    return null;
                }

                case "ride":
                {
                    // as pressing E on the nearest jet ski would (it has to be in reach; "far": any distance, for a test one out on the water)
                    bool far = args.Length > 1 && args[1].ToLowerInvariant() == "far";
                    if (far && !CheatsAllowed()) { error("jetski ride far moves the player: single player, the host, or devcommands only"); return null; }
                    JetSki near = JetSki.Loaded.Where(j => j != null && j.Controls != null)
                        .OrderBy(j => Vector3.Distance(j.transform.position, me.transform.position)).FirstOrDefault();
                    if (near == null) { error("no jet ski loaded near you"); return null; }
                    near.Mount(me, far);
                    output(new JObject { ["mounting"] = true, ["distance"] = Math.Round(Vector3.Distance(near.transform.position, me.transform.position), 1) });
                    return null;
                }

                case "sit":
                {
                    // the passenger seat of the nearest jet ski, as pressing E on it would (in reach)
                    JetSki near = JetSki.Loaded.Where(j => j != null && j.PassengerSeat != null).OrderBy(j => Vector3.Distance(j.transform.position, me.transform.position)).FirstOrDefault();
                    Chair seat = near != null ? near.GetComponentInChildren<Chair>() : null;
                    if (seat == null) { error("no jet ski with a passenger seat near you"); return null; }
                    seat.Interact(me, false, false);
                    output(new JObject { ["sitting"] = me.IsAttached() });
                    return null;
                }

                case "off":
                    me.StopDoodadControl();
                    if (me.IsAttached()) me.AttachStop();
                    output(new JObject { ["off"] = true });
                    return null;

                case "float":
                {
                    // how the nearest jet ski sits in the water
                    JetSki near = JetSki.Loaded.Where(j => j != null).OrderBy(j => Vector3.Distance(j.transform.position, me.transform.position)).FirstOrDefault();
                    if (near == null) { error("no jet ski loaded near you"); return null; }
                    Ship ship = near.GetComponent<Ship>();
                    Rigidbody body = near.GetComponent<Rigidbody>();
                    WaterVolume cache = null;
                    float water = Floating.GetWaterLevel(near.transform.position, ref cache);
                    Vector3 com = near.transform.InverseTransformPoint(body.worldCenterOfMass);
                    output(new JObject
                    {
                        ["originY"] = Math.Round(near.transform.position.y, 3), ["water"] = Math.Round(water, 3),
                        ["originBelowWater"] = Math.Round(water - near.transform.position.y, 3),
                        ["centreOfMass"] = new JArray(Math.Round(com.x, 3), Math.Round(com.y, 3), Math.Round(com.z, 3)),
                        ["waterLevelOffset"] = ship.m_waterLevelOffset, ["force"] = ship.m_force, ["forceDistance"] = ship.m_forceDistance,
                        ["disableLevel"] = ship.m_disableLevel, ["mass"] = body.mass, ["autoCentre"] = body.automaticCenterOfMass,
                        ["owner"] = near.GetComponent<ZNetView>().IsOwner(), ["shipEnabled"] = ship.enabled, ["kinematic"] = body.isKinematic, ["sleeping"] = body.IsSleeping(),
                        ["roll"] = Math.Round(Vector3.SignedAngle(Vector3.ProjectOnPlane(Vector3.up, near.transform.forward), near.transform.up, near.transform.forward), 1), ["pitch"] = Math.Round(-Mathf.Asin(near.transform.forward.y) * Mathf.Rad2Deg, 1), ["speed"] = Math.Round(near.Speed, 1), ["rudder"] = Math.Round(near.GetComponent<Ship>().GetRudderValue(), 2),
                        ["motorCalls"] = near.MotorCalls, ["floatSet"] = near.FloatSet, ["velocity"] = Math.Round(body.linearVelocity.magnitude, 2), ["isMine"] = Spawned.Contains(near.gameObject),
                    });
                    return null;
                }

                case "paint":
                {
                    // as E on the nearest jet ski's cowl would: its next colour
                    JetSki near = JetSki.Loaded.Where(j => j != null).OrderBy(j => Vector3.Distance(j.transform.position, me.transform.position)).FirstOrDefault();
                    if (near == null) { error("no jet ski loaded near you"); return null; }
                    near.NextPaint(me);
                    output(new JObject { ["was"] = near.PaintIndex, ["note"] = "the owner's game changes it; read it again in a moment" });
                    return null;
                }

                case "all":
                {
                    // every jet ski loaded: where, who is on it, how full its saddlebags are
                    var list = new JArray();
                    foreach (JetSki j in JetSki.Loaded.Where(j => j != null))
                    {
                        Container c = j.GetComponentInChildren<Container>(true);
                        list.Add(new JObject
                        {
                            ["at"] = new JArray(Math.Round(j.transform.position.x, 1), Math.Round(j.transform.position.y, 1), Math.Round(j.transform.position.z, 1)),
                            ["rider"] = j.RiderNow()?.GetPlayerName(), ["passenger"] = j.PassengerNow()?.GetPlayerName(),
                            ["bagItems"] = c != null && c.GetInventory() != null ? c.GetInventory().NrOfItems() : -1,
                        });
                    }
                    output(new JObject { ["jetskis"] = list });
                    return null;
                }

                case "bags":
                {
                    // the saddlebags of the jet ski you ride (or the nearest): there, what's in them, where their hit boxes are
                    JetSki near = riding ?? JetSki.Loaded.Where(j => j != null).OrderBy(j => Vector3.Distance(j.transform.position, me.transform.position)).FirstOrDefault();
                    if (near == null) { error("no jet ski loaded near you"); return null; }
                    Container c = near.GetComponentInChildren<Container>(true);
                    var boxes = new JArray();
                    if (c != null)
                        foreach (Collider col in c.GetComponentsInChildren<Collider>(true))
                        {
                            Vector3 at = near.transform.InverseTransformPoint(col.bounds.center);
                            boxes.Add(new JObject { ["name"] = col.name, ["layer"] = LayerMask.LayerToName(col.gameObject.layer), ["enabled"] = col.enabled && col.gameObject.activeInHierarchy, ["at"] = new JArray(Math.Round(at.x, 2), Math.Round(at.y, 2), Math.Round(at.z, 2)) });
                        }
                    output(new JObject
                    {
                        ["hasBags"] = c != null, ["items"] = c != null && c.GetInventory() != null ? c.GetInventory().NrOfItems() : -1,
                        ["slots"] = c != null ? c.m_width * c.m_height : 0, ["hover"] = c != null ? c.GetHoverText() : null, ["boxes"] = boxes,
                        ["distance"] = Math.Round(Vector3.Distance(near.transform.position, me.transform.position), 1),
                    });
                    return null;
                }

                case "showpaint":
                {
                    // the build menu's copy (what render draws) in one of the colours, for comparing them; "showpaint 0" puts red back
                    int n = args.Length > 1 && int.TryParse(args[1], out int k) ? Mathf.Clamp(k, 0, Effects.Paints.Length - 1) : 0;
                    Look.Of("Paint").color = Effects.Paints[n];
                    output(new JObject { ["prefabPaint"] = Effects.PaintKeys[n] });
                    return null;
                }

                case "pose":
                    Rider.Enabled = args.Length < 2 || args[1].ToLowerInvariant() != "off";
                    output(new JObject { ["pose"] = Rider.Enabled });
                    return null;

                case "tune":
                    for (int i = 1; i + 1 < args.Length; i += 2)
                    {
                        if (!Rider.Tune.ContainsKey(args[i])) { error("no such number: " + args[i] + " (they are: " + string.Join(", ", Rider.Tune.Keys) + ")"); continue; }
                        if (float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) Rider.Tune[args[i]] = v;
                        else error("not a number: " + args[i + 1]);
                    }
                    output(new JObject { ["tune"] = JObject.FromObject(Rider.Tune) });
                    return null;

                case "bones":
                {
                    if (riding == null || riding.Body == null) { error("not riding a jet ski"); return null; }
                    var bones = new JObject();
                    foreach (var kv in Rider.Last) bones[kv.Key] = new JArray(Math.Round(kv.Value.x, 3), Math.Round(kv.Value.y, 3), Math.Round(kv.Value.z, 3));
                    Vector3 seat = riding.Body.InverseTransformPoint(me.transform.position);
                    output(new JObject { ["note"] = "after the pose, metres in the jet ski's body: x right, y up, z forward", ["playerRoot"] = new JArray(Math.Round(seat.x, 3), Math.Round(seat.y, 3), Math.Round(seat.z, 3)), ["bones"] = bones });
                    return null;
                }
            }
            error("unknown: jetski " + sub);
            return null;
        }
    }
}
