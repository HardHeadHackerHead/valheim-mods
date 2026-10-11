using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Quiver
{
    /// <summary>
    /// Arrows that hit a living creature, kept with it until it dies, then dropped at its body with its loot. The shooter's own game keeps
    /// the list and drops the arrows: whichever player's game runs the creature (and decides its loot), nothing is needed from it, so only
    /// the player who shoots needs the mod. A creature that gets away (not hit again for five minutes, or gone without dying: unloaded,
    /// despawned, unsummoned) keeps its arrows.
    ///
    /// How a death is known here: on the game that runs the creature, Character.OnDeath (only that game calls it). On any other game the
    /// creature is just destroyed, often before its last health (0) arrives, so its death shows as health 0 seen before it went, or as its
    /// own ragdoll (one of its death effects, a network object every nearby game gets) appearing where it was a moment later.
    /// </summary>
    internal static class Stuck
    {
        private class Entry
        {
            public Character Target;
            public string Name;
            public Vector3 Last;
            public float LastHit, Seen;
            public bool Died;
            public HashSet<string> Ragdolls;   // (the prefab names of its ragdolls)
            public readonly Dictionary<string, KeyValuePair<ItemDrop.ItemData, int>> Arrows = new Dictionary<string, KeyValuePair<ItemDrop.ItemData, int>>();
        }

        private struct Body { public string Name; public Vector3 At; public float Time; }

        private const float Patience = 300f;   // seconds after the last hit before a creature counts as got away
        private const int MaxKept = 200;       // creatures followed at once, at most (the ones hit longest ago go first)
        private const float Wait = 3f;         // seconds after a creature went to look for its body
        private const float BodyRange = 8f;    // metres between where it was last seen and its ragdoll
        private static readonly Dictionary<int, Entry> Hit = new Dictionary<int, Entry>();
        private static readonly List<Body> Bodies = new List<Body>();
        private static float _next;

        /// <summary>One more arrow of this kind hit the creature.</summary>
        public static void Add(Character target, ItemDrop.ItemData arrow)
        {
            int id = target.GetInstanceID();
            if (!Hit.TryGetValue(id, out Entry e))
            {
                if (Hit.Count >= MaxKept) Hit.Remove(Hit.OrderBy(kv => kv.Value.LastHit).First().Key);
                Hit[id] = e = new Entry { Target = target, Name = target.name, Seen = Time.time, Ragdolls = RagdollNames(target) };
            }
            e.LastHit = Time.time;
            string name = arrow.m_dropPrefab.name;
            e.Arrows[name] = new KeyValuePair<ItemDrop.ItemData, int>(e.Arrows.TryGetValue(name, out var had) ? had.Key : arrow, (e.Arrows.TryGetValue(name, out var had2) ? had2.Value : 0) + 1);
            e.Last = target.transform.position;
            if (Plugin.Debug.Value) Plugin.Log.LogInfo($"  {target.name} has {string.Join(", ", e.Arrows.Select(kv => kv.Value.Value + " " + kv.Key))} stuck in it");
        }

        /// <summary>The creature died on this game (it runs it): Character.OnDeath.</summary>
        public static void OnDeath(Character target)
        {
            if (Hit.Count == 0 || target == null || !Hit.TryGetValue(target.GetInstanceID(), out Entry e)) return;
            e.Died = true;
            e.Last = target.transform.position;
        }

        /// <summary>A ragdoll appeared on this game (a creature died here or on another game).</summary>
        public static void OnRagdoll(Ragdoll ragdoll)
        {
            if (Hit.Count == 0 || ragdoll == null) return;
            Bodies.Add(new Body { Name = Utils.GetPrefabName(ragdoll.gameObject), At = ragdoll.transform.position, Time = Time.time });
        }

        /// <summary>A few times a second: the creatures that have died drop their arrows.</summary>
        public static void Tick()
        {
            if (Hit.Count == 0) { Bodies.Clear(); return; }
            if (Time.time < _next) return;
            _next = Time.time + 0.25f;
            Bodies.RemoveAll(b => Time.time - b.Time > Wait + 2f);
            foreach (int id in Hit.Keys.ToList())
            {
                Entry e = Hit[id];
                bool gone = e.Target == null;
                if (!gone)
                {
                    e.Last = e.Target.transform.position;
                    e.Seen = Time.time;
                    if (e.Target.IsDead() || e.Target.GetHealth() <= 0f) e.Died = true;   // (health: the last value its owner sent)
                }
                if (!e.Died && gone && FoundBody(e)) e.Died = true;
                if (e.Died) { Drop(e); Hit.Remove(id); continue; }
                if (gone && Time.time - e.Seen > Wait)
                {
                    if (Plugin.Debug.Value) Plugin.Log.LogInfo($"  {e.Name} with arrows in it went away without dying: they stay with it");
                    Hit.Remove(id);
                }
                else if (!gone && Time.time - e.LastHit > Patience) Hit.Remove(id);   // (got away: not hit for a long while)
            }
        }

        // Its own ragdoll appeared where it was, about when it went.
        private static bool FoundBody(Entry e)
        {
            if (e.Ragdolls == null || e.Ragdolls.Count == 0) return false;
            foreach (Body b in Bodies)
                if (b.Time >= e.Seen - 1f && e.Ragdolls.Contains(b.Name) && Vector3.Distance(b.At, e.Last) < BodyRange) return true;
            return false;
        }

        private static HashSet<string> RagdollNames(Character target)
        {
            var names = new HashSet<string>();
            if (target.m_deathEffects?.m_effectPrefabs == null) return names;
            foreach (EffectList.EffectData fx in target.m_deathEffects.m_effectPrefabs)
                if (fx != null && fx.m_enabled && fx.m_prefab != null && fx.m_prefab.GetComponent<Ragdoll>() != null) names.Add(fx.m_prefab.name);
            return names;
        }

        private static void Drop(Entry e)
        {
            Vector3 at = e.Last + Vector3.up * 0.4f;
            foreach (var kv in e.Arrows.Values)
            {
                ItemDrop.ItemData one = kv.Key.Clone();
                one.m_stack = kv.Value;
                one.m_equipped = false; // (a copy of the arrows you had equipped)
                ItemDrop.DropItem(one, kv.Value, at + Random.insideUnitSphere * 0.3f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                if (Plugin.Debug.Value) Plugin.Log.LogInfo($"  {e.Name} died: {kv.Value} {kv.Key.m_dropPrefab.name} dropped with its loot");
            }
        }

        public static void Clear() { Hit.Clear(); Bodies.Clear(); }
    }

    /// <summary>A creature died on the game that runs it (only that game calls OnDeath).</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class Character_OnDeath_Stuck
    {
        private static void Prefix(Character __instance) => Stuck.OnDeath(__instance);
    }

    /// <summary>A ragdoll appeared: a creature died, here or on the game that runs it.</summary>
    [HarmonyPatch(typeof(Ragdoll), "Awake")]
    internal static class Ragdoll_Awake_Stuck
    {
        private static void Postfix(Ragdoll __instance) => Stuck.OnRagdoll(__instance);
    }
}
