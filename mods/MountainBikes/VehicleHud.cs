using System.Linq;
using DHack.Shared;
using TMPro;
using UnityEngine;
using static DHack.Shared.Translations;

namespace MountainBikes
{
    /// <summary>
    /// The mountain bike's gauge, the Jet Ski's: a small board of the game's wood at the bottom right with the speed (km/h), what the
    /// rider is asking for (idle, cruise, full speed, braking, reverse) and a bar filling toward full speed. It hangs under the game's HUD,
    /// so it hides with it and scales with the player's interface scale. Shown while the local player has the controls.
    /// </summary>
    internal static class VehicleHud
    {
        private const string Name = "MountainBikeGauge";
        private static RectTransform _panel, _bar;
        private static TMP_Text _speed, _unit, _gear;
        private static string _speedShown, _gearShown;

        /// <summary>Every frame (from the plugin's Update): the gauge while riding, gone otherwise.</summary>
        internal static void Tick()
        {
            GroundVehicle v = GroundVehicle.Riding(Player.m_localPlayer);
            if (v == null) Hide(); else Show(v);
        }

        private static void Show(GroundVehicle v)
        {
            if (_panel == null && !Build()) return;
            if (!_panel.gameObject.activeSelf) _panel.gameObject.SetActive(true);

            float kmh = Mathf.Abs(v.Speed) * 3.6f;
            string speed = Mathf.RoundToInt(kmh).ToString();
            if (speed != _speedShown) { _speed.text = speed; _speedShown = speed; }

            string key; Color colour;
            switch (v.Gear)
            {
                case 2: key = "mountainbikes_gear_full"; colour = GameUi.Gold; break;
                case 1: key = "mountainbikes_gear_cruise"; colour = GameUi.Warm; break;
                case 4: key = "mountainbikes_gear_brake"; colour = GameUi.Warn; break;
                case 3: key = "mountainbikes_gear_reverse"; colour = GameUi.Warn; break;
                default: key = "mountainbikes_gear_idle"; colour = GameUi.Dim; break;
            }
            if (key != _gearShown) { _gear.text = T(key); _gear.color = colour; _gearShown = key; }
            GameUi.SetBar(_bar, kmh / Mathf.Max(1f, Plugin.TopSpeed.Value * 3.6f), null, v.Speed < -0.3f ? GameUi.Warn : GameUi.Gold);
        }

        internal static void Hide()
        {
            if (_panel != null && _panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);
        }

        internal static void Destroy()
        {
            if (_panel != null) Object.Destroy(_panel.gameObject);
            _panel = null;
        }

        private static bool Build()
        {
            Transform hud = GameUi.HudRoot;
            if (hud == null || !GameUi.Ready()) return false;
            foreach (Transform old in hud.Cast<Transform>().Where(t => t.name == Name).ToList()) Object.Destroy(old.gameObject);   // (a hot reload's leftover)

            _panel = GameUi.Board(hud, Name, Vector2.zero, new Vector2(250f, 100f));
            _panel.anchorMin = _panel.anchorMax = new Vector2(1f, 0f);   // bottom right, clear of the middle of the screen
            _panel.pivot = new Vector2(1f, 0f);
            _panel.anchoredPosition = new Vector2(-40f, 160f);

            _speed = GameUi.Label(_panel, "0", new Vector2(14f, -6f), new Vector2(100f, 52f), 40f, GameUi.Warm, GameUi.Serif, TextAlignmentOptions.BottomRight);   // (not Norse: its 0 is a rune-like diamond)
            _speed.fontStyle = FontStyles.Bold;
            _unit = GameUi.Label(_panel, T("mountainbikes_kmh"), new Vector2(120f, -34f), new Vector2(60f, 24f), 16f, GameUi.Dim, null, TextAlignmentOptions.BottomLeft);
            _gear = GameUi.Label(_panel, "", new Vector2(124f, -10f), new Vector2(112f, 26f), 18f, GameUi.Dim, null, TextAlignmentOptions.TopRight);
            _gear.textWrappingMode = TextWrappingModes.NoWrap;
            _bar = GameUi.Bar(_panel, new Vector2(14f, -68f), new Vector2(222f, 18f), 0f, GameUi.Gold, null);
            _speedShown = _gearShown = null;
            return true;
        }
    }
}
