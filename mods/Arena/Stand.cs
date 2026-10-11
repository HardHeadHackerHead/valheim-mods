using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Arena
{
    /// <summary>
    /// An Arena Waystone. Built anywhere (hammer, Misc), it takes you to the Arena's forecourt. The one standing in the forecourt itself
    /// (part of the arena) takes you back to the waystone you came by, or up into the stands to watch.
    /// </summary>
    internal class Stand : MonoBehaviour, Hoverable, Interactable
    {
        public bool Arrival;

        public string GetHoverText()
        {
            if (Arrival)
                return Localization.instance.Localize("$arena_waystone\n[<color=yellow><b>$KEY_Use</b></color>] $arena_hover_home");
            string where = Site.Known ? T("arena_km_away", (Travel.Distance(transform.position) / 1000f).ToString("0.0")) : T("arena_being_found");
            string busy = Net.RemoteFight ? $"\n<color=#e8a060>{Net.BusyText}</color>" : "";
            string best = Ladder.Champion("road", out Ladder.Entry champ) ? $"\n<color=#e8c060>{T("arena_hover_road_champion", champ.Name)}</color>" : "";
            return Localization.instance.Localize("$arena_waystone\n[<color=yellow><b>$KEY_Use</b></color>] ") + T("arena_hover_travel", where) + busy + best;
        }

        public string GetHoverName() => T("arena_waystone");

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer) return false;
            if (Contest.Active || Duel.Active) { Hud.Say(T("arena_not_in_fight")); return true; }
            Window.Open(Arrival ? Window.Context.Arrival : Window.Context.Waystone, this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }

    /// <summary>Going to the arena and back by waystone. Where you left from is kept on your character, so the way home survives a restart.</summary>
    internal static class Travel
    {
        private const string ReturnKey = "dh_arena_return";

        internal static float Distance(Vector3 from) => Site.Known ? new Vector2(from.x - Site.Origin.x, from.z - Site.Origin.z).magnitude : 0f;

        internal static void ToArena(Stand from)
        {
            Player p = Player.m_localPlayer;
            if (p == null || !Site.Known) return;
            Vector3 back = from.transform.position + from.transform.forward * 2.2f;
            p.m_customData[ReturnKey] = $"{back.x.ToString(System.Globalization.CultureInfo.InvariantCulture)},{back.y.ToString(System.Globalization.CultureInfo.InvariantCulture)},{back.z.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            Go(p, Site.Point(Layout.Arrival, 0.3f) + Site.Turn * new Vector3(0f, 0f, 2.6f), Site.Turn);
        }

        internal static bool HasReturn(out Vector3 back)
        {
            back = Vector3.zero;
            Player p = Player.m_localPlayer;
            if (p == null || !p.m_customData.TryGetValue(ReturnKey, out string v)) return false;
            string[] f = v.Split(',');
            if (f.Length < 3) return false;
            var c = System.Globalization.CultureInfo.InvariantCulture;
            if (!float.TryParse(f[0], System.Globalization.NumberStyles.Float, c, out back.x) || !float.TryParse(f[1], System.Globalization.NumberStyles.Float, c, out back.y)
                || !float.TryParse(f[2], System.Globalization.NumberStyles.Float, c, out back.z)) return false;
            return true;
        }

        internal static void Home()
        {
            Player p = Player.m_localPlayer;
            if (p == null || !HasReturn(out Vector3 back)) return;
            Go(p, back, p.transform.rotation);
        }

        internal static void ToStands()
        {
            Player p = Player.m_localPlayer;
            if (p == null || !Site.Known) return;
            Go(p, Site.Point(Layout.Stands, 0.3f), Site.Facing(Layout.Stands));
        }

        private static void Go(Player p, Vector3 to, Quaternion facing)
        {
            if (!p.IsTeleportable(false) && !p.IsDebugFlying()) { p.Message(MessageHud.MessageType.Center, "$msg_noteleport"); return; }
            p.TeleportTo(to, facing, true);
        }
    }
}
