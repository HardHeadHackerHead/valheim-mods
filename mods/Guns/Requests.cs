using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Guns
{
    /// <summary>
    /// A command for Claude Tools (found while the game runs, no reference between the two): `guns fire [seconds]` holds the trigger of
    /// the gun in hand, as the player would, so a gun's look and sound can be checked with pictures. It fires real rounds (and flame) where the
    /// player looks. Registered again whenever Claude Tools is (re)loaded.
    /// </summary>
    internal static class Requests
    {
        private const string ClaudeToolsGuid = "com.quad.claudetools";
        private static BaseUnityPlugin _claudeTools;
        private static float _nextCheck;

        internal static void Tick()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 5f;
            BaseUnityPlugin found = Chainloader.PluginInfos.TryGetValue(ClaudeToolsGuid, out PluginInfo info) ? info.Instance : null;
            if (found == _claudeTools) return;
            _claudeTools = found;
            if (found == null) return;
            try
            {
                MethodInfo register = found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static);
                if (register == null) return;
                Func<string[], Action<JObject>, Action<string>, IEnumerator> run = CmdGuns;
                register.Invoke(null, new object[] { Plugin.Name, "guns", "guns fire [seconds=2]: hold the trigger of the gun in hand for that long, as the player would (real rounds, where the player looks); returns at once | guns last: rounds used and how far the flame reached | guns others: the gun this game sees in each other player's hands", run });
            }
            catch (Exception e) { Plugin.Log.LogWarning("could not add the guns command to Claude Tools: " + e.Message); }
        }

        internal static void Unregister()
        {
            try { _claudeTools?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Plugin.Name }); }
            catch (Exception) { }
            _claudeTools = null;
        }

        private static ItemDrop.ItemData _item;
        private static GunDef _def;
        private static int _before;
        private static float _seconds;

        private static IEnumerator CmdGuns(string[] a, Action<JObject> output, Action<string> error)
        {
            string verb = a.Length > 1 ? a[1] : "";
            if (verb == "last")
            {
                if (_def == null) { error("nothing fired yet"); return null; }
                var o = new JObject { ["gun"] = _def.Id, ["seconds"] = _seconds, ["rounds_before"] = _before, ["rounds_now"] = Ammo.Rounds(_item, _def), ["holding"] = Time.time < Fire.HoldUntil };
                if (_def.Flame) o["flame_reach_m"] = Math.Round(Flame.Reach, 2);
                output(o);
                return null;
            }
            if (verb == "others")
            {
                var list = new JArray();
                foreach (Player other in Player.GetAllPlayers())
                {
                    if (other == null || other == Player.m_localPlayer) continue;
                    ZDO zdo = Share.ZdoOf(other);
                    VisEquipment ve = other.GetComponent<VisEquipment>();
                    list.Add(new JObject
                    {
                        ["name"] = other.GetPlayerName(),
                        ["distance_m"] = Player.m_localPlayer != null ? Math.Round(Vector3.Distance(other.transform.position, Player.m_localPlayer.transform.position), 1) : -1,
                        ["right_item_hash_zdo"] = zdo != null ? zdo.GetInt(ZDOVars.s_rightItem) : 0,
                        ["right_item_field"] = ve != null ? HarmonyLib.Traverse.Create(ve).Field("m_rightItem").GetValue<int>() : 0,
                        ["gun"] = Arsenal.HeldBy(other)?.Id,
                    });
                }
                output(new JObject { ["others"] = list });
                return null;
            }
            if (verb != "fire") { error("usage: guns fire [seconds=2] | guns last | guns others"); return null; }
            Player p = Player.m_localPlayer;
            GunDef d = Arsenal.Held(p, out ItemDrop.ItemData item);
            if (d == null) { error("no gun in hand"); return null; }
            _seconds = a.Length > 2 && float.TryParse(a[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s) ? Mathf.Clamp(s, 0.1f, 20f) : 2f;
            _item = item; _def = d; _before = Ammo.Rounds(item, d);
            Flame.Reach = 0f;
            Fire.HoldUntil = Time.time + _seconds;   // (returns at once, so pictures can be taken while it fires; `guns last` tells what came of it)
            output(new JObject { ["firing"] = d.Id, ["seconds"] = _seconds, ["rounds"] = _before });
            return null;
        }
    }
}
