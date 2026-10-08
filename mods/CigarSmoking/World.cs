using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CigarSmoking
{
    /// <summary>
    /// Wild tobacco: each strain grows in its own biome (the Meadows, the Black Forest, the Plains), added to the game's list of what grows
    /// where. Only zones the world has not generated yet get them: the game places vegetation once, when a zone is first visited, so
    /// ground you have already explored stays as it was (which is also why the plants are in the world the first time you find them).
    /// </summary>
    internal static class World
    {
        private static readonly List<ZoneSystem.ZoneVegetation> Added = new List<ZoneSystem.ZoneVegetation>();

        internal static void Add()
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null) return;
            foreach (Strain s in Strains.All)
            {
                if (!Things.Made.TryGetValue(s.WildPickable, out GameObject prefab) || prefab == null) continue;
                if (zones.m_vegetation.Exists(v => v.m_prefab == prefab)) continue;
                zones.m_vegetation.RemoveAll(v => v.m_name == "dh_" + s.Id + "_tobacco");   // one left by an earlier copy of the mod
                var veg = new ZoneSystem.ZoneVegetation
                {
                    m_name = "dh_" + s.Id + "_tobacco",
                    m_prefab = prefab,
                    m_enable = true,
                    m_min = 0f,
                    m_max = 2f,                       // up to two clumps in a zone
                    m_groupSizeMin = 1,
                    m_groupSizeMax = 3,
                    m_groupRadius = 5f,
                    m_scaleMin = 0.9f,
                    m_scaleMax = 1.2f,
                    m_randTilt = 6f,
                    m_biome = s.Wild,
                    m_biomeArea = Heightmap.BiomeArea.Everything,
                    m_blockCheck = true,
                    m_minAltitude = 2f,
                    m_maxAltitude = 120f,
                    m_maxTilt = 28f,
                };
                zones.m_vegetation.Add(veg);
                Added.Add(veg);
            }
        }

        internal static void Remove()
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones != null) zones.m_vegetation.RemoveAll(v => Added.Contains(v) || (v.m_name != null && v.m_name.StartsWith("dh_") && v.m_name.EndsWith("_tobacco")));
            Added.Clear();
        }
    }

    // The game builds its list of what grows where when it starts: ours go on the end of it.
    [HarmonyPatch(typeof(ZoneSystem), "Start")]
    internal static class ZoneSystem_Start
    {
        private static void Postfix() => World.Add();
    }
}
