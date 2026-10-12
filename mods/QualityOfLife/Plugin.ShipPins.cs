using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace QualityOfLife
{
    /// <summary>
    /// Your ships on the map: every raft, karve, longship and drakkar in the world gets a pin with a longship of its own on the minimap and
    /// the big map, named for the kind of ship, following it as it sails. They are found from the world's saved objects, so a ship far away
    /// shows too (on the game hosting the world; a player who joins sees the ones near them). The pins are not saved with your map.
    /// </summary>
    public partial class Plugin
    {
        private ConfigEntry<bool> _shipPins;
        private ConfigEntry<Minimap.PinType> _shipPinType;
        // By the ship's object id, kept as its two numbers: a ZDOID itself works out its hash from a table the game empties each time you
        // join a world (ZDOID.Reset), so ZDOIDs kept from before you left for the main menu can't be found again (it threw every second).
        private readonly Dictionary<(long, uint), Minimap.PinData> _shipPinsByZdo = new Dictionary<(long, uint), Minimap.PinData>();
        private Minimap _shipPinsOn;   // the map those pins are on (a new world has a new one)
        private Dictionary<int, string> _shipKinds;   // prefab hash -> name shown on the pin
        private Sprite _shipIcon;
        private float _nextShipPinScan;
        // The look through the world's objects, a slice each frame: all of them for every kind of ship at once took ~40 ms every second.
        private List<KeyValuePair<int, string>> _scanKinds;   // null: not looking now
        private int _scanKind, _scanIndex;
        private readonly List<ZDO> _scanList = new List<ZDO>();
        private readonly List<(ZDO zdo, int prefab, string name)> _scanFound = new List<(ZDO, int, string)>();   // (kept over frames: checked again before use)

        private void BindShipPins()
        {
            _shipPins = Config.Bind("Map", "ShowShips", true, "Show every ship in the world on the minimap and the map, with a longship icon, wherever it is.");
            _shipPinType = Config.Bind("Map", "ShipPinType", Minimap.PinType.Icon4,
                "Which of the map's pin kinds the ship pins count as (their picture is the longship either way). Hiding that kind with the map's pin filters hides them too.");
        }

        private void UpdateShipPins()
        {
            if (Minimap.instance == null || ZDOMan.instance == null || ZNetScene.instance == null) return;
            if (!_shipPins.Value) { ClearShipPins(); return; }
            if (_shipPinsOn != Minimap.instance) { _shipPinsByZdo.Clear(); _shipPinsOn = Minimap.instance; _scanKinds = null; }   // (another world: its own pins)
            if (_scanKinds == null && Time.unscaledTime < _nextShipPinScan) return;

            if (_shipKinds == null)
            {
                _shipKinds = new Dictionary<int, string>();
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    if (prefab == null || prefab.GetComponent<Ship>() == null) continue;
                    Piece piece = prefab.GetComponent<Piece>();
                    string name = piece != null ? Localization.instance.Localize(piece.m_name) : prefab.name;
                    _shipKinds[prefab.name.GetStableHashCode()] = name;
                }
            }
            if (_shipIcon == null) _shipIcon = LoadShipIcon();

            // every ship the world knows of (its saved object, which moves with it), by kind: one slice of the world's objects per frame (the
            // game's iterative search looks at up to 400 sectors a call), one kind after another; the pins move once the whole look is done
            if (_scanKinds == null)
            {
                _scanKinds = _shipKinds.ToList();
                _scanKind = 0; _scanIndex = 0;
                _scanList.Clear(); _scanFound.Clear();
            }
            while (_scanKind < _scanKinds.Count)
            {
                var kind = _scanKinds[_scanKind];
                string prefabName = ZNetScene.instance.GetPrefab(kind.Key)?.name;
                if (prefabName != null && !ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefabName, _scanList, ref _scanIndex)) return; // more next frame
                foreach (ZDO zdo in _scanList) _scanFound.Add((zdo, kind.Key, kind.Value));
                _scanList.Clear();
                _scanIndex = 0;
                _scanKind++;
                if (prefabName != null) return; // (one search a frame)
            }
            _scanKinds = null;
            _nextShipPinScan = Time.unscaledTime + 1f;

            var seen = new HashSet<(long, uint)>();
            foreach (var (zdo, prefab, name) in _scanFound)
            {
                // (found a few frames ago: the game reuses the objects of removed things, so check it is still that ship)
                if (zdo == null || !zdo.IsValid() || zdo.GetPrefab() != prefab) continue;
                var key = (zdo.m_uid.UserID, zdo.m_uid.ID);
                seen.Add(key);
                Vector3 at = zdo.GetPosition();
                if (_shipPinsByZdo.TryGetValue(key, out Minimap.PinData pin) && PinStillThere(pin)) { pin.m_pos = at; continue; }
                pin = Minimap.instance.AddPin(at, _shipPinType.Value, name, false, false, 0L);
                if (_shipIcon != null)
                {
                    pin.m_icon = _shipIcon;
                    if (pin.m_iconElement != null) pin.m_iconElement.sprite = _shipIcon;
                }
                _shipPinsByZdo[key] = pin;
                Logger.LogInfo($"Ship on the map: {name} at ({at.x:0}, {at.z:0}){(_shipIcon == null ? " (no longship picture: the mod's ship.png was not found)" : "")}");
            }
            _scanFound.Clear();
            foreach (var gone in _shipPinsByZdo.Where(kv => !seen.Contains(kv.Key)).ToList())
            {
                Minimap.instance.RemovePin(gone.Value); // (sunk, taken apart, or out of this game's sight)
                _shipPinsByZdo.Remove(gone.Key);
            }
        }

        private static readonly FieldInfo PinsField = HarmonyLib.AccessTools.Field(typeof(Minimap), "m_pins");

        /// <summary>The pin is still on the map (you can remove pins from the big map by hand; then it is made again).</summary>
        private static bool PinStillThere(Minimap.PinData pin) => PinsField?.GetValue(Minimap.instance) is List<Minimap.PinData> pins && pins.Contains(pin);

        private void ClearShipPins()
        {
            if (Minimap.instance != null) foreach (Minimap.PinData pin in _shipPinsByZdo.Values) Minimap.instance.RemovePin(pin);
            _shipPinsByZdo.Clear();
            _scanKinds = null;
        }

        // Texture2D.LoadImage lives in a module the build cannot reference directly, so it is called by name.
        private static readonly MethodInfo LoadImageMethod =
            Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });

        /// <summary>The longship picture (ship.png, inside the mod).</summary>
        private static Sprite LoadShipIcon()
        {
            using (System.IO.Stream s = typeof(Plugin).Assembly.GetManifestResourceStream("ship.png"))
            {
                if (s == null || LoadImageMethod == null) return null;
                var bytes = new byte[s.Length];
                s.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
                if (!(bool)LoadImageMethod.Invoke(null, new object[] { tex, bytes })) return null;
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
        }
    }
}
