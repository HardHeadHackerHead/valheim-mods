using UnityEngine;
using static DHack.Shared.Translations;

namespace Dungeons
{
    /// <summary>A lever in a dungeon: "Pull lever" (the game's words, from its Obliterator), the game's lever sound, and the pull sent to the
    /// entrance's owner, who decides (MechSet.UseFrom).</summary>
    internal class DungeonLever : MonoBehaviour, Hoverable, Interactable
    {
        private MechSet _set;
        private string _key;
        private float _next;

        public void Init(MechSet set, string key)
        {
            _set = set;
            _key = key;
        }

        public string GetHoverText()
        {
            if (_set == null) return "";
            if (_set.ByKey.TryGetValue(_key, out Mech locked) && _set.LockedLever(locked))
                return Localization.instance.Localize($"{T("dungeons_lever")}\n<color=#e8a060>{T("dungeons_lever_locked")}</color>");
            if (_set.ByKey.TryGetValue(_key, out Mech m) && _set.IsDone(m))
                return Localization.instance.Localize($"{T("dungeons_lever")}\n<color=#a0a0a0>{T("dungeons_lever_done")}</color>");
            return Localization.instance.Localize($"{T("dungeons_lever")}\n[<color=yellow><b>$KEY_Use</b></color>] $piece_pulllever");
        }

        public string GetHoverName() => T("dungeons_lever");

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || _set == null || user != Player.m_localPlayer || Time.time < _next) return false;
            if (_set.ByKey.TryGetValue(_key, out Mech done) && _set.IsDone(done)) return false;   // (a one-shot lever, pulled: it stays down)
            if (done != null && _set.LockedLever(done))
            {
                user.Message(MessageHud.MessageType.Center, T("dungeons_lever_locked"));
                return false;
            }
            Site site = _set.Site;
            if (site?.Zdo == null) return false;
            _next = Time.time + 0.5f;
            if (_set.ByKey.TryGetValue(_key, out Mech m))
            {
                m.PulledHere = Time.time;
                MechSet.Play(m, MechSet.LeverFx());
            }
            site.Act("use:" + _key);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
