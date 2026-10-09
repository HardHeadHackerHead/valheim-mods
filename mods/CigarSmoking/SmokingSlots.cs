using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CigarSmoking
{
    // An instance owns its registrations: add-ons register again when the live plugin changes after F6.
    internal sealed class SmokingSlots
    {
        private readonly HashSet<string> _extra = new HashSet<string>(StringComparer.Ordinal);
        internal bool Register(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 128) return false;
            if (_extra.Contains(name)) return true;
            if (_extra.Count >= 32) return false;
            return _extra.Add(name);
        }
        internal void Unregister(string name) { if (name != null) _extra.Remove(name); }
        internal bool StopOthers(Character character, string keep)
        {
            string[] names = Types.All.Select(t => Plugin.EffectPrefix + t.Id).Concat(_extra).Distinct().ToArray();
            if (character == null || !names.Contains(keep)) return false;
            int retained = keep.GetStableHashCode();
            SEMan effects = character.GetSEMan();
            // Snapshot before callbacks: stopping an effect may unregister its add-on.
            foreach (string name in names)
            {
                int hash = name.GetStableHashCode();
                if (hash != retained && effects.HaveStatusEffect(hash)) effects.RemoveStatusEffect(hash, true);
            }
            return true;
        }
    }
}
