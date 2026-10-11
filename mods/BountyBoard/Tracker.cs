using UnityEngine;
using static DHack.Shared.Translations;

namespace BountyBoard
{
    /// <summary>
    /// The group's running contracts on your screen, part of the game's HUD (GameUi.cs): what they are and how far along, so you can hunt
    /// with them in mind. It hides with the HUD and while a menu or the map is open, and is built again only when something changed.
    /// </summary>
    internal static class Tracker
    {
        private const string Name = "BountyTracker";
        private const float W = 300f, Pad = 12f;

        private static RectTransform _panel;
        private static string _shown = "";
        private static float _nextCheck;

        internal static void Tick()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.3f;

            Plugin plugin = Plugin.Instance;
            State state = plugin?.Current;
            Player player = Player.m_localPlayer;
            bool show = Plugin.ShowTracker.Value && state != null && player != null && !Window.IsOpen && GameUi.HudRoot != null && GameUi.Ready() &&
                        !InventoryGui.IsVisible() && !Menu.IsVisible() && !Minimap.IsOpen() && !StoreGui.IsVisible() &&
                        (state.Active.Count > 0 || plugin.ToClaim() > 0);
            if (!show) { Destroy(); return; }

            string signature = Localization.instance.GetSelectedLanguage() + "," + Plugin.TrackerX.Value + "," + Plugin.TrackerY.Value + "," + Plugin.TrackerScale.Value + "|" + plugin.ToClaim();
            foreach (Bounty b in state.Active)
                signature += "|" + b.Id + "," + b.Progress + "," + (b.Kind == Kind.Gather ? player.GetInventory().CountItems(Rules.ItemName(b.Target)) : 0);
            if (_panel != null && signature == _shown) return;
            Build(state, plugin, player);
            _shown = signature;
        }

        internal static void Destroy()
        {
            if (_panel != null) Object.Destroy(_panel.gameObject);
            _panel = null; _shown = "";
        }

        private static void Build(State state, Plugin plugin, Player player)
        {
            Destroy();
            Transform hud = GameUi.HudRoot;
            // a hot reload leaves the old copy's panel behind (its types differ from ours): find it by name
            foreach (Transform old in hud) if (old.name == Name) Object.Destroy(old.gameObject);

            int toClaim = plugin.ToClaim();
            float rowH = 50f, h = Pad * 2f + 30f + state.Active.Count * rowH + (toClaim > 0 ? 26f : 0f);
            _panel = GameUi.Box(hud, new Vector2(Plugin.TrackerX.Value, -Plugin.TrackerY.Value), new Vector2(W, h));
            _panel.name = Name;
            _panel.localScale = Vector3.one * Plugin.TrackerScale.Value;

            GameUi.Label(_panel, T("bountyboard_tracker_title"), new Vector2(Pad, -Pad + 2f), new Vector2(W - Pad * 2f, 28f), 20f, GameUi.Warm, GameUi.Norse);
            float y = Pad + 30f;
            foreach (Bounty b in state.Active)
            {
                bool gather = b.Kind == Kind.Gather;
                int have = gather ? player.GetInventory().CountItems(Rules.ItemName(b.Target)) : 0;
                int shown = Mathf.Min(b.Count, b.Progress);
                TMPro.TMP_Text line = GameUi.Label(_panel, Rules.Describe(b) + (gather && have > 0 ? "  <color=#bdb7a9>" + T("bountyboard_you_have", have) + "</color>" : ""),
                    new Vector2(Pad, -y), new Vector2(W - Pad * 2f, 24f), 16f, GameUi.Text);
                line.enableWordWrapping = false;
                line.overflowMode = TMPro.TextOverflowModes.Ellipsis;
                GameUi.Bar(_panel, new Vector2(Pad, -y - 24f), new Vector2(W - Pad * 2f, 18f), shown / (float)Mathf.Max(1, b.Count),
                    shown >= b.Count ? GameUi.Good : GameUi.Gold, shown + "/" + b.Count, 14f);
                y += rowH;
            }
            if (toClaim > 0)
            {
                string text = T(toClaim == 1 ? "bountyboard_to_collect" : "bountyboard_to_collect_many", toClaim);
                TMPro.TMP_Text line = GameUi.Label(_panel, text, new Vector2(Pad, -y), new Vector2(W - Pad * 2f, 24f), 16f, GameUi.Good);
                float extra = line.GetPreferredValues(text, W - Pad * 2f, 0f).y - 24f;   // (a longer language's words may take two lines)
                if (extra > 0f) _panel.sizeDelta += new Vector2(0f, extra);
            }
        }
    }
}
