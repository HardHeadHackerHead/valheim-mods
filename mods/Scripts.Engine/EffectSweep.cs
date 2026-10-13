using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Scripts
{
    /// <summary>
    /// Removes world records of effects and sounds (the prefabs on Lists.VisualEffects and Lists.Sounds) that are lying in the world. An
    /// effect should never have one, but Blockscript 0.1.0 to 0.1.3 made the networked ones as world objects, and each left its record behind
    /// when it ended: the game then waits for those objects forever, so a respawn or a teleport next to one never finishes. Runs on the
    /// server (the only game that may remove any record) once a world is loaded and every ten minutes after, one search step a frame.
    /// </summary>
    internal static class EffectSweep
    {
        private const float Every = 600f;
        private static float _next = 5f;
        private static bool _running;
        internal static int Removed;

        internal static void Tick()
        {
            if (_running || ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null || Core.Plugin == null) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Every;
            Core.Plugin.StartCoroutine(Sweep());
        }

        /// <summary>Now (Claude Tools: blockscript sweep).</summary>
        internal static void Soon() { _next = 0f; }

        private static IEnumerator Sweep()
        {
            _running = true;
            int found = 0;
            var names = Lists.VisualEffects.Concat(Lists.Sounds).Distinct().ToList();
            foreach (string name in names)
            {
                var zdos = new List<ZDO>();
                int index = 0;
                bool done = false;
                while (!done)
                {
                    if (ZDOMan.instance == null) { _running = false; yield break; }
                    try { done = ZDOMan.instance.GetAllZDOsWithPrefabIterative(name, zdos, ref index); }
                    catch (System.Exception) { done = true; }
                    yield return null;
                }
                foreach (ZDO zdo in zdos)
                {
                    try
                    {
                        if (zdo == null || !zdo.IsValid()) continue;
                        zdo.SetOwner(ZDOMan.GetSessionID());
                        ZDOMan.instance.DestroyZDO(zdo);
                        found++;
                    }
                    catch (System.Exception) { }
                }
            }
            Removed += found;
            if (found > 0) Core.Log.LogInfo("Blockscript: removed " + found + " leftover effect record(s) from the world (they could stop a respawn or a teleport from finishing)");
            _running = false;
        }
    }
}
