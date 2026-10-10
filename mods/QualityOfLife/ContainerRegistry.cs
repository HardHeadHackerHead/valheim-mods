using System.Collections.Generic;
using HarmonyLib;

namespace QualityOfLife
{
    /// <summary>
    /// A list of every chest in the world, kept up to date as chests appear. Looking chests up here is nearly free, whereas asking
    /// Unity to search the whole scene for them (what the code did before) costs real time and was making the menus stutter.
    /// </summary>
    internal static class ContainerRegistry
    {
        private static readonly List<Container> All = new List<Container>();

        public static void Register(Container c)
        {
            if (c != null && !c.name.StartsWith("piece_recycler") && !All.Contains(c)) All.Add(c); // the Recycler is not a storage chest
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

        public static void Clear() { All.Clear(); Seen.Clear(); }

        // When each chest's saved contents last changed, as far as this game has seen (chests near you, while the inventory is open).
        private static readonly Dictionary<Container, KeyValuePair<uint, float>> Seen = new Dictionary<Container, KeyValuePair<uint, float>>();
        private const float SettleSeconds = 3f;

        /// <summary>Note the chest's save revision (call when chests are scanned), so a change made by someone else can be noticed.</summary>
        public static void Watch(Container c)
        {
            var view = NView.GetValue(c) as ZNetView;
            if (view == null || !view.IsValid()) return;
            uint revision = view.GetZDO().DataRevision;
            if (!Seen.TryGetValue(c, out KeyValuePair<uint, float> seen))
                Seen[c] = new KeyValuePair<uint, float>(revision, float.NegativeInfinity); // first look: settled as far as we know
            else if (seen.Key != revision)
                Seen[c] = new KeyValuePair<uint, float>(revision, UnityEngine.Time.time);
        }

        /// <summary>
        /// Leave this chest alone for now: someone has it open, or another player's game owns it and its contents changed in the last
        /// few seconds (they are using it, and our copy may not have their latest change yet: moving items now could undo theirs).
        /// </summary>
        public static bool Busy(Container c)
        {
            if (InUse(c)) return true;
            var view = NView.GetValue(c) as ZNetView;
            if (view == null || !view.IsValid() || view.IsOwner()) return false;
            uint revision = view.GetZDO().DataRevision;
            if (!Seen.TryGetValue(c, out KeyValuePair<uint, float> seen)) return false;
            return seen.Key != revision || UnityEngine.Time.time - seen.Value < SettleSeconds;
        }

        private static readonly System.Reflection.FieldInfo NView = AccessTools.Field(typeof(Container), "m_nview");
        private static readonly System.Reflection.MethodInfo Load = AccessTools.Method(typeof(Container), "Load");

        /// <summary>
        /// Someone has this chest open. IsInUse() is only right on the chest's owner; everyone else has to read the flag the owner
        /// keeps in the chest's save data.
        /// </summary>
        public static bool InUse(Container c)
        {
            if (c.IsInUse()) return true;
            var view = NView.GetValue(c) as ZNetView;
            return view != null && view.IsValid() && !view.IsOwner() && view.GetZDO().GetInt(ZDOVars.s_inUse) == 1;
        }

        /// <summary>
        /// Load the chest's latest contents from its save data. A chest you don't own is only refreshed once a second, so the copy
        /// you see can be that far behind. (The game's own reload: does nothing if the copy is already the latest.)
        /// </summary>
        public static void Reload(Container c) => Load?.Invoke(c, null);

        /// <summary>
        /// Only the owner of a chest can save its contents, so take ownership first (matters in multiplayer), then reload it so we
        /// change the latest contents and not an old copy (saving an old copy would undo another player's changes).
        /// </summary>
        public static void TakeOwnership(Container c)
        {
            var view = NView.GetValue(c) as ZNetView;
            if (view == null || !view.IsValid() || view.IsOwner()) return;
            view.ClaimOwnership();
            Reload(c);
        }
    }

    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class Container_Awake
    {
        private static void Postfix(Container __instance) => ContainerRegistry.Register(__instance);
    }
}
