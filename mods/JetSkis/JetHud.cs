using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using static DHack.Shared.Translations;

namespace JetSkis
{
    /// <summary>
    /// The jet ski's own gauge, in place of the boat's wheel, wind and sail in the middle of the screen: a small board of the game's wood
    /// at the bottom right with the speed (km/h), the throttle (idle, cruise, full speed, reverse) and a bar filling toward full speed.
    /// It hangs under the game's HUD, so it hides with it and scales with the player's interface scale.
    /// </summary>
    internal static class JetHud
    {
        private const string Name = "JetSkiGauge";
        private static RectTransform _panel, _bar;
        private static TMP_Text _speed, _unit, _gear;
        private static string _speedShown, _gearShown;

        internal static void Show(JetSki ski)
        {
            if (_panel == null && !Build()) return;
            if (!_panel.gameObject.activeSelf) _panel.gameObject.SetActive(true);

            float kmh = Mathf.Abs(ski.Speed) * 3.6f;
            string speed = Mathf.RoundToInt(kmh).ToString();
            if (speed != _speedShown) { _speed.text = speed; _speedShown = speed; }

            string key; Color colour;
            switch (ski.Throttle)
            {
                case Ship.Speed.Full: key = "jetskis_gear_full"; colour = GameUi.Gold; break;
                case Ship.Speed.Half: case Ship.Speed.Slow: key = "jetskis_gear_cruise"; colour = GameUi.Warm; break;
                case Ship.Speed.Back: key = "jetskis_gear_reverse"; colour = GameUi.Warn; break;
                default: key = "jetskis_gear_idle"; colour = GameUi.Dim; break;
            }
            if (key != _gearShown) { _gear.text = T(key); _gear.color = colour; _gearShown = key; }
            GameUi.SetBar(_bar, kmh / Mathf.Max(1f, Plugin.TopSpeed.Value * 3.6f), null, ski.Speed < -0.3f ? GameUi.Warn : GameUi.Gold);
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
            _unit = GameUi.Label(_panel, T("jetskis_kmh"), new Vector2(120f, -34f), new Vector2(60f, 24f), 16f, GameUi.Dim, null, TextAlignmentOptions.BottomLeft);
            _gear = GameUi.Label(_panel, "", new Vector2(124f, -10f), new Vector2(112f, 26f), 18f, GameUi.Dim, null, TextAlignmentOptions.TopRight);
            _gear.textWrappingMode = TextWrappingModes.NoWrap;
            _bar = GameUi.Bar(_panel, new Vector2(14f, -68f), new Vector2(222f, 18f), 0f, GameUi.Gold, null);
            _speedShown = _gearShown = null;
            return true;
        }
    }

    /// <summary>On a jet ski the boat's display (wheel, wind, sails) gives way to the jet ski's gauge.</summary>
    [HarmonyPatch(typeof(Hud), "UpdateShipHud")]
    internal static class Hud_UpdateShipHud
    {
        private static void Postfix(Hud __instance, Player player)
        {
            JetSki ski = player != null ? JetSki.Of(player.GetControlledShip()) : null;
            if (ski == null) { JetHud.Hide(); return; }
            if (__instance.m_shipHudRoot.activeSelf) __instance.m_shipHudRoot.SetActive(false);
            JetHud.Show(ski);
        }
    }
}
