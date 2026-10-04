using System.Collections.Generic;
using HarmonyLib;

namespace FeedFromChests
{
    /// <summary>
    /// A list of every chest in the world, kept up to date as chests appear. Looking chests up here is nearly free, whereas asking
    /// Unity to search the whole scene for them costs real time and made the menu stutter.
    /// </summary>
    internal static class ContainerRegistry
    {
        private static readonly List<Container> All = new List<Container>();

        public static void Register(Container c)
        {
            if (c != null && !All.Contains(c)) All.Add(c);
        }

        /// <summary>Pick up chests that already exist (once, when the mod loads or reloads).</summary>
        public static void Seed()
        {
            foreach (Container c in UnityEngine.Object.FindObjectsOfType<Container>()) Register(c);
        }

        /// <summary>The chests that still exist (destroyed ones are dropped).</summary>
        public static List<Container> Alive()
        {
            All.RemoveAll(c => c == null);
            return All;
        }

        public static void Clear() => All.Clear();
    }

    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class Container_Awake
    {
        private static void Postfix(Container __instance) => ContainerRegistry.Register(__instance);
    }
}
