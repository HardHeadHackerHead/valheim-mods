using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using static DHack.Shared.Translations;

namespace BuildOrders
{
    /// <summary>
    /// The Bridge options panel, in the game's own look (GameUi.cs): opens when the end of a bridge is set, at the side of the screen so the
    /// bridge stays in view. The ghost stays where it is and changes as you change the settings (width, material, rails, roof, supports, shape,
    /// torches); Confirm places the plan, Change end lets you aim the end again, Cancel drops it. The choices are kept for the next bridge.
    /// </summary>
    public partial class Plugin
    {
        internal static bool BridgeOptionsOpen;
        private BridgeOptions _bridgeOpts;

        private const float BridgeW = 520f, BridgePad = 24f;
        private RectTransform _bridgeWin;
        private string _bridgeShown = "";
        private float _bridgeCheckAt;
        private Action _bridgePending;   // a click's action, run in Update

        private void OpenBridgeOptions()
        {
            _bridgeOpts = _bridgeOpts ?? CurrentBridgeOptions();   // kept through "Change end"
            BridgeOptionsOpen = true;
            _bridgeDirty = true;
        }

        private void ConfirmBridge(Player player)
        {
            if (!_bridgeStart.HasValue || _bridgeOpts == null) return;
            SaveBridgeOptions(_bridgeOpts);   // remember the choices for next time
            Vector3 start = _bridgeStart.Value, end = _bridgeEnd;
            BridgeOptions o = _bridgeOpts;
            CancelBridge();
            BridgeDesign d = PlanBridge(player, start, end, o);
            player.Message(MessageHud.MessageType.TopLeft, T("buildorders_bridge_planned", d.Length.ToString("0")));
        }

        private void DestroyBridgeWindow()
        {
            if (_bridgeWin != null) Destroy(_bridgeWin.gameObject);
            _bridgeWin = null;
            _bridgeShown = "";
            _bridgePending = null;
            UITooltip.HideTooltip();
        }

        /// <summary>Every frame (from Update): show or hide the panel as BridgeOptionsOpen says, run a click's action, rebuild it when what it shows changed.</summary>
        private void TickBridgeOptions()
        {
            if (!BridgeOptionsOpen || _bridgeOpts == null) { if (_bridgeWin != null || _bridgePending != null) DestroyBridgeWindow(); return; }
            Player player = Player.m_localPlayer;
            if (player == null) { DestroyBridgeWindow(); return; }
            bool fresh = _bridgeWin == null;
            if (fresh && !GameUi.Ready())
            {
                CancelBridge();
                player.Message(MessageHud.MessageType.TopLeft, T("buildorders_bridge_failed"));
                return;
            }
            if (_bridgePending != null)
            {
                Action act = _bridgePending;
                _bridgePending = null;
                act();
                if (!BridgeOptionsOpen || _bridgeOpts == null) { DestroyBridgeWindow(); return; }
                _bridgeCheckAt = 0f;
            }
            if (!fresh && Time.unscaledTime < _bridgeCheckAt) return;
            _bridgeCheckAt = Time.unscaledTime + 0.2f;

            // what it costs, against what you carry (and the chests nearby)
            BridgeDesign d = _bridgeDesign;
            var needs = new List<KeyValuePair<string, int>>();
            var have = new List<int>();
            if (d != null)
            {
                needs = MaterialsOf(d.Entries.Select(e => e.Prefab));
                ForceBuildContext(true);
                try
                {
                    Inventory inv = player.GetInventory();
                    foreach (var kv in needs) have.Add(inv.CountItems(kv.Key));
                }
                finally { ForceBuildContext(false); }
            }

            BridgeOptions o = _bridgeOpts;
            var sb = new StringBuilder();
            sb.Append(o.Width).Append(o.Material).Append(o.Sides).Append(o.Roof).Append(o.Supports).Append(o.Shape).Append(o.Ends).Append(o.Torches);
            if (d != null)
            {
                sb.Append('|').Append(Mathf.RoundToInt(d.Length)).Append('|').Append(d.Entries.Count).Append('|').Append(string.Join(";", d.Warnings.Select(w => w.Shown).ToArray()));
                for (int i = 0; i < needs.Count; i++) sb.Append('|').Append(needs[i].Key).Append(have[i]).Append('/').Append(needs[i].Value);
            }
            string signature = sb.ToString();
            if (fresh || signature != _bridgeShown) BuildBridgeWindow(player, d, needs, have, signature);
        }

        private void BuildBridgeWindow(Player player, BridgeDesign d, List<KeyValuePair<string, int>> needs, List<int> have, string signature)
        {
            if (_bridgeWin != null) Destroy(_bridgeWin.gameObject);
            UITooltip.HideTooltip();
            _bridgeShown = signature;
            BridgeOptions o = _bridgeOpts;

            _bridgeWin = GameUi.Window("BuildOrdersBridge", new Vector2(BridgeW, 600f));
            // at the right-hand side of the screen: the bridge stays in view
            _bridgeWin.anchorMin = _bridgeWin.anchorMax = new Vector2(1f, 0.5f);
            _bridgeWin.pivot = new Vector2(1f, 0.5f);
            _bridgeWin.anchoredPosition = new Vector2(-30f, 0f);
            Transform w = _bridgeWin;

            GameUi.Label(w, T("buildorders_bridge"), new Vector2(BridgePad, -14f), new Vector2(200f, 44f), 34f, GameUi.Warm, GameUi.Norse);
            TMP_Text summary = GameUi.Line(w, d != null ? T("buildorders_bridge_summary", d.Length.ToString("0"), d.Entries.Count) : "", new Vector2(-BridgePad, -26f), 260f, 17f, GameUi.Text,
                                           null, TextAlignmentOptions.TopRight, new Vector2(1f, 1f));
            GameUi.Braid(w, new Vector2(0f, -64f), BridgeW - BridgePad * 2f);

            float y = 80f;
            void Choice<T>(string label, T value, T[] values, string[] names, Action<T> set)
            {
                GameUi.Line(w, label, new Vector2(BridgePad, -y - 7f), 96f, 17f, GameUi.Text);
                float x0 = BridgePad + 100f, bw = (BridgeW - BridgePad - x0 - (values.Length - 1) * 6f) / values.Length;
                for (int i = 0; i < values.Length; i++)
                {
                    T choice = values[i];
                    bool on = EqualityComparer<T>.Default.Equals(value, choice);
                    GameUi.Tab(w, names[i], new Vector2(x0 + i * (bw + 6f), -y), new Vector2(bw, 34f), on, () => _bridgePending = () => { set(choice); _bridgeDirty = true; }, 15f);
                }
                y += 40f;
            }
            string Game(string key) => Localization.instance.Localize(key);   // the game's own word
            Choice(T("buildorders_bridge_width"), o.Width, new[] { 2, 4, 6 }, new[] { T("buildorders_m", 2), T("buildorders_m", 4), T("buildorders_m", 6) }, v => o.Width = v);
            Choice(T("buildorders_bridge_material"), o.Material, new[] { BridgeMaterial.Wood, BridgeMaterial.CoreWood, BridgeMaterial.Darkwood, BridgeMaterial.Stone },
                new[] { Game("$item_wood"), Game("$item_roundlog"), T("buildorders_bridge_darkwood"), Game("$item_stone") }, v => o.Material = v);
            Choice(T("buildorders_bridge_sides"), o.Sides, new[] { BridgeSides.Rails, BridgeSides.HalfWalls, BridgeSides.None }, new[] { T("buildorders_bridge_handrails"), T("buildorders_bridge_halfwalls"), T("buildorders_bridge_none") }, v => o.Sides = v);
            Choice(T("buildorders_bridge_roof"), o.Roof, new[] { false, true }, new[] { T("buildorders_bridge_open"), T("buildorders_bridge_covered") }, v => o.Roof = v);
            if (o.Material != BridgeMaterial.Stone)
                Choice(T("buildorders_bridge_supports"), o.Supports, new[] { BridgeSupports.Auto, BridgeSupports.Every2m, BridgeSupports.Every4m }, new[] { T("buildorders_bridge_auto"), T("buildorders_bridge_every", 2), T("buildorders_bridge_every", 4) }, v => o.Supports = v);
            Choice(T("buildorders_bridge_shape"), o.Shape, new[] { BridgeShape.Straight, BridgeShape.Arched }, new[] { T("buildorders_bridge_straight"), T("buildorders_bridge_arched") }, v => o.Shape = v);
            Choice(T("buildorders_bridge_ends"), o.Ends, new[] { BridgeEnds.Sloped, BridgeEnds.Steps }, new[] { T("buildorders_bridge_sloped"), T("buildorders_bridge_steps") }, v => o.Ends = v);
            Choice(T("buildorders_bridge_torches"), o.Torches, new[] { false, true }, new[] { T("buildorders_bridge_none"), T("buildorders_bridge_every", 8) }, v => o.Torches = v);

            // what it costs, against what you have (and the chests nearby): icons with the game's tooltips
            if (d != null)
            {
                y += 6f;
                GameUi.Line(w, T("buildorders_bridge_needs"), new Vector2(BridgePad, -y), BridgeW - BridgePad * 2f, 16f, GameUi.Dim);
                y += 26f;
                float x = BridgePad;
                for (int i = 0; i < needs.Count; i++)
                {
                    string amount = $"{have[i]}/{needs[i].Value}";
                    float guess = 26f + 3f + GameUi.Width(summary, amount) * 15f / 17f + 14f;   // (the summary line measures text: it is 17 pt, the amounts 15)
                    if (x > BridgePad && x + guess > BridgeW - BridgePad) { x = BridgePad; y += 32f; }
                    x += Cost(w, new Vector2(x, -y), needs[i].Key, amount, have[i] >= needs[i].Value ? GameUi.Good : GameUi.Warn, 26f, 15f);
                }
                y += 34f;
                foreach (string warn in d.Warnings.Select(x => x.Shown))
                {
                    TMP_Text t = GameUi.Label(w, warn, new Vector2(BridgePad, -y), new Vector2(BridgeW - BridgePad * 2f, 24f), 15f, GameUi.Warn);
                    y += t.GetPreferredValues(warn, BridgeW - BridgePad * 2f, 0f).y + 4f;
                }
            }

            // confirm, aim the end again, or drop it
            y += 10f;
            float bw3 = (BridgeW - BridgePad * 2f - 16f) / 3f;
            var confirm = GameUi.Button(w, T("buildorders_bridge_confirm"), new Vector2(BridgePad, -y), new Vector2(bw3, 42f), () => _bridgePending = () => ConfirmBridge(player), d != null);
            if (d != null) GameUi.Label(confirm).color = GameUi.Gold;
            GameUi.Button(w, T("buildorders_bridge_change_end"), new Vector2(BridgePad + bw3 + 8f, -y), new Vector2(bw3, 42f), () => _bridgePending = () => { BridgeOptionsOpen = false; _bridgeEndFixed = false; });
            var cancel = GameUi.Button(w, Localization.instance.Localize("$menu_cancel"), new Vector2(BridgePad + (bw3 + 8f) * 2f, -y), new Vector2(bw3, 42f), () => _bridgePending = () =>
            {
                CancelBridge();
                player.Message(MessageHud.MessageType.TopLeft, T("buildorders_bridge_cancelled"));
            });
            GameUi.Label(cancel).color = GameUi.Warn;
            y += 42f + BridgePad;

            _bridgeWin.sizeDelta = new Vector2(BridgeW, y);
        }
    }
}
