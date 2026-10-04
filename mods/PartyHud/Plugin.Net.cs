using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace PartyHud
{
    /// <summary>Sending our own numbers to the other players, and receiving theirs.</summary>
    public partial class Plugin
    {
        private const string RpcStats = "DHack_PartyStats";

        /// <summary>What another player's game told us about them.</summary>
        private class Remote
        {
            public string Name;
            public float Hp, MaxHp, St, MaxSt, Eitr, MaxEitr;
            public float Seen = -999f;
        }

        private readonly Dictionary<long, Remote> _remote = new Dictionary<long, Remote>();
        private ZRoutedRpc _registeredOn;
        private float _nextSend;

        private void UpdateNetwork()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) { _registeredOn = null; return; }

            // After a hot reload the previous copy removes its handlers at the end of the frame; wait a moment so we
            // never register first and then get our own handlers removed.
            if (Time.frameCount <= _awakeFrame + 2) return;

            if (rpc != _registeredOn)
            {
                _registeredOn = rpc;
                _remote.Clear();
                UnregisterRpc(); // Valheim throws if a handler name is registered twice
                rpc.Register<string>(RpcStats, OnStats);
            }

            Player me = Player.m_localPlayer;
            if (me != null && Time.time >= _nextSend)
            {
                _nextSend = Time.time + 0.33f;
                SendStats(me);
            }
        }

        /// <summary>Valheim has no "unregister", so remove our handler from its table directly (needed for hot reload).</summary>
        private static void UnregisterRpc()
        {
            if (ZRoutedRpc.instance == null) return;
            var table = AccessTools.Field(typeof(ZRoutedRpc), "m_functions").GetValue(ZRoutedRpc.instance) as IDictionary;
            table?.Remove(RpcStats.GetStableHashCode());
        }

        private void SendStats(Player me)
        {
            // Nobody to tell if we're alone in the world.
            if (ZNet.instance == null || ZNet.instance.GetPlayerList().Count < 2) return;

            string payload = string.Join("|", new[]
            {
                me.GetPlayerName(),
                F(me.GetHealth()), F(me.GetMaxHealth()),
                F(me.GetStamina()), F(me.GetMaxStamina()),
                F(me.GetEitr()), F(me.GetMaxEitr()),
            });

            try { ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcStats, payload); }
            catch (Exception e) { Logger.LogWarning("Could not share stats: " + e.Message); }
        }

        private static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        private void OnStats(long sender, string payload)
        {
            if (sender == MyId) return;
            string[] f = (payload ?? "").Split('|');
            if (f.Length < 7) return;

            if (!_remote.TryGetValue(sender, out Remote r)) _remote[sender] = r = new Remote();
            r.Name = f[0];
            r.Hp = ParseFloat(f[1]); r.MaxHp = ParseFloat(f[2]);
            r.St = ParseFloat(f[3]); r.MaxSt = ParseFloat(f[4]);
            r.Eitr = ParseFloat(f[5]); r.MaxEitr = ParseFloat(f[6]);
            r.Seen = Time.time;
        }
    }
}
