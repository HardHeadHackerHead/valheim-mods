using System;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace CraftFromChests
{
    /// <summary>
    /// Other mods that also let you craft with what's in chests. With two of them, every chest is counted twice (crafts pay half, the
    /// rest is free), so when one of them is doing the job this mod stands down and leaves it to them.
    ///
    /// Looked up the first time it matters, not when the mod loads: mods loaded after this one (and everything the mod manager loads
    /// from BepInEx/scripts) aren't in the plugin list yet at that point.
    /// </summary>
    internal static class OtherChestMods
    {
        private const string AzuCraftyBoxes = "Azumatt.AzuCraftyBoxes";
        private const string CraftFromContainers = "aedenthorn.CraftFromContainers";
        private const string ValheimPlus = "org.bepinex.plugins.valheim_plus";

        private static bool _looked;
        private static string _always;              // a mod that crafts from chests whenever it is installed
        private static PropertyInfo _vpCurrent, _vpSection, _vpEnabled; // ValheimPlus: Configuration.Current.CraftFromChest.IsEnabled (off by default)
        private static float _vpReadAt = -10f;
        private static bool _vpOn;
        private static string _said;

        /// <summary>The name of the other mod doing the job, or null when it's ours to do.</summary>
        internal static string Which
        {
            get
            {
                if (!_looked) Look();
                string who = _always ?? (ValheimPlusOn() ? "ValheimPlus (its CraftFromChest section is on)" : null);
                if (who != _said)
                {
                    _said = who;
                    if (who != null) Plugin.Log.LogWarning($"{who} also lets you craft from chests; {Plugin.Name} stands down so chests aren't counted twice");
                    else Plugin.Log.LogInfo($"{Plugin.Name} crafts from chests again (no other chest-crafting mod is doing it)");
                }
                return who;
            }
        }

        private static void Look()
        {
            _looked = true;
            if (Chainloader.PluginInfos.ContainsKey(AzuCraftyBoxes)) _always = "AzuCraftyBoxes";
            else if (Chainloader.PluginInfos.ContainsKey(CraftFromContainers)) _always = "CraftFromContainers";

            if (Chainloader.PluginInfos.TryGetValue(ValheimPlus, out var info) && info.Instance != null)
            {
                try
                {
                    Type config = info.Instance.GetType().Assembly.GetType("ValheimPlus.Configurations.Configuration");
                    _vpCurrent = config?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                    _vpSection = config?.GetProperty("CraftFromChest", BindingFlags.Public | BindingFlags.Instance);
                    _vpEnabled = _vpSection?.PropertyType.GetProperty("IsEnabled", BindingFlags.Public | BindingFlags.Instance);
                    if (_vpEnabled == null) Plugin.Log.LogWarning("ValheimPlus is installed but its CraftFromChest setting can't be read; if you turn it on, turn this mod off");
                }
                catch (Exception e) { Plugin.Log.LogWarning("Could not read ValheimPlus' settings: " + e.Message); }
            }
        }

        /// <summary>ValheimPlus' craft-from-chest is a setting the server can change, so it is read again every few seconds.</summary>
        private static bool ValheimPlusOn()
        {
            if (_vpEnabled == null) return false;
            if (Time.unscaledTime - _vpReadAt < 3f) return _vpOn;
            _vpReadAt = Time.unscaledTime;
            try
            {
                object current = _vpCurrent.GetValue(null, null);
                object section = current != null ? _vpSection.GetValue(current, null) : null;
                _vpOn = section != null && (bool)_vpEnabled.GetValue(section, null);
            }
            catch (Exception) { _vpOn = false; }
            return _vpOn;
        }
    }
}
