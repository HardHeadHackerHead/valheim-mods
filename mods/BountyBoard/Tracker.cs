using System.Linq;
using UnityEngine;

namespace BountyBoard
{
    /// <summary>The group's running contracts on your screen: what they are and how far along, so you can hunt with them in mind.</summary>
    internal static class Tracker
    {
        internal static void Draw()
        {
            if (!Plugin.ShowTracker.Value || Event.current.type != EventType.Repaint) return;
            Plugin plugin = Plugin.Instance;
            State state = plugin?.Current;
            Player player = Player.m_localPlayer;
            if (state == null || player == null || Window.IsOpen) return;
            if (InventoryGui.IsVisible() || Menu.IsVisible() || Minimap.IsOpen() || StoreGui.IsVisible()) return;
            int toClaim = plugin.ToClaim();
            if (state.Active.Count == 0 && toClaim == 0) return;
            Styles.Ensure();

            float scale = Plugin.TrackerScale.Value * Mathf.Max(0.8f, Screen.height / 1080f);
            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            const float w = 290f, rowHeight = 46f, pad = 10f;
            float h = pad * 2f + 24f + state.Active.Count * rowHeight + (toClaim > 0 ? 22f : 0f);
            var panel = new Rect(Plugin.TrackerX.Value / scale, Plugin.TrackerY.Value / scale, w, h);
            Styles.Rounded(new Rect(panel.x + 2f, panel.y + 3f, w, h), new Color(0f, 0f, 0f, 0.3f), 7f);
            Styles.Rounded(panel, new Color(0.07f, 0.06f, 0.05f, 0.82f), 7f);
            Styles.Outline(panel, new Color(0.62f, 0.47f, 0.22f, 0.9f), 7f);

            float y = panel.y + pad;
            GUI.Label(new Rect(panel.x + pad, y, w - pad * 2f, 20f), "Group contracts", Small(true));
            y += 24f;

            foreach (Bounty b in state.Active)
            {
                bool gather = b.Kind == Kind.Gather;
                int have = gather ? player.GetInventory().CountItems(Rules.ItemName(b.Target)) : 0;
                int shown = Mathf.Min(b.Count, b.Progress);
                GUI.Label(new Rect(panel.x + pad, y, w - pad * 2f, 20f), Rules.Describe(b) + (gather && have > 0 ? $"  (you have {have})" : ""), Small(false));

                var bar = new Rect(panel.x + pad, y + 21f, w - pad * 2f, 18f);
                Styles.Rounded(bar, new Color(0.05f, 0.04f, 0.03f, 1f), 5f);
                float frac = Mathf.Clamp01(shown / (float)Mathf.Max(1, b.Count));
                if (frac > 0f) Styles.Rounded(new Rect(bar.x, bar.y, bar.width * frac, bar.height), new Color(0.78f, 0.58f, 0.22f), 5f);
                Centered(bar, shown + "/" + b.Count);
                y += rowHeight;
            }

            if (toClaim > 0)
            {
                var good = new GUIStyle(Small(false)) { fontStyle = FontStyle.Bold };
                good.normal.textColor = new Color(0.6f, 0.9f, 0.6f);
                GUI.Label(new Rect(panel.x + pad, y, w - pad * 2f, 20f), toClaim + " reward" + (toClaim == 1 ? "" : "s") + " to collect at a board", good);
            }
            GUI.matrix = saved;
        }

        private static GUIStyle _small, _header, _center;

        /// <summary>The "3/6" in the middle of a bar: big enough to read, with a dark edge so it shows on the gold fill as well as the dark part.</summary>
        private static void Centered(Rect bar, string text)
        {
            if (_center == null)
            {
                _center = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow, font = Styles.GameFont(false) };
                _center.normal.textColor = Color.white;
            }
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.9f);
            foreach (Vector2 d in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
                GUI.Label(new Rect(bar.x + d.x, bar.y + d.y, bar.width, bar.height), text, _center);
            GUI.color = old;
            GUI.Label(bar, text, _center);
        }

        private static GUIStyle Small(bool header, bool centered = false, int size = 12)
        {
            if (centered) { var c = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, font = Styles.GameFont(false) }; c.normal.textColor = Color.white; return c; }
            if (_small == null)
            {
                _small = new GUIStyle(GUI.skin.label) { fontSize = 14, clipping = TextClipping.Clip, font = Styles.GameFont(false) };
                _small.normal.textColor = new Color(0.95f, 0.91f, 0.84f);
                _header = new GUIStyle(_small) { fontSize = 15, fontStyle = FontStyle.Bold };
                _header.normal.textColor = new Color(0.95f, 0.78f, 0.35f);
            }
            return header ? _header : _small;
        }
    }
}
