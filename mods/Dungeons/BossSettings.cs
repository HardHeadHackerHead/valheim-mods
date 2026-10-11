using BepInEx.Configuration;

namespace Dungeons
{
    /// <summary>
    /// The boss toolkit's look-and-feel settings: each player's own, never synced. "Screen shake" (0-100 %, default 50) scales every
    /// camera shake the dungeons make (BossShow.Shake); 0 turns it off. Bound as the plugin starts (Plugin.Awake: Bind), so it's in the
    /// config file and the config manager before the first fight.
    /// </summary>
    internal static class BossSettings
    {
        private static ConfigEntry<int> _shake;

        /// <summary>The plugin's start: the setting into its config.</summary>
        public static void Bind(ConfigFile config)
        {
            _shake = config.Bind("3 - Look and feel", "ScreenShake", 50,
                new ConfigDescription("How much the camera shakes at a dungeon boss's big moments (its landing, a shield breaking, its death), in percent. 0: never. Your own, not synced.",
                    new AcceptableValueRange<int>(0, 100)));
        }

        /// <summary>The player's screen shake, 0..1.</summary>
        public static float ScreenShake
        {
            get
            {
                if (_shake == null) return 0.5f;
                return UnityEngine.Mathf.Clamp(_shake.Value, 0, 100) / 100f;
            }
        }
    }
}
