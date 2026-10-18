using System.Linq;
using DHack.Shared;
using TMPro;
using UnityEngine;
using static DHack.Shared.Translations;

namespace RocketRoadster
{
    /// <summary>
    /// The roadster's gauge (the Jet Ski's and Four Wheeler's, grown a little): a small board of the game's wood at the bottom right with the
    /// speed (km/h), what it is doing (idle, cruise, full speed, boost, braking, reverse; in the air its height, as the reveal's broadcast
    /// showed it), a bar filling toward the boost top speed, and the air tank. It hangs under the game's HUD, so it hides with it and scales
    /// with the player's interface scale. Shown while the local player drives.
    /// </summary>
    internal static class RoadsterHud
    {
        private const string Name = "RocketRoadsterGauge";
        private static RectTransform _panel, _bar, _tank;
        private static TMP_Text _speed, _unit, _gear, _tankLabel;
        private static string _speedShown, _gearShown;

        /// <summary>Every frame (from the plugin's Update): the gauge while driving, gone otherwise.</summary>
        internal static void Tick()
        {
            if (GroundVehicle.Riding(Player.m_localPlayer) is Roadster car) Show(car); else Hide();
        }

        private static void Show(Roadster car)
        {
            if (_panel == null && !Build()) return;
            if (!_panel.gameObject.activeSelf) _panel.gameObject.SetActive(true);

            float kmh = Mathf.Abs(car.Speed) * 3.6f;
            string speed = Mathf.RoundToInt(kmh).ToString();
            if (speed != _speedShown) { _speed.text = speed; _speedShown = speed; }

            string text; Color colour;
            if (car.Airborne && car.WheelsOnGround == 0)
            {
                text = T("roadster_altitude", car.Altitude.ToString("0.0"));
                colour = (car.JetsNow & 1) != 0 ? GameUi.Gold : GameUi.Text;
            }
            else if (car.Boosting) { text = T("roadster_gear_boost"); colour = GameUi.Gold; }
            else
            {
                switch (car.Gear)
                {
                    case 2: text = T("roadster_gear_full"); colour = GameUi.Gold; break;
                    case 1: text = T("roadster_gear_cruise"); colour = GameUi.Warm; break;
                    case 4: text = T("roadster_gear_brake"); colour = GameUi.Warn; break;
                    case 3: text = T("roadster_gear_reverse"); colour = GameUi.Warn; break;
                    default: text = T("roadster_gear_idle"); colour = GameUi.Dim; break;
                }
            }
            if (text != _gearShown) { _gear.text = text; _gear.color = colour; _gearShown = text; }
            GameUi.SetBar(_bar, kmh / Mathf.Max(1f, Plugin.BoostSpeed.Value * 3.6f), null, car.Speed < -0.3f ? GameUi.Warn : GameUi.Gold);
            GameUi.SetBar(_tank, car.Tank, null, car.Tank < 0.2f ? GameUi.Warn : new Color(0.85f, 0.92f, 1f));
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

            _panel = GameUi.Board(hud, Name, Vector2.zero, new Vector2(260f, 130f));
            _panel.anchorMin = _panel.anchorMax = new Vector2(1f, 0f);   // bottom right, clear of the middle of the screen
            _panel.pivot = new Vector2(1f, 0f);
            _panel.anchoredPosition = new Vector2(-40f, 160f);

            _speed = GameUi.Label(_panel, "0", new Vector2(14f, -6f), new Vector2(100f, 52f), 40f, GameUi.Warm, GameUi.Serif, TextAlignmentOptions.BottomRight);   // (not Norse: its 0 is a rune-like diamond)
            _speed.fontStyle = FontStyles.Bold;
            _unit = GameUi.Label(_panel, T("roadster_kmh"), new Vector2(120f, -34f), new Vector2(60f, 24f), 16f, GameUi.Dim, null, TextAlignmentOptions.BottomLeft);
            _gear = GameUi.Label(_panel, "", new Vector2(124f, -10f), new Vector2(122f, 26f), 18f, GameUi.Dim, null, TextAlignmentOptions.TopRight);
            _gear.textWrappingMode = TextWrappingModes.NoWrap;
            _bar = GameUi.Bar(_panel, new Vector2(14f, -66f), new Vector2(232f, 16f), 0f, GameUi.Gold, null);
            _tankLabel = GameUi.Label(_panel, T("roadster_tank"), new Vector2(14f, -88f), new Vector2(70f, 24f), 15f, GameUi.Dim, null, TextAlignmentOptions.Left);
            _tank = GameUi.Bar(_panel, new Vector2(86f, -92f), new Vector2(160f, 16f), 1f, new Color(0.85f, 0.92f, 1f), null);
            _speedShown = _gearShown = null;
            return true;
        }
    }
}
