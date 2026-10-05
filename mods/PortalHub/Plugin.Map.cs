using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PortalHub
{
    /// <summary>
    /// Portals on the map: each one gets the game's own portal icon with its name (so it follows the map's zoom and the pin
    /// filters), and the big map draws a line from each portal to where it goes, with an arrow when it is one-way.
    /// </summary>
    public partial class Plugin
    {
        private ConfigEntry<bool> _mapIcons, _mapLines;
        private readonly Dictionary<string, Minimap.PinData> _pins = new Dictionary<string, Minimap.PinData>();
        private float _pinsFor = -1f, _nextPinCheck;

        private static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> PinList =
            AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");

        private void BindMapConfig()
        {
            _mapIcons = Config.Bind("Map", "ShowPortals", true, "Show every portal on the map with its name.");
            _mapLines = Config.Bind("Map", "ShowLinks", true, "On the big map, draw a line from each portal to where it goes (an arrow if it only goes one way).");
        }

        private void UpdateMap()
        {
            Minimap map = Minimap.instance;
            if (map == null) return;
            if (!_enabled.Value || !_mapIcons.Value || Player.m_localPlayer == null) { ClearPins(map); return; }

            // The list is refreshed while you stand near a portal; also while the big map is open, so it is never stale there.
            if (Minimap.IsOpen() && Time.time >= _nextAsk) { _nextAsk = Time.time + 10f; RequestList(); }

            // Right-clicking a pin on the map deletes it: if one of ours went missing, put them back.
            if (Time.time >= _nextPinCheck)
            {
                _nextPinCheck = Time.time + 1f;
                List<Minimap.PinData> all = PinList(map);
                if (_pins.Values.Any(p => !all.Contains(p))) _pinsFor = -1f;
            }

            if (_pinsFor != PortalsAt) { SyncPins(map); _pinsFor = PortalsAt; }
        }

        private void SyncPins(Minimap map)
        {
            ClearPins(map);
            foreach (PortalInfo p in Portals)
                _pins[p.Id] = map.AddPin(p.Pos, Minimap.PinType.Icon3, string.IsNullOrWhiteSpace(p.Name) ? "Portal" : p.Name, false, false);
        }

        private void ClearPins(Minimap map)
        {
            if (_pins.Count == 0) return;
            if (map != null) foreach (Minimap.PinData pin in _pins.Values) map.RemovePin(pin);
            _pins.Clear();
            _pinsFor = -1f;
        }

        // ---- the links on the big map ----

        private void DrawMapLines()
        {
            if (!_mapLines.Value || !_enabled.Value || !Minimap.IsOpen() || Event.current.type != EventType.Repaint) return;

            var done = new HashSet<string>();
            foreach (PortalInfo from in Portals)
            {
                if (from.Dest.Length == 0 || !_pins.TryGetValue(from.Id, out Minimap.PinData a)) continue;
                PortalInfo to = Find(from.Dest);
                if (to == null || !_pins.TryGetValue(to.Id, out Minimap.PinData b)) continue;

                bool mutual = to.Dest == from.Id;
                if (mutual && !done.Add(to.Id + ">" + from.Id)) continue; // a two-way link is drawn once
                if (mutual) done.Add(from.Id + ">" + to.Id);

                if (!OnScreen(a, out Vector2 pa) || !OnScreen(b, out Vector2 pb)) continue;
                Color color = mutual ? new Color(0.95f, 0.78f, 0.35f, 0.85f) : new Color(0.55f, 0.85f, 1f, 0.85f);
                Line(pa, pb, color, 2.5f);
                if (!mutual) Arrow(pa, pb, color);
            }
        }

        /// <summary>Where this pin's icon is on the screen right now (IMGUI coordinates), if it is drawn.</summary>
        private static bool OnScreen(Minimap.PinData pin, out Vector2 position)
        {
            position = Vector2.zero;
            RectTransform ui = pin.m_uiElement;
            if (ui == null || !ui.gameObject.activeInHierarchy) return false;
            Camera cam = ui.GetComponentInParent<Canvas>()?.worldCamera;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, ui.position);
            position = new Vector2(screen.x, Screen.height - screen.y);
            return true;
        }

        private static void Line(Vector2 a, Vector2 b, Color color, float width)
        {
            Matrix4x4 saved = GUI.matrix;
            float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.color = color;
            GUI.DrawTexture(new Rect(a.x, a.y - width / 2f, (b - a).magnitude, width), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.matrix = saved;
        }

        /// <summary>An arrowhead a little before the destination end, pointing at it.</summary>
        private static void Arrow(Vector2 from, Vector2 to, Color color)
        {
            Vector2 dir = (to - from).normalized;
            if ((to - from).magnitude < 60f) return;
            Vector2 tip = to - dir * 18f;
            Vector2 side = new Vector2(-dir.y, dir.x);
            Line(tip, tip - dir * 12f + side * 7f, color, 2.5f);
            Line(tip, tip - dir * 12f - side * 7f, color, 2.5f);
        }
    }
}
