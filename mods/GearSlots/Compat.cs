using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace GearSlots
{
    /// <summary>
    /// Other mods that keep their own slots in the same cells under the ordinary rows. Two mods using one cell put items on top of each
    /// other or drop them, so when one of these is installed GearSlots stands down: no gear rows, no panel, and anything it had in its
    /// rows goes into the bag.
    /// </summary>
    internal static class Compat
    {
        private static readonly string[][] SlotMods =
        {
            new[] { "shudnal.ExtraSlots", "ExtraSlots" },
            new[] { "randyknapp.mods.equipmentandquickslots", "Equipment and Quick Slots" },
            new[] { "Azumatt.AzuExtendedPlayerInventory", "AzuExtendedPlayerInventory" },
            new[] { "aedenthorn.ExtendedPlayerInventory", "Extended Player Inventory" },
            new[] { "com.bruce.valheim.comfyquickslots", "ComfyQuickSlots" },
        };

        private const string BetterArchery = "ishid4.mods.betterarchery";

        private static bool _checked;
        private static string _reason;

        /// <summary>
        /// The other mod's name when GearSlots must stand down, else null. Worked out the first time it matters (your character loading
        /// into a world), not when the mod starts: mods loaded after this one, and every mod ScriptEngine loads, are not listed yet then.
        /// </summary>
        public static string StandDownFor
        {
            get
            {
                if (!_checked && Player.m_localPlayer != null)
                {
                    _checked = true;
                    _reason = Find();
                    if (_reason != null)
                    {
                        Layout.ForgetNormalRows(); // QualityOfLife reads it: there are no gear rows now
                        Plugin.Log?.LogWarning($"{_reason} is installed and keeps its own slots in the same inventory cells: the gear slots are off");
                    }
                }
                return _reason;
            }
        }

        public static bool StandingDown => StandDownFor != null;

        private static string Find()
        {
            foreach (string[] mod in SlotMods)
                if (Chainloader.PluginInfos.ContainsKey(mod[0])) return mod[1];

            // BetterArchery's quiver (on by default) is an extra row under the ordinary ones; with it off, the mod adds no rows.
            if (Chainloader.PluginInfos.TryGetValue(BetterArchery, out BepInEx.PluginInfo info))
            {
                ConfigEntry<bool> quiver = null;
                bool readable = info.Instance != null && info.Instance.Config.TryGetEntry("Quiver", "Enable Quiver", out quiver);
                if (!readable || quiver.Value) return "BetterArchery (its quiver)";
            }
            return null;
        }
    }
}
