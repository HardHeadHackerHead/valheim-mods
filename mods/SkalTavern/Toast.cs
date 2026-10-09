using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SkalTavern
{
    /// <summary>
    /// The toast (Toast: Key). You raise a cup, and so does everyone near you who has the mod and toasts within a few seconds (the game's own
    /// network call tells them, so it works whoever hosts). Each of you, and each companion of yours standing by (AICompanion), counts as
    /// someone to drink with, and while the toast lasts you all get a Skal! buff: more stamina and health back, up to four to drink with.
    /// </summary>
    internal static class Toast
    {
        private const string Rpc = "DHack_SkalToast";

        private class Other { public long Sender; public string Name; public Vector3 Pos; public float At; }

        private static readonly List<Other> Others = new List<Other>();
        private static float _last = -999f, _nextAllowed;
        private static ZRoutedRpc _on;

        /// <summary>How many you are drinking with, for the buff (read by SE_Skal).</summary>
        public static int Partners;

        // ---- the network ----------------------------------------------------------------------------------------------

        public static void UpdateNetwork()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) { _on = null; return; }
            // After a hot reload the previous copy removes its handler at the end of the frame; wait a moment so we never
            // register first and then get our own handler removed.
            if (Plugin.Instance == null || Time.frameCount <= Plugin.Instance.AwakeFrame + 2) return;
            if (rpc == _on) return;
            _on = rpc;
            Unregister();   // (the game throws if a name is registered twice)
            rpc.Register<Vector3, string>(Rpc, OnToast);
        }

        public static void Unregister()
        {
            if (ZRoutedRpc.instance == null) return;
            var table = AccessTools.Field(typeof(ZRoutedRpc), "m_functions").GetValue(ZRoutedRpc.instance) as IDictionary;
            table?.Remove(Rpc.GetStableHashCode());
            _on = null;
        }

        private static void OnToast(long sender, Vector3 pos, string name)
        {
            if (sender == ZDOMan.GetSessionID()) return;
            Others.RemoveAll(o => o.Sender == sender);
            Others.Add(new Other { Sender = sender, Name = name, Pos = pos, At = Time.time });
            Player player = Player.m_localPlayer;
            if (player != null) Evaluate(player);
        }

        // ---- raising a cup ---------------------------------------------------------------------------------------------

        public static void Raise(Player player)
        {
            if (Time.time < _nextAllowed || player.IsDead()) return;
            _nextAllowed = Time.time + 6f;
            _last = Time.time;
            player.StartEmote("cheer");
            ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, Rpc, player.transform.position, player.GetPlayerName());
            if (!Evaluate(player)) player.Message(MessageHud.MessageType.Center, "Skal! A toast is better with company.");
        }

        /// <summary>Who you are drinking with, and the buff for it. False when there is no one.</summary>
        private static bool Evaluate(Player player)
        {
            float window = Plugin.ToastSeconds.Value;
            if (Time.time - _last > window) return false;   // (you have not raised a cup yourself)
            Others.RemoveAll(o => Time.time - o.At > window + 4f);
            List<Other> near = Others.Where(o => Time.time - o.At <= window && Vector3.Distance(o.Pos, player.transform.position) < 15f).ToList();
            int companions = Character.GetAllCharacters().Count(c => c != null && !c.IsDead() && Vector3.Distance(c.transform.position, player.transform.position) < 12f
                                                                     && (c.GetComponent<ZNetView>()?.GetZDO()?.GetLong("dhc_id", 0L) ?? 0L) != 0L);
            int partners = near.Count + Mathf.Min(companions, 3);
            if (partners <= 0) return false;

            Partners = Mathf.Min(partners, 4);
            player.GetSEMan().AddStatusEffect(Drinks.SkalHash, true);
            var who = near.Select(o => o.Name).ToList();
            if (companions > 0) who.Add(companions == 1 ? "your companion" : "your companions");
            player.Message(MessageHud.MessageType.Center, "Skal! You drink with " + string.Join(" and ", who));
            return true;
        }
    }

    /// <summary>The toast's buff: stamina and health come back faster, more with more to drink with.</summary>
    public class SE_Skal : SE_Stats
    {
        public override void Setup(Character character)
        {
            m_ttl = 300f;
            int n = Mathf.Clamp(Toast.Partners, 1, 4);
            m_staminaRegenMultiplier = 1f + 0.10f * n;
            m_healthRegenMultiplier = 1f + 0.08f * n;
            base.Setup(character);
        }

        public override string GetTooltipString() => "Skal! You raised a cup with friends: stamina and health come back faster, and more with more to drink with.";
    }
}
