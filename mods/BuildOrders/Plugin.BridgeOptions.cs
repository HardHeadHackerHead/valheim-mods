using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// The Bridge options panel: opens when the end of a bridge is set. The ghost stays where it is and changes as you change the settings
    /// (width, material, rails, roof, supports, shape, torches); Confirm places the plan, Change end lets you aim the end again, Cancel drops it.
    /// The choices are kept for the next bridge.
    /// </summary>
    public partial class Plugin
    {
        internal static bool BridgeOptionsOpen;
        private BridgeOptions _bridgeOpts;
        private Rect _bridgeRect;
        private bool _bridgeRectPlaced;

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
            player.Message(MessageHud.MessageType.TopLeft, $"Bridge planned: {d.Length:0} m. Build it with E (hold E for everything near you)");
        }

        private void DrawBridgeOptions()
        {
            if (!BridgeOptionsOpen || _bridgeOpts == null) return;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
            EnsureWindowStyles();
            Matrix4x4 saved = GUI.matrix;
            float s = Mathf.Max(0.8f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float sw = Screen.width / s, sh = Screen.height / s;
            float w = 470f, h = Mathf.Min(760f, sh - 60f);
            if (!_bridgeRectPlaced) { _bridgeRect = new Rect(sw - w - 30f, (sh - h) / 2f, w, h); _bridgeRectPlaced = true; }   // at the side: the bridge stays in view
            _bridgeRect.width = w; _bridgeRect.height = h;
            _bridgeRect = GUI.Window(8872, _bridgeRect, BridgeOptionsContents, GUIContent.none, GUIStyle.none);
            _bridgeRect.x = Mathf.Clamp(_bridgeRect.x, 0f, Mathf.Max(0f, sw - w));
            _bridgeRect.y = Mathf.Clamp(_bridgeRect.y, 0f, Mathf.Max(0f, sh - h));
            GUI.matrix = saved;
        }

        private void BridgeOptionsContents(int id)
        {
            float w = _bridgeRect.width, h = _bridgeRect.height;
            Round(new Rect(0, 0, w, h), new Color(0.05f, 0.07f, 0.08f, 0.97f), 10f);
            Round(new Rect(0, 0, w, h), new Color(0.05f, 0.07f, 0.08f, 0.9f), 10f);
            Outline(new Rect(0, 0, w, h), new Color(PreviewColor.r, PreviewColor.g, PreviewColor.b, 0.85f), 10f);
            Player player = Player.m_localPlayer;
            if (player == null) return;
            BridgeOptions o = _bridgeOpts;
            BridgeDesign d = _bridgeDesign;

            GUILayout.BeginArea(new Rect(16f, 12f, w - 32f, h - 24f));
            GUILayout.Label("Bridge", _wTitle);
            GUILayout.Label(d != null ? $"{d.Length:0} m long   ·   {d.Entries.Count} pieces" : "", _wText);
            GUILayout.Space(6);

            bool Choice<T>(string label, ref T value, T[] values, string[] names)
            {
                GUILayout.Label(label, _wBold);
                GUILayout.BeginHorizontal();
                bool changed = false;
                for (int i = 0; i < values.Length; i++)
                {
                    bool on = EqualityComparer<T>.Default.Equals(value, values[i]);
                    if (GUILayout.Button(names[i], on ? _wButtonOn : _wButton, GUILayout.Height(28)) && !on) { value = values[i]; changed = true; }
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
                return changed;
            }

            bool dirty = false;
            int width = o.Width; BridgeMaterial mat = o.Material; BridgeSides sides = o.Sides; BridgeSupports sup = o.Supports; BridgeShape shape = o.Shape; BridgeEnds ends = o.Ends;
            bool roof = o.Roof, torches = o.Torches;
            dirty |= Choice("Width", ref width, new[] { 2, 4, 6 }, new[] { "2 m", "4 m", "6 m" });
            dirty |= Choice("Material", ref mat, new[] { BridgeMaterial.Wood, BridgeMaterial.CoreWood, BridgeMaterial.Darkwood, BridgeMaterial.Stone }, new[] { "Wood", "Core wood", "Darkwood", "Stone" });
            dirty |= Choice("Sides", ref sides, new[] { BridgeSides.Rails, BridgeSides.HalfWalls, BridgeSides.None }, new[] { "Handrails", "Half walls", "None" });
            dirty |= Choice("Roof", ref roof, new[] { false, true }, new[] { "Open", "Covered" });
            if (mat != BridgeMaterial.Stone)
                dirty |= Choice("Supports", ref sup, new[] { BridgeSupports.Auto, BridgeSupports.Every2m, BridgeSupports.Every4m }, new[] { "Auto", "Every 2 m", "Every 4 m" });
            dirty |= Choice("Shape", ref shape, new[] { BridgeShape.Straight, BridgeShape.Arched }, new[] { "Straight", "Arched" });
            dirty |= Choice("Ends", ref ends, new[] { BridgeEnds.Sloped, BridgeEnds.Steps }, new[] { "Sloped deck", "Level + steps" });
            dirty |= Choice("Torches", ref torches, new[] { false, true }, new[] { "None", "Every 8 m" });
            if (dirty)
            {
                o.Width = width; o.Material = mat; o.Sides = sides; o.Roof = roof; o.Supports = sup; o.Shape = shape; o.Ends = ends; o.Torches = torches;
                _bridgeDirty = true;
            }

            // what it costs, against what you carry (and the chests nearby)
            if (d != null)
            {
                var needs = MaterialsOf(d.Entries.Select(e => e.Prefab));
                ForceBuildContext(true);
                string cost;
                try
                {
                    Inventory inv = player.GetInventory();
                    cost = string.Join("   ", needs.Select(kv =>
                    {
                        int have = inv.CountItems(kv.Key);
                        return $"<color={(have >= kv.Value ? "#8fe08f" : "#ffb38a")}>{Localization.instance.Localize(kv.Key)} {have}/{kv.Value}</color>";
                    }).ToArray());
                }
                finally { ForceBuildContext(false); }
                GUILayout.Label("Needs (you have / it takes):", _wBold);
                GUILayout.Label(cost, new GUIStyle(_wText) { wordWrap = true });
                foreach (string warn in d.Warnings) GUILayout.Label("<color=#ffb070>" + warn + "</color>", new GUIStyle(_wDim) { wordWrap = true });
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Confirm", _wButtonGood, GUILayout.Height(34))) ConfirmBridge(player);
            if (GUILayout.Button("Change end", _wButton, GUILayout.Height(34))) { BridgeOptionsOpen = false; _bridgeEndFixed = false; }
            if (GUILayout.Button("Cancel", _wButtonBad, GUILayout.Height(34))) { CancelBridge(); player.Message(MessageHud.MessageType.TopLeft, "Bridge cancelled"); }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, w, 40));
        }
    }
}
