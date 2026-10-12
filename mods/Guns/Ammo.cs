using UnityEngine;

namespace Guns
{
    /// <summary>
    /// Magazines and reloading. There is no ammo item yet: a gun just holds the rounds in its magazine (kept on the item, so it is saved with
    /// it and travels with it), and reloading fills it for free. Reload with the secondary attack button (middle mouse), or pull the trigger
    /// on an empty gun. A gun that loads one round at a time (the shotgun, the lever action) can be fired between rounds. Swapping to
    /// another weapon cancels the reload and loses nothing.
    /// </summary>
    internal static class Ammo
    {
        private const string Key = "dh_mag";

        private static ItemDrop.ItemData _item;
        private static GunDef _def;
        private static float _start, _end, _stepStart;
        private static int _stage;

        internal static bool Reloading => _item != null;

        /// <summary>The reload's progress, 0 to 1 (of the whole reload, or of the round being loaded).</summary>
        internal static float Progress => _item == null ? 0f : Mathf.Clamp01((Time.time - _stepStart) / Mathf.Max(0.01f, _end - _stepStart));

        /// <summary>A smooth 0-1-0 over the reload, for the gun dipping while the magazine is changed.</summary>
        internal static float Dip => _item == null ? 0f : Mathf.Sin(Mathf.Clamp01((Time.time - _start) / Mathf.Max(0.01f, _end - _start)) * Mathf.PI);

        internal static int Rounds(ItemDrop.ItemData item, GunDef d)
        {
            int full = d.MagNow;
            if (item?.m_customData != null && item.m_customData.TryGetValue(Key, out string s) && int.TryParse(s, out int n)) return Mathf.Clamp(n, 0, full);
            return full;   // a new gun, or one never fired, is full
        }

        internal static void Set(ItemDrop.ItemData item, int rounds) => item.m_customData[Key] = Mathf.Max(0, rounds).ToString();

        /// <summary>Takes one round out; returns false when there is none.</summary>
        internal static bool Spend(ItemDrop.ItemData item, GunDef d, int n = 1)
        {
            int have = Rounds(item, d);
            if (have <= 0) return false;
            Set(item, have - n);
            return true;
        }

        /// <summary>Starts reloading (the secondary attack button, or an empty trigger). Returns whether a reload is going.</summary>
        internal static bool Begin(Player p, ItemDrop.ItemData item, GunDef d)
        {
            if (_item != null) return true;
            if (Rounds(item, d) >= d.MagNow) return false;
            _item = item; _def = d;
            _start = _stepStart = Time.time;
            _end = _start + Mathf.Max(0.1f, d.ReloadNow);
            _stage = 0;
            return true;
        }

        internal static void Cancel() { _item = null; _def = null; }

        /// <summary>Every frame: the reload goes on, finishes, or is dropped when the gun is put away.</summary>
        internal static void Tick(Player p)
        {
            if (_item == null) return;
            if (p == null || p.IsDead() || p.GetCurrentWeapon() != _item) { Cancel(); return; }
            float now = Time.time;
            Vector3 at = p.transform.position + Vector3.up;
            if (_def.PerRound)
            {
                if (now < _end) return;
                int rounds = Rounds(_item, _def) + 1;
                Set(_item, rounds);
                Fx.Click(_def.LoadClick, at);
                if (rounds >= _def.MagNow) { Fx.Click(_def.CycleClick ?? Sound.Click.Rack, at); Cancel(); return; }
                _stepStart = now;
                _end = now + Mathf.Max(0.1f, _def.ReloadNow);
                return;
            }
            float t = (now - _start) / Mathf.Max(0.01f, _end - _start);
            if (_def.ReloadClip != null && Fx.HasReload(_def.ReloadClip))   // a recording of the whole reload (a pair: the second pistol a moment after the first)
            {
                float[] starts = { 0f, 0.14f };
                while (_stage < (_def.Dual ? 2 : 1) && t >= starts[_stage]) { Fx.Reload(_def.ReloadClip, at); _stage++; }
                if (now >= _end) { Set(_item, _def.MagNow); Cancel(); }
                return;
            }
            Cue[] cues = _def.Cues;
            while (cues != null && _stage < cues.Length && t >= cues[_stage].At) Fx.Click(cues[_stage++].Click, at);   // the sounds of the reload, each at its time
            if (now >= _end) { Set(_item, _def.MagNow); Cancel(); }
        }
    }
}
