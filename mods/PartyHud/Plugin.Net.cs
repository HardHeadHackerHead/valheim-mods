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
            public List<KeyValuePair<int, float>> Effects = new List<KeyValuePair<int, float>>(); // status effect name hash -> seconds left
            public float EffectsAt;
            public bool FoodsKnown; // their game sends food (older versions of the mod do not)
            public List<RawFood> Foods = new List<RawFood>(); // what they are eating
        }

        private struct RawFood { public string Prefab; public float Time, Burn; }

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
                EffectsText(me),
                FoodsText(me),
            });

            try { ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcStats, payload); }
            catch (Exception e) { Logger.LogWarning("Could not share stats: " + e.Message); }
        }

        private static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        // ---- status effects (buffs and debuffs) ---------------------------------------------------

        /// <summary>Our active status effects as "hash:seconds,hash:seconds" (the effect is looked up by its name hash on the other side).</summary>
        private static string EffectsText(Player me)
        {
            var parts = new List<string>();
            foreach (StatusEffect se in me.GetSEMan().GetStatusEffects())
            {
                if (se == null || se.m_icon == null) continue; // hidden ones have no picture
                parts.Add(se.NameHash() + ":" + F(Mathf.Max(0f, se.GetRemaningTime())));
                if (parts.Count >= 12) break;
            }
            return string.Join(",", parts.ToArray());
        }

        private static List<KeyValuePair<int, float>> ParseEffects(string text)
        {
            var list = new List<KeyValuePair<int, float>>();
            foreach (string part in (text ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = part.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[0], out int hash)) list.Add(new KeyValuePair<int, float>(hash, ParseFloat(kv[1])));
            }
            return list;
        }

        /// <summary>The picture and how much of its time is left (0 to 1; 1 when it has no time limit) for each effect.</summary>
        private static List<Effect> ToEffects(List<KeyValuePair<int, float>> raw, float ageSeconds)
        {
            var result = new List<Effect>();
            if (ObjectDB.instance == null) return result;
            foreach (var kv in raw)
            {
                StatusEffect se = ObjectDB.instance.GetStatusEffect(kv.Key);
                if (se == null || se.m_icon == null) continue;
                float left = Mathf.Max(0f, kv.Value - ageSeconds);
                result.Add(new Effect { Icon = se.m_icon, Fraction = se.m_ttl > 0f ? Mathf.Clamp01(left / se.m_ttl) : 1f });
            }
            return result;
        }


        // ---- food ---------------------------------------------------------------------------------

        /// <summary>The food we are eating (up to three) as "item:seconds left:total seconds,...".</summary>
        private static string FoodsText(Player me)
        {
            var parts = new List<string>();
            foreach (Player.Food food in me.GetFoods())
            {
                if (food == null || food.m_item == null || food.m_item.m_dropPrefab == null) continue;
                parts.Add(food.m_item.m_dropPrefab.name + ":" + F(Mathf.Max(0f, food.m_time)) + ":" + F(food.m_item.m_shared.m_foodBurnTime));
                if (parts.Count >= 3) break;
            }
            return string.Join(",", parts.ToArray());
        }

        private static List<RawFood> ParseFoods(string text)
        {
            var list = new List<RawFood>();
            foreach (string part in (text ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = part.Split(':');
                if (f.Length == 3) list.Add(new RawFood { Prefab = f[0], Time = ParseFloat(f[1]), Burn = ParseFloat(f[2]) });
            }
            return list;
        }

        /// <summary>The picture and the share of its time left, for each food (the time keeps counting down between messages).</summary>
        private static List<FoodSlot> ToFoods(List<RawFood> raw, float ageSeconds)
        {
            var result = new List<FoodSlot>();
            if (ObjectDB.instance == null) return result;
            foreach (RawFood food in raw)
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(food.Prefab);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                float left = Mathf.Max(0f, food.Time - ageSeconds);
                result.Add(new FoodSlot { Icon = drop.m_itemData.GetIcon(), Fraction = food.Burn > 0f ? Mathf.Clamp01(left / food.Burn) : 0f });
            }
            return result;
        }

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
            r.Effects = f.Length > 7 ? ParseEffects(f[7]) : new List<KeyValuePair<int, float>>();
            r.EffectsAt = Time.time;
            r.Foods = f.Length > 8 ? ParseFoods(f[8]) : new List<RawFood>();
            r.FoodsKnown = f.Length > 8;
            r.Seen = Time.time;
        }
    }
}
