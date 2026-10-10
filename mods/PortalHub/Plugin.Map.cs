using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace PortalHub
{
    /// <summary>
    /// Portals on the map: each one gets the game's own portal icon with its name (so it follows the map's zoom and the pin
    /// filters), and the big map draws a line from each portal to where it goes, with an arrow when it is one-way. The lines are part
    /// of the map's own interface (a layer under its pins), so they move, scale and hide with it and never cover a pin or a window.
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
            if (!Enabled || !_mapIcons.Value || Player.m_localPlayer == null) { ClearPins(map); return; }

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

        private const string LinkLayerName = "PortalHubLinks";
        private const float LinkWidth = 2.5f;

        private RectTransform _linkLayer;
        private readonly List<LinkPart> _linkParts = new List<LinkPart>();
        private Dictionary<string, PortalInfo> _byId = new Dictionary<string, PortalInfo>();
        private float _byIdFor = -1f;

        /// <summary>One straight piece of a link (a line, or one side of an arrowhead), and where it was last put.</summary>
        private class LinkPart { public RectTransform Rect; public Image Image; public Vector2 A, B; public Color Color; }

        /// <summary>
        /// After the map has placed its pins this frame (from LateUpdate): a line from each linked portal's pin to its destination's, on a
        /// layer under the pins inside the big map. The pieces are kept, and moved only when a pin moved (panning, zooming).
        /// </summary>
        private void UpdateMapLines()
        {
            Minimap map = Minimap.instance;
            RectTransform root = map != null ? map.m_pinRootLarge : null;
            if (root == null || !_mapLines.Value || !Enabled || !Minimap.IsOpen() || _pins.Count == 0)
            {
                if (_linkLayer != null && _linkLayer.gameObject.activeSelf) _linkLayer.gameObject.SetActive(false);
                return;
            }
            if (_linkLayer == null || _linkLayer.parent != root)
            {
                DestroyMapLines();
                // a hot reload leaves the old copy's layer behind (its types differ from ours): find it by name
                foreach (Transform old in root.Cast<Transform>().Where(t => t.name == LinkLayerName).ToList()) Destroy(old.gameObject);
                var go = new GameObject(LinkLayerName, typeof(RectTransform));
                _linkLayer = (RectTransform)go.transform;
                _linkLayer.SetParent(root, false);
                _linkLayer.anchorMin = Vector2.zero;
                _linkLayer.anchorMax = Vector2.one;
                _linkLayer.offsetMin = _linkLayer.offsetMax = Vector2.zero;
            }
            if (!_linkLayer.gameObject.activeSelf) _linkLayer.gameObject.SetActive(true);
            if (_linkLayer.GetSiblingIndex() != 0) _linkLayer.SetAsFirstSibling();   // under the pins (the map adds new pins after it)

            if (_byIdFor != PortalsAt)
            {
                _byIdFor = PortalsAt;
                _byId = new Dictionary<string, PortalInfo>();
                foreach (PortalInfo p in Portals) _byId[p.Id] = p;
            }

            int used = 0;
            var done = new HashSet<string>();
            foreach (PortalInfo from in Portals)
            {
                if (from.Dest.Length == 0 || !_pins.TryGetValue(from.Id, out Minimap.PinData a)) continue;
                if (!_byId.TryGetValue(from.Dest, out PortalInfo to) || !_pins.TryGetValue(to.Id, out Minimap.PinData b)) continue;

                bool mutual = to.Dest == from.Id;
                if (mutual && !done.Add(to.Id + ">" + from.Id)) continue; // a two-way link is drawn once
                if (mutual) done.Add(from.Id + ">" + to.Id);

                if (!OnMap(a, root, out Vector2 pa) || !OnMap(b, root, out Vector2 pb)) continue;
                Color color = mutual ? new Color(0.95f, 0.78f, 0.35f, 0.85f) : new Color(0.55f, 0.85f, 1f, 0.85f);
                Piece(used++, pa, pb, color);
                if (mutual || (pb - pa).magnitude < 60f) continue;
                // an arrowhead a little before the destination end, pointing at it
                Vector2 dir = (pb - pa).normalized, side = new Vector2(-dir.y, dir.x), tip = pb - dir * 18f;
                Piece(used++, tip, tip - dir * 12f + side * 7f, color);
                Piece(used++, tip, tip - dir * 12f - side * 7f, color);
            }
            for (int i = used; i < _linkParts.Count; i++)
                if (_linkParts[i].Rect != null && _linkParts[i].Rect.gameObject.activeSelf) _linkParts[i].Rect.gameObject.SetActive(false);
        }

        /// <summary>Where this pin's icon is, in the link layer's space, if the big map shows it now.</summary>
        private bool OnMap(Minimap.PinData pin, RectTransform root, out Vector2 position)
        {
            position = Vector2.zero;
            RectTransform ui = pin.m_uiElement;
            if (ui == null || ui.parent != root || !ui.gameObject.activeInHierarchy) return false;
            position = _linkLayer.InverseTransformPoint(ui.position);
            return true;
        }

        /// <summary>Puts piece <paramref name="index"/> from a to b (made the first time it is needed; moved only if it changed).</summary>
        private void Piece(int index, Vector2 a, Vector2 b, Color color)
        {
            while (_linkParts.Count <= index) _linkParts.Add(new LinkPart());
            LinkPart part = _linkParts[index];
            if (part.Rect == null)
            {
                var go = new GameObject("Link", typeof(RectTransform));
                part.Rect = (RectTransform)go.transform;
                part.Rect.SetParent(_linkLayer, false);
                part.Rect.anchorMin = part.Rect.anchorMax = new Vector2(0.5f, 0.5f);
                part.Rect.pivot = new Vector2(0f, 0.5f);
                part.Image = go.AddComponent<Image>();
                part.Image.raycastTarget = false;   // (clicks go to the map: its pins, panning)
                part.A = part.B = new Vector2(float.NaN, float.NaN);
                part.Color = Color.clear;
            }
            if (!part.Rect.gameObject.activeSelf) part.Rect.gameObject.SetActive(true);
            if (part.Color != color) { part.Color = color; part.Image.color = color; }
            if ((part.A - a).sqrMagnitude < 0.01f && (part.B - b).sqrMagnitude < 0.01f) return;
            part.A = a; part.B = b;
            Vector2 d = b - a;
            part.Rect.localPosition = a;
            part.Rect.sizeDelta = new Vector2(d.magnitude, LinkWidth);
            part.Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        private void DestroyMapLines()
        {
            if (_linkLayer != null) Destroy(_linkLayer.gameObject);
            _linkLayer = null;
            _linkParts.Clear();
        }
    }
}
