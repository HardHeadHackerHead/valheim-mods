using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Quiver
{
    /// <summary>
    /// Arrows that hit a living creature, kept with it until it dies, then dropped at its body with its loot. The shooter's own game keeps
    /// the list and drops the arrows: whichever player's game runs the creature (and decides its loot), nothing is needed from it, so only
    /// the player who shoots needs the mod. A creature that gets away (not dead within two minutes, or gone) keeps its arrows.
    /// </summary>
    internal static class Stuck
    {
        private class Entry
        {
            public Character Target;
            public string Name;
            public Vector3 Last;
            public float Since, Seen;
            public readonly Dictionary<string, KeyValuePair<ItemDrop.ItemData, int>> Arrows = new Dictionary<string, KeyValuePair<ItemDrop.ItemData, int>>();
        }

        private const float Patience = 120f;
        private static readonly Dictionary<int, Entry> Hit = new Dictionary<int, Entry>();
        private static float _next;

        /// <summary>One more arrow of this kind hit the creature.</summary>
        public static void Add(Character target, ItemDrop.ItemData arrow)
        {
            int id = target.GetInstanceID();
            if (!Hit.TryGetValue(id, out Entry e)) Hit[id] = e = new Entry { Target = target, Name = target.name, Since = Time.time, Seen = Time.time };
            string name = arrow.m_dropPrefab.name;
            e.Arrows[name] = new KeyValuePair<ItemDrop.ItemData, int>(e.Arrows.TryGetValue(name, out var had) ? had.Key : arrow, (e.Arrows.TryGetValue(name, out var had2) ? had2.Value : 0) + 1);
            e.Last = target.transform.position;
            if (Plugin.Debug.Value) Plugin.Log.LogInfo($"  {target.name} has {string.Join(", ", e.Arrows.Select(kv => kv.Value.Value + " " + kv.Key))} stuck in it");
        }

        /// <summary>A few times a second: the creatures that have died drop their arrows.</summary>
        public static void Tick()
        {
            if (Hit.Count == 0 || Time.time < _next) return;
            _next = Time.time + 0.25f;
            foreach (int id in Hit.Keys.ToList())
            {
                Entry e = Hit[id];
                bool gone = e.Target == null;
                if (!gone) { e.Last = e.Target.transform.position; e.Seen = Time.time; }
                // A creature that dies is replaced by its body at once, so it can be gone before it is seen dead: one that vanishes close to
                // the player, a moment after it was last seen, died. (One that goes far away was only unloaded or despawned.)
                Player player = Player.m_localPlayer;
                bool vanished = gone && Time.time - e.Seen < 1.5f && player != null && Vector3.Distance(e.Last, player.transform.position) < 70f;
                bool dead = !gone && e.Target.IsDead();
                if (dead || vanished) Drop(e);
                else if (gone && Plugin.Debug.Value) Plugin.Log.LogInfo($"  a creature with arrows in it went away without dying ({Time.time - e.Seen:0.0} s ago, {(player != null ? Vector3.Distance(e.Last, player.transform.position) : -1f):0} m off)");
                if (dead || gone || Time.time - e.Since > Patience) Hit.Remove(id);
            }
        }

        private static void Drop(Entry e)
        {
            Vector3 at = e.Last + Vector3.up * 0.4f;
            foreach (var kv in e.Arrows.Values)
            {
                ItemDrop.ItemData one = kv.Key.Clone();
                one.m_stack = kv.Value;
                ItemDrop.DropItem(one, kv.Value, at + Random.insideUnitSphere * 0.3f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                if (Plugin.Debug.Value) Plugin.Log.LogInfo($"  {e.Name} died: {kv.Value} {kv.Key.m_dropPrefab.name} dropped with its loot");
            }
        }

        public static void Clear() => Hit.Clear();
    }
}
